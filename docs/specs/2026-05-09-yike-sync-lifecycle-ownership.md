# readboard 弈客同步生命周期所有权修复（2026-05-09）

## 背景

当前弈客同步控制命令（`yikeSyncStart` / `yikeSyncStop`）同时由两条路径发送：

1. `Form1` 按钮事件里直接 `SendLine("yikeSyncStart")`
2. `SyncSessionCoordinator` 在 keep-sync 生命周期里发送控制命令

这会导致生命周期所有权分叉，表现为：

- 同一次启动动作可能触发重复 `yikeSyncStart`
- 一次性同步（`TryRunOneTimeSync`）没有成对 stop，宿主端可能残留同步会话

## 修复目标

1. 弈客控制命令只由 `SyncSessionCoordinator` 负责发送，`Form1` 不再直接发 `yikeSyncStart`。
2. 一次性同步在弈客平台下也走成对生命周期：`start -> one-time run -> stop`。
3. keep-sync / continuous-sync 既有语义保持不变（已有 `SendSync` / `SendStopSync` 时序不变）。

## 边界

### In Scope

- `readboard/Form1.cs`
- `readboard/Core/Protocol/SyncSessionCoordinator.Orchestration.cs`
- 对应 verification tests（仅覆盖生命周期行为）

### Out of Scope

- 弈客几何识别/归一化算法
- 弈客落子坐标换算、RenderWidget 点击路径
- keep-sync 主循环采样策略（`RunKeepSyncLoop`）本轮不改
- 协议关键词与 wire 文本（`ProtocolKeywords`）不改

## 约束

1. 不改变协议字面值（`yikeSyncStart` / `yikeSyncStop` / `sync` / `stopsync`）。
2. 不新增配置项，不新增兜底分支。
3. 所有行为变化必须有对应测试锁定。

## 验证要求

至少覆盖以下回归点：

1. `TryRunOneTimeSync` 在 yike 平台发送成对控制命令。
2. `TryRunOneTimeSync` 失败路径也会发送 `yikeSyncStop`（前提是已发送 start）。
3. keep-sync 的既有控制命令时序不回退。
