# Microsoft Store 提交准备脚本
# 1. 构建三平台 MSIX + bundle + .msixupload（调用 build-msix.ps1）
# 2. 收集 Store 提交资产（图标 PNG / 商店列表 / 隐私政策）→ msix\upload\assets.zip
# 3. 校验 Package.appxmanifest 身份与 Partner Center 分配值一致
# 4. 生成商店身份速查文件 store-identity.md，供 Partner Center 填报时复制粘贴
#
# 用法：powershell -ExecutionPolicy Bypass -File tools\prepare-store.ps1 [-SkipBuild]
# 产物：msix\upload\ 目录下所有文件

param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$root = "$PSScriptRoot\.."
$msixDir = "$root\msix"
$uploadDir = "$root\msix\upload"
$storeDir = "$root\store"
$manifestPath = "$root\EyeCare\Package.appxmanifest"

# ---- Partner Center 分配的正式身份（请勿修改）----
$store = [ordered]@{
    "包名 (Package Identity Name)" = "DE3C23BA.666688A021C8"
    "发布者 (Publisher)"           = "CN=AAC205F6-41D2-4FAD-8218-4E47E5D84363"
    "发布者显示名 (PublisherDisplayName)" = "块了解"
    "包系列名 (PFN)"               = "DE3C23BA.666688A021C8_p7a589d6fj0mw"
    "包 SID"                       = "S-1-15-2-2625154744-1510418594-1530711297-101362933-1894812014-2551522639-3939733232"
    "Store ID"                     = "9N57F4STPJQD"
    "Store 链接"                   = "https://apps.microsoft.com/detail/9N57F4STPJQD"
    "Store 协议链接"               = "ms-windows-store://pdp/?productid=9N57F4STPJQD"
    "MSA 应用 ID"                  = "a33bf7af-64f3-4984-b60f-8093300d6852"
}

# ---- 0. 校验 manifest 身份与 Partner Center 一致 ----
Write-Host "0. 校验 Package.appxmanifest 身份..." -ForegroundColor Cyan
# 注意：PS 5.1 的 Get-Content 默认按 ANSI/GBK 读取，UTF-8 中文会被破坏，必须显式指定编码
[xml]$manifest = Get-Content -Path $manifestPath -Raw -Encoding UTF8
$checks = @(
    @{ 标签 = "Identity/Name";            实际 = $manifest.Package.Identity.Name;          期望 = $store["包名 (Package Identity Name)"] }
    @{ 标签 = "Identity/Publisher";       实际 = $manifest.Package.Identity.Publisher;     期望 = $store["发布者 (Publisher)"] }
    @{ 标签 = "Properties/PublisherDisplayName"; 实际 = $manifest.Package.Properties.PublisherDisplayName; 期望 = $store["发布者显示名 (PublisherDisplayName)"] }
)
$mismatch = $false
foreach ($c in $checks) {
    if ($c.实际 -ne $c.期望) {
        Write-Host "  ✗ $($c.标签) 不一致：manifest='$($c.实际)' 期望='$($c.期望)'" -ForegroundColor Red
        $mismatch = $true
    } else {
        Write-Host "  ✓ $($c.标签) = $($c.实际)" -ForegroundColor Green
    }
}
if ($mismatch) { throw "manifest 身份与 Partner Center 不一致，请先修正 EyeCare\Package.appxmanifest" }

# ---- 1. 构建 MSIX ----
if (-not $SkipBuild) {
    Write-Host "1. 构建 MSIX（三平台 + bundle + msixupload）" -ForegroundColor Cyan
    & "$PSScriptRoot\build-msix.ps1"
    if ($LASTEXITCODE -ne 0) { throw "build-msix.ps1 失败" }
} else {
    Write-Host "1. 跳过构建（-SkipBuild）" -ForegroundColor DarkGray
}

# ---- 2. 收集 Store 提交资产 ----
Write-Host "2. 收集 Store 提交资产..." -ForegroundColor Cyan
New-Item -Path $uploadDir -ItemType Directory -Force | Out-Null

# 图标资源（由 tools\gen_store_assets.py 从 eye.ico 生成，勿用 .ico 冒充 .png）
Get-ChildItem "$storeDir\*.png" | Copy-Item -Destination $uploadDir -Force
# 商店列表与隐私政策（供填报时复制）
Copy-Item "$storeDir\Store-Listing.md"  $uploadDir -Force
Copy-Item "$root\PRIVACY.md"            $uploadDir -Force
# MSIX 产物
Get-ChildItem "$msixDir\*.msixupload", "$msixDir\*.msixbundle", "$msixDir\EyeCare-*.msix" -ErrorAction SilentlyContinue |
    Copy-Item -Destination $uploadDir -Force

# 资产 ZIP（图标 PNG 打包，便于一次性上传）
Compress-Archive -Path "$uploadDir\*.png" -DestinationPath "$uploadDir\assets.zip" -Force

# ---- 3. 生成商店身份速查文件 ----
Write-Host "3. 生成 store-identity.md..." -ForegroundColor Cyan
$lines = @("# EyeCare 商店身份速查（Partner Center 分配）", "")
foreach ($k in $store.Keys) {
    $lines += "- **$k**：``$($store[$k])``"
}
$lines += "", "> 打包身份以 EyeCare\Package.appxmanifest 为准；本文件仅用于 Partner Center 填报对照。"
$lines | Out-File -FilePath "$uploadDir\store-identity.md" -Encoding UTF8

Write-Host ""
Write-Host "✅ 打包准备完成" -ForegroundColor Green
Write-Host "上传目录: $uploadDir"
Write-Host ""
Write-Host "产物清单：" -ForegroundColor Cyan
Get-ChildItem $uploadDir -File | ForEach-Object {
    Write-Host "  $($_.Name) - $([math]::Round($_.Length / 1KB, 1)) KB"
}
Write-Host ""
Write-Host "下一步：" -ForegroundColor Yellow
Write-Host "  1. 打开 https://partner.microsoft.com/dashboard 进入应用「护眼助手」(Store ID: $($store['Store ID']))"
Write-Host "  2. 新建提交 → 上传包：选择 EyeCare.msixupload（或 .msixbundle）"
Write-Host "  3. 按 store\Store-Listing.md 填写描述，上传图标（assets.zip 内含各尺寸 PNG）"
Write-Host "  4. 隐私政策 URL：https://github.com/kuailiaojie/eye-care/blob/main/PRIVACY.md"
Write-Host "  5. 提交送审（每次提交包版本号必须高于上一版，改 Package.appxmanifest 的 Version）"
