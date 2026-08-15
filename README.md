# Zidimi Uninstaller

A modern, fast, and thorough software management and uninstallation utility for Windows.

[![Release](https://img.shields.io/github/v/release/khanh779-9/zidimi-uninstaller?style=flat-square&color=0078D4)](https://github.com/khanh779-9/zidimi-uninstaller/releases)
[![License: MIT](<https://img.shields.io/badge/License-MIT%20with%20Trademark%20Protection-blue.svg?style=flat-square>)](LICENSE)
[![Platform](<https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D4?style=flat-square&logo=windows>)](https://github.com/khanh779-9/zidimi-uninstaller)
[![.NET](https://img.shields.io/badge/.NET-8.0--windows-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)

---

## Table of Contents

- [Overview](#overview)
- [Screenshots](#screenshots)
- [Feature Overview Matrix](#feature-overview-matrix)
- [Comparison with Windows Default Uninstaller](#comparison-with-windows-default-uninstaller)
- [Detailed Module Capabilities](#detailed-module-capabilities)
  - [1. Desktop Applications Management](#1-desktop-applications-management)
  - [2. Windows Store Apps (UWP / MSIX / AppX)](#2-windows-store-apps-uwp--msix--appx)
  - [3. Windows Optional Features](#3-windows-optional-features)
  - [4. Package Manager (WinGet)](#4-package-manager-winget)
  - [5. Startup Manager](#5-startup-manager)
  - [6. Deep Clean and Residue Removal](#6-deep-clean-and-residue-removal)
  - [7. Safety and Process Hunter](#7-safety-and-process-hunter)
- [Supported Languages](#supported-languages)
- [Configuration and Settings](#configuration-and-settings)
- [System Requirements](#system-requirements)
- [Download and Installation](#download-and-installation)
- [Building from Source](#building-from-source)
  - [Prerequisites](#prerequisites)
  - [Build Steps](#build-steps)
- [License and Trademark Notice](#license-and-trademark-notice)
- [Contributing and Support](#contributing-and-support)

---

## Overview

**Zidimi Uninstaller** is an open-source Windows uninstallation and system maintenance tool built with **WPF (.NET 8)**. It provides a centralized, high-performance interface to monitor, manage, and thoroughly remove desktop applications, Windows Store apps (UWP/MSIX), Windows optional features, and package manager packages, while eliminating orphaned residue in the file system and Windows Registry.

---

## Screenshots

### Dashboard

![Dashboard](screenshot/dashboard.png)

### Installed Applications

![Installed Applications](screenshot/installed_app.png)

### Store Apps

![Microsoft Store Apps](screenshot/store_app.png)

### Windows Features (DISM)

![Windows Features](screenshot/windows_features.png)

### Startup Manager

![Startup Manager](screenshot/startup_app.png)

### WinGet Package Manager

![WinGet Package Manager](screenshot/winget_package.png)

### Settings

![Settings](screenshot/settings.png)

---

## Feature Overview Matrix

| Module | Core Functionality | Supported Actions | Target Scope |
| :----------------------------- | :--------------------------------------------------------------- | :------------------------------------------------------------------------- | :------------------------------------------------------------------ |
| **Desktop Applications** | Comprehensive inventory of Win32 and 64-bit software | Standard Uninstall, Quiet/Silent Uninstall, Force Uninstall, Leftover Scan | Installed programs across machine and user scopes |
| **Store Apps** | Management of Microsoft Store, UWP, and MSIX applications | Standard Remove, Forced Removal, Package Inspection | User packages and system-wide provisioned packages |
| **Windows Features** | Toggle optional Windows system components | Enable, Disable, Status Inspection | Windows optional features (Hyper-V, WSL, Sandbox, etc.) |
| **WinGet Packages** | Management of software installed through Windows Package Manager | Search, Update, Remove | Packages tracked via WinGet CLI |
| **Startup Manager** | Control applications launching at boot | Enable, Disable, Delete, Path Inspection | Registry Run/RunOnce keys, Startup folders, Scheduled Tasks |
| **Deep Clean** | Post-uninstall residue detection and cleanup | Scan, Selective Clean, Send to Recycle Bin, Permanent Delete | File system (`AppData`, `ProgramData`, `Temp`), Registry keys |
| **Process Hunter** | Active process monitoring for target software | Detection, Auto-Kill on demand, Process tree termination | Running executables and child processes |
| **Restore Point Engine** | Automated safety checkpoints before system modifications | Create System Restore Point | Windows Volume Shadow Copy / System Restore API |

---

## Comparison with Windows Default Uninstaller

| Feature                                           | Windows Default ("Apps & Features") |  Zidimi Uninstaller  |
| :------------------------------------------------ | :---------------------------------: | :------------------: |
| Win32 / 64-bit Desktop Application Uninstallation |              Supported              |      Supported      |
| Microsoft Store (UWP / MSIX) App Management       |              Supported              |      Supported      |
| Silent / Unattended Uninstallation Mode           |            Not Available            |      Supported      |
| Force Uninstall for Broken Uninstallers           |            Not Available            |      Supported      |
| Post-Uninstall Leftover & Registry Scanning       |            Not Available            |      Supported      |
| Windows Optional Features Management              |       Separate Control Panel       |      Integrated      |
| WinGet Package Manager Integration                |              CLI Only              |    Integrated GUI    |
| Startup Program Management                        |          Task Manager Only          |      Integrated      |
| Automatic Process Termination before Uninstall    |            Not Available            |      Supported      |
| Automatic System Restore Point Creation           |            Not Available            |      Supported      |
| Multi-language Support with Hot-Switching         |      System Language Dependent      | 7 Built-in Languages |
| Portable Execution (No Installation Required)     |           Not Applicable           |      Supported      |

---

## Detailed Module Capabilities

### 1. Desktop Applications Management

- **Full Inventory**: Queries 32-bit and 64-bit Registry uninstall keys (`HKLM` and `HKCU`) to identify all registered programs with installation date, size, publisher, and version.
- **Silent Uninstall**: Automatically identifies and applies silent/unattended flags (`/quiet`, `/qn`, `/s`, `/silent`, `/VERYSILENT`) for supported installers (MSI, Inno Setup, Nullsoft NSIS, InstallShield, WiX).
- **Force Removal**: Cleans up program files, registry keys, and uninstall registration when an installer is corrupt or missing.
- **Batch Processing**: Filter, sort, and select multiple applications for sequential processing.

### 2. Windows Store Apps (UWP / MSIX / AppX)

- **AppX Package Enumeration**: Leverages Windows Package Manager APIs to discover installed modern apps across all user profiles.
- **Bloatware Cleanup**: Easily remove pre-installed Windows applications and background service packages.
- **Package Manifest Details**: View package family names, publishers, architecture, and installation paths.

### 3. Windows Optional Features

- **Integrated State Management**: Reads optional Windows components and displays current state (Enabled / Disabled / Pending).
- **One-Click Toggles**: Dispatch DISM operations in the background without needing to open the legacy Control Panel.

### 4. Package Manager (WinGet)

- **CLI Bridge**: Integrates with the `winget` command-line tool.
- **Package Control**: Inspect package identifiers, source repositories, installed versions, and available upgrades.

### 5. Startup Manager

- **Multi-Location Inspection**: Aggregates startup entries from `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, `HKLM\Software\Microsoft\Windows\CurrentVersion\Run`, 32-bit Wow6432Node equivalents, and user Startup folders.
- **Performance Optimization**: Disable high-impact startup items to reduce boot times without deleting them permanently.

### 6. Deep Clean and Residue Removal

- **Targeted Scanning**: Searches common residual paths:
  - `%ProgramFiles%` and `%ProgramFiles(x86)%`
  - `%AppData%` (Roaming) and `%LocalAppData%`
  - `%ProgramData%`
  - Registry paths: `HKCU\Software`, `HKLM\Software`, `HKLM\Software\WOW6432Node`
- **Safe Cleanup**: Choose between safe recycling (Windows Recycle Bin) or permanent file deletion.

### 7. Safety and Process Hunter

- **Process Hunter**: Queries running processes matching application executable names and file paths, offering automated process termination to prevent file lock errors during uninstallation.
- **Restore Point Engine**: Calls Windows System Restore APIs to snapshot the system state prior to modifications.

---

## Supported Languages

Zidimi Uninstaller includes localization for 7 languages:

| Language Code | Language Name | Native Name | Status |
| :------------ | :---------------------- | :------------- | :------: |
| `en-US` | English (United States) | English | Complete |
| `vi-VN` | Vietnamese | Tiếng Việt | Complete |
| `de-DE` | German | Deutsch | Complete |
| `fr-FR` | French | Français | Complete |
| `it-IT` | Italian | Italiano | Complete |
| `ru-RU` | Russian | Русский | Complete |
| `zh-CN` | Chinese (Simplified) | 简体中文 | Complete |

Language selection can be changed on the fly in the Settings view without restarting the application.

---

## Configuration and Settings

| Setting Key | Default | Description |
| :------------------------- | :-------- | :-------------------------------------------------------------------------------- |
| `HideSystemComponents` | `true` | Hides Windows core components and update patches from the main application list. |
| `PreferQuietUninstall` | `false` | Attempts to run quiet/unattended uninstall commands by default when available. |
| `ConfirmBeforeUninstall` | `true` | Prompts for user confirmation before initiating an uninstall routine. |
| `EnableDeepClean` | `true` | Automatically triggers leftover scanning after an uninstallation finishes. |
| `CreateRestorePoint` | `false` | Creates a Windows System Restore point prior to executing uninstalls. |
| `AutoKillProcesses` | `true` | Automatically detects and stops associated running processes before uninstalling. |
| `SendToRecycleBin` | `true` | Sends leftover files to the Recycle Bin instead of deleting them permanently. |

Settings are persisted in `%LocalAppData%\ZidimiUninstaller\settings.json`.

---

## System Requirements

- **Operating System**: Windows 10 (Version 1809 or higher) or Windows 11 (64-bit).
- **Runtime**: [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or standalone release package).
- **Privileges**: Administrator permissions are recommended for full uninstallation and Registry cleanup capabilities.

---

## Download and Installation

1. Go to the [Releases](https://github.com/khanh779-9/zidimi-uninstaller/releases) page.
2. Download the latest release package (`zidimi-uninstaller-v1.1.4.zip`).
3. Extract the zip file into any folder.
4. Run `zidimi-uninstaller.exe`.

---

## Building from Source

### Prerequisites

- [Visual Studio 2022](https://visualstudio.microsoft.com/) (version 17.8+) with the **.NET Desktop Development** workload, or the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

### Build Steps

```bash
# Clone the repository
git clone https://github.com/khanh779-9/zidimi-uninstaller.git
cd zidimi-uninstaller

# Restore dependencies
dotnet restore

# Build solution in Release mode
dotnet build -c Release

# Publish release package
dotnet publish zidimi-uninstaller/zidimi-uninstaller.csproj -c Release -o ./publish
```

---

## License and Trademark Notice

This project is licensed under the **MIT License with Trademark Protection**.

```
Copyright (c) 2026 khanh779-9 (Zidimi Uninstaller)
```

### Trademark Notice

- The names **"Zidimi"**, **"Zidimi Uninstaller"**, the product logo, and related brand identifiers are proprietary trademarks and intellectual property of the author (**khanh779-9**).
- This license does not grant rights or permissions to use the "Zidimi" name, trademark, or logo for third-party commercial, marketing, or endorsement purposes without prior written authorization.
- Any modified versions, forks, or derivative works distributed publicly must rebrand and use distinct names and logos that do not cause confusion regarding the origin of the software.

For full terms, refer to the [LICENSE](LICENSE) file.

---

## Contributing and Support

- **Bug Reports & Feature Requests**: Submit an issue via [GitHub Issues](https://github.com/khanh779-9/zidimi-uninstaller/issues).
- **Pull Requests**: Contributions and enhancements are welcome via [GitHub Pull Requests](https://github.com/khanh779-9/zidimi-uninstaller/pulls).
