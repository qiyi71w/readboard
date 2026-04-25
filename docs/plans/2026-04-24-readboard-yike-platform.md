# 弈客平台支持 Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 readboard 增加新的同步平台 `弈客`（`SyncMode.Yike`），支持自动棋盘识别、协议接收 host 端解析好的房间号与手数、后台落子，并保证下游 lizzieyzy-next 能识别同盘 / 换房 / 手数变化。

**Architecture:** 全程 TDD。沿用现有"按平台特征色锚点扫描"棋盘检测骨架，沿用 FoxBackgroundPlace 的后台落子路径；新增 `YikeWindowContext`、`yike` 入站协议消息、`yikeRoomToken` / `yikeMoveNumber` 出站字段，与野狐字段平行不复用。元数据进入 `SendBoardSnapshot` 判重，主窗体标题按签名节流刷新。host 侧（lizzieyzy-next）改动单独跟进，不在本 plan 范围。

**Tech Stack:** C# / .NET 10 (`net10-windows`) / WinForms / xUnit / 现有 LegacyBoardLocator 颜色扫描骨架 / 现有协议常量 ProtocolKeywords。

**前置依赖（实现期需要的，不是本 plan 可生成的）：**
- 用户提供至少 1 张弈客窗口完整客户区截图 PNG（19 路普通对弈、19 路直播、19 路比赛各一张为佳），保存到 `fixtures/yike/` 下作为 fixture 用于 `TryResolveYikeBounds` 调参与回归测试。在 Task 4 之前必须就位。

**Spec:** `docs/specs/2026-04-24-readboard-yike-platform-design.md`

---

## File Map

新建：
- `readboard/Core/Protocol/YikeWindowContext.cs` —— 弈客上下文数据模型
- `tests/Readboard.VerificationTests/Protocol/YikeWindowContextTests.cs`
- `tests/Readboard.VerificationTests/Protocol/YikeProtocolInboundParsingTests.cs`
- `tests/Readboard.VerificationTests/Protocol/YikeOutboundProtocolContractTests.cs`
- `tests/Readboard.VerificationTests/Recognition/YikeBoardLocatorTests.cs`
- `tests/Readboard.VerificationTests/Host/YikeMainWindowTitleTests.cs`
- `fixtures/yike/<样张>.png`（用户提供）

修改：
- `readboard/Core/Models/SyncMode.cs` —— 加 `Yike = 6`
- `readboard/Core/Protocol/ProtocolKeywords.cs` —— 加 `Yike` / `YikeRoomTokenPrefix` / `YikeMoveNumberPrefix`
- `readboard/Core/Recognition/BoardRecognitionResult.cs` —— `UsesAutoDetectedBounds` 加 `Yike`，加 `TryResolveYikeBounds` 与 `CreateYikeLeftPatterns` / `CreateYikeRightPatterns`
- `readboard/Core/Protocol/LegacySyncWindowLocator.cs` —— 加 `FindYikeWindow`（按标题前缀匹配 `弈客大厅` / `弈客直播`）
- `readboard/Core/Protocol/LegacyProtocolAdapter.cs` —— 入站识别 `yike` 行，出站新增 `CreateYikeRoomTokenMessage` / `CreateYikeMoveNumberMessage`
- `readboard/Core/Protocol/IReadBoardProtocolAdapter.cs` —— 同步增加方法签名
- `readboard/Core/Protocol/SyncCoordinatorHostSnapshot.cs` —— 加 `YikeContext` 字段
- `readboard/Core/Protocol/SyncSessionRuntimeState.cs` —— 加 `lastSentYikeContextSignature` / `lastCapturedYikeContext`
- `readboard/Core/Protocol/SyncSessionCoordinator.cs` —— `SendBoardSnapshot` 判重纳入弈客签名；新增 `SetYikeContext` 入口；`Reset` 清理弈客字段
- `readboard/Core/Protocol/SyncSessionCoordinator.Orchestration.cs` —— 后台落子分支接受 `SyncMode.Yike`
- `readboard/Core/Protocol/BackgroundSelectionWindowBindingCoordinator.cs` —— 接受 `SyncMode.Yike`
- `readboard/Core/Models/ProtocolMessage.cs` —— 如需新增 `ProtocolMessageKind.YikeContext`
- `readboard/MainWindowTitleFormatter.cs` —— 加弈客分支
- `readboard/MainForm.Configuration.cs` / `Form1.cs` —— 平台单选按钮加 `rdoYike`，绑定到 `SyncMode.Yike`
- `readboard/Form1.Designer.cs` —— 加 `rdoYike` 控件
- `readboard/language_cn.txt` / `language_en.txt` / `language_jp.txt` / `language_kr.txt` —— 新增 `MainForm_rdoYike` 与 `MainForm_titleTagYike`

