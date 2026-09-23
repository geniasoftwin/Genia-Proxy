param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root "GeniaProxy.csproj"
$TestsProject = Join-Path $Root "tests\GeniaProxy.Tests\GeniaProxy.Tests.csproj"
$Engine = Join-Path $Root "engine\sing-box.exe"
$EngineChecksum = Join-Path $Root "engine\sing-box.sha256"
$XrayEngine = Join-Path $Root "engine\xray.exe"
$XrayChecksum = Join-Path $Root "engine\xray.sha256"
$Wintun = Join-Path $Root "engine\wintun.dll"
$WintunChecksum = Join-Path $Root "engine\wintun.sha256"
$PrepareEngines = Join-Path $Root "Prepare-Engines.ps1"
$PinnedSingBoxSha256 = "AAD0EDE010EAFA7B277E520464F3A66FDE820103D737EFF739F40F3CC9451DCC"
$BrowserSwitcherDirectory = Join-Path $Root "browser-integration\Genia-Proxy-Switcher"
$BrowserSwitcherManifest = Join-Path $BrowserSwitcherDirectory "manifest.json"
$PublishDirectory = Join-Path $Root "publish"
$BuildDirectory = Join-Path $Root "bin"
$IntermediateDirectory = Join-Path $Root "obj"

[xml]$ProjectXml = Get-Content -LiteralPath $Project -Raw
$Version = @($ProjectXml.Project.PropertyGroup.Version)[0]
$FileVersion = @($ProjectXml.Project.PropertyGroup.FileVersion)[0]
$AssemblyVersion = @($ProjectXml.Project.PropertyGroup.AssemblyVersion)[0]

if ([string]::IsNullOrWhiteSpace($Version) -or
    [string]::IsNullOrWhiteSpace($FileVersion) -or
    [string]::IsNullOrWhiteSpace($AssemblyVersion)) {
    throw "Version metadata is not fully specified in GeniaProxy.csproj."
}

$Archive = Join-Path $Root (
    "GeniaProxy-portable-{0}-v{1}.zip" -f $Runtime, $Version
)

