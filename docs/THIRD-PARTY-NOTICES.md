# Third-Party Notices And License Inventory

MCServerLauncher Future is GPL-3.0-only. The following direct runtime and SDK dependencies are kept in their own license terms; source and package notices remain with the corresponding package.

| Package | Version | License | Scope | Upstream |
|---|---:|---|---|---|
| TouchSocket / TouchSocket.Http / TouchSocket.Core.DependencyInjection | 4.2.17 | MIT | daemon/client WebSocket transport | <https://github.com/RRQM/TouchSocket> |
| MessagePipe | 1.8.2 | MIT | daemon-internal event fan-out | <https://github.com/Cysharp/MessagePipe> |
| Serilog and Serilog.Extensions.Logging | 4.3.0 / 8.0.0 | Apache-2.0 | daemon/WPF logging | <https://github.com/serilog/serilog> |
| Serilog.Sinks.Async | 2.1.0 | Apache-2.0 | daemon/WPF async logging sink | <https://github.com/serilog> |
| Serilog.Sinks.Console | 6.0.0 daemon / 6.1.1 WPF | Apache-2.0 | daemon/WPF console logging sink | <https://github.com/serilog> |
| Serilog.Sinks.File | 6.0.0 daemon / 7.0.0 WPF | Apache-2.0 | daemon/WPF file logging sink | <https://github.com/serilog> |
| Microsoft.Extensions.DependencyInjection | 9.0.4 | MIT | daemon/WPF dependency injection | <https://github.com/dotnet/runtime> |
| Microsoft.Extensions.Logging | 9.0.2 | MIT | daemon logging abstractions and integration | <https://github.com/dotnet/runtime> |
| Microsoft.Extensions.Logging.Abstractions | 10.0.9 | MIT | Daemon API public logging abstraction | <https://github.com/dotnet/runtime> |
| Microsoft.Extensions.ObjectPool | 10.0.0 | MIT | daemon object pooling | <https://github.com/dotnet/runtime> |
| Microsoft.Management.Infrastructure | 3.0.0 | MIT | daemon Windows system information | <https://github.com/microsoft/omi> |
| RustyOptions | 0.10.1 | MIT | public `Result` contracts | <https://github.com/RRQM/RustyOptions> |
| NuGet.Versioning | 7.6.0 | MIT | plugin API range validation | <https://github.com/NuGet/NuGet.Client> |
| Brigadier.NET | 1.2.13 | MIT | daemon console command parsing | <https://github.com/RRQM/Brigadier.NET> |
| Downloader | 3.1.2 daemon / 4.0.3 WPF | MIT | WPF and installer downloads | <https://github.com/bezzad/Downloader> |
| System.IdentityModel.Tokens.Jwt | 8.9.0 | MIT | daemon authentication tokens | <https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet> |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | WPF client MVVM helpers | <https://github.com/CommunityToolkit/dotnet> |
| AvalonEdit | 6.3.1.120 | MIT | WPF client editor | <https://github.com/icsharpcode/AvalonEdit> |
| iNKORE.UI.WPF.Emojis | 0.3.6.4 | MIT | WPF client emoji controls | <https://github.com/iNKORE-NET/UI.WPF.Emojis> |
| iNKORE.UI.WPF | 1.2.8 | MIT | WPF client controls | <https://github.com/iNKORE-NET/UI.WPF> |
| iNKORE.UI.WPF.Modern | 0.10.2.1 | MIT | WPF client controls | <https://github.com/iNKORE-NET/UI.WPF.Modern> |
| Microsoft.NETCore.Platforms | 7.0.4 | MIT | WPF platform assets | <https://github.com/dotnet/runtime> |
| Microsoft.Toolkit.Uwp.Notifications | 7.1.3 | MIT | WPF client notifications | <https://github.com/CommunityToolkit/WindowsCommunityToolkit> |
| Microsoft.Xaml.Behaviors.Wpf | 1.1.135 | MIT | WPF client behaviors | <https://github.com/microsoft/XamlBehaviorsWpf> |
| System.ComponentModel.Composition | 10.0.2 | MIT | WPF composition support | <https://github.com/dotnet/runtime> |
| System.Data.DataSetExtensions | 4.5.0 | MIT | WPF data extensions | <https://github.com/dotnet/runtime> |
| System.Drawing.Common | 10.0.2 | MIT | WPF test image fixtures | <https://github.com/dotnet/runtime> |
| BenchmarkDotNet | 0.15.8 | MIT | benchmark project only | <https://github.com/dotnet/BenchmarkDotNet> |

Test-only dependencies (xUnit, Microsoft.NET.Test.Sdk, coverlet, and the `System.Drawing.Common` row above) are used only by test projects and are not part of the Daemon API or daemon runtime package. The WPF rows above are direct WPF client dependencies and are intentionally outside the Daemon API package; their package-specific notices remain with each package.

The authoritative package-specific notices are available in NuGet package metadata and the upstream repositories. This inventory is reviewed when a direct runtime dependency or version changes.
