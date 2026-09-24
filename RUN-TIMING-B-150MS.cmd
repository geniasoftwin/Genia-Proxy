@echo off
setlocal
cd /d "%~dp0"
if not exist "data" mkdir "data"
>"data\timing-ab-mode.txt" echo B
echo RC3 TIMING-AB mode B: 150 ms barrier before primary direct UDP DNS probe.
start "" "%~dp0GeniaProxy.exe"
