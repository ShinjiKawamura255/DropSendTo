function Copy-ReleaseDocuments {
    param([string]$RepoRoot, [string]$OutputDirectory)
    foreach ($name in @('USER_GUIDE.md', 'LICENSE')) {
        Copy-Item -LiteralPath (Join-Path $RepoRoot $name) -Destination $OutputDirectory -Force -ErrorAction Stop
    }
}

function Invoke-ReleaseSigning {
    param(
        [string]$ExecutablePath,
        [string]$CertificatePath,
        [string]$CertificatePassword,
        [string]$SignToolPath = 'signtool.exe',
        [string]$TimestampUrl,
        [scriptblock]$ReadSignature = { param($path) Get-AuthenticodeSignature -LiteralPath $path }
    )
    if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) { throw 'Executable for signing is missing.' }
    if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) { throw 'Signing certificate is missing.' }
    $signArgs = @('sign', '/fd', 'SHA256', '/f', $CertificatePath)
    if ($CertificatePassword) { $signArgs += @('/p', $CertificatePassword) }
    if ($TimestampUrl) { $signArgs += @('/tr', $TimestampUrl, '/td', 'SHA256') }
    $signArgs += $ExecutablePath
    & $SignToolPath @signArgs
    if ($LASTEXITCODE -ne 0) { throw "Code signing failed with exit code $LASTEXITCODE." }
    $signature = & $ReadSignature $ExecutablePath
    if ($signature.Status -ne 'Valid') { throw "Code signing verification failed: $($signature.Status)." }
}

function Get-ContainedOutputPath([string]$root, [string]$name) {
    $resolvedRoot = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $resolvedPath = [IO.Path]::GetFullPath((Join-Path $resolvedRoot $name))
    if (-not $resolvedPath.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release output path escapes its output root.'
    }
    return $resolvedPath
}

function New-ReleaseStage([string]$OutputRoot) {
    $root = [IO.Path]::GetFullPath($OutputRoot)
    $name = '.release-stage-' + [Guid]::NewGuid().ToString('N')
    $path = Get-ContainedOutputPath $root $name
    New-Item -ItemType Directory -Path $path -ErrorAction Stop | Out-Null
    return [pscustomobject]@{ Root = $root; Name = $name; Path = $path; PreserveOnFailure = $false }
}

function Initialize-ReleasePathMetadata {
    if ('DropSendTo.ReleasePathMetadata' -as [type]) { return }
    # FILE_ATTRIBUTE_TAG_INFO distinguishes cloud placeholders from path redirection.
    # Tags: https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/winnt.h
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace DropSendTo {
    public static class ReleasePathMetadata {
        [StructLayout(LayoutKind.Sequential)]
        public struct AttributeTagInfo { public uint Attributes; public uint Tag; }
        [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, ExactSpelling=true, SetLastError=true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass,
            out AttributeTagInfo information, uint size);
        public static uint GetTag(string path) {
            // Open the reparse point itself, without hydrating content or following a junction/symlink.
            using (var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                AttributeTagInfo information;
                if (!GetFileInformationByHandleEx(handle, 9, out information, 8))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return information.Tag;
            }
        }
        public static bool IsCloudTag(uint tag) { return (tag & ~0x0000F000u) == 0x9000001Au; }
    }
}
'@
}

