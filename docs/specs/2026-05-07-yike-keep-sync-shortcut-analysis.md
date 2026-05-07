# yike 模式 keep-sync 短路可行性分析

## 背景

readboard 在弈客（yike）模式下当前并行跑两条链路：

1. **遗留链路**：`RunKeepSyncLoop` 每帧截图 → 颜色识别棋盘 → 通过 `outboundBoardSnapshotEmitter.EmitWhileSynchronized` 发 `start <w> <h>` / `re=...` / `end` / overlay 等棋盘状态消息给宿主 lizzieyzy-next
2. **新链路**：宿主主动通过 `yikeGeometry` 协议消息把浏览器视口下的棋盘几何（`left/top/width/height/firstX/firstY/cellX/cellY`）发给 readboard；readboard 存到 `runtimeState.LastCapturedYikeGeometry`；落子时 `ResolvePlacementFrame` → `CreateYikePlacementFrame` 用这份几何重建 frame，PostMessage 到 `Chrome_RenderWidgetHostHWND` 子窗口完成点击

新链路是 2026-04 之后引入的（详见 `2026-04-24-readboard-yike-platform-design.md` 顶部 2026-05-05 更新说明）。在 lizzieyzy-next 端的事实查证（`src/main/java/featurecat/lizzie/analysis/ReadBoard.java`）显示：

- yike 模式下宿主 **`re=...` 行被丢弃**：`end` 分支的 `isYikePlatform` 判断会跳过 `syncBoardStones`
- yike 模式下宿主 **`start <w> <h>` 仍消费**（无平台判断），会触发 `Lizzie.board.reopen`/`resetActiveSyncState`
- 真正的弈客棋盘同步走宿主自己的浏览器 DOM 路径，与 readboard 发的 `re=` 互斥

也就是说 readboard 在 yike 模式下花在截图和识别上的 CPU **99% 是浪费**，唯一仍被消费的是 `start` 行（且 yike 是固定 19 路，棋盘大小变化几乎不发生）。

理论上可以把 yike 模式下的截图 + 识别 + outbound 棋盘状态消息整套短路掉，只保留 `yikeGeometry` 接收 + 控制命令 + 落子。本文记录这次梳理的结构与短路时遇到的硬耦合，作为未来动手前的起点笔记。

## yike 模式下 keep-sync 的实际执行步骤

入口与调度均在 `readboard/Core/Protocol/SyncSessionCoordinator.Orchestration.cs`：

- `TryStartContinuousSync` → `RunContinuousSyncLoop` → `TryStartDiscoveredKeepSync` 写 `runtimeState.SelectedWindowHandle` → `TryStartKeepSyncSession`
- `TryStartKeepSync`（用户主动 sync 按钮）直接进 `TryStartKeepSyncSession` → `TryPrimeSyncFrame` 首帧识别 → `TryActivateKeepSyncSession` 起 `RunKeepSyncLoop`
- `RunKeepSyncLoop` 逐帧执行：
  1. 首次 `SendSync` → `SendYikeSyncStart` 发 `yikeSyncStart`
  2. `TryCaptureSnapshot` 拿 host snapshot
  3. 写 `runtimeState.SelectedWindowHandle`
  4. `EnsureSyncSourceSelected`（yike 走 Background 分支末尾的 `EnsureSelectionBounds`）
  5. `DispatchPendingMove` → 若有 pending move 走 `PlacePendingMove` → `ResolvePlacementFrame` → yike 分支用 `CreateYikePlacementFrame` 完全重建 frame
  6. `TryProcessKeepSyncSample` → `TryRecognizeSample` → `CaptureFrame`（截图）→ `RecognizeFrame`（颜色识别）→ `CompleteRecognizedSample` 写 `runtimeState.CurrentBoardFrame` / `CurrentBoardPixelWidth/Height` / `LastBoardPayload` → `BuildRecognizedSampleProtocolDispatch` → `DispatchRecognizedSampleProtocol`
  7. `WaitForNextSample` 节流回到第 2 步

## 共享状态依赖图

