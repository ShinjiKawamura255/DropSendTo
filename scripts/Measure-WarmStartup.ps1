[CmdletBinding()]
param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\src\DropSendTo\bin\Release\net10.0-windows\DropSendTo.exe'),
    [ValidateRange(5, 100)]
    [int]$SampleCount = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$thresholdMilliseconds = 1000.0
$resolvedExecutable = [System.IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "Release executable was not found: $resolvedExecutable"
}

$existing = @(Get-Process -Name 'DropSendTo' -ErrorAction SilentlyContinue)
if ($existing.Count -gt 0) {
    $ids = ($existing.Id | Sort-Object) -join ', '
    throw "DropSendTo is already running (PID: $ids). Close it before measuring startup."
}

function Get-ProtectedFileState {
    param([Parameter(Mandatory)][string]$AppDataRoot)

    $appRoot = Join-Path $AppDataRoot 'DropSendTo'
    $paths = @(
        (Join-Path $appRoot 'config.json'),
        (Join-Path $appRoot 'config.json.bak')
    )
    $logRoot = Join-Path $appRoot 'logs'
    if (Test-Path -LiteralPath $logRoot -PathType Container) {
        $paths += @(Get-ChildItem -LiteralPath $logRoot -Filter 'app*.log' -File | ForEach-Object FullName)
    }

    return @($paths | Sort-Object -Unique | ForEach-Object {
        if (Test-Path -LiteralPath $_ -PathType Leaf) {
            $item = Get-Item -LiteralPath $_
            [pscustomobject]@{
                Path = [System.IO.Path]::GetFullPath($item.FullName)
                Exists = $true
                Length = $item.Length
                LastWriteUtcTicks = $item.LastWriteTimeUtc.Ticks
                Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
            }
        }
        else {
            [pscustomobject]@{
                Path = [System.IO.Path]::GetFullPath($_)
                Exists = $false
                Length = $null
                LastWriteUtcTicks = $null
                Sha256 = $null
            }
        }
    })
}

function Assert-PathContained {
    param(
        [Parameter(Mandatory)][string]$Candidate,
        [Parameter(Mandatory)][string]$Root
    )

    $candidateFull = [System.IO.Path]::GetFullPath($Candidate)
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = $rootFull + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidateFull.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Probe path escaped the temporary data root: $candidateFull"
    }
}

function Stop-ProbeProcess {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process) {
        return
    }

    try {
        if (-not $Process.HasExited) {
            $Process.Kill()
            if (-not $Process.WaitForExit(5000)) {
                throw "Probe process $($Process.Id) did not terminate."
            }
        }
    }
    finally {
        $Process.Dispose()
    }
}

function Measure-OneStartup {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string]$DataRoot
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.UseShellExecute = $false
    $startInfo.WorkingDirectory = Split-Path -Parent $Executable
    $startInfo.EnvironmentVariables['DROPSENDTO_STARTUP_PROBE'] = '1'
    $startInfo.EnvironmentVariables['DROPSENDTO_DATA_ROOT'] = $DataRoot

    $process = $null
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $process = [System.Diagnostics.Process]::Start($startInfo)
        if ($null -eq $process) {
            throw 'Failed to start the probe process.'
        }

        if (-not $process.WaitForInputIdle(10000)) {
            throw 'Probe process did not reach an input-idle state within 10 seconds.'
        }

        $stopwatch.Stop()
        return $stopwatch.Elapsed.TotalMilliseconds
    }
    finally {
        $stopwatch.Stop()
        Stop-ProbeProcess -Process $process
    }
}

$realAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
$beforeState = @(Get-ProtectedFileState -AppDataRoot $realAppData)
$beforeJson = $beforeState | ConvertTo-Json -Compress -Depth 4
$probeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("DropSendTo-startup-probe-" + [Guid]::NewGuid().ToString('N'))
$samples = [System.Collections.Generic.List[double]]::new()

try {
    New-Item -ItemType Directory -Path $probeRoot | Out-Null

    $warmup = Measure-OneStartup -Executable $resolvedExecutable -DataRoot $probeRoot
    $probeConfig = Join-Path $probeRoot 'DropSendTo\config.json'
    $probeLog = Join-Path $probeRoot 'DropSendTo\logs\app.log'
    foreach ($probePath in @($probeConfig, $probeLog)) {
        if (-not (Test-Path -LiteralPath $probePath -PathType Leaf)) {
            throw "Expected probe output was not created: $probePath"
        }
        Assert-PathContained -Candidate (Resolve-Path -LiteralPath $probePath).Path -Root $probeRoot
    }

    for ($index = 1; $index -le $SampleCount; $index++) {
        $elapsed = Measure-OneStartup -Executable $resolvedExecutable -DataRoot $probeRoot
        $samples.Add($elapsed)
        Write-Host ("Sample {0}: {1:N1} ms" -f $index, $elapsed)
    }

    $ordered = @($samples | Sort-Object)
    if (($ordered.Count % 2) -eq 1) {
        $median = $ordered[[int][Math]::Floor($ordered.Count / 2)]
    }
    else {
        $upper = [int]($ordered.Count / 2)
        $median = ($ordered[$upper - 1] + $ordered[$upper]) / 2.0
    }
    $maximum = ($samples | Measure-Object -Maximum).Maximum

    $afterState = @(Get-ProtectedFileState -AppDataRoot $realAppData)
    $afterJson = $afterState | ConvertTo-Json -Compress -Depth 4
    if ($beforeJson -cne $afterJson) {
        throw 'The real user config/log file hashes or mtimes changed during the isolated probe.'
    }
    if ($maximum -ge $thresholdMilliseconds) {
        throw ("Warm-start requirement failed: max {0:N1} ms must be below {1:N0} ms." -f $maximum, $thresholdMilliseconds)
    }

    [pscustomobject]@{
        Executable = $resolvedExecutable
        WarmupMilliseconds = [Math]::Round($warmup, 1)
        SamplesMilliseconds = @($samples | ForEach-Object { [Math]::Round($_, 1) })
        MedianMilliseconds = [Math]::Round($median, 1)
        MaximumMilliseconds = [Math]::Round($maximum, 1)
        ThresholdMilliseconds = $thresholdMilliseconds
        ProbeDataRoot = $probeRoot
        UserDataUnchanged = $true
        Passed = $true
    }
}
finally {
    if (Test-Path -LiteralPath $probeRoot -PathType Container) {
        Remove-Item -LiteralPath $probeRoot -Recurse -Force
    }
}
