param(
  [string]$Rid = "win-x64",
  [string]$Version = "",
  [switch]$SelfContained,
  [switch]$KillRunning,
  [switch]$NoZip
)

$ErrorActionPreference = 'Stop'

function Invoke-Step($name, $script) {
  Write-Host "== $name =="
  & $script
}

if ($KillRunning) {
  Write-Host "== Kill running DropSendTo.exe if any =="
  Get-Process DropSendTo -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 300
}

if (-not $Version -or $Version -eq "") {
  try {
    $Version = (git describe --tags --abbrev=0) 2>$null
  } catch {}
  if (-not $Version) { $Version = (Get-Date -Format 'yyyyMMddHHmmss') }
}

$proj = Join-Path $PSScriptRoot "..\src\DropSendTo\DropSendTo.csproj"
$outRoot = Join-Path $PSScriptRoot "..\dist"
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
$outDir = Join-Path $outRoot "DropSendTo_${Rid}_$Version"

Invoke-Step "Restore" { dotnet restore $proj }

$sc = $false
if ($SelfContained) { $sc = $true }

$props = @(
  "-p:PublishSingleFile=true",
  "-p:IncludeNativeLibrariesForSelfExtract=true",
  "-p:DebugType=None",
  "-p:DebugSymbols=false"
)

Invoke-Step "Publish" {
  dotnet publish $proj -c Release -r $Rid --self-contained:$sc -o $outDir @props
}

if (-not $NoZip) {
  $zipPath = "$outDir.zip"
  if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
  Invoke-Step "Zip" { Compress-Archive -Path (Join-Path $outDir '*') -DestinationPath $zipPath }
  Write-Host "Artifact: $zipPath"
} else {
  Write-Host "Artifact dir: $outDir"
}

Write-Host "Done."
