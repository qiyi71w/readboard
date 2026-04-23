# Readboard .NET 10 Upgrade Design

## Overview

将 readboard 从 .NET Framework 4.8 + Legacy MSBuild 全面升级到 .NET 10 + SDK-style 项目格式，同时更新所有第三方依赖、清理死代码、简化测试架构，并利用 .NET 10 WinForms 新特性实现 UI 现代化。

## Motivation

- .NET Framework 4.8 已停止新功能开发，仅安全维护
- OpenCvSharp3 (2018) 已停止维护，存在安全和兼容性风险
- lw.dll COM 互操作为已禁用的死代码
- 测试项目通过源码链接桥接框架差异，维护成本高
- .NET 10 WinForms 提供原生暗色模式等现代特性

## Target

- **Runtime**: .NET 10.0 LTS (支持至 2028-11)
- **TFM**: `net10.0-windows`
- **UI**: WinForms (继续使用，利用 .NET 10 新特性)
- **Platform**: AnyCPU（升级 OpenCvSharp4 后解除 x86 限制）

## Non-Goals

- 不迁移到 WPF/MAUI/Blazor
- 不重构核心同步/识别架构
- 不改变与 LizzieYzy-Next 的通信协议

---

## Phase 1: 框架升级 + 依赖更新 + 死代码清理

### 目标

一次性完成框架切换和所有不兼容依赖的替换。这些变更无法分步进行——切换到 `net10.0-windows` 后，`System.Web.Extensions`、`OpenCvSharp3`、`Interop.lw.dll`、`MouseKeyboardActivityMonitor.dll` 均无法编译，必须同步替换。

### 1.1 readboard.csproj 转换为 SDK-style

**当前**: Legacy MSBuild 格式 (`ToolsVersion="12.0"`)，`packages.config`
**目标**: SDK-style，`PackageReference`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <OutputType>WinExe</OutputType>
    <UseWindowsForms>true</UseWindowsForms>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ApplicationManifest>Properties\app.manifest</ApplicationManifest>
  </PropertyGroup>
