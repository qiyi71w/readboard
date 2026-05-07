# readboard 弈客平台支持设计

日期：2026-04-24

更新：2026-05-05

> 后续实现方向已按用户确认调整：弈客棋盘几何不再以 readboard 自己截图识别为唯一来源；`lizzieyzy-next` 会通过新增协议消息主动上报当前棋盘几何，readboard 主要用这份几何做后台落子。本文其余仍保留的"readboard 自己识别弈客棋盘"描述，以本更新说明为准。

## 目标

为 readboard 增加新的同步平台 `弈客`（Yike），覆盖弈客大厅、弈客直播、对弈与个人直播房间，以及无房间号的比赛页。要求：

- 在 readboard 侧引入一个新的 `SyncMode.Yike` 枚举值，行为对标 `SyncMode.FoxBackgroundPlace`（后台落子）。
- 棋盘矩形优先由 `lizzieyzy-next` 主动上报当前浏览器内棋盘几何；readboard 使用这份几何做后台落子，不引入手动框选。
- 房间号与手数通过协议扩展由 `lizzieyzy-next` 主动告知 readboard，避免在 readboard 侧 OCR 网页内容。
- 元数据（房间号、手数）必须像野狐一样进入 `SendBoardSnapshot` 的判重输入，否则下游恢复语义会退化。
- 比赛页无房间号场景必须正确表达为"未知房间"，不能伪造或沿用旧值。

## 范围与兼容约束

本次只在 readboard 增加弈客相关代码，并在与 `lizzieyzy-next` 之间扩展协议；不修改：

- 现有野狐 / 弈城 / 新浪 / Background / Foreground 的行为
- 现有协议中既有消息行的语义（仅新增）
- `lizzieyzy-next` 已实现的 URL 解析和 Socket.IO 行情逻辑（仅新增一条出站消息把已经解析好的字段传给 readboard）

显示范围：

- `弈客` （单一 SyncMode 值）

非显示范围：

- 不新增 `弈客直播` 与 `弈客大厅` 的两套 SyncMode；二者在 readboard 侧识别策略一致，仅窗口标题字面值不同。
- 不在 readboard 自己解析弈客 URL；URL 的房间号解析继续由 `lizzieyzy-next` 已有的 `OnlineDialog` 负责。
- 不做基于 OCR 的房间号或手数获取。
- 不做"通用棋盘检测"。

## 已确认的事实

- `lizzieyzy-next` 用 JCEF（`me.friwi:jcefmaven`）嵌入一个 CEF 浏览器，弈客窗口由 `BrowserFrame extends JFrame` 创建，标题为 `弈客大厅` 或 `弈客直播`，由用户从主菜单选择哪个入口决定，进入后不会切换。
- `BrowserFrame` 的 URL 来源于 `browser_.getURL()`；`OnlineDialog` 已实现房间号正则：`live/room/{roomId}` / `game/{gameId}` / 比赛页（无房间号）。
- 当前手数在 `lizzieyzy-next` 通过 Socket.IO 实时同步获取，已有内部状态。
- 弈客窗口地址栏是 Swing `JTextField`，不是原生 Win32 Edit 控件，外部 UI Automation 不能稳定读取。
- 弈客棋盘由网页 HTML/CSS 渲染，但 `lizzieyzy-next` 的 JCEF 注入能力可以直接读取房间页 DOM/canvas 几何，因此 readboard 不必再依赖截图猜测点击区域。
- `LegacyBoardLocator` 已有按平台扫描特征色锚点定位棋盘矩形的实现：野狐找 (49,49,49)、Tygem 找米黄、Sina 找 (251,218,162)。
- `BoardRecognitionResult.UsesAutoDetectedBounds` 当前白名单为 `Fox / FoxBackgroundPlace / Tygem / Sina`，需扩展加入 `Yike`。

## 设计

### SyncMode 与窗口匹配

- 在 `SyncMode` 末尾追加 `Yike = 6`，行为对标 `FoxBackgroundPlace`：
  - 后台落子（复用 `BackgroundSelectionWindowBindingCoordinator` 的现有路径）
  - 自动棋盘识别
  - 元数据进入下游判重
- 窗口匹配采用标题前缀白名单：`弈客大厅` 或 `弈客直播` 任一即视为弈客窗口。
- 窗口枚举与句柄绑定沿用 `LegacySyncWindowLocator` 现有机制，仅新增弈客的标题判定分支。
- 不为弈客新增 `WindowKind` 之类的细分枚举；弈客在 readboard 侧只有一种窗口形态。

### 棋盘几何

