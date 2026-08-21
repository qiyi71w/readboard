<div align="center">

# ReadBoard

A Windows Go board synchronization tool for [LizzieYzy-Next](https://github.com/wimi321/lizzieyzy-next), providing board capture, stone recognition, board-state reporting, and simulated move placement.

<p>
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" />
  <img alt="Platform" src="https://img.shields.io/badge/Platform-Windows-0078D4?logo=windows&logoColor=white" />
  <img alt="UI" src="https://img.shields.io/badge/UI-WebView2-0C7CD5" />
  <img alt="Release" src="https://img.shields.io/github/v/release/qiyi71w/readboard?label=Release" />
  <img alt="Downloads" src="https://img.shields.io/github/downloads/qiyi71w/readboard/total?label=Downloads" />
</p>

<a href="readme.md">简体中文</a> ｜
<a href="readme_en.md">English</a>

![screenshot](assets/screenshot-v3.1.png)

</div>

<details>
<summary>v3.0 WinForms screenshot</summary>

![v3.0 demo](assets/demo.png)

</details>

> [!IMPORTANT]
> **This project may no longer be compatible with the original [LizzieYzy](https://github.com/yzyray/lizzieyzy).**
> Active development, protocol changes, and packaging all target [LizzieYzy-Next](https://github.com/wimi321/lizzieyzy-next); compatibility with the original LizzieYzy is no longer guaranteed. Original-LizzieYzy users should keep using its bundled readboard or migrate to LizzieYzy-Next.

## Overview

ReadBoard captures board images from external Go clients, recognizes stones with OpenCV color thresholds, and streams board state to the host (LizzieYzy-Next) over TCP or standard input/output. It also receives placement commands from the host and executes them through simulated clicks.

## Features

- External board-window capture, including Fox / YeHu and Yike window binding and title parsing
- OpenCV stone recognition with real-time board-state reporting to the host
- Move placement through simulated input; Fox supports background placement (the window must not be minimized)
- Fox identity recognition: determines the local player's nickname and stone color from the player list and match bar
- Hosted updates: check → download → verify, then hand off installation and replacement to the host
- Light / Dark / Follow-System color modes
- Localized in Simplified Chinese, English, Japanese, and Korean

## Maintained Branches

This repository maintains two release lines. Update checks automatically select a channel for the current Windows version:

| Branch | UI framework | Version line | System requirement |
| --- | --- | --- | --- |
| `main` | WebView2 | v3.1.x | Windows 10 version 1809 (build 17763)+ |
| `legacy/winforms` | WinForms | v3.0.x | Earlier Windows releases |

> [!NOTE]
> The channels are independent. The WebView2 mainline is not merged wholesale into `legacy/winforms`; older systems continue to receive selected fixes through the legacy channel.

## Requirements

- Windows 10 version 1809 (build 17763) or later, or Windows 11
- [WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (shared system runtime; startup offers an installation link when it is missing)
- Official releases are self-contained and do not require a separate .NET Runtime; development requires the .NET 10 SDK

## Usage

In normal use, LizzieYzy-Next launches `readboard.exe`, which communicates with the host over TCP or standard input/output. Launching without arguments does not show a window.

To open the UI for debugging, simulate the host launch:

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run-readboard-ui-debug.ps1
```

Or point at an executable from a release package:

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
> Always use `scripts/package-readboard-release.local.ps1`; do not hand-roll build, copy, or compression commands. Use `-SkipZip` by default to produce only a directory, and create a ZIP only when distributing a release.

```powershell
# Release directory only
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-readboard-release.local.ps1 -SkipZip

# Distribution ZIP
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-readboard-release.local.ps1
```

## Relationship with LizzieYzy-Next

ReadBoard is an external companion to LizzieYzy-Next. The host-side launcher lives at:

```text
lizzieyzy-next/src/main/java/featurecat/lizzie/analysis/ReadBoard.java
```

Any change to launch arguments, protocol text, release layout, or packaged contents must be cross-checked against LizzieYzy-Next.
