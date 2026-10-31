# DiskMonitor 💽

A sleek, lightweight Windows desktop disk monitor widget.

![Platform](https://img.shields.io/badge/Platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET-4.8-purple)
![Release](https://img.shields.io/badge/Version-1.0.7-green)

---

## ✨ Features

- **Mini & Expanded Views**: Discrete minimal bar docked at the top; click to expand for detailed disk usage.
- **Fluid Pop-Up Animation**: Silky smooth 60 FPS ease-out animation when expanding from the mini bar into the detailed card and collapsing back, including smooth vertical expansion for nested utilities.
- **Auto-Collapse**: Expands on click, and automatically collapses back to mini bar after 8 seconds (hovering resets the countdown).
- **Accurate Real-Time Drive Stats**: Monitor all active drives with color-coded usage bars.
- **⏻ Built-In Power Timer**:
  - Schedule **Shutdown**, **Sleep**, or **Hibernate** on a countdown.
  - Quick presets (**15m, 30m, 45m, 60m**) or custom free-form minutes.
  - Interactive right-click quick actions and cancel toggle.
  - **Live Mini Bar Countdown**: When a timer is active, the collapsed bar displays a real-time ticking countdown with color alerts (yellow < 5m, red < 1m) instead of storage indicators.
  - **Audio-Visual 30-Second Warning**: Styled floating alert with warning sound, countdown bar, and "Cancel" / "Proceed Now" controls to prevent accidental shutdowns.
- **Always on Top**: Floats conveniently on top of all windows by default, with a one-click toggle in the right-click menu.
- **Triple-Redundancy Auto-Start**: Guaranteed startup at Windows boot and login via Windows Task Scheduler (logon trigger), Shell Startup folder shortcut, and Run registry key with Task Manager synchronization.
- **Ultra Lightweight**: Minimal CPU & memory footprint (~66 KB binary).

---

## 📥 Download & Installation

Choose the version that best fits your preference:

| Package | File | Size | Description |
|---|---|---|---|
| **🚀 Portable (Recommended)** | **[DiskMonitor.exe](https://github.com/MrKumaarism/DiskMonitor/raw/main/Portable/DiskMonitor.exe)**<br>*(or **[ZIP](https://github.com/MrKumaarism/DiskMonitor/raw/main/Portable/DiskMonitor-Portable-v1.0.7.zip)**)* | **~66 KB**<br>*(~35 KB zip)* | **Zero installation required.** Just download, place anywhere, and run. Supports **"Start with Windows"** and **"Power Timer"** via right-click! |
| **📦 Setup Wizard** | **[DiskMonitor-Setup-v1.07.exe](https://github.com/MrKumaarism/DiskMonitor/raw/main/Installer/DiskMonitor-Setup-v1.07.exe)** | **~2.1 MB** | **Standard Windows installer.** Installs to your user profile, creates Start Menu and Startup shortcuts, registers logon scheduled task, and includes an uninstaller in Windows Settings. |

### How to use:
- **Portable Version**: Download `DiskMonitor.exe` and double-click to launch. To have it start automatically on Windows login, right-click the bar and check **"Start with Windows"**.
- **Installer Version**: Download and run `DiskMonitor-Setup-v1.07.exe` to follow the setup wizard.

---

## 🛠️ Building from Source

### Prerequisites
- .NET Framework 4.8 SDK / Developer Pack
- Inno Setup 6 (for packaging the installer)

### Build & Package
```powershell
# Build application
dotnet build -c Release

# Compile installer
& "C:\Users\<User>\AppData\Local\Programs\Inno Setup 6\ISCC.exe" ".\DiskMonitorSetup.iss"
```
The output installer will be generated in the `Installer/` directory.

---

## 📄 License
DgLogiQ © 2026. All rights reserved.