</Project>
```

关键决策：
- **不再指定 `PlatformTarget`**（默认 AnyCPU，OpenCvSharp4 提供 x86+x64 native）
- `GenerateAssemblyInfo` 设为 `false`（CI 通过 `AssemblyInfo.cs` 中的 `AssemblyInformationalVersion` 校验版本号与 git tag）
- `AllowUnsafeBlocks` 保留（当前 Release 配置有此设置）
- `ApplicationManifest` 显式指定（SDK-style 不自动包含 manifest）
- 添加 `System.Drawing.Common` NuGet 包引用（.NET 10 需要显式引用）

**移除的 Legacy 配置：**
- `System.Deployment` 引用（.NET 10 不存在）
- ClickOnce 相关属性（`PublishUrl`、`Install`、`UpdateEnabled`、`UpdateMode`、`BootstrapperPackage`、`WCFMetadata`）
- `Prefer32Bit`

**Resources/Settings 处理：**
- `Properties/Resources.resx` + `Resources.Designer.cs` — SDK-style 项目自动处理 `.resx`，需确认 `<Generator>` 和 `<AutoGen>` 属性迁移
- `Properties/Settings.settings` + `Settings.Designer.cs` — 需确认 SDK-style 下的处理方式

### 1.2 NuGet 迁移

- 删除 `readboard/packages.config`
- 删除 `packages/OpenCvSharp3-AnyCPU.4.0.0.20181129/` 目录
- 所有依赖转为 `PackageReference`

### 1.3 App.config / DPI 配置

当前 `App.config` 中的 DPI 配置迁移为 `Program.cs` 中的代码调用：

```csharp
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
```

**注意：** `AppDomain.CurrentDomain.BaseDirectory` 在 .NET 10 中行为可能不同（单文件发布等场景）。需要验证配置文件和语言文件的运行时路径解析。

### 1.4 OpenCvSharp3 → OpenCvSharp4

**移除：**
- `packages/OpenCvSharp3-AnyCPU.4.0.0.20181129/` 整个目录
- csproj 中的 `RemoveUnusedOpenCvSharpX64Runtime` 自定义 MSBuild target
- 对 `OpenCvSharp.Blob.dll`、`OpenCvSharp.UserInterface.dll` 的引用（OpenCvSharp4 已移除这些子程序集）

**添加：**
- `OpenCvSharp4.Windows` NuGet 包（自带 x86 + x64 native runtime）

**代码适配：**
- 命名空间保持 `OpenCvSharp`（向后兼容）
- 核心 API (`Mat`, `Cv2.InRange`, `Cv2.CvtColor`, `Cv2.FindContours`) 基本不变
- 检查 `MatType` 常量名等 breaking changes
- 确认无 `using OpenCvSharp.Blob` 或 `using OpenCvSharp.UserInterface`（经验证：项目中无此引用）

### 1.5 JSON 序列化迁移

**移除：**
- `System.Web.Extensions` 程序集引用
- `tests/Shared/JavaScriptSerializerShim.cs`

**替换：**
`System.Web.Script.Serialization.JavaScriptSerializer` → `System.Text.Json.JsonSerializer`

**影响文件：**
- `Core/Configuration/DualFormatAppConfigStore.cs` — 配置文件的 JSON 读写
- `GitHubUpdateChecker.cs` — GitHub API JSON 响应解析

**注意事项：**
- `System.Text.Json` 默认属性名大小写敏感，需使用 `JsonSerializerOptions { PropertyNameCaseInsensitive = true }` 保持兼容
- 需要确保配置文件的序列化/反序列化输出兼容现有格式（向后兼容已有的 JSON 配置文件）

### 1.6 lw.dll 死代码清理

**确认：** `canUseLW` 在 `Form1.cs:48` 初始化为 `false`，第 1877 行 `canUseLW = false;// true;`。lw.dll 功能完全未启用。

**移除清单：**

| 文件 | 移除内容 |
|------|---------|
| `readboard/Interop.lw.dll` | 整个文件 |
| `readboard/lw.dll` | 整个文件 |
| `readboard/generate_lw_interop.ps1` | 整个文件 |
| `tests/Shared/LightweightInteropShim.cs` | 整个文件 |
| `Core/Placement/IMovePlacementService.cs` | `PlacementLightweightInteropClient` 类、`PlaceLightweight` 方法、`IPlacementLightweightInteropClient` 接口、`lightweightInteropFactory` 字段 |
| `Core/Placement/MovePlacementRequest.cs` | `UseLightweightInterop` 属性 |
| `Core/Placement/MovePlacementResult.cs` | `PlacementPathKind.LightweightInterop` 枚举值 |
| `Core/Protocol/SyncCoordinatorHostSnapshot.cs` | `CanUseLightweightInterop` 属性 |
| `Form1.cs` | `canUseLW` 字段、`lw` 字段、lw 初始化代码、`ReleasePlacementBinding` 中 lw 调用 |

**保留：**
- `ISyncCoordinatorHost.ReleasePlacementBinding` 接口方法 — 保留但移除内部 lw 调用
- `PlacementPathKind` 枚举 — 移除 `LightweightInterop` 值
- 相关测试用例需更新

### 1.7 MouseKeyboardActivityMonitor 替代

**当前：** `MouseKeyboardActivityMonitor.dll` 二进制引用（v4.0），.NET Framework 程序集，.NET 10 上无法加载。

**方案：** 内联 P/Invoke 实现（`SetWindowsHookEx` + `UnhookWindowsHookEx`）

理由：
- 项目已大量使用 P/Invoke（50+ 处）
- 仅需要键盘钩子功能（不需要鼠标监控）
- 减少外部二进制依赖

**实现：**
- 在 `Core/` 下创建 `KeyboardHook.cs`
- 使用 `SetWindowsHookEx(WH_KEYBOARD_LL, ...)` 实现全局键盘钩子
- 移除 `MouseKeyboardActivityMonitor.dll` 二进制

