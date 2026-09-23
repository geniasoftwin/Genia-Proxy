param(
    [ValidateSet("Xray", "SingBox")]
    [string]$Core = "Xray",

    [string]$ProxyIp = "203.0.113.10",

    [string]$ExpectedExitIp = "203.0.113.10",

    [int]$ParallelRequests = 20
)

$ErrorActionPreference = "Stop"

function Pass([string]$Message) {
    Write-Host ("[PASS] {0}" -f $Message) -ForegroundColor Green
}

function Fail([string]$Message) {
    Write-Host ("[FAIL] {0}" -f $Message) -ForegroundColor Red
    $script:Failed++
}

function Info([string]$Message) {
    Write-Host ("[INFO] {0}" -f $Message) -ForegroundColor Cyan
}

function Get-TraceValue {
    param(
        [string]$Trace,
        [string]$Key
    )

    foreach ($line in ($Trace -split "`r?`n")) {
        if ($line -match "^$([regex]::Escape($Key))=(.*)$") {
            return $Matches[1].Trim()
        }
    }

    return $null
}

$Failed = 0
$ExpectedTunName = if ($Core -eq "Xray") {
    "geniaproxy-tun"
} else {
    "GeniaProxy"
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdmin = $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $isAdmin) {
    throw "Run PowerShell as Administrator."
}

Info ("Core={0}; expected TUN={1}" -f $Core, $ExpectedTunName)

$adapter = Get-NetAdapter -Name $ExpectedTunName -ErrorAction SilentlyContinue
if (-not $adapter) {
    Fail ("TUN adapter '{0}' was not found." -f $ExpectedTunName)
    Write-Host ""
    Write-Host "TUN integration test: FAIL (TUN prerequisite)" -ForegroundColor Red
    exit 1
} elseif ($adapter.Status -ne "Up") {
    Fail ("TUN adapter exists but status is {0}." -f $adapter.Status)
    Write-Host ""
    Write-Host "TUN integration test: FAIL (TUN prerequisite)" -ForegroundColor Red
    exit 1
} else {
    Pass ("TUN adapter is Up (ifIndex {0})." -f $adapter.ifIndex)
}

