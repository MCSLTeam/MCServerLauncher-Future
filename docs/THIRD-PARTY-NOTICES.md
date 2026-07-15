# Third-Party Notices And License Inventory

MCServerLauncher Future is GPL-3.0-only. The following direct runtime and SDK dependencies are kept in their own license terms; source and package notices remain with the corresponding package.

| Package | Version | License | Scope | Upstream |
|---|---:|---|---|---|
| TouchSocket / TouchSocket.Http / TouchSocket.Core.DependencyInjection | 4.2.17 | MIT | daemon/client WebSocket transport | <https://github.com/RRQM/TouchSocket> |
| MessagePipe | 1.8.2 | MIT | daemon-internal event fan-out | <https://github.com/Cysharp/MessagePipe> |
| Serilog and Serilog.Extensions.Logging | 4.3.0 / 8.0.0 | Apache-2.0 | daemon/WPF logging | <https://github.com/serilog/serilog> |
| Serilog.Sinks.Async / Console / File | 2.1.0 / 6.0.0 / 6.0.0 | Apache-2.0 | logging sinks | <https://github.com/serilog> |
| Microsoft.Extensions.* | 9.0.2-10.0.9 | MIT | dependency injection, logging, object pooling | <https://github.com/dotnet/runtime> |
| RustyOptions | 0.10.1 | MIT | public `Result` contracts | <https://github.com/RRQM/RustyOptions> |
| NuGet.Versioning | 7.6.0 | MIT | plugin API range validation | <https://github.com/NuGet/NuGet.Client> |
| Brigadier.NET | 1.2.13 | MIT | daemon console command parsing | <https://github.com/RRQM/Brigadier.NET> |
| Downloader | 3.1.2 | MIT | WPF and installer downloads | <https://github.com/bezzad/Downloader> |
| System.IdentityModel.Tokens.Jwt | 8.9.0 | MIT | daemon authentication tokens | <https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet> |
| BenchmarkDotNet | 0.15.8 | MIT | benchmark project only | <https://github.com/dotnet/BenchmarkDotNet> |

Test-only dependencies (xUnit, Microsoft.NET.Test.Sdk, and coverlet) are used only by test projects and are not part of the Daemon API or daemon runtime package. WPF-only UI dependencies are likewise not part of the daemon API package.

The authoritative package-specific notices are available in NuGet package metadata and the upstream repositories. This inventory is reviewed when a direct runtime dependency or version changes.