### 1.8 P/Invoke AnyCPU 审计

从 x86 切换到 AnyCPU 后，需要审计所有 P/Invoke 声明的指针大小安全性：
- `int` 参数是否应该是 `IntPtr`（如窗口句柄 HWND）
- `SetLastError` 和 `CharSet` 设置是否正确
- 回调委托签名的指针大小一致性

涉及文件：
- `Form1.cs`（11 处 DllImport）
- `Core/Capture/IBoardCaptureService.cs`（14 处）
- `Core/Display/DisplayScaling.cs`（3 处）
- `Core/Placement/IMovePlacementService.cs`（7 处）
- `Core/Transport/PipeTransport.cs`（6 处）
- `Core/Protocol/FoxWindowTitleReader.cs`（2 处）

### 1.9 测试和基准项目框架升级

- `VerificationTests.csproj`: `net8.0` → `net10.0-windows`
- `Benchmarks.csproj`: `net8.0` → `net10.0-windows`
- 两个项目中的 shim 文件暂时保留（Phase 2 统一处理）

### 1.10 验证标准

- [ ] `dotnet build readboard.sln` 编译通过
- [ ] `dotnet test` 全部测试通过
- [ ] 基准测试可运行
- [ ] 应用启动正常，核心同步功能工作
- [ ] OpenCvSharp4 棋子识别正确（fixture replay 测试通过）
- [ ] JSON 配置文件读写兼容现有格式
- [ ] 键盘钩子功能正常工作

---

## Phase 2: 测试项目简化 + CI/打包更新

### 目标

统一框架后，从源码链接改为项目引用，消除框架桥接 shim。同步更新 CI 和打包流程。

### 2.1 测试引用方式

**当前：** 60+ 条 `<Compile Include="..\..\readboard\..." Link="Production\..." />`

**目标：** `<ProjectReference Include="..\..\readboard\readboard.csproj" />`

### 2.2 基准项目引用方式

基准项目 (`Benchmarks.csproj`) 同样使用源码链接 + 全部三个 shim，需要同样的处理。

**目标：** `<ProjectReference Include="..\..\readboard\readboard.csproj" />`

### 2.3 移除 shim 文件

| Shim | 原因 |
|------|------|
| `JavaScriptSerializerShim.cs` | Phase 1 已替换为 System.Text.Json |
| `LightweightInteropShim.cs` | Phase 1 已移除 lw.dll |
| `WindowsFormsScreenShim.cs` | 统一 .NET 10 后可直接引用 WinForms |

### 2.4 可见性调整

添加 `[assembly: InternalsVisibleTo("Readboard.VerificationTests")]` 到主项目。
必要时同时添加 `InternalsVisibleTo("Readboard.ProtocolConfigBenchmarks")`。

优先使用 `InternalsVisibleTo`，避免不必要地暴露 API。

### 2.5 CI 工作流更新

`.github/workflows/package-release.yml` 需要全面更新：

| 当前 | 目标 |
|------|------|
| `setup-dotnet` 指定 .NET 8 | 指定 .NET 10 (`10.0.x`) |
| `setup-msbuild` + `msbuild readboard.sln` | `dotnet build readboard.sln` |
| `msbuild /t:Restore /p:RestorePackagesConfig=true` | `dotnet restore` |
| 验证特定 native DLL (lw.dll, OpenCvSharp3 DLLs) | 更新为 OpenCvSharp4 产出物 |

### 2.6 打包脚本更新

`scripts/package-readboard-release.local.ps1` 需要重大更新：

| 当前 | 目标 |
|------|------|
| `Platform = 'x86'` 参数校验 | 移除平台限制或改为 AnyCPU |
| `msbuild.exe` 直接调用 | `dotnet build` / `dotnet publish` |
| 期望 `readboard.exe.config` | .NET 10 产出结构（`readboard.exe` host + `readboard.dll`） |
| 检查 `lw.dll`, `MouseKeyboardActivityMonitor.dll` | 移除这些检查 |
| 检查 `OpenCvSharp3` 特定 DLL | 更新为 OpenCvSharp4 产出物 |

