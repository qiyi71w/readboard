# readboard

readboard 是 [LizzieYzy](https://github.com/yzyray/lizzieyzy) 和 [LizzieYzy-Next](https://github.com/wimi321/lizzieyzy-next) 的 Windows 棋盘同步工具：抓取外部棋盘、识别棋子、把棋盘状态发回宿主。当前版本支持野狐后台落子（不能最小化窗口，仅 Windows）。

![演示图](assets/demo.png)

## 当前维护版本

仓库里的 .NET 10 WinForms 版本是唯一在维护的版本。简易版 readboard 已停止维护，生产改动、打包、宿主集成都以本仓库为准。

## 使用方式

正常使用时 `readboard.exe` 由 LizzieYzy / LizzieYzy-Next 启动，通过标准输入输出或 TCP 与宿主通信。无参数启动不会显示窗口。

需要单独打开 UI 调试时，用脚本模拟宿主启动：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run-readboard-ui-debug.ps1
```

指定某个发布包里的 exe：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run-readboard-ui-debug.ps1 -ExePath "D:\path\to\readboard.exe"
```

## 开发

需要 Windows 和 .NET 10 SDK。

```powershell
dotnet restore readboard.sln --configfile NuGet.Config
dotnet build readboard.sln -c Debug
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --no-build
```

更多见 [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)。

## 打包

生成 release 目录（不打 zip）：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-readboard-release.local.ps1 -SkipZip
```

生成 GitHub release 用的 zip：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-readboard-release.local.ps1
```

版本号取自 `readboard/Properties/AssemblyInfo.cs` 的 `AssemblyInformationalVersion`。

## 与 LizzieYzy-Next 的关系

本项目是 `lizzieyzy-next` 的外接同步工具，宿主侧启动逻辑在：

```text
lizzieyzy-next/src/main/java/featurecat/lizzie/analysis/ReadBoard.java
```

涉及启动参数、协议文本、发布目录结构或打包内容的改动，需同步检查 LizzieYzy-Next。

## English

readboard is a Windows board synchronization helper for LizzieYzy and LizzieYzy-Next. It captures an external board, recognizes stones, and sends the board state back to the host application.

For development notes, see [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).
