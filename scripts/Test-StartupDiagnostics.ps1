[CmdletBinding()]
param([string]$OutputDirectory, [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'TestResults/StartupDiagnostics' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$dotnetPath = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } elseif (Test-Path "$env:USERPROFILE/.dotnet/dotnet.exe") {
    "$env:USERPROFILE/.dotnet/dotnet.exe"
} else { (Get-Command dotnet -ErrorAction Stop).Source }
$project = Join-Path $repoRoot 'tests/DropSendTo.SettingsProbe/DropSendTo.SettingsProbe.csproj'
& $dotnetPath build $project -c $Configuration --no-restore --verbosity quiet *> (Join-Path $OutputDirectory 'build.log')
if ($LASTEXITCODE -ne 0) {
    Get-Content (Join-Path $OutputDirectory 'build.log') -Tail 15
    throw 'Startup diagnostics probe build failed.'
}
$executable = Join-Path $repoRoot "tests/DropSendTo.SettingsProbe/bin/$Configuration/net10.0-windows/DropSendTo.SettingsProbe.exe"
foreach ($mode in @('logger-japanese', 'logger-english', 'future-schema')) {
    $output = Join-Path $OutputDirectory ($mode + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $output | Out-Null
    $process = Start-Process -FilePath $executable -ArgumentList ('--startup-diagnostics ' + $mode + ' "' + $output + '"') `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $output 'stdout.log') `
        -RedirectStandardError (Join-Path $output 'stderr.log')
    try {
        if (-not $process.WaitForExit(15000)) { throw "Startup diagnostics probe timed out: $mode" }
        if (Test-Path (Join-Path $output 'report.txt')) { Get-Content (Join-Path $output 'report.txt') }
        if ($process.ExitCode -ne 0) {
            Get-Content (Join-Path $output 'stderr.log') -Tail 10
            throw "Startup diagnostics probe failed: $mode"
        }
    } finally {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
        $process.Dispose()
    }
}
Write-Output "Startup diagnostics evidence: $OutputDirectory"