if ($adapter) {
    $publicRoute = Find-NetRoute -RemoteIPAddress 1.1.1.1 |
        Where-Object { $_.PSObject.Properties["DestinationPrefix"] } |
        Select-Object -First 1
    $publicIfIndex = $publicRoute.InterfaceIndex

    if ($publicIfIndex -eq $adapter.ifIndex) {
        Pass "Best route to 1.1.1.1 uses TUN."
    } else {
        Fail ("Best route to 1.1.1.1 uses ifIndex {0}, expected {1}." -f `
            $publicIfIndex, $adapter.ifIndex)
    }

    $proxyRoute = Find-NetRoute -RemoteIPAddress $ProxyIp |
        Where-Object { $_.PSObject.Properties["DestinationPrefix"] } |
        Select-Object -First 1
    $proxyIfIndex = $proxyRoute.InterfaceIndex

    if ($Core -eq "Xray" -and $proxyIfIndex -eq $adapter.ifIndex) {
        Fail "Proxy endpoint is routed back into Xray TUN (routing loop risk)."
    } else {
        Pass ("Proxy endpoint best route uses ifIndex {0}." -f $proxyIfIndex)
    }
}

$ipv6Defaults = Get-NetRoute `
    -AddressFamily IPv6 `
    -DestinationPrefix "::/0" `
    -ErrorAction SilentlyContinue

if ($Core -eq "Xray") {
    if ($ipv6Defaults) {
        Fail "IPv6 default route exists; Xray 4.2.x IPv4-only TUN is not leak-safe."
    } else {
        Pass "No IPv6 default route; IPv6 is fail-closed for this test."
    }
}

$dns = Get-DnsClientServerAddress -AddressFamily IPv4 |
    Where-Object { $_.ServerAddresses.Count -gt 0 }

Info "Active IPv4 DNS:"
$dns | Format-Table InterfaceAlias, InterfaceIndex, ServerAddresses -AutoSize

if ($Core -eq "Xray") {
    $publicDns = @("1.1.1.1", "1.0.0.1")
    $physicalDns = $dns |
        Where-Object { $_.InterfaceIndex -ne $adapter.ifIndex } |
        Select-Object -ExpandProperty ServerAddresses

    $leakSafe = $publicDns |
        Where-Object { $physicalDns -contains $_ }

    if ($leakSafe.Count -gt 0) {
        Pass "Physical adapter has leak-safe public DNS configured by GeniaProxy."
    } else {
        Fail "Leak-safe DNS 1.1.1.1/1.0.0.1 was not found on a physical adapter."
    }
}
elseif ($Core -eq "SingBox") {
    $tunDns = $dns |
        Where-Object { $_.InterfaceIndex -eq $adapter.ifIndex } |
        Select-Object -First 1

    $hasPrimary = $tunDns -and ($tunDns.ServerAddresses -contains "1.1.1.1")
    $hasSecondary = $tunDns -and ($tunDns.ServerAddresses -contains "1.0.0.1")

    if ($hasPrimary -and $hasSecondary) {
        Pass "Sing-box TUN has GeniaProxy DNS 1.1.1.1/1.0.0.1."
    } else {
        Fail "Sing-box TUN DNS 1.1.1.1/1.0.0.1 is missing."
    }

    $tunIpInterface = Get-NetIPInterface `
        -InterfaceIndex $adapter.ifIndex `
        -AddressFamily IPv4 `
        -ErrorAction SilentlyContinue

    if ($tunIpInterface -and
        $tunIpInterface.AutomaticMetric -eq "Disabled" -and
        $tunIpInterface.InterfaceMetric -eq 5) {
        Pass "Sing-box TUN interface metric is fixed at 5."
    } else {
        $metric = if ($tunIpInterface) {
            $tunIpInterface.InterfaceMetric
        } else {
            "missing"
        }

        Fail ("Sing-box TUN interface metric is {0}; expected 5." -f $metric)
    }

    try {
        $resolved = Resolve-DnsName `
            "www.microsoft.com" `
            -Type A `
            -DnsOnly `
            -QuickTimeout `
            -ErrorAction Stop |
            Where-Object { $_.IPAddress } |
            Select-Object -First 1

        if ($resolved) {
            Pass "Windows system DNS resolves through sing-box TUN."
        } else {
            Fail "Windows system DNS returned no IPv4 answer through sing-box TUN."
        }
    } catch {
        Fail ("Windows system DNS failed through sing-box TUN: {0}" -f $_.Exception.Message)
    }
}

$trace = & curl.exe `
    --silent `
    --show-error `
    --fail `
    --noproxy "*" `
    -4 `
    "https://www.cloudflare.com/cdn-cgi/trace"

if ($LASTEXITCODE -ne 0) {
    Fail ("curl trace failed with exit code {0}." -f $LASTEXITCODE)
} else {
    $exitIp = Get-TraceValue -Trace ($trace -join "`n") -Key "ip"

    if ($exitIp -eq $ExpectedExitIp) {
        Pass ("Cloudflare sees expected exit IP {0}." -f $exitIp)
    } else {
        Fail ("Cloudflare sees {0}; expected {1}." -f $exitIp, $ExpectedExitIp)
    }
}

Info ("Starting {0} parallel HTTPS requests..." -f $ParallelRequests)

$jobs = 1..$ParallelRequests | ForEach-Object {
    Start-Job -ScriptBlock {
        $code = & curl.exe `
            --silent `
            --show-error `
            --noproxy "*" `
            -4 `
            --output NUL `
            --write-out "%{http_code}" `
            "https://www.cloudflare.com/cdn-cgi/trace"

        if ($LASTEXITCODE -ne 0) {
            return "ERR:$LASTEXITCODE"
        }

        return $code
    }
}

try {
    Wait-Job -Job $jobs | Out-Null
    $results = Receive-Job -Job $jobs
} finally {
    Remove-Job -Job $jobs -Force -ErrorAction SilentlyContinue
}

$ok = @($results | Where-Object { $_ -eq "200" }).Count
$bad = @($results | Where-Object { $_ -ne "200" })

if ($ok -eq $ParallelRequests) {
    Pass ("Parallel HTTPS: {0}/{0} HTTP 200." -f $ParallelRequests)
} else {
    Fail ("Parallel HTTPS: {0}/{1} HTTP 200. Bad results: {2}" -f `
        $ok, $ParallelRequests, ($bad -join ", "))
}

Write-Host ""
if ($Failed -eq 0) {
    Write-Host "TUN integration test: PASS" -ForegroundColor Green
    exit 0
}

Write-Host ("TUN integration test: FAIL ({0} checks)" -f $Failed) `
    -ForegroundColor Red
exit 1