---

## Tasks

### Task 1: 添加 `SyncMode.Yike` 枚举值

**Files:**
- Modify: `readboard/Core/Models/SyncMode.cs`
- Test: `tests/Readboard.VerificationTests/Recognition/SyncModeViewportAcceptanceTests.cs`（已存在，确认覆盖 Yike）

- [ ] **Step 1: 写失败的测试**

在 `SyncModeViewportAcceptanceTests.cs` 增加：

```csharp
[Fact]
public void Yike_uses_auto_detected_bounds()
{
    Assert.True(LegacyBoardLocator_TestAccess.UsesAutoDetectedBounds(SyncMode.Yike));
}
```

如果 `LegacyBoardLocator_TestAccess` 不存在，改为通过 `InternalsVisibleTo`（已配置）直接调用 `LegacyBoardLocator` 内部方法。如该方法当前为 `private`，本 task 仅断言枚举值存在并先用 `Assert.Equal((SyncMode)6, SyncMode.Yike)`，等 Task 4 再断言 `UsesAutoDetectedBounds`。

- [ ] **Step 2: 跑测试确认 FAIL**

`dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~Yike_uses_auto_detected_bounds"`
预期：编译失败，`SyncMode.Yike` 不存在。

- [ ] **Step 3: 加枚举值**

```csharp
internal enum SyncMode
{
    Fox = 0,
    Tygem = 1,
    Sina = 2,
    Background = 3,
    FoxBackgroundPlace = 4,
    Foreground = 5,
    Yike = 6
}
```

- [ ] **Step 4: 跑测试**

期望：第一个断言（枚举值存在）通过；如果第二条已经写就先 skip 或留 fail（Task 4 解决）。

- [ ] **Step 5: 提交**

```bash
git add readboard/Core/Models/SyncMode.cs tests/Readboard.VerificationTests/Recognition/SyncModeViewportAcceptanceTests.cs
git commit -m "feat(yike): 增加 SyncMode.Yike 枚举值"
```

---

### Task 2: 协议关键字

**Files:**
- Modify: `readboard/Core/Protocol/ProtocolKeywords.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/YikeProtocolInboundParsingTests.cs`（新建）

- [ ] **Step 1: 写失败的测试**

```csharp
public class YikeProtocolKeywordsTests
{
    [Fact]
    public void exposes_yike_inbound_prefix()
    {
        Assert.Equal("yike", ProtocolKeywords.Yike);
    }

    [Fact]
    public void exposes_outbound_prefixes_parallel_to_fox()
    {
        Assert.Equal("yikeRoomToken ", ProtocolKeywords.YikeRoomTokenPrefix);
        Assert.Equal("yikeMoveNumber ", ProtocolKeywords.YikeMoveNumberPrefix);
    }
}
```

文件先放到 `YikeProtocolInboundParsingTests.cs`，后续 task 在同文件补入站解析用例。

- [ ] **Step 2: 跑测试确认 FAIL**

预期：编译失败，`ProtocolKeywords.Yike` / `YikeRoomTokenPrefix` / `YikeMoveNumberPrefix` 不存在。

- [ ] **Step 3: 加常量**

在 `ProtocolKeywords.cs` 末尾增加：

```csharp
internal const string Yike = "yike";
internal const string YikeRoomTokenPrefix = "yikeRoomToken ";
internal const string YikeMoveNumberPrefix = "yikeMoveNumber ";
```

- [ ] **Step 4: 跑测试**

期望全部 PASS。

- [ ] **Step 5: 提交**

```bash
git commit -am "feat(yike): 增加弈客协议关键字常量"
```

---

### Task 3: `YikeWindowContext` 数据模型

**Files:**
- Create: `readboard/Core/Protocol/YikeWindowContext.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/YikeWindowContextTests.cs`

- [ ] **Step 1: 写失败的测试**

```csharp
public class YikeWindowContextTests
{
    [Fact]
    public void unknown_has_null_room_and_move()
    {
        var ctx = YikeWindowContext.Unknown();
        Assert.Null(ctx.RoomToken);
        Assert.Null(ctx.MoveNumber);
    }

    [Fact]
    public void copy_of_null_returns_unknown()
    {
        var ctx = YikeWindowContext.CopyOf(null);
        Assert.NotNull(ctx);
        Assert.Null(ctx.RoomToken);
        Assert.Null(ctx.MoveNumber);
    }

    [Fact]
    public void signature_changes_when_room_or_move_changes()
    {
        var a = new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 };
        var b = new YikeWindowContext { RoomToken = "65191829", MoveNumber = 17 };
        var c = new YikeWindowContext { RoomToken = "65191830", MoveNumber = 16 };
        var aDup = new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 };
        Assert.NotEqual(a.ContextSignature, b.ContextSignature);
        Assert.NotEqual(a.ContextSignature, c.ContextSignature);
        Assert.Equal(a.ContextSignature, aDup.ContextSignature);
    }

    [Fact]
    public void unknown_signature_is_stable()
    {
        Assert.Equal(YikeWindowContext.Unknown().ContextSignature, YikeWindowContext.Unknown().ContextSignature);
    }
}
```

