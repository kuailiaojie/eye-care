# 护眼助手 EyeCare 👀

一个基于 **Windows App SDK (WinUI 3)** 与 **Fluent Design** 构建的 Windows 护眼软件，帮助你缓解长时间使用电脑导致的数字眼疲劳。

## ✨ 功能特性

参考了 CareUEyes、Iris、Eye Saver、f.lux 等主流护眼软件的功能设计，核心能力包括：

| 功能 | 说明 |
|------|------|
| 🌙 蓝光过滤 | **两种实现方式**：叠加层（兼容所有显示器与 HDR）或系统级 **Gamma 校正**（f.lux / LightBulb 同款，直接压缩蓝光通道，全屏游戏同样生效） |
| 🔦 亮度调节 | 通过覆盖层降低屏幕亮度，1% 精度调节，避免刺眼；附 PWM 频闪消除建议 |
| 🌗 自动昼夜色温 | 白天偏白（默认 6500K）、夜间偏暖（默认 3500K），日出日落时段平滑过渡，保护褪黑素分泌 |
| ⏰ 休息提醒 | 支持 20-20-20 护眼法则与自定义间隔，短休息 / 长休息交替 |
| 🧠 智能暂停 | 检测到用户离开电脑（无输入 60 秒）时自动暂停计时 |
| 🎮 全屏自动暂停 | 检测到全屏游戏 / 视频 / 演示时自动暂停过滤与休息计时，退出全屏后恢复 |
| 🔒 强制休息 | 休息时全屏覆盖不可跳过，确保真正放松 |
| 🖥️ 系统托盘 | 驻留托盘，右键菜单快捷开关，关闭窗口最小化到托盘 |
| 🚀 开机自启 | 通过注册表 Run 键实现 |
| 💾 多显示器 | 覆盖所有显示器，各自独立叠加 |

### 20-20-20 护眼法则

每工作 20 分钟，远眺 20 英尺（约 6 米）外至少 20 秒，给眼睛一个放松的机会。

## 🔬 科学依据

- **蓝光过滤的定位**：Cochrane 2023 系统综述（17 项 RCT，619 名受试者）显示，防蓝光滤镜对缓解视疲劳的直接证据有限，但**夜间降低屏幕色温**对昼夜节律调节（褪黑素分泌、改善睡眠）有明确的生理学依据。因此本软件的夜间色温调度比单纯的"防蓝光"更有价值。
- **休息提醒是核心**：美国眼科学会（AAO）推荐的 20-20-20 规则是目前预防计算机视觉综合征（CVS）**最具循证支持**的手段——缓解睫状肌调节痉挛、促进眨眼。
- **调节方式差异**：叠加层（Alpha 遮罩）兼容性最好；Gamma 校正直接修改显示器查找表，无窗口开销，但 HDR 模式下系统接管色彩映射、可能不生效，此时请切回叠加层模式。

## 🛠️ 技术栈

- **Windows App SDK 1.6** + **WinUI 3**（原生桌面 UI）
- **.NET 8**
- **Fluent Design System**（卡片、InfoBar、导航视图等现代控件）
- **Win32 互操作**：`SetLayeredWindowAttributes` 分层透明窗口（蓝光/亮度覆盖层）、`SetDeviceGammaRamp`（系统级 Gamma 校正）、`Shell_NotifyIcon`（系统托盘）、`GetLastInputInfo`（智能暂停）、`GetForegroundWindow`（全屏检测）、注册表（自启动）

## 📁 项目结构

```
EyeCare/
├── App.xaml / App.xaml.cs           # 应用入口，初始化各服务
├── MainWindow.xaml(.cs)             # 主设置窗口（NavigationView）
├── BreakWindow.xaml(.cs)            # 全屏休息提醒窗口
├── Models/
│   └── AppSettings.cs               # 设置数据模型
├── Services/
│   ├── SettingsService.cs           # 设置持久化（JSON）
│   ├── FilterOverlayService.cs      # 蓝光过滤 + 亮度覆盖层
│   ├── GammaRampService.cs          # 系统级 Gamma 校正（f.lux 式）
│   ├── DayNightSchedule.cs          # 自动昼夜色温调度
│   ├── FullscreenPauseService.cs    # 全屏程序检测与自动暂停
│   ├── BreakReminderService.cs      # 休息提醒计时器
│   ├── TrayIconService.cs           # 系统托盘
│   └── StartupService.cs            # 开机自启动
├── Native/
│   └── NativeMethods.cs             # Win32 P/Invoke 声明
├── Pages/                           # 四个设置页面
│   ├── OverviewPage(.xaml/.cs)      # 概览
│   ├── FilterPage                   # 蓝光与亮度
│   ├── BreakPage                    # 休息提醒
│   └── SettingsPage                 # 常规设置
└── Assets/eye.ico                   # 应用图标
```

