# AI.Assistant 智能助手

源码仓库：[foaier/AI.Assistant](https://github.com/foaier/AI.Assistant)。

AI.Assistant 是一个 AI 智能助手。包含助手管理、工作台、外观设置、模型服务和业务策略、知识库、发布管理、公开聊天、会话记录，以及 SSE / WebSocket 流式响应。

打开页面即可管理助手，无需登录。默认使用本地 SQLite 数据库、文件存储和持久化向量索引。

## 快速启动

所有启动命令集中在 [`run`](run/README.md) 目录。以下命令均在项目根目录执行，也可以在 Windows 中双击对应的 `.bat` 文件。

| 方式 | 环境要求 | 启动入口 | 访问地址 |
|---|---|---|---|
| Windows 本地开发 | .NET 10 SDK、Node.js 22.12+、pnpm 11.4.0 | `run/start.bat` | <http://127.0.0.1:5188> |
| Windows Docker | 已启动的 Docker Desktop，使用 Linux 容器 | `run/docker-start.bat` | <http://127.0.0.1:5088> |
| Linux/macOS Docker | Docker、Docker Compose v2 或更新版本 | `sh run/docker-start.sh` | <http://127.0.0.1:5088> |

两种运行模式默认共用后端 5088 端口，切换前先停止正在运行的另一种模式。Docker 模式不要求本机安装 .NET、Node.js 或 pnpm。

### Windows 本地开发

安装 .NET 10 SDK、Node.js 22.12+ 和 pnpm 11.4.0，然后执行：

```powershell
.\run\start.bat
```

首次启动会安装前端依赖、还原并编译后端，再同时启动前后端。打开 <http://127.0.0.1:5188> 进入助手工作台，后端 API 地址为 <http://127.0.0.1:5088>。

停止本项目的两个进程：

```powershell
.\run\stop.bat
```

脚本会检查 5088 / 5188 端口；如已被占用则退出。它只停止自己记录的进程。日志位于 `.run/`，数据位于 `data/`，二者都不纳入版本控制。

已安装依赖时，可以运行 `.\run\start.bat -SkipInstall`。自定义数据目录使用 `-DataDirectory "D:\AssistantData"`。

### Docker 一键启动

Windows PowerShell：

```powershell
# 构建镜像并启动，等待健康检查通过
.\run\docker-start.bat

# 停止并移除容器，保留数据卷
.\run\docker-stop.bat
```

Linux/macOS：

```sh
# 构建镜像并启动
sh run/docker-start.sh

# 停止并移除容器，保留数据卷
sh run/docker-stop.sh
```

也可以直接使用 Compose：

```sh
docker compose --project-directory run -f run/compose.yaml up -d --build --wait --wait-timeout 120

# 查看运行状态与最近日志
docker compose --project-directory run -f run/compose.yaml ps
docker compose --project-directory run -f run/compose.yaml logs --tail=100 -f assistant

# 停止，保留数据卷
docker compose --project-directory run -f run/compose.yaml down
```

打开 <http://127.0.0.1:5088>，同一容器提供已构建的前端与后端 API。数据库、上传文件、加密密钥和默认本地向量索引保存在 `ai-assistant_assistant-data` 数据卷中，时区默认使用 `Asia/Shanghai`。

**修改代码后，再次执行 Docker 启动命令即可重新构建并更新容器。** 仅执行 `docker compose restart` 不会把代码变更打包到镜像中。

如需修改访问端口或绑定地址，首次配置时将 `run/.env.example` 复制为 `run/.env`，修改 `ASSISTANT_PORT`、`ASSISTANT_BIND_ADDRESS` 后重新执行启动命令。详细说明见 [`run/README.md`](run/README.md)。

### 向量存储与 Qdrant

**当前 Docker 启动配置没有安装或启动 Qdrant。** `run/compose.yaml` 只有 `assistant` 服务；默认由应用把向量持久化到 SQLite，并执行本地余弦相似度检索，因此无需额外启动向量数据库。

后端已支持接入单独部署的 Qdrant。配置 `Assistant__Knowledge__Qdrant__Endpoint` 为可访问的 gRPC 地址后，会改用 Qdrant；常用端口为 **6334**，6333 是 HTTP API / Dashboard 端口。接入配置方式见 [`run/README.md`](run/README.md#向量库与-qdrant)。

无论使用本地索引还是 Qdrant，知识库文档向量化仍需要在页面配置可用的 Embedding 模型服务。

## 使用

1. 在“模型服务”配置 OpenAI 兼容服务地址、API Key 和模型。密钥可以使用 `config://配置键` 引用 `appsettings.json` 中的值，也可以直接输入后由本项目 AES-GCM 密钥加密存储。
2. 在“模型策略”创建业务策略，选择对应模型服务及模型。
3. 在“智能助手”创建助手，选择策略，设置欢迎语、外观和功能；需要知识问答时绑定知识库。
4. 在工作台测试并发布，使用生成的 `/assistants/{assistantKey}` 链接公开访问。草稿与已发布版本隔离；更新公开内容需要重新发布。

模型和基础设施配置集中在 `backend/AI.Assistant.Api/appsettings.json`，运行时无需连接 Apollo。原业务数据库和会话数据独立保留在各自项目中。

后端统一使用 `DateTime.Now` 获取当前时间，按服务器本地时间处理业务日期；接口返回的日期包含本地时区偏移。

## 目录

```text
backend/
  AI.Assistant.Api/
    Controllers/      助手管理与公开聊天接口
    Models/           DTO、请求/响应、执行快照及其他数据容器，按用途分目录
    Services/         Assistant 业务、接口、仓储及 Domain/Entities 实体
    Infrastructure/   本地事务、文件、凭据、向量存储适配
    Migrations/       当前 SQLite 初始化结构及校验
    Program.cs        独立 ASP.NET Core 宿主
  AI.Assistant.Tests/    抽离的原测试及独立 HTTP/SQLite 集成测试
frontend/
  client/features/public-assistant/  原助手页面、组件、服务
  client/context/                   界面语言上下文
  e2e/                              浏览器联调
run/                                Windows 批处理、Docker 配置和启动说明
data/                               本机数据库、资产、加密密钥
```

## 构建与验证

```powershell
.\run\build.bat
.\run\test.bat
```

构建输出：`frontend/dist/`、`.run/publish/`。保留项目目录结构时，使用已构建版本运行：

```powershell
cd D:\MyCode\AI.Assistant\backend\AI.Assistant.Api
dotnet ..\..\.run\publish\AI.Assistant.Api.dll
```

打开 <http://127.0.0.1:5088>，同一后端进程会托管已构建前端。开发时前端由 5188 代理 `/api` 到 5088。

浏览器验收使用独立测试数据目录（不要对已有业务数据运行）：

```powershell
.\run\start.bat -SkipInstall -DataDirectory "$PWD\.run\browser-data"
cd frontend
pnpm exec playwright install chromium
pnpm test:e2e
```

浏览器测试直接操作管理页面和 API，不需要密码或 Cookie。桌面和手机报告位于 `frontend/playwright-report/`，测试截图位于 `frontend/test-results/`。

## 配置与数据

配置保存在 `backend/AI.Assistant.Api/appsettings.json`。可用 .NET 环境变量（用 `__` 表示层级）或后端目录的 `appsettings.Local.json` 覆盖本机配置。

| 配置 | 用途 |
|---|---|
| `Assistant__DataDirectory` | 独立数据目录 |
| `Assistant__Knowledge__Qdrant__Endpoint` | 可选 Qdrant gRPC 地址，通常使用 6334 端口；缺省使用 SQLite 持久化向量索引 |
| `Assistant__Knowledge__Qdrant__ApiKey` | 可选 Qdrant 密钥 |
| `Assistant__Redis__ConnectionString` | 可选 Redis 事件传输；缺省使用单进程内存事件代理 |

模型服务中的 `config://AssistantSecrets:ModelKey` 引用本地配置对应键；`appsettings://` 同义，原有 `apollo://` 引用也只从本地对应键读取，不会请求 Apollo。`env://变量名` 继续支持显式环境变量凭据。

备份整个 `data/` 目录，并同时保留本地配置。数据库内加密的凭据还需要 `keys.json` 才能解密。开发阶段仅维护 `001_initial.sql` 中的当前结构及对应校验值，结构变更后重建开发数据库。
