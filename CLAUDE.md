# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

EyeCare (护眼助手) is a Windows-only eye-care utility: a WinUI 3 desktop app (.NET 8, Windows App SDK 1.6) that overlays blue-light filtering, controls brightness, runs 20-20-20 break reminders, lives in the system tray, and shows a desktop widget. It is distributed as a self-contained MSIX from the Microsoft Store (Store ID `9N57F4STPJQD`). UI strings and code comments are Chinese.

There is no unit-test project. The only automated check besides building is `python tools/validate_ui_regressions.py`.

## Commands

All builds are Windows-only and require a platform + RID:

```bash
# Restore / build / self-contained publish (x64 example)
dotnet restore EyeCare/EyeCare.csproj -p:Platform=x64 -r win-x64
dotnet build   EyeCare/EyeCare.csproj -c Release -p:Platform=x64 -r win-x64
dotnet publish EyeCare/EyeCare.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish

# UI regression guard (enforces the page/handler invariants listed below)
python tools/validate_ui_regressions.py

# Local MSIX build (run inside a VS Developer Command Prompt; needs Windows SDK MakeAppx)
powershell -ExecutionPolicy Bypass -File tools\build-msix.ps1 [-Version 1.0.1.0]
powershell -ExecutionPolicy Bypass -File tools\prepare-store.ps1 [-SkipBuild]
```

`global.json` pins .NET SDK 8.0.402 (roll-forward latestPatch). Supported platforms/RIDs: `x64/win-x64`, `x86/win-x86`, `ARM64/win-arm64`.

CI: `.github/workflows/build.yml` publishes all three architectures; `build-msix.yml` produces per-arch MSIX + `EyeCare.msixbundle` (and `.msixupload` only if `makemsixupload.exe` exists — newer SDKs removed it, so the bundle is uploaded directly); `publish-microsoft-store.yml` submits the bundle to Partner Center on pushes to `main`, but refuses to overwrite a pending submission. CI generates a monotonic four-part package version and does not modify the manifest; bump `Identity Version` in `EyeCare/Package.appxmanifest` for a manual Store submission.

## Architecture

### Composition root and global state

`EyeCare/App.xaml.cs` is the entire composition root. It constructs every service in `OnLaunchedCore` and exposes them as static singletons on `App` (`App.Settings`, `App.FilterOverlay`, `App.GammaRamp`, `App.BreakReminder`, `App.TrayIcon`, `App.FullscreenPause`, `App.Widget`, …). Pages, windows, and services reach dependencies through these statics rather than DI.

Two cross-cutting rules:

- **All filter changes go through `App.ApplyFilters()`** (overlay + Gamma), never by calling one service alone. Pages mutate `App.Settings.Data`, call `App.Settings.Save()`, then `App.ApplyFilters()`.
- **Background threads must marshal to `App.UiDispatcher`.** `BreakReminderService`, `FullscreenPauseService`, `DesktopWidgetService`, and tray callbacks all fire off the UI thread; `App` translates their events with `UiDispatcher.TryEnqueue`.

### Settings

`Models/AppSettings.cs` is the single settings model, serialized as JSON (string enums) to `%LOCALAPPDATA%\EyeCare\settings.json` by `SettingsService`. `Save()` is what raises `SettingsChanged`, which `DesktopWidgetService` subscribes to for show/hide. Settings are loaded once at startup; there is a legacy migration for brightness `>= 0.999`.

### Blue-light filtering — two mutually exclusive modes

`FilterMode` selects exactly one implementation; both read `DayNightSchedule.GetEffectiveColorTemperature` so automatic day/night temperature applies to either.

