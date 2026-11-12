# Apollo 配置迁移调查

本次调查只读取原 Glevent 项目和已配置 Apollo namespace，没有修改原代码、原数据库或远程配置。本文只记录路径、键名、类型与状态，不记录凭据值或服务器令牌。

**目前尚未完成真实助手配置值的迁移。** 已检查的可用 Apollo namespace 没有 Assistant 相关配置，不能把连接 Apollo 的引导配置视为已经迁入模型、Embedding 或联网搜索配置。

## 已核查来源

- 原项目 `byt.srv.glevent/src/byt.srv.glevent/appsettings.json` 及 Development、Expo、Fat、Production、Release 配置。
- `Program.cs` 配置加载顺序、Apollo namespace 注册。
- `start-dev.sh`、`start-backend.sh`、`backend-service.sh`、`backend-service.ps1`、`run-local.sh`、`Properties/launchSettings.json`。
- ignored `.run`、源码 `App_Data`、构建输出、当前配置环境变量键名与 .NET 进程归属。
- 已安装 Apollo SDK 的默认缓存位置 `C:\opt\data/<AppId>/config-cache`、`/opt/data/<AppId>/config-cache`，以及合理的 Windows AppData/Temp 缓存目录。

在已检查来源中未发现实际 `.env` 或 `appsettings.*.Local.json` 覆盖文件，没有 Apollo/Assistant/Redis 进程环境覆盖键，没有运行中的原 Glevent 后端进程，也未发现原 Apollo 缓存。WSL 只有 docker-desktop，未发现原开发 Linux 发行版。

## Apollo 读取结果

| 环境 / namespace | 状态 | 助手相关配置 |
| --- | --- | --- |
| Development / `application` | GET 成功，2 个支付 sandbox 配置键 | 无 |
| Development / `Public.CaptchaOptions` | GET 成功，12 个 TCaptchaOptions/GeetestOptions 配置键 | 无 |
| Development / `Public.BayantuLogOptions` | GET 成功，20 个 BayantuLog/Logging 及日志 RabbitMQ 配置键 | 无 |
| Development / `Public.GleventCmsSharedOptions` | HTTP 404 | 未取得 |
| Expo / 已配置 namespace | 网络不可用 | 未取得 |
| Fat / 已配置 namespace | 网络不可用 | 未取得 |
| Production、Release / 已配置 namespace | 网络不可用 | 未取得 |

支付、验证码、日志等其他业务的凭据未迁入独立 Assistant。

## 原助手实际配置位置

原 Assistant 直接从 IConfiguration 读取的键包括：

- `Assistant:Provider:TimeoutSeconds`
- `Assistant:Provider:MaxResponseBytes`
- `Assistant:Knowledge:Qdrant:Endpoint`
- `Assistant:Knowledge:Qdrant:ApiKey`
- `AssistantCredentials:CurrentKeyId`
- `AssistantCredentials:EncryptionKeys:{keyId}`
- 原共享加密 fallback：`PaymentCredentials:CurrentKeyId`、`PaymentCredentials:EncryptionKeys:{keyId}`

原 stream ticket 签名由 `JwtKeyResolver` 的 `assistant-stream-ticket` purpose key 派生。

**模型服务与联网搜索的实际配置存放在原数据库，不由 Apollo 直接持有。** 表 `assistant_policy_record.settings_json` 保存模型服务的 `BaseUrl`、`SecretReference`、`SecretVersion`、`Models`，以及联网搜索的 `Endpoint`、`BotId`、`ServiceName`、`ApiKey`。知识库 Embedding 的选型保存在 `assistant_knowledge_embedding_policy`。

仅复制 Apollo 的 AppId/MetaServer/Env 引导配置，不能创建这些数据库记录，也不能自动启用模型。原数据库未被本次调查连接或修改。完成真实值迁移需要可用的助手配置导出，或明确含助手配置的实际 Apollo 环境、AppId 和 namespace；含已加密模型密钥的数据库导出还需要对应加密 key ring。

调查辅助脚本位于 `.run/read-assistant-apollo.ps1`；它仅选择 Assistant、对应加密 key ring 与 Redis，不输出值。目前 `.run/assistant-apollo-values.json` 为空对象。
