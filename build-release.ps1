param([string]$BasePackageRoot = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$version = '1.4.2'
$senderVersion = '1.0.5'
$sourceRoot = Join-Path $PSScriptRoot 'AirSpan-source'
$outputRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "release\v$version"))
$packageRoot = Join-Path $outputRoot 'package'
$asarRoot = Join-Path $outputRoot 'asar'
$senderSourceRoot = Join-Path $outputRoot 'sender-source'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Reset-ReleaseDirectory([string]$Target) {
    $absolute = [IO.Path]::GetFullPath($Target)
    if (-not $absolute.StartsWith($outputRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release cleanup target is outside the output directory: $absolute"
    }
    if (Test-Path -LiteralPath $absolute) {
        $item = Get-Item -LiteralPath $absolute -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing to clean a linked output directory.' }
        Write-Output "Verified generated cleanup target: $absolute"
        Remove-Item -LiteralPath $absolute -Recurse -Force
    }
    New-Item -ItemType Directory -Path $absolute -Force | Out-Null
}

function Zip-Directory([string]$Directory, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($Directory, $Destination, [IO.Compression.CompressionLevel]::Optimal, $false)
}

$baseAirPlay = Join-Path ([IO.Path]::GetFullPath($BasePackageRoot)) 'AirPlay'
$baseAsar = Join-Path $baseAirPlay 'Sender\resources\app.asar'
if (-not (Test-Path -LiteralPath $baseAsar)) { throw 'BasePackageRoot must contain the complete release AirPlay/Sender runtime.' }
if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot 'web-dist\index.html'))) { throw 'Build the sender frontend first.' }
$asarPackage = Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'node_modules\.pnpm') -Directory -Filter '@electron+asar@*' | Select-Object -First 1
if (-not $asarPackage) { throw 'Install sender dependencies with pnpm install --frozen-lockfile --ignore-scripts.' }
$asarCli = Join-Path $asarPackage.FullName 'node_modules\@electron\asar\bin\asar.js'

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $sourceRoot 'tools\audio-endpoint-monitor\build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Mute monitor build failed.' }

Reset-ReleaseDirectory $packageRoot
Reset-ReleaseDirectory $asarRoot
Reset-ReleaseDirectory $senderSourceRoot
Copy-Item -LiteralPath $baseAirPlay -Destination (Join-Path $packageRoot 'AirPlay') -Recurse
foreach ($name in @('WifiScreen.exe','README.txt','VALIDATION.txt')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $packageRoot
}
$nativeSourceRoot = Join-Path $packageRoot 'source'
New-Item -ItemType Directory -Path $nativeSourceRoot -Force | Out-Null
foreach ($name in @('WifiScreen.cs','MainForm.cs','Streaming.cs','StreamingTests.cs','app.manifest','build.ps1','browser-viewer.test.cjs')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $nativeSourceRoot
}

& node $asarCli extract $baseAsar $asarRoot
if ($LASTEXITCODE -ne 0) { throw 'Runtime ASAR extraction failed.' }
# Only retain original production dependencies; every application module and
# frontend asset is copied fresh from the source being released.
foreach ($folder in @('electron','web-dist')) {
    $oldFolder = Join-Path $asarRoot $folder
    if (Test-Path -LiteralPath $oldFolder) {
        $absolute = [IO.Path]::GetFullPath($oldFolder)
        if (-not $absolute.StartsWith($asarRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid ASAR cleanup path.' }
        if (((Get-Item -LiteralPath $absolute).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing linked ASAR directory.' }
        Write-Output "Verified generated cleanup target: $absolute"
        Remove-Item -LiteralPath $absolute -Recurse -Force
    }
}
New-Item -ItemType Directory -Path (Join-Path $asarRoot 'electron') -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'electron') -File |
    Where-Object { $_.Name -notlike '*.test.mjs' } |
    Copy-Item -Destination (Join-Path $asarRoot 'electron')
Copy-Item -LiteralPath (Join-Path $sourceRoot 'web-dist') -Destination $asarRoot -Recurse
$packageFile = Join-Path $asarRoot 'package.json'
$packageData = Get-Content -LiteralPath $packageFile -Raw | ConvertFrom-Json
$packageData.version = $senderVersion
[IO.File]::WriteAllText($packageFile, ($packageData | ConvertTo-Json -Depth 20), $utf8)
$resourceRoot = Join-Path $packageRoot 'AirPlay\Sender\resources'
& node $asarCli pack $asarRoot (Join-Path $resourceRoot 'app.asar')
if ($LASTEXITCODE -ne 0) { throw 'Runtime ASAR packaging failed.' }
Copy-Item -LiteralPath (Join-Path $sourceRoot 'electron\preload.cjs') -Destination $resourceRoot -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot 'tools\audio-endpoint-monitor\audio-endpoint-monitor.exe') -Destination $resourceRoot -Force

# Public source archive: no dependencies, generated frontend, executables,
# local validation reports, profiles, logs or screenshots from this workspace.
$sourcePrefix = $sourceRoot.TrimEnd('\') + '\'
foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Force) {
    $relative = $file.FullName.Substring($sourcePrefix.Length)
    if ($relative -match '^(node_modules|web-dist|\.tanstack|\.git)\\' -or
        $file.Extension -in @('.exe','.log','.dat') -or
        $relative -eq 'tools\audio-endpoint-monitor\VALIDATION.txt') { continue }
    $target = Join-Path $senderSourceRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
$fixedSourceZip = Join-Path $packageRoot 'AirPlay\AirSpan-fixed-source.zip'
Zip-Directory $senderSourceRoot $fixedSourceZip
$fixNotes = "WiFi Screen $version / modified AirSpan $senderVersion`r`n`r`n" +
    (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'RELEASE-NOTES-1.4.2.md') -Raw) +
    "`r`nExact modified sender source: AirSpan-fixed-source.zip.`r`n" +
    "Native source: ../source/. Keep the complete Sender runtime and resources together.`r`n" +
    "Rebuild instructions: AirSpan source BUILD-WINDOWS.md and repository build-release.ps1.`r`n" +
    "Original upstream/helper source and all third-party notices remain included.`r`n"
[IO.File]::WriteAllText((Join-Path $packageRoot 'AirPlay\FIX-NOTES.txt'), $fixNotes, $utf8)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AIRPLAY-INTEGRATION.txt') -Destination (Join-Path $packageRoot 'AirPlay\INTEGRATION.txt') -Force

$hashLines = foreach ($relative in @('WifiScreen.exe','AirPlay\Sender\resources\app.asar','AirPlay\Sender\resources\audio-endpoint-monitor.exe','AirPlay\AirSpan-fixed-source.zip')) {
    $digest = Get-FileHash -LiteralPath (Join-Path $packageRoot $relative) -Algorithm SHA256
    "$($digest.Hash)  $relative"
}
[IO.File]::WriteAllText((Join-Path $packageRoot 'SHA256.txt'), (($hashLines -join "`n") + "`n"), $utf8)

$archivePath = Join-Path $outputRoot 'WifiScreen.zip'
Zip-Directory $packageRoot $archivePath
$archiveHash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
[IO.File]::WriteAllText((Join-Path $outputRoot 'SHA256SUMS.txt'), "$($archiveHash.Hash)  WifiScreen.zip`n", $utf8)
Write-Output "Release ready: $archivePath"
Write-Output "SHA-256: $($archiveHash.Hash)"
