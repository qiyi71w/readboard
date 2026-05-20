# Fox Auto Play Color Detection Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a safe Fox-only `执黑 / 执白 / 自动` auto-play color mode so readboard can decide whether to send `play black ...` or `play white ...` after the user saves their Fox identity.

**Architecture:** Keep the wire protocol unchanged and resolve auto color before `SyncCoordinatorHostSnapshot.PlayColor` is populated. Manual black/white stays in `MainForm`; Fox auto mode stores a remembered nickname plus a visual row signature, then uses a small, testable analyzer to match the current Fox player row and read the row's black/white icon. If the result is not reliable, the resolver returns unknown and no `play` command is sent.

**Tech Stack:** WinForms, .NET 10, System.Drawing bitmap/pixel analysis, xUnit verification tests, existing readboard text protocol.

---

## Assumptions

- Do not add OCR or a new external runtime dependency in the first implementation.
- The saved Fox nickname is for user-facing clarity and settings; actual automatic matching uses the visual signature captured from the user-confirmed row.
- Auto mode applies only to `SyncMode.Fox` and `SyncMode.FoxBackgroundPlace`.
- The first implementation may need real Fox screenshots for final tuning, but unit tests should cover the resolver and image heuristics with synthetic bitmaps.
- The current root worktree has an unrelated untracked `main-wip-backup.patch`; keep all implementation work inside `.claude/worktrees/fox-auto-color-detection`.

## Relevant Docs Checked

- `docs/DEVELOPMENT.md`
- `docs/specs/2026-04-21-readboard-fox-title-status-design.md`
- `docs/specs/2026-05-04-sync-session-outbound-send-phase3.md`
- `docs/specs/2026-05-20-fox-auto-play-color-detection-design.md`

## File Map

- Modify: `readboard/Core/Models/AppConfig.cs`
  - Add persisted auto-play color mode and Fox identity fields.
- Modify: `readboard/Core/Configuration/DualFormatAppConfigStore.cs`
  - Load/save new JSON properties.
  - Extend legacy other config by appending new fields only at the end.
- Create: `readboard/Core/AutoPlay/AutoPlayColorMode.cs`
  - Define `ManualBlack`, `ManualWhite`, `FoxAuto`.
- Create: `readboard/Core/AutoPlay/AutoPlayColorStatus.cs`
  - Define UI/logic statuses such as `ManualBlack`, `ManualWhite`, `Unconfigured`, `RecognizedBlack`, `RecognizedWhite`, `NicknameNotMatched`, `ColorUnknown`, `UnsupportedPlatform`.
- Create: `readboard/Core/AutoPlay/AutoPlayColorResolution.cs`
  - Immutable result with `PlayColor`, `Status`, and `IsKnown`.
- Create: `readboard/Core/AutoPlay/FoxPlayerRowCandidate.cs`
  - Holds row bounds, stone icon bounds, optional row signature.
- Create: `readboard/Core/AutoPlay/FoxPlayerRowSignature.cs`
  - Build and compare normalized visual signatures from row snippets.
- Create: `readboard/Core/AutoPlay/FoxPlayerStoneIconDetector.cs`
  - Pure black/white/unknown detector for row icon snippets.
- Create: `readboard/Core/AutoPlay/FoxAutoPlayColorResolver.cs`
  - Applies manual/auto priority and returns `AutoPlayColorResolution`.
- Modify: `readboard/Form1.Designer.cs`
  - Add `radioAutoPlayColor` and `lblAutoPlayColorStatus`.
- Modify: `readboard/Form1.cs`
  - Wire the third radio option, status text, config persistence, and `CaptureSnapshotCore()`.
- Modify: `readboard/MainForm.Configuration.cs`
  - Apply and persist `AutoPlayColorMode`.
- Create: `readboard/FoxAutoPlayIdentityDialog.cs`
  - First-run dialog for selecting current row and saving nickname.
- Create: `readboard/FoxAutoPlayIdentityDialog.Designer.cs`
  - Minimal WinForms controls for row choices, nickname input, remember checkbox.
- Modify: `readboard/Form4.Designer.cs`
  - Add Fox nickname settings controls.
- Modify: `readboard/Form4.cs`
  - Load/save/clear saved nickname and signature.
- Modify: `readboard/Program.cs`
  - Add default language keys.
