# Web 用户与子令牌

C# daemon 不内置 Web 用户数据库。外部系统负责用户认证、撤销策略和令牌分发，并可由持有 daemon 主令牌的连接通过 `/api/v2` JSON-RPC 方法 `mcsl.auth.token.issue` 签发短期 JWT。该方法要求 `mcsl.auth.token.issue` 权限，并且仅接受主令牌身份；还必须启用 `security.allow_main_token_issue`。

请求参数为 `subject`、绝对 URI 格式的 `audience`、非空 `permissions` 和正整数 `ttl_seconds`。权限必须是调用方权限的子集且不能包含裸 `*`，有效期不能超过 `security.max_token_ttl_seconds`。成功结果包含 `token`、`subject`、`audience`、`permissions`、`expires_at` 和 `token_id`；获得子令牌的 client 使用 `/api/v2?token=<jwt>` 建立 WebSocket。

子令牌只能缩小可调用 catalog capability，不能绕过 daemon-side permission、path、instance lifecycle 或 session ownership 校验。
