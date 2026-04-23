# MainForm 状态边界

日期: 2026-04-23

## 背景

本轮清理前，`MainForm` 保留了部分历史字段：`public static int ox1`、`public static int oy1`、`public static int type`，以及实例字段 `boardW` / `boardH`。这些字段性质不同，不能统一改成 `Program.CurrentContext.Config`。

## 约定

- `AppConfig` 是持久化配置快照，负责启动加载和保存写出；它不是所有 UI 编辑中间态的实时唯一来源。
- `selectionX1` / `selectionY1` / `ox2` / `oy2` 是本次框选的运行时坐标，不能写入 `AppConfig`。
- `ox1` / `oy1` 只是 `selectionX1` / `selectionY1` 的历史 public static 镜像。移除前必须全仓和集成端 grep；确认无引用后应删除镜像字段及同步赋值，而不是迁移到 Config。
- `currentSyncType` 是当前同步模式运行时状态；保存时才映射到 `AppConfig.SyncMode`，加载时从 `AppConfig.SyncMode` 恢复。
- `type` 是 `currentSyncType` 的历史 public static 镜像。移除前必须确认集成端没有直接字段访问或 reflection 依赖；移除不能改变 `LegacyTypeToken = CurrentSyncType.ToString()` 的协议行为。
- `boardW` / `boardH` 是窗体当前棋盘尺寸运行时状态；保存时写入 `AppConfig.BoardWidth` / `BoardHeight`。如要清理，应通过私有属性或专用 setter 收敛读写，不能让文本框编辑过程直接污染持久配置快照。

## 验证

- 全仓 grep `ox1`、`oy1`、`MainForm.type`、`boardW`、`boardH`。
- 对 `D:\dev\weiqi\lizzieyzy-next` grep `MainForm`、`ox1`、`oy1`、`readboard.MainForm`。
- 修改后运行主测试项目，特别关注启动配置、棋盘尺寸、同步模式、框选校准相关回归测试。

2026-04-23 本轮清理结果：

- `ox1`、`oy1`、`type` 在 readboard 生产代码中只剩历史镜像字段和镜像赋值，已删除。
- `D:\dev\weiqi\lizzieyzy-next` 未发现 `MainForm.ox1`、`MainForm.oy1`、`MainForm.type`、`readboard.MainForm` 或相关 reflection 字段访问。
- `boardW` / `boardH` 保持运行时字段，本轮不改。
- `LegacyTypeToken = CurrentSyncType.ToString()` 保持不变，并由源代码回归测试锁定。

## 不在范围内

| 项 | 决策 | 原因 |
|---|---|---|
| 把 `boardW` / `boardH` 直接替换为 `Program.CurrentContext.Config.BoardWidth/Height` | 不做 | 会混淆持久配置和 UI 编辑中间态 |
| 把框选坐标写入配置 | 不做 | 框选坐标是瞬时运行状态，不是用户配置 |
| 改变协议里的同步模式 token | 不做 | `LegacyTypeToken` 是对外协议行为 |