- [ ] **Step 2: 跑测试 FAIL**

预期：`YikeWindowContext` 不存在。

- [ ] **Step 3: 实现**

```csharp
namespace readboard
{
    internal sealed class YikeWindowContext
    {
        public string RoomToken { get; set; }
        public int? MoveNumber { get; set; }

        public string ContextSignature
        {
            get
            {
                string room = string.IsNullOrWhiteSpace(RoomToken) ? "_" : RoomToken.Trim();
                string move = MoveNumber.HasValue ? MoveNumber.Value.ToString() : "_";
                return "room=" + room + ";move=" + move;
            }
        }

        public static YikeWindowContext Unknown()
        {
            return new YikeWindowContext();
        }

        public static YikeWindowContext CopyOf(YikeWindowContext ctx)
        {
            if (ctx == null) return Unknown();
            return new YikeWindowContext { RoomToken = ctx.RoomToken, MoveNumber = ctx.MoveNumber };
        }
    }
}
```

- [ ] **Step 4: 跑测试 PASS**

- [ ] **Step 5: 提交**

```bash
git add readboard/Core/Protocol/YikeWindowContext.cs tests/Readboard.VerificationTests/Protocol/YikeWindowContextTests.cs
git commit -m "feat(yike): 增加 YikeWindowContext 数据模型"
```

---

### Task 4: 棋盘自动识别 `TryResolveYikeBounds`

**前置：** 用户已经把样张放到 `fixtures/yike/` 下。

**Files:**
- Modify: `readboard/Core/Recognition/BoardRecognitionResult.cs`
- Create: `tests/Readboard.VerificationTests/Recognition/YikeBoardLocatorTests.cs`

- [ ] **Step 1: 写失败的测试**

```csharp
public class YikeBoardLocatorTests
{
    [Fact]
    public void Yike_in_auto_detected_bounds_whitelist()
    {
        // 通过 InternalsVisibleTo 直接调用
        Assert.True(InvokeUsesAutoDetectedBounds(SyncMode.Yike));
    }

    [Fact]
    public void resolves_yike_bounds_from_fixture()
    {
        string path = VerificationFixtureLocator.Resolve("yike/sample-19road-room.png");
        using (Bitmap bmp = new Bitmap(path))
        {
            BoardFrame frame = BoardFrameTestFactory.FromBitmap(bmp, SyncMode.Yike);
            Assert.True(LegacyPixelMap.TryCreate(frame, out var pixels, out _, out _));
            Assert.True(LegacyBoardLocator_TestAccess.TryResolveYikeBounds(pixels, out var bounds));
            // 棋盘应该是近方形，且不全屏
            double ratio = (double)bounds.Width / bounds.Height;
            Assert.InRange(ratio, 0.95, 1.05);
            Assert.True(bounds.Width < pixels.Width);
        }
    }
}
```

如果现有 test 项目没有 `LegacyBoardLocator_TestAccess` / `BoardFrameTestFactory`，沿用与 Fox 测试相同的访问方式（参考 `FoxWindowBindingPipelineTests` 怎么调内部 API）。

- [ ] **Step 2: 跑测试 FAIL**

预期：`TryResolveYikeBounds` 不存在；或第一条断言失败。

- [ ] **Step 3: 实现**

在 `BoardRecognitionResult.cs` 中：

3a) 修改 `UsesAutoDetectedBounds`：

```csharp
private static bool UsesAutoDetectedBounds(SyncMode syncMode)
{
    return syncMode == SyncMode.Fox
        || syncMode == SyncMode.FoxBackgroundPlace
        || syncMode == SyncMode.Tygem
        || syncMode == SyncMode.Sina
        || syncMode == SyncMode.Yike;
}
```

3b) 在 `TryResolveBounds` 的 switch 中新增分支：

```csharp
case SyncMode.Yike:
    return TryResolveYikeBounds(pixels, out sourceBounds);
```

3c) 增加 `TryResolveYikeBounds` 与配套色锚 patterns。骨架对齐 `TryResolveFoxBounds`，特征色根据用户提供的样张取色调参（通常弈客棋盘外缘是浅木色 + 灰边线交界）。例：