function Assert-LastExitCode {
    param(
        [string]$Step
    )

    if ($LASTEXITCODE -ne 0) {
        throw ("{0} failed with exit code {1}." -f $Step, $LASTEXITCODE)
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found. Install .NET SDK 10.0.400 or newer."
}

if (-not (Test-Path -LiteralPath $Project)) {
    throw "GeniaProxy.csproj was not found."
}

if (-not (Test-Path -LiteralPath $TestsProject)) {
    throw "The test project was not found."
}

if (-not (Test-Path -LiteralPath $PrepareEngines)) {
    throw "Prepare-Engines.ps1 was not found."
}

Write-Host "Preparing pinned engine set..." -ForegroundColor Cyan
& $PrepareEngines

if (-not (Test-Path -LiteralPath $Engine)) {
    throw "engine\sing-box.exe was not found."
}

if (-not (Test-Path -LiteralPath $EngineChecksum)) {
    throw "engine\sing-box.sha256 was not found."
}

if (-not (Test-Path -LiteralPath $XrayEngine)) {
    throw "engine\xray.exe was not found."
}

if (-not (Test-Path -LiteralPath $XrayChecksum)) {
    throw "engine\xray.sha256 was not found."
}

if (-not (Test-Path -LiteralPath $Wintun)) {
    throw "engine\wintun.dll was not found."
}

if (-not (Test-Path -LiteralPath $WintunChecksum)) {
    throw "engine\wintun.sha256 was not found."
}

$ExpectedHash = ((Get-Content -LiteralPath $EngineChecksum -Raw) -split "\s+")[0].Trim()
$ActualHash = (Get-FileHash -LiteralPath $Engine -Algorithm SHA256).Hash

if ([string]::IsNullOrWhiteSpace($ExpectedHash)) {
    throw "engine\sing-box.sha256 is empty or invalid."
}

if ($ExpectedHash.ToUpperInvariant() -ne $PinnedSingBoxSha256) {
    throw "engine\sing-box.sha256 is not pinned to approved sing-box 1.14.0."
}

if ($ExpectedHash -ne $ActualHash) {
    throw "The SHA-256 checksum of engine\sing-box.exe does not match."
}

$ExpectedXrayHash = ((Get-Content -LiteralPath $XrayChecksum -Raw) -split "\s+")[0].Trim()
$ActualXrayHash = (Get-FileHash -LiteralPath $XrayEngine -Algorithm SHA256).Hash

if ([string]::IsNullOrWhiteSpace($ExpectedXrayHash)) {
    throw "engine\xray.sha256 is empty or invalid."
}

if ($ExpectedXrayHash -ne $ActualXrayHash) {
    throw "The SHA-256 checksum of engine\xray.exe does not match."
}

$ExpectedWintunHash = ((Get-Content -LiteralPath $WintunChecksum -Raw) -split "\s+")[0].Trim()
$ActualWintunHash = (Get-FileHash -LiteralPath $Wintun -Algorithm SHA256).Hash

if ([string]::IsNullOrWhiteSpace($ExpectedWintunHash)) {
    throw "engine\wintun.sha256 is empty or invalid."
}

if ($ExpectedWintunHash -ne $ActualWintunHash) {
    throw "The SHA-256 checksum of engine\wintun.dll does not match."
}

$RequiredSwitcherFiles = @(
    "manifest.json", "background.js", "popup.html", "popup.js", "off.png", "on.png"
)
foreach ($Name in $RequiredSwitcherFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $BrowserSwitcherDirectory $Name))) {
        throw ("Browser Integration Switcher file missing: {0}" -f $Name)
    }
}
$SwitcherManifestJson = Get-Content -LiteralPath $BrowserSwitcherManifest -Raw -Encoding UTF8 | ConvertFrom-Json
if ($SwitcherManifestJson.name -ne "Genia Proxy Switcher Direct" -or $SwitcherManifestJson.version -ne "5.6.0.6") {
    throw "Browser Integration Switcher manifest is not 5.6.0 Stable Direct."
}
$SwitcherBackgroundPath = Join-Path $BrowserSwitcherDirectory "background.js"
$SwitcherBackgroundText = Get-Content -LiteralPath $SwitcherBackgroundPath -Raw -Encoding UTF8
$RequiredDirectBridgeMarkers = @(
    'managerBridgeRefreshInFlight',
    'requestManagerBridgeRefresh(',
    'scheduleManagerRouteReconcile(',
    'queueProxyErrorState(',
    'MANAGER_POLL_TRANSITION_MS: 350'
)
foreach ($Marker in $RequiredDirectBridgeMarkers) {
    if (-not $SwitcherBackgroundText.Contains($Marker)) {
        throw ("Switcher Direct queue/race marker missing: {0}" -f $Marker)
    }
}
if ($SwitcherBackgroundText -match 'enqueue\(\s*\(\)\s*=>\s*managerWatchdogTick\(') {
    throw "Switcher Direct regression: Direct Bridge watchdog is back inside operationQueue."
}
if ($SwitcherBackgroundText -match 'enqueue\([^\r\n]{0,200}refreshManagerBridge\(') {
    throw "Switcher Direct regression: Direct Bridge refresh is back inside operationQueue."
}
Write-Host "[OK] Browser Integration Direct Bridge Switcher Stable queue/race markers verified." -ForegroundColor Green

$ForbiddenNativeHostPaths = @(
    (Join-Path $Root "browser-integration\native-host"),
    (Join-Path $Root "browser-integration\native-host-source"),
    (Join-Path $Root "browser-integration\MANAGER_BRIDGE_PROTOCOL.md")
)
foreach ($ForbiddenNativePath in $ForbiddenNativeHostPaths) {
    if (Test-Path -LiteralPath $ForbiddenNativePath) {
        throw ("Direct Bridge source unexpectedly contains legacy NativeHost asset: {0}" -f $ForbiddenNativePath)
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $Root "Services\BrowserDirectBridgeService.cs"))) {
    throw "BrowserDirectBridgeService.cs was not found."
}

if (Test-Path -LiteralPath $PublishDirectory) {
    Remove-Item -LiteralPath $PublishDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $BuildDirectory) {
    Remove-Item -LiteralPath $BuildDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $IntermediateDirectory) {
    Remove-Item -LiteralPath $IntermediateDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $Archive) {
    Remove-Item -LiteralPath $Archive -Force
}

Write-Host ("Source project: {0}" -f $Project) -ForegroundColor Cyan
Write-Host ("Building version: {0}" -f $Version) -ForegroundColor Cyan

Write-Host "Running tests..." -ForegroundColor Cyan
& dotnet run --project $TestsProject -c Release
Assert-LastExitCode "Tests"

