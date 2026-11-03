[CmdletBinding()]
param([string]$OutputDirectory, [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'TestResults/MouseGestures' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$dotnetPath = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } elseif (Test-Path "$env:USERPROFILE/.dotnet/dotnet.exe") {
    "$env:USERPROFILE/.dotnet/dotnet.exe"
} else { (Get-Command dotnet -ErrorAction Stop).Source }
$project = Join-Path $repoRoot 'tests/DropSendTo.SettingsProbe/DropSendTo.SettingsProbe.csproj'
$buildLog = Join-Path $OutputDirectory 'build.log'
& $dotnetPath build $project -c $Configuration --verbosity quiet *> $buildLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $buildLog -Tail 20
    throw 'Mouse gesture probe build failed.'
}
$probePath = Join-Path $repoRoot "tests/DropSendTo.SettingsProbe/bin/$Configuration/net10.0-windows/DropSendTo.SettingsProbe.exe"
$reportPath = Join-Path $OutputDirectory 'report.txt'
if (Test-Path -LiteralPath $reportPath) { Remove-Item -LiteralPath $reportPath }
$probe = Start-Process -FilePath $probePath -ArgumentList ('--mouse-gestures "' + $OutputDirectory + '"') `
    -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $OutputDirectory 'stdout.log') `
    -RedirectStandardError (Join-Path $OutputDirectory 'stderr.log')
if (-not $probe.WaitForExit(45000)) {
    $probe.Kill()
    throw 'Mouse gesture probe timed out.'
}
if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath }
if ($probe.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $OutputDirectory 'stderr.log') -Tail 15
    throw "Mouse gesture probe failed: $($probe.ExitCode)"
}
Write-Output "WPF gesture images and report: $OutputDirectory"
