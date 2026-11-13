@echo off
chcp 65001 >nul
setlocal
rem 构建前端静态文件并发布后端，支持 -SkipInstall。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows.ps1" -Action Build %*
set "runExitCode=%errorlevel%"
if not "%AI_ASSISTANT_NO_PAUSE%"=="1" pause
exit /b %runExitCode%
