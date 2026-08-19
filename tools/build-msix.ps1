# 本地 MSIX 打包脚本（可选，GitHub Actions 也可自动打包）
# 用法：在 VS Developer Command Prompt 中运行 powershell -File tools\build-msix.ps1

$ErrorActionPreference = "Stop"

$root = "$PSScriptRoot\.."
$project = "$root\EyeCare\EyeCare.csproj"
$outDir = "$root\msix"

New-Item -Path $outDir -ItemType Directory -Force | Out-Null

Write-Host "=== 构建 MSIX (x64) ===" -ForegroundColor Cyan
msbuild $project /t:Build /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
  /p:WindowsPackageType=MSIX /p:GenerateAppxPackageOnBuild=True `
  /p:AppxPackageOutputPath="$outDir" /p:AppxPackageSigningEnabled=False `
  /p:AppxBundle=Never

Write-Host "=== MSIX 构建完成 ===" -ForegroundColor Green
Get-ChildItem "$outDir\*.msix" | ForEach-Object {
    Write-Host "  $($_.Name) - $([math]::Round($_.Length / 1MB, 2)) MB"
}
