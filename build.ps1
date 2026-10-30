$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$destination = Join-Path $PSScriptRoot 'WifiScreen.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/out:$destination" "/win32manifest:$PSScriptRoot\app.manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll "$PSScriptRoot\WifiScreen.cs" "$PSScriptRoot\MainForm.cs" "$PSScriptRoot\Streaming.cs" "$PSScriptRoot\StreamingTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Write-Output "Built $destination"
