$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Release-Artifacts.ps1')

function Assert-Throws($name, [scriptblock]$action) {
    try { & $action } catch { return }
    throw "$name did not fail."
}

$fixture = Join-Path (Join-Path $PSScriptRoot '..\TestResults') ('ReleaseArtifactFixture-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$output = Join-Path $fixture 'output'
New-Item -ItemType Directory -Path $source, $output -Force | Out-Null
Set-Content -LiteralPath (Join-Path $source 'USER_GUIDE.md') -Value 'Guide'
Set-Content -LiteralPath (Join-Path $source 'LICENSE') -Value 'License sentinel'
Copy-ReleaseDocuments -RepoRoot $source -OutputDirectory $output
if (-not (Test-Path -LiteralPath (Join-Path $output 'LICENSE'))) { throw 'Release package omits LICENSE.' }
if ((Get-Content -LiteralPath (Join-Path $output 'LICENSE') -Raw).Trim() -cne 'License sentinel') { throw 'LICENSE changed.' }

$exe = Join-Path $output 'DropSendTo.exe'
$certificate = Join-Path $fixture 'test.pfx'
Set-Content -LiteralPath $exe -Value 'unsigned fixture'
Set-Content -LiteralPath $certificate -Value 'fake certificate'
$signer = Join-Path $fixture 'signer.cmd'
Set-Content -LiteralPath $signer -Value '@exit /b 37'
Assert-Throws 'Native signing failure' {
    Invoke-ReleaseSigning -ExecutablePath $exe -CertificatePath $certificate -SignToolPath $signer `
        -ReadSignature { param($path) [pscustomobject]@{ Status = 'Valid' } }
}
Set-Content -LiteralPath $signer -Value '@exit /b 0'
Assert-Throws 'Zero exit without a valid signature' {
    Invoke-ReleaseSigning -ExecutablePath $exe -CertificatePath $certificate -SignToolPath $signer `
        -ReadSignature { param($path) [pscustomobject]@{ Status = 'NotSigned' } }
}
Invoke-ReleaseSigning -ExecutablePath $exe -CertificatePath $certificate -SignToolPath $signer `
    -ReadSignature { param($path) [pscustomobject]@{ Status = 'Valid' } }

# Exercise the public build script with isolated output and a local fake compiler.
$releaseRoot = Join-Path $fixture 'release'
$version = 'v9.8.7-rc.1'
$variantName = "DropSendTo_win-x64_$version"
$portableName = "DropSendTo_Portable_win-x64_$version"
$fakeDotnet = Join-Path $fixture 'dotnet.ps1'
Set-Content -LiteralPath $fakeDotnet -Value @'
if ($args[0] -eq 'restore') {
    $global:LASTEXITCODE = if ($env:RELEASE_TEST_FAILURE -eq 'restore') { 31 } else { 0 }
    return
}
if ($args[0] -eq 'publish') {
    $env:RELEASE_TEST_PUBLISH_COUNT = [string](1 + [int]$env:RELEASE_TEST_PUBLISH_COUNT)
    $global:LASTEXITCODE = 0
    if ($env:RELEASE_TEST_FAILURE -eq 'publish' -or
        ($env:RELEASE_TEST_FAILURE -eq 'second-publish' -and $env:RELEASE_TEST_PUBLISH_COUNT -eq '2')) {
        $global:LASTEXITCODE = 32
        return
    }
    $output = $args[[Array]::IndexOf($args, '-o') + 1]
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $output 'DropSendTo.exe') -Value 'new executable'
    return
}
throw 'Unexpected fake compiler command.'
'@
function Compress-Archive {
    param($Path, $DestinationPath)
    if ($env:RELEASE_TEST_FAILURE -eq 'zip' -or
        ($env:RELEASE_TEST_FAILURE -eq 'second-zip' -and $env:RELEASE_TEST_PUBLISH_COUNT -eq '2')) {
        throw 'Injected zip failure.'
    }
    Microsoft.PowerShell.Archive\Compress-Archive -Path $Path -DestinationPath $DestinationPath
}
function Copy-Item {
    if ($env:RELEASE_TEST_FAILURE -eq 'documents' -and $args -contains '-LiteralPath') {
        throw 'Injected document copy failure.'
    }
    if ($env:RELEASE_TEST_FAILURE -eq 'latest-copy' -and $args -contains '-Path') {
        throw 'Injected latest copy failure.'
    }
    Microsoft.PowerShell.Management\Copy-Item @args
}
function Reset-ReleaseFixture {
    foreach ($name in @('latest', $variantName, $portableName)) {
        $dir = Join-Path $releaseRoot $name
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $dir 'DropSendTo.exe') -Value "previous $name"
    }
    foreach ($name in @($variantName, $portableName)) {
        Set-Content -LiteralPath (Join-Path $releaseRoot "$name.zip") -Value "previous $name.zip"
    }
    $env:RELEASE_TEST_PUBLISH_COUNT = '0'
}
function Assert-PreviousRelease {
    foreach ($name in @('latest', $variantName, $portableName)) {
        $value = (Get-Content -LiteralPath (Join-Path (Join-Path $releaseRoot $name) 'DropSendTo.exe') -Raw).Trim()
        if ($value -cne "previous $name") { throw "Previous $name was lost." }
    }
    foreach ($name in @($variantName, $portableName)) {
        $value = (Get-Content -LiteralPath (Join-Path $releaseRoot "$name.zip") -Raw).Trim()
        if ($value -cne "previous $name.zip") { throw "Previous $name.zip was lost." }
    }
}
function Get-ReleaseSnapshot {
    (@(Get-ChildItem -LiteralPath $releaseRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($releaseRoot.Length) + '|' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })) -join "`n"
}
$savedDotnet = $env:DOTNET_EXE
$savedFailure = $env:RELEASE_TEST_FAILURE
$savedCount = $env:RELEASE_TEST_PUBLISH_COUNT
try {
    $env:DOTNET_EXE = $fakeDotnet
    foreach ($failure in @('restore', 'publish', 'second-publish', 'sign', 'zip', 'second-zip', 'documents', 'latest-copy')) {
        Reset-ReleaseFixture
        $snapshot = Get-ReleaseSnapshot
        $env:RELEASE_TEST_FAILURE = $failure
        $parameters = @{ OutputRoot = $releaseRoot; Version = $version; Portable = $true }
        if ($failure -eq 'sign') {
            Set-Content -LiteralPath $signer -Value '@exit /b 37'
            $parameters.CertificatePath = $certificate
            $parameters.SignToolPath = $signer
        }
        Assert-Throws "Build $failure failure" { & (Join-Path $PSScriptRoot 'Build-Release.ps1') @parameters }
        Assert-PreviousRelease
        if ((Get-ReleaseSnapshot) -cne $snapshot) { throw "Build $failure failure changed prior files." }
        Write-Output "Preserved prior outputs after $failure failure."
    }

    # Inject a failure after several artifacts have already been promoted, then compare all bytes.
    Reset-ReleaseFixture
    $snapshot = Get-ReleaseSnapshot
    $promotionStage = New-ReleaseStage $releaseRoot
    try {
        foreach ($name in @($variantName, $portableName, 'latest')) {
            $dir = Join-Path $promotionStage.Path $name
            New-Item -ItemType Directory -Path $dir | Out-Null
            Set-Content -LiteralPath (Join-Path $dir 'DropSendTo.exe') -Value 'new executable'
        }
        foreach ($name in @($variantName, $portableName)) {
            Set-Content -LiteralPath (Join-Path $promotionStage.Path "$name.zip") -Value 'new ZIP'
        }
        $fault = @{ Triggered = $false }
        Assert-Throws 'Mid-promotion failure' {
            Publish-ReleaseStage -Stage $promotionStage -Names @($variantName, "$variantName.zip", $portableName, "$portableName.zip", 'latest') -Move {
                param($sourcePath, $targetPath)
                if (-not $fault.Triggered -and $sourcePath -ceq (Join-Path $promotionStage.Path $portableName)) {
                    $fault.Triggered = $true
                    throw 'Injected promotion failure.'
                }
                Move-Item -LiteralPath $sourcePath -Destination $targetPath -ErrorAction Stop
            }
        }
        if (-not $fault.Triggered) { throw 'Mid-promotion failure injection did not execute.' }
    } finally { Remove-ReleaseStage $promotionStage }
    Assert-PreviousRelease
    if ((Get-ReleaseSnapshot) -cne $snapshot) { throw 'Promotion rollback changed prior files.' }
    Write-Output 'Promotion rollback restored every previous artifact.'

    foreach ($scenario in @('new-output', 'move-then-throw', 'rollback-failure')) {
        $edgeRoot = Join-Path $fixture $scenario
        New-Item -ItemType Directory -Path $edgeRoot | Out-Null
        if ($scenario -ne 'new-output') {
            foreach ($name in @('one', 'two')) { Set-Content -LiteralPath (Join-Path $edgeRoot $name) -Value "old $name" }
        }
        $edgeStage = New-ReleaseStage $edgeRoot
        foreach ($name in @('one', 'two')) { Set-Content -LiteralPath (Join-Path $edgeStage.Path $name) -Value "new $name" }
        $fault = @{ Triggered = $false; RollbackTriggered = $false }
        Assert-Throws "Promotion $scenario" {
            Publish-ReleaseStage -Stage $edgeStage -Names @('one', 'two') -Move {
                param($sourcePath, $targetPath)
                if (-not $fault.Triggered -and $sourcePath -ceq (Join-Path $edgeStage.Path 'two')) {
                    $fault.Triggered = $true
                    if ($scenario -eq 'move-then-throw') {
                        Move-Item -LiteralPath $sourcePath -Destination $targetPath -ErrorAction Stop
                    }
                    throw 'Injected promotion failure.'
                }
                if ($scenario -eq 'rollback-failure' -and $sourcePath -ceq (Join-Path $edgeStage.Path 'rollback/two')) {
                    $fault.RollbackTriggered = $true
                    throw 'Injected restoration failure.'
                }
                Move-Item -LiteralPath $sourcePath -Destination $targetPath -ErrorAction Stop
            }
        }
        if (-not $fault.Triggered) { throw "$scenario failure injection did not execute." }
        if ($scenario -eq 'rollback-failure') {
            Remove-ReleaseStage $edgeStage
            if (-not $fault.RollbackTriggered -or -not $edgeStage.PreserveOnFailure -or
                (Get-Content -LiteralPath (Join-Path $edgeStage.Path 'rollback/two') -Raw).Trim() -cne 'old two') {
                throw 'Failed rollback lost the recoverable previous artifact.'
            }
            # Simulate the documented manual recovery after the filesystem fault has been removed.
            Move-Item -LiteralPath (Join-Path $edgeStage.Path 'rollback/two') -Destination (Join-Path $edgeRoot 'two')
            $edgeStage.PreserveOnFailure = $false
        }
        Remove-ReleaseStage $edgeStage
        foreach ($name in @('one', 'two')) {
            $target = Join-Path $edgeRoot $name
            if ($scenario -eq 'new-output') {
                if (Test-Path -LiteralPath $target) { throw 'Rollback retained partially promoted new output.' }
            } elseif ((Get-Content -LiteralPath $target -Raw).Trim() -cne "old $name") {
                throw "$scenario did not preserve $name."
            }
        }
    }
    Write-Output 'Promotion edge tests passed (new output, move-then-throw, rollback failure recovery).'

    Reset-ReleaseFixture
    $env:RELEASE_TEST_FAILURE = ''
    & (Join-Path $PSScriptRoot 'Build-Release.ps1') -OutputRoot $releaseRoot -Version $version -Portable -NoZip
    foreach ($name in @('latest', $variantName, $portableName)) {
        $value = (Get-Content -LiteralPath (Join-Path (Join-Path $releaseRoot $name) 'DropSendTo.exe') -Raw).Trim()
        if ($value -cne 'new executable') { throw "Successful build did not promote $name." }
    }
    # -NoZip retains the existing ZIP, as before; it does not silently regenerate or delete it.
    if ((Get-Content -LiteralPath (Join-Path $releaseRoot "$variantName.zip") -Raw).Trim() -cne "previous $variantName.zip") {
        throw 'NoZip changed a previous ZIP.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $releaseRoot 'latest/PortableSelfContained/LICENSE'))) {
        throw 'Latest omitted the additional variant documents.'
    }
    Reset-ReleaseFixture
    & (Join-Path $PSScriptRoot 'Build-Release.ps1') -OutputRoot $releaseRoot -Version $version -Portable
    foreach ($name in @($variantName, $portableName)) {
        $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $releaseRoot "$name.zip"))
        try {
            foreach ($file in @('DropSendTo.exe', 'USER_GUIDE.md', 'LICENSE')) {
                if ($null -eq $archive.GetEntry($file)) { throw "Promoted ZIP omitted $file." }
            }
        } finally { $archive.Dispose() }
    }
} finally {
    $env:DOTNET_EXE = $savedDotnet
    $env:RELEASE_TEST_FAILURE = $savedFailure
    $env:RELEASE_TEST_PUBLISH_COUNT = $savedCount
    Remove-Item -LiteralPath Function:\Compress-Archive
    Remove-Item -LiteralPath Function:\Copy-Item
}
Write-Output 'Release artifact tests passed (documents, signing, eight staged build failures, four promotion faults, ZIP and NoZip).'

