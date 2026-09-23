[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (& git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) {
    throw "Run this script from inside the GeniaProxy Git repository."
}

$hooksDir = Join-Path $repoRoot ".git\hooks"
$hookPath = Join-Path $hooksDir "pre-commit"

New-Item -ItemType Directory -Force -Path $hooksDir | Out-Null

$hook = @'
#!/bin/sh
set -e

if command -v pwsh >/dev/null 2>&1; then
    PS=pwsh
elif command -v powershell.exe >/dev/null 2>&1; then
    PS=powershell.exe
else
    echo "GeniaProxy pre-commit: PowerShell was not found; refusing to bypass source preflight."
    exit 1
fi

"$PS" -NoProfile -ExecutionPolicy Bypass -File tools/Invoke-SourcePreflight.ps1 -Path .
'@

[System.IO.File]::WriteAllText(
    $hookPath,
    $hook,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Host "Installed GeniaProxy pre-commit safety hook:"
Write-Host $hookPath
Write-Host ""
Write-Host "Every commit will run tools/Invoke-SourcePreflight.ps1."
