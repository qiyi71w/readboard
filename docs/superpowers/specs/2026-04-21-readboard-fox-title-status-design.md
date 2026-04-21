# readboard 野狐同步标题状态提示设计

日期：2026-04-21

## 目标

在不新增第二套窗口抓取链路的前提下，让 `readboard` 在选择野狐同步类型并开启同步后，于主窗体标题动态显示当前抓取到的野狐窗口上下文，方便快速确认抓取是否成功。

核心目标：

- 标题显示与当前未提交改动中发往 `lizzieyzy-next` 的野狐窗口上下文保持同源。
- 同时支持野狐实时房间和野狐棋谱回放页。
- 明确区分成功态与失败态，避免标题持续显示过期房间号或过期手数。
- 不改变现有协议语义，不为标题提示再新增独立轮询线程或窗口扫描逻辑。

## 范围

本次设计只覆盖 `readboard` 主窗体标题提示行为，不新增新设置项，不修改对外协议格式，也不改变 `lizzieyzy-next` 的接入方式。

显示范围：

- `野狐`
- `野狐(后台落子)`

非显示范围：

- `弈城`
- `新浪`
- `其他(前台)`
- `其他(后台)`

## 设计原则

- 标题数据只保留一个来源：复用 `Form1` 中现有的 `ResolveFoxWindowContext()` 解析结果。
- 标题展示只在“当前为野狐类型且同步已开启”时生效，其他情况恢复基础标题。
- 失败态必须覆盖旧成功态；当当前抓取失败时，不能继续显示上一次成功解析出的房间号或手数。
- 标题拼接逻辑抽成纯函数，便于测试和回归验证。

## 数据来源与状态模型

### 数据来源

沿用当前未提交改动中已经存在的链路：

- `Form1.CaptureSnapshotCore()` 调用 `ResolveFoxWindowContext()`
- `ResolveFoxWindowContext()` 基于已选窗口句柄读取野狐窗口标题
- `FoxWindowContextParser` 解析出：
  - `LiveRoom`
  - `RecordView`
  - `Unknown`

可复用字段：

- `RoomToken`
- `LiveTitleMove`
- `RecordCurrentMove`
- `RecordTotalMove`
- `RecordAtEnd`

### 主窗体状态

`MainForm` 维护轻量标题显示状态，至少包含：

- 基础标题文本：来自 `MainForm_title`
- 当前是否应显示动态标题
- 最近一次解析得到的 `FoxWindowContext`
- 当前是否拥有有效的野狐窗口句柄

不引入新的后台状态机，不将标题显示职责下沉到 `SyncSessionCoordinator`。

## 标题格式

基础标题：

- `棋盘同步工具`

动态标题格式：

- 实时房间：`棋盘同步工具 [房间][43581号][第89手]`
- 棋谱页：`棋盘同步工具 [棋谱][第120/333手]`
- 棋谱末手：`棋盘同步工具 [棋谱][第333/333手][末手]`
- 同步中但未抓到：`棋盘同步工具 [野狐][同步中][未抓到标题信息]`

格式规则：

- 实时房间使用 `[房间]` 标识。
- 棋谱页使用 `[棋谱]` 标识。
- 棋谱页在 `RecordAtEnd = true` 时附加 `[末手]`。
- 失败态不展示旧房间号或旧手数。

## 刷新时机

### 正常刷新

以下事件触发标题刷新：

- `CaptureSnapshotCore()` 每次获取或刷新当前 `FoxWindowContext` 后
- 开始持续同步
- 开始一键同步
- 停止同步
- 切换平台类型
- 更新已选窗口句柄
- 重新完成选窗或框选绑定后

### 强制重建

`强制重建` 需要作为明确的标题刷新点：

- 点击 `btnForceRebuild` 时，先立刻基于当前 `hwnd` 和当前平台重新解析一次 `FoxWindowContext` 并刷新标题
- 随后的下一次 `CaptureSnapshotCore()` 再按正常链路刷新一次

这样可以同时覆盖：

- 用户点击重建后立即确认当前绑定目标是否正确
- 重建后真实抓取结果发生变化的场景

### 清空时机

以下情况立即恢复基础标题：

- 停止同步
- 切换到非野狐平台
- 主窗体关闭

## 失败态与误报控制

当满足“野狐类型 + 同步已开启”但出现以下任一情况时，显示失败态：

- `hwnd == IntPtr.Zero`
- 无法创建窗口描述
- `FoxWindowContext.Kind == Unknown`

失败态统一显示：

- `基础标题 + [野狐][同步中][未抓到标题信息]`

约束：

- 不保留旧成功态用于兜底展示
- 不对失败态做静默降级
- 不新增隐藏重试逻辑或额外容错线程

## 实现结构

建议新增两个局部能力：

### 标题格式化 helper

新增纯函数 helper，例如：

- `MainWindowTitleFormatter`

输入：

- 基础标题
- 是否启用动态标题
- `FoxWindowContext`
- 是否拥有有效句柄

输出：

- 最终标题字符串

该 helper 不直接依赖窗体控件，不直接访问全局状态。

### MainForm 标题刷新入口

在 `MainForm` 内新增统一入口，例如：

- `UpdateMainWindowTitle()`

职责：

- 决定当前是否应显示动态标题
- 在需要时重新解析当前窗口上下文
- 调用 formatter 得到最终标题
- 统一写入 `this.Text`

要求：

- 其他位置不直接拼接标题片段
- 标题更新逻辑集中，避免多处写 `this.Text`

## 国际化

新增标题附加标签语言项，不在代码中硬编码：

- `MainForm_titleTagFox`
- `MainForm_titleTagRoom`
- `MainForm_titleTagRecord`
- `MainForm_titleTagSyncing`
- `MainForm_titleTagTitleMissing`
- `MainForm_titleTagRecordEnd`

同步更新：

- `readboard/language_cn.txt`
- `readboard/language_en.txt`
- `readboard/language_jp.txt`
- `readboard/language_kr.txt`
- `Program.cs` 中默认语言项

## 测试要求

### 单元测试

为标题格式化 helper 增加纯字符串断言测试，至少覆盖：

- 房间标题
- 棋谱标题
- 棋谱末手标题
- 同步中但未抓到标题信息
- 停止同步时恢复基础标题

### 源码回归测试

补充或更新宿主边界类测试，至少验证：

- `MainForm` 使用统一标题刷新入口
- `btnForceRebuild_Click` 会触发标题刷新路径
- 标题相关新增语言项已接入

### 验证边界

本次不要求引入真实窗口端到端 UI 自动化，但需要保证：

- 标题显示与现有 `FoxWindowContextParser` 解析结果一致
- 选择非野狐平台时不会残留野狐状态标题
- 停止同步后不会残留动态标题

## 非目标

- 不新增用户配置项来开关标题提示
- 不修改 `lizzieyzy-next` 协议
- 不新增独立窗口轮询器
- 不为了标题提示重构 `SyncSessionCoordinator` 职责边界
