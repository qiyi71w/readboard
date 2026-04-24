# Self-Contained 发布设计与边界

日期: 2026-04-24

## 背景

v3.0.0 前，readboard 以 framework-dependent 模式发布（~50MB，15 文件），用户必须自行安装 .NET 10 Desktop Runtime 才能运行。
为降低用户门槛，发布改为 self-contained + win-x64，用户开箱即用 `readboard.exe`。

## 发布模式

- `dotnet publish -c Release -r win-x64 --self-contained true`
- 输出目录：`readboard/bin/Release/net10.0-windows/win-x64/publish/`
- Release 产物：~237MB，282 文件 + 13 个本地化卫星程序集子目录（cs/de/.../zh-Hans/zh-Hant）

### 为何锁 win-x64

- 依赖 `OpenCvSharp4.Windows` NuGet 包，该包仅提供 x64/x86 原生 DLL，无 ARM64
- readboard 历史上一直是 Windows x64 桌面应用，用户群不含 ARM64 设备
- 若未来要支持 ARM64，需同时替换 OpenCV 原生依赖并产出双 RID 包

## 打包脚本约束

`scripts/package-readboard-release.local.ps1`:

- `dotnet build` → `dotnet publish`，构建命令名一旦改回会导致 framework-dependent 回归
- `Copy-DirectoryContents` 必须用 `Copy-Item -Recurse`，否则漏掉 13 个卫星程序集子目录（约 52 个 `.resources.dll`）
- `BuildOutputDir` 默认指向 `publish/` 而非 build 输出根，`-SkipBuild` 测试传入的扁平 fake 目录仍能工作
- 移除了 `buildPatterns` / `optionalStaticPatterns` / `nativeRuntimePatterns` 三个 glob 清单：publish 本身就产出所有必要文件（runtime DLL、OpenCV 原生库、language/readme 资源），无需脚本再二次筛选复制

### 测试锁定

`FrameworkContractTests`:
- `PackagingScriptAndWorkflow_UseNet10BuildContract`: 断言 `dotnet publish` + `--self-contained true` + `-r win-x64` 三个字符串同时存在
- `PackagingScript_UsesDotnetPublishForReadboardProject`: 由 `UsesDotnetBuildForReadboardProject` 重命名而来

## Reproducible Build：SDK 版本钉死

`global.json` 钉 `10.0.104`，`rollForward: latestFeature`:
- 允许次级 feature band 滚动（如 10.0.2xx），但不跨 major
- 保证本地与 CI 环境使用同一 SDK，避免 `deps.json` / `runtimeconfig.json` 因 SDK 补丁版本差异产出不同字节的 zip
- CI 侧 `actions/setup-dotnet` 会读取 `global.json` 优先于 `dotnet-version` 参数

维护者升级 SDK 时：同步改 `global.json` 与 workflow 的 `dotnet-version`，并本地重新打包验证。

## 不在范围内（已评审，决策为不修复）

| 项 | 决策 | 原因 |
|---|---|---|
| PublishSingleFile 单 exe 打包 | 不修复 | OpenCvSharpExtern 原生依赖难以单文件化，且单文件首次启动需解压会变慢 |
| PublishTrimmed 裁剪未用程序集 | 不修复 | WinForms + 反射重度依赖，裁剪后高概率运行时异常 |
| 剔除 13 个本地化卫星程序集以瘦身 | 不修复 | 收益 <5MB，破坏 .NET 自带错误消息本地化，得不偿失 |
| 同时发布 x86 / ARM64 RID | 不修复 | OpenCvSharp4.Windows 无 ARM64 原生库；x86 需求已随 Win10+ 普及消失 |
| ReadyToRun 预编译加速启动 | 不修复 | 体积再涨 ~30%，启动加速 100-300ms 对桌面工具不构成价值 |

## 用户升级路径

- 用户从 framework-dependent 旧版升级到 self-contained 新版：直接替换 `readboard/` 目录即可，无需卸载旧 .NET 运行时
- 配置文件 `config.readboard.json` / 两个 legacy txt 格式不变，跨模式兼容
