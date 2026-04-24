# readboard 开发说明

面向 readboard 维护者：本地开发、代码结构、验证流程、宿主集成边界。

## 项目定位

readboard 是 LizzieYzy / LizzieYzy-Next 调用的 Windows 棋盘同步工具，负责截图、识别棋盘，并通过旧文本协议把棋盘状态和同步命令发回宿主。

当前维护版本是本仓库的 .NET 10 WinForms 版本；简易版 readboard 已停止维护。

## 开发环境

- Windows
- .NET 10 SDK
- PowerShell 7（命令用 `pwsh.exe`）
- Visual Studio 或 Rider 可选；命令行构建不依赖 IDE

从 WSL 或 Codex 调用 Windows 工具时，Windows 程序传 Windows 路径。把 `/mnt/...` 转成 Windows 路径，或在 `pwsh.exe -NoProfile` 里从 Windows 路径进入仓库。

## 常用命令

以下命令都在仓库根目录执行。

```powershell
dotnet restore readboard.sln --configfile NuGet.Config
dotnet build readboard.sln -c Debug
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --no-build
```

只跑一组测试：

```powershell
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~Protocol"
```

跑性能和验收基准：

```powershell
dotnet run --project benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj
```

生成发布目录（不打 zip）：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-readboard-release.local.ps1 -SkipZip
```

生成 GitHub release 用的 zip：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-readboard-release.local.ps1
```

## 调 UI

`readboard.exe` 正常由 LizzieYzy-Next 带参数启动，无参数启动不会显示窗口。调 UI 时用脚本模拟宿主启动：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run-readboard-ui-debug.ps1
```

指定构建配置和语言：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run-readboard-ui-debug.ps1 -Configuration Release -Language en
```