```csharp
private static bool TryResolveYikeBounds(LegacyPixelMap pixels, out PixelRect sourceBounds)
{
    sourceBounds = null;
    if (!TryFindPattern(pixels, CreateYikeLeftPatterns(), SearchDirection.LeftTop, out var upLeft))
        return false;
    if (!TryFindPattern(pixels, CreateYikeRightPatterns(), SearchDirection.RightTop, out var upRight))
        return false;
    int size = upRight.X - upLeft.X;
    if (size <= 0) return false;
    sourceBounds = ClipBounds(new PixelRect(upLeft.X, upLeft.Y, size, size), pixels.Width, pixels.Height);
    return sourceBounds != null;
}

private static ColorPattern[] CreateYikeLeftPatterns() { /* 调参后填入 */ }
private static ColorPattern[] CreateYikeRightPatterns() { /* 调参后填入 */ }
```

具体 RGB 由实现期间用 `pixels.GetPixel(x,y)` 在样张棋盘左上 / 右上像素位置取色后填入。如果单一颜色锚点不稳，允许参考 Sina 的多点连续 pattern 方式。

- [ ] **Step 4: 跑测试 PASS**

- [ ] **Step 5: 提交**

```bash
git commit -am "feat(yike): 自动识别弈客棋盘矩形"
```

---

### Task 5: 窗口定位 `FindYikeWindow`

**Files:**
- Modify: `readboard/Core/Protocol/LegacySyncWindowLocator.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/LegacySyncWindowLocatorTests.cs`

- [ ] **Step 1: 写失败的测试**

新增（如已用 fixture 模式见已有 Fox 测试）：

```csharp
[Fact]
public void Yike_lobby_title_matches() {
    // 标题前缀策略可不依赖真实窗口，独立提取一个 IsYikeTitleCandidate 静态方法测
    Assert.True(LegacySyncWindowLocator.IsYikeTitleCandidate("弈客大厅"));
    Assert.True(LegacySyncWindowLocator.IsYikeTitleCandidate("弈客直播"));
    Assert.True(LegacySyncWindowLocator.IsYikeTitleCandidate("弈客直播 - 19路"));
    Assert.False(LegacySyncWindowLocator.IsYikeTitleCandidate("野狐"));
    Assert.False(LegacySyncWindowLocator.IsYikeTitleCandidate(""));
}
```

- [ ] **Step 2: 跑测试 FAIL**

- [ ] **Step 3: 实现**

```csharp
public IntPtr FindWindowHandle(SyncMode syncMode)
{
    if (syncMode == SyncMode.Tygem) return FindTygemWindow();
    if (syncMode == SyncMode.Sina) return FindSinaWindow();
    if (syncMode == SyncMode.Fox || syncMode == SyncMode.FoxBackgroundPlace) return FindFoxWindow();
    if (syncMode == SyncMode.Yike) return FindYikeWindow();
    return IntPtr.Zero;
}

internal static bool IsYikeTitleCandidate(string title)
{
    if (string.IsNullOrEmpty(title)) return false;
    return title.StartsWith("弈客大厅", StringComparison.Ordinal)
        || title.StartsWith("弈客直播", StringComparison.Ordinal);
}

private static IntPtr FindYikeWindow()
{
    Dictionary<IntPtr, string> roots = GetOpenWindows();
    foreach (var kv in roots)
    {
        if (IsYikeTitleCandidate(GetWindowTitle(kv.Key)))
            return kv.Key;
    }
    return IntPtr.Zero;
}
```

注意：`FindYikeWindow` 不限制进程名（lizzieyzy-next 是 Java 进程，进程名因打包方式可能是 `java`、`javaw`、`lizzie` 等），仅靠标题前缀匹配；这与野狐 / 弈城 / 新浪基于进程名 + 类名的策略不同，但与"窗口标题前缀稳定"的事实相符。

- [ ] **Step 4: 跑测试 PASS**

- [ ] **Step 5: 提交**

```bash
git commit -am "feat(yike): 按标题前缀定位弈客窗口句柄"
```

---

### Task 6: 入站协议解析 `yike room=... move=...`

**Files:**
- Modify: `readboard/Core/Protocol/LegacyProtocolAdapter.cs`
- Modify: `readboard/Core/Models/ProtocolMessage.cs`（新增 `ProtocolMessageKind.YikeContext` + 字段）
- Test: `tests/Readboard.VerificationTests/Protocol/YikeProtocolInboundParsingTests.cs`

- [ ] **Step 1: 写失败的测试**

