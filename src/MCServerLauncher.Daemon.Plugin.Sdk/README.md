# MCServerLauncher.Daemon.Plugin.Sdk

Developer SDK for MCServerLauncher Future plugins.

## Install

```xml
<PackageReference Include="MCServerLauncher.Daemon.Plugin.Sdk" Version="1.0.0" />
```

Place `mcsl-plugin.json` beside the project (or under the project directory) and mark a partial class with `[DaemonPluginModule]`.

## Module shape

```csharp
using MCServerLauncher.Daemon.Plugin.Sdk;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Errors;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;

[DaemonPluginModule]
public partial class HealthPlugin
{
    public void ConfigureServices(IServiceCollection services, HealthPluginFeatures features)
    {
        // features only exposes surfaces declared in mcsl-plugin.json
        // register plugin-owned services into the private container
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
        => Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
        => Task.FromResult(PluginResult.Ok());
}
```

Point the manifest entry type at the generated adapter:

```json
"entry": {
  "assembly": "Your.Plugin.dll",
  "type": "Your.Namespace.Generated.DaemonPluginAdapter"
}
```

Formal extension point declarations are optional but validated by both the generator and daemon loader:

```json
"extensionPoints": [
  {
    "id": "provider.java",
    "kind": "provider",
    "priority": 10
  }
]
```

Only catalogued extension point ids are legal. Private implementation hooks, reflection patching, Harmony patching, monkey patching, and root service replacement are not extension points.

## Extension Protocol events

Daemon C# plugins that drive client UI state should declare `event.publish` and `event.daemon`, then register the plugin-owned event descriptor with `PluginExtensionProtocol.CreateExtensionEvent(pluginId)`. Publish `PluginExtensionProtocol.StatePatch(...)`, `StateSnapshot(...)`, or `Event(...)` as the event data; the helper validates the plugin id, revision, object-shaped payload, and unsafe JSON keys before the envelope reaches Web/WPF clients.

```csharp
var descriptor = PluginExtensionProtocol.CreateExtensionEvent("community.example.status-panel");
var publisher = features.Events.Register(descriptor).Unwrap();
using var patch = JsonDocument.Parse("""{ "status": "ready" }""");
await publisher.PublishAsync(
    DaemonEventField<EmptyRequest>.Missing,
    DaemonEventField<ExtensionProtocolDispatchResult>.FromValue(
        PluginExtensionProtocol.StatePatch("community.example.status-panel", 1, patch.RootElement)),
    cancellationToken);
```

## Publish

```powershell
dotnet publish -c Release -p:MCSLPluginBundle=true -o artifacts/plugins/your.plugin
```

The SDK's `buildTransitive` targets wire `mcsl-plugin.json` as an analyzer additional file and filter shared host assemblies out of the plugin publish bundle.
