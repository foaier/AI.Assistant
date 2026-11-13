@echo off
chcp 65001 >nul
setlocal
rem 启动前后端，路径以本批处理文件所在目录为准。
rem 支持 -SkipInstall 和 -DataDirectory 参数。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows.ps1" -Action Start %*
set "runExitCode=%errorlevel%"
if not "%AI_ASSISTANT_NO_PAUSE%"=="1" pause
exit /b %runExitCode%
