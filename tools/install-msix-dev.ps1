# Dev-install an unsigned Store-bound MSIX (re-runnable).
# Flow:
#   1. extract pristine unsigned copy from the downloaded artifact zip
#      (an in-place re-signed file accumulates multiple signatures, which
#      breaks MSIX deployment with 0x87E80034 GetManifestReader)
#   2. self-signed CA code-signing cert (REUSED across runs; CN = manifest Publisher)
#   3. signtool sign ONCE
#   4. trust cert in LocalMachine (one UAC prompt, skipped if already trusted)
#   5. Add-AppxPackage
# NOTE: keep this file pure ASCII - PS 5.1 reads BOM-less UTF-8 as ANSI/GBK.
$ErrorActionPreference = "Stop"

$bundlePath = "C:\Users\kxin\Downloads\Compressed\EyeCare-Store-Upload_3\EyeCare.msixbundle"
$work = Join-Path $env:TEMP "eyecare-sign"
$cleanDir = Join-Path $work "frombundle"
New-Item -Path $cleanDir -ItemType Directory -Force | Out-Null
# The Store bundle packages the ORIGINAL unsigned per-arch MSIX files verbatim,
# so extracting x64 from it yields a pristine copy (the loose .msix in the
# download folder may carry stale signatures from re-signing).
# Expand-Archive only accepts .zip - copy the bundle to a .zip name first.
$bundleZip = Join-Path $cleanDir "bundle.zip"
Copy-Item $bundlePath $bundleZip -Force
Expand-Archive -Path $bundleZip -DestinationPath $cleanDir -Force
$msix = Join-Path $cleanDir "EyeCare-x64.msix"
if (-not (Test-Path $msix)) { throw "EyeCare-x64.msix not found in bundle" }
Write-Host "pristine x64 msix extracted from bundle: $msix"

$pfx = Join-Path $work "eye-dev.pfx"
$cer = Join-Path $work "eye-dev.cer"
# PFX 仅用于本次运行的临时签名，使用随机口令，避免在仓库中保留口令。
$pfxPwd = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
$pwd = ConvertTo-SecureString $pfxPwd -Force -AsPlainText
$publisher = "CN=AAC205F6-41D2-4FAD-8218-4E47E5D84363"

Write-Host "=== 1. cert (reuse or create CA code-signing cert) ==="
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher `
        -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy Exportable `
        -KeyAlgorithm RSA -KeyLength 2048 `
        -TextExtension @(
            "2.5.29.19={text}CA=TRUE",
            "2.5.29.37={text}1.3.6.1.5.5.7.3.3"
        )
    Write-Host "created CA cert: $($cert.Thumbprint)"
} else {
    Write-Host "reusing cert: $($cert.Thumbprint)"
}
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null

Write-Host "=== 2. locate signtool ==="
$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw "signtool.exe not found" }
Write-Host "signtool: $signtool"

Write-Host "=== 3. sign ONCE ==="
& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint /f $pfx /p $pfxPwd /d "EyeCare" $msix
if ($LASTEXITCODE -ne 0) { throw "signtool failed (exit $LASTEXITCODE)" }
$sigCount = (& $signtool verify /all $msix 2>&1 | Select-String -Pattern "Index of signature" | Measure-Object).Count
Write-Host "signatures on package: $sigCount (must be 1)"
if ($sigCount -ne 1) { throw "package has $sigCount signatures - refusing to install" }

Write-Host "=== 4. trust cert in LocalMachine ==="
$alreadyTrusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
if (-not $alreadyTrusted) {
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
    $marker = Join-Path $env:TEMP "eyecare-cert-imported.txt"
    Remove-Item $marker -Force -ErrorAction SilentlyContinue
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) {
        Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
        Write-Host "already admin - imported directly to LocalMachine"
    } else {
        Write-Host "elevating cert import to LocalMachine (please accept the UAC prompt)..."
        $elevateScript = Join-Path $PSScriptRoot "elevate-cert.ps1"
        try {
            Start-Process powershell.exe -Verb RunAs -Wait `
                -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$elevateScript`"", "`"$cer`"")
        } catch {
            throw "UAC declined or elevation failed: $($_.Exception.Message)"
        }
        if (-not (Test-Path $marker)) { throw "elevated import did not complete (marker missing)" }
        Write-Host "LocalMachine import done (UAC accepted)"
    }
} else {
    Write-Host "cert already trusted in LocalMachine - skipping UAC"
}

Write-Host "=== 5. install MSIX ==="
Add-AppxPackage -Path $msix -ErrorAction Stop
Write-Host "INSTALL OK"

$pkg = Get-AppxPackage -Name "DE3C23BA.666688A021C8"
if ($pkg) {
    Write-Host "Name: $($pkg.Name)"
    Write-Host "Version: $($pkg.Version)"
    Write-Host "Arch: $($pkg.Architecture)"
    Write-Host "InstallLocation: $($pkg.InstallLocation)"
    Write-Host "PFN: $($pkg.PackageFamilyName)"
} else {
    Write-Host "WARNING: package not found after install"
    exit 2
}
