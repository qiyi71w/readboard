# MainForm Theme Layout Implementation Plan

> 说明：这份计划对应上一轮“主题布局修复”的实现范围。当前关于“启动主题不同导致窗口尺寸和布局分叉”的修复，已转由 [2026-04-21-mainform-theme-startup-parity.md](/mnt/d/dev/weiqi/readboard/docs/superpowers/plans/2026-04-21-mainform-theme-startup-parity.md) 作为执行入口；本文件保留用于追溯已完成的布局修复决策。

> **For agentic workers:** Choose the execution workflow explicitly. Use `superpowers:executing-plans` when higher-priority instructions prefer inline execution or tasks are tightly coupled. Use `superpowers:subagent-driven-development` only when tasks are truly independent and delegation is explicitly desired. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复主窗口新主题的大空白与同步区错位，让默认主题和新版主题共用同一套布局骨架，同时保持不同 DPI / 分辨率下的稳定行为，并把 `修复版主题` 统一更名为 `新版主题`。

**Architecture:** 保持主窗口布局逻辑集中在 [`readboard/Form1.cs`](/mnt/d/dev/weiqi/readboard/readboard/Form1.cs)，引入 `MainHeaderLayoutMetrics` 和一组 theme-neutral measurement helpers，让主题切换只改变视觉样式、不改变排版决策。新增独立的主窗口布局回归测试文件，锁住头部锚点、同步区共享列和主题命名，避免后续 DPI/主题修补再次把布局打散。

**Tech Stack:** C# / WinForms / .NET Framework、xUnit 源码回归测试、现有 `DisplayScaling` 缩放逻辑、多语言文本资源。

---

## File Map

- Modify: [`readboard/Form1.cs`](/mnt/d/dev/weiqi/readboard/readboard/Form1.cs)
  - 引入 `MainHeaderLayoutMetrics`
  - 增加主窗口布局专用测量 helper
  - 调整 `ApplyMainFormUi()`、头部/棋盘/同步区布局锚点
  - 统一同步区共享列宽与控件槽位
- Modify: [`readboard/Program.cs`](/mnt/d/dev/weiqi/readboard/readboard/Program.cs)
  - 内置中文字符串 `MainForm_themeOptimized` 改为 `新版主题`
- Modify: [`readboard/language_cn.txt`](/mnt/d/dev/weiqi/readboard/readboard/language_cn.txt)
- Modify: [`readboard/language_en.txt`](/mnt/d/dev/weiqi/readboard/readboard/language_en.txt)
- Modify: [`readboard/language_jp.txt`](/mnt/d/dev/weiqi/readboard/readboard/language_jp.txt)
- Modify: [`readboard/language_kr.txt`](/mnt/d/dev/weiqi/readboard/readboard/language_kr.txt)
  - 同步更新主题显示名称
- Create: [`tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs`](/mnt/d/dev/weiqi/readboard/tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs)
  - 新增主窗口主题布局回归测试

### Task 1: 锁定主窗口共享布局骨架

**Files:**
- Create: `tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs`
- Modify: `readboard/Form1.cs`
- Test: `tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs`

- [ ] **Step 1: 先写失败中的布局骨架回归测试**