```csharp
[Fact]
public void parses_full_yike_line()
{
    var msg = new LegacyProtocolAdapter().ParseInbound("yike room=65191829 move=16");
    Assert.Equal(ProtocolMessageKind.YikeContext, msg.Kind);
    Assert.Equal("65191829", msg.YikeRoomToken);
    Assert.Equal(16, msg.YikeMoveNumber);
}

[Fact]
public void parses_yike_without_room()
{
    var msg = new LegacyProtocolAdapter().ParseInbound("yike move=42");
    Assert.Equal(ProtocolMessageKind.YikeContext, msg.Kind);
    Assert.Null(msg.YikeRoomToken);
    Assert.Equal(42, msg.YikeMoveNumber);
}

[Fact]
public void parses_yike_without_move()
{
    var msg = new LegacyProtocolAdapter().ParseInbound("yike room=65191829");
    Assert.Equal(ProtocolMessageKind.YikeContext, msg.Kind);
    Assert.Equal("65191829", msg.YikeRoomToken);
    Assert.Null(msg.YikeMoveNumber);
}

[Fact]
public void parses_bare_yike_as_unknown()
{
    var msg = new LegacyProtocolAdapter().ParseInbound("yike");
    Assert.Equal(ProtocolMessageKind.YikeContext, msg.Kind);
    Assert.Null(msg.YikeRoomToken);
    Assert.Null(msg.YikeMoveNumber);
}

[Fact]
public void ignores_garbage_in_yike_payload()
{
    var msg = new LegacyProtocolAdapter().ParseInbound("yike room=65191829 move=abc");
    Assert.Equal(ProtocolMessageKind.YikeContext, msg.Kind);
    Assert.Equal("65191829", msg.YikeRoomToken);
    Assert.Null(msg.YikeMoveNumber);
}
```

- [ ] **Step 2: 跑测试 FAIL**

- [ ] **Step 3: 实现**

在 `ProtocolMessage.cs` 中：

```csharp
internal enum ProtocolMessageKind { /* 现有... */, YikeContext }
internal sealed class ProtocolMessage
{
    // 现有字段...
    public string YikeRoomToken { get; set; }
    public int? YikeMoveNumber { get; set; }
}
```

在 `LegacyProtocolAdapter.ParseInbound` 中，在 `Quit` 分支之后插入：

```csharp
if (trimmed == ProtocolKeywords.Yike || trimmed.StartsWith(ProtocolKeywords.Yike + " ", StringComparison.Ordinal))
    return ParseYikeContext(trimmed);
```

新增私有方法 `ParseYikeContext(string trimmed)`：按空格切分 token，识别 `room=<value>` 与 `move=<value>`，无效或缺省则保持 null。返回 `new ProtocolMessage { Kind = ProtocolMessageKind.YikeContext, RawText = trimmed, YikeRoomToken = ..., YikeMoveNumber = ... }`。

- [ ] **Step 4: 跑测试 PASS**

- [ ] **Step 5: 提交**

```bash
git commit -am "feat(yike): 解析 yike 入站协议消息"
```

---

### Task 7: 出站协议序列化 `yikeRoomToken` / `yikeMoveNumber`

**Files:**
- Modify: `readboard/Core/Protocol/IReadBoardProtocolAdapter.cs`
- Modify: `readboard/Core/Protocol/LegacyProtocolAdapter.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/YikeOutboundProtocolContractTests.cs`

- [ ] **Step 1: 写失败的测试**

```csharp
public class YikeOutboundProtocolContractTests
{
    [Fact]
    public void room_token_message_format()
    {
        var msg = new LegacyProtocolAdapter().CreateYikeRoomTokenMessage("65191829");
        Assert.Equal("yikeRoomToken 65191829", msg.RawText);
    }

    [Fact]
    public void move_number_message_format()
    {
        var msg = new LegacyProtocolAdapter().CreateYikeMoveNumberMessage(16);
        Assert.Equal("yikeMoveNumber 16", msg.RawText);
    }
}
```

- [ ] **Step 2: 跑测试 FAIL**

- [ ] **Step 3: 实现**

`IReadBoardProtocolAdapter` 加：

```csharp
ProtocolMessage CreateYikeRoomTokenMessage(string roomToken);
ProtocolMessage CreateYikeMoveNumberMessage(int moveNumber);
```

`LegacyProtocolAdapter` 实现：

```csharp
public ProtocolMessage CreateYikeRoomTokenMessage(string roomToken)
    => CreateLegacyMessage(ProtocolKeywords.YikeRoomTokenPrefix + (roomToken ?? string.Empty));

public ProtocolMessage CreateYikeMoveNumberMessage(int moveNumber)
    => CreateLegacyMessage(ProtocolKeywords.YikeMoveNumberPrefix + moveNumber);
```

- [ ] **Step 4: 跑测试 PASS**

- [ ] **Step 5: 提交**

```bash
git commit -am "feat(yike): 增加弈客出站协议消息构造"
```

