# SmartAssistant 前端

独立 React 18 / TypeScript / Vite 前端，助手页面、聊天、知识库、发布、模型服务和策略界面均提取自原项目。所有源码、组件、翻译、字体与时区工具都存放在本目录内，构建不引用 Glevent。

## 开发

要求 Node.js 22.12+、pnpm 11.4。先启动独立后端（`http://127.0.0.1:5088`），再执行：

```powershell
pnpm install --frozen-lockfile
pnpm dev
```

打开 `http://127.0.0.1:5188` 即可进入助手管理工作台。

管理页面：

- `/admin/assistants`：智能助手与工作台。
- `/admin/knowledge-bases`：知识库。
- `/admin/assistant-models`：业务模型策略。
- `/admin/system/assistant-models`：模型服务与联网搜索。
- `/assistants/:assistantKey`：公开聊天。

开发代理将 `/api`、`/uploads` 转发至 `127.0.0.1:5088`。生产构建文件输出至 `dist`，部署时让前后端使用同一个站点。管理界面和 API 可直接访问，不设租户或组织，不需要密码或会话 Cookie。公开聊天沿用匿名会话 Bearer Token；模型服务凭证仍由后端管理。

## 验证

```powershell
pnpm typecheck
pnpm build
pnpm test
```

真实浏览器验收要求独立前后端均已启动，并使用一次性验收数据目录：

```powershell
pnpm test:e2e
```

测试会在当前后端创建验收助手、知识库及固定问答配置，仅用于隔离的验收数据目录。桌面与手机均覆盖直接进入工作台、无 Cookie 管理 API、持久化、匿名固定问答、刷新恢复及撤回发布。固定问答绕过模型服务，测试不连接第三方模型供应商。

`source-manifest.json` 记录提取文件在原项目中的相对位置和原始 SHA-256，可与抽离后的适配差异核对。助手现有回归测试全部保留；适配层新增无凭据 API、业务错误和 multipart 传输测试。依赖版本固定到原项目锁文件中的版本，避免抽离时引入依赖升级。

独立适配范围：应用入口与导航、本地权限、无凭据 API 传输、上传地址、助手翻译与显示时区 Provider。保留原时区约定 `Asia/Shanghai`。聊天取消流程捕获已校验的 runId，超时句柄允许初始化前为空；这些仅用于通过严格 TypeScript 检查。