- Modify: `readboard/language_cn.txt`, `readboard/language_en.txt`, `readboard/language_jp.txt`, `readboard/language_kr.txt`
  - Add UI strings.
- Test: `tests/Readboard.VerificationTests/Configuration/AppConfigDefaultsTests.cs`
- Test: `tests/Readboard.VerificationTests/Configuration/DualFormatAppConfigStoreTests.cs`
- Test: `tests/Readboard.VerificationTests/AutoPlay/FoxAutoPlayColorResolverTests.cs`
- Test: `tests/Readboard.VerificationTests/AutoPlay/FoxPlayerStoneIconDetectorTests.cs`
- Test: `tests/Readboard.VerificationTests/AutoPlay/FoxPlayerRowSignatureTests.cs`
- Test: `tests/Readboard.VerificationTests/Host/StartupAndShutdownRegressionTests.cs`
- Test: `tests/Readboard.VerificationTests/Host/HighDpiSourceRegressionTests.cs`

## Task 1: Persist Auto-Play Mode And Fox Identity

**Files:**
- Create: `readboard/Core/AutoPlay/AutoPlayColorMode.cs`
- Modify: `readboard/Core/Models/AppConfig.cs`
- Modify: `readboard/Core/Configuration/DualFormatAppConfigStore.cs`
- Test: `tests/Readboard.VerificationTests/Configuration/AppConfigDefaultsTests.cs`
- Test: `tests/Readboard.VerificationTests/Configuration/DualFormatAppConfigStoreTests.cs`

- [ ] **Step 1: Write failing config default tests**

Add assertions:

```csharp
Assert.Equal(AutoPlayColorMode.ManualBlack, config.AutoPlayColorMode);
Assert.True(string.IsNullOrEmpty(config.FoxAutoPlayNickname));
Assert.True(string.IsNullOrEmpty(config.FoxAutoPlayRowSignature));
```

- [ ] **Step 2: Run default config test and verify it fails**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~AppConfigDefaultsTests" -c Debug
```

Expected: FAIL because the new properties do not exist.

- [ ] **Step 3: Add the enum and default properties**

Create `readboard/Core/AutoPlay/AutoPlayColorMode.cs`:

```csharp
namespace readboard
{
    internal enum AutoPlayColorMode
    {
        ManualBlack = 0,
        ManualWhite = 1,
        FoxAuto = 2
    }
}
```

Add to `AppConfig`:

```csharp
public AutoPlayColorMode AutoPlayColorMode { get; set; }
public string FoxAutoPlayNickname { get; set; }
public string FoxAutoPlayRowSignature { get; set; }
```

In `CreateDefault(...)`, set:

```csharp
AutoPlayColorMode = AutoPlayColorMode.ManualBlack,
FoxAutoPlayNickname = string.Empty,
FoxAutoPlayRowSignature = string.Empty,
```

- [ ] **Step 4: Run default config test and verify it passes**

Run the same filtered command.

- [ ] **Step 5: Write failing load/save tests**

In `DualFormatAppConfigStoreTests.Save_WritesJsonAndLegacyMirrorWithUpdatedMetadata`, set:

```csharp
config.AutoPlayColorMode = AutoPlayColorMode.FoxAuto;
config.FoxAutoPlayNickname = "野狐高段9D";
config.FoxAutoPlayRowSignature = "sig-abc";
```

Assert JSON contains all three property names and update the legacy other mirror to append the new fields at the end:

```text
220430_9_9_-1_-1_200_1_50_-1_-1_1_0_1_7_1_2_野狐高段9D_sig-abc
```

Add a partial JSON load test for these fields.

- [ ] **Step 6: Run config store tests and verify failure**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~DualFormatAppConfigStoreTests" -c Debug
```

Expected: FAIL until load/save supports the fields.

- [ ] **Step 7: Implement config load/save**

In `ApplyJsonOverrides(...)`, add:

```csharp
config.AutoPlayColorMode = (AutoPlayColorMode)ReadIntValue(values, "AutoPlayColorMode", (int)config.AutoPlayColorMode);
config.FoxAutoPlayNickname = ReadStringValue(values, "FoxAutoPlayNickname", config.FoxAutoPlayNickname);
config.FoxAutoPlayRowSignature = ReadStringValue(values, "FoxAutoPlayRowSignature", config.FoxAutoPlayRowSignature);
```