```csharp
using System;
using System.IO;
using Readboard.VerificationTests;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class MainFormThemeLayoutRegressionTests
    {
        [Fact]
        public void MainForm_DefinesThemeNeutralHeaderMetricsAndMeasurementHelpers()
        {
            string source = LoadSource("readboard", "Form1.cs");

            Assert.Contains("private readonly struct MainHeaderLayoutMetrics", source);
            Assert.Contains("private MainHeaderLayoutMetrics ArrangeMainHeader()", source);
            Assert.Contains("private int MeasureMainLayoutButtonWidth(Button button, int minimumLogicalWidth)", source);
            Assert.Contains("private int MeasureMainLayoutOptionWidth(ButtonBase option, int minimumLogicalWidth)", source);
            Assert.Contains("private int MeasureMainLayoutLabelWidth(Label label, int minimumLogicalWidth = 0)", source);
            Assert.Contains("TextRenderer.MeasureText(button.Text, Control.DefaultFont).Width", source);
            Assert.Contains("TextRenderer.MeasureText(button.Text, UiTheme.BodyFont).Width", source);
        }

        [Fact]
        public void MainForm_HeaderMetrics_RecordPlatformBottomAndUtilityBottomSeparately()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string legacyHeader = GetMethodSlice(source, "private MainHeaderLayoutMetrics ArrangeLegacyMainHeader()");
            string adaptiveHeader = GetMethodSlice(source, "private MainHeaderLayoutMetrics ArrangeAdaptiveMainHeader()");

            Assert.Contains("return new MainHeaderLayoutMetrics(groupBox1.Bottom, btnCheckUpdate.Bottom, groupBox1.Width, true);", legacyHeader);
            Assert.Contains("return new MainHeaderLayoutMetrics(groupBox1.Bottom, btnCheckUpdate.Bottom, groupBox1.Width, true);", adaptiveHeader);
            Assert.Contains("return new MainHeaderLayoutMetrics(btnCheckUpdate.Bottom, btnCheckUpdate.Bottom, contentWidth, false);", adaptiveHeader);
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path);
        }

        private static string GetMethodSlice(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing signature: {signature}");
            int braceStart = source.IndexOf('{', start);
            int depth = 0;
            for (int index = braceStart; index < source.Length; index++)
            {
                if (source[index] == '{')
                    depth++;
                else if (source[index] == '}')
                    depth--;

                if (depth == 0)
                    return source.Substring(start, index - start + 1);
            }

            throw new InvalidOperationException($"Could not slice method: {signature}");
        }
    }
}
```

- [ ] **Step 2: 运行测试并确认它先失败**

```bash
timeout 60s dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~MainFormThemeLayoutRegressionTests" --no-restore
```

Expected: FAIL，提示缺少 `MainHeaderLayoutMetrics`、`MeasureMainLayoutButtonWidth` 或 `ArrangeMainHeader()` 仍然返回 `int`。

- [ ] **Step 3: 在 `Form1.cs` 里加入共享头部度量和 theme-neutral measurement helpers**

```csharp
private readonly struct MainHeaderLayoutMetrics
{
    public MainHeaderLayoutMetrics(int platformBottom, int utilityBottom, int platformWidth, bool utilitiesInRightColumn)
    {
        PlatformBottom = platformBottom;
        UtilityBottom = utilityBottom;
        PlatformWidth = platformWidth;
        UtilitiesInRightColumn = utilitiesInRightColumn;
    }

    public int PlatformBottom { get; }
    public int UtilityBottom { get; }
    public int PlatformWidth { get; }
    public bool UtilitiesInRightColumn { get; }
}

private int MeasureMainLayoutButtonWidth(Button button, int minimumLogicalWidth)
{
    int minimumWidth = ScaleValue(minimumLogicalWidth);
    int classicWidth = TextRenderer.MeasureText(button.Text, Control.DefaultFont).Width;
    int optimizedWidth = TextRenderer.MeasureText(button.Text, UiTheme.BodyFont).Width;
    return Math.Max(minimumWidth, Math.Max(classicWidth, optimizedWidth) + ScaleValue(28));
}

private int MeasureMainLayoutOptionWidth(ButtonBase option, int minimumLogicalWidth)
{
    int minimumWidth = ScaleValue(minimumLogicalWidth);
    int classicWidth = TextRenderer.MeasureText(option.Text, Control.DefaultFont).Width;
    int optimizedWidth = TextRenderer.MeasureText(option.Text, UiTheme.BodyFont).Width;
    return Math.Max(minimumWidth, Math.Max(classicWidth, optimizedWidth) + ScaleValue(24));
}

private int MeasureMainLayoutLabelWidth(Label label, int minimumLogicalWidth = 0)
{
    int minimumWidth = ScaleValue(minimumLogicalWidth);
    int classicWidth = TextRenderer.MeasureText(label.Text, Control.DefaultFont).Width;
    int optimizedWidth = TextRenderer.MeasureText(label.Text, UiTheme.BodyFont).Width;
    return Math.Max(minimumWidth, Math.Max(classicWidth, optimizedWidth));
}

private MainHeaderLayoutMetrics ArrangeMainHeader()
{
    if (CanUseLegacyMainDesktopLayout())
        return ArrangeLegacyMainHeader();

    return ArrangeAdaptiveMainHeader();
}
```