---

### Task 8: `SyncCoordinatorHostSnapshot` 与 runtime state

**Files:**
- Modify: `readboard/Core/Protocol/SyncCoordinatorHostSnapshot.cs`
- Modify: `readboard/Core/Protocol/SyncSessionRuntimeState.cs`

- [ ] **Step 1: 写失败的测试**

在已有 `SyncSessionCoordinatorTests` 体系中加：

```csharp
[Fact]
public void host_snapshot_carries_yike_context()
{
    var snap = new SyncCoordinatorHostSnapshot();
    snap.YikeContext = new YikeWindowContext { RoomToken = "x", MoveNumber = 1 };
    Assert.Equal("x", snap.YikeContext.RoomToken);
}
```

- [ ] **Step 2: 跑测试 FAIL**

- [ ] **Step 3: 实现**

`SyncCoordinatorHostSnapshot.cs`：增加 `public YikeWindowContext YikeContext { get; set; }`
`SyncSessionRuntimeState.cs`：增加 `public string LastSentYikeContextSignature { get; set; }` 与 `public YikeWindowContext LastCapturedYikeContext { get; set; }`（按现有命名风格）

- [ ] **Step 4: 跑测试 PASS**

- [ ] **Step 5: 提交**

```bash
git commit -am "feat(yike): 在 host snapshot 与 runtime state 增加弈客上下文字段"
```

---

### Task 9: `SendBoardSnapshot` 判重纳入弈客签名 + 发送弈客字段

**Files:**
- Modify: `readboard/Core/Protocol/SyncSessionCoordinator.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/SyncSessionCoordinatorTests.cs`（增量）

- [ ] **Step 1: 写失败的测试**

至少三条用例：

```csharp
[Fact]
public void resends_when_yike_room_changes_even_if_payload_same() {
    // 初始：发送一帧含 room=A
    // 再发送同 payload，但 room=B → 必须再发一帧
}

[Fact]
public void resends_when_yike_move_changes_even_if_payload_same() { /* 同上但只动 move */ }

[Fact]
public void emits_yike_room_token_and_move_number_lines_when_present() {
    // 断言出站协议行包含 "yikeRoomToken 65191829" 与 "yikeMoveNumber 16"
}

[Fact]
public void omits_yike_lines_in_non_yike_mode() { /* SyncMode.Fox 下不含 yikeRoomToken / yikeMoveNumber */ }
```

参考 `LegacyOutboundProtocolContractTests` 怎么捕获出站行。

- [ ] **Step 2: 跑测试 FAIL**

- [ ] **Step 3: 实现**

3a) `SetYikeContext(YikeWindowContext context)` 入口（外部调用方在 Task 11 接入）：

```csharp
public void SetYikeContext(YikeWindowContext context)
{
    lock (stateLock) { state.LastCapturedYikeContext = YikeWindowContext.CopyOf(context); }
}
```

3b) `SendBoardSnapshot` 内现有判重链补一段：

```csharp
string yikeSignature = (snapshot.YikeContext ?? state.LastCapturedYikeContext ?? YikeWindowContext.Unknown()).ContextSignature;
bool yikeChanged = !string.Equals(state.LastSentYikeContextSignature, yikeSignature, StringComparison.Ordinal);
```

把 `yikeChanged` 加入"是否需要发送"判定（与 `payloadChanged || foxChanged || forceRebuild` 并列）。发送成功后更新 `state.LastSentYikeContextSignature = yikeSignature`。

3c) 在野狐拼装 `roomToken` / `foxMoveNumber` 出站行的同段（参考 `SyncSessionCoordinator.cs:825-835`），增加弈客分支：

```csharp
if (snapshot.SyncMode == SyncMode.Yike && yikeContext != null)
{
    if (!string.IsNullOrWhiteSpace(yikeContext.RoomToken))
        messages.Add(protocolAdapter.CreateYikeRoomTokenMessage(yikeContext.RoomToken.Trim()));
    if (yikeContext.MoveNumber.HasValue)
        messages.Add(protocolAdapter.CreateYikeMoveNumberMessage(yikeContext.MoveNumber.Value));
}
```

3d) `Reset` / 类似清理路径：把 `LastSentYikeContextSignature` 与 `LastCapturedYikeContext` 一并清空。

- [ ] **Step 4: 跑测试 PASS**

- [ ] **Step 5: 提交**

```bash
git commit -am "feat(yike): SendBoardSnapshot 纳入弈客上下文判重与字段输出"
```

---

### Task 10: 后台落子分支接受 `SyncMode.Yike`

