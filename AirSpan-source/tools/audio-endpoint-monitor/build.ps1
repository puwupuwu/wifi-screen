$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
$destination = Join-Path $PSScriptRoot 'audio-endpoint-monitor.exe'
& $compiler /nologo /target:exe /platform:anycpu /optimize+ /warnaserror+ "/out:$destination" (Join-Path $PSScriptRoot 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Audio endpoint monitor compilation failed.' }
Write-Output "Built $destination"
