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
$PrepareSingBox = Join-Path $Root "Prepare-SingBox-1.14.1.ps1"
$PrepareXray = Join-Path $Root "Prepare-Xray-26.3.27.ps1"
$PinnedSingBoxArchiveSha256 = "5197F16D492D93202DC623622149A6ED040F8ECA263128F91D603F2B901BAA89"
$PinnedXrayExeSha256 = "15C2D007954AC53BA69B80EC91242786B3C0B71D52649165B4CA1D5CC96EF8F1"
$BrowserSwitcherDirectory = Join-Path $Root "browser-integration\Genia-Proxy-Switcher"
$BrowserSwitcherManifest = Join-Path $BrowserSwitcherDirectory "manifest.json"
$ProtocolLabBoundary = Join-Path $Root "PROTOCOL-LAB-BOUNDARY.md"
$ProtocolLabStatus = Join-Path $Root "PROTOCOL-LAB-ALPHA2-STATUS.md"
$WhitelistBoundary = Join-Path $Root "PROTOCOL-LAB-WHITELIST-BOUNDARY.md"
$XrayExperimentalBoundary = Join-Path $Root "PROTOCOL-LAB-XRAY-EXPERIMENTAL-BOUNDARY.md"
$StableEngineSmoke = Join-Path $Root "tests\windows\Test-StableEngineSmoke.ps1"
$ProtocolLabFailureIsolation = Join-Path $Root "tests\windows\Test-ProtocolLabFailureIsolation.ps1"
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
    "GeniaProxy-{0}-Alpha2-ProtocolLab-portable-{1}.zip" -f $Version, $Runtime
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

if (-not (Test-Path -LiteralPath $PrepareSingBox)) {
    throw "Prepare-SingBox-1.14.1.ps1 was not found."
}
if (-not (Test-Path -LiteralPath $PrepareXray)) {
    throw "Prepare-Xray-26.3.27.ps1 was not found."
}

Write-Host "Preparing pinned sing-box 1.14.1 Engine Refresh baseline..." -ForegroundColor Cyan
& $PrepareSingBox
Write-Host "Verifying pinned Xray 26.3.27 Engine Refresh RC3 stable baseline..." -ForegroundColor Cyan
& $PrepareXray

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

if ($ExpectedHash -ne $ActualHash) {
    throw "The SHA-256 checksum of engine\sing-box.exe does not match."
}

$SingBoxVersionOutput = @(& $Engine version 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "sing-box version check failed."
}
if (-not ($SingBoxVersionOutput | Where-Object { $_ -match '^sing-box version\s+1\.14\.1(?:\s|$)' })) {
    throw "engine\sing-box.exe is not sing-box 1.14.1."
}
$SingBoxProvenance = Join-Path $Root "engine\sing-box.provenance.txt"
if (-not (Test-Path -LiteralPath $SingBoxProvenance)) {
    throw "engine\sing-box.provenance.txt was not found."
}
$ProvenanceText = Get-Content -LiteralPath $SingBoxProvenance -Raw -Encoding UTF8
if (-not $ProvenanceText.Contains($PinnedSingBoxArchiveSha256)) {
    throw "sing-box provenance does not contain the approved 1.14.1 archive SHA256."
}
Write-Host "[OK] sing-box 1.14.1 version + archive provenance gate passed." -ForegroundColor Green

$ExpectedXrayHash = ((Get-Content -LiteralPath $XrayChecksum -Raw) -split "\s+")[0].Trim()
$ActualXrayHash = (Get-FileHash -LiteralPath $XrayEngine -Algorithm SHA256).Hash

if ([string]::IsNullOrWhiteSpace($ExpectedXrayHash)) {
    throw "engine\xray.sha256 is empty or invalid."
}

if ($ExpectedXrayHash -ne $ActualXrayHash) {
    throw "The SHA-256 checksum of engine\xray.exe does not match."
}