同时把头部/宽度判定里的按钮与选项测量替换成新的 helper：

```csharp
int settingsWidth = MeasureMainLayoutButtonWidth(btnSettings, 72);
int helpWidth = MeasureMainLayoutButtonWidth(btnHelp, 68);
int themeWidth = MeasureMainLayoutButtonWidth(btnTheme, 68);
int komiWidth = MeasureMainLayoutButtonWidth(btnKomi65, 170);
int updateWidth = MeasureMainLayoutButtonWidth(btnCheckUpdate, 170);
```

并让两个头部方法返回明确的 metrics：

```csharp
return new MainHeaderLayoutMetrics(groupBox1.Bottom, btnCheckUpdate.Bottom, groupBox1.Width, true);
```

```csharp
return new MainHeaderLayoutMetrics(btnCheckUpdate.Bottom, btnCheckUpdate.Bottom, contentWidth, false);
```

- [ ] **Step 4: 再跑同一组测试，确认现在通过**

```bash
timeout 60s dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~MainFormThemeLayoutRegressionTests" --no-restore
```

Expected: PASS，`MainFormThemeLayoutRegressionTests` 全绿。

- [ ] **Step 5: 提交这一组 guardrails**

```bash
git add tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs readboard/Form1.cs
git commit -m "refactor(ui): 抽取主窗口共享布局度量"
```

### Task 2: 修复头部到棋盘区/同步区的锚点链路

**Files:**
- Modify: `tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs`
- Modify: `readboard/Form1.cs`
- Test: `tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs`

- [ ] **Step 1: 写出头部、棋盘区、同步区之间的失败回归测试**

```csharp
[Fact]
public void ApplyMainFormUi_UsesPlatformBottomForBoardAndUtilitySafeBottomForSync()
{
    string source = LoadSource("readboard", "Form1.cs");
    string methodSlice = GetMethodSlice(source, "private void ApplyMainFormUi()");

    Assert.Contains("MainHeaderLayoutMetrics headerLayout = ArrangeMainHeader();", methodSlice);
    Assert.Contains("int boardBottom = ArrangeMainBoardSection(headerLayout.PlatformBottom + ScaleValue(12), headerLayout);", methodSlice);
    Assert.Contains("int syncTop = Math.Max(boardBottom, headerLayout.UtilityBottom) + ScaleValue(12);", methodSlice);
    Assert.Contains("int syncBottom = ArrangeMainSyncSection(syncTop);", methodSlice);
    Assert.Contains("ArrangeMainActions(syncBottom + ScaleValue(12));", methodSlice);
}

[Fact]
public void AdaptiveBoardLayout_UsesHeaderPlatformWidthWhenUtilitiesStayAtRight()
{
    string source = LoadSource("readboard", "Form1.cs");
    string methodSlice = GetMethodSlice(source, "private int ArrangeAdaptiveMainBoardSection(int top, MainHeaderLayoutMetrics headerLayout)");

    Assert.Contains("int groupWidth = headerLayout.UtilitiesInRightColumn ? headerLayout.PlatformWidth : contentWidth;", methodSlice);
    Assert.Contains("groupBox2.SetBounds(left, top, groupWidth, 0);", methodSlice);
}
```

- [ ] **Step 2: 运行测试并确认它先失败**

```bash
timeout 60s dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~MainFormThemeLayoutRegressionTests" --no-restore
```