Write-Host "Restoring dependencies..." -ForegroundColor Cyan
& dotnet restore $Project -r $Runtime
Assert-LastExitCode "Restore"

Write-Host "Publishing GeniaProxy..." -ForegroundColor Cyan
# Do not pass -o/PublishDir on the command line. dotnet normalizes even relative
# output paths to an absolute MSBuild property, and a comma in a parent folder
# (for example "Sources, Projects Windows") is then parsed as a property separator
# and causes MSB1006. Let the SDK use its default publish path, then copy the
# completed publish tree into the project's portable staging folder.
Push-Location $Root
try {
    & dotnet publish ".\GeniaProxy.csproj" `
        -c Release `
        -r $Runtime `
        --self-contained true `
        --no-restore `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        -p:FileVersion=$FileVersion `
        -p:AssemblyVersion=$AssemblyVersion
    Assert-LastExitCode "Publish"
}
finally {
    Pop-Location
}

$SdkPublishDirectory = Join-Path $Root (
    "bin\Release\net10.0-windows\{0}\publish" -f $Runtime
)

if (-not (Test-Path -LiteralPath $SdkPublishDirectory)) {
    throw ("SDK publish folder was not found: {0}" -f $SdkPublishDirectory)
}

New-Item -ItemType Directory -Path $PublishDirectory -Force | Out-Null
Copy-Item `
    -Path (Join-Path $SdkPublishDirectory "*") `
    -Destination $PublishDirectory `
    -Recurse `
    -Force

$PublishedApp = Join-Path $PublishDirectory "GeniaProxy.exe"
$PublishedEngine = Join-Path $PublishDirectory "engine\sing-box.exe"
$PublishedChecksum = Join-Path $PublishDirectory "engine\sing-box.sha256"
$PublishedXray = Join-Path $PublishDirectory "engine\xray.exe"
$PublishedXrayChecksum = Join-Path $PublishDirectory "engine\xray.sha256"
$PublishedWintun = Join-Path $PublishDirectory "engine\wintun.dll"
$PublishedWintunChecksum = Join-Path $PublishDirectory "engine\wintun.sha256"
$PublishedNotices = Join-Path $PublishDirectory "THIRD-PARTY-NOTICES.md"
$PublishedBrowserSwitcherDirectory = Join-Path $PublishDirectory "browser-integration\Genia-Proxy-Switcher"
$PublishedBrowserSwitcherManifest = Join-Path $PublishedBrowserSwitcherDirectory "manifest.json"
$ProfilesDirectory = Join-Path $PublishDirectory "data\profiles"

if (-not (Test-Path -LiteralPath $PublishedApp)) {
    throw "Published GeniaProxy.exe was not found."
}

$PublishedFileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
    $PublishedApp
).FileVersion

