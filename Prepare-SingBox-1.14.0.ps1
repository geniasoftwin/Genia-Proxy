param(
    [string]$CandidatePath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$EngineDirectory = Join-Path $Root "engine"
$Target = Join-Path $EngineDirectory "sing-box.exe"
$Checksum = Join-Path $EngineDirectory "sing-box.sha256"
$LocalCandidate = Join-Path $Root "sing-box-1.14.0.exe"
$ExpectedSha256 = "AAD0EDE010EAFA7B277E520464F3A66FDE820103D737EFF739F40F3CC9451DCC"
$ExpectedArchiveSha256 = "3FFB56267DA14E287BE48BD10CF7E6505260125BAD940B75101FBB4D5D58E5D6"
$DownloadUrl = "https://github.com/SagerNet/sing-box/releases/download/v1.14.0/sing-box-1.14.0-windows-amd64.zip"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-SingBox([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "sing-box candidate not found: $Path"
    }

    $hash = Get-Hash $Path
    if ($hash -ne $ExpectedSha256) {
        throw "sing-box 1.14.0 SHA256 mismatch. Found: $hash"
    }

    $versionOutput = @(& $Path version 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "sing-box version check failed with exit code $LASTEXITCODE."
    }

    $versionLine = ($versionOutput | Where-Object {
        $_ -match '^sing-box version\s+1\.14\.0(?:\s|$)'
    } | Select-Object -First 1)

    if (-not $versionLine) {
        throw "Candidate does not identify itself as sing-box version 1.14.0."
    }

    Write-Host "[OK] $versionLine" -ForegroundColor Green
    Write-Host "[OK] sing-box 1.14.0 SHA256 verified." -ForegroundColor Green
}

if (Test-Path -LiteralPath $Target) {
    $currentHash = Get-Hash $Target
    if ($currentHash -eq $ExpectedSha256) {
        Assert-SingBox $Target
        [System.IO.File]::WriteAllText(
            $Checksum,
            "$ExpectedSha256  sing-box.exe`r`n",
            $Utf8NoBom
        )
        return
    }
}

if ([string]::IsNullOrWhiteSpace($CandidatePath) -and
    (Test-Path -LiteralPath $LocalCandidate)) {
    $CandidatePath = $LocalCandidate
}

$TempRoot = Join-Path $env:TEMP ("GeniaProxy-sing-box-1.14.0-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null

try {
    $Selected = $null

    if (-not [string]::IsNullOrWhiteSpace($CandidatePath)) {
        $Selected = (Resolve-Path -LiteralPath $CandidatePath).Path
    }
    else {
        Write-Host "Downloading official sing-box 1.14.0 windows-amd64..." -ForegroundColor Cyan
        $Archive = Join-Path $TempRoot "sing-box-1.14.0-windows-amd64.zip"
        Invoke-WebRequest -Uri $DownloadUrl -OutFile $Archive -UseBasicParsing

        $archiveHash = Get-Hash $Archive
        if ($archiveHash -ne $ExpectedArchiveSha256) {
            throw "sing-box 1.14.0 archive SHA256 mismatch. Found: $archiveHash"
        }

        $Extract = Join-Path $TempRoot "extract"
        Expand-Archive -LiteralPath $Archive -DestinationPath $Extract -Force
        $Selected = (Get-ChildItem -LiteralPath $Extract -Filter "sing-box.exe" -File -Recurse | Select-Object -First 1).FullName

        if ([string]::IsNullOrWhiteSpace($Selected)) {
            throw "sing-box.exe was not found in the official release archive."
        }
    }

    Assert-SingBox $Selected

    New-Item -ItemType Directory -Path $EngineDirectory -Force | Out-Null
    $TempTarget = Join-Path $EngineDirectory (".sing-box.1.14.0." + [Guid]::NewGuid().ToString("N") + ".tmp")
    try {
        Copy-Item -LiteralPath $Selected -Destination $TempTarget -Force
        if ((Get-Hash $TempTarget) -ne $ExpectedSha256) {
            throw "Copied sing-box candidate changed during staging."
        }
        Copy-Item -LiteralPath $TempTarget -Destination $Target -Force
    }
    finally {
        if (Test-Path -LiteralPath $TempTarget) {
            Remove-Item -LiteralPath $TempTarget -Force -ErrorAction SilentlyContinue
        }
    }

    [System.IO.File]::WriteAllText(
        $Checksum,
        "$ExpectedSha256  sing-box.exe`r`n",
        $Utf8NoBom
    )

    Write-Host "[OK] engine\sing-box.exe prepared for GeniaProxy." -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $TempRoot) {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