Expected: FAIL，提示 `ApplyMainFormUi()` 仍按单一 `nextTop` 串起来，`ArrangeAdaptiveMainBoardSection` 也还没有接收 `headerLayout`。

- [ ] **Step 3: 改造主布局流，让棋盘区只跟平台区走，同步区再看 utility bottom**

```csharp
private void ApplyMainFormUi()
{
    // ... existing setup ...
    ApplyMainFormTheme();

    MainHeaderLayoutMetrics headerLayout = ArrangeMainHeader();
    int boardBottom = ArrangeMainBoardSection(headerLayout.PlatformBottom + ScaleValue(12), headerLayout);
    int syncTop = Math.Max(boardBottom, headerLayout.UtilityBottom) + ScaleValue(12);
    int syncBottom = ArrangeMainSyncSection(syncTop);
    ArrangeMainActions(syncBottom + ScaleValue(12));
}

private int ArrangeMainBoardSection(int top, MainHeaderLayoutMetrics headerLayout)
{
    if (CanUseLegacyMainDesktopLayout())
        return ArrangeLegacyMainBoardSection(top);

    return ArrangeAdaptiveMainBoardSection(top, headerLayout);
}

private int ArrangeAdaptiveMainBoardSection(int top, MainHeaderLayoutMetrics headerLayout)
{
    int left = ScaleValue(12);
    int contentWidth = ClientSize.Width - left * 2;
    int groupWidth = headerLayout.UtilitiesInRightColumn ? headerLayout.PlatformWidth : contentWidth;

    groupBox2.SetBounds(left, top, groupWidth, 0);
    // existing inner layout stays here
}
```

Legacy header 和 adaptive side-by-side header 继续返回：

```csharp
return new MainHeaderLayoutMetrics(groupBox1.Bottom, btnCheckUpdate.Bottom, groupBox1.Width, true);
```

Stacked adaptive header 返回：

```csharp
return new MainHeaderLayoutMetrics(btnCheckUpdate.Bottom, btnCheckUpdate.Bottom, contentWidth, false);
```

- [ ] **Step 4: 重新运行测试，确认锚点链路生效**

```bash
timeout 60s dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~MainFormThemeLayoutRegressionTests" --no-restore
```

Expected: PASS，新增的锚点测试和 Task 1 测试都通过。

- [ ] **Step 5: 提交主窗口头部/棋盘区布局修复**

```bash
git add tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs readboard/Form1.cs
git commit -m "fix(ui): 修复主窗口头部与棋盘区锚点关系"
```

### Task 3: 统一同步区的共享列与上下对齐

**Files:**
- Modify: `tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs`
- Modify: `readboard/Form1.cs`
- Test: `tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs`

- [ ] **Step 1: 先写同步区共享列的失败测试**

```csharp
[Fact]
public void MainForm_SyncRows_UseSharedToggleSlotsAndSharedVisitsColumns()
{
    string source = LoadSource("readboard", "Form1.cs");
    string legacySlice = GetMethodSlice(source, "private int ArrangeLegacyMainSyncSection(int top)");
    string adaptiveSlice = GetMethodSlice(source, "private int ArrangeAdaptiveMainSyncSection(int top)");

    Assert.Contains("int sharedToggleWidth = GetSharedMainSyncToggleWidth();", legacySlice);
    Assert.Contains("int sharedColorWidth = GetSharedMainSyncColorWidth();", legacySlice);
    Assert.Contains("int sharedConditionSlotWidth = GetSharedMainSyncConditionSlotWidth();", legacySlice);
    Assert.Contains("int sharedVisitsLabelWidth = GetSharedMainSyncVisitsLabelWidth();", legacySlice);
    Assert.Contains("chkBothSync.AutoSize = false;", legacySlice);
    Assert.Contains("chkAutoPlay.AutoSize = false;", adaptiveSlice);
    Assert.Contains("panel1.Size = new Size(sharedConditionSlotWidth, rowHeight);", legacySlice);
    Assert.Contains("panel2.Size = new Size(sharedVisitsLabelWidth, rowHeight);", legacySlice);
    Assert.Contains("panel4.Size = new System.Drawing.Size(sharedVisitsLabelWidth, rowHeight);", adaptiveSlice);
}

[Fact]
public void MainForm_DefinesSharedSyncSlotHelpers()
{
    string source = LoadSource("readboard", "Form1.cs");

    Assert.Contains("private int GetSharedMainSyncToggleWidth()", source);
    Assert.Contains("private int GetSharedMainSyncColorWidth()", source);
    Assert.Contains("private int GetSharedMainSyncConditionSlotWidth()", source);
    Assert.Contains("private int GetSharedMainSyncVisitsLabelWidth()", source);
}
```

