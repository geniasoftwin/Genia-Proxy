@echo off
setlocal
cd /d "%~dp0"
echo GeniaProxy 4.5.0 Alpha 1 Engine Refresh RC3 - verify Xray 26.3.27
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Prepare-Xray-26.3.27.ps1" %*
set EXITCODE=%ERRORLEVEL%
exit /b %EXITCODE%
