param(
    [string]$Rid = "win-x64",
    [string]$Version = "",
    [switch]$SelfContained,
    [switch]$KillRunning,
    [switch]$NoZip,
    [switch]$Portable,
    [switch]$PortableTrim,
    [switch]$VersionOnly,
    [switch]$InvariantGlobalization,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$SignToolPath = "signtool.exe",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = 'Stop'
# Progress bar output (e.g., Compress-Archive) can overwrite warnings in the console; suppress it to keep logs readable.
$ProgressPreference = 'SilentlyContinue'

. (Join-Path $PSScriptRoot 'Release-Version.ps1')
. (Join-Path $PSScriptRoot 'Release-Artifacts.ps1')

function Invoke-Step($name, $script) {
    Write-Host "== $name =="
    & $script
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

function Get-SafeVariantName($name, $index) {
    if (-not $name) {
        return "Variant$index"
    }
    $safe = ($name -replace '[^A-Za-z0-9]', '')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        $safe = "Variant$index"
    }
    return $safe
}

function Get-ContainedOutputPath([string]$root, [string]$name) {
    $resolvedRoot = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $resolvedPath = [IO.Path]::GetFullPath((Join-Path $resolvedRoot $name))
    if (-not $resolvedPath.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release output path escapes its output root.'
    }
    return $resolvedPath
}

$wpfTrimmingSupported = $false
if ($PortableTrim) {
    if (-not $wpfTrimmingSupported) {
        Write-Warning "PortableTrim is not supported for this WPF application and has been disabled."
        $PortableTrim = $false
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnetExe = Resolve-DotnetExe
Push-Location $repoRoot

try {
    $Version = Resolve-ReleaseVersionFromRepository -ExplicitVersion $Version -RepoRoot $repoRoot
    if ($VersionOnly) {
        Write-Output $Version
        return
    }

    # Validate before stopping a running app or changing any release output.
    if (Test-StableReleaseVersion $Version) {
        Assert-OfficialReleaseFromRepository -Version $Version -RepoRoot $repoRoot
    }

    if ($KillRunning) {
        Write-Host "== Kill running DropSendTo.exe if any =="
        Get-Process DropSendTo -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
    }

    $proj = Join-Path $repoRoot 'src/DropSendTo/DropSendTo.csproj'
    if ($Rid -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]*$') { throw 'Unsafe runtime identifier.' }
    $outRoot = if ($OutputRoot) { [IO.Path]::GetFullPath($OutputRoot) } else { Join-Path $repoRoot 'dist' }
    New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
    $outDir = Get-ContainedOutputPath $outRoot "DropSendTo_${Rid}_$Version"
    $portableOutDir = Get-ContainedOutputPath $outRoot "DropSendTo_Portable_${Rid}_$Version"
    $latestDir = Get-ContainedOutputPath $outRoot 'latest'
    if (Test-Path $latestDir) {
        Remove-Item $latestDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $latestDir | Out-Null

    Invoke-Step "Restore" { Invoke-Dotnet restore $proj }

    $props = @(
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    ) + @(Get-ReleaseBuildProperties -Version $Version)

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

    $variantIndex = 0
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
            Invoke-Dotnet publish $proj -c Release -r $Rid --self-contained:$scFlag -o $variantDir @props @($variant.ExtraProps)
        }

        $exePath = Join-Path $variantDir 'DropSendTo.exe'
        Copy-ReleaseDocuments -RepoRoot $repoRoot -OutputDirectory $variantDir

        if ($CertificatePath) {
            Invoke-Step "Code Sign - $variantName" {
                Invoke-ReleaseSigning -ExecutablePath $exePath -CertificatePath $CertificatePath `
                    -CertificatePassword $CertificatePassword -SignToolPath $SignToolPath -TimestampUrl $TimestampUrl
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

        $targetLatestDir = if ($variantIndex -eq 0) {
            $latestDir
        } else {
            $safeName = Get-SafeVariantName $variantName ($variantIndex + 1)
            $nestedLatest = Get-ContainedOutputPath $latestDir $safeName
            if (Test-Path $nestedLatest) {
                Remove-Item $nestedLatest -Recurse -Force
            }
            New-Item -ItemType Directory -Force -Path $nestedLatest | Out-Null
            $nestedLatest
        }
        Copy-Item -Path (Join-Path $variantDir '*') -Destination $targetLatestDir -Recurse -Force

        $variantIndex++
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