- [ ] **Step 2: 运行测试并确认它先失败**

```bash
timeout 60s dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~MainFormThemeLayoutRegressionTests" --no-restore
```

Expected: FAIL，因为 `Form1.cs` 还没有共享的 toggle / color / condition slot helper。

- [ ] **Step 3: 让同步区两行使用相同槽位宽度与 theme-neutral label width**

```csharp
private const int MainSyncConditionInputLogicalWidth = 68;
private const int MainSyncVisitsInputLogicalWidth = 92;

private int GetSharedMainSyncToggleWidth()
{
    return Math.Max(chkBothSync.PreferredSize.Width, chkAutoPlay.PreferredSize.Width);
}

private int GetSharedMainSyncColorWidth()
{
    return Math.Max(radioBlack.PreferredSize.Width, radioWhite.PreferredSize.Width);
}

private int GetSharedMainSyncConditionLabelWidth()
{
    return Math.Max(MeasureMainLayoutLabelWidth(lblPlayCondition), MeasureMainLayoutLabelWidth(lblTime));
}

private int GetSharedMainSyncConditionSlotWidth()
{
    return GetSharedMainSyncConditionLabelWidth() + ScaleValue(8) + ScaleValue(MainSyncConditionInputLogicalWidth);
}

private int GetSharedMainSyncVisitsLabelWidth()
{
    return Math.Max(MeasureMainLayoutLabelWidth(lblTotalVisits), MeasureMainLayoutLabelWidth(lblBestMoveVisits));
}
```

在 legacy 和 adaptive 两个同步区方法里统一这些槽位：

```csharp
int sharedToggleWidth = GetSharedMainSyncToggleWidth();
int sharedColorWidth = GetSharedMainSyncColorWidth();
int sharedConditionSlotWidth = GetSharedMainSyncConditionSlotWidth();
int sharedVisitsLabelWidth = GetSharedMainSyncVisitsLabelWidth();

chkBothSync.AutoSize = false;
chkAutoPlay.AutoSize = false;
radioBlack.AutoSize = false;
radioWhite.AutoSize = false;

chkBothSync.Size = new Size(sharedToggleWidth, rowHeight);
chkAutoPlay.Size = new Size(sharedToggleWidth, rowHeight);
radioBlack.Size = new Size(sharedColorWidth, rowHeight);
radioWhite.Size = new Size(sharedColorWidth, rowHeight);

panel1.Size = new Size(sharedConditionSlotWidth, rowHeight);
panel2.Size = new Size(sharedVisitsLabelWidth, rowHeight);
panel3.Size = new Size(GetSharedMainSyncConditionLabelWidth(), rowHeight);
panel4.Size = new Size(sharedVisitsLabelWidth, rowHeight);

textBox1.Size = new Size(ScaleValue(MainSyncConditionInputLogicalWidth), rowHeight);
textBox2.Size = new Size(ScaleValue(MainSyncVisitsInputLogicalWidth), rowHeight);
textBox3.Size = new Size(ScaleValue(MainSyncVisitsInputLogicalWidth), rowHeight);
```

这一步的目标是让：

- `最大计算量(选填)` / `首选计算量(选填)` 上下严格对齐
- `引擎自动落子条件` / `每手用时` 的槽位也共用宽度
- 主题切换后因为字体不同导致的标签宽度漂移被压平