# A cross-session lock must be a held Global object, not merely a same-session Local name.
Add-Type -TypeDefinition @"
using System.Threading;
public static class ReleaseGlobalMutexProbe {
    public static bool IsHeld(string name) {
        bool held = false;
        var thread = new Thread(() => {
            try {
                using (var mutex = Mutex.OpenExisting(name)) {
                    bool acquired = false;
                    try { acquired = mutex.WaitOne(0); held = !acquired; }
                    catch (AbandonedMutexException) { acquired = true; }
                    finally { if (acquired) mutex.ReleaseMutex(); }
                }
            } catch (WaitHandleCannotBeOpenedException) { }
        });
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(3000)) throw new System.Exception("Mutex probe did not stop.");
        return held;
    }
}
"@
$globalRoot = Join-Path $fixture 'global-promotion'
New-Item -ItemType Directory -Path $globalRoot | Out-Null
$globalStage = New-ReleaseStage $globalRoot
Set-Content -LiteralPath (Join-Path $globalStage.Path 'artifact.txt') -Value 'prepared'
$globalHash = [Security.Cryptography.SHA256]::Create()
try {
    $globalKey = [IO.Path]::GetFullPath($globalRoot).TrimEnd([IO.Path]::DirectorySeparatorChar).ToUpperInvariant()
    $globalDigest = [BitConverter]::ToString($globalHash.ComputeHash([Text.Encoding]::UTF8.GetBytes($globalKey))).Replace('-', '')
} finally { $globalHash.Dispose() }
$globalName = "Global\DropSendTo.Release.$globalDigest"
$script:globalPromotionObserved = $false
Publish-ReleaseStage -Stage $globalStage -Names @('artifact.txt') -Move {
    param($source, $target)
    $script:globalPromotionObserved = [ReleaseGlobalMutexProbe]::IsHeld($globalName)
    Move-Item -LiteralPath $source -Destination $target -ErrorAction Stop
}
if (-not $script:globalPromotionObserved) { throw 'Release promotion does not hold a cross-session Global mutex.' }
Remove-ReleaseStage $globalStage
Write-Output 'Global release promotion mutex ownership test passed.'

