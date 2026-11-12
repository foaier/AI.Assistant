# 抽离记录

2026-11-11 从 `D:\WorkCode\3_Coordination\Glevent\byt.srv.glevent` 的 main 工作树复制；来源提交 `bdd620d568890db1398c42a531a5c6f8950c38cb`。源工作树只读，所有构建和验证在 `D:\MyCode\Assistant` 下运行；项目现已重命名并位于 `D:\MyCode\SmartAssistant`。

前端保留 `public-assistant` 页面及组件，提取所需 UI、样式和 i18n；独立入口只包含助手、知识库、模型策略、模型服务和公开聊天路由。按用户后续要求已移除登录，页面直接打开；领域审计使用独立应用的固定操作人和单组织上下文。

后端保留 Assistant 的业务领域、应用服务、控制器、仓储和运行协议；移除对原完整宿主、私有包源、活动与日程模块的编译依赖。命名空间现统一以 `SmartAssistant` 开头，原 `Bayantu.Extensions.Common/DAL` 的本地薄适配位于 `SmartAssistant.Extensions.Common/DAL`，不依赖原私有 NuGet 包。

默认存储由原 MySQL 改为独立 SQLite，使用静态版本化迁移保留表字段、索引、乐观锁及墓碑语义。文件资产和加密密钥独立保存；不再生成管理员密码或登录 Cookie。未执行原项目数据库迁移，未搬运原业务数据。公开聊天依然校验自己的匿名会话 Bearer 和签名 stream ticket。

原活动和日程工具的处理器源保留用于参考，编译时显式排除；独立工具目录仅提供已接入的联网搜索。模型、Embedding 调用仍使用原 OpenAI 兼容协议。

复制清单中的 SHA256 对应原始源文件字节。新项目的适配变更包括前端独立上下文/传输、严格类型检查修正，以及后端平台适配/工具可用性收敛；原始文件和副本后续可以各自独立演进。

按用户后续要求，助手运行配置迁入本地 `appsettings.json`；凭据解析支持本地 `config://`，旧 `apollo://` 也只读本地配置。运行时没有 Apollo 客户端依赖或网络配置加载。验证报告见 `validation.md`，未向外部服务发起付费验证。