$XrayVersionOutput = @(& $XrayEngine version 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "Xray version check failed."
}
if (-not ($XrayVersionOutput | Where-Object { $_ -match '^Xray\s+26\.3\.27(?:\s|$)' })) {
    throw "engine\xray.exe is not Xray 26.3.27."
}
$XrayProvenance = Join-Path $Root "engine\xray.provenance.txt"
if (-not (Test-Path -LiteralPath $XrayProvenance)) {
    throw "engine\xray.provenance.txt was not found."
}
$XrayProvenanceText = Get-Content -LiteralPath $XrayProvenance -Raw -Encoding UTF8
if (-not $XrayProvenanceText.Contains("version=26.3.27") -or
    -not $XrayProvenanceText.Contains("exe_sha256=$PinnedXrayExeSha256")) {
    throw "Xray provenance does not contain the approved 26.3.27 executable identity."
}
if ($ActualXrayHash.ToUpperInvariant() -ne $PinnedXrayExeSha256) {
    throw "engine\xray.exe is not the pinned RC3 Xray 26.3.27 executable."
}
Write-Host "[OK] Xray 26.3.27 version + executable provenance gate passed." -ForegroundColor Green

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
if ($SwitcherManifestJson.name -ne "Genia Proxy Switcher Direct" -or $SwitcherManifestJson.version -ne "5.6.0.7") {
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

if (-not (Test-Path -LiteralPath $ProtocolLabBoundary)) {
    throw "PROTOCOL-LAB-BOUNDARY.md was not found."
}
if (-not (Test-Path -LiteralPath $ProtocolLabStatus)) {
    throw "PROTOCOL-LAB-ALPHA2-STATUS.md was not found."
}
if (-not (Test-Path -LiteralPath $WhitelistBoundary)) {
    throw "PROTOCOL-LAB-WHITELIST-BOUNDARY.md was not found."
}
if (-not (Test-Path -LiteralPath $XrayExperimentalBoundary)) {
    throw "PROTOCOL-LAB-XRAY-EXPERIMENTAL-BOUNDARY.md was not found."
}
if (-not (Test-Path -LiteralPath $StableEngineSmoke)) {
    throw "tests\windows\Test-StableEngineSmoke.ps1 was not found."
}
if (-not (Test-Path -LiteralPath $ProtocolLabFailureIsolation)) {
    throw "tests\windows\Test-ProtocolLabFailureIsolation.ps1 was not found."
}

$StartupSource = Get-Content -LiteralPath (Join-Path $Root "Program.cs") -Raw -Encoding UTF8
$SingleInstanceSource = Get-Content -LiteralPath (Join-Path $Root "Services\SingleInstanceService.cs") -Raw -Encoding UTF8
$StartupJournalSource = Get-Content -LiteralPath (Join-Path $Root "Services\StartupJournal.cs") -Raw -Encoding UTF8
$ProtocolLabSource = Get-Content -LiteralPath (Join-Path $Root "Services\ProtocolLabFeatureCatalog.cs") -Raw -Encoding UTF8
$ProtocolLabAuditSource = Get-Content -LiteralPath (Join-Path $Root "Services\ProtocolLabSelectionAudit.cs") -Raw -Encoding UTF8
$MainWindowSource = Get-Content -LiteralPath (Join-Path $Root "MainWindow.xaml.cs") -Raw -Encoding UTF8
$ProtocolLabSafetySource = Get-Content -LiteralPath (Join-Path $Root "Services\ProtocolLabConfigSafetyService.cs") -Raw -Encoding UTF8
$WhitelistBoundarySource = Get-Content -LiteralPath (Join-Path $Root "Services\ProtocolLabWhitelistModeBoundary.cs") -Raw -Encoding UTF8
$XrayExperimentalBoundarySource = Get-Content -LiteralPath (Join-Path $Root "Services\ProtocolLabXrayExperimentalBoundary.cs") -Raw -Encoding UTF8
$TestsSource = Get-Content -LiteralPath (Join-Path $Root "tests\GeniaProxy.Tests\Program.cs") -Raw -Encoding UTF8

foreach ($RequiredMarker in @(
    'startup-journal.jsonl',
    'TryBecomePrimaryAfterFailedActivation',
    'RunPreUiTunRecovery',
    'startup.safeTakeoverSucceeded'
)) {
    if (-not $StartupSource.Contains($RequiredMarker)) {
        throw ("FIX4 startup marker missing: {0}" -f $RequiredMarker)
    }
}

foreach ($RequiredMarker in @(
    'NamedPipeServerStream',
    'ActivationAck',
    'TryActivatePrimary'
)) {
    if (-not $SingleInstanceSource.Contains($RequiredMarker)) {
        throw ("FIX4 single-instance marker missing: {0}" -f $RequiredMarker)
    }
}

if (-not $StartupJournalSource.Contains('FileShare.ReadWrite | FileShare.Delete')) {
    throw "FIX4 startup journal is not live-readable."
}

foreach ($RequiredMarker in @(
    'anytls',
    'tuic',
    'snell',
    'whitelist-mode',
    'xray-experimental',
    'EnabledByDefault'
)) {
    if (-not $ProtocolLabSource.Contains($RequiredMarker)) {
        throw ("Protocol Lab boundary marker missing: {0}" -f $RequiredMarker)
    }
}

foreach ($RequiredMarker in @(
    'Startup journal live-readable',
    'Single-instance activation ACK',
    'Protocol Lab Alpha 1 boundary',
    'Protocol Lab Alpha 2 capability model',
    'Protocol Lab AnyTLS selection gate',
    'Protocol Lab TUIC selection gate',
    'Protocol Lab Snell selection gate',
    'Protocol Lab whitelist boundary',
    'Protocol Lab Xray experimental boundary',
    'Protocol Lab selection audit',
    'Protocol Lab local-proxy isolation'
)) {
    if (-not $TestsSource.Contains($RequiredMarker)) {
        throw ("Alpha 2 regression test marker missing: {0}" -f $RequiredMarker)
    }
}

foreach ($RequiredMarker in @(
    'Protocol Lab selection',
    'support=',
    'enabledByDefault=',
    'SelectionLogged',
    'RequireSelectableAndLog'
)) {
    if (-not $ProtocolLabAuditSource.Contains($RequiredMarker)) {
        throw ("Protocol Lab audit marker missing: {0}" -f $RequiredMarker)
    }
}

foreach ($RequiredMarker in @(
    'ProtocolLabSelectionAudit.SelectionLogged +=',
    'ProtocolLabSelectionAudit.SelectionLogged -='
)) {
    if (-not $MainWindowSource.Contains($RequiredMarker)) {
        throw ("Protocol Lab UI audit wiring marker missing: {0}" -f $RequiredMarker)
    }
}

foreach ($RequiredMarker in @(
    'RequireSelectable',
    'set_system_proxy',
    '127.0.0.1'
)) {
    if (-not $ProtocolLabSafetySource.Contains($RequiredMarker)) {
        throw ("Protocol Lab config safety marker missing: {0}" -f $RequiredMarker)
    }
}

if (-not $WhitelistBoundarySource.Contains('DesignOnly') -or
    -not $XrayExperimentalBoundarySource.Contains('DesignOnly')) {
    throw "Alpha 2 design-only boundary marker missing."
}

Write-Host "[OK] Alpha 2 startup recovery, capability, audit and isolation markers verified." -ForegroundColor Green

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
$PublishedXrayProvenance = Join-Path $PublishDirectory "engine\xray.provenance.txt"
$PublishedWintun = Join-Path $PublishDirectory "engine\wintun.dll"
$PublishedWintunChecksum = Join-Path $PublishDirectory "engine\wintun.sha256"
$PublishedNotices = Join-Path $PublishDirectory "THIRD-PARTY-NOTICES.md"
$PublishedProtocolLabBoundary = Join-Path $PublishDirectory "PROTOCOL-LAB-BOUNDARY.md"
$PublishedProtocolLabStatus = Join-Path $PublishDirectory "PROTOCOL-LAB-ALPHA2-STATUS.md"
$PublishedWhitelistBoundary = Join-Path $PublishDirectory "PROTOCOL-LAB-WHITELIST-BOUNDARY.md"
$PublishedXrayExperimentalBoundary = Join-Path $PublishDirectory "PROTOCOL-LAB-XRAY-EXPERIMENTAL-BOUNDARY.md"
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

if (-not (Test-Path -LiteralPath $PublishedXrayProvenance)) {
    throw "Published engine\xray.provenance.txt was not found."
}
$PublishedXrayProvenanceText = Get-Content -LiteralPath $PublishedXrayProvenance -Raw -Encoding UTF8
if (-not $PublishedXrayProvenanceText.Contains("version=26.3.27") -or
    -not $PublishedXrayProvenanceText.Contains("exe_sha256=$PinnedXrayExeSha256")) {
    throw "Published Xray provenance does not contain the approved 26.3.27 executable identity."
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

if (-not (Test-Path -LiteralPath $PublishedProtocolLabBoundary)) {
    throw "Published PROTOCOL-LAB-BOUNDARY.md was not found."
}
if (-not (Test-Path -LiteralPath $PublishedProtocolLabStatus)) {
    throw "Published PROTOCOL-LAB-ALPHA2-STATUS.md was not found."
}
if (-not (Test-Path -LiteralPath $PublishedWhitelistBoundary)) {
    throw "Published PROTOCOL-LAB-WHITELIST-BOUNDARY.md was not found."
}
if (-not (Test-Path -LiteralPath $PublishedXrayExperimentalBoundary)) {
    throw "Published PROTOCOL-LAB-XRAY-EXPERIMENTAL-BOUNDARY.md was not found."
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
    $PublishedSwitcherManifestJson.version -ne "5.6.0.7") {
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
Write-Host ("Alpha 2 status: {0}" -f (Test-Path -LiteralPath $PublishedProtocolLabStatus))
Write-Host ("ZIP archive:    {0}" -f (Test-Path -LiteralPath $Archive))
