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

## 稳定盘面确认（2026-09-22）

- 持续同步和快速同步最终共用 `RunKeepSyncLoop` 的 capture → recognition → dispatch 入口。一个出站结果段发送首次有效观察，再允许后续独立有效采样发送一次完整确认；第三次起相同结果静默。结果变化后重新建立该段资格，包括 A → B → A。
- 出站相等条件仍为 payload、有效手数、末手来源和窗口上下文。marker 或来源变化不被新规则吞掉；宿主继续拥有归一化冲突身份、HOLD 与接纳决策。
- 新观察资格从真实捕获入口建立，绑定缓存 generation，并由单个识别采样持有一次性消费状态。直接调用 `SendBoardSnapshot`、重放同一采样、无效识别或采集失败不能取得确认资格。单次读盘不启动额外采样。
- 取得出站锁并复验会话后才执行棋盘去重和确认消费；未分发的旧会话批次不预先占用新会话资格。传输异常仍遵守既有失败规则，不新增重试。
- 缓存重置和真正的平台切换同时失效确认状态；重复设置相同平台不重置。停止清理的 idle 判断与清理受同一 worker 锁保护，不能跨越新 worker 激活。
- 显式 `forceRebuild` 只消费一次，普通确认不重复携带它。Emitter 仍只顺序发送完整批次，不拥有采样计数、ACK 或宿主重建决策。
