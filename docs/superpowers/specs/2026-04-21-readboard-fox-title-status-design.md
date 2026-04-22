# readboard 野狐同步标题状态与性能安全设计

日期：2026-04-21

修订说明：

- 本文替代同日较早版本的“标题状态提示设计”。
- 修订原因是后续代码审查发现：把野狐标题解析和主窗体标题刷新直接放进 `CaptureSnapshotCore()` 的高频 UI 路径，会在 `野狐` / `野狐(后台落子)` 下引入明显性能回退。
- 同时确认 `lizzieyzy-next` 会把 `foxMoveNumber`、`roomToken`、`recordCurrentMove`、`recordTotalMove`、`recordAtEnd`、`recordTitleFingerprint` 当作同步决策输入，而不是纯展示字段；因此不能通过“减少元数据刷新频率”来换性能。

## 目标

在不改变现有 readboard 对外协议语义的前提下，同时满足以下目标：

- 主窗体标题能显示当前野狐窗口上下文，帮助确认绑定是否正确。
- 保留“标题信息与发往 `lizzieyzy-next` 的窗口上下文同源”这一约束。
- 修复野狐模式下由标题解析引入的鼠标丢帧、卡顿和 UI 卡死感。
- 不引入“同盘串到别的房间/别的棋谱窗口”的新问题。

## 范围与兼容约束

本次设计只修改 `readboard` 的 Fox 标题获取、缓存、刷新和主窗体标题更新策略，不修改：

- 对外协议行名与字段语义
- `lizzieyzy-next` 的消费逻辑
- 非野狐平台行为

显示范围：

- `野狐`
- `野狐(后台落子)`

非显示范围：

- `弈城`
- `新浪`
- `其他(前台)`
- `其他(后台)`

## 已确认问题

现有实现的问题不是“沿父窗口链找标题”本身，而是“每次采样都在 UI 线程里重新沿父链解析”：

- `Form1.CaptureSnapshotCore()` 当前会在每次快照采样时调用 `ResolveFoxWindowContext()`。
- `ResolveFoxWindowContext()` 会沿父窗口链逐级创建窗口描述并解析标题。
- 这条链路会重复执行窗口矩形、类名、标题、DPI 和进程 DPI awareness 相关读取。
- 同一时刻还会在 `CaptureSnapshotCore()` 内直接刷新主窗体标题。

结果是：

- `一键同步` 的轮询路径会卡
- `持续同步` 的高频采样会卡
- 是否勾选双向同步不影响该回退，因为问题发生在同步采样前半段

## 下游契约约束

`lizzieyzy-next` 侧已经把以下字段纳入同步决策：

- `foxMoveNumber`
- `roomToken`
- `liveTitleMove`
- `recordCurrentMove`
- `recordTotalMove`
- `recordAtEnd`
- `recordTitleFingerprint`
- `forceRebuild`

这些值的作用不只是显示，还包括：

- 决定是否进入 Fox 完整恢复路径
- 决定是否允许主线祖先扫描
- 决定 `resumeState` 是否失效
- 决定相同盘面下是否应当强制重建

因此必须保留以下协议语义：

- 即使棋盘 `Payload` 不变，只要 `foxMoveNumber` 或窗口上下文签名变化，仍然要让当前帧对下游可见。
- 不能把元数据刷新时机降级成“只有棋盘变化才刷新”。
- 不能把 `roomToken`、`recordTitleFingerprint`、`recordTotalMove` 改成纯展示字段。

## 设计原则

- 同盘一致性优先于性能优化：标题上下文必须来自当前棋盘句柄所在的同一父链。
- 性能优化只允许优化“获取方式”，不允许削弱“字段语义”。
- 不做全局窗口猜测，不从所有野狐窗口里挑“最像”的候选。
- 不新增独立轮询线程；所有刷新都复用现有同步生命周期和采样节奏。
- 主窗体标题更新必须与快照采样解耦，避免每帧都写 `this.Text`。

