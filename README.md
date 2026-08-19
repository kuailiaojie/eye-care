# 护眼助手 EyeCare 👀

一个基于 **Windows App SDK (WinUI 3)** 与 **Fluent Design** 构建的 Windows 护眼软件，帮助你缓解长时间使用电脑导致的数字眼疲劳。

## ✨ 功能特性

参考了 CareUEyes、Iris、Eye Saver、f.lux 等主流护眼软件的功能设计，核心能力包括：

| 功能 | 说明 |
|------|------|
| 🌙 蓝光过滤 | 在每个显示器上叠加柔和的琥珀色滤镜，减少蓝光刺激，色温 1000K~10000K 可调 |
| 🔦 亮度调节 | 通过覆盖层降低屏幕亮度，1% 精度调节，避免刺眼 |
| ⏰ 休息提醒 | 支持 20-20-20 护眼法则与自定义间隔，短休息 / 长休息交替 |
| 🧠 智能暂停 | 检测到用户离开电脑（无输入 60 秒）时自动暂停计时 |
| 🔒 强制休息 | 休息时全屏覆盖不可跳过，确保真正放松 |
| 🖥️ 系统托盘 | 驻留托盘，右键菜单快捷开关，关闭窗口最小化到托盘 |
| 🚀 开机自启 | 通过注册表 Run 键实现 |
| 💾 多显示器 | 覆盖所有显示器，各自独立叠加 |

### 20-20-20 护眼法则

每工作 20 分钟，远眺 20 英尺（约 6 米）外至少 20 秒，给眼睛一个放松的机会。

## 🛠️ 技术栈

- **Windows App SDK 1.6** + **WinUI 3**（原生桌面 UI）
- **.NET 8**
- **Fluent Design System**（卡片、InfoBar、导航视图等现代控件）
- **Win32 互操作**：`SetLayeredWindowAttributes` 分层透明窗口（蓝光/亮度覆盖层）、`Shell_NotifyIcon`（系统托盘）、`GetLastInputInfo`（智能暂停）、注册表（自启动）

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

## 📝 使用说明

1. 启动后应用驻留系统托盘，自动按默认设置开启蓝光过滤（色温 4500K）与休息提醒（20-20-20）。
2. 点击托盘图标打开主界面进行详细设置。
3. 点击窗口关闭按钮（×）会最小化到托盘，托盘右键菜单「退出」才真正退出。
4. 设置保存在 `%LOCALAPPDATA%\EyeCare\settings.json`。

## ⚠️ 说明

蓝光过滤与亮度调节通过**分层透明覆盖窗口**实现（鼠标穿透、不影响正常操作），这是与 f.lux / Iris 等软件的通用做法；部分全屏独占游戏或硬件加速窗口（如某些视频播放器的独占模式）下覆盖层可能不生效，属正常现象。