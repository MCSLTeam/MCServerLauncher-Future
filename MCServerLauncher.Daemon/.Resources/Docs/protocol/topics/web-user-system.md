# Web 用户系统

当前 `MCServerLauncher-Future` C# daemon 不内置 Web 用户系统。daemon 只负责校验主令牌和子令牌，并根据 token 中的权限字符串执行 action 权限检查。

Web 前端或其他外部服务如果需要用户、密码、刷新令牌、多用户管理等能力，应在自己的后端中实现，然后使用 daemon 主令牌申请权限受限的子令牌。

## 当前 daemon 支持的鉴权能力

- 主令牌来自 daemon 配置。
- 子令牌通过 `POST /subtoken` 申请。
- 子令牌是 JWT。
- WebSocket 连接通过 `?token=<令牌>` 传入 token。
- action handler 使用权限节点检查当前连接是否允许执行。

## 推荐外部 Web 流程

```mermaid
sequenceDiagram
    participant User as 用户
    participant Web as Web 前端
    participant Backend as Web 后端
    participant Daemon as daemon

    User ->> Web: 登录
    Web ->> Backend: 提交账号密码
    Backend -->> Web: 返回 Web 登录态
    Web ->> Backend: 请求 daemon 连接信息
    Backend ->> Daemon: POST /subtoken，使用主令牌申请子令牌
    Daemon -->> Backend: 返回子令牌
    Backend -->> Web: 返回 daemon 地址和子令牌
    Web ->> Daemon: ws(s)://host:port/api/v1?token=<子令牌>
    Daemon -->> Web: action 响应和 event 推送
```

## 不属于当前 daemon 协议的内容

以下内容不是当前 C# daemon 内置协议的一部分：

- 用户注册和登录。
- 密码哈希和刷新令牌。
- 多用户权限管理 UI。
- Web 会话状态。