In `ApplyLegacyOtherConfig(...)`, extend the layout comment with length 18 and read indexes 15-17 only when present.

In `WriteLegacyOtherConfig(...)`, append:

```csharp
builder.Append('_').Append((int)config.AutoPlayColorMode);
builder.Append('_').Append(EscapeLegacyToken(config.FoxAutoPlayNickname));
builder.Append('_').Append(EscapeLegacyToken(config.FoxAutoPlayRowSignature));
```

Use a small helper that replaces `_`, `\r`, and `\n` with spaces, matching the existing underscore-delimited legacy format.

- [ ] **Step 8: Run config tests and commit**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~AppConfigDefaultsTests|FullyQualifiedName~DualFormatAppConfigStoreTests" -c Debug
git add readboard\Core\AutoPlay\AutoPlayColorMode.cs readboard\Core\Models\AppConfig.cs readboard\Core\Configuration\DualFormatAppConfigStore.cs tests\Readboard.VerificationTests\Configuration\AppConfigDefaultsTests.cs tests\Readboard.VerificationTests\Configuration\DualFormatAppConfigStoreTests.cs
git commit -m "feat(readboard): 保存野狐自动落子身份配置"
```

Expected: tests PASS.

## Task 2: Add Pure Auto-Play Color Resolver

**Files:**
- Create: `readboard/Core/AutoPlay/AutoPlayColorStatus.cs`
- Create: `readboard/Core/AutoPlay/AutoPlayColorResolution.cs`
- Create: `readboard/Core/AutoPlay/FoxAutoPlayColorResolver.cs`
- Test: `tests/Readboard.VerificationTests/AutoPlay/FoxAutoPlayColorResolverTests.cs`

- [ ] **Step 1: Write failing resolver tests**

Cover:

- manual black returns `PlayColor = "black"`;
- manual white returns `PlayColor = "white"`;
- auto on non-Fox returns null and `UnsupportedPlatform`;
- auto with empty signature returns null and `Unconfigured`;
- auto with recognized black returns `"black"`;
- auto with recognized white returns `"white"`;
- auto with unknown returns null.

- [ ] **Step 2: Run resolver tests and verify failure**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~FoxAutoPlayColorResolverTests" -c Debug
```

Expected: FAIL because files do not exist.

- [ ] **Step 3: Implement resolver types**

Keep dependencies small:

```csharp
internal static class FoxAutoPlayColorResolver
{
    public static AutoPlayColorResolution Resolve(
        AutoPlayColorMode mode,
        SyncMode syncMode,
        string savedRowSignature,
        AutoPlayColorResolution detected)
    {
        if (mode == AutoPlayColorMode.ManualBlack)
            return AutoPlayColorResolution.Known("black", AutoPlayColorStatus.ManualBlack);
        if (mode == AutoPlayColorMode.ManualWhite)
            return AutoPlayColorResolution.Known("white", AutoPlayColorStatus.ManualWhite);
        if (!IsFoxMode(syncMode))
            return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.UnsupportedPlatform);
        if (string.IsNullOrWhiteSpace(savedRowSignature))
            return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.Unconfigured);
        return detected ?? AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown);
    }
}
```

The final implementation can adjust names, but keep the resolver pure and testable.

- [ ] **Step 4: Run resolver tests and commit**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~FoxAutoPlayColorResolverTests" -c Debug
git add readboard\Core\AutoPlay tests\Readboard.VerificationTests\AutoPlay
git commit -m "feat(readboard): 增加自动落子棋色解析器"
```

Expected: tests PASS.

## Task 3: Detect Fox Player Row Signature And Stone Color

**Files:**
- Create: `readboard/Core/AutoPlay/FoxPlayerRowCandidate.cs`
- Create: `readboard/Core/AutoPlay/FoxPlayerRowSignature.cs`
- Create: `readboard/Core/AutoPlay/FoxPlayerStoneIconDetector.cs`
- Test: `tests/Readboard.VerificationTests/AutoPlay/FoxPlayerRowSignatureTests.cs`
- Test: `tests/Readboard.VerificationTests/AutoPlay/FoxPlayerStoneIconDetectorTests.cs`

- [ ] **Step 1: Write failing stone icon detector tests**

Use synthetic bitmaps:

- black circle on light background returns black;
- white circle on dark/colored background returns white;
- flat background returns unknown;
- mixed ambiguous sample returns unknown.

- [ ] **Step 2: Write failing row signature tests**

Use synthetic row snippets:

- identical snippets match;
- same text-like pixels with small brightness variation match;
- different text-like pixels do not match;
- blank rows return empty/invalid signature.

- [ ] **Step 3: Run auto-play image tests and verify failure**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~FoxPlayerStoneIconDetectorTests|FullyQualifiedName~FoxPlayerRowSignatureTests" -c Debug
```

