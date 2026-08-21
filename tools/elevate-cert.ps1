# Runs elevated (via UAC) to import the dev signing cert into LocalMachine stores.
# MSIX deployment validates the chain against machine-level stores only;
# CurrentUser TrustedPeople/Root is not consulted -> 0x800B0109.
param([string]$CerPath)
$ErrorActionPreference = "Stop"
Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Import-Certificate -FilePath $CerPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Set-Content -Path (Join-Path $env:TEMP "eyecare-cert-imported.txt") -Value "OK $(Get-Date)" -Encoding ASCII
