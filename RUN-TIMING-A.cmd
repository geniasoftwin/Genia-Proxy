@echo off
setlocal
cd /d "%~dp0"
if not exist "data" mkdir "data"
>"data\timing-ab-mode.txt" echo A
echo RC3 TIMING-AB mode A: original timing, 0 ms barrier.
start "" "%~dp0GeniaProxy.exe"