Expected: FAIL until implementation exists.

- [ ] **Step 4: Implement stone icon detector**

Implement a pure method such as:

```csharp
public static AutoPlayColorResolution Detect(Bitmap iconBitmap)
```

Rules:

- count dark pixels where RGB channels are all below a conservative threshold;
- count light pixels where RGB channels are all above a conservative threshold;
- require a minimum colored-pixel ratio so panel background is not classified as white;
- return unknown when both ratios pass or neither passes.

- [ ] **Step 5: Implement row signature**

Use a deterministic compact signature:

- crop only the nickname area, not the stone icon;
- normalize to a small grayscale grid, such as 48 by 12;
- threshold relative to the row's average brightness;
- serialize as a short hex or base64 string.

Do not use OCR. Do not store full screenshots in config.

- [ ] **Step 6: Run image tests and commit**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~FoxPlayerStoneIconDetectorTests|FullyQualifiedName~FoxPlayerRowSignatureTests" -c Debug
git add readboard\Core\AutoPlay tests\Readboard.VerificationTests\AutoPlay
git commit -m "feat(readboard): 识别野狐玩家行棋色"
```

Expected: tests PASS.

## Task 4: Add MainForm Three-Way Color Mode

**Files:**
- Modify: `readboard/Form1.Designer.cs`
- Modify: `readboard/Form1.cs`
- Modify: `readboard/MainForm.Configuration.cs`
- Modify: `readboard/Program.cs`
- Modify: `readboard/language_cn.txt`
- Modify: `readboard/language_en.txt`
- Modify: `readboard/language_jp.txt`
- Modify: `readboard/language_kr.txt`
- Test: `tests/Readboard.VerificationTests/Host/StartupAndShutdownRegressionTests.cs`
- Test: `tests/Readboard.VerificationTests/Host/HighDpiSourceRegressionTests.cs`

- [ ] **Step 1: Write failing source regression tests**

Add assertions that:

- `Form1.Designer.cs` declares `radioAutoPlayColor`;
- `flowLayoutPanel2` includes `radioAutoPlayColor` after `radioWhite`;
- `CaptureSnapshotCore()` uses a resolver method rather than direct `GetSelectedPlayColor()` with only two branches;
- `MainForm.Configuration.cs` loads and persists `AutoPlayColorMode`;
- language keys exist in `Program.cs` and all four language files.

- [ ] **Step 2: Run source regression tests and verify failure**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~StartupAndShutdownRegressionTests|FullyQualifiedName~HighDpiSourceRegressionTests" -c Debug
```

Expected: FAIL on missing controls/keys.

- [ ] **Step 3: Add UI controls and language keys**

Add:

- `radioAutoPlayColor` with text `自动`;
- `lblAutoPlayColorStatus` as a compact status label near auto-play controls.

Update layout code in both `ArrangeLegacyMainSyncSection(...)` and `ArrangeAdaptiveMainSyncSection(...)`:

- preserve stable row heights;
- include the new radio and status label in width calculations;
- keep text fitting at high DPI.

- [ ] **Step 4: Add mode getters/setters**

In `Form1.cs`, replace the two-radio-only logic with:

```csharp
private AutoPlayColorMode GetSelectedAutoPlayColorMode()
private void ApplyAutoPlayColorMode(AutoPlayColorMode mode)
private AutoPlayColorResolution ResolveCurrentAutoPlayColor()
private void UpdateAutoPlayColorStatus(AutoPlayColorResolution resolution)
```

Keep `SendPlayCommandIfSelected()` and `CaptureSnapshotCore()` using the resolved `PlayColor`.