**Files:**
- Modify: `readboard/Core/Protocol/BackgroundSelectionWindowBindingCoordinator.cs`
- Modify: `readboard/Core/Protocol/SyncSessionCoordinator.Orchestration.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/SyncSessionCoordinatorOrchestrationTests.cs`（增量）

- [ ] **Step 1: 写失败的测试**

复用现有 `FoxBackgroundPlace` 后台落子测试，参数化 `[InlineData(SyncMode.FoxBackgroundPlace), InlineData(SyncMode.Yike)]` 走通同一条 happy path（落子序列产出 `placeComplete`）。

- [ ] **Step 2: 跑测试 FAIL**

- [ ] **Step 3: 实现**

打开两个文件，找到现存所有 `syncMode == SyncMode.FoxBackgroundPlace` 判定，按需扩展：
- 走"后台落子"路径的判定 → 改成 `IsBackgroundPlaceMode(syncMode)`，集中函数：

```csharp
private static bool IsBackgroundPlaceMode(SyncMode m)
    => m == SyncMode.FoxBackgroundPlace || m == SyncMode.Yike;
```

- 不要修改 Fox 标题父链解析、`FoxWindowBinding` 那条链路接受弈客（弈客没有"野狐父链上下文"概念）。即弈客模式下进入"后台落子" + "自动识别棋盘" + 不进入 Fox 标题流程。

- [ ] **Step 4: 跑测试 PASS**

- [ ] **Step 5: 提交**

```bash
git commit -am "feat(yike): 后台落子路径接受弈客模式"
```

---

### Task 11: MainForm 接入弈客上下文 + UI 选项

**Files:**
- Modify: `readboard/Form1.Designer.cs`（新增 `rdoYike`）
- Modify: `readboard/Form1.cs`（事件绑定）
- Modify: `readboard/MainForm.Configuration.cs`（持久化绑定）
- Modify: `readboard/MainForm.Protocol.cs`（处理 `ProtocolMessageKind.YikeContext`）
- Modify: `readboard/language_cn.txt` / `language_en.txt` / `language_jp.txt` / `language_kr.txt`

- [ ] **Step 1: 写失败的测试**

新建 `tests/Readboard.VerificationTests/Configuration/YikeConfigurationBindingTests.cs`：

```csharp
[Fact]
public void config_round_trips_yike_sync_mode()
{
    var cfg = AppConfig.CreateDefault("v1", "key");
    cfg.SyncMode = SyncMode.Yike;
    var clone = cfg.Clone();
    Assert.Equal(SyncMode.Yike, clone.SyncMode);
}
```

并复用 `DualFormatAppConfigStoreTests` 模式加 round-trip 用例。

- [ ] **Step 2: 跑测试 FAIL**（如已通过则改加 UI 行为断言）

- [ ] **Step 3: 实现**

3a) `Form1.Designer.cs` 加 `RadioButton rdoYike`，在野狐 / 后台 / 弈城 / 新浪所在 panel 同位置追加，命名一致。

3b) `Form1.cs` 在 `BindSyncModeRadios` / 等价方法里加：

```csharp
if (rdoYike.Checked) config.SyncMode = SyncMode.Yike;
// 反向绑定同理
```

3c) `MainForm.Protocol.cs` 增加：

```csharp
case ProtocolMessageKind.YikeContext:
    sessionCoordinator.SetYikeContext(new YikeWindowContext {
        RoomToken = msg.YikeRoomToken,
        MoveNumber = msg.YikeMoveNumber
    });
    RefreshMainWindowTitleIfNeeded();
    break;
```

3d) 四份语言文件追加：

```
MainForm_rdoYike=弈客
MainForm_titleTagYike=弈客
```

`language_en.txt`：`MainForm_rdoYike=Yike` / `MainForm_titleTagYike=Yike`
`language_jp.txt`：`MainForm_rdoYike=弈客` / `MainForm_titleTagYike=弈客`
`language_kr.txt`：`MainForm_rdoYike=Yike` / `MainForm_titleTagYike=Yike`

3e) 切换平台到非弈客时，`SetYikeContext(YikeWindowContext.Unknown())` 兜底清理。

- [ ] **Step 4: 跑测试 PASS**

- [ ] **Step 5: 提交**

```bash
git commit -am "feat(yike): MainForm 接入弈客模式与协议处理"
```

---

### Task 12: 主窗体标题弈客分支

**Files:**
- Modify: `readboard/MainWindowTitleFormatter.cs`
- Create/Modify: `tests/Readboard.VerificationTests/Host/YikeMainWindowTitleTests.cs`

- [ ] **Step 1: 写失败的测试**

