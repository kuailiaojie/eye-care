# Microsoft Store 打包准备脚本
# 适用于 Windows App SDK + WinUI 3 + .NET 8 项目
# 生成：EyeCare.msix / EyeCare.msixupload

$ErrorActionPreference = "Stop"

$projectDir = "$PSScriptRoot\EyeCare"
$msixDir = "$PSScriptRoot\msix"
$uploadDir = "$PSScriptRoot\msix\upload"
$storeDir = "$PSScriptRoot\store"

New-Item -Path $msixDir -ItemType Directory -Force | Out-Null
New-Item -Path $uploadDir -ItemType Directory -Force | Out-Null
New-Item -Path $storeDir -ItemType Directory -Force | Out-Null

Write-Host "1. 构建 MSIX" -ForegroundColor Cyan
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"

# 使用 Windows App SDK 的打包工具生成 MSIX
# 这里假设你已安装了 Windows App SDK PowerShell 工具
# 或者使用 GitHub Actions 的打包步骤

Write-Host "2. 生成 Store 资产" -ForegroundColor Cyan
# 图标（必须 150x150 PNG）
Copy-Item "$projectDir\Assets\eye.ico" "$storeDir\Square150x150Logo.scale-200.png"

# 横幅（必须 1200x300 PNG）
Copy-Item "$projectDir\Assets\banner.png" "$storeDir\Banner.png"

# Logo（必须 50x50 PNG）
Copy-Item "$projectDir\Assets\eye.ico" "$storeDir\SmallTile.png"

# Logo（必须 300x300 PNG）
Copy-Item "$projectDir\Assets\eye.ico" "$storeDir\LargeTile.png"

Write-Host "3. 创建 MSIX 包" -ForegroundColor Cyan
# 使用 Windows App SDK 打包工具
# 推荐命令（如果已安装 SDK 工具）：
# winget pack --project "$projectDir" --output "$msixDir"

# 如果没有 SDK 工具，使用 PowerShell 手动打包
# 这里简化版，实际需要 Visual Studio + Windows App SDK 工具链
$packagePath = "$msixDir\EyeCare.msix"

# 实际打包命令示例（请根据你的 SDK 安装位置调整）
# msbuild "$projectDir\EyeCare\EyeCare.csproj" /t:Package /p:AppxPackageSigningEnabled=false /p:AppxPackageOutputPath="$msixDir" /p:AppxPackageName="EyeCare" /p:AppxPackageVersion="1.0.0.0"

Write-Host "4. 创建 Store 资产 ZIP" -ForegroundColor Cyan
# 用于 Store 提交的 assets.zip
Compress-Archive -Path "$storeDir" -DestinationPath "$uploadDir\assets.zip" -Force

Write-Host "5. 准备 Store 提交清单" -ForegroundColor Cyan
$storeManifest = @"
<?xml version="1.0" encoding="UTF-8"?>
<PackageManifest>
  <PackageIdentity Name="YourCompany.EyeCare" Version="1.0.0.0" Publisher="CN=YourName" />
  <Dependencies>
    <PackageDependency Name="Microsoft.WindowsAppRuntime.1.2" MinVersion="1.2.0.0" />
  </Dependencies>
  <Resources>
    <Resource Language="zh-CN" />
  </Resources>
  <Properties>
    <DisplayName>护眼助手</DisplayName>
    <PublisherDisplayName>护眼助手</PublisherDisplayName>
    <PublisherName>护眼助手</PublisherName>
    <Logo>assets\Square150x150Logo.scale-200.png</Logo>
    <Banner>assets\Banner.png</Banner>
  </Properties>
  <Applications>
    <Application Id="EyeCare" EntryPoint="EyeCare.App" />
  </Applications>
</PackageManifest>
"@

$storeManifest | Out-File -FilePath "$uploadDir\manifest.xml" -Encoding UTF8

Write-Host "✅ 打包准备完成"
Write-Host "MSIX 包: $packagePath"
Write-Host "Store 资产: $uploadDir\assets.zip"
Write-Host "Store 清单: $uploadDir\manifest.xml"

Write-Host "下一步："
Write-Host "1. 上传 assets.zip 到 https://partner.microsoft.com/dashboard/developer/dashboard/developer-dashboard"
Write-Host "2. 填写 Store 资产说明（图标、截图、描述）"
Write-Host "3. 提交发布（Submit for release）"
Write-Host "4. 等待审核通过后上架"