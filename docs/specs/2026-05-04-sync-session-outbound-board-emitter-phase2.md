# SyncSessionCoordinator 棋盘出站编排边界（Phase 2）

日期: 2026-05-04

## 背景

Phase 1 已把协议发送出口收口到 `OutboundProtocolDispatcher`，但 `SyncSessionCoordinator.SendBoardSnapshot(...)` 仍同时承担：

- dedupe 与状态更新
- window context / `forceRebuild` / `foxMoveNumber` / 棋盘行 / `end` 的发送顺序编排

Phase 2 只抽第二部分，保持前者留在 coordinator。

## 约定

- 新增内部 `OutboundBoardSnapshotEmitter`
- `SyncSessionCoordinator` 继续负责：
  - `ResolveEffectiveFoxMoveNumber(...)`
  - `BuildOutboundWindowContextUnsafe()`
  - dedupe 判定
  - `forceRebuildArmed` 消费
  - `LastBoardPayload` / `lastSentBoardFoxMoveNumber` / `lastSentWindowContextSignature` 更新
- `OutboundBoardSnapshotEmitter` 只负责按既定顺序发送一个已计算好的 batch：
  1. window context messages
  2. `forceRebuild`
  3. `foxMoveNumber`
  4. `snapshot.ProtocolLines`
  5. `end`

## 生命周期与线程

- Phase 2 仍通过 `OutboundProtocolDispatcher` 同步发送
- 不改变 `workerLock` / `stateLock` 的覆盖范围
- 不把发送移出当前调用线程

## 不在范围内

| 项 | 决策 | 原因 |
|---|---|---|
| 把 dedupe 状态迁到 emitter | 不做 | 这会让发送 helper 重新持有 coordinator 状态 |
| 改变 `syncPlatform` / `roomToken` / `record*` / `foxMoveNumber` / `forceRebuild` 的发送条件 | 不做 | 这些语义已被 Fox title/status 设计和现有回归测试锁定 |
| 缩小 `workerLock` 覆盖范围 | 不做 | 这是下一阶段才处理的时序变更 |
