<div align="center">

# readboard

A Windows board synchronization helper for [LizzieYzy-Next](https://github.com/wimi321/lizzieyzy-next): captures Go boards from third-party clients, recognizes stones, streams board state to the host, and simulates placements.

<a href="readme.md">简体中文</a> ｜
<a href="readme_en.md">English</a>

![demo](assets/demo.png)

</div>

> [!IMPORTANT]
> **This project may no longer be compatible with the original [LizzieYzy](https://github.com/yzyray/lizzieyzy).**
> Active development, protocol changes, and packaging all target [LizzieYzy-Next](https://github.com/wimi321/lizzieyzy-next); compatibility with the original LizzieYzy is no longer guaranteed. Original-LizzieYzy users should keep using its bundled readboard or migrate to LizzieYzy-Next.

## Overview

readboard captures Go board screenshots from external Go clients, recognizes stones via color thresholding, and streams the board state to the host (LizzieYzy-Next) over TCP or named pipes. It also receives placement commands from the host and executes them through simulated clicks.

The .NET 10 WinForms version in this repository is the only actively maintained version. The legacy "simple readboard" is no longer maintained — all production changes, packaging, and host integration are done here.

## Features

- External board window capture, including Fox / YeHu window binding and title parsing
- Stone recognition with real-time board state sync to the host
- Move placement via simulated input; Fox supports background placement (window must not be minimized)
- Classic / Optimized themes with Light / Dark / Follow-System color modes
- Localized in Simplified Chinese, English, Japanese, and Korean

## Requirements

- Windows 10 / 11
- .NET 10 runtime; .NET 10 SDK for development

## Usage

In normal use, `readboard.exe` is launched by LizzieYzy-Next and talks to the host over TCP or named pipes. Launching without arguments does not show a window.

To open the UI for debugging, simulate the host launch:

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run-readboard-ui-debug.ps1
```

Or point at a packaged exe:

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run-readboard-ui-debug.ps1 -ExePath "D:\path\to\readboard.exe"
```

## Development

```powershell
dotnet restore readboard.sln --configfile NuGet.Config
dotnet build readboard.sln -c Debug
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --no-build
```

See [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for more.

## Packaging

> [!TIP]
> Always use `scripts/package-readboard-release.local.ps1` — do not hand-roll build / copy / compress commands. Default to `-SkipZip` (folder only); produce a zip only when distributing.

```powershell
# Folder only, no zip
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-readboard-release.local.ps1 -SkipZip

# Release zip
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-readboard-release.local.ps1
```

The version is read from `AssemblyInformationalVersion` in `readboard/Properties/AssemblyInfo.cs` (currently `v3.0.2`).

## Relationship with LizzieYzy-Next

This project is an external companion to LizzieYzy-Next. The host-side launcher lives at:

```text
lizzieyzy-next/src/main/java/featurecat/lizzie/analysis/ReadBoard.java
```

Any change to launch arguments, protocol text, release layout, or packaged contents must be cross-checked against LizzieYzy-Next.
