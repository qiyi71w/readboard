# SyncSessionCoordinator 只读 SessionState 访问约定

日期: 2026-04-23

## 背景

`SyncSessionCoordinator` 的 `SessionState` 由 `stateLock` 串行保护，所有读写都必须在 `lock (stateLock)` 范围内。最初 4 个只读属性（`StartedSync` / `KeepSync` / `IsContinuousSyncing` / `SyncBoth`）各自 inline `lock (stateLock) return sessionState.X;`，模板重复且容易漏锁（新增只读属性时遗忘 lock 会读到撕裂状态）。

## 约定

**只读 `SessionState` 访问必须走 `GetLockedSessionState<T>(Func<SessionState, T> selector)` 辅助方法，不得 inline `lock (stateLock)` + 裸返回。**

```csharp
public bool StartedSync
{
    get { return GetLockedSessionState(s => s.StartedSync); }
}
```

## 边界（明确**不**适用的场景）

以下模式**仍使用 inline `lock (stateLock)`**，不要改写：

- **写操作** —— 赋值、`Reset()`、状态机迁移必须 inline lock（闭包签名不合适）
- **组合读 + 写** —— 如 `EndKeepSync` 同时读 `PendingMove` 并翻转 `StartedSync`/`KeepSync`
- **多值读取** —— 需在同一锁窗口读多个字段构造不可变快照时（返回 tuple 反而让调用点难读）
- **读取后立即 signal 事件的场景** —— `TryTakePendingMove` 类需要捕获 `shouldSignal` 局部变量

`GetLockedSessionState` 只覆盖"锁住 → 读一个字段 → 返回"的 trivial getter。

## 为什么不走 `[return]` 属性 + 显式锁标注

C# 没有 Kotlin 的 `@GuardedBy` 编译期检查；加扩展方法或 `Locked<T>` 包装会让类型签名噪音远大于收益（只 4 个属性）。保持辅助方法简短、**内部私有**、不导出即可。

## 测试覆盖

无新增测试 —— 现有 309 个用例对 `StartedSync`/`KeepSync`/`IsContinuousSyncing`/`SyncBoth` 的行为断言已足够保证语义不回退（任何读到不一致状态的场景都会挂现有用例）。

## 不在范围内（评审后决策为不做）

| 项 | 决策 | 原因 |
|---|---|---|
| 把 `SessionState` 整个字段改为 `ImmutableSessionState` + `Interlocked.Exchange` | 不做 | `SessionState` 内含 `PendingMoveState`（有 `AttemptsRemaining` 等可变字段），重构面积远大于本次简化目标 |
| 抽 `Locked<T>` wrapper 类型 | 不做 | 只 4 个 getter，wrapper 成本超过收益 |
| 把 `stateLock` 改成 `ReaderWriterLockSlim` | 不做 | getter 持锁时间极短（纳秒级字段读），读写锁的开销反而更高 |
