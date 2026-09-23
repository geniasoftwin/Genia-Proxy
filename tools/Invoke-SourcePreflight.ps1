[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Path = "."
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path -LiteralPath $Path).Path

$excludedDirectoryNames = @(
    ".git", ".vs", ".idea",
    "bin", "obj", "out", "output", "dist", "artifacts", "publish",
    "packages", "TestResults", "coverage", "node_modules"
)

$blockedExtensions = @(
    ".pem", ".key", ".pfx", ".p12", ".jks", ".keystore"
)

$suspiciousNames = @(
    ".env", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519",
    "credentials.json", "secrets.json"
)

$textExtensions = @(
    ".cs", ".csproj", ".sln", ".props", ".targets",
    ".ps1", ".psm1", ".psd1", ".cmd", ".bat",
    ".json", ".jsonc", ".yaml", ".yml", ".toml", ".ini", ".conf",
    ".xml", ".config", ".md", ".txt", ".env", ".example"
)

$knownPublicInfrastructureIPv4 = @(
    "1.1.1.1",
    "1.0.0.1",
    "8.8.8.8"
)

$knownPublicProbeHosts = @(
    "www.cloudflare.com",
    "www.microsoft.com",
    "www.gstatic.com",
    "myip.opendns.com"
)

$findings = New-Object 'System.Collections.Generic.List[object]'

function Add-Finding {
    param(
        [string]$File,
        [string]$Line,
        [string]$Rule
    )

    $findings.Add([pscustomobject]@{
        File = $File
        Line = $Line
        Rule = $Rule
    })
}

