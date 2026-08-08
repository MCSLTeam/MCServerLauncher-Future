# Startup Plugin Developer Guide

## SDK model

Plugin API 1.0 plugins reference only the SDK package:

    <PackageReference Include="MCServerLauncher.Daemon.Plugin.Sdk" Version="1.0.0" />

The SDK carries the exact Daemon API/Common dependency chain, the source generator, and
the buildTransitive publish targets. Do not add a direct Daemon API package reference.
Do not implement IDaemonPlugin; the SDK generates that adapter and treats a handwritten
adapter as a diagnostic error.

Plugins are trusted in-process code. Features are admission and audit boundaries, not a
sandbox. Hooks, hot unload, factory/store/control/filesystem capabilities, and broad
service registries remain outside this milestone; direct plugin-to-plugin calls are
limited to explicitly declared provider dependencies and shared Contracts assemblies.

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
        "api": "[1.0.0,2.0.0)",
        "features": ["rpc.register"]
      }
    }

The package id is lowercase and dot-separated. It owns the plugin.<id>. protocol
namespace. The host validates the generated metadata and manifest digest before loading
plugin IL. A changed digest, unknown feature, missing grant, invalid range, dependency
cycle, missing dependency, or catalog conflict skips the affected bundle atomically.

A Phase 7 manifest may add an optional versioned dependency section. It is additive;
old manifests remain valid and `package`, `entry`, `requires.api`, and
`requires.features` keep their Preview-2 meaning. `dependencies.plugins` declares
startup-order/provider dependencies; `dependencies.contracts` declares shared typed
Contracts assemblies by file name, NuGet-style version range, and SHA-256 fingerprint.

    {
      "dependencies": {
        "version": 1,
        "plugins": [
          { "id": "community.example.provider", "version": "[1.0.0,2.0.0)" }
        ],
        "contracts": [
          {
            "assembly": "Example.Contracts.dll",
            "version": "[1.0.0,2.0.0)",
            "sha256": "<64 lowercase hex characters>"
          }
        ]
      }
    }

Plugin dependencies are evaluated after discovery and admission preflight but before
entry assemblies are loaded. Providers start before consumers, consumers stop before
providers, and a missing, incompatible, skipped, cyclic, or failed provider skips the
affected consumer set. Declared Contracts assemblies are loaded once into a shared
contract load context and resolved into participating plugin ALCs; private copies that
conflict with declared shared contracts are rejected.

Generated feature bags expose `Providers` and `Imports` for direct typed plugin-to-plugin
calls. Providers export interface implementations with `features.Providers.Export<T>()`;
consumers import them with `features.Imports.Import<T>("provider.plugin.id")`. The
contract interface type must come from a declared Contracts assembly, and imports require
a matching `dependencies.plugins` provider declaration. The host does not expose a root
service provider or RPC fallback for plugin-to-plugin calls.

Plugins that declare `event.subscribe` get `features.Subscriptions`. Subscriptions accept
built-in typed event descriptors such as `BuiltInProtocolDefinitions.InstanceLog`, deliver
`DaemonEventField<TMeta>` / `DaemonEventField<TData>` values with the same missing/null/value
semantics as remote events, and are cleaned up automatically when the plugin stops or fails.

Preview-1 grantable features are system.query, instance.query, instance.manage,
operation.query, operation.cancel, provisioning.manage, network.http.listen,
auth.verify, and storage.private. Preview-2/Phase-7 also includes file.read,
file.write, event-rule.manage, backup.manage, monitoring.query, audit.query,
automation.manage, and event.subscribe. The host also provides rpc.register and
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

StartAsync is bounded by the daemon plugin startup timeout. Dependency providers are
started before their consumers; if a provider fails configure/start or is skipped, the
consumer fails before its own StartAsync is invoked. On failure or timeout, host
registrations, events, and future HTTP admissions are revoked while cleanup is supervised
without making /api/v2 unavailable. StopAsync releases plugin-owned resources; the host
cancels the lifetime token first and stops successful plugins in reverse dependency/start
order.

## Publish

    dotnet build Example.Plugin/Example.Plugin.csproj -c Release
    dotnet publish Example.Plugin/Example.Plugin.csproj -c Release -p:MCSLPluginBundle=true -o artifacts/plugins/community.example.health

MCSLPluginBundle=true removes host-provided shared assemblies from the bundle. Deploy the
published entry DLL, mcsl-plugin.json, optional config.json, and private dependencies
under plugins/community.example.health/ beside the daemon. The operator must then opt the
plugin in through the daemon config.json (`plugins.entries.community.example.health.enabled: true`);
a plugin id absent from `plugins.entries` stays disabled. Do not bundle the daemon,
TouchSocket, MessagePipe, Serilog, MCServerLauncher.Daemon.API.dll,
MCServerLauncher.Common.dll, or MCServerLauncher.Daemon.Plugin.Sdk.dll.

The accepted Preview-2 versions, internal Release assets, and payload hashes are
recorded in docs/preview2-package-pin.md. Public distribution remains gated there.