- [ ] **Step 5: Run source regression tests**

Run the same filtered command.

Expected: tests PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add readboard\Form1.Designer.cs readboard\Form1.cs readboard\MainForm.Configuration.cs readboard\Program.cs readboard\language_cn.txt readboard\language_en.txt readboard\language_jp.txt readboard\language_kr.txt tests\Readboard.VerificationTests\Host\StartupAndShutdownRegressionTests.cs tests\Readboard.VerificationTests\Host\HighDpiSourceRegressionTests.cs
git commit -m "feat(readboard): 增加自动落子棋色模式"
```

## Task 5: Add First-Run Fox Identity Dialog And Settings Entry

**Files:**
- Create: `readboard/FoxAutoPlayIdentityDialog.cs`
- Create: `readboard/FoxAutoPlayIdentityDialog.Designer.cs`
- Modify: `readboard/Form1.cs`
- Modify: `readboard/Form4.Designer.cs`
- Modify: `readboard/Form4.cs`
- Modify: `readboard/Program.cs`
- Modify: `readboard/language_cn.txt`
- Modify: `readboard/language_en.txt`
- Modify: `readboard/language_jp.txt`
- Modify: `readboard/language_kr.txt`
- Test: `tests/Readboard.VerificationTests/Host/StartupAndShutdownRegressionTests.cs`
- Test: `tests/Readboard.VerificationTests/Host/HighDpiSourceRegressionTests.cs`

- [ ] **Step 1: Write failing source tests for dialog/settings**

Assert:

- dialog files exist and expose selected nickname/signature properties;
- `Form1.cs` opens the dialog only when auto mode is selected and no saved signature exists;
- `Form4.cs` loads and clears `FoxAutoPlayNickname` and `FoxAutoPlayRowSignature`;
- settings layout includes a nickname text box and clear/reselect button;
- all language files contain the new keys.

- [ ] **Step 2: Run source tests and verify failure**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~StartupAndShutdownRegressionTests|FullyQualifiedName~HighDpiSourceRegressionTests" -c Debug
```

Expected: FAIL.

- [ ] **Step 3: Implement dialog skeleton**

The first version should not need live OCR:

- show two row preview boxes when row snippets are available;
- allow `上方玩家是我` / `下方玩家是我`;
- allow manual nickname entry;
- include `记住这个昵称，以后自动使用`;
- if no row preview is available, still allow nickname entry but do not create a signature.

- [ ] **Step 4: Wire first-run behavior**

In `radioAutoPlayColor_CheckedChanged(...)`:

- if initialization is in progress, return after setting state;
- if auto is checked and current sync type is not Fox, update status and do not prompt;
- if auto is checked and saved signature is empty, open the dialog;
- on confirm, update `Program.CurrentConfig`, persist config, clear cached detection state, and resend play state if keep sync is active;
- on cancel, switch back to previous manual mode and do not send `play`.

- [ ] **Step 5: Add settings controls**

In `SettingsForm`:

- show current nickname in `txtFoxAutoPlayNickname`;
- add `btnClearFoxAutoPlayIdentity`;
- clearing empties both nickname and signature;
- saving writes nickname changes and clears signature if nickname text changed manually.

- [ ] **Step 6: Run source tests and commit**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~StartupAndShutdownRegressionTests|FullyQualifiedName~HighDpiSourceRegressionTests" -c Debug
git add readboard\FoxAutoPlayIdentityDialog.cs readboard\FoxAutoPlayIdentityDialog.Designer.cs readboard\Form1.cs readboard\Form4.cs readboard\Form4.Designer.cs readboard\Program.cs readboard\language_cn.txt readboard\language_en.txt readboard\language_jp.txt readboard\language_kr.txt tests\Readboard.VerificationTests\Host\StartupAndShutdownRegressionTests.cs tests\Readboard.VerificationTests\Host\HighDpiSourceRegressionTests.cs
git commit -m "feat(readboard): 添加野狐自动模式身份设置"
```

Expected: tests PASS.

## Task 6: Integrate Fox Row Detection With Snapshot PlayColor

**Files:**
- Modify: `readboard/Form1.cs`
- Modify: `readboard/Core/AutoPlay/FoxPlayerRowCandidate.cs`
- Create: `readboard/Core/AutoPlay/FoxPlayerRowLocator.cs`
- Test: `tests/Readboard.VerificationTests/AutoPlay/FoxPlayerRowLocatorTests.cs`
- Test: `tests/Readboard.VerificationTests/AutoPlay/FoxAutoPlayColorResolverTests.cs`

- [ ] **Step 1: Write failing row locator tests**

Use synthetic scaled Fox-like right-panel bitmaps. Cover:

- two candidate rows are found;
- row icon rectangles are inside each row;
- unrelated blank bitmap returns no candidates;
- scaled bitmap keeps candidate proportions.

- [ ] **Step 2: Run row locator tests and verify failure**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~FoxPlayerRowLocatorTests" -c Debug
```

