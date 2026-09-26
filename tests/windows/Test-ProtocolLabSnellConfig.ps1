param(
    [string]$SingBoxPath = ""
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if ([string]::IsNullOrWhiteSpace($SingBoxPath)) {
    $SingBoxPath = Join-Path $Root "engine\sing-box.exe"
}

if (-not (Test-Path -LiteralPath $SingBoxPath)) {
    throw "sing-box.exe not found: $SingBoxPath"
}

$TempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("GeniaProxy-ProtocolLab-Snell-" + [Guid]::NewGuid().ToString("N"))
$ConfigPath = Join-Path $TempDirectory "snell-probe.json"

New-Item -ItemType Directory -Path $TempDirectory -Force | Out-Null

try {
    Write-Host "=== GeniaProxy Protocol Lab Snell v6 config-check ==="

    & dotnet run `
        --project (Join-Path $Root "tests\GeniaProxy.Tests\GeniaProxy.Tests.csproj") `
        -c Release `
        -- `
        --write-snell-config `
        $ConfigPath

    if ($LASTEXITCODE -ne 0) {
        throw "Snell test config generation failed with exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "=== sing-box version ==="
    & $SingBoxPath version

    if ($LASTEXITCODE -ne 0) {
        throw "sing-box version failed with exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "=== sing-box check ==="
    & $SingBoxPath check -c $ConfigPath

    if ($LASTEXITCODE -ne 0) {
        throw "sing-box rejected the generated Snell config with exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "[OK] Generated Snell v6 Protocol Lab config accepted by pinned sing-box."
    Write-Host "[OK] Initial Snell path uses v6 with mode=default and no userkey by default."
}
finally {
    Remove-Item -LiteralPath $TempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
