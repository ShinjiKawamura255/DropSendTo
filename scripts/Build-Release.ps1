param(
    [string]$Rid = "win-x64",
    [string]$Version = "",
    [switch]$SelfContained,
    [switch]$KillRunning,
    [switch]$NoZip,
    [switch]$Portable,
    [switch]$PortableTrim,
    [switch]$InvariantGlobalization,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$SignToolPath = "signtool.exe",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = 'Stop'

function Invoke-Step($name, $script) {
    Write-Host "== $name =="
    & $script
}

$wpfTrimmingSupported = $false
if ($PortableTrim) {
    if (-not $wpfTrimmingSupported) {
        Write-Warning "PortableTrim は WPF アプリではサポートされないため無効化します。"
        $PortableTrim = $false
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot

try {
    if ($KillRunning) {
        Write-Host "== Kill running DropSendTo.exe if any =="
        Get-Process DropSendTo -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
    }

    if (-not $Version -or $Version -eq "") {
        try {
            $Version = (git describe --tags --abbrev=0) 2>$null
        } catch {}
        if (-not $Version) {
            $Version = (Get-Date -Format 'yyyyMMddHHmmss')
        }
    }

    $proj = Join-Path $repoRoot 'src/DropSendTo/DropSendTo.csproj'
    $outRoot = Join-Path $repoRoot 'dist'
    New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
    $outDir = Join-Path $outRoot "DropSendTo_${Rid}_$Version"
    $portableOutDir = Join-Path $outRoot "DropSendTo_Portable_${Rid}_$Version"

    Invoke-Step "Restore" { dotnet restore $proj }

    $props = @(
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )

    if ($InvariantGlobalization) {
        $props += "-p:InvariantGlobalization=true"
    }

    $buildVariants = @()

    if (-not $SelfContained) {
        $buildVariants += [pscustomobject]@{
            Name          = "Framework Dependent"
            OutputDir     = $outDir
            SelfContained = $false
            ExtraProps    = @()
        }
    }

    if ($SelfContained) {
        $buildVariants += [pscustomobject]@{
            Name          = "Self-Contained"
            OutputDir     = $outDir
            SelfContained = $true
            ExtraProps    = @()
        }
    }

    if ($PortableTrim -and -not $Portable) {
        Write-Warning "PortableTrim flag is ignored because Portable build is not requested."
    }

    if ($Portable -and -not $SelfContained) {
        $portableProps = @(
            "-p:EnableCompressionInSingleFile=true"
        )
        if ($PortableTrim) {
            $portableProps += @(
                "-p:PublishTrimmed=true",
                "-p:TrimMode=partial"
            )
        }
        $buildVariants += [pscustomobject]@{
            Name          = "Portable Self-Contained"
            OutputDir     = $portableOutDir
            SelfContained = $true
            ExtraProps    = $portableProps
        }
    } elseif ($Portable -and $SelfContained) {
        Write-Warning "Portable flag is ignored because SelfContained build already selected."
    }

    if ($buildVariants.Count -eq 0) {
        throw "No build variants to publish."
    }

    $artifacts = @()

    foreach ($variant in $buildVariants) {
        $variantName = $variant.Name
        $variantDir = $variant.OutputDir
        $variantSelfContained = [bool]$variant.SelfContained
        $scFlag = if ($variantSelfContained) { 'true' } else { 'false' }

        if (Test-Path $variantDir) {
            Remove-Item $variantDir -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $variantDir | Out-Null

        Invoke-Step "Publish - $variantName" {
            dotnet publish $proj -c Release -r $Rid --self-contained:$scFlag -o $variantDir @props @($variant.ExtraProps)
        }

        $exePath = Join-Path $variantDir 'DropSendTo.exe'

        if ($CertificatePath) {
            if (-not (Test-Path $CertificatePath)) {
                throw "Certificate not found: $CertificatePath"
            }
            if (-not (Test-Path $exePath)) {
                throw "Executable not found for signing: $exePath"
            }
            $signArgs = @('sign', '/fd', 'SHA256', '/f', $CertificatePath)
            if ($CertificatePassword) {
                $signArgs += @('/p', $CertificatePassword)
            }
            if ($TimestampUrl) {
                $signArgs += @('/tr', $TimestampUrl, '/td', 'SHA256')
            }
            $signArgs += $exePath

            Invoke-Step "Code Sign - $variantName" {
                & $SignToolPath @signArgs
            }
        }

        if (-not $NoZip) {
            $zipPath = "$variantDir.zip"
            if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
            Invoke-Step "Zip - $variantName" { Compress-Archive -Path (Join-Path $variantDir '*') -DestinationPath $zipPath }
            $artifacts += $zipPath
        }
        else {
            $artifacts += $variantDir
        }
    }

    foreach ($artifact in $artifacts) {
        if ($artifact -like "*.zip") {
            Write-Host "Artifact: $artifact"
        } else {
            Write-Host "Artifact dir: $artifact"
        }
    }

    Write-Host "Done."
}
finally {
    Pop-Location
}
