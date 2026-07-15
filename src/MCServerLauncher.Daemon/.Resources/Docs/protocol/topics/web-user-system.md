# Web 用户与子令牌

C# daemon 不内置 Web 用户数据库。外部系统可持有 daemon 主令牌，并通过 `POST /subtoken` 签发短期 JWT：form fields 为 `token`、`permissions` 和可选 `expires` seconds。

成功响应是 JWT 文本。无效 expiry/permission 返回 400，主令牌不匹配返回 401。外部系统负责用户认证、撤销策略和令牌分发；获得子令牌的 client 使用 `/api/v2?token=<jwt>` 建立 WebSocket。

子令牌只能缩小可调用 catalog capability，不能绕过 daemon-side permission、path、instance lifecycle 或 session ownership 校验。
