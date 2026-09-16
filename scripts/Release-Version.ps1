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

        $shortSha = ((& git rev-parse --short=12 HEAD 2>$null) | Select-Object -First 1).Trim()
        $nearestTag = ((& git describe --tags --abbrev=0 HEAD 2>$null) | Select-Object -First 1)
        if ($LASTEXITCODE -ne 0 -or $null -eq $nearestTag) {
            $nearestTag = ''
        }
        else {
            $nearestTag = $nearestTag.Trim()
        }

        if ($nearestTag) {
            $commitDistance = [int](((& git rev-list "$nearestTag..HEAD" --count 2>$null) | Select-Object -First 1).Trim())
        }
        else {
            $commitDistance = [int](((& git rev-list HEAD --count 2>$null) | Select-Object -First 1).Trim())
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
