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
    param(
        [System.Diagnostics.Process]$Process
    )

    if ($null -eq $Process) {
        return
    }

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
            $connectTask = $client.ConnectAsync(
                [System.Net.IPAddress]::Loopback,
                $Port
            )

            if ($connectTask.Wait(250) -and $client.Connected) {
                return
            }
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

$Ports = [System.Collections.Generic.HashSet[int]]::new()

while ($Ports.Count -lt 3) {
    [void]$Ports.Add((Get-FreeTcpPort))
}

$PortArray = @($Ports)
$AnyTlsPort = $PortArray[0]
$ProxyPort = $PortArray[1]
$OriginPort = $PortArray[2]

$TempDirectory = Join-Path (
    [System.IO.Path]::GetTempPath()
) (
    "GeniaProxy-ProtocolLab-AnyTLS-Runtime-" +
    [Guid]::NewGuid().ToString("N")
)

$CertificatePath = Join-Path $TempDirectory "localhost-cert.pem"
$PrivateKeyPath = Join-Path $TempDirectory "localhost-key.pem"
$ServerConfigPath = Join-Path $TempDirectory "server.json"
$ClientConfigPath = Join-Path $TempDirectory "client.json"
$ServerStdoutPath = Join-Path $TempDirectory "server.stdout.log"
$ServerStderrPath = Join-Path $TempDirectory "server.stderr.log"
$ClientStdoutPath = Join-Path $TempDirectory "client.stdout.log"
$ClientStderrPath = Join-Path $TempDirectory "client.stderr.log"

$ServerProcess = $null
$ClientProcess = $null
$OriginJob = $null
$HttpClient = $null
$HttpHandler = $null
$Certificate = $null
$Rsa = $null

New-Item -ItemType Directory -Path $TempDirectory -Force | Out-Null

