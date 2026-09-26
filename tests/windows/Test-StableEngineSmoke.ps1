param(
    [string]$SingBoxPath = "",
    [string]$XrayPath = ""
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if ([string]::IsNullOrWhiteSpace($SingBoxPath)) {
    $SingBoxPath = Join-Path $Root "engine\sing-box.exe"
}

if ([string]::IsNullOrWhiteSpace($XrayPath)) {
    $XrayPath = Join-Path $Root "engine\xray.exe"
}

foreach ($path in @($SingBoxPath, $XrayPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Engine not found: $path"
    }
}

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new(
        [System.Net.IPAddress]::Loopback,
        0
    )

    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Stop-ProcessTreeSafe {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process) { return }

    try {
        if (-not $Process.HasExited) {
            $Process.Kill($true)
            $Process.WaitForExit(5000) | Out-Null
        }
    }
    catch {
    }
}

function Wait-TcpPort {
    param(
        [int]$Port,
        [System.Diagnostics.Process]$Process,
        [string]$Name,
        [string]$ErrorLogPath
    )

    $deadline = [DateTime]::UtcNow.AddSeconds(10)

    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            $details = ""
            if (Test-Path -LiteralPath $ErrorLogPath) {
                $details = Get-Content -LiteralPath $ErrorLogPath -Raw
            }
            throw "$Name exited before local proxy port became ready. $details"
        }

        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $task = $client.ConnectAsync([System.Net.IPAddress]::Loopback, $Port)
            if ($task.Wait(250) -and $client.Connected) { return }
        }
        catch {
        }
        finally {
            $client.Dispose()
        }

        Start-Sleep -Milliseconds 100
    }

    throw "$Name did not open local proxy port $Port within 10 seconds."
}

$SingBoxPort = Get-FreeTcpPort
$XrayPort = Get-FreeTcpPort
while ($XrayPort -eq $SingBoxPort) {
    $XrayPort = Get-FreeTcpPort
}

$TempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("GeniaProxy-StableSmoke-" + [Guid]::NewGuid().ToString("N"))
$SingBoxConfig = Join-Path $TempDirectory "stable-singbox.json"
$XrayConfig = Join-Path $TempDirectory "stable-xray.json"
$SingBoxOut = Join-Path $TempDirectory "singbox.stdout.log"
$SingBoxErr = Join-Path $TempDirectory "singbox.stderr.log"
$XrayOut = Join-Path $TempDirectory "xray.stdout.log"
$XrayErr = Join-Path $TempDirectory "xray.stderr.log"

$SingBoxProcess = $null
$XrayProcess = $null

New-Item -ItemType Directory -Path $TempDirectory -Force | Out-Null

try {
    Write-Host "=== GeniaProxy stable-engine smoke ==="
    Write-Host "sing-box local proxy port: $SingBoxPort"
    Write-Host "Xray local proxy port: $XrayPort"

    Write-Host ""
    Write-Host "=== Generate configs through stable import paths ==="

    & dotnet run --project (Join-Path $Root "tests\GeniaProxy.Tests\GeniaProxy.Tests.csproj") -c Release -- --write-stable-singbox-config $SingBoxConfig $SingBoxPort
    if ($LASTEXITCODE -ne 0) {
        throw "Stable sing-box config generation failed."
    }

    & dotnet run --project (Join-Path $Root "tests\GeniaProxy.Tests\GeniaProxy.Tests.csproj") -c Release -- --write-stable-xray-config $XrayConfig $XrayPort
    if ($LASTEXITCODE -ne 0) {
        throw "Stable Xray config generation failed."
    }

    Write-Host ""
    Write-Host "=== Pinned engine identity ==="
    & $SingBoxPath version
    if ($LASTEXITCODE -ne 0) { throw "sing-box version failed." }
    & $XrayPath version
    if ($LASTEXITCODE -ne 0) { throw "Xray version failed." }

    Write-Host ""
    Write-Host "=== Stable config validation ==="
    & $SingBoxPath check -c $SingBoxConfig
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned sing-box rejected the stable Hysteria2 config."
    }

    & $XrayPath run -test -config $XrayConfig
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned Xray rejected the stable VLESS/XHTTP TLS config."
    }

    Write-Host ""
    Write-Host "=== Stable engine startup smoke ==="

    $SingBoxProcess = Start-Process -FilePath $SingBoxPath -ArgumentList @("run","-c",$SingBoxConfig) -PassThru -WindowStyle Hidden -RedirectStandardOutput $SingBoxOut -RedirectStandardError $SingBoxErr
    Wait-TcpPort -Port $SingBoxPort -Process $SingBoxProcess -Name "sing-box" -ErrorLogPath $SingBoxErr
    Write-Host "[OK] Stable sing-box local proxy started."

    $XrayProcess = Start-Process -FilePath $XrayPath -ArgumentList @("run","-config",$XrayConfig) -PassThru -WindowStyle Hidden -RedirectStandardOutput $XrayOut -RedirectStandardError $XrayErr
    Wait-TcpPort -Port $XrayPort -Process $XrayProcess -Name "Xray" -ErrorLogPath $XrayErr
    Write-Host "[OK] Stable Xray local proxy started."

    if ($SingBoxProcess.HasExited) { throw "Stable sing-box exited unexpectedly." }
    if ($XrayProcess.HasExited) { throw "Stable Xray exited unexpectedly." }

    Write-Host ""
    Write-Host "[OK] Stable sing-box config + startup smoke passed."
    Write-Host "[OK] Stable Xray config + startup smoke passed."
    Write-Host "[OK] Protocol Lab changes did not replace stable engine pins."
}
catch {
    Write-Host ""
    Write-Host "=== sing-box stderr ==="
    if (Test-Path -LiteralPath $SingBoxErr) { Get-Content -LiteralPath $SingBoxErr }
    Write-Host ""
    Write-Host "=== Xray stderr ==="
    if (Test-Path -LiteralPath $XrayErr) { Get-Content -LiteralPath $XrayErr }
    throw
}
finally {
    Stop-ProcessTreeSafe -Process $XrayProcess
    Stop-ProcessTreeSafe -Process $SingBoxProcess
    Remove-Item -LiteralPath $TempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
