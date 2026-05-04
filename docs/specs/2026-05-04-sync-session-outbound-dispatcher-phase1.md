# SyncSessionCoordinator 出站发送边界（Phase 1）

日期: 2026-05-04

## 背景

`SyncSessionCoordinator` 既负责同步状态机，也直接持有协议序列化与 `transport.Send` 的发送出口。Phase 1 先把发送出口收口到独立内部类，减少 coordinator 的职责，但不改变发送时序和锁语义。

## 约定

- 新增内部 `OutboundProtocolDispatcher`，负责：
  - `ProtocolMessage -> line` 序列化
  - `transport.Send(...)`
  - `transport.SendError(...)`
  - shutdown 三条协议的串行发送与关闭
- `SyncSessionCoordinator` 继续负责：
  - 决定何时发
  - 决定发哪些 `ProtocolMessage`
  - `SendBoardSnapshot` / `SendOverlayLine` / window context / dedupe 规则
- Phase 1 保持同步发送；调用点仍然在原来的线程和锁窗口内执行，不把发送移出 `workerLock`。

## 生命周期

- `SyncSessionCoordinator.Start()` 打开 dispatcher
- `StopCore()` 仍通过 `CloseOutboundProtocol()` 阻止后续发送
- `SendShutdownProtocol()` 仍保持三条 shutdown 消息的原有顺序：`stopSync` -> `bothSync 0` -> `endSync`

## 不在范围内

| 项 | 决策 | 原因 |
|---|---|---|
| 缩小 `workerLock` 覆盖范围 | 不做 | 这会改变发送时序与竞态面，属于后续阶段 |
| 把 `SendBoardSnapshot` 的 context/board 编排一并搬走 | 不做 | Phase 1 只抽发送出口，不改出站决策边界 |
| 引入异步发送队列 | 不做 | 会改变 stop/shutdown/backpressure 语义 |
