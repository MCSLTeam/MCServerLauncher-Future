# MCServerLauncher.Daemon.Plugin.Sdk

Developer SDK for MCServerLauncher Future plugins.

## Install

```xml
<PackageReference Include="MCServerLauncher.Daemon.Plugin.Sdk" Version="2.0.0-preview.2" />
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

## Publish

```powershell
dotnet publish -c Release -p:MCSLPluginBundle=true -o artifacts/plugins/your.plugin
```

The SDK's `buildTransitive` targets wire `mcsl-plugin.json` as an analyzer additional file and filter shared host assemblies out of the plugin publish bundle.
