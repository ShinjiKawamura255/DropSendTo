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