- **Overlay** (`FilterOverlayService`): one layered topmost Win32 popup per monitor for amber tint plus a second black popup per monitor for dimming. Windows use `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, return `HTTRANSPARENT` for `WM_NCHITTEST`, and have alpha/visibility updated by `SetLayeredWindowAttributes`. Overlay filtering is disabled when `FilterMode == GammaRamp` to avoid double filtering.
- **GammaRamp** (`GammaRampService`): enumerates attached displays, saves each original ramp, writes a Tanner Helland-derived ramp via `SetDeviceGammaRamp`, re-applies every 10 s, and restores originals on `Disable()`/exit. Does not work on HDR displays.

`FullscreenPauseService` polls the foreground window every 2 s and suspends overlays and break timing while a fullscreen app is active. Both filter services also consult `App.FullscreenPause`.

### Break reminder and windows

`BreakReminderService` runs a 1 s `Timer`, exposes `WorkTick`/`BreakTick`/`BreakStarted`/`BreakFinished`, and pauses on fullscreen or on user idle >60 s (`GetLastInputInfo`). `App` subscribes to those events once at startup and routes them to `MainWindow`, which owns the singleton `BreakWindow` (fullscreen on the primary monitor's work area; subscribes to `BreakTick` itself for the countdown). `EnforceBreak` hides the skip button.

`MainWindow` is a `NavigationView` hosting four `Pages/` (Overview, Filter, Break, Settings). Closing it hides it to the tray when `MinimizeToTray`; `App._window` is nulled on close and `App.ShowMainWindow()` recreates it. Tray/widget callbacks must use `ShowMainWindow()`, never a stale window reference.

`DesktopWidgetService` creates `WidgetWindow` when `DesktopWidgetEnabled`, refreshes it every second with the same pause logic as the break service, and hides it when disabled. `WidgetWindow` is borderless/topmost/tool-window, sizes itself by measuring `CardBorder` (`FitToContent`) in physical pixels scaled by DPI, restores a persisted position (default: top-right of the primary work area), and debounces drag-position saves into `WidgetX/WidgetY`.

### Tray

`TrayIconService` runs its own STA thread with a native message loop and `Shell_NotifyIcon` (`NOTIFYICON_VERSION_4`). Read mouse notifications from the **low word** of `lParam`; an exception escaping its `WndProc` kills the process, so every branch is wrapped in try/catch. It only raises events — `App` marshals them to the UI thread.

### Shutdown and diagnostics

`App.ExitApp()` is the only real exit path: restore Gamma ramps first, then dispose fullscreen/overlay/break/tray/widget, then close the window. Set `_isExiting` first so window-close handlers do not just hide to tray.

Startup crash diagnosis writes to `%LOCALAPPDATA%\EyeCare\crash.log`. Add `App.Trace("stage")` around risky startup steps and `App.LogCrash` in catch blocks; widget construction already logs stage-by-stage because a bad XAML resource there previously killed startup.

## UI conventions and known pitfalls

- Every page sets `_loading = true` while populating controls in `Loaded` and returns early from **every** `ValueChanged`/`Toggled`/`SelectionChanged` handler when `_loading` is true. `tools/validate_ui_regressions.py` enforces this for the Filter page handlers — keep it passing.
- Page content must stay `HorizontalAlignment="Center"` with the inner `StackPanel` not pinned `Left`; the same script checks all four pages.
- Use WinUI 3 resource keys only. UWP-only keys such as `AcrylicInAppBrush` do not exist in WinUI 3 and crash at startup (fixed in `090a431`); theme brushes resolved in code must fall back to literal colors (see `WidgetWindow.ResolveThemeBrush`).
- Work in DIPs in XAML but physical pixels for `AppWindow.Resize`/`Move`/`Position`; scale by `XamlRoot.RasterizationScale` or monitor DPI. The app is PerMonitorV2 DPI-aware.
- P/Invoke must stay pointer-size correct across x86/x64/ARM64 (use `GetWindowLongPtr`/`SetWindowLongPtr`, `IntPtr`/`UIntPtr`) — ARM64/x86 packaging is part of CI.
- Do not change the Partner Center identity in `Package.appxmanifest` (`Name`, `Publisher`, `PublisherDisplayName`); `prepare-store.ps1` validates them.
