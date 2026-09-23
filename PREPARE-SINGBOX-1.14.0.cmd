@echo off
setlocal
cd /d "%~dp0"
echo GeniaProxy 4.3.3 RC2 - prepare sing-box 1.14.0
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Prepare-SingBox-1.14.0.ps1" %*
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" (
  echo PREPARE FAILED. Exit code: %RC%
) else (
  echo PREPARE PASSED.
)
pause
exit /b %RC%