指定某个 release 包里的 `readboard.exe`：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run-readboard-ui-debug.ps1 -ExePath "D:\path\to\readboard.exe"
```

也可以从 `cmd.exe` 调包装脚本：

```bat
scripts\run-readboard-ui-debug.cmd Debug cn "D:\path\to\readboard.exe"
```

脚本使用 pipe 模式参数：

```text
readboard.exe yzy " " " " " " 0 cn -1
```

走的是接近宿主的启动路径。完整的协议闭环调试需要从宿主项目启动。

## 启动参数

入口在 `readboard/Program.cs`，参数解析在 `readboard/Core/Models/LaunchOptions.cs`。

宿主启动参数格式：

```text
readboard.exe yzy <aiTime> <playouts> <firstPolicy> <transport> <language> <tcpPort>
```

参数含义：

| 位置 | 示例 | 含义 |
| --- | --- | --- |
| 0 | `yzy` | 固定启动标记 |
| 1 | `30` 或 `" "` | 自动落子每手用时 |
| 2 | `1000` 或 `" "` | 最大计算量 |
| 3 | `200` 或 `" "` | 首选计算量 |
| 4 | `0` / `1` | `0` 为标准输入输出 pipe，`1` 为 TCP |
| 5 | `cn` | 语言文件后缀 |
| 6 | `-1` 或端口 | pipe 模式传 `-1`，TCP 模式传端口 |

LizzieYzy-Next 的 native readboard 启动逻辑在宿主仓库 `src/main/java/featurecat/lizzie/analysis/ReadBoard.java`。

## 代码地图

主要目录：

- `readboard/`：WinForms 应用和核心代码
- `readboard/Core/`：同步、协议、截图、识别、落子、配置等可测试逻辑
- `tests/Readboard.VerificationTests/`：xUnit 验证测试
- `benchmarks/Readboard.ProtocolConfigBenchmarks/`：协议、配置、识别和持续同步验收基准
- `fixtures/`：协议、配置、识别回放测试数据
- `scripts/`：本地调试和发布打包脚本
- `.github/workflows/package-release.yml`：GitHub release 打包流程

关键文件：

- `Program.cs`：程序入口、运行时初始化、语言加载、传输选择
- `Form1.cs` / `MainForm.*.cs`：主窗体、UI 事件、协议宿主实现
- `MainFormRuntimeComposer.cs`：把主窗体、协调器和运行时依赖装配到一起
- `SyncSessionCoordinator*.cs`：同步状态机、协议收发、持续同步编排
- `LegacyProtocolAdapter.cs` / `ProtocolKeywords.cs`：旧文本协议解析和生成
- `PipeTransport.cs` / `TcpTransport.cs`：宿主通信
- `DualFormatAppConfigStore.cs`：JSON 配置和旧配置文件的双格式读写
- `IBoardCaptureService.cs`：截图和窗口坐标处理
- `IBoardRecognitionService.cs`：棋盘识别
- `IMovePlacementService.cs`：自动落子
- `IOverlayService.cs`：原棋盘选点显示

## 协议边界

readboard 与 LizzieYzy-Next 之间是逐行文本协议。`ProtocolKeywords` 是仓库内部常量，wire 文本本身才是兼容边界。

改协议时同步处理：

1. 保持旧 wire 文本逐字兼容，或同步改宿主解析。
2. 更新 `ProtocolKeywords` 和 `LegacyProtocolAdapter`。
3. 更新协议 fixture 或协议契约测试。
4. 到 LizzieYzy-Next 核对 `ReadBoard.java` 的解析逻辑。
5. 跑协议相关测试。

```powershell
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~Protocol"
```

## 配置文件

运行目录下使用三类配置文件：

- `config.readboard.json`：当前主配置
- `config_readboard.txt`：旧主配置镜像
- `config_readboard_others.txt`：旧扩展配置镜像

`DualFormatAppConfigStore` 优先读 JSON；没有 JSON 时尝试导入旧格式。保存时同时写 JSON 和旧格式镜像，保证老集成路径仍能读到配置。

改配置字段时：

- 给 `AppConfig.CreateDefault` 加默认值。
- 更新 JSON 读写和 legacy 镜像读写。
- 保持 machine key 和 protocol version 校验语义。
- 补配置读写测试。

## UI 和 DPI

UI 是 WinForms，主窗口逻辑主要在 `Form1.cs`。项目启用了 `HighDpiMode.PerMonitorV2`，UI 改动需覆盖 100% 以外的缩放和多屏场景。

改窗口、控件、字体、弹窗位置或截图坐标时，检查：

- 不同 DPI 缩放
- 不同分辨率
- 多屏或跨屏
- 窗口坐标保存和恢复
- 截图坐标与屏幕坐标转换

相关测试集中在 `tests/Readboard.VerificationTests/Host`、`Display`、`Capture`。

## 打包和版本

发布版本来自 `readboard/Properties/AssemblyInfo.cs`：

```csharp
[assembly: AssemblyInformationalVersion("v3.0.0")]
```

打包脚本用这个版本生成目录名：

```text
release/readboard-github-release-v3.0.0
```

默认发布脚本构建 Release 并复制：

- `readboard.exe`
- `readboard.dll`
- `readboard.runtimeconfig.json`
- `readboard.deps.json`
- OpenCvSharp 相关 dll/pdb
- `language_*.txt`
- `readme*.rtf`
- `OpenCvSharpExtern.dll`

发布包不包含旧的 `lw.dll`、`Interop.lw.dll`、`MouseKeyboardActivityMonitor.dll` 或 `readboard.exe.config`。

GitHub Actions 在 tag `v*` 上会校验 tag 与 `AssemblyInformationalVersion` 一致，然后跑测试、benchmark acceptance、打包并发布 zip。

## 改动前后的检查

小改动可以只跑相关测试；协议、配置、截图、识别、打包、UI 启动路径的改动跑完整验证。

常用顺序：

```powershell
dotnet build readboard.sln -c Debug
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --no-build
dotnet run --project benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj
```

打包改动再跑：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-readboard-release.local.ps1 -SkipZip
```

打包后检查：

- `readboard.exe` 存在
- release 目录存在
- 未要求 zip 时没有新 zip
- `PackageVersion` 与源码版本一致
- 产物修改时间是本次打包产生的

## 与 LizzieYzy-Next 对接

本仓库是 LizzieYzy-Next 的外接程序。涉及接口、参数、目录结构、发布产物或启动方式时，同步检查宿主。

宿主侧重点文件（相对宿主仓库根目录）：

```text
src/main/java/featurecat/lizzie/analysis/ReadBoard.java
```

本地常用的 LizzieYzy-Next 工作副本示例路径：`D:\dev\weiqi\lizzieyzy-next`。实际路径以自己的检出位置为准。

宿主默认在工作目录下找 `readboard/readboard.exe` 或 `readboard/readboard.bat`。release 结构、文件名或启动参数变化时，宿主侧同步调整。
