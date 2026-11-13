@echo off
chcp 65001 >nul
setlocal
rem 停止由本项目启动的前后端，保留业务数据和日志。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows.ps1" -Action Stop
set "runExitCode=%errorlevel%"
if not "%AI_ASSISTANT_NO_PAUSE%"=="1" pause
exit /b %runExitCode%
