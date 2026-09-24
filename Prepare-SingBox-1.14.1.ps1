param(
    [string]$CandidatePath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$EngineDirectory = Join-Path $Root "engine"
$Target = Join-Path $EngineDirectory "sing-box.exe"
$Checksum = Join-Path $EngineDirectory "sing-box.sha256"
$Provenance = Join-Path $EngineDirectory "sing-box.provenance.txt"
$LocalArchive = Join-Path $Root "sing-box-1.14.1-windows-amd64.zip"
$Version = "1.14.1"
$ExpectedArchiveSha256 = "5197F16D492D93202DC623622149A6ED040F8ECA263128F91D603F2B901BAA89"
$DownloadUrl = "https://github.com/SagerNet/sing-box/releases/download/v1.14.1/sing-box-1.14.1-windows-amd64.zip"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-Archive([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "sing-box archive not found: $Path"
    }
    $hash = Get-Hash $Path
    if ($hash -ne $ExpectedArchiveSha256) {
        throw "sing-box 1.14.1 archive SHA256 mismatch. Found: $hash"
    }
    Write-Host "[OK] sing-box 1.14.1 official archive SHA256 verified." -ForegroundColor Green
}

function Assert-Binary([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "sing-box.exe not found after extraction."
    }
    $versionOutput = @(& $Path version 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "sing-box version check failed with exit code $LASTEXITCODE."
    }
    $versionLine = ($versionOutput | Where-Object {
        $_ -match '^sing-box version\s+1\.14\.1(?:\s|$)'
    } | Select-Object -First 1)
    if (-not $versionLine) {
        throw "Extracted core does not identify itself as sing-box version 1.14.1."
    }
    Write-Host "[OK] $versionLine" -ForegroundColor Green
}

if ([string]::IsNullOrWhiteSpace($CandidatePath) -and
    (Test-Path -LiteralPath $Target) -and
    (Test-Path -LiteralPath $Checksum) -and
    (Test-Path -LiteralPath $Provenance)) {
    try {
        $expectedExeHash = ((Get-Content -LiteralPath $Checksum -Raw) -split "\s+")[0].Trim().ToUpperInvariant()
        $actualExeHash = Get-Hash $Target
        $provenanceText = Get-Content -LiteralPath $Provenance -Raw -Encoding UTF8
        if ($expectedExeHash -eq $actualExeHash -and
            $provenanceText.Contains($ExpectedArchiveSha256)) {
            Assert-Binary $Target
            Write-Host "[OK] Existing sing-box 1.14.1 engine + provenance verified; download skipped." -ForegroundColor Green
            return
        }
    }
    catch {
        Write-Host "Existing sing-box candidate is not trusted; preparing from the pinned archive." -ForegroundColor DarkYellow
    }
}

if ([string]::IsNullOrWhiteSpace($CandidatePath) -and (Test-Path -LiteralPath $LocalArchive)) {
    $CandidatePath = $LocalArchive
}

$TempRoot = Join-Path $env:TEMP ("GeniaProxy-sing-box-1.14.1-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null

try {
    if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
        $Archive = Join-Path $TempRoot "sing-box-1.14.1-windows-amd64.zip"
        Write-Host "Downloading official sing-box 1.14.1 windows-amd64..." -ForegroundColor Cyan
        Invoke-WebRequest -Uri $DownloadUrl -OutFile $Archive -UseBasicParsing
    }
    else {
        $Archive = (Resolve-Path -LiteralPath $CandidatePath).Path
        if ([System.IO.Path]::GetExtension($Archive) -ne ".zip") {
            throw "Engine Refresh RC1 accepts only the official sing-box 1.14.1 windows-amd64 ZIP archive as an offline candidate."
        }
    }

    Assert-Archive $Archive

    $Extract = Join-Path $TempRoot "extract"
    Expand-Archive -LiteralPath $Archive -DestinationPath $Extract -Force
    $Selected = (Get-ChildItem -LiteralPath $Extract -Filter "sing-box.exe" -File -Recurse | Select-Object -First 1).FullName
    if ([string]::IsNullOrWhiteSpace($Selected)) {
        throw "sing-box.exe was not found in the official release archive."
    }
    Assert-Binary $Selected

    $ExeHash = Get-Hash $Selected
    New-Item -ItemType Directory -Path $EngineDirectory -Force | Out-Null
    $TempTarget = Join-Path $EngineDirectory (".sing-box.1.14.1." + [Guid]::NewGuid().ToString("N") + ".tmp")
    try {
        Copy-Item -LiteralPath $Selected -Destination $TempTarget -Force
        if ((Get-Hash $TempTarget) -ne $ExeHash) {
            throw "Copied sing-box candidate changed during staging."
        }
        Copy-Item -LiteralPath $TempTarget -Destination $Target -Force
    }
    finally {
        if (Test-Path -LiteralPath $TempTarget) {
            Remove-Item -LiteralPath $TempTarget -Force -ErrorAction SilentlyContinue
        }
    }

    [System.IO.File]::WriteAllText($Checksum, "$ExeHash  sing-box.exe`r`n", $Utf8NoBom)
    $ProvenanceText = @(
        "version=1.14.1",
        "source=official-github-release",
        "archive=sing-box-1.14.1-windows-amd64.zip",
        "archive_sha256=$ExpectedArchiveSha256",
        "exe_sha256=$ExeHash"
    ) -join "`r`n"
    [System.IO.File]::WriteAllText($Provenance, $ProvenanceText + "`r`n", $Utf8NoBom)
    Write-Host "[OK] engine\\sing-box.exe prepared for Engine Refresh RC1." -ForegroundColor Green
    Write-Host "[INFO] sing-box.exe SHA256: $ExeHash" -ForegroundColor DarkCyan
}
finally {
    if (Test-Path -LiteralPath $TempRoot) {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