## 数据模型

### `FoxWindowBinding`

新增轻量绑定对象，只表达“当前棋盘句柄应该读哪个标题源句柄”，至少包含：

- `BoardHandle`
- `TitleSourceHandle`
- `WindowKind`

它不保存全局枚举结果，不保存多候选列表。

### `FoxWindowContext`

继续沿用现有结构：

- `RoomToken`
- `LiveTitleMove`
- `RecordCurrentMove`
- `RecordTotalMove`
- `RecordAtEnd`
- `TitleFingerprint`

### `MainForm` 本地缓存状态

`MainForm` 额外维护：

- 当前 `FoxWindowBinding`
- 最近一次成功解析的 `FoxWindowContext`
- 绑定是否需要重建
- 最近一次标题上下文签名
- 最近一次已应用到 `this.Text` 的标题字符串

## 两阶段获取策略

### 第一阶段：低频绑定

绑定负责解决“当前棋盘句柄对应哪一个标题源句柄”。

规则：

- 从当前棋盘句柄开始沿父窗口链向上查找。
- 找到父链上第一个能解析为 `LiveRoom` 或 `RecordView` 的窗口，就把它记为 `TitleSourceHandle`。
- 若整条父链都无法解析，则绑定失败，当前上下文为 `Unknown`。

触发绑定的时机：

- 选中窗口句柄变化
- 开始持续同步
- 开始一键同步
- 切换平台类型
- 重新完成选窗或框选绑定后
- 点击 `强制重建`
- 当前绑定已失效

### 第二阶段：高频轻量刷新

刷新负责解决“已绑定标题源窗口当前显示的房间号/手数是什么”。

规则：

- 在同步采样路径中，只对已绑定的 `TitleSourceHandle` 做轻量标题读取。
- 轻量读取路径只允许做：
  - 句柄有效性检查
  - 父链归属校验
  - `GetWindowText`
  - `FoxWindowContextParser.Parse(...)`
- 不再在这条路径上构造完整 `WindowDescriptor`。
- 不再在这条路径上做窗口 DPI / 进程 DPI awareness / 边界推导。

## 同盘约束

为了避免你担心的“房间观战窗口和棋谱窗口同时开着时串盘”，同盘约束固定为：

- 只承认“当前棋盘句柄到标题源句柄的父链关系”。
- 刷新时必须再次校验 `TitleSourceHandle` 仍然位于 `BoardHandle` 的父链上。
- 一旦失去父链关系，立即作废绑定并回到 `Unknown`。
- 不允许从其他顶层 Fox 窗口重新猜测新的标题源。

这意味着：

- 保留了“沿父链确保同盘”的正确性
- 去掉的是“每次都重走重型父链解析”的成本

## 主窗体标题策略

基础标题：

- `棋盘同步工具`

动态标题格式保持不变：

- 实时房间：`棋盘同步工具 [房间][43581号][第89手]`
- 棋谱页：`棋盘同步工具 [棋谱][第120/333手]`
- 棋谱末手：`棋盘同步工具 [棋谱][第333/333手][末手]`
- 同步中但未抓到：`棋盘同步工具 [野狐][同步中][未抓到标题信息]`

但刷新策略修改为：

- 不再在每次 `CaptureSnapshotCore()` 都直接拼标题并写 `this.Text`
- 只有以下情况才实际更新主窗体标题：
  - 同步状态变化
  - 平台类型变化
  - 标题上下文签名变化
  - 失败态与成功态切换
- 若格式化结果与当前窗体标题完全相同，则不重复写 `this.Text`

## 协议行为要求

为了兼容 `lizzieyzy-next`，协议层保持以下行为：

