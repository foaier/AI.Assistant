@echo off
chcp 65001 >nul
setlocal
rem 执行前端与后端测试，支持 -SkipInstall。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows.ps1" -Action Test %*
set "runExitCode=%errorlevel%"
if not "%AI_ASSISTANT_NO_PAUSE%"=="1" pause
exit /b %runExitCode%