- [ ] **Step 4: 重新跑回归测试，确认同步区对齐已经锁住**

```bash
timeout 60s dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~MainFormThemeLayoutRegressionTests" --no-restore
```

Expected: PASS，新的同步区断言和前两步的头部断言一起通过。

- [ ] **Step 5: 提交同步区共享列修复**

```bash
git add tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs readboard/Form1.cs
git commit -m "fix(ui): 统一同步区共享列并修复上下对齐"
```

### Task 4: 更新主题显示名并完成验证

**Files:**
- Modify: `tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs`
- Modify: `readboard/Program.cs`
- Modify: `readboard/language_cn.txt`
- Modify: `readboard/language_en.txt`
- Modify: `readboard/language_jp.txt`
- Modify: `readboard/language_kr.txt`
- Test: `tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs`

- [ ] **Step 1: 先写主题命名回归测试**

```csharp
[Fact]
public void ThemeResources_RenameOptimizedThemeToNewTheme()
{
    Assert.Contains("langItems[\"MainForm_themeOptimized\"] = \"新版主题\";", LoadSource("readboard", "Program.cs"));
    Assert.Contains("MainForm_themeOptimized=新版主题", LoadSource("readboard", "language_cn.txt"));
    Assert.Contains("MainForm_themeOptimized=New Theme", LoadSource("readboard", "language_en.txt"));
    Assert.Contains("MainForm_themeOptimized=新テーマ", LoadSource("readboard", "language_jp.txt"));
    Assert.Contains("MainForm_themeOptimized=새 테마", LoadSource("readboard", "language_kr.txt"));

    Assert.DoesNotContain("修复版主题", LoadSource("readboard", "Program.cs"));
    Assert.DoesNotContain("MainForm_themeOptimized=修复版主题", LoadSource("readboard", "language_cn.txt"));
}
```

- [ ] **Step 2: 运行测试并确认它先失败**

```bash
timeout 60s dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~MainFormThemeLayoutRegressionTests.ThemeResources_RenameOptimizedThemeToNewTheme" --no-restore
```

Expected: FAIL，因为当前资源里还是 `修复版主题 / Refined Theme / 調整済みテーマ / 보정 테마`。

- [ ] **Step 3: 修改内置字符串和多语言资源**

```csharp
langItems["MainForm_themeOptimized"] = "新版主题";
```

```text
MainForm_themeOptimized=新版主题
```

```text
MainForm_themeOptimized=New Theme
```

```text
MainForm_themeOptimized=新テーマ
```

```text
MainForm_themeOptimized=새 테마
```

- [ ] **Step 4: 跑完自动验证，再做一轮人工验证**

```bash
timeout 60s dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~MainFormThemeLayoutRegressionTests" --no-restore
timeout 60s dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~Host" --no-restore
```

Expected: PASS，`MainFormThemeLayoutRegressionTests` 和 Host 相关回归测试全绿。

人工验证清单：

- 默认主题和新版主题切换后，`平台类型 / 棋盘规格 / 同步与自动落子` 的位置一致。
- 宽窗口下，`棋盘规格` 填到 `检查更新` 左边，不再留下大空白。
- `同步与自动落子` 保持在 `棋盘规格` 下方，且不会盖住右侧工具列。
- `最大计算量(选填)` 与 `首选计算量(选填)` 完整上下对齐。
- `100% / 125% / 150% / 200%` 缩放下没有截断、重叠和异常空白。
- 把 `检查更新` 切到 `检查中`，按钮仍保持同一列宽度，不把布局挤乱。

- [ ] **Step 5: 提交主题命名和最终验证通过的改动**

```bash
git add tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs readboard/Form1.cs readboard/Program.cs readboard/language_cn.txt readboard/language_en.txt readboard/language_jp.txt readboard/language_kr.txt
git commit -m "fix(i18n): 统一新版主题显示名称"
```