`lizzieyzy-next` 在弈客房间页通过 JCEF 注入读取当前棋盘 DOM/canvas 几何，并新增一条 host → readboard 协议消息：

```text
yikeGeometry left=<x> top=<y> width=<w> height=<h> board=<size>
```

- 坐标相对于 Chromium 渲染子窗口 client 区域。
- `board` 为当前路数，允许 5 路等非 19 路房间。
- readboard 在 Yike 后台落子时优先使用这份几何，并将点击目标收敛到 `Chrome_RenderWidgetHostHWND` 子窗口。
- 当宿主暂时拿不到可靠棋盘时，可发送裸 `yikeGeometry` 清空旧几何，避免 readboard 沿用 stale 坐标。

### 协议扩展（host → readboard）

新增一条由 `lizzieyzy-next` 发往 readboard 的入站消息行：

```
yike room=<roomToken> move=<moveNumber>
```

- `room`：房间号字符串。比赛页或未知场景下，整个 `room=...` 段缺省。
- `move`：当前手数，正整数。未知时整段缺省。
- readboard 接收到 `move<=0` 或无法解析为整数时，按未知手数处理，不向下游发送 `yikeMoveNumber`。
- 二者都缺省时，`yike` 行用于通知 readboard 当前进入的弈客上下文为"未知"。
- `lizzieyzy-next` 在以下时机发送 `yike` 消息：
  - 浏览器加载新 URL 后
  - Socket.IO 报告手数变更后
  - 用户切换大厅 / 直播页面或切换房间后
  - readboard 触发 `sync` 时由 `lizzieyzy-next` 主动重发当前快照

readboard 侧：

- 在 `ProtocolKeywords` 中追加 `Yike` 关键字常量，遵循现有协议常量收敛策略。
- 协议解析在 `LegacyProtocolAdapter` 等入站路径中识别 `yike` 行，更新当前会话的弈客上下文。

### 数据模型

新增 `YikeWindowContext`，结构对齐 `FoxWindowContext` 的精简子集：

- `RoomToken`（可空）
- `MoveNumber`（可空）—— 类名已含 `Yike` 前缀，类型成员不再重复
- `ContextSignature`：由上述字段拼出的稳定签名，用于判重

出站协议字段名仍为 `yikeRoomToken` / `yikeMoveNumber`（线上字段需要平台前缀以便下游分派）。

`YikeWindowContext` 只进入 `SyncSessionRuntimeState`，不进入 `SyncCoordinatorHostSnapshot`。上下文由入站 `yike` 协议消息写入 runtime state；快照采集路径不得把 MainForm 本地缓存重新写回 coordinator，避免 reset / 重连后旧上下文被回灌。

### 下游契约约束

`SyncSessionCoordinator.SendBoardSnapshot` 现有判重语义保留，并扩展为：

- `Payload` 变化 → 发送
- `foxMoveNumber` 变化（野狐路径）→ 发送
- 弈客上下文签名变化（`RoomToken` 或 `MoveNumber`）→ 发送
- `forceRebuild` armed → 发送

禁止退化为"只有 Payload 变化才发送"。`yikeMoveNumber` 的语义与 `foxMoveNumber` 平行，不复用同一字段名，避免下游误判平台。

`lizzieyzy-next` 侧的下游消费契约由 `lizzieyzy-next` 项目自行设计。本 spec 锁定 readboard 出站字段名：

- `yikeRoomToken`：与野狐 `roomToken` 平行
- `yikeMoveNumber`：与野狐 `foxMoveNumber` 平行

字段独立命名、不复用野狐字段，便于下游按平台分派。出现的具体行格式与协议常量在实现期间按 `ProtocolKeywords` 现有风格落地。

readboard 在弈客同步会话启动和停止时还会向 `lizzieyzy-next` 发送两条弈客专用控制命令：

```text
yikeSyncStart
yikeSyncStop
```

这两条只控制 `lizzieyzy-next` 内置弈客浏览器的网页同步任务，不替代旧的 `sync` / `stopsync`。旧命令继续表示 readboard 自身的棋盘同步状态。

`lizzieyzy-next` 内置弈客浏览器的停止同步按钮会反向发送：

```text
yikeBrowserSyncStop
```

readboard 只在当前平台为弈客时响应该命令，用它停止当前 readboard 同步会话，让两边的同步开关状态保持一致。

### 主窗体标题

复用 `MainWindowTitleFormatter` 既有结构，新增弈客分支，格式：

- 普通对弈或个人直播：`棋盘同步工具 [弈客][65191829号][第16手]`
- 比赛或未知房间：`棋盘同步工具 [弈客][第16手]`
- 同步中但未抓到上下文：`棋盘同步工具 [弈客][同步中][未抓到上下文]`

