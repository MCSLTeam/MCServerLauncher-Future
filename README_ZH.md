# MCServerLauncher Future

MCServerLauncher Future 用守护进程管理 Minecraft 服务器和其他控制台程序。本仓库包含 .NET 守护进程、WPF 客户端连接层、共享协议契约、可打包的 Daemon API 以及协议测试。

[![GPLv3](https://img.shields.io/badge/License-GPLv3-blue)](LICENSE)

## 架构

- 守护进程只提供一个经过认证的 `/api/v2` WebSocket 端点，使用类型化 JSON-RPC 和版本化二进制传输帧。
- `src/MCServerLauncher.Daemon.API` 是传输无关的 NuGet 边界，包含应用、协议、状态、错误和启动插件契约。
- `src/MCServerLauncher.DaemonClient` 实现远程应用和类型化事件 API。
- `src/MCServerLauncher.WPF` 是 Windows 桌面客户端，只通过 daemon client 连接层访问守护进程。
- 启动插件是受信任且只在启动阶段运行的 sidecar。插件可以注册类型化 RPC、发布类型化事件、读取不可变实例快照，但不能引用 TouchSocket、MessagePipe、Serilog 或守护进程实现类型。

启用插件的守护进程是未裁剪的 JIT single-file 主机，并通过 sidecar 加载插件。产品不支持 Native AOT 或 `PublishTrimmed=true`。

## 构建和测试

项目目标框架为 .NET 10：

```powershell
dotnet build MCServerLauncher.sln /m:1
dotnet test tests/MCServerLauncher.ProtocolTests/MCServerLauncher.ProtocolTests.csproj -c Release /m:1
dotnet test tests/MCServerLauncher.Daemon.ApiTests/MCServerLauncher.Daemon.ApiTests.csproj -c Release /m:1
```

运行守护进程或 WPF 客户端：

```powershell
dotnet run --project src/MCServerLauncher.Daemon/MCServerLauncher.Daemon.csproj
dotnet run --project src/MCServerLauncher.WPF/MCServerLauncher.WPF.csproj
```

## 插件 SDK

请阅读[插件开发指南](docs/plugin-developer-guide.md)，了解 manifest、capability、生命周期和 sidecar 发布布局。打包公开 API：

```powershell
dotnet pack src/MCServerLauncher.Daemon.API/MCServerLauncher.Daemon.API.csproj -c Release -o artifacts/packages
```

## 发布文档

- [守护进程手册](docs/daemon-manual.md)
- [第三方声明和许可证清单](docs/THIRD-PARTY-NOTICES.md)
- [发布工作流说明](Release.md)
- [变更日志](CHANGELOG.md)

WPF 客户端需要 .NET Desktop Runtime 10.x。框架依赖的守护进程包需要 .NET Runtime 10.x；self-contained 包会包含运行时。

## 许可证

MCServerLauncher Future 使用 [GNU General Public License v3.0](LICENSE) 发布。第三方包信息记录在 [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md)。