try {
    Write-Host "=== GeniaProxy Protocol Lab AnyTLS loopback runtime smoke ==="
    Write-Host "AnyTLS port: $AnyTlsPort"
    Write-Host "Client proxy port: $ProxyPort"
    Write-Host "Origin HTTP port: $OriginPort"

    $Rsa = [System.Security.Cryptography.RSA]::Create(2048)

    $Request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
        [System.Security.Cryptography.X509Certificates.X500DistinguishedName]::new(
            "CN=localhost"
        ),
        $Rsa,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
    )

    $San = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $San.AddDnsName("localhost")
    $San.AddIpAddress([System.Net.IPAddress]::Loopback)
    $Request.CertificateExtensions.Add($San.Build())

    $Request.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new(
            $false,
            $false,
            0,
            $true
        )
    )

    $Request.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
            [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,
            $true
        )
    )

    $ServerAuthOids = [System.Security.Cryptography.OidCollection]::new()
    [void]$ServerAuthOids.Add(
        [System.Security.Cryptography.Oid]::new(
            "1.3.6.1.5.5.7.3.1"
        )
    )

    $Request.CertificateExtensions.Add(
        [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new(
            $ServerAuthOids,
            $true
        )
    )

    $Certificate = $Request.CreateSelfSigned(
        [DateTimeOffset]::UtcNow.AddMinutes(-5),
        [DateTimeOffset]::UtcNow.AddDays(1)
    )

    $Utf8NoBom = [System.Text.UTF8Encoding]::new($false)

    [System.IO.File]::WriteAllText(
        $CertificatePath,
        $Certificate.ExportCertificatePem(),
        $Utf8NoBom
    )

    [System.IO.File]::WriteAllText(
        $PrivateKeyPath,
        $Rsa.ExportPkcs8PrivateKeyPem(),
        $Utf8NoBom
    )

    $Password = "protocol-lab-loopback-secret"

    $ServerConfig = [ordered]@{
        log = [ordered]@{
            level = "warn"
            timestamp = $true
        }
        inbounds = @(
            [ordered]@{
                type = "anytls"
                tag = "anytls-in"
                listen = "127.0.0.1"
                listen_port = $AnyTlsPort
                users = @(
                    [ordered]@{
                        name = "protocol-lab"
                        password = $Password
                    }
                )
                tls = [ordered]@{
                    enabled = $true
                    certificate_path = $CertificatePath
                    key_path = $PrivateKeyPath
                }
            }
        )
        outbounds = @(
            [ordered]@{
                type = "direct"
                tag = "direct"
            }
        )
        route = [ordered]@{
            final = "direct"
        }
    }

    [System.IO.File]::WriteAllText(
        $ServerConfigPath,
        ($ServerConfig | ConvertTo-Json -Depth 12),
        $Utf8NoBom
    )

    Write-Host ""
    Write-Host "=== Generate client config through GeniaProxy code ==="

    & dotnet run --project (Join-Path $Root "tests\GeniaProxy.Tests\GeniaProxy.Tests.csproj") -c Release -- --write-anytls-runtime-config $ClientConfigPath $AnyTlsPort $ProxyPort $CertificatePath

    if ($LASTEXITCODE -ne 0) {
        throw "AnyTLS runtime client config generation failed with exit code $LASTEXITCODE."
    }

    Write-Host ""
    Write-Host "=== Validate both configs with pinned sing-box ==="

    & $SingBoxPath check -c $ServerConfigPath
    if ($LASTEXITCODE -ne 0) {
        throw "sing-box rejected the loopback AnyTLS server config."
    }

    & $SingBoxPath check -c $ClientConfigPath
    if ($LASTEXITCODE -ne 0) {
        throw "sing-box rejected the generated AnyTLS client config."
    }

    $Marker = "GENIAPROXY_ANYTLS_LOOPBACK_OK"

    $OriginJob = Start-Job -ArgumentList $OriginPort, $Marker -ScriptBlock {
        param(
            [int]$Port,
            [string]$Marker
        )

        $Listener = [System.Net.Sockets.TcpListener]::new(
            [System.Net.IPAddress]::Loopback,
            $Port
        )

        $Client = $null
        $CrLf = [string][char]13 + [string][char]10
        $HeaderEnd = $CrLf + $CrLf

        try {
            $Listener.Start()
            $Client = $Listener.AcceptTcpClient()
            $Client.ReceiveTimeout = 10000

            $Stream = $Client.GetStream()
            $Buffer = New-Object byte[] 8192
            $RequestText = ""

            do {
                $Read = $Stream.Read(
                    $Buffer,
                    0,
                    $Buffer.Length
                )

                if ($Read -le 0) {
                    break
                }

                $RequestText += [System.Text.Encoding]::ASCII.GetString(
                    $Buffer,
                    0,
                    $Read
                )
            }
            while (
                -not $RequestText.Contains($HeaderEnd) -and
                $RequestText.Length -lt 32768
            )

            $BodyBytes = [System.Text.Encoding]::UTF8.GetBytes($Marker)

            $Headers = (
                "HTTP/1.1 200 OK" + $CrLf +
                "Content-Type: text/plain; charset=utf-8" + $CrLf +
                "Content-Length: $($BodyBytes.Length)" + $CrLf +
                "Connection: close" + $CrLf +
                $CrLf
            )

            $HeaderBytes = [System.Text.Encoding]::ASCII.GetBytes($Headers)

            $Stream.Write(
                $HeaderBytes,
                0,
                $HeaderBytes.Length
            )

            $Stream.Write(
                $BodyBytes,
                0,
                $BodyBytes.Length
            )

            $Stream.Flush()
        }
        finally {
            if ($null -ne $Client) {
                $Client.Dispose()
            }

            $Listener.Stop()
        }
    }

    Start-Sleep -Milliseconds 300

    Write-Host ""
    Write-Host "=== Start local AnyTLS server ==="

    $ServerStart = @{
        FilePath = $SingBoxPath
        ArgumentList = @("run", "-c", $ServerConfigPath)
        PassThru = $true
        WindowStyle = "Hidden"
        RedirectStandardOutput = $ServerStdoutPath
        RedirectStandardError = $ServerStderrPath
    }

    $ServerProcess = Start-Process @ServerStart

    Wait-TcpPort -Port $AnyTlsPort -Process $ServerProcess -Name "AnyTLS server" -ErrorLogPath $ServerStderrPath

    Write-Host "AnyTLS server ready."

    Write-Host ""
    Write-Host "=== Start generated AnyTLS client ==="

    $ClientStart = @{
        FilePath = $SingBoxPath
        ArgumentList = @("run", "-c", $ClientConfigPath)
        PassThru = $true
        WindowStyle = "Hidden"
        RedirectStandardOutput = $ClientStdoutPath
        RedirectStandardError = $ClientStderrPath
    }

    $ClientProcess = Start-Process @ClientStart

    Wait-TcpPort -Port $ProxyPort -Process $ClientProcess -Name "AnyTLS client" -ErrorLogPath $ClientStderrPath

    Write-Host "AnyTLS client proxy ready."

    Write-Host ""
    Write-Host "=== Send HTTP request through mixed -> AnyTLS -> direct ==="

    $ProxyUri = [System.Uri]::new(
        "http://127.0.0.1:$ProxyPort"
    )

    $HttpHandler = [System.Net.Http.HttpClientHandler]::new()
    $HttpHandler.UseProxy = $true
    $HttpHandler.Proxy = [System.Net.WebProxy]::new(
        $ProxyUri,
        $false
    )

    $HttpClient = [System.Net.Http.HttpClient]::new(
        $HttpHandler,
        $true
    )

    $HttpClient.Timeout = [TimeSpan]::FromSeconds(10)

    $Response = $HttpClient.GetAsync(
        "http://127.0.0.1:$OriginPort/probe"
    ).GetAwaiter().GetResult()

    $Body = $Response.Content.ReadAsStringAsync(
    ).GetAwaiter().GetResult()

    if (-not $Response.IsSuccessStatusCode) {
        throw "Loopback HTTP request failed with status $([int]$Response.StatusCode)."
    }

    if ($Body -ne $Marker) {
        throw "Unexpected loopback response body: $Body"
    }

    Write-Host ""
    Write-Host "[OK] AnyTLS loopback runtime path verified."
    Write-Host "[OK] TLS certificate verification remained enabled."
    Write-Host "[OK] Data path: mixed -> AnyTLS -> direct -> local HTTP origin."
}
catch {
    Write-Host ""
    Write-Host "=== AnyTLS server stderr ==="
    if (Test-Path -LiteralPath $ServerStderrPath) {
        Get-Content -LiteralPath $ServerStderrPath
    }

    Write-Host ""
    Write-Host "=== AnyTLS client stderr ==="
    if (Test-Path -LiteralPath $ClientStderrPath) {
        Get-Content -LiteralPath $ClientStderrPath
    }

    throw
}
finally {
    if ($null -ne $HttpClient) {
        $HttpClient.Dispose()
    }
    elseif ($null -ne $HttpHandler) {
        $HttpHandler.Dispose()
    }

    Stop-ProcessTreeSafe -Process $ClientProcess
    Stop-ProcessTreeSafe -Process $ServerProcess

    if ($null -ne $OriginJob) {
        Stop-Job -Job $OriginJob -ErrorAction SilentlyContinue
        Remove-Job -Job $OriginJob -Force -ErrorAction SilentlyContinue
    }

    if ($null -ne $Certificate) {
        $Certificate.Dispose()
    }

    if ($null -ne $Rsa) {
        $Rsa.Dispose()
    }

    Remove-Item -LiteralPath $TempDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