刷新策略沿用野狐设计：上下文签名变化或同步状态切换时才写 `this.Text`，不在每帧采样路径上更新。

### 后台落子

复用 `BackgroundSelectionWindowBindingCoordinator` 的 FoxBackgroundPlace 路径，在该协调器和 `SyncSessionCoordinator.Orchestration.cs` 内增加 `SyncMode.Yike` 分支即可，不引入新的协调器类。

### 国际化

新增四语言项（`language_cn.txt` / `language_en.txt` / `language_jp.txt` / `language_kr.txt`），命名遵循现有键风格：

- `MainForm_titleTagYike`
- `MainForm_syncMode_yike`（如平台下拉项使用）

不复用野狐的标签键。

## 失败态与失效规则

立即失效并把弈客上下文回到 `Unknown` 的条件：

- 当前平台不是弈客
- 句柄无效（`hwnd == IntPtr.Zero` 或窗口已销毁）
- 最近一次 `yike` 消息不是在当前弈客窗口句柄下接收的；用户重选窗口或自动发现到新窗口后，必须等待 host 为新句柄重发 `yike` 消息
- readboard 以协议行到达时的当前选中窗口句柄作为 `yike` 消息接收句柄；如果 UI 队列执行该命令时句柄已变化，丢弃该上下文并保持 `Unknown`
- 用户切换到非弈客平台
- `lizzieyzy-next` 未发送过 `yike` 消息或最近一次 `yike` 消息显式声明无房间号且无手数
- 同步会话停止、重新开始或内部同步缓存 reset 后，MainForm 本地缓存的弈客上下文也必须失效；新的同步会话必须等待 host 重新发送 `yike` 消息
- 协议断开重连后，重连前的弈客上下文不沿用，必须等新一条 `yike` 消息

内部同步缓存 reset 由 coordinator 通过 host 通知 MainForm 清理本地弈客缓存；快照采集不得把 reset 前的 MainForm 本地缓存重新写回 runtime state。

readboard 端在收到首条 `yike` 消息前的兜底：按"未知"处理（`RoomToken` 与 `MoveNumber` 均为 null），主窗体标题显示 `[弈客][同步中][未抓到上下文]`，棋盘识别与后台落子仍正常运行。

不允许：

- 旧成功上下文兜底
- readboard 自己用 OCR 兜底房间号或手数

## 测试要求

`readboard` 自动化测试至少覆盖：

- 标题前缀同时支持 `弈客大厅` 与 `弈客直播`，二者都进入弈客分支
- `LegacyBoardLocator.TryResolveYikeBounds` 在样张上能定位棋盘矩形（fixture 由实现期间补充）
- `BoardRecognitionResult.UsesAutoDetectedBounds(SyncMode.Yike)` 为 true
- `yike` 入站协议解析覆盖：完整字段、缺省 `room`、缺省 `move`、全部缺省、未知关键字行的健壮性
- `Payload` 不变但弈客上下文签名变化时 `SendBoardSnapshot` 仍发送一帧
- 协议断开重连后弈客上下文不残留
- 主窗体标题在弈客模式下按上下文签名节流刷新，不每帧写 `this.Text`

手工验证至少覆盖：

- 弈客大厅普通对弈房间：能识别棋盘、显示房间号与手数、后台落子可用
- 弈客直播个人直播房间：同上
- 弈客比赛页：棋盘识别可用，标题与协议字段对"无房间号"表现正确
- 切换平台到弈客或切回野狐 / 其他平台时，上下文不串

## 非目标

- 不修改 `lizzieyzy-next` 已有的 URL 解析或 Socket.IO 逻辑（仅在该项目侧新增一条出站协议消息）
- 不在 readboard 内 OCR 任何网页内容
- 不实现通用棋盘检测算法
- 不为弈客大厅 / 弈客直播拆分两个 SyncMode
- 不把弈客字段塞进野狐已有协议字段；弈客字段独立命名

## 配套改动（host 侧）

按 `CLAUDE.md` 中"改动影响接口或集成行为先更新 host 侧"的约定，本 spec 同步要求 `lizzieyzy-next` 项目：

- 在 `BrowserFrame` / `OnlineDialog` 适当位置新增 `yike` 协议消息的发送逻辑，输入为已解析好的房间号与当前手数。
- 与 readboard 同时上线；先行落地的一侧需在另一侧落地前保持向后兼容（readboard 收到未知行忽略；`lizzieyzy-next` 在尚未升级的 readboard 上不应因发送 `yike` 行而出错）。
