# readboard 弈客落子 DPI 坐标一致性修复（2026-05-09）

## 背景

线上反馈：同一弈客房间里，偶发自动落子偏移；关闭同步再开启并刷新后恢复正常。  
同时可见的棋盘外框与网格点位是正确的，说明几何识别链路通常正常，问题更可能在最终点击坐标投递。

## 结论与假设

弈客模式下点击通过 `PostMessage` 投递到 `Chrome_RenderWidgetHostHWND`。  
当宿主窗口被标记为 `IsDpiAware=false` 时，现有通用 background 路径会对 client 坐标做 DPI 放大，可能把已正确的几何点再次放大，导致偏移。

## 修复目标

1. `SyncMode.Yike` 下，`BackgroundPost` 使用几何链路给出的 client 坐标直接投递。
2. 不改变非弈客平台（Fox/Tygem/Sina/Background）的 DPI 缩放规则。
3. 用回归测试锁定“弈客 + DpiUnaware 窗口”场景不再放大坐标。

## 边界

### In Scope

- `readboard/Core/Placement/IMovePlacementService.cs`
- `tests/Readboard.VerificationTests/Placement/LegacyMovePlacementServiceTests.cs`

### Out of Scope

- 弈客几何探针与候选排序逻辑（lizzie 侧）
- `yikeGeometry` 协议字段定义
- 弈客会话状态机与同步生命周期
- 非弈客平台落子路径

## 验证要求

至少覆盖：

1. 现有弈客落子用例保持通过（含 RenderWidget 目标句柄断言）。
2. 新增 `IsDpiAware=false, DpiScale=1.5` 场景下，弈客 `lParam` 不发生额外放大。
3. 相关 placement 回归集通过。
