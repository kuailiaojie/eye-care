# 隐私政策 — 护眼助手 (EyeCare)

最后更新：2026-08-19

## 数据收集

护眼助手 (EyeCare) **不收集、不存储、不传输**任何用户个人数据。

- 不联网：应用无任何网络请求功能
- 不收集：不采集任何个人信息、使用习惯或系统配置
- 不存储：设置数据仅保存在本地 `%LOCALAPPDATA%\EyeCare\settings.json`，用于记住用户的偏好

## 本地存储

以下数据仅保存在用户本机：

| 数据 | 位置 | 用途 |
|---|---|---|
| 用户设置 | %LOCALAPPDATA%\EyeCare\settings.json | 蓝光强度、亮度、休息间隔等偏好 |
| 开机自启 | 注册表 HKCU\Software\Microsoft\Windows\CurrentVersion\Run | 可选的开机自动启动 |

以上数据不会上传到任何服务器。

## 权限说明

| 权限 | 用途 |
|---|---|
| runFullTrust | 完全信任应用，用于蓝光过滤覆盖窗口、屏幕亮度调节、系统托盘图标 |
| 注册表写入 | 仅用于开机自启动设置 |

## 第三方服务

本应用不使用任何第三方分析、广告或追踪服务。

## 儿童隐私

本应用不针对 13 岁以下儿童，也不会 knowingly 收集儿童信息。

## 联系方式

如有隐私相关问题，请提交 Issue：
https://github.com/kuailiaojie/eye-care/issues

---

© 2026 kuailiaojie. All rights reserved.