- `CaptureSnapshotCore()` 仍然要为当前帧提供 `foxMoveNumber`
- `sessionCoordinator.SetFoxWindowContext(...)` 仍然按当前缓存上下文更新
- `SyncSessionCoordinator.SendBoardSnapshot(...)` 仍然保留现有判重语义：
  - `Payload` 变了，发送
  - `foxMoveNumber` 变了，发送
  - 窗口上下文签名变了，发送
  - `forceRebuild` armed，发送

禁止退化成：

- 只有 `Payload` 变化才发送
- 标题相关字段只在显式重建或窗口切换时发送

## 失败态与失效规则

以下任一情况都立即失效并回到 `Unknown`：

- `hwnd == IntPtr.Zero`
- 当前平台不是野狐类型
- `TitleSourceHandle` 无效
- `TitleSourceHandle` 不再属于当前 `BoardHandle` 父链
- 标题为空或解析结果为 `Unknown`

约束：

- 不展示旧成功态兜底
- 不沿用旧绑定冒充当前窗口
- 不做全局窗口重猜

允许的恢复策略：

- 未绑定或绑定失效后，可以在现有同步采样路径上做节流重绑
- 不新增额外线程
- 不做隐藏后台扫描

## 实现结构

建议把现有实现调整为三块：

### 1. 纯格式化

继续由 `MainWindowTitleFormatter` 负责标题字符串拼接。

### 2. 绑定与轻量刷新

新增或拆分局部能力，例如：

- `FoxWindowContextBindingResolver`
- `FoxWindowTitleReader`

职责分别是：

- 低频建立 `FoxWindowBinding`
- 高频轻量读取已绑定标题源的文本

### 3. `MainForm` 统一入口

`MainForm` 只暴露统一的标题/上下文刷新入口，例如：

- `EnsureFoxWindowBinding()`
- `RefreshFoxWindowContextFromBinding()`
- `ApplyMainWindowTitleIfChanged()`

要求：

- `CaptureSnapshotCore()` 不再直接走重型父链解析
- `CaptureSnapshotCore()` 不再直接每帧刷新标题
- 其他位置不直接拼接标题片段

## 国际化

保持现有语言项，不新增新的展示标签键：

- `MainForm_titleTagFox`
- `MainForm_titleTagRoom`
- `MainForm_titleTagRecord`
- `MainForm_titleTagSyncing`
- `MainForm_titleTagTitleMissing`
- `MainForm_titleTagRecordEnd`

## 测试要求

### `readboard` 自动化测试

至少覆盖：

- 父链绑定能从棋盘句柄定位到正确标题源句柄
- 房间窗口与棋谱窗口同时存在时不会串盘
- 轻量刷新只读取已绑定标题源，不重新构造完整 `WindowDescriptor`
- 绑定失效后立即回到 `Unknown`
- `Payload` 不变但 `roomToken`、`recordTitleFingerprint`、`recordTotalMove` 或 `foxMoveNumber` 变化时，仍会发送一帧
- `CaptureSnapshotCore()` 不再在热路径中直接做重型标题解析与每帧标题写入

### 下游兼容验证

至少要继续满足 `lizzieyzy-next` 侧既有语义：

- `roomToken` 变化会导致旧恢复锚点失效
- `recordTitleFingerprint` 变化会导致旧恢复锚点失效
- `recordTotalMove` 变化会导致旧恢复锚点失效
- `foxMoveNumber` 与标题手数冲突时不会错误进入 Fox 恢复路径

### 手工验证

至少验证：

- `野狐` 和 `野狐(后台落子)` 下不再出现明显鼠标丢帧和卡顿
- 同时打开房间观战窗口与棋谱窗口时，不会把标题上下文串到另一盘
- 停止同步、切非野狐平台、关闭主窗体后，不残留动态标题

## 非目标

- 不修改 `lizzieyzy-next` 代码
- 不改变 readboard 对外协议行名
- 不新增独立窗口轮询器
- 不把房间号或标题骨架升格成棋局唯一 ID
- 不通过“降低元数据更新频率”来换性能
