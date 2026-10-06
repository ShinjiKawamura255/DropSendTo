function Test-StableReleaseVersion {
    param([AllowEmptyString()] [string] $Value)
    $parsed = $null
    return $Value -cmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -and
        [Version]::TryParse($Value.Substring(1), [ref]$parsed)
}

function Get-ReleaseBuildProperties {
    param([Parameter(Mandatory)] [string] $Version)
    if (-not (Test-ReleaseVersionSafe $Version)) { throw 'Unsafe release version.' }
    return @("-p:InformationalVersion=$Version", '-p:IncludeSourceRevisionInInformationalVersion=false')
}

function Assert-OfficialReleaseState {
    param(
        [Parameter(Mandatory)] [string] $Version,
        [bool] $TagMatchesHead,
        [bool] $TrackedDirty,
        [bool] $UntrackedDirty
    )
    if (-not (Test-StableReleaseVersion $Version)) { throw 'Official release tags must use vX.Y.Z.' }
    if (-not $TagMatchesHead) { throw "The official release tag '$Version' must point to HEAD." }
    if ($TrackedDirty -or $UntrackedDirty) { throw 'Official releases require a clean worktree, including untracked files.' }
}

function Assert-OfficialReleaseFromRepository {
    param([Parameter(Mandatory)] [string] $Version, [Parameter(Mandatory)] [string] $RepoRoot)
    $probe = Get-ReleaseVersionProbe -RepoRoot $RepoRoot
    Push-Location $RepoRoot
    try {
        $tagCommit = @(& git rev-parse --verify "refs/tags/${Version}^{commit}" 2>$null)[0]
        $tagExists = $LASTEXITCODE -eq 0
        $headCommit = @(& git rev-parse --verify HEAD 2>$null)[0]
        $tagMatches = $tagExists -and $LASTEXITCODE -eq 0 -and $tagCommit -ceq $headCommit
        Assert-OfficialReleaseState -Version $Version -TagMatchesHead $tagMatches `
            -TrackedDirty $probe.TrackedDirty -UntrackedDirty $probe.UntrackedDirty
    }
    finally { Pop-Location }
}

function Test-ReleaseVersionSafe {
    param([AllowEmptyString()] [string] $Value)

    return -not [string]::IsNullOrWhiteSpace($Value) -and $Value -match '^[A-Za-z0-9][A-Za-z0-9._-]*$'
}

function Resolve-ReleaseVersion {
    [CmdletBinding()]
    param(
        [AllowEmptyString()] [string] $ExplicitVersion = '',
        [AllowEmptyString()] [string] $NearestTag = '',
        [AllowEmptyString()] [string] $ShortSha = 'nogit',
        [int] $CommitDistance = 0,
        [bool] $TrackedDirty = $false,
        [bool] $UntrackedDirty = $false,
        [DateTime] $TimestampUtc = [DateTime]::UtcNow
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitVersion)) {
        if (-not (Test-ReleaseVersionSafe $ExplicitVersion)) {
            throw "Version contains characters that are unsafe for an artifact name: '$ExplicitVersion'."
        }
        return $ExplicitVersion
    }

    if (-not (Test-ReleaseVersionSafe $ShortSha)) {
        $ShortSha = 'nogit'
    }

    if (-not [string]::IsNullOrWhiteSpace($NearestTag) -and -not (Test-ReleaseVersionSafe $NearestTag)) {
        $NearestTag = ''
    }

    $distance = [Math]::Max(0, $CommitDistance)
    $dirty = $TrackedDirty -or $UntrackedDirty
    if ($NearestTag -and $distance -eq 0 -and -not $dirty) {
        return $NearestTag
    }

    if ($NearestTag) {
        $version = "$NearestTag-dev.$distance.$ShortSha"
    }
    else {
        $stamp = $TimestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")
        $version = "v0.0.0-dev.$stamp.$ShortSha"
    }

    if ($dirty) {
        $version += '.dirty'
    }
    return $version
}

function Get-ReleaseVersionProbe {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $RepoRoot)

    Push-Location $RepoRoot
    try {
        & git rev-parse --verify HEAD *> $null
        if ($LASTEXITCODE -ne 0) {
            return [pscustomobject]@{
                NearestTag    = ''
                ShortSha      = 'nogit'
                CommitDistance = 0
                TrackedDirty  = $false
                UntrackedDirty = $false
            }
        }

        $shortSha = @(& git rev-parse --short=12 HEAD 2>$null)[0].Trim()
        $nearestTag = @(& git describe --tags --abbrev=0 HEAD 2>$null)[0]
        if ($LASTEXITCODE -ne 0 -or $null -eq $nearestTag) {
            $nearestTag = ''
        }
        else {
            $nearestTag = $nearestTag.Trim()
        }

        if ($nearestTag) {
            $commitDistance = [int](@(& git rev-list "$nearestTag..HEAD" --count 2>$null)[0].Trim())
        }
        else {
            $commitDistance = [int](@(& git rev-list HEAD --count 2>$null)[0].Trim())
        }

        & git diff-index --quiet HEAD --
        $trackedDirty = $LASTEXITCODE -ne 0
        $untrackedFiles = @(& git ls-files --others --exclude-standard 2>$null)

        return [pscustomobject]@{
            NearestTag     = $nearestTag
            ShortSha       = $shortSha
            CommitDistance = $commitDistance
            TrackedDirty   = $trackedDirty
            UntrackedDirty = $untrackedFiles.Count -gt 0
        }
    }
    finally {
        Pop-Location
    }
}

function Resolve-ReleaseVersionFromRepository {
    [CmdletBinding()]
    param(
        [AllowEmptyString()] [string] $ExplicitVersion = '',
        [Parameter(Mandatory)] [string] $RepoRoot,
        [DateTime] $TimestampUtc = [DateTime]::UtcNow
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitVersion)) {
        return Resolve-ReleaseVersion -ExplicitVersion $ExplicitVersion -TimestampUtc $TimestampUtc
    }

    $probe = Get-ReleaseVersionProbe -RepoRoot $RepoRoot
    return Resolve-ReleaseVersion -NearestTag $probe.NearestTag -ShortSha $probe.ShortSha `
        -CommitDistance $probe.CommitDistance -TrackedDirty $probe.TrackedDirty `
        -UntrackedDirty $probe.UntrackedDirty -TimestampUtc $TimestampUtc
}
