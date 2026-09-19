# DiskMonitor 💽

A sleek, lightweight Windows desktop disk monitor widget.

![Platform](https://img.shields.io/badge/Platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET-4.8-purple)
![Release](https://img.shields.io/badge/Version-1.0.2-green)

---

## ✨ Features

- **Mini & Expanded Views**: Discrete minimal bar docked at the top; click to expand for detailed disk usage.
- **Auto-Collapse**: Expands on click, and automatically collapses back to mini bar after 8 seconds (hovering resets the countdown).
- **Accurate Real-Time Drive Stats**: Monitor all active drives with color-coded usage bars.
- **Auto-Start With Windows**: Toggle auto-launch directly from the right-click context menu.
- **Ultra Lightweight**: Minimal CPU & memory footprint (~35 KB binary).

---

## 📥 Download & Installation

Download the latest installer from the **[Releases](https://github.com/MrKumaarism/DiskMonitor/releases)** section:

1. Download **`DiskMonitor-Setup-v1.02.exe`**
2. Run the setup wizard to install
3. DiskMonitor will launch and sit discreetly at the top edge of your screen.

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
