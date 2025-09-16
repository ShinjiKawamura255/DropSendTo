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

if ($KillRunning) {
  Write-Host "== Kill running DropSendTo.exe if any =="
  Get-Process DropSendTo -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
}

if (-not $NoRestore) {
  Invoke-Step "Restore" { dotnet restore .\DropSendTo.sln }
}

Invoke-Step "Build (Debug)" { dotnet build .\DropSendTo.sln -c Debug -v minimal }

Invoke-Step "Test (Debug)" { dotnet test .\DropSendTo.sln -c Debug -l "trx;LogFileName=test_results.trx" --nologo }

if ($Release) {
  Invoke-Step "Build (Release)" { dotnet build .\DropSendTo.sln -c Release -v minimal }
}

Write-Host "Done."
