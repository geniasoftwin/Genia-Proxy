[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$EngineDirectory = Join-Path $Root "engine"
$PrepareSingBox = Join-Path $Root "Prepare-SingBox-1.14.1.ps1"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$XrayUrl = "https://github.com/XTLS/Xray-core/releases/download/v26.3.27/Xray-windows-64.zip"
$XrayArchiveSha256 = "D004C39288CE9ADA487C6F398C7C545F7D749E44BDFDD59DBC9F865AFBA4E1AD"
$XrayExeSha256 = "15C2D007954AC53BA69B80EC91242786B3C0B71D52649165B4CA1D5CC96EF8F1"

$WintunUrl = "https://www.wintun.net/builds/wintun-0.14.1.zip"
$WintunArchiveSha256 = "07C256185D6EE3652E09FA55C0B673E2624B565E02C4B9091C79CA7D2F24EF51"
$WintunDllSha256 = "E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE"

function Get-Hash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Write-Checksum([string]$Path, [string]$Hash, [string]$Name) {
    [System.IO.File]::WriteAllText($Path, "$Hash  $Name`r`n", $Utf8NoBom)
}

function Install-FromArchive {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$ArchiveSha256,
        [Parameter(Mandatory = $true)][string]$MemberName,
        [Parameter(Mandatory = $true)][string]$ExpectedMemberSha256,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$ChecksumPath,
        [string]$RequiredRelativeSuffix = ""
    )

    if (Test-Path -LiteralPath $TargetPath) {
        if ((Get-Hash $TargetPath) -eq $ExpectedMemberSha256) {
            Write-Checksum $ChecksumPath $ExpectedMemberSha256 ([IO.Path]::GetFileName($TargetPath))
            Write-Host "[OK] $Name already verified." -ForegroundColor Green
            return
        }
    }

    $TempRoot = Join-Path $env:TEMP ("GeniaProxy-engine-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null
    try {
        $Archive = Join-Path $TempRoot "package.zip"
        Write-Host "Downloading $Name from official upstream..." -ForegroundColor Cyan
        Invoke-WebRequest -Uri $Url -OutFile $Archive -UseBasicParsing
        $archiveHash = Get-Hash $Archive
        if ($archiveHash -ne $ArchiveSha256) {
            throw "$Name archive SHA-256 mismatch."
        }

        $Extract = Join-Path $TempRoot "extract"
        Expand-Archive -LiteralPath $Archive -DestinationPath $Extract -Force
        $Candidates = @(Get-ChildItem -LiteralPath $Extract -Recurse -File -Filter $MemberName)
        if (-not [string]::IsNullOrWhiteSpace($RequiredRelativeSuffix)) {
            $normalizedSuffix = $RequiredRelativeSuffix.Replace('/', '\\')
            $Candidates = @($Candidates | Where-Object {
                $_.FullName.Replace('/', '\\').EndsWith($normalizedSuffix, [StringComparison]::OrdinalIgnoreCase)
            })
        }
        if ($Candidates.Count -ne 1) {
            throw "$Name expected member was not uniquely identified."
        }

        $Selected = $Candidates[0].FullName
        if ((Get-Hash $Selected) -ne $ExpectedMemberSha256) {
            throw "$Name extracted binary SHA-256 mismatch."
        }

        New-Item -ItemType Directory -Path (Split-Path -Parent $TargetPath) -Force | Out-Null
        $TempTarget = $TargetPath + "." + [Guid]::NewGuid().ToString("N") + ".tmp"
        try {
            Copy-Item -LiteralPath $Selected -Destination $TempTarget -Force
            if ((Get-Hash $TempTarget) -ne $ExpectedMemberSha256) {
                throw "$Name changed during staging."
            }
            Move-Item -LiteralPath $TempTarget -Destination $TargetPath -Force
        }
        finally {
            if (Test-Path -LiteralPath $TempTarget) {
                Remove-Item -LiteralPath $TempTarget -Force -ErrorAction SilentlyContinue
            }
        }

        Write-Checksum $ChecksumPath $ExpectedMemberSha256 ([IO.Path]::GetFileName($TargetPath))
        Write-Host "[OK] $Name verified and installed." -ForegroundColor Green
    }
    finally {
        if (Test-Path -LiteralPath $TempRoot) {
            Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if (-not (Test-Path -LiteralPath $PrepareSingBox)) {
    throw "Prepare-SingBox-1.14.1.ps1 was not found."
}

New-Item -ItemType Directory -Path $EngineDirectory -Force | Out-Null

& $PrepareSingBox
if ($LASTEXITCODE -ne 0) {
    throw "sing-box preparation failed with exit code $LASTEXITCODE."
}

Install-FromArchive `
    -Name "Xray-core 26.3.27" `
    -Url $XrayUrl `
    -ArchiveSha256 $XrayArchiveSha256 `
    -MemberName "xray.exe" `
    -ExpectedMemberSha256 $XrayExeSha256 `
    -TargetPath (Join-Path $EngineDirectory "xray.exe") `
    -ChecksumPath (Join-Path $EngineDirectory "xray.sha256")

Install-FromArchive `
    -Name "Wintun 0.14.1 amd64" `
    -Url $WintunUrl `
    -ArchiveSha256 $WintunArchiveSha256 `
    -MemberName "wintun.dll" `
    -ExpectedMemberSha256 $WintunDllSha256 `
    -TargetPath (Join-Path $EngineDirectory "wintun.dll") `
    -ChecksumPath (Join-Path $EngineDirectory "wintun.sha256") `
    -RequiredRelativeSuffix "wintun\bin\amd64\wintun.dll"

Write-Host "[OK] GeniaProxy engine set prepared." -ForegroundColor Green