if ($PublishedFileVersion -ne $FileVersion) {
    $VersionError = (
        "Published GeniaProxy.exe has version {0}; expected {1}." -f `
        $PublishedFileVersion,
        $FileVersion
    )

    throw $VersionError
}

if (-not (Test-Path -LiteralPath $PublishedEngine)) {
    throw "Published engine\sing-box.exe was not found."
}

if (-not (Test-Path -LiteralPath $PublishedChecksum)) {
    throw "Published engine\sing-box.sha256 was not found."
}

if (-not (Test-Path -LiteralPath $PublishedXray)) {
    throw "Published engine\xray.exe was not found."
}

if (-not (Test-Path -LiteralPath $PublishedXrayChecksum)) {
    throw "Published engine\xray.sha256 was not found."
}

if (-not (Test-Path -LiteralPath $PublishedWintun)) {
    throw "Published engine\wintun.dll was not found."
}

if (-not (Test-Path -LiteralPath $PublishedWintunChecksum)) {
    throw "Published engine\wintun.sha256 was not found."
}

if (-not (Test-Path -LiteralPath $PublishedNotices)) {
    throw "Published THIRD-PARTY-NOTICES.md was not found."
}

if (-not (Test-Path -LiteralPath $PublishedBrowserSwitcherManifest)) {
    throw "Published Browser Integration Switcher manifest missing."
}

$PublishedEngineHash = (Get-FileHash -LiteralPath $PublishedEngine -Algorithm SHA256).Hash
if ($PublishedEngineHash -ne $ExpectedHash) {
    throw "The published sing-box.exe checksum does not match."
}

$PublishedXrayHash = (Get-FileHash -LiteralPath $PublishedXray -Algorithm SHA256).Hash
if ($PublishedXrayHash -ne $ExpectedXrayHash) {
    throw "The published xray.exe checksum does not match."
}

$PublishedWintunHash = (Get-FileHash -LiteralPath $PublishedWintun -Algorithm SHA256).Hash
if ($PublishedWintunHash -ne $ExpectedWintunHash) {
    throw "The published wintun.dll checksum does not match."
}

foreach ($Name in $RequiredSwitcherFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishedBrowserSwitcherDirectory $Name))) {
        throw ("Published Switcher file missing: {0}" -f $Name)
    }
}
$PublishedSwitcherManifestJson = Get-Content -LiteralPath $PublishedBrowserSwitcherManifest -Raw -Encoding UTF8 | ConvertFrom-Json
if ($PublishedSwitcherManifestJson.name -ne "Genia Proxy Switcher Direct" -or
    $PublishedSwitcherManifestJson.version -ne "5.6.0.6") {
    throw "Published Switcher Direct manifest mismatch."
}

$PublishedLegacyNativeHost = @(
    (Join-Path $PublishDirectory "browser-integration\native-host"),
    (Join-Path $PublishDirectory "browser-integration\native-host-source"),
    (Join-Path $PublishDirectory "browser-integration\MANAGER_BRIDGE_PROTOCOL.md")
)
foreach ($LegacyNativePath in $PublishedLegacyNativeHost) {
    if (Test-Path -LiteralPath $LegacyNativePath) {
        throw ("Published Direct Bridge package unexpectedly contains legacy NativeHost asset: {0}" -f $LegacyNativePath)
    }
}

New-Item -ItemType Directory -Path $ProfilesDirectory -Force | Out-Null

$ForbiddenFiles = @(
    (Join-Path $PublishDirectory "data\settings.json"),
    (Join-Path $PublishDirectory "data\system-proxy-backup.json"),
    (Join-Path $PublishDirectory "data\tun-network-backup.json"),
    (Join-Path $PublishDirectory "engine\sing-box.backup.exe"),
    (Join-Path $PublishDirectory "engine\xray.backup.exe")
)

foreach ($ForbiddenFile in $ForbiddenFiles) {
    if (Test-Path -LiteralPath $ForbiddenFile) {
        throw ("A forbidden file was included in publish: {0}" -f $ForbiddenFile)
    }
}

$PublishedProfiles = Get-ChildItem `
    -LiteralPath $ProfilesDirectory `
    -Filter "*.json" `
    -File `
    -ErrorAction SilentlyContinue

if ($PublishedProfiles) {
    throw "User profiles were included in publish."
}

Write-Host "Creating ZIP archive..." -ForegroundColor Cyan
Compress-Archive `
    -Path (Join-Path $PublishDirectory "*") `
    -DestinationPath $Archive `
    -CompressionLevel Optimal

if (-not (Test-Path -LiteralPath $Archive)) {
    throw "The ZIP archive was not created."
}

Write-Host ""
Write-Host "Build completed." -ForegroundColor Green
Write-Host ("Publish folder: {0}" -f $PublishDirectory)
Write-Host ("ZIP archive:    {0}" -f $Archive)
Write-Host ""
Write-Host "Checks:" -ForegroundColor Cyan
Write-Host ("GeniaProxy.exe: {0}" -f (Test-Path -LiteralPath $PublishedApp))
Write-Host ("File version:   {0}" -f $PublishedFileVersion)
Write-Host ("sing-box.exe:   {0}" -f (Test-Path -LiteralPath $PublishedEngine))
Write-Host ("xray.exe:       {0}" -f (Test-Path -LiteralPath $PublishedXray))
Write-Host ("wintun.dll:     {0}" -f (Test-Path -LiteralPath $PublishedWintun))
Write-Host ("Direct Bridge code: {0}" -f (Test-Path -LiteralPath (Join-Path $Root "Services\BrowserDirectBridgeService.cs")))
Write-Host ("Switcher Direct 5.6.0 Stable: {0}" -f (Test-Path -LiteralPath $PublishedBrowserSwitcherManifest))
Write-Host ("Notices:        {0}" -f (Test-Path -LiteralPath $PublishedNotices))
Write-Host ("ZIP archive:    {0}" -f (Test-Path -LiteralPath $Archive))
