# Color Mode 设计与边界

日期: 2026-04-23

## 背景

.NET 10 升级后，应用默认使用 `Application.SetColorMode(SystemColorMode.System)` 跟随系统深/浅色。
本次新增"颜色模式"设置，让用户可独立于系统选择 跟随系统 / 深色 / 浅色。

## 配置

`AppConfig.ColorMode`（int）：
- `0` = `ColorModeSystem` 跟随系统（默认）
- `1` = `ColorModeDark` 强制深色
- `2` = `ColorModeLight` 强制浅色

JSON 字段名 `ColorMode`，legacy `config_readboard_others.txt` 末尾追加（位置 14，0-based），向后兼容缺字段时回退默认 0。

### JSON Schema 兼容承诺

- `ColorMode` 字段名一旦发布即视为公共合约，禁止改名（会导致老配置静默丢失用户偏好）
- 取值范围扩展（如新增 `3` = 高对比度）需保持 0/1/2 含义不变
- 测试 `DualFormatAppConfigStoreTests.Save_RoundTripsColorMode` 锁定 JSON 字段名 `"ColorMode"` 的存在
- 测试通过 `JsonDocument` 解析断言字段值，与 `JsonSerializer` 缩进/格式化选项解耦

### Legacy 写出与旧版兼容

- 新版 `WriteLegacyOtherConfig` 永远写出 15 字段（含 ColorMode）
- 旧版（v3.0.0 之前）读取该文件时只识别前 13 字段，会把 `parts[12]` 误读为 UiThemeMode（实际是 DisableShowInBoardShortcut）—— **已知降级，不可避免**
- 因此推荐用户从 v3.0.0+ 单向升级，不回退

## 启动序列

`Program.Main` 必须按此顺序：

```
InitializeRuntime              # 先读 config 拿到 ColorMode
EnableVisualStyles
SetCompatibleTextRenderingDefault(false)
SetHighDpiMode(PerMonitorV2)
SetColorMode(GetSystemColorMode(Config.ColorMode))
```

`SetColorMode` 必须在 `EnableVisualStyles` 之后调用，否则颜色模式可能不生效。

`GetSystemColorMode` 映射：
- `ColorModeDark` → `SystemColorMode.Dark`
- `ColorModeLight` → `SystemColorMode.Classic`（这是 .NET 10 中"强制浅色经典 visual style"的语义，命名混乱但行为正确）
- `ColorModeSystem` → `SystemColorMode.System`

## UiTheme.IsDarkMode 合约

```csharp
public static bool IsDarkMode { get; }
```

判定优先级：
1. 用户配置 `ColorMode = Dark` → true
2. 用户配置 `ColorMode = Light` → false
3. `ColorMode = System` → 看 `Application.SystemColorMode == SystemColorMode.Dark`

**容错**: 当 `Program.CurrentContext` 或 `Program.CurrentConfig` 不可用（设计器、单元测试无 runtime 初始化）时，回退到"跟随系统"语义，不抛异常。catch 范围限定为 `NullReferenceException`，避免吞掉真实 bug。

## 主题切换矩阵

`UiThemeMode` 控制窗体样式分支，`ColorMode` 控制配色：

| UiThemeMode | IsDarkMode | 窗体调用 | UiTheme 配色 |
|---|---|---|---|
| Optimized | true | `ApplyOptimizedMainFormTheme` | 深色 |
| Optimized | false | `ApplyOptimizedMainFormTheme` | 浅色 |
| Classic | * | `ApplyClassicMainFormTheme` | SystemColors（不走 UiTheme） |

**命名说明**: 历史上曾短暂改名为 `ApplyDarkMainFormTheme`，但由于该方法同时承担深色与浅色的优化主题入口，且既有 parity 回归测试（`MainFormThemeStartupParityRegressionTests.MainForm_AppliesDistinctClassicAndNewThemeVisuals`）锁定 `ApplyOptimizedMainFormTheme` 名称，已恢复原名。维护者注意：方法体内的 `UiTheme.StyleXxx` 自带 `IsDarkMode` 分支，配色随 ColorMode 自动适配。

