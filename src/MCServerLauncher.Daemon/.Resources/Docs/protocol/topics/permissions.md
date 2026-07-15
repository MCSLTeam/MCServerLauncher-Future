# 权限

每个 RPC/event descriptor 在冻结 catalog 中声明 permission。`*` 表示任意已认证连接；其他值必须由当前 token 的 permission 集合满足。daemon 在执行 application method、建立 subscription 和创建 file session 前进行权威校验。

权限是 daemon-side trust boundary。WPF client 或其他 client 的预检查只用于交互，不能替代 daemon 校验。调用 `rpc.discover` 可查看当前 runtime catalog 的 permission metadata。