| 字段 | 写者 | 读者 |
|---|---|---|
| `runtimeState.SelectedWindowHandle` | `TryRunOneTimeSync` / `TryStartKeepSync` / `TryStartDiscoveredKeepSync` / `RunKeepSyncLoop` 每帧重写 / `Form1.SetSelectedWindowHandle`（用户选窗口）| `CreateCaptureRequest` / `CreateYikePlacementFrame` / `BuildRecognizedSampleProtocolDispatch` / `ResolveSelectedWindowHandle` |
| `runtimeState.CurrentBoardFrame` | `ReplaceRuntimeFrame`（仅在 `CompleteRecognizedSample` / `ClearRuntimeFrame` / `FinishKeepSyncLoop`）| `PlacePendingMove`（`!= null` gate）/ `ResolvePlacementFrame`（base frame，yike 下被覆盖） |
| `runtimeState.CurrentBoardPixelWidth/Height` | `UpdateBoardGeometry`（识别成功）/ `SetYikeGeometry`（入站时） | `HandlePlaceRequest` 的 `TryQueuePendingMove` boardPixelWidth gate / `CompleteRecognizedSample` 的 previousArea 比较 |
| `runtimeState.LastCapturedYikeGeometry` | `SetYikeGeometry`（入站 yikeGeometry）/ `StopSyncSession` 清空 | `ResolvePlacementFrame` |
| `runtimeState.LastCapturedYikeContext` | `SetYikeContext`（入站 `yike room=...`）/ `ResetSyncCachesCore` 清 | `BuildOutboundWindowContextUnsafe`（出站上下文消息）/ `ResolveYikeContextSignatureUnsafe` |
| `sessionState.LastBoardPayload` | `TryBuildOutboundBoardSnapshotBatch` | 同函数判重 |

## 落子链路对老链路的真正硬依赖

落子 `HandlePlaceRequest` → `TryQueuePendingMove` → `RunKeepSyncLoop` 内 `DispatchPendingMove` → `PlacePendingMove`，对老链路实际有 4 处 gate：

1. `sessionState.KeepSync == true` —— keep-sync 必须真正激活
2. `runtimeState.CurrentBoardPixelWidth >= snapshot.BoardWidth` —— `SetYikeGeometry` 已经会写这两个值，**短路后只要 host 先发 yikeGeometry 再发 place 就过**
3. `runtimeState.CurrentBoardFrame != null` —— **当前是对老链路的强耦合**。yike 下若不再跑 `CompleteRecognizedSample`，CurrentBoardFrame 永远 null，落子直接被这个 gate 挡掉
4. `ResolvePlacementFrame` 的 yike 分支：`LastCapturedYikeGeometry.IsUsable` 必须真。Window 句柄从 `CurrentBoardFrame.Window` 首选，fallback 到 `runtimeState.SelectedWindowHandle` —— 即使老链路短路，SelectedWindowHandle 仍会被持续写入

**结论**：落子对老链路的真正硬依赖只剩 `CurrentBoardFrame != null` 这一条。识别出的 viewport 在 yike 分支已被 `CreateYikePlacementFrame` 完全替换，老 frame 内容不被消费。

## 短路风险清单

### 激进方案：yike 下整个 `RunKeepSyncLoop` 跳过截图/识别/外发

**必须先做的解耦**（按依赖顺序）：

1. **`yikeContext`/`syncPlatform`/`yikeRoomToken`/`yikeMoveNumber` 出站路径解耦**：当前这些消息的递送嵌在 `TryBuildOutboundBoardSnapshotBatch` → `BuildOutboundWindowContextUnsafe`，**靠每帧 board snapshot 触发**。spec §下游契约约束（`2026-04-24-readboard-yike-platform-design.md:108-124`）强制要求"上下文签名变化时下游必须收到一帧"。短路前必须把这条递送路径从 board snapshot batch 中独立出来，让 `SetYikeContext` 调用即触发推送 + 判重。
2. **`SendSync` + `yikeSyncStart` 触发时机迁移**：当前由 `RunKeepSyncLoop` 首帧触发；keep-sync 不跑则要找新触发点（如 `BeginKeepSync` 或 `OnKeepSyncStarted`）。
3. **`PlacePendingMove` 的 `CurrentBoardFrame != null` gate 改写**：在 yike 模式下改为 `LastCapturedYikeGeometry.IsUsable`，其他模式保留 `CurrentBoardFrame != null`。

完成上述 3 项后，yike 模式下 `RunKeepSyncLoop` 才能剪掉 `TryProcessKeepSyncSample` 整段，保留 `DispatchPendingMove` + 句柄维护 + 节流。

**仍要保留的出站消息**：`yikeSyncStart` / `yikeSyncStop` / `yikeContext`（房间号/手数）/ `placeComplete` / `version` / `error` / `ready` / `syncPlatform`。

**会变成无用代码可一并删除**：
- `BoardRecognitionResult.TryResolveYikeBounds` / `FindFirstInnerGridLine` / `IsYikeGridLine` / `ScanForGridLine`
- `IBoardCaptureService` 里 yike 强制 PrintWindow 分支 + `CapturePrintWindowFullContent`（如果只 yike 用）
- `tests/.../Recognition/YikeBoardLocatorTests.cs`
- 锁定 `WaitForLine("end", ...)` 的 yike 测试用例需重写

### 保守方案：只在 dispatch 末端禁外发 `start`/`re=`/`end`/`overlay`/`clear`，识别仍跑

