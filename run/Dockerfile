# 前端构建阶段：仅在构建时安装 Node.js 和 pnpm。
# 最终镜像只包含静态资源，不运行前端开发服务器。
FROM node:24-bookworm-slim AS frontend-build
WORKDIR /src/frontend
RUN npm install --global pnpm@11.4.0
COPY frontend/package.json frontend/pnpm-lock.yaml frontend/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY frontend/ ./
RUN pnpm build

# 后端构建阶段：先还原依赖，再复制源码，便于复用构建缓存。
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS backend-build
WORKDIR /src
COPY backend/AI.Assistant.Api/AI.Assistant.Api.csproj backend/AI.Assistant.Api/
RUN dotnet restore backend/AI.Assistant.Api/AI.Assistant.Api.csproj
COPY backend/AI.Assistant.Api/ backend/AI.Assistant.Api/
RUN dotnet publish backend/AI.Assistant.Api/AI.Assistant.Api.csproj \
    --no-restore -c Release -o /out /p:UseAppHost=false

# 保持应用现有的前后端相对目录，使后端可直接托管前端页面。
# 数据卷目录由非 root 应用用户持有，时区默认采用北京时间。
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl tzdata libsqlite3-0 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data \
    && chown app:app /app/data
WORKDIR /app/backend/AI.Assistant.Api
COPY --from=backend-build /out/ ./
COPY --from=frontend-build /src/frontend/dist/ /app/frontend/dist/
ENV URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Assistant__DataDirectory=/app/data \
    TZ=Asia/Shanghai
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=3s --start-period=20s --retries=12 \
    CMD curl --fail --silent http://127.0.0.1:8080/api/health > /dev/null || exit 1
USER $APP_UID
ENTRYPOINT ["dotnet", "AI.Assistant.Api.dll"]
