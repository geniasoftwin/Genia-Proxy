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
    "packages", "TestResults", "coverage", "node_modules",
    "logs", "runtime", "temp", "tmp"
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

function Get-RelativePathCompat {
    param([string]$FullName)

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

    $relative = Get-RelativePathCompat $FullName
    $parts = $relative -split '[\\/]'

    foreach ($part in $parts) {
        if ($excludedDirectoryNames -contains $part) {
            return $true
        }
    }

    return $false
}

function Test-PlaceholderLine {
    param([string]$Line)

    return $Line -match '(?i)(example|placeholder|redacted|changeme|replace[_ -]?me|your[_ -]?|dummy|test-only|<[^>]+>|REDACTED)'
}

function Test-VersionContext {
    param([string]$Line)

    return $Line -match '(?i)(version|fileversion|assemblyversion|manifestversion|switchermanifestversion)'
}

function Test-PublicIPv4 {
    param([string]$Address)

    if ($knownPublicInfrastructureIPv4 -contains $Address) {
        return $false
    }

    $parts = $Address.Split('.')
    if ($parts.Count -ne 4) {
        return $false
    }

    $octets = @()
    foreach ($part in $parts) {
        $value = 0
        if (-not [int]::TryParse($part, [ref]$value)) {
            return $false
        }

        if ($value -lt 0 -or $value -gt 255) {
            return $false
        }

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

    # Benchmark and documentation-only ranges.
    if ($a -eq 198 -and ($b -eq 18 -or $b -eq 19)) { return $false }
    if ($a -eq 192 -and $b -eq 0 -and $c -eq 2) { return $false }
    if ($a -eq 198 -and $b -eq 51 -and $c -eq 100) { return $false }
    if ($a -eq 203 -and $b -eq 0 -and $c -eq 113) { return $false }

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
    $relative = Get-RelativePathCompat $file.FullName
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

            # Literal UUID assigned to an access-related field.
            if ($line -match '(?i)\b(uuid|user[_-]?id|client[_-]?id)\b.*[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}') {
                if (-not (Test-PlaceholderLine $line)) {
                    Add-Finding -File $relative -Line $lineNumber -Rule "Potential access UUID"
                }
            }

            # Complete static proxy/access URI. Runtime interpolation and test fixtures are ignored.
            $uriMatch = [regex]::Match(
                $line,
                '(?i)\b(?:vless|hysteria2?|hy2|trojan|tuic|ss|socks5?)://[^\s''"]+'
            )

            if ($uriMatch.Success) {
                $isSafeUri =
                    (Test-PlaceholderLine $line) -or
                    ($line -match '[{}]') -or
                    ($line -match '(127\.0\.0\.1|198\.51\.100\.|203\.0\.113\.|192\.0\.2\.)')

                if (-not $isSafeUri) {
                    Add-Finding -File $relative -Line $lineNumber -Rule "Proxy/access URI"
                }
            }

            # Literal credentials only; variable assignments are intentionally ignored.
            $credentialMatch = [regex]::Match(
                $line,
                '(?i)\b(password|passwd|token|api[_-]?key|secret|private[_-]?key|privateKey)\b\s*[:=]\s*["'']([^"'']{6,})["'']'
            )

            if ($credentialMatch.Success -and (-not (Test-PlaceholderLine $line))) {
                Add-Finding -File $relative -Line $lineNumber -Rule "Credential or private-key assignment"
            }

            # Literal host/endpoint assignments only.
            $hostMatch = [regex]::Match(
                $line,
                '(?i)\b(server|address|endpoint|host|sni)\b\s*[:=]\s*["'']([a-z0-9][a-z0-9.-]+\.[a-z]{2,})["'']'
            )

            if ($hostMatch.Success) {
                $hostValue = $hostMatch.Groups[2].Value.ToLowerInvariant()
                $isKnownHost =
                    ($knownPublicProbeHosts -contains $hostValue) -or
                    ($hostValue -match '(^|\.)example\.(com|org|net)$') -or
                    ($hostValue -eq "example.invalid") -or
                    ($hostValue -eq "localhost")

                if ((-not $isKnownHost) -and (-not (Test-PlaceholderLine $line))) {
                    Add-Finding -File $relative -Line $lineNumber -Rule "Potential production hostname/endpoint"
                }
            }

            foreach ($match in [regex]::Matches($line, '(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?!\d)')) {
                $address = $match.Value

                if (Test-VersionContext $line) {
                    continue
                }

                # Intentional split-default-route prefixes.
                if (($address -eq "0.0.0.0" -or $address -eq "128.0.0.0") -and
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
    Write-Host "Review or replace real values, then rerun this script."
    exit 1
}

Write-Host "PRECHECK PASSED: no configured high-risk patterns were found." -ForegroundColor Green
Write-Host "This is a safety net, not a substitute for manual review."
exit 0