**改动面**：在 `DispatchRecognizedSampleProtocol` 或 `EmitWhileSynchronized` 末端按 yike 平台过滤消息，保留 `WindowContextMessages`（含 yikeRoomToken/yikeMoveNumber/syncPlatform）。

**优点**：不需要触动 placement gate 和 SendSync 时机；`CurrentBoardFrame` / `CurrentBoardPixelWidth/Height` 仍由 `CompleteRecognizedSample` 填好，落子和验证都正常。

**缺点**：仍在做无意义的截图 + 颜色识别，CPU 浪费照旧。

**仍要改的测试**：`YikeBackgroundPlaceTests` 三处 `WaitForLine("end", ...)` 不再成立，须重写为不依赖 `end`，或改为只断言不发 `end`。

## 测试与文档契约清单

会被短路改动破坏的测试（`tests/Readboard.VerificationTests/` 下）：

- `Protocol/YikeBackgroundPlaceTests.cs` — 三处 `WaitForLine("end", ...)` 锁定老链路在 yike 下发 `end`；另有 `re=yike` payload 锁定
- `Protocol/SyncSessionCoordinatorAcceptanceMatrixTests.cs` / `SyncSessionCoordinatorTests.cs` / `SyncSessionCoordinatorOrchestrationTests.cs` — SyncMode.Yike 矩阵覆盖
- `Recognition/YikeBoardLocatorTests.cs` / `Recognition/SyncModeViewportAcceptanceTests.cs` — 锁定 yike 也走识别 + `UsesAutoDetectedBounds`
- `Protocol/YikeOutboundProtocolContractTests.cs` / `YikeProtocolInboundParsingTests.cs` / `YikeProtocolKeywordsTests.cs` / `YikeWindowContextTests.cs`
- `Host/YikeMainFormIntegrationRegressionTests.cs` / `YikeMainWindowTitleTests.cs`
- `Placement/LegacyMovePlacementServiceTests.cs` / `LegacyMovePlacementAcceptanceMatrixTests.cs`

文档需同步修订（`docs/specs/2026-04-24-readboard-yike-platform-design.md`）：

- 第 7 行更新说明已撤回"自己识别为唯一来源"，**为短路开了门**
- 第 13、52-58 行仍把 yike 描述成"行为对标 FoxBackgroundPlace（自动棋盘识别）" — 与短路冲突
- 第 108-124 行下游契约要求 `yikeMoveNumber` 与 `foxMoveNumber` 平行、签名变化必须发送 — **必须保留**，激进短路时需改路径而非删契约
- 第 126-141 行 `yikeSyncStart`/`yikeSyncStop`/`yikeBrowserSyncStop` — 控制类必须保留
- 第 188-198 行测试要求"Payload 不变但弈客上下文签名变化时仍发送一帧" — 锁定上下文必须经 board snapshot 路径外发；激进短路要解耦后才能满足
- 第 209-213 行 Non-Goals "不实现通用棋盘检测" — 不冲突

## 推荐路径

**当前不动**。理由：

1. 当前 yike 模式工作正常（除了 CPU 浪费），不是性能瓶颈
2. 激进短路需要 3 项解耦 + 重写多个测试 + 同步改 spec，工作量大
3. 保守短路虽然改动小，但仍要重写 `WaitForLine("end")` 类测试，性价比一般
4. 任何方案都涉及公共契约（spec 第 108-124 行 `yikeMoveNumber` 平行性、第 188-198 行测试要求），需要谨慎

**未来动手时的最小切口**：先做激进方案的"3 项解耦"（独立的上下文推送路径、SendSync 触发点迁移、placement gate 改写）作为一个独立小重构提交，不附带行为变更。完成后再单独提一个 PR 做 yike 模式 keep-sync 的实际短路与死代码清理。

## 不在范围内 / 已决策不修复

- **`TryResolveYikeBounds` 等识别代码暂不删**：必须先确认 yike 模式 keep-sync 已不再调用 `TryResolveBounds(SyncMode.Yike, ...)`，否则删了会导致识别返回 false 中断 keep-sync 主循环
- **`YikeWindowContext` 数据模型保留**：`yike room=... move=...` 协议消息仍是宿主活的出站消息，readboard 仍消费用于主窗体标题展示和 `BuildOutboundWindowContextUnsafe` 出站 token 签名
- **`Chrome_RenderWidgetHostHWND` 子窗口路径保留**：`LegacyMovePlacementService.TryResolveYikeRenderWidgetClientBounds` 是新链路落子的核心
- **`LegacySyncWindowLocator.FindYikeWindow`/`IsYikeTitleCandidate` 保留**：仍需识别弈客窗口才能 PostMessage