Expected: FAIL.

- [ ] **Step 3: Implement conservative row locator**

Use the full Fox window bitmap captured through the existing selected window path. The locator should:

- operate only on Fox modes;
- scan the right-side list panel region, not the board;
- return exactly two row candidates only when both rows are plausible;
- return no candidates when layout is ambiguous.

Do not read titles or enumerate global windows here.

- [ ] **Step 4: Integrate cached detection in MainForm**

Add a small `MainForm` cache:

- last detected `AutoPlayColorResolution`;
- last detection window handle;
- last detection context signature;
- last detection timestamp.

Invalidate on:

- sync mode change;
- Fox context change;
- saved nickname/signature change;
- switching away from auto;
- selecting a different window.

Use the cache so `CaptureSnapshotCore()` does not perform heavy work on every sample.

- [ ] **Step 5: Run resolver and row locator tests**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~FoxPlayerRowLocatorTests|FullyQualifiedName~FoxAutoPlayColorResolverTests" -c Debug
```

Expected: tests PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add readboard\Core\AutoPlay readboard\Form1.cs tests\Readboard.VerificationTests\AutoPlay
git commit -m "feat(readboard): 接入野狐自动棋色识别"
```

## Task 7: Final Verification And Manual Checks

**Files:**
- Modify if needed: `docs/specs/2026-05-20-fox-auto-play-color-detection-design.md`
- Modify if needed: `docs/plans/2026-05-20-fox-auto-play-color-detection.md`

- [ ] **Step 1: Run focused test suite**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --filter "FullyQualifiedName~AutoPlay|FullyQualifiedName~DualFormatAppConfigStoreTests|FullyQualifiedName~StartupAndShutdownRegressionTests|FullyQualifiedName~HighDpiSourceRegressionTests" -c Debug
```

Expected: PASS.

- [ ] **Step 2: Run full verification suite**

Run:

```powershell
C:\Users\admin\.dotnet\dotnet.exe test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj -c Debug
```

Expected: PASS.

- [ ] **Step 3: Manual UI checks**

Run the host-style debug launcher:

```powershell
pwsh.exe -NoProfile -File scripts\run-readboard-ui-debug.ps1 -Configuration Debug
```

Check:

- `执黑 / 执白 / 自动` fit in both default and optimized themes.
- Auto status text does not overlap at high DPI.
- Settings form keeps scroll and buttons visible.
- Selecting `自动` with no saved identity opens the dialog.
- Canceling the dialog does not send `play`.
- Manual `执黑` and `执白` still send the old protocol.

- [ ] **Step 4: Real Fox checks**

With a real Fox window:

- select `野狐`, choose `自动`, save identity;
- verify empty board at move 0 does not guess from stone count;
- verify recognized black sends `play black ...`;
- verify recognized white sends `play white ...`;
- switch rooms and verify old detection does not carry over;
- close or change window and verify status becomes unknown instead of reusing stale data.

- [ ] **Step 5: Commit any final doc or test adjustments**

Run:

```powershell
git status --short
git add <only-intended-files>
git commit -m "test(readboard): 覆盖野狐自动棋色识别"
```

Only commit if there are actual final changes.

## Final Acceptance Criteria

- Manual `执黑` and `执白` behavior is unchanged.
- `自动` mode never sends `play` outside Fox modes.
- `自动` mode never sends `play` when identity or color is unknown.
- Saved nickname/signature round-trips through config.
- The first-run dialog can be canceled without side effects.
- Settings can clear saved identity.
- No new wire protocol lines are added.
- Focused and full verification tests pass.
