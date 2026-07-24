# Startup Plugin Developer Guide

## SDK model

Plugin API 2.0 plugins reference only the SDK package:

    <PackageReference Include="MCServerLauncher.Daemon.Plugin.Sdk" Version="2.0.0-preview.2" />

The SDK carries the exact Daemon API/Common dependency chain, the source generator, and
the buildTransitive publish targets. Do not add a direct Daemon API package reference.
Do not implement IDaemonPlugin; the SDK generates that adapter and treats a handwritten
adapter as a diagnostic error.

Plugins are trusted in-process code. Features are admission and audit boundaries, not a
sandbox. Hooks, hot unload, factory/store/control/filesystem capabilities, and
plugin-to-plugin services remain outside this milestone.

## Manifest

Add one mcsl-plugin.json next to the project file. The SDK passes it to the generator
and copies it beside the published entry assembly.

    {
      "package": {
        "id": "community.example.health",
        "version": "1.0.0"
      },
      "entry": {
        "assembly": "Example.Plugin.dll",
        "type": "Example.Plugin.Generated.DaemonPluginAdapter"
      },
      "requires": {
        "api": "[2.0.0,3.0.0)",
        "features": ["rpc.register"]
      }
    }

The package id is lowercase and dot-separated. It owns the plugin.<id>. protocol
namespace. The host validates the generated metadata and manifest digest before loading
plugin IL. A changed digest, unknown feature, missing grant, invalid range, or catalog
conflict skips the whole bundle atomically.

Preview-1 grantable features are system.query, instance.query, instance.manage,
operation.query, operation.cancel, provisioning.manage, network.http.listen,
auth.verify, and storage.private. The host also provides rpc.register and
event.publish for generated modules. Every listed feature is required.

## Module

Write a partial module. The generator creates the adapter, the feature bag, private DI
registration, metadata, and authorized application facades.

    using MCServerLauncher.Common.Contracts.Protocol;
    using MCServerLauncher.Common.Contracts.Serialization;
    using MCServerLauncher.Daemon.API.Errors;
    using MCServerLauncher.Daemon.API.Protocol;
    using MCServerLauncher.Daemon.Plugin.Sdk;
    using Microsoft.Extensions.DependencyInjection;
    using RustyOptions;

    namespace Example.Plugin;

    [DaemonPluginModule]
    public partial class HealthPlugin
    {
        public void ConfigureServices(IServiceCollection services, HealthPluginFeatures features)
        {
            var registration = features.Rpc.Register(
                "ping",
                BuiltInProtocolJsonContext.Default.EmptyRequest,
                BuiltInProtocolJsonContext.Default.UnitResult,
                new RpcDocumentation(
                    "community.example.health",
                    "Health ping",
                    "Checks that the plugin is active.",
                    "example.empty-request",
                    "example.unit-result"),
                static (_, _) => Task.FromResult(PluginResult.Ok<UnitResult>(new UnitResult())));
            if (registration.IsErr(out var error))
                throw new InvalidOperationException(error!.Message);
        }

        public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PluginResult.Ok());

        public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PluginResult.Ok());
    }

HealthPluginFeatures exposes only the surfaces declared by requires.features.
Use features.ForPrincipal(principal) for user-originated application calls; it returns
permission-checked facades. MCP tools must not fall back to the host principal.

The optional same-directory config.json is cold-read once at daemon startup through the
generated configuration service. There is no manifest configuration field and no plugin
reload path.

## Lifecycle

ConfigureServices records registrations and private services; it must not start I/O or
background work. The host validates every draft globally, starts admitted plugins before
opening /api/v2, then activates successful catalog contributions. A plugin that starts
background work waits for activation before publishing events.

StartAsync is bounded by the daemon plugin startup timeout. On failure or timeout, host
registrations, events, and future HTTP admissions are revoked while cleanup is supervised
without making /api/v2 unavailable. StopAsync releases plugin-owned resources; the host
cancels the lifetime token first and stops successful plugins in reverse order.

## Publish

    dotnet build Example.Plugin/Example.Plugin.csproj -c Release
    dotnet publish Example.Plugin/Example.Plugin.csproj -c Release -p:MCSLPluginBundle=true -o artifacts/plugins/community.example.health

MCSLPluginBundle=true removes host-provided shared assemblies from the bundle. Deploy the
published entry DLL, mcsl-plugin.json, optional config.json, and private dependencies
under plugins/community.example.health/ beside the daemon. Do not bundle the daemon,
TouchSocket, MessagePipe, Serilog, MCServerLauncher.Daemon.API.dll,
MCServerLauncher.Common.dll, or MCServerLauncher.Daemon.Plugin.Sdk.dll.

The accepted Preview-1 versions, public Release assets, and payload hashes are
recorded in docs/preview1-package-pin.md. MCP-0..5 must pin those exact packages.
