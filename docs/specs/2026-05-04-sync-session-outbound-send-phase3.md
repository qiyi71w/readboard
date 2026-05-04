# SyncSessionCoordinator 发送移出 workerLock（Phase 3）

日期: 2026-05-04

## 背景

Phase 1 抽出了 `OutboundProtocolDispatcher`，Phase 2 抽出了 `OutboundBoardSnapshotEmitter`，但 keep-sync 热路径里仍有协议发送发生在 `workerLock` 临界区内。

Phase 3 的目标不是改状态机，而是把**发送本身**移出 `workerLock`，同时避免 stop/restart 与多行协议发送交叉出半帧。

## 约定

- `workerLock` 内只做：
  - 当前 worker 身份校验
  - runtime 状态更新
  - dedupe / cache / pending move 处理
  - 生成不可变的 protocol dispatch plan
- 真正的协议发送在 `workerLock` 外执行
- 多条相关协议必须通过 `OutboundProtocolDispatcher.ExecuteBatch(...)` 在同一个 dispatcher 同步窗口内发送，不能拆成多次独立 `Send(...)`

## 适用范围

- keep-sync 启动时的 `SendSync()`
- keep-sync 结束时的 `SendStopSync()`
- `RecognizedSample` 对应的整帧协议发送：
  - `clear`
  - overlay line
  - `start`
  - board snapshot batch

## 不在范围内

| 项 | 决策 | 原因 |
|---|---|---|
| 改写 keep-sync / continuous-sync 生命周期状态机 | 不做 | Phase 3 只调整发送位置，不重写状态机 |
| 改变 `SendBoardSnapshot(...)` 判重条件 | 不做 | Fox title/status 设计和现有协议测试已经锁定 |
| 引入异步协议发送队列 | 不做 | 会改变 stop/shutdown/backpressure 语义 |
