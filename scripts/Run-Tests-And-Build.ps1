param(
    [switch]$Release
)

Write-Host "== Restore =="
dotnet restore .\DropSendTo.sln
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== Build (Debug) =="
dotnet build .\DropSendTo.sln -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== Test (Debug) =="
dotnet test .\DropSendTo.sln -c Debug -l "trx;LogFileName=test_results.trx"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Release) {
  Write-Host "== Build (Release) =="
  dotnet build .\DropSendTo.sln -c Release
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Done."