```csharp
public class YikeMainWindowTitleTests
{
    [Fact]
    public void renders_room_and_move()
    {
        string text = MainWindowTitleFormatter.FormatYike(
            "棋盘同步工具", MainWindowTitleDisplayMode.Syncing, true,
            new YikeWindowContext { RoomToken = "65191829", MoveNumber = 16 },
            yikeTag: "弈客", roomSuffix: "号", singleMoveFormat: "第{0}手", titleMissingTag: "未抓到上下文", syncingTag: "同步中");
        Assert.Equal("棋盘同步工具 [弈客][65191829号][第16手]", text);
    }

    [Fact]
    public void renders_move_only_when_no_room()
    {
        string text = MainWindowTitleFormatter.FormatYike(
            "棋盘同步工具", MainWindowTitleDisplayMode.Syncing, true,
            new YikeWindowContext { MoveNumber = 16 },
            yikeTag: "弈客", roomSuffix: "号", singleMoveFormat: "第{0}手", titleMissingTag: "未抓到上下文", syncingTag: "同步中");
        Assert.Equal("棋盘同步工具 [弈客][第16手]", text);
    }

    [Fact]
    public void renders_missing_when_no_handle()
    {
        string text = MainWindowTitleFormatter.FormatYike(
            "棋盘同步工具", MainWindowTitleDisplayMode.Syncing, false,
            YikeWindowContext.Unknown(),
            yikeTag: "弈客", roomSuffix: "号", singleMoveFormat: "第{0}手", titleMissingTag: "未抓到上下文", syncingTag: "同步中");
        Assert.Equal("棋盘同步工具 [弈客][同步中][未抓到上下文]", text);
    }
}
```

- [ ] **Step 2: 跑测试 FAIL**

- [ ] **Step 3: 实现**

新增 `MainWindowTitleFormatter.FormatYike(...)`，参考已有 `Format(...)`（Fox）骨架。在 `MainForm` 调用方按当前 `SyncMode` 选择 `Format` 或 `FormatYike`。

调用方按签名节流：仅在 `YikeContext.ContextSignature` 或 `displayMode` 变化时写 `this.Text`，与野狐策略一致。

- [ ] **Step 4: 跑测试 PASS**

- [ ] **Step 5: 提交**

```bash
git commit -am "feat(yike): 主窗体标题渲染弈客上下文"
```

---

### Task 13: 全量回归 + 手工冒烟

- [ ] **Step 1: 全量测试**

```bash
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj
```

期望：全部通过。

- [ ] **Step 2: benchmark 接受套**

```bash
dotnet run --project benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj
```

期望：弈客新增不影响野狐 / 弈城 / 新浪基线。

- [ ] **Step 3: 手工冒烟**

按 spec 测试要求：
- 弈客大厅普通对弈房间：能识别棋盘、显示房间号 + 手数（依赖 host 已实现 `yike` 出站）、后台落子
- 弈客直播个人直播房间：同上
- 弈客比赛页：棋盘识别 OK，标题与协议字段表现"无房间号"
- 切换平台到 / 离开弈客时上下文不串

如 lizzieyzy-next host 侧 `yike` 协议消息尚未实装，先手工用 pipe / TCP 注入 `yike room=XXX move=N` 验证 readboard 侧链路。

- [ ] **Step 4: 提交（如有 fixture / 文档微调）**

```bash
git commit -am "test(yike): 加入手工冒烟用 fixture 与回归基线"
```

---

## 配套改动（host 侧，独立 PR，不在本 plan 内）

按 `CLAUDE.md` "改动影响接口或集成行为先更新 host 侧"约定：

- `lizzieyzy-next` `BrowserFrame` / `OnlineDialog` 在 URL 变化或 Socket.IO 手数变化时，向 readboard 发送 `yike room=<token> move=<n>`。
- 接收 readboard 出站 `yikeRoomToken` / `yikeMoveNumber` 行（与野狐 `roomToken` / `foxMoveNumber` 平行处理）。
- 与 readboard 升级保持向后兼容：旧版 readboard 收到 `yike` 行会走 `ProtocolMessage.CreateLegacyLine` 兜底路径不报错；旧版 lizzieyzy-next 不发送 `yike`，新 readboard 在弈客模式下仅显示"未抓到上下文"。

---

## 跨任务约定

- TDD：每个 task 内严格遵守"测试先红 → 实现 → 测试转绿 → 提交"。
- 提交频次：每个 task 1 次提交。
- 测试隔离：弈客新测试独立文件，不修改野狐 / 弈城 / 新浪既有测试断言。
- 命名：弈客字段统一用 `Yike` 前缀（`YikeWindowContext` / `YikeRoomToken` / `YikeMoveNumber` / `SetYikeContext`），不复用 Fox 字段。
- 出现冲突或假设不成立时（特别是 Task 4 颜色阈值），停下来上报，不要默默用兜底逻辑掩盖。