## 运行时变更策略

修改 ColorMode 必须重启才生效，因为 `Application.SetColorMode` 仅在启动时调用。

`Form4.button1_Click` 检测 `ColorMode` 变更后弹 `MessageBox` 提示用户重启。
不自动 `Application.Restart()`，避免打断用户当前同步任务。

**视觉过渡边界**: 用户改 ColorMode 但未重启时，若 DPI 变化或主题切换触发 `ApplyMainFormUi` 重绘，控件配色会立即按新 ColorMode 重新计算（因为 `IsDarkMode` 读 `Program.CurrentConfig.ColorMode`），但系统 chrome（标题栏等）由 `Application.SetColorMode` 控制，仅启动时生效 → 短暂视觉不一致。重启后修复。该状态由用户主动选择"不立即重启"承担。

## 不在范围内（已评审，决策为不修复）

| 项 | 决策 | 原因 |
|---|---|---|
| 浅色优化主题 RadioButton 选中态加亮边框 | 不修复 | 浅色 Flat 下系统圆点已能区分选中态 |
| 颜色模式 RadioButton 翻译过长溢出 | 不修复 | 当前 cn/en/jp/kr 翻译长度可控 |
| 颜色模式 RadioButton 包 GroupBox | 不修复 | 当前设置面板无其他 RadioButton，不会冲突 |
| `Application.Restart()` 一键重启 | 不修复 | 会打断同步任务 |
| `DualFormatAppConfigStore` 重构为强类型 DTO 或 `Dictionary<string, JsonElement>` | 不修复 | 现有 `value.ToString()` + `*.TryParse` 模式正确处理 `JsonElement`，已被 13 个 store 测试验证 |
| `GitHubUpdateChecker` 重构为 DTO + `Deserialize<T>` | 不修复 | 风格建议；现有实现已被 8 个用例覆盖 |

## JsonElement 兼容性

`DualFormatAppConfigStore` 反序列化为 `Dictionary<string, object>`，在 `System.Text.Json` 下 value 实际是 `JsonElement`。`Read*Value` 系列通过 `value.ToString()` 取原始 JSON 文本：

- `JsonElement` (Number) → `ToString()` 返回数字字面量（如 `"42"`）→ `int.TryParse` 解析正确
- `JsonElement` (True/False) → `ToString()` 返回 `"True"`/`"False"` → `bool.TryParse` 解析正确
- `JsonElement` (String) → `ToString()` 返回字符串内容（不含引号）

此机制已被 `Save_RoundTripsColorMode` 与 `DualFormatAppConfigStoreTests` 全套用例验证。

## Win32 Hook 失败处理

`GlobalMouseHook` / `GlobalKeyboardHook` 调用 `SetWindowsHookEx`，可能在低权限或被安全软件拦截时返回 `IntPtr.Zero`：

- `GlobalMouseHook.Start`: 失败时 `enabled = false` 保持状态一致性，`Trace.WriteLine` 记录（Release 与 Debug 都生效）
- `GlobalKeyboardHook.Start`: 无 `Enabled` 状态对外暴露，失败时 `Trace.WriteLine` 记录
- 不抛异常：避免现有调用点必须加 try/catch；钩子失败不应阻塞应用启动

## 测试覆盖

- `AppConfigDefaultsTests`: 默认 `ColorMode = 0`
- `DualFormatAppConfigStoreTests.Save_RoundTripsColorMode`: 三种值往返 + JSON 字段名/值断言（用 `JsonDocument` 解耦格式）
- `DualFormatAppConfigStoreTests.Save_WritesJsonAndLegacyMirrorWithUpdatedMetadata`: legacy 格式末位追加 ColorMode
- `FrameworkContractTests.Program_WiresColorModeStartupThroughGetSystemColorMode`: source-level 锁定 `Application.SetColorMode(GetSystemColorMode(Config.ColorMode))` 与三个分支映射
