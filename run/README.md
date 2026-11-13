# 启动与维护命令

所有入口均可从任意工作目录调用，也可以在 Windows 资源管理器中双击 `.bat` 文件。

## Windows 一键启动

安装 .NET 10 SDK、Node.js 22.12+ 和 pnpm 11.4，然后双击 `start.bat`。首次运行自动安装前端依赖、还原并编译后端，随后同时启动前后端。

| 文件 | 用途 |
|---|---|
| `start.bat` | 启动前后端，访问 <http://127.0.0.1:5188> |
| `stop.bat` | 停止本项目的前后端进程 |
| `build.bat` | 构建前端到 `frontend/dist`，发布后端到 `.run/publish` |
| `test.bat` | 执行前端和后端测试 |

后端地址为 <http://127.0.0.1:5088>。关闭启动窗口后，服务继续运行；停止服务请使用 `stop.bat`。日志及进程记录位于项目根目录的 `.run`，业务数据位于 `data`。

在项目根目录执行以下命令，可跳过已安装的依赖，或指定隔离数据目录。相对数据路径以项目根目录为基准。

```bat
run\start.bat -SkipInstall
run\start.bat -SkipInstall -DataDirectory ".run\browser-data"
run\stop.bat
run\build.bat -SkipInstall
run\test.bat -SkipInstall
```

脚本遇到端口占用会退出，停止操作会核对记录中的进程编号和启动时间。自动化执行时设置环境变量 `AI_ASSISTANT_NO_PAUSE=1`，可取消批处理结束时的按键等待。`windows.ps1` 是批处理共用的内部实现，无需修改系统执行策略。

## Docker 一键启动

需要 Docker 和 Docker Compose v2 或更新版本。Windows 使用已启动的 Docker Desktop，并切换到 Linux 容器模式；无需本机安装 .NET、Node.js 或 pnpm。

Windows 双击 `docker-start.bat`，停止时双击 `docker-stop.bat`；也可以在项目根目录的 PowerShell 中执行：

```powershell
.\run\docker-start.bat
.\run\docker-stop.bat
```

Linux/macOS 在项目根目录执行：

```sh
sh run/docker-start.sh
sh run/docker-stop.sh
```

首次启动会构建镜像并等待健康检查通过。访问 <http://127.0.0.1:5088>，前端页面、API、SSE 和 WebSocket 由同一个后端进程提供。Docker 模式使用生产构建，修改代码后再次执行启动命令即可重新构建并更新容器；仅重启容器不会更新镜像中的代码。

直接使用 Compose 的等价命令：

```sh
docker compose --project-directory run -f run/compose.yaml up -d --build --wait --wait-timeout 120
docker compose --project-directory run -f run/compose.yaml ps
docker compose --project-directory run -f run/compose.yaml logs --tail=100 -f assistant
docker compose --project-directory run -f run/compose.yaml down
```

`Dockerfile.dockerignore` 随 `Dockerfile` 自动生效，排除本机产物、测试数据及 `appsettings.Local.json`。数据库、上传文件及加密密钥存放在 `ai-assistant_assistant-data` 命名卷中，与 Windows 本地运行的 `data` 目录独立；正常停止、删除容器和重新构建不会删除该卷。

默认绑定本机 5088 端口，与 Windows 开发模式的后端端口相同，两种模式请选择一种启动。可复制 `.env.example` 为本目录的 `.env`，调整绑定地址、端口和时区。默认时区为 `Asia/Shanghai`，配合应用的 `DateTime.Now`。

模型服务可直接在页面配置。若使用 `config://AssistantSecrets:ModelKey` 等配置引用，可在 `compose.yaml` 的 `environment` 中通过 `AssistantSecrets__ModelKey` 等环境变量提供配置；本机 `appsettings.Local.json` 不会自动传入容器。

## 向量库与 Qdrant

当前 `compose.yaml` 只定义了 `assistant` 服务，**没有包含 Qdrant 容器，也不会安装 Qdrant**。Windows 本地启动脚本同样不会启动 Qdrant。默认向量存储为应用内置的 SQLite 持久化索引，数据与业务数据库一起保留在本地 `data` 目录或 Docker 的 `ai-assistant_assistant-data` 数据卷中。

后端根据 `Assistant:Knowledge:Qdrant:Endpoint` 选择向量存储：未配置时使用本地索引，配置有效地址后使用 Qdrant。Qdrant 负责保存和检索向量；生成向量所需的 Embedding 模型服务仍需在页面单独配置。

Windows 原生程序的下载、解压、配置、启动验证、可选 Dashboard 和开机自启步骤，见项目 [README：Windows 原生安装 Qdrant](../README.md#windows-原生安装-qdrant)。其中也包含 Docker Desktop 应用连接 Windows 宿主机 Qdrant 的配置方式。

接入已部署的 Qdrant 时，使用以下配置：

| 配置 | 说明 |
|---|---|
| `Assistant__Knowledge__Qdrant__Endpoint` | 后端可以访问的 Qdrant gRPC 地址，通常为 `http://服务地址:6334` |
| `Assistant__Knowledge__Qdrant__ApiKey` | Qdrant 启用了 API Key 时填写 |

Windows 本地运行可将配置写入后端的 `appsettings.Local.json`，也可在启动前设置当前终端的环境变量。例如，已经在本机 6334 端口运行了 Qdrant 时：

```powershell
$env:Assistant__Knowledge__Qdrant__Endpoint = 'http://127.0.0.1:6334'
.\run\start.bat
```

Docker 运行时，需要将上述配置加入 `compose.yaml` 的 `services.assistant.environment`，再重新执行 `docker-start.bat` 或 `docker-start.sh`。地址必须能从应用容器访问；容器内的 `127.0.0.1` 指向应用容器自身。仅在 `run/.env` 中添加配置不会自动传给容器，还需要在 Compose 的 `environment` 中引用。

Qdrant 的 6333 端口用于 HTTP API / Dashboard，后端连接使用 gRPC 的 6334 端口。这里说明的是接入已有服务，当前一键启动命令仍只部署 AI.Assistant。
