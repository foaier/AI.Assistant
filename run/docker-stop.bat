@echo off
chcp 65001 >nul
setlocal
rem 停止并移除本项目容器，保留存放数据库、上传文件和密钥的数据卷。
docker compose --project-directory "%~dp0." -f "%~dp0compose.yaml" down
set "runExitCode=%errorlevel%"
if not "%AI_ASSISTANT_NO_PAUSE%"=="1" pause
exit /b %runExitCode%