function Get-RelativePath {
    param([string]$FullName)

    # PowerShell 5.1 runs on .NET Framework, which does not provide
    # System.IO.Path.GetRelativePath(). Keep this helper compatible with
    # both Windows PowerShell 5.1 and PowerShell 7+.
    $resolved = [System.IO.Path]::GetFullPath($FullName)
    $base = [System.IO.Path]::GetFullPath($root)
    $separator = [string][System.IO.Path]::DirectorySeparatorChar

    if ($resolved.Equals($base, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "."
    }

    if (-not $base.EndsWith($separator)) {
        $base += $separator
    }

    if ($resolved.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $resolved.Substring($base.Length)
    }

    return $resolved
}

function Test-ExcludedPath {
    param([string]$FullName)

    $relative = Get-RelativePath $FullName
    $parts = $relative -split '[\\/]'

    foreach ($part in $parts) {
        if ($excludedDirectoryNames -contains $part) {
            return $true
        }
    }

    return $false
}

function Test-PlaceholderValue {
    param([string]$Line)

    if ($Line -match '(?i)(example|placeholder|redacted|changeme|replace[_ -]?me|your[_ -]?|dummy|test-only|<[^>]+>|REDACTED)') {
        return $true
    }

    # Common low-entropy fixture values used only in tests/examples.
    return $Line -match '(?i)["'']secret["'']'
}

function Test-VersionLikeContext {
    param([string]$Line)

    return $Line -match '(?i)(version|fileversion|assemblyversion|manifest\s+build|switchermanifestversion)'
}

function Test-SafeProxyUriLine {
    param(
        [string]$Uri,
        [string]$Line
    )

    if (Test-PlaceholderValue $Line) { return $true }

    if ($Line -match '(?i)(127\.0\.0\.1|198\.51\.100\.|203\.0\.113\.|192\.0\.2\.)') {
        return $true
    }

    # Source-code interpolation constructs a URI at runtime but does not embed a credential.
    if ($Line -match '[{}]') { return $true }

    # Bare scheme mentions in docs/UI text are not access credentials.
    if (($Uri -notmatch '@') -and
        ($Uri -notmatch '(?i)[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}') -and
        ($Uri -notmatch '(?i)^ss://[A-Za-z0-9+/_=-]{20,}')) {
        return $true
    }

    return $false
}

function Test-PublicIPv4 {
    param([string]$Address)

    $parts = $Address.Split('.')
    if ($parts.Count -ne 4) { return $false }

    $octets = @()
    foreach ($part in $parts) {
        $value = 0
        if (-not [int]::TryParse($part, [ref]$value)) { return $false }
        if ($value -lt 0 -or $value -gt 255) { return $false }
        $octets += $value
    }

    $a = $octets[0]
    $b = $octets[1]
    $c = $octets[2]

    # Unspecified, loopback, RFC1918, link-local, CGNAT, multicast/reserved.
    if ($a -eq 0 -or $a -eq 10 -or $a -eq 127 -or $a -ge 224) { return $false }
    if ($a -eq 169 -and $b -eq 254) { return $false }
    if ($a -eq 172 -and $b -ge 16 -and $b -le 31) { return $false }
    if ($a -eq 192 -and $b -eq 168) { return $false }
    if ($a -eq 100 -and $b -ge 64 -and $b -le 127) { return $false }

    # Benchmark/test/documentation ranges.
    if ($a -eq 198 -and ($b -eq 18 -or $b -eq 19)) { return $false }
    if ($a -eq 192 -and $b -eq 0 -and $c -eq 2) { return $false }
    if ($a -eq 198 -and $b -eq 51 -and $c -eq 100) { return $false }
    if ($a -eq 203 -and $b -eq 0 -and $c -eq 113) { return $false }

    # Intentional public recursive DNS services used by the product/test suite.
    if ($knownPublicInfrastructureIPv4 -contains $Address) { return $false }

    return $true
}

Write-Host "=== GeniaProxy source preflight ==="
Write-Host "Root: $root"
Write-Host "PowerShell: $($PSVersionTable.PSVersion)"
Write-Host "Matched secret values are never printed."
Write-Host ""

$files = Get-ChildItem -LiteralPath $root -Recurse -File -Force |
    Where-Object { -not (Test-ExcludedPath $_.FullName) }

foreach ($file in $files) {
    $relative = Get-RelativePath $file.FullName
    $extension = $file.Extension.ToLowerInvariant()
    $name = $file.Name.ToLowerInvariant()

    if ($blockedExtensions -contains $extension) {
        Add-Finding -File $relative -Line "-" -Rule "Private key/certificate container file"
        continue
    }

    if ($suspiciousNames -contains $name) {
        Add-Finding -File $relative -Line "-" -Rule "Sensitive credential filename"
    }

    if (($textExtensions -notcontains $extension) -and ($name -notlike ".env*")) {
        continue
    }

    $lineNumber = 0

    try {
        foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
            $lineNumber++

            if ($line -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----') {
                Add-Finding -File $relative -Line $lineNumber -Rule "Embedded private key"
            }

            $uriMatch = [regex]::Match(
                $line,
                '(?i)\b(vless|hysteria2?|hy2|trojan|tuic|ss|socks5?)://[^\s''"]+'
            )
            if ($uriMatch.Success -and
                (-not (Test-SafeProxyUriLine -Uri $uriMatch.Value -Line $line))) {
                Add-Finding -File $relative -Line $lineNumber -Rule "Proxy/access URI"
            }

            # Flag only literal credential assignments. Variable-to-variable assignments such as
            # CancellationToken token = request.Token are not secrets.
            $credentialMatch = [regex]::Match(
                $line,
                '(?i)\b(password|passwd|token|api[_-]?key|secret|private[_-]?key|privateKey)\b\s*["'']?\s*[:=]\s*["'']([^"'']{6,})["'']'
            )
            if ($credentialMatch.Success -and (-not (Test-PlaceholderValue $line))) {
                Add-Finding -File $relative -Line $lineNumber -Rule "Credential or private-key assignment"
            }

            if ($line -match '(?i)\b(uuid|user[_-]?id|client[_-]?id)\b\s*["'']?\s*[:=]\s*["'']?\s*[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}') {
                if (-not (Test-PlaceholderValue $line)) {
                    Add-Finding -File $relative -Line $lineNumber -Rule "Potential access UUID"
                }
            }

            # Only literal endpoint assignments are relevant. Expressions such as
            # endpoint = Inspect(source) or server = uri.Host are not embedded infrastructure.
            $hostMatch = [regex]::Match(
                $line,
                '(?i)\b(server|address|endpoint|host|sni)\b\s*["'']?\s*[:=]\s*["'']([a-z0-9][a-z0-9.-]+\.[a-z]{2,})["'']'
            )
            if ($hostMatch.Success) {
                $hostValue = $hostMatch.Groups[2].Value.ToLowerInvariant()
                $isKnownHost =
                    ($knownPublicProbeHosts -contains $hostValue) -or
                    ($hostValue -match '(^|\.)example\.(com|org|net)
        }
    }
    catch {
        Add-Finding -File $relative -Line "-" -Rule "Could not safely inspect text file"
    }
}

$findings = $findings |
    Sort-Object File, Line, Rule -Unique

if (@($findings).Count -gt 0) {
    Write-Host "PRECHECK FAILED: review the following locations before any public push." -ForegroundColor Red
    $findings | Format-Table -AutoSize
    Write-Host ""
    Write-Host "Replace real values with explicit placeholders, then rerun this script."
    exit 1
}

Write-Host "PRECHECK PASSED: no configured high-risk patterns were found." -ForegroundColor Green
Write-Host "This is a safety net, not a substitute for manual review."
exit 0
) -or
                    ($hostValue -eq 'example.invalid') -or
                    ($hostValue -eq 'localhost')

                if ((-not $isKnownHost) -and (-not (Test-PlaceholderValue $line))) {
                    Add-Finding -File $relative -Line $lineNumber -Rule "Potential production hostname/endpoint"
                }
            }

            foreach ($match in [regex]::Matches($line, '(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?!\d)')) {
                $address = $match.Value

                # Do not mistake product/assembly/manifest version numbers for IPv4.
                if (Test-VersionLikeContext $line) { continue }

                # These two prefixes intentionally split the IPv4 default route in half.
                if (($address -eq '0.0.0.0' -or $address -eq '128.0.0.0') -and
                    ($line -match ([regex]::Escape($address) + '/1'))) {
                    continue
                }

                if (Test-PublicIPv4 $address) {
                    Add-Finding -File $relative -Line $lineNumber -Rule "Public IPv4 address"
                }
            }
        }
    }
    catch {
        Add-Finding -File $relative -Line "-" -Rule "Could not safely inspect text file"
    }
}

$findings = $findings |
    Sort-Object File, Line, Rule -Unique

if (@($findings).Count -gt 0) {
    Write-Host "PRECHECK FAILED: review the following locations before any public push." -ForegroundColor Red
    $findings | Format-Table -AutoSize
    Write-Host ""
    Write-Host "Replace real values with explicit placeholders, then rerun this script."
    exit 1
}

Write-Host "PRECHECK PASSED: no configured high-risk patterns were found." -ForegroundColor Green
Write-Host "This is a safety net, not a substitute for manual review."
exit 0
