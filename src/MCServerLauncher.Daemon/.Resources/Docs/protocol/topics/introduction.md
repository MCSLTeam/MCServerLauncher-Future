# Daemon V2 协议

MCServerLauncher Future daemon 只公开 `/api/v2` WebSocket endpoint。控制面使用 JSON-RPC 2.0，事件使用 JSON-RPC notification，文件内容使用带版本头的 binary frame。

协议的唯一机器可读来源是启动后冻结的 typed catalog。连接成功后调用 `rpc.discover` 可取得当前 runtime OpenRPC 文档；内置协议的 checked-in Apifox 文档由同一 catalog 生成。

共享 wire contracts 位于 `MCServerLauncher.Common`，应用接口位于 `MCServerLauncher.Daemon.API`。协议不提供旧 envelope、兼容 endpoint 或 fallback parser。

相关主题：[连接](connection.md)、[RPC](rpc.md)、[事件](events.md)、[文件传输](file-transfer.md)、[权限](permissions.md)。
