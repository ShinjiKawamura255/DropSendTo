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

Push-Location $repoRoot

try {
    if ($KillRunning) {
        Write-Host "== Kill running DropSendTo.exe if any =="
        Get-Process DropSendTo -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }

    if (-not $NoRestore) {
        Invoke-Step "Restore" { dotnet restore $solutionPath }
    }

    Invoke-Step "Build (Debug)" { dotnet build $solutionPath -c Debug -v minimal }

    Invoke-Step "Test (Debug)" {
        dotnet test $solutionPath -c Debug -l "trx;LogFileName=test_results.trx" --nologo
    }

    if ($Release) {
        Invoke-Step "Build (Release)" { dotnet build $solutionPath -c Release -v minimal }
    }

    Write-Host "Done."
}
finally {
    Pop-Location
}