function Test-ReleasePathRedirected([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    if (-not ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { return $false }
    Initialize-ReleasePathMetadata
    # Only known non-redirecting CLOUD tags are allowed; unknown tags still fail closed.
    return -not [DropSendTo.ReleasePathMetadata]::IsCloudTag([DropSendTo.ReleasePathMetadata]::GetTag($Path))
}

function Assert-ReleaseStage($Stage) {
    if ($Stage.Name -notmatch '^\.release-stage-[a-f0-9]{32}$' -or
        $Stage.Path -cne (Get-ContainedOutputPath $Stage.Root $Stage.Name)) {
        throw 'Invalid release staging ownership.'
    }
    if (-not (Test-Path -LiteralPath $Stage.Path -PathType Container) -or
        (Test-ReleasePathRedirected $Stage.Path)) {
        throw 'Release staging directory is missing or redirected.'
    }
}

function Remove-ReleaseStage($Stage) {
    Assert-ReleaseStage $Stage
    if ($Stage.PreserveOnFailure) { return }
    Remove-Item -LiteralPath $Stage.Path -Recurse -Force -ErrorAction Stop
}

function Publish-ReleaseStage {
    param(
        $Stage,
        [string[]]$Names,
        [scriptblock]$Move = { param($source, $target) Move-Item -LiteralPath $source -Destination $target -ErrorAction Stop }
    )
    Assert-ReleaseStage $Stage
    # Independent builds can prepare concurrently, but promotions into the same root must not interleave.
    $hash = [Security.Cryptography.SHA256]::Create()
    try {
        $rootKey = [IO.Path]::GetFullPath($Stage.Root).TrimEnd([IO.Path]::DirectorySeparatorChar).ToUpperInvariant()
        $digest = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($rootKey))).Replace('-', '')
    } finally { $hash.Dispose() }
    $mutex = [Threading.Mutex]::new($false, "Global\DropSendTo.Release.$digest")
    $acquired = $false
    try {
        try { $acquired = $mutex.WaitOne(30000) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'Another release promotion is using this output root.' }
        Invoke-ReleasePromotion -Stage $Stage -Names $Names -Move $Move
    } finally {
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

function Invoke-ReleasePromotion {
    param(
        $Stage,
        [string[]]$Names,
        [scriptblock]$Move = { param($source, $target) Move-Item -LiteralPath $source -Destination $target -ErrorAction Stop }
    )
    Assert-ReleaseStage $Stage
    if ($Names.Count -eq 0 -or @($Names | Select-Object -Unique).Count -ne $Names.Count) {
        throw 'Release promotion requires distinct artifacts.'
    }
    # Only immediate output children can be replaced. Validate the complete set before the first move.
    $entries = @()
    $rollback = Get-ContainedOutputPath $Stage.Path 'rollback'
    foreach ($name in $Names) {
        if ([IO.Path]::GetFileName($name) -cne $name -or $name -eq $Stage.Name -or $name -eq 'rollback') {
            throw 'Invalid release artifact name.'
        }
        $source = Get-ContainedOutputPath $Stage.Path $name
        $target = Get-ContainedOutputPath $Stage.Root $name
        if (-not (Test-Path -LiteralPath $source)) { throw "Staged artifact is missing: $name" }
        foreach ($path in @($source, $target)) {
            if (Test-ReleasePathRedirected $path) {
                throw "Release artifact is redirected: $name"
            }
        }
        $entries += [pscustomobject]@{
            Source = $source; Target = $target; Backup = Get-ContainedOutputPath $rollback $name; Attempted = $false
        }
    }
    New-Item -ItemType Directory -Path $rollback -ErrorAction Stop | Out-Null
    try {
        foreach ($entry in $entries) {
            $entry.Attempted = $true
            if (Test-Path -LiteralPath $entry.Target) { & $Move $entry.Target $entry.Backup }
            & $Move $entry.Source $entry.Target
        }
    } catch {
        $promotionError = $_
        $rollbackErrors = @()
        for ($index = $entries.Count - 1; $index -ge 0; $index--) {
            $entry = $entries[$index]
            if (-not $entry.Attempted) { continue }
            try {
                # Also detect a move that changed the filesystem before throwing.
                if (-not (Test-Path -LiteralPath $entry.Source) -and (Test-Path -LiteralPath $entry.Target)) {
                    & $Move $entry.Target $entry.Source
                }
                if (Test-Path -LiteralPath $entry.Backup) { & $Move $entry.Backup $entry.Target }
            } catch { $rollbackErrors += $_.Exception.Message }
        }
        if ($rollbackErrors.Count -gt 0) {
            $Stage.PreserveOnFailure = $true
            throw "Release promotion failed: $($promotionError.Exception.Message). Recovery files retained at $($Stage.Path). Rollback errors: $($rollbackErrors -join '; ')"
        }
        throw $promotionError
    }
}
