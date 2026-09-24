@echo off
setlocal
echo GeniaProxy 4.5.0 Alpha 1 Engine Refresh RC1 - prepare sing-box 1.14.1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Prepare-SingBox-1.14.1.ps1" %*
exit /b %ERRORLEVEL%
