param(
    [switch]$Release,
    [switch]$KillRunning,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

function Invoke-Step($name, $scriptBlock) {
    Write-Host "== $name =="
    & $scriptBlock
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$solutionPath = Join-Path $repoRoot 'DropSendTo.sln'
$dotnetExe = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { 'dotnet' }

Push-Location $repoRoot

try {
    if ($KillRunning) {
        Write-Host "== Kill running DropSendTo.exe if any =="
        Get-Process DropSendTo -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }

    if (-not $NoRestore) {
        Invoke-Step "Restore" { & $dotnetExe restore $solutionPath }
    }

    Invoke-Step "Build (Debug)" { & $dotnetExe build $solutionPath -c Debug -v minimal }

    Invoke-Step "Test (Debug)" {
        & $dotnetExe test $solutionPath -c Debug -l "trx;LogFileName=test_results.trx" --nologo
    }

    if ($Release) {
        Invoke-Step "Build (Release)" { & $dotnetExe build $solutionPath -c Release -v minimal }
    }

    Write-Host "Done."
}
finally {
    Pop-Location
}
