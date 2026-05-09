# readboard 弈客同步稳定性修复（2026-05-09）

## 本文目的

记录本轮针对“弈客同步触发不稳定 / 自动落子不触发”的已落地修复，作为 `worktree-feat-yike-platform` 当前行为基线。

## 对照文档

- `docs/specs/2026-04-24-readboard-yike-platform-design.md`
- `docs/specs/2026-05-07-yike-keep-sync-shortcut-analysis.md`

本轮实现保持以下约束不变：

- readboard 不新增弈客 URL 解析职责，房间语义仍由宿主发送。
- 弈客点击仍走 `Chrome_RenderWidgetHostHWND` 子窗口后台路径。
- `yikeSyncStart` / `yikeSyncStop` 协议语义不变。

## 已修复项

1. `一键同步` / `持续同步` / `开始同步` 进入弈客模式时，若协议会话已连接，会主动发送 `yikeSyncStart`。
2. 弈客 `place` 不再依赖首次截图识别成功；即使采样识别失败，只要 `yikeGeometry` 可用，仍可走落子路径。
3. 弈客落子路径改为后台 `PostMessage`，并在点击前先发 `WM_MOUSEMOVE`，`WM_LBUTTONDOWN` 使用 `MK_LBUTTON`。
4. 弈客几何重建 `BoardFrame` 时补齐句柄与窗口 bounds 回填，减少“有几何但 frame 为空”导致的落子短路。

## 关键改动文件

- `readboard/Form1.cs`
- `readboard/Core/Protocol/SyncSessionCoordinator.Orchestration.cs`
- `readboard/Core/Placement/IMovePlacementService.cs`
- `tests/Readboard.VerificationTests/Protocol/YikeBackgroundPlaceTests.cs`
- `tests/Readboard.VerificationTests/Placement/LegacyMovePlacementServiceTests.cs`
- `tests/Readboard.VerificationTests/Placement/LegacyMovePlacementAcceptanceMatrixTests.cs`

## 回归覆盖

- `YikeBackgroundPlaceTests`
  - 新增“capture 持续失败但 geometry 可用时仍能 placeComplete”用例。
- `LegacyMovePlacementServiceTests`
  - 弈客路径断言改为 `BackgroundPost`，并校验投递目标是 CEF RenderWidget 子窗口。
- `LegacyMovePlacementAcceptanceMatrixTests`
  - 弈客模式矩阵预期从 `BackgroundSend` 更新为 `BackgroundPost`。
