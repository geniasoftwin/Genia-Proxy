param(
    [string]$CandidatePath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$EngineDirectory = Join-Path $Root "engine"
$Target = Join-Path $EngineDirectory "xray.exe"
$Checksum = Join-Path $EngineDirectory "xray.sha256"
$Provenance = Join-Path $EngineDirectory "xray.provenance.txt"
$LocalArchive = Join-Path $Root "Xray-windows-64-v26.3.27.zip"
$Version = "26.3.27"
$ExpectedExeSha256 = "15C2D007954AC53BA69B80EC91242786B3C0B71D52649165B4CA1D5CC96EF8F1"
$ExpectedExeSize = 35613696
$DownloadUrl = "https://github.com/XTLS/Xray-core/releases/download/v26.3.27/Xray-windows-64.zip"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-BinaryIdentity([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "xray.exe not found: $Path"
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -ne $ExpectedExeSize) {
        throw "Xray 26.3.27 size mismatch. Found: $($item.Length); expected: $ExpectedExeSize."
    }

    $hash = Get-Hash $Path
    if ($hash -ne $ExpectedExeSha256) {
        throw "Xray 26.3.27 executable SHA256 mismatch. Found: $hash"
    }

    $versionOutput = @(& $Path version 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Xray version check failed with exit code $LASTEXITCODE."
    }
    $versionLine = ($versionOutput | Where-Object {
        $_ -match '^Xray\s+26\.3\.27(?:\s|$)'
    } | Select-Object -First 1)
    if (-not $versionLine) {
        throw "Pinned executable does not identify itself as Xray 26.3.27."
    }
    Write-Host "[OK] $versionLine" -ForegroundColor Green
}

function Write-Provenance([string]$SourceKind, [string]$ArchiveHash) {
    [System.IO.File]::WriteAllText($Checksum, "$ExpectedExeSha256  xray.exe`r`n", $Utf8NoBom)
    $rows = @(
        "version=26.3.27",
        "commit=d2758a0",
        "go=go1.26.1",
        "platform=windows/amd64",
        "source=$SourceKind",
        "release_channel=validated-stable-baseline",
        "verification=pinned-executable-sha256",
        "exe_size=$ExpectedExeSize",
        "exe_sha256=$ExpectedExeSha256"
    )
    if (-not [string]::IsNullOrWhiteSpace($ArchiveHash)) {
        $rows += "archive=Xray-windows-64.zip"
        $rows += "archive_sha256=$ArchiveHash"
    }
    [System.IO.File]::WriteAllText($Provenance, (($rows -join "`r`n") + "`r`n"), $Utf8NoBom)
}

# RC3 is intentionally offline-friendly: the source ZIP already carries the
# Windows-tested 26.3.27 binary. Verify it byte-for-byte and do not replace it.
if ([string]::IsNullOrWhiteSpace($CandidatePath) -and (Test-Path -LiteralPath $Target)) {
    try {
        Assert-BinaryIdentity $Target
        Write-Provenance "validated-bundled-baseline" ""
        Write-Host "[OK] Existing Xray 26.3.27 stable baseline verified; no download needed." -ForegroundColor Green
        return
    }
    catch {
        Write-Host "Bundled Xray did not pass the RC3 identity gate; recovery preparation will be used." -ForegroundColor DarkYellow
    }
}

if ([string]::IsNullOrWhiteSpace($CandidatePath) -and (Test-Path -LiteralPath $LocalArchive)) {
    $CandidatePath = $LocalArchive
    Write-Host "Using offline Xray 26.3.27 archive next to source." -ForegroundColor Cyan
}

$TempRoot = Join-Path $env:TEMP ("GeniaProxy-Xray-26.3.27-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null

try {
    if ([string]::IsNullOrWhiteSpace($CandidatePath)) {
        $Archive = Join-Path $TempRoot "Xray-windows-64-v26.3.27.zip"
        Write-Host "Downloading official Xray 26.3.27 windows-64 recovery archive..." -ForegroundColor Cyan
        Invoke-WebRequest -Uri $DownloadUrl -OutFile $Archive -UseBasicParsing
        $SourceKind = "official-github-release-recovery"
    }
    else {
        $Archive = (Resolve-Path -LiteralPath $CandidatePath).Path
        if ([System.IO.Path]::GetExtension($Archive) -ne ".zip") {
            throw "Only an Xray 26.3.27 windows-64 ZIP archive is accepted."
        }
        $SourceKind = "offline-archive-recovery"
    }

    $ArchiveHash = Get-Hash $Archive
    $Extract = Join-Path $TempRoot "extract"
    Expand-Archive -LiteralPath $Archive -DestinationPath $Extract -Force
    $Selected = (Get-ChildItem -LiteralPath $Extract -Filter "xray.exe" -File -Recurse | Select-Object -First 1).FullName
    if ([string]::IsNullOrWhiteSpace($Selected)) {
        throw "xray.exe was not found in the archive."
    }

    # Hash and size are checked before the extracted executable is run.
    $selectedItem = Get-Item -LiteralPath $Selected
    $selectedHash = Get-Hash $Selected
    if ($selectedItem.Length -ne $ExpectedExeSize -or $selectedHash -ne $ExpectedExeSha256) {
        throw "Recovery archive does not contain the pinned Xray 26.3.27 executable. SHA256: $selectedHash"
    }

    Assert-BinaryIdentity $Selected
    New-Item -ItemType Directory -Path $EngineDirectory -Force | Out-Null
    Copy-Item -LiteralPath $Selected -Destination $Target -Force
    Assert-BinaryIdentity $Target
    Write-Provenance $SourceKind $ArchiveHash
    Write-Host "[OK] engine\xray.exe restored to the validated Xray 26.3.27 baseline." -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $TempRoot) {
        Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
