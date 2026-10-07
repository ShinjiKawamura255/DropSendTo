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
Write-Output 'Release artifact tests passed (documents, signing failure, signature verification).'