Initialize-ReleasePathMetadata
$cloudBase = [Convert]::ToUInt32('9000001A', 16)
for ($variant = 0; $variant -lt 16; $variant++) {
    if (-not [DropSendTo.ReleasePathMetadata]::IsCloudTag($cloudBase + ($variant * 4096))) {
        throw 'A known CLOUD reparse tag was rejected.'
    }
}
foreach ($tag in @('A0000003', 'A000000C', '9000001C', '80000021', '00000000')) {
    if ([DropSendTo.ReleasePathMetadata]::IsCloudTag([Convert]::ToUInt32($tag, 16))) {
        throw 'A redirecting or unknown reparse tag was accepted.'
    }
}
$junctionTarget = Join-Path $fixture 'junction-target'
$junctionRoot = Join-Path $fixture 'junction-output'
New-Item -ItemType Directory -Path $junctionTarget, $junctionRoot | Out-Null
Set-Content -LiteralPath (Join-Path $junctionTarget 'keep.txt') -Value 'outside stage sentinel'
$junctionName = '.release-stage-' + [Guid]::NewGuid().ToString('N')
$junctionPath = Join-Path $junctionRoot $junctionName
New-Item -ItemType Junction -Path $junctionPath -Target $junctionTarget | Out-Null
$junctionStage = [pscustomobject]@{Root=$junctionRoot;Name=$junctionName;Path=$junctionPath;PreserveOnFailure=$false}
Assert-Throws 'Redirected stage ownership' { Assert-ReleaseStage $junctionStage }
Assert-Throws 'Redirected stage cleanup' { Remove-ReleaseStage $junctionStage }
if ((Get-Content -LiteralPath (Join-Path $junctionTarget 'keep.txt') -Raw).Trim() -cne 'outside stage sentinel') {
    throw 'Redirected stage cleanup modified its target.'
}
# Remove only the junction entry, never recursively traverse its destination.
[IO.Directory]::Delete($junctionPath)
Write-Output 'Cloud tag-family and real junction rejection tests passed.'
