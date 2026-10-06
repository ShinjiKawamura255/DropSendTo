[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'TestResults/SettingsWindow'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$projectPath = Join-Path $repoRoot 'tests/DropSendTo.SettingsProbe/DropSendTo.SettingsProbe.csproj'
$dotnetPath = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } elseif (Test-Path "$env:USERPROFILE/.dotnet/dotnet.exe") {
    "$env:USERPROFILE/.dotnet/dotnet.exe"
} else { (Get-Command dotnet -ErrorAction Stop).Source }
$buildLog = Join-Path $OutputDirectory 'build.log'
& $dotnetPath build $projectPath --verbosity quiet *> $buildLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $buildLog -Tail 20
    throw 'Settings probe build failed.'
}
$probePath = Join-Path $repoRoot 'tests/DropSendTo.SettingsProbe/bin/Debug/net10.0-windows/DropSendTo.SettingsProbe.exe'
$reportPath = Join-Path $OutputDirectory 'report.txt'
if (Test-Path $reportPath) { Remove-Item -LiteralPath $reportPath }
$probeProcess = Start-Process -FilePath $probePath -ArgumentList ('"' + $OutputDirectory + '"') -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $OutputDirectory 'stdout.log') -RedirectStandardError (Join-Path $OutputDirectory 'stderr.log')
if (-not $probeProcess.WaitForExit(60000)) {
    $probeProcess.Kill()
    throw 'Settings probe timed out.'
}
if (Test-Path $reportPath) { Get-Content -LiteralPath $reportPath }
if ($probeProcess.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $OutputDirectory 'stderr.log') -Tail 15
    throw "Settings probe failed: $($probeProcess.ExitCode)"
}
Write-Output "WPF images and report: $OutputDirectory"
