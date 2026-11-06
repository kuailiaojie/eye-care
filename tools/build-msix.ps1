# Local MSIX and Portable Package Build Script
param(
    [string]$Version = "1.1.3.0",
    [string]$SingleArch = "",
    [switch]$SkipBundle,
    [bool]$BuildPortable = $true
)

$ErrorActionPreference = "Stop"

$root = "$PSScriptRoot\.."
$project = "$root\EyeCare\EyeCare.csproj"
$outDir = "$root\msix"
$distDir = "$root\dist"
$bundleDir = "$outDir\bundle"

New-Item -Path $outDir -ItemType Directory -Force | Out-Null
New-Item -Path $distDir -ItemType Directory -Force | Out-Null

# Locate dotnet.exe and MSBuild
$dotnet = "dotnet"
$userDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
if (Test-Path $userDotnet) {
    $dotnet = $userDotnet
    $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
    $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
}

$hasMsbuild = [bool](Get-Command msbuild -ErrorAction SilentlyContinue)

function Invoke-Build {
    param([string[]]$BuildArgs)
    if ($hasMsbuild) {
        & msbuild $BuildArgs
    } else {
        & $dotnet msbuild $BuildArgs
    }
    if ($LASTEXITCODE -ne 0) { throw "Build command failed: $BuildArgs" }
}

# Locate Windows SDK MakeAppx.exe
$makeAppx = $null
$makeMsixUpload = $null
$kits = "C:\Program Files (x86)\Windows Kits\10"
if (Test-Path $kits) {
    $foundAppx = Get-ChildItem "$kits\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($foundAppx) {
        $makeAppx = $foundAppx.FullName
    } else {
        $ack = Get-ChildItem "$kits\App Certification Kit\makeappx.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($ack) { $makeAppx = $ack.FullName }
    }

    $foundUpload = Get-ChildItem "$kits\bin\*\x64\makemsixupload.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($foundUpload) { $makeMsixUpload = $foundUpload.FullName }
}

$allPlatforms = @(
    @{ Name = "x64";   Platform = "x64";   Rid = "win-x64" }
    @{ Name = "x86";   Platform = "x86";   Rid = "win-x86" }
    @{ Name = "arm64"; Platform = "ARM64"; Rid = "win-arm64" }
)

$platforms = if ($SingleArch) {
    $allPlatforms | Where-Object { $_.Name -ieq $SingleArch }
} else {
    $allPlatforms
}

# ---- 1. Build MSIX Packages ----
foreach ($p in $platforms) {
    Write-Host "=== Building MSIX ($($p.Name)) v$Version ===" -ForegroundColor Cyan
    Invoke-Build @(
        $project,
        "/t:Build",
        "/p:Configuration=Release",
        "/p:Platform=$($p.Platform)",
        "/p:RuntimeIdentifier=$($p.Rid)",
        "/p:WindowsPackageType=MSIX",
        "/p:GenerateAppxPackageOnBuild=True",
        "/p:AppxPackageVersion=$Version",
        "/p:AppxPackageOutput=$outDir\EyeCare-$($p.Name).msix",
        "/p:AppxPackageSigningEnabled=False",
        "/p:AppxBundle=Never",
        "/v:minimal"
    )
    Copy-Item "$outDir\EyeCare-$($p.Name).msix" "$distDir\EyeCare-v$Version-$($p.Name).msix" -Force
}

# ---- 2. Create Multi-Arch Bundle ----
if (-not $SkipBundle -and -not $SingleArch -and $makeAppx) {
    Write-Host "=== Creating EyeCare.msixbundle ===" -ForegroundColor Cyan
    New-Item -Path $bundleDir -ItemType Directory -Force | Out-Null
    Get-ChildItem "$outDir\EyeCare-*.msix" | Copy-Item -Destination $bundleDir -Force
    & $makeAppx bundle /d $bundleDir /p "$outDir\EyeCare.msixbundle" /o /v
    if ($LASTEXITCODE -eq 0) {
        Copy-Item "$outDir\EyeCare.msixbundle" "$distDir\EyeCare-v$Version.msixbundle" -Force

        # ---- 3. Generate Store .msixupload ----
        if ($makeMsixUpload) {
            Write-Host "=== Creating EyeCare.msixupload ===" -ForegroundColor Cyan
            & $makeMsixUpload /p "$outDir\EyeCare.msixbundle" /o "$outDir\EyeCare.msixupload"
            if ($LASTEXITCODE -eq 0) {
                Copy-Item "$outDir\EyeCare.msixupload" "$distDir\EyeCare-v$Version.msixupload" -Force
            }
        }
    }
}

# ---- 4. Build Unpackaged Portable App (Loose Files & Zip) ----
if ($BuildPortable) {
    Write-Host "=== Building Unpackaged Portable App (win-x64) ===" -ForegroundColor Cyan
    $portableOut = [System.IO.Path]::GetFullPath("$distDir\EyeCare-win-x64-portable")
    $runningProcesses = Get-Process -Name "EyeCare" -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and [System.IO.Path]::GetFullPath($_.Path).StartsWith($portableOut, [System.StringComparison]::OrdinalIgnoreCase)
        } catch { $false }
    }
    if ($runningProcesses) {
        Write-Host "Stopping running portable EyeCare process..." -ForegroundColor Yellow
        $runningProcesses | Stop-Process -Force
        Start-Sleep -Seconds 1
    }
    if (Test-Path $portableOut) { Remove-Item $portableOut -Recurse -Force }

    & $dotnet publish $project -c Release -p:Platform=x64 -r win-x64 --self-contained true `
        -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -o $portableOut /v:minimal

    if ($LASTEXITCODE -eq 0) {
        Write-Host "=== Creating Portable Zip Archive ===" -ForegroundColor Cyan
        $zipPath = "$distDir\EyeCare-v$Version-win-x64-portable.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path "$portableOut\*" -DestinationPath $zipPath -Force
    } else {
        Write-Warning "Portable build failed. Check logs."
    }
}

Write-Host ""
Write-Host "Build completed successfully! Distribution artifacts:" -ForegroundColor Green
Get-ChildItem "$distDir\*" -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.PSIsContainer) {
        $count = (Get-ChildItem $_.FullName -Recurse).Count
        Write-Host "  [Directory] $($_.Name)\ ($count files)" -ForegroundColor Yellow
    } else {
        $mb = [math]::Round($_.Length / 1MB, 2)
        Write-Host "  [Package]   $($_.Name) - $mb MB" -ForegroundColor Cyan
    }
}
