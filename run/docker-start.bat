@echo off
chcp 65001 >nul
setlocal
rem 从任意目录构建并启动完整应用，等待容器健康检查通过。
rem 请先启动 Docker Desktop，并使用 Linux 容器模式。
docker compose --project-directory "%~dp0." -f "%~dp0compose.yaml" up -d --build --wait --wait-timeout 120
set "runExitCode=%errorlevel%"
if not "%runExitCode%"=="0" goto finish
echo AI.Assistant 已启动，默认地址：http://127.0.0.1:5088
echo 自定义端口请查看 run/.env，停止服务请执行 run/docker-stop.bat。
:finish
if not "%AI_ASSISTANT_NO_PAUSE%"=="1" pause
exit /b %runExitCode%
