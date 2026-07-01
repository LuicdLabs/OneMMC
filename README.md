<div style="display: flex; align-items: center; justify-content: left;">
  <picture>
    <img height="120" src="./doc/images/AppLogo.png" />
  </picture>
  <h1 style="margin-left: 16px;">
    <span>OneMMC</span>
  </h1>
</div>

A modern Windows system management suite built with WinUI 3, designed as a streamlined and contemporary alternative to the legacy Microsoft Management Console (MMC)

> [!WARNING]
> This App is currently in an early dogfooding stage and remains under active development.
>
> Parts of the codebase are incomplete and may directly modify critical system components, including disks, services, users, and group policies.
>
> Use only in isolated test environments or virtual machines.

> [!IMPORTANT]
> **AI-Assisted Development Disclaimer**
>
> This application is developed through a collaborative process combining AI-generated code and manual debugging by the author. As a result, the codebase may contain:
>
> - Unforeseen bugs, logic errors, or security vulnerabilities inherent to AI-generated code
> - Code patterns that are difficult to maintain, or structural and architectural design issues that require ongoing remediation — resolving these is a primary development priority
>
> **Before deploying or using this software in any real environment, you must evaluate its stability and robustness in an isolated virtual machine or test environment.** The author assumes no responsibility for any system instability, data loss, or damage resulting from the use of this software.

<picture>
  <img src="./doc/images/AppScreenShot.png" />
</picture>

## ✨ Features

- Built with WinUI 3, featuring native Dark Mode support, high-DPI awareness, smooth motions, and modern Fluent Design UI/UX behaviors
- Designed following the [Windows 11 design principles](https://learn.microsoft.com/en-us/windows/apps/design/design-principles), with improved visual hierarchy, simplified workflows, and optimized touch/tablet experience
- Consolidates commonly used administrative tools (Services, Device Manager, Event Viewer, Disk Management, Local Users and Groups, and more) into a unified experience
- Built directly on Win32 APIs, COM, WMI, and CIM for low-level and high-performance system management integration
- Avoids unnecessary abstraction layers to preserve compatibility with existing Windows management infrastructure

---

## 🛠️ Development Guide

### Prerequisites

- Windows 10 May 2020 Update (version 2004, 10.0.19041.0) or newer
- Visual Studio 2026 (recommended) or Visual Studio 2022 17.8+ with the following workloads:
    - Desktop development with C++
    - WinUI application development
    - .NET desktop development
- [.NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Windows App SDK Runtime](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)

### Debugging

#### Clone the Repository
```bash
git clone https://github.com/LuicdLabs/OneMMC
```

#### Open in Visual Studio
- Navigate to **File → Open → Project/Solution**
- Select the `OneMMC.slnx` solution file

#### Set Startup Project
- Right‑click the solution in **Solution Explorer** → **Configure Startup Projects...**
- Under **Configure Startup Projects**, select **Single startup project:**
- From the dropdown, choose **OneMMC**
- Click **OK**

#### Switch to Unpackaged
- In Toolbar, find the dropdown that says **OneMMC (Package)** and change it to **OneMMC (Unpackaged)**
- Navigate to **Debug → Start Debugging** to build and run the application