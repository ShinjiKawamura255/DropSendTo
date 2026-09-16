$ErrorActionPreference = 'Stop'

$helperPath = Join-Path $PSScriptRoot 'Release-Version.ps1'
. $helperPath

function Assert-Equal {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Expected,
        [Parameter(Mandatory)] [string] $Actual
    )

    if ($Expected -cne $Actual) {
        throw "$Name failed. Expected '$Expected', actual '$Actual'."
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [scriptblock] $Action
    )

    try {
        & $Action
    }
    catch {
        return
    }

    throw "$Name failed. Expected an exception."
}

$timestamp = [DateTime]::SpecifyKind([DateTime]'2026-09-16T01:02:03', [DateTimeKind]::Utc)
$sha = 'abc123def456'

Assert-Equal 'clean exact tag' 'v1.3.0' (
    Resolve-ReleaseVersion -NearestTag 'v1.3.0' -ShortSha $sha -CommitDistance 0 `
        -TrackedDirty $false -UntrackedDirty $false -TimestampUtc $timestamp)

Assert-Equal 'clean post-tag commit' 'v1.3.0-dev.4.abc123def456' (
    Resolve-ReleaseVersion -NearestTag 'v1.3.0' -ShortSha $sha -CommitDistance 4 `
        -TrackedDirty $false -UntrackedDirty $false -TimestampUtc $timestamp)

Assert-Equal 'tracked dirty exact tag' 'v1.3.0-dev.0.abc123def456.dirty' (
    Resolve-ReleaseVersion -NearestTag 'v1.3.0' -ShortSha $sha -CommitDistance 0 `
        -TrackedDirty $true -UntrackedDirty $false -TimestampUtc $timestamp)

Assert-Equal 'untracked dirty exact tag' 'v1.3.0-dev.0.abc123def456.dirty' (
    Resolve-ReleaseVersion -NearestTag 'v1.3.0' -ShortSha $sha -CommitDistance 0 `
        -TrackedDirty $false -UntrackedDirty $true -TimestampUtc $timestamp)

Assert-Equal 'repository without tags' 'v0.0.0-dev.20260916T010203Z.abc123def456' (
    Resolve-ReleaseVersion -NearestTag '' -ShortSha $sha -CommitDistance 7 `
        -TrackedDirty $false -UntrackedDirty $false -TimestampUtc $timestamp)

Assert-Equal 'explicit version override' 'v9.8.7-rc.1' (
    Resolve-ReleaseVersion -ExplicitVersion 'v9.8.7-rc.1' -NearestTag 'v1.3.0' -ShortSha $sha `
        -CommitDistance 4 -TrackedDirty $true -UntrackedDirty $true -TimestampUtc $timestamp)

Assert-Throws 'unsafe explicit version' {
    Resolve-ReleaseVersion -ExplicitVersion '..\outside' -NearestTag '' -ShortSha $sha `
        -CommitDistance 0 -TrackedDirty $false -UntrackedDirty $false -TimestampUtc $timestamp
}

$distPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')) 'dist'
$distSnapshotBefore = if (Test-Path $distPath) {
    @(Get-ChildItem -LiteralPath $distPath -Recurse -Force | ForEach-Object {
        "{0}|{1}|{2:o}" -f $_.FullName, $_.Length, $_.LastWriteTimeUtc
    }) -join "`n"
}
else {
    '<missing>'
}

$versionOnlyOutput = & (Join-Path $PSScriptRoot 'Build-Release.ps1') -Version 'v9.8.7-rc.1' -VersionOnly
Assert-Equal 'Build-Release version-only output' 'v9.8.7-rc.1' (($versionOnlyOutput | Select-Object -Last 1).ToString())

$distSnapshotAfter = if (Test-Path $distPath) {
    @(Get-ChildItem -LiteralPath $distPath -Recurse -Force | ForEach-Object {
        "{0}|{1}|{2:o}" -f $_.FullName, $_.Length, $_.LastWriteTimeUtc
    }) -join "`n"
}
else {
    '<missing>'
}
Assert-Equal 'version-only leaves dist unchanged' $distSnapshotBefore $distSnapshotAfter

Write-Host 'Release version tests passed (9 assertions).'
