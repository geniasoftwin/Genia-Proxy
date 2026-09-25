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

$UsedTcpPorts = [System.Collections.Generic.HashSet[int]]::new()

function Get-UniqueTcpPort {
    do {
        $port = Get-FreeTcpPort
    }
    while (-not $UsedTcpPorts.Add($port))

    return $port
}

function Get-FreeUdpPort {
    $client = [System.Net.Sockets.UdpClient]::new(0)

    try {
        return ([System.Net.IPEndPoint]$client.Client.LocalEndPoint).Port
    }
    finally {
        $client.Dispose()
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
            throw "$Name exited before port $Port became ready. $details"
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

    throw "$Name did not open port $Port within 10 seconds."
}

function Assert-ListenerAlive {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$Port,
        [string]$Name,
        [string]$ErrorLogPath
    )

    if ($Process.HasExited) {
        $details = ""
        if (Test-Path -LiteralPath $ErrorLogPath) {
            $details = Get-Content -LiteralPath $ErrorLogPath -Raw
        }
        throw "$Name exited during Protocol Lab failure isolation test. $details"
    }

    Wait-TcpPort -Port $Port -Process $Process -Name $Name -ErrorLogPath $ErrorLogPath
}

function Get-CriticalNetworkSignature {
    $criticalV4 = @("0.0.0.0/0", "0.0.0.0/1", "128.0.0.0/1")

    $routes4 = @(
        Get-NetRoute -AddressFamily IPv4 -ErrorAction Stop |
            Where-Object { $_.DestinationPrefix -in $criticalV4 } |
            Select-Object DestinationPrefix, InterfaceIndex, NextHop, RouteMetric |
            Sort-Object DestinationPrefix, InterfaceIndex, NextHop, RouteMetric
    )

    $routes6 = @(
        Get-NetRoute -AddressFamily IPv6 -ErrorAction Stop |
            Where-Object { $_.DestinationPrefix -eq "::/0" } |
            Select-Object DestinationPrefix, InterfaceIndex, NextHop, RouteMetric |
            Sort-Object DestinationPrefix, InterfaceIndex, NextHop, RouteMetric
    )

    $dns = @(
        Get-DnsClientServerAddress -ErrorAction Stop |
            ForEach-Object {
                [pscustomobject]@{
                    InterfaceIndex = $_.InterfaceIndex
                    AddressFamily = [string]$_.AddressFamily
                    ServerAddresses = @($_.ServerAddresses) -join ","
                }
            } |
            Sort-Object InterfaceIndex, AddressFamily, ServerAddresses
    )

    return [ordered]@{
        Routes4 = $routes4
        Routes6 = $routes6
        Dns = $dns
    } | ConvertTo-Json -Depth 6 -Compress
}

function Invoke-ExpectedProxyFailure {
    param(
        [int]$ProxyPort,
        [string]$Name
    )

    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseProxy = $true
    $handler.Proxy = [System.Net.WebProxy]::new(
        [System.Uri]::new("http://127.0.0.1:$ProxyPort"),
        $false
    )

    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [TimeSpan]::FromSeconds(4)

    try {
        try {
            $response = $client.GetAsync(
                "http://127.0.0.1:1/protocol-lab-failure"
            ).GetAwaiter().GetResult()

            if ($response.IsSuccessStatusCode) {
                throw "$Name unexpectedly produced a successful response."
            }
        }
        catch [System.Net.Http.HttpRequestException] {
        }
        catch [System.Threading.Tasks.TaskCanceledException] {
        }
    }
    finally {
        $client.Dispose()
    }
}

$StableSingBoxPort = Get-UniqueTcpPort
$StableXrayPort = Get-UniqueTcpPort
$AnyTlsProxyPort = Get-UniqueTcpPort
$TuicProxyPort = Get-UniqueTcpPort
$SnellProxyPort = Get-UniqueTcpPort
$AnyTlsServerPort = Get-UniqueTcpPort
$TuicServerPort = Get-FreeUdpPort
$SnellServerPort = Get-UniqueTcpPort

$TempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("GeniaProxy-ProtocolLab-FailureIsolation-" + [Guid]::NewGuid().ToString("N"))
$StableSingBoxConfig = Join-Path $TempDirectory "stable-singbox.json"
$StableXrayConfig = Join-Path $TempDirectory "stable-xray.json"
$AnyTlsConfig = Join-Path $TempDirectory "anytls-failure.json"
$TuicConfig = Join-Path $TempDirectory "tuic-failure.json"
$SnellConfig = Join-Path $TempDirectory "snell-failure.json"
$CertificatePath = Join-Path $TempDirectory "localhost-cert.pem"

$StableSingBoxOut = Join-Path $TempDirectory "stable-singbox.stdout.log"
$StableSingBoxErr = Join-Path $TempDirectory "stable-singbox.stderr.log"
$StableXrayOut = Join-Path $TempDirectory "stable-xray.stdout.log"
$StableXrayErr = Join-Path $TempDirectory "stable-xray.stderr.log"

$StableSingBoxProcess = $null
$StableXrayProcess = $null
$LabProcess = $null
$Certificate = $null
$Rsa = $null

New-Item -ItemType Directory -Path $TempDirectory -Force | Out-Null

try {
    Write-Host "=== GeniaProxy Protocol Lab failure-isolation gate ==="

    $Rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $Request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        [System.Security.Cryptography.X509Certificates.X500DistinguishedName]::new("CN=localhost"),
        $Rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
    )

    $San = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $San.AddDnsName("localhost")
    $San.AddIpAddress([System.Net.IPAddress]::Loopback)
    $Request.CertificateExtensions.Add($San.Build())

    $Certificate = $Request.CreateSelfSigned(
        [DateTimeOffset]::UtcNow.AddMinutes(-5),
        [DateTimeOffset]::UtcNow.AddHours(1)
    )

    [System.IO.File]::WriteAllText(
        $CertificatePath,
        $Certificate.ExportCertificatePem(),
        [System.Text.UTF8Encoding]::new($false)
    )

    Write-Host ""
    Write-Host "=== Generate stable + failing Protocol Lab configs ==="

    & dotnet run --project (Join-Path $Root "tests\GeniaProxy.Tests\GeniaProxy.Tests.csproj") -c Release -- --write-stable-singbox-config $StableSingBoxConfig $StableSingBoxPort
    if ($LASTEXITCODE -ne 0) { throw "Stable sing-box config generation failed." }

    & dotnet run --project (Join-Path $Root "tests\GeniaProxy.Tests\GeniaProxy.Tests.csproj") -c Release -- --write-stable-xray-config $StableXrayConfig $StableXrayPort
    if ($LASTEXITCODE -ne 0) { throw "Stable Xray config generation failed." }

    & dotnet run --project (Join-Path $Root "tests\GeniaProxy.Tests\GeniaProxy.Tests.csproj") -c Release -- --write-anytls-runtime-config $AnyTlsConfig $AnyTlsServerPort $AnyTlsProxyPort $CertificatePath
    if ($LASTEXITCODE -ne 0) { throw "AnyTLS failure config generation failed." }

    & dotnet run --project (Join-Path $Root "tests\GeniaProxy.Tests\GeniaProxy.Tests.csproj") -c Release -- --write-tuic-runtime-config $TuicConfig $TuicServerPort $TuicProxyPort $CertificatePath
    if ($LASTEXITCODE -ne 0) { throw "TUIC failure config generation failed." }

    & dotnet run --project (Join-Path $Root "tests\GeniaProxy.Tests\GeniaProxy.Tests.csproj") -c Release -- --write-snell-runtime-config $SnellConfig $SnellServerPort $SnellProxyPort
    if ($LASTEXITCODE -ne 0) { throw "Snell failure config generation failed." }

    foreach ($config in @($StableSingBoxConfig, $AnyTlsConfig, $TuicConfig, $SnellConfig)) {
        & $SingBoxPath check -c $config
        if ($LASTEXITCODE -ne 0) { throw "sing-box rejected config: $config" }
    }

    & $XrayPath run -test -config $StableXrayConfig
    if ($LASTEXITCODE -ne 0) { throw "Xray rejected stable smoke config." }

    Write-Host ""
    Write-Host "=== Start stable local sessions ==="

    $StableSingBoxProcess = Start-Process -FilePath $SingBoxPath -ArgumentList @("run","-c",$StableSingBoxConfig) -PassThru -WindowStyle Hidden -RedirectStandardOutput $StableSingBoxOut -RedirectStandardError $StableSingBoxErr
    Wait-TcpPort -Port $StableSingBoxPort -Process $StableSingBoxProcess -Name "stable sing-box" -ErrorLogPath $StableSingBoxErr

    $StableXrayProcess = Start-Process -FilePath $XrayPath -ArgumentList @("run","-config",$StableXrayConfig) -PassThru -WindowStyle Hidden -RedirectStandardOutput $StableXrayOut -RedirectStandardError $StableXrayErr
    Wait-TcpPort -Port $StableXrayPort -Process $StableXrayProcess -Name "stable Xray" -ErrorLogPath $StableXrayErr

    $BaselineNetwork = Get-CriticalNetworkSignature

    Write-Host "[OK] Stable sing-box + Xray listeners are active."
    Write-Host "[OK] Baseline routes/DNS snapshot captured."

    $cases = @(
        [pscustomobject]@{ Name = "AnyTLS"; Config = $AnyTlsConfig; Port = $AnyTlsProxyPort },
        [pscustomobject]@{ Name = "TUIC"; Config = $TuicConfig; Port = $TuicProxyPort },
        [pscustomobject]@{ Name = "Snell v6"; Config = $SnellConfig; Port = $SnellProxyPort }
    )

    foreach ($case in $cases) {
        $labOut = Join-Path $TempDirectory (($case.Name -replace "[^A-Za-z0-9]", "") + ".stdout.log")
        $labErr = Join-Path $TempDirectory (($case.Name -replace "[^A-Za-z0-9]", "") + ".stderr.log")

        Write-Host ""
        Write-Host ("=== Expected {0} remote failure ===" -f $case.Name)

        $LabProcess = Start-Process -FilePath $SingBoxPath -ArgumentList @("run","-c",$case.Config) -PassThru -WindowStyle Hidden -RedirectStandardOutput $labOut -RedirectStandardError $labErr

        try {
            Wait-TcpPort -Port $case.Port -Process $LabProcess -Name $case.Name -ErrorLogPath $labErr
            Invoke-ExpectedProxyFailure -ProxyPort $case.Port -Name $case.Name
        }
        finally {
            Stop-ProcessTreeSafe -Process $LabProcess
            $LabProcess = $null
        }

        Assert-ListenerAlive -Process $StableSingBoxProcess -Port $StableSingBoxPort -Name "stable sing-box" -ErrorLogPath $StableSingBoxErr
        Assert-ListenerAlive -Process $StableXrayProcess -Port $StableXrayPort -Name "stable Xray" -ErrorLogPath $StableXrayErr

        $AfterNetwork = Get-CriticalNetworkSignature
        if ($AfterNetwork -ne $BaselineNetwork) {
            throw "$($case.Name) failure changed critical Windows routes or DNS state."
        }

        Write-Host ("[OK] {0} failure left stable sessions alive." -f $case.Name)
        Write-Host ("[OK] {0} failure left critical routes/DNS unchanged." -f $case.Name)
    }

    Write-Host ""
    Write-Host "[OK] Protocol Lab failure isolation verified for AnyTLS, TUIC and Snell v6."
    Write-Host "[OK] Stable sing-box and Xray sessions remained alive throughout."
    Write-Host "[OK] Critical Windows routes/DNS remained unchanged throughout."
}
catch {
    Write-Host ""
    Write-Host "=== stable sing-box stderr ==="
    if (Test-Path -LiteralPath $StableSingBoxErr) { Get-Content -LiteralPath $StableSingBoxErr }
    Write-Host ""
    Write-Host "=== stable Xray stderr ==="
    if (Test-Path -LiteralPath $StableXrayErr) { Get-Content -LiteralPath $StableXrayErr }
    throw
}
finally {
    Stop-ProcessTreeSafe -Process $LabProcess
    Stop-ProcessTreeSafe -Process $StableXrayProcess
    Stop-ProcessTreeSafe -Process $StableSingBoxProcess

    if ($null -ne $Certificate) { $Certificate.Dispose() }
    if ($null -ne $Rsa) { $Rsa.Dispose() }

    Remove-Item -LiteralPath $TempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
