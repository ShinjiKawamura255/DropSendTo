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

function Resolve-DotnetExe() {
    if ($env:DOTNET_EXE) {
        return $env:DOTNET_EXE
    }

    $userDotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path $userDotnet) {
        return $userDotnet
    }

    return 'dotnet'
}

function Invoke-Dotnet {
    & $dotnetExe @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE. Using: $dotnetExe"
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$solutionPath = Join-Path $repoRoot 'DropSendTo.sln'
$dotnetExe = Resolve-DotnetExe

Push-Location $repoRoot

try {
    if ($KillRunning) {
        Write-Host "== Kill running DropSendTo.exe if any =="
        Get-Process DropSendTo -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }

    if (-not $NoRestore) {
        Invoke-Step "Restore" { Invoke-Dotnet restore $solutionPath }
    }

    Invoke-Step "Build (Debug)" { Invoke-Dotnet build $solutionPath -c Debug -v minimal }

    Invoke-Step "Test (Debug)" {
        Invoke-Dotnet test $solutionPath -c Debug -l "trx;LogFileName=test_results.trx" --nologo
    }

    if ($Release) {
        Invoke-Step "Build (Release)" { Invoke-Dotnet build $solutionPath -c Release -v minimal }
    }

    Write-Host "Done."
}
finally {
    Pop-Location
}