### 2.7 CLAUDE.md 更新

升级完成后，`CLAUDE.md` 中的构建命令需要更新：

| 当前 | 目标 |
|------|------|
| `msbuild readboard.sln /t:Restore /p:RestorePackagesConfig=true` | `dotnet restore readboard.sln` |
| `msbuild readboard.sln /p:Configuration=Release /p:Platform="Any CPU"` | `dotnet build readboard.sln -c Release` |
| "x86 target" 约束说明 | 更新为 AnyCPU |
| ".NET Framework 4.8" 说明 | 更新为 ".NET 10" |

### 2.8 验证标准

- [ ] 所有测试通过（项目引用，无源码链接）
- [ ] 所有 shim 已移除
- [ ] CI 流程正常运行（推送后验证）
- [ ] 打包脚本输出正确的发布结构

---

## Phase 3: UI 现代化

### 目标

利用 .NET 10 WinForms 新特性提升 UI 体验。

### 3.1 暗色模式

.NET 9+ WinForms 内置暗色模式支持：

```csharp
// Program.cs
Application.SetColorMode(SystemColorMode.System); // 跟随系统明暗设置
```

- 系统级控件（按钮、文本框、组框等）自动适应暗色/亮色
- 自定义绘制区域需要手动适配

### 3.2 主题系统整合

**当前：** 两套手工主题 (Classic / Optimized)，通过 `UiTheme.cs` 管理配色。

**目标：** 利用系统原生暗色模式替代手工配色，简化主题系统：
- 移除手动设置前景/背景色的代码（让系统暗色模式自动处理）
- 保留必要的自定义样式（如特定状态指示的颜色）
- 考虑是否保留 Classic/Optimized 切换，或统一为一套跟随系统的方案

### 3.3 DPI 改进

.NET 10 WinForms 对 PerMonitorV2 DPI 的支持更完善：
- 控件在不同 DPI 显示器间拖动时自动缩放
- 字体渲染质量提升
- 减少手工 DPI 计算代码

### 3.4 影响的窗体

| 窗体 | 变更范围 |
|------|---------|
| Form1 (主窗体) | 暗色模式 + DPI 改进 + 布局优化 |
| Form2 (放大镜) | 暗色模式适配 |
| Form4 (设置) | 暗色模式 + 控件样式统一 |
| Form5 (平台选择) | 暗色模式适配 |
| Form7 (提示) | 暗色模式适配 |
| FormUpdate (更新) | 暗色模式适配 |

### 3.5 验证标准

- [ ] 暗色模式下所有窗体显示正常
- [ ] 亮色模式下不退化
- [ ] 不同 DPI 设置下布局正确
- [ ] 自定义绘制区域在暗色/亮色模式下可读

---

## Risk Assessment

| 风险 | 等级 | 缓解措施 |
|------|------|---------|
| OpenCvSharp4 API breaking changes | 中 | fixture replay 测试覆盖核心识别逻辑 |
| System.Text.Json 序列化差异 | 中 | 配置文件兼容性测试，`PropertyNameCaseInsensitive` |
| P/Invoke 指针大小 (x86→AnyCPU) | 中 | 逐文件审计，集中在 HWND 参数 |
| 打包产出结构变化 | 中 | 打包脚本全面更新，与 CI 同步 |
| `AppDomain.BaseDirectory` 路径差异 | 低 | 运行时验证配置/语言文件加载 |
| 暗色模式下自定义控件显示异常 | 低 | 逐个窗体验证 |

## Execution Order

```
Phase 1 (框架+依赖+清理) → Phase 2 (测试简化+CI+打包) → Phase 3 (UI 现代化)
```

每个 Phase 完成后确保：编译通过、测试通过、功能正常。
