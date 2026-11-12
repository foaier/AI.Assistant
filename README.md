# SmartAssistant 智能助手

从 Glevent 的 Assistant 功能抽离的独立前后端项目。保留助手管理、工作台、外观设置、模型服务和业务策略、知识库、发布管理、公开聊天、会话记录，以及 SSE / WebSocket 流式响应。

运行和构建均不依赖 Glevent 源目录，不连接原项目数据库。打开页面即可管理助手，无需登录。此项目使用本地 SQLite 数据库和文件存储，不设租户或组织；前端保留原助手页面和中英文文案。

## 快速启动（Windows PowerShell）

需要 .NET 10 SDK、Node.js 22.12+ 和 pnpm 11.4。

```powershell
cd D:\MyCode\SmartAssistant
.\start-dev.ps1
```

打开 <http://127.0.0.1:5188>，直接进入助手工作台。

停止本项目的两个进程：

```powershell
.\stop-dev.ps1
```

脚本会检查 5088 / 5188 端口；如已被占用则退出。它只停止自己记录的进程。日志位于 `.run/`，数据位于 `data/`，二者都不纳入版本控制。

已安装依赖时，可以运行 `.\start-dev.ps1 -SkipInstall`。自定义数据目录使用 `-DataDirectory 'D:\AssistantData'`。

## 使用

1. 在“模型服务”配置 OpenAI 兼容服务地址、API Key 和模型。密钥可以使用 `config://配置键` 引用 `appsettings.json` 中的值，也可以直接输入后由本项目 AES-GCM 密钥加密存储。
2. 在“模型策略”创建业务策略，选择对应模型服务及模型。
3. 在“智能助手”创建助手，选择策略，设置欢迎语、外观和功能；需要知识问答时绑定知识库。
4. 在工作台测试并发布，使用生成的 `/assistants/{assistantKey}` 链接公开访问。草稿与已发布版本隔离；更新公开内容需要重新发布。

模型和基础设施配置集中在 `backend/SmartAssistant.Api/appsettings.json`，运行时无需连接 Apollo。原业务数据库和会话数据独立保留在各自项目中。

## 目录

```text
backend/
  SmartAssistant.Api/
    Controllers/      助手管理与公开聊天接口
    Modules/          Assistant 业务、契约、仓储及 Domain/Entities 实体
    Platform/         固定操作人、事务、文件、凭据、向量存储适配
    Migrations/       SQLite 版本迁移及校验
    Program.cs        独立 ASP.NET Core 宿主
  SmartAssistant.Tests/    抽离的原测试及独立 HTTP/SQLite 集成测试
frontend/
  client/features/public-assistant/  原助手页面、组件、服务
  client/context/                   界面语言与本地权限适配
  e2e/                              浏览器联调
docs/                               来源清单及验证说明
data/                               本机数据库、资产、加密密钥
```

## 构建与验证

```powershell
.\build.ps1
.\test.ps1
```

构建输出：`frontend/dist/`、`.run/publish/`。保留项目目录结构时，使用已构建版本运行：

```powershell
cd D:\MyCode\SmartAssistant\backend\SmartAssistant.Api
dotnet ..\..\.run\publish\SmartAssistant.Api.dll
```

打开 <http://127.0.0.1:5088>，同一后端进程会托管已构建前端。开发时前端由 5188 代理 `/api` 到 5088。

浏览器验收使用独立测试数据目录（不要对已有业务数据运行）：

```powershell
.\start-dev.ps1 -SkipInstall -DataDirectory "$PWD\.run\browser-data"
cd frontend
pnpm exec playwright install chromium
pnpm test:e2e
```

浏览器测试直接操作管理页面和 API，不需要密码或 Cookie。桌面和手机报告位于 `frontend/playwright-report/`，测试截图位于 `frontend/test-results/`。

## 配置与数据

配置保存在 `backend/SmartAssistant.Api/appsettings.json`。可用 .NET 环境变量（用 `__` 表示层级）或后端目录的 `appsettings.Local.json` 覆盖本机配置。

| 配置 | 用途 |
|---|---|
| `Assistant__DataDirectory` | 独立数据目录 |
| `Assistant__Knowledge__Qdrant__Endpoint` | 可选 Qdrant gRPC 地址；缺省使用本地持久化余弦检索 |
| `Assistant__Knowledge__Qdrant__ApiKey` | 可选 Qdrant 密钥 |
| `Assistant__Redis__ConnectionString` | 可选 Redis 事件传输；缺省使用单进程内存事件代理 |

模型服务中的 `config://AssistantSecrets:ModelKey` 引用本地配置对应键；`appsettings://` 同义，原有 `apollo://` 引用也只从本地对应键读取，不会请求 Apollo。`env://变量名` 继续支持显式环境变量凭据。

备份整个 `data/` 目录，并同时保留本地配置。数据库内加密的凭据还需要 `keys.json` 才能解密。数据库仅通过版本化迁移初始化；后续结构变更需要新增迁移，不修改已应用的 SQL 或校验值。

## 抽离边界

- 原 Glevent 活动/日程查询工具依赖业务模块，独立项目不注册这两类工具；绑定不可用工具的发布会被拒绝。需要时可实现独立集成后再注册。
- 模型、Embedding 和联网搜索保留真实服务调用。知识文档支持文本、Markdown、JSON、DOCX，与抽离时原解析器一致。
- 默认 SQLite、本地向量索引和内存事件代理适合单机运行。本地向量检索按集合扫描；更大规模可配置 Qdrant。多副本部署还需要共享文件和会话运行状态，本项目默认部署方式为单进程。
- 领域事件在事务内写入本地 Outbox，当前独立项目没有外部事件订阅者；没有复制原系统消息总线和业务消费者。
- 助手、知识库和问答采用物理删除；关联运行、文档及索引记录级联清理，同一问题可以删除后重新创建。

来源记录见 `docs/backend-source-manifest.json`、`frontend/source-manifest.json` 和 `backend/SmartAssistant.Tests/source-manifest.json`。来源清单只用于追溯；构建与运行没有源项目路径依赖。