## 🔨 本地构建

**环境要求**：.NET 8 SDK + Windows 10/11（无需 Visual Studio，可用命令行构建）

```bash
# 还原依赖
dotnet restore EyeCare/EyeCare.csproj -p:Platform=x64 -r win-x64

# 编译
dotnet build EyeCare/EyeCare.csproj -c Release -p:Platform=x64 -r win-x64

# 发布自包含（免安装 .NET 运行时）
dotnet publish EyeCare/EyeCare.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish
```

> 若使用 Visual Studio，直接打开 `EyeCare.sln` 并选择 x64 / x86 / ARM64 平台运行即可。

## 🤖 GitHub Actions 自动构建

仓库已配置 `.github/workflows/build.yml`，在 `main` 分支 push、PR 或手动触发时，会在 `windows-latest` 上自动构建并发布 **x64 / x86 / arm64** 三个平台的自包含产物（打包为 Artifact）。

在页面 GitHub → **Actions** → 选择工作流 → **Run workflow** 即可手动触发，完成后到 Artifacts 下载对应平台的压缩包。

## 🏪 Microsoft Store 打包

应用已在 Partner Center 注册（Store ID `9N57F4STPJQD`），正式身份写入 `EyeCare/Package.appxmanifest`：

| 项目 | 值 |
|---|---|
| 包名 (Identity Name) | `DE3C23BA.666688A021C8` |
| 发布者 (Publisher) | `CN=AAC205F6-41D2-4FAD-8218-4E47E5D84363` |
| 包系列名 (PFN) | `DE3C23BA.666688A021C8_p7a589d6fj0mw` |
| Store 链接 | https://apps.microsoft.com/detail/9N57F4STPJQD |

**打包/提交流程**（任选其一）：

- **本地一键准备**（VS Developer Command Prompt）：
  ```bash
  powershell -ExecutionPolicy Bypass -File tools\prepare-store.ps1
  ```
  该脚本会校验 manifest 身份 → 构建 x64/x86/ARM64 三平台 MSIX → 生成 `EyeCare.msixbundle` 与 `EyeCare.msixupload` → 汇总商店资产到 `msix\upload\`。
- **CI 自动打包**：`.github/workflows/build-msix.yml` 在 `main` push 或手动触发时构建三平台 MSIX，并产出 `EyeCare.msixupload`（Store 上传格式）与 `EyeCare-Store-Upload` 资产包。
- 商店列表文案见 `store/Store-Listing.md`，图标资源由 `tools/gen_store_assets.py` 生成，隐私政策见 `PRIVACY.md`。

> 注意：每次提交到 Store 的包版本号必须高于上一版，改 `Package.appxmanifest` 中 `Identity` 的 `Version` 即可。

## 📝 使用说明

1. 启动后应用驻留系统托盘，自动按默认设置开启蓝光过滤（色温 4500K）与休息提醒（20-20-20）。
2. 点击托盘图标打开主界面进行详细设置。
3. 点击窗口关闭按钮（×）会最小化到托盘，托盘右键菜单「退出」才真正退出。
4. 设置保存在 `%LOCALAPPDATA%\EyeCare\settings.json`。

## ⚠️ 说明

- 蓝光过滤与亮度调节通过**分层透明覆盖窗口**实现（鼠标穿透、不影响正常操作），这是与 f.lux / Iris 等软件的通用做法；部分全屏独占游戏或硬件加速窗口（如某些视频播放器的独占模式）下覆盖层可能不生效，此时可切换到 **Gamma 校正** 模式或开启 **全屏自动暂停**。
- Gamma 校正模式在 HDR 显示器上可能不生效（系统接管色彩映射），建议 HDR 用户使用叠加层模式。
