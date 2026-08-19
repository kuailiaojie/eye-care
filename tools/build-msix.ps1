# 本地 MSIX 打包脚本
# 用法（在 Visual Studio Developer Command Prompt 中执行）：
#   powershell -ExecutionPolicy Bypass -File tools\build-msix.ps1
#   powershell -ExecutionPolicy Bypass -File tools\build-msix.ps1 -Version 1.0.1.0
#
# 产物（输出到 msix\）：
#   EyeCare-<arch>.msix     各平台独立包（x64 / x86 / ARM64）
#   EyeCare.msixbundle      多架构捆绑包
#   EyeCare.msixupload      Store 上传格式（提交 Partner Center 用这个）
#
# 注意：
# - 包身份（Name / Publisher / PublisherDisplayName）已写入 EyeCare\Package.appxmanifest，
#   对应 Partner Center 分配的正式身份（PFN: DE3C23BA.666688A021C8_p7a589d6fj0mw），
#   本脚本不再硬编码身份。
# - 若 -Version 未生效（版本仍为旧值），请直接修改 Package.appxmanifest 中 Identity 的 Version。

param(
    [string]$Version = "1.0.0.0",
    [switch]$SkipBundle
)

$ErrorActionPreference = "Stop"

$root = "$PSScriptRoot\.."
$project = "$root\EyeCare\EyeCare.csproj"
$outDir = "$root\msix"
$bundleDir = "$outDir\bundle"

New-Item -Path $outDir -ItemType Directory -Force | Out-Null

# 定位 Windows SDK 工具（MakeAppx / makemsixupload）
$kitsRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
$makeAppx = $null
$makeMsixUpload = $null
if (Test-Path $kitsRoot) {
    $makeAppx = Get-ChildItem "$kitsRoot\*\x64\MakeAppx.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    $makeMsixUpload = Get-ChildItem "$kitsRoot\*\x64\makemsixupload.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $makeAppx) { throw "MakeAppx.exe 未找到（请安装 Windows SDK，路径: $kitsRoot）" }

$platforms = @(
    @{ Name = "x64";   Platform = "x64";   Rid = "win-x64" }
    @{ Name = "x86";   Platform = "x86";   Rid = "win-x86" }
    @{ Name = "arm64"; Platform = "ARM64"; Rid = "win-arm64" }
)

# ---- 1. 构建各平台 MSIX ----
foreach ($p in $platforms) {
    Write-Host "=== 构建 MSIX ($($p.Name)) v$Version ===" -ForegroundColor Cyan
    msbuild $project /t:Restore /p:Configuration=Release /p:Platform=$($p.Platform) /p:RuntimeIdentifier=$($p.Rid) /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Restore 失败 ($($p.Name))" }
    msbuild $project /t:Build /p:Configuration=Release /p:Platform=$($p.Platform) /p:RuntimeIdentifier=$($p.Rid) `
        /p:WindowsPackageType=MSIX /p:GenerateAppxPackageOnBuild=True `
        /p:AppxPackageVersion=$Version `
        /p:AppxPackageOutput="$outDir\EyeCare-$($p.Name).msix" `
        /p:AppxPackageSigningEnabled=False /p:AppxBundle=Never /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "构建失败 ($($p.Name))" }
}

# ---- 2. 创建多架构 bundle ----
if (-not $SkipBundle) {
    Write-Host "=== 创建 EyeCare.msixbundle ===" -ForegroundColor Cyan
    New-Item -Path $bundleDir -ItemType Directory -Force | Out-Null
    Get-ChildItem "$outDir\EyeCare-*.msix" | Copy-Item -Destination $bundleDir -Force
    & $makeAppx bundle /d $bundleDir /p "$outDir\EyeCare.msixbundle" /v
    if ($LASTEXITCODE -ne 0) { throw "bundle 创建失败" }

    # ---- 3. 生成 Store 上传格式 .msixupload ----
    if ($makeMsixUpload) {
        Write-Host "=== 创建 EyeCare.msixupload ===" -ForegroundColor Cyan
        & $makeMsixUpload /p "$outDir\EyeCare.msixbundle" /o "$outDir\EyeCare.msixupload"
        if ($LASTEXITCODE -ne 0) { throw "msixupload 创建失败" }
    } else {
        Write-Warning "未找到 makemsixupload.exe，跳过 .msixupload 生成（bundle 也可直接上传 Store）"
    }
}

Write-Host "=== 构建完成 ===" -ForegroundColor Green
Get-ChildItem "$outDir\*.msix", "$outDir\*.msixbundle", "$outDir\*.msixupload" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  $($_.Name) - $([math]::Round($_.Length / 1MB, 2)) MB"
}
