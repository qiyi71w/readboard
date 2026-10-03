# ReadBoard 开发说明

面向 ReadBoard 维护者，覆盖开发环境、运行时边界、代码结构、验证策略、宿主集成和发布流程。当前源码与测试是最终依据；`docs/specs/` 和 `docs/plans/` 中的历史设计文档可能早于当前实现。

## 项目与分支

ReadBoard 是 [LizzieYzy-Next](https://github.com/wimi321/lizzieyzy-next) 启动的 Windows 外接程序。它从第三方围棋客户端捕获棋盘、识别棋子，将状态通过逐行文本协议发送给宿主，并把宿主下发的落子转换为前台或后台点击。

仓库维护两条发布线：

| 分支 | 可见 UI | 版本线 | 支持系统 |
| --- | --- | --- | --- |
| `main` | WebView2 | v3.1.x | Windows 10 version 1809（build 17763）及以上 |
| `legacy/winforms` | WinForms | v3.0.x | 更早的 Windows |

本开发说明描述 `main`。简易版已经停止维护；WebView2 新功能不要回填到旧 WinForms 线，跨线修复应先在 `main` 落地，再按需移植。

## 核心边界

- 进程仍是 .NET 10 WinForms `WinExe`，WinForms 提供 HWND、消息循环、原生选择框和 WebView2 宿主；用户看到的主界面是 `readboard/WebView/` 中的 HTML/CSS/JavaScript。
- 截图、识别、同步、落子、配置、更新状态机和宿主协议都在 C#。JavaScript 只发送意图并渲染 C# 发布的权威快照。
- `Form1.Designer.cs` 只保留窗口 chrome。不要恢复隐藏 WinForms 业务控件，也不要把它们当状态源。
- 宿主协议的精确 wire 文本是兼容边界；C# 常量名不是协议。
- `readboard/Properties/AssemblyInfo.cs` 中的 `AssemblyInformationalVersion` 是发布版本源。
- 无有效宿主参数时程序直接退出，不显示 UI。

## 开发环境

必需环境：

- Windows 10 version 1809+ 或 Windows 11
- `.NET SDK 10.0.104`；`global.json` 固定该 SDK，并允许 `latestFeature` roll-forward
- PowerShell 7（命令使用 `pwsh.exe`）
- WebView2 Evergreen Runtime
- Node.js 20+ 与 npm，仅用于 WebView Playwright 测试

Visual Studio 或 Rider 可选，命令行流程不依赖 IDE。

可以在 WSL 编辑源码和运行纯 DOM 测试，但 Windows 应用构建、xUnit 验证、真实 WebView2 宿主 E2E、打包和桌面验收应在原生 Windows checkout 中执行。调用 Windows 工具时传 Windows 路径，不要让 `npm.cmd` 从 WSL UNC 工作目录启动真实宿主测试。

## 首次构建

以下命令在仓库根目录执行：

```powershell
dotnet restore readboard.sln --configfile NuGet.Config
dotnet build readboard.sln -c Debug
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --no-build
```

WebView DOM 测试：

```bash
npm ci
npm run test:webview
```

`npm run test:webview` 会安装 Chromium，直接加载静态 WebView 文件，验证权威 snapshot 的动态文本、语言切换、设置校验、弹层和 accessibility 行为；它不启动 `readboard.exe`。

## 启动与调试

### 宿主启动参数

入口是 `readboard/Program.cs`，解析器是 `readboard/Core/Models/LaunchOptions.cs`：

```text
readboard.exe yzy <aiTime> <playouts> <firstPolicy> <transport> <language> <tcpPort> [--log-dir <abs>] [--host-session-id <id>] [--logging-contract 1] [--diagnostics on|off] [--capture on|off]
```

| 位置 | 示例 | 含义 |
| --- | --- | --- |
| 0 | `yzy` | 固定启动标记；缺失或不同则直接退出 |
| 1 | `30` 或 `" "` | 自动落子每手用时 |
| 2 | `1000` 或 `" "` | 最大计算量 |
| 3 | `200` 或 `" "` | 首选计算量 |
| 4 | `0` / `1` | `0` 为标准输入输出 pipe，`1` 为 TCP |
| 5 | `cn` / `en` / `jp` / `kr` | 语言后缀；空值默认 `cn` |
| 6 | `-1` 或端口 | pipe 模式通常为 `-1`，TCP 模式为宿主监听端口 |

新宿主在 7 个位置参数后追加 named 日志参数。完整 contract launch 要求同时有 `--logging-contract 1`、绝对 `--log-dir`（ReadBoard 自有根，正常为 `WORK_DIR/logs/readboard`）和 `--host-session-id`。任何 present-but-incomplete、malformed 或相对路径都是 explicit unavailable，不走 LocalAppData fallback；legacy launch 只表示完全没有这些新参数。wire 文本见 `ProtocolKeywords` 的 `readboardLoggingV1` / `readboardLoggingSet` / `readboardLoggingObserved`。

持久化的 Settings 语言可以在初始化时覆盖宿主语言参数；这不改变参数格式。

### 启动顺序

`Program.Main` 的关键顺序：

1. `LaunchOptions.TryParse` 校验宿主参数。
2. 从可执行文件目录加载配置和语言资源，创建 `RuntimeContext`。
3. 启用 `HighDpiMode.PerMonitorV2`，检查 Windows 最低版本。
4. 创建 pipe 或 TCP transport，以及 `SyncSessionCoordinator`。
5. `MainFormRuntimeComposer` 装配捕获、识别、落子、overlay 和诊断依赖。
6. 检查 WebView2 Evergreen Runtime；缺失或过旧时显示原生下载/重试/退出对话框，不静默安装，也不回退到旧 UI。
7. `StartupProtocolHandshake` 启动会话、排空启动命令、发送 `ready`、重放启动状态。
8. 进入 `Application.Run(mainForm)`。

修改启动、握手或关闭顺序时，重点查看：

- `readboard/Core/Protocol/StartupProtocolHandshake.cs`
- `readboard/Core/Protocol/MainFormShutdownCoordinator.cs`
- `readboard/Core/Protocol/SessionCoordinatorScope.cs`
- `tests/Readboard.VerificationTests/Architecture/`

### 单独调 UI

无参数启动不会显示窗口。使用脚本模拟 pipe 模式宿主参数：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run-readboard-ui-debug.ps1
```

指定构建配置、语言或发布包中的程序：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run-readboard-ui-debug.ps1 -Configuration Release -Language en
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run-readboard-ui-debug.ps1 -ExePath "D:\path\to\readboard.exe"
```

也可以从 `cmd.exe` 调包装脚本：

```bat
scripts\run-readboard-ui-debug.cmd Debug cn "D:\path\to\readboard.exe"
```

脚本实际传入：

```text
readboard.exe yzy " " " " " " 0 cn -1
```

这个模式能检查窗口壳，但没有宿主对端。协议闭环使用真实 LizzieYzy-Next，或运行原生 Windows 的 fake-host E2E。

## 代码地图

| 路径 | 职责 |
| --- | --- |
| `readboard/Program.cs` | 入口、配置/语言初始化、系统与 WebView2 gate、transport 选择 |
| `readboard/Form1.cs`、`readboard/MainForm.*.cs` | 薄 WinForms 宿主、WebView bridge、协议和各 journey adapter |
| `readboard/Form1.Designer.cs` | 仅窗口 chrome；没有业务控件 |
| `readboard/Form2.cs`、`readboard/Form5.cs` | 原生棋盘选择 overlay 和放大镜 |
| `readboard/WebView/` | 随包发布的静态 UI；没有前端构建步骤 |
| `readboard/Core/ControlCenter/` | 实时偏好、session 投影、动作 enablement |
| `readboard/Core/Configuration/` | Settings Draft、JSON/legacy 双格式持久化 |
| `readboard/Core/Protocol/` | 同步状态机、握手/关闭、wire adapter、出站顺序 |
| `readboard/Core/Capture/` | 屏幕/窗口/PrintWindow 捕获和坐标投影 |
| `readboard/Core/Recognition/` | 棋盘定位、棋子分类、末手推断和 payload 复用 |
| `readboard/Core/Placement/` | 前台、后台、野狐和弈客落子 |
| `readboard/Core/AutoPlay/` | 自动落子授权、野狐身份与棋色识别 |
| `readboard/Core/WebView/` | snapshot 发布、窗口命令和更新检查 journey |
| `readboard/Core/Transport/` | pipe 与 `127.0.0.1` TCP 逐行传输 |
| `tests/Readboard.VerificationTests/` | xUnit 模块、wire、编排、fixture replay 和打包测试 |
| `tests/WebView/` | DOM Playwright 与真实 WebView2 host E2E |
| `fixtures/` | config、protocol、recognition、Yike 回放数据 |
| `benchmarks/` | 协议、配置和识别 release acceptance |
| `scripts/` | UI 调试与发布打包 |

`bin/`、`obj/`、`release/`、`release-runs/`、`temp-*`、根目录日志和 `debug-diagnostics/` 是生成物或诊断证据，不应作为源码提交，除非任务明确要求 fixture。

## WebView2 UI 契约

### JSON bridge 与 snapshot

主要入口：

- `readboard/MainForm.WebView.cs`
- `readboard/ReadBoardUiModels.cs`
- `readboard/Core/WebView/WebViewStatePublisher.cs`
- `readboard/WebView/app.js`
- `tests/Readboard.VerificationTests/Architecture/ArchitectureContractFenceTests.cs`

WebView 通过虚拟主机 `https://app.readboard/index.html` 加载静态资源。JavaScript 向 C# 发送 `{ type, payload }` 意图；C# 发布 `{ "type": "state", "payload": ReadBoardUiState }`。

必须保持以下规则：

- 每个语义事件最多发布一份完整 `ReadBoardUiState`，不用增量 patch。
- 无效 JSON 直接忽略；合法但被当前状态拒绝的意图返回一份未变的权威 snapshot；真正 no-op 不发布。
- 控件 enablement、持久化结果、协议状态和动态文本由 C# 计算。`app.js` 不从多个字段重新推导业务规则。
- 新 snapshot 字段必须同时进入 C# model/投影、`ArchitectureContractFenceTests` 和 `app.js` 渲染。
- 静态标签可由 JavaScript `t()` 处理；动态错误和日志使用 Semantic Message，由 C# 按当前语言解析。

### Control Center

`ControlCenterRuntime` 是平台、棋盘尺寸、双向同步、原棋盘显示、自动落子、棋色、落子方式和引擎条件的实时状态源；它也统一编排身份操作、授权证据失效、重新求值及直接请求。

实时偏好的提交顺序：

1. 合法 intent 立即更新当前进程值。
2. 失效旧证据并投影窗口/session；依次执行双向同步与重发、原棋盘显示、直接自动落子请求/停止、引擎参数效果。
3. 最后调用 `TryPersist` 一次。持久化失败不回滚进程值或已执行效果；snapshot 标记未保存并带错误。
4. 不自动重试，后续偏好修改或正常退出可以再次保存；非预期效果异常直接传播，不继续保存。

一次性动作通过 `ControlCenterActionAdapter` 调用 coordinator。不要从 DOM 或 `MainForm` 增加第二套 enablement/动作状态机。

自动落子授权的边界：

- Runtime 管启用周期、当次选择与接受的观察；`FoxIdentitySelection` 管身份和房间代次；`FoxMatchBarLiveRecognition` 管玩家名单缓存；coordinator 管同步 operation、宿主授权账本与出站串行化。
- `IControlCenterEnvironment` 返回句柄、目标有效性、完整窗口 context、绑定失效事实、玩家行和时间，并完成原生投影。平台变化先重新读取目标有效性（无句柄为 null，失效句柄为 false），再发布最终状态。它不决定授权，不从标题绘制回调授权政策。
- 首次无身份选择 FoxAuto 只打开身份弹层，保留当次手动选择；取消不改选择。确认由 Runtime 一次完成选人、使用/保存、失效、求值与直接请求。保存身份失败仍使用进程身份并关闭弹层；清除保存失败保留原保存身份。
- 窗口句柄、绑定失败、房间/状态、RecordView 的手数/终局/标题以及新启用周期都会失效识别证据。已知名单缓存到失效为止；未知名单沿用 1000ms 重试间隔。窗口失效本身不结束启用周期。
- 周期捕获在 UI thread 调用 `RefreshAutoPlayColor` 取得事实，不直接签发；`RequestAutoPlay` 经 `AutoPlayWireIssuer` 执行 force 请求，周期发送仍由 coordinator 去重和撤销。构造只接线，初始化投影不发 wire，启动重放与 keep-sync 重发由 Runtime 编排。
- 周期刷新同时返回棋色、已知状态或授权状态是否变化；与同次捕获的窗口 context 变化合并后，只请求一份最终 snapshot。上下文不变时的重新识别也必须发布；三项授权事实和上下文均不变时，捕获本身不额外发布。
- 验证使用真实 Runtime、身份模块与 coordinator，加记录 transport 和原生事实 fixture；不在测试里补发生产应执行的停止或签发。`ControlCenterAuthorizationTests` 覆盖状态/wire/失败，真实 WebView2 + FakeHost 覆盖桥接、线程和最终 snapshot；后者不代替真实 Fox 对局验收。
- `real-webview2-authorization.spec.js` 用独立原生 HWND/UI Automation 输入窗口，经生产 locator 和玩家读取器验证选人、Use Once、Save and Use，以及目标销毁后的平台切换。测试观察真实 `chrome.webview` 完整消息、落盘配置和 wire，不注入候选或 renderer state；外部窗口输入 fixture 不等于真实 Fox 对局。

### Settings Draft

`SettingsDraftRuntime` 拥有设置页草稿。Update 和 Reset 只改草稿；Cancel 从最新活动配置重建；只有 Save 才提交。

Save 的顺序固定为：校验 → overlay 到最新活动配置 → 持久化 → 替换活动配置 → 应用 language/theme/background-analysis effect → 发布。校验或持久化失败时不能执行 effect。Settings Save 不得改变主窗口 client size，也不得重新引入旧 `ApplyMainFormUi` 布局路径。

### 窗口、DPI 与原生 surface

主窗口使用 Per-Monitor V2 DPI。改窗口尺寸、chrome、弹层、选框、截图或点击坐标时检查：

- 100% 以外缩放；
- 多屏、跨屏和负坐标；
- 保存/恢复窗口位置；
- logical/client/screen/window 坐标转换；
- `Form2` / `Form5` 原生选择 surface；
- Settings Save 前后 client size 不变。

自动化覆盖集中在 `Host/HighDpiSourceRegressionTests.cs`、`Display/`、`Capture/`、`Placement/`；真实客户端窗口和多屏 DPI 仍需要桌面验收。

## 同步、识别与自动落子

共享管线是：

```text
Capture -> Recognition -> BoardSnapshot -> OutboundBoardSnapshotEmitter
```

`SyncSessionCoordinator` 管 transport 生命周期、锁保护的 session state、出站 dispatcher 和 payload 去重；`SyncSessionCoordinator.Orchestration.cs` 管一次同步、持续同步、落子、停止/清盘和诊断。

关键不变量：

- `StopSyncSessionAndClearBoard` 是停止同步并清盘的单一协调操作，避免旧 worker 在清盘后继续发送 snapshot。
- 全黑或全白识别结果无效，不发送棋盘 payload，也不授权自动落子。
- 棋盘未变化时通常抑制重复 payload；窗口 context、`forceRebuild`、Fox move number 或 `lastMoveSource` 变化仍可触发发送。
- `lastMoveSource` 的视觉可信来源只有 `redBlueMarker` 和 `foxCornerFlip`；不要把启发式 `deviation` / `stoneCount` 当作 GMA 回合真值。
- 弈客的 host geometry 用于落子和像素尺寸，不替代从截图定位棋盘。
- `play>` 只能由 `AutoPlayWireIssuer` 在 keep-sync、双向同步、自动落子和已知棋色都满足时发送；未知身份、观战或歧义必须 fail closed。
- 实际发送 `stopsync` 时，在同一出站串行边界撤销已发送的自动落子授权缓存并推进授权代际；界面勾选状态不是宿主仍有授权的证据。重启同步后，只有当前快照仍满足授权条件才重新发送一次 `play>`；稳定帧不重复授权，已关闭的自动落子不因重启而恢复。普通落子与 GMA 使用同一授权缓存生命周期，不改变各自的 wire 格式。

线程包括 UI thread、transport reader、持续同步 worker、串行落子队列和诊断 writer。新异步观察应携带 generation，并忽略过期结果；不要用 `Thread.Sleep` 固化时序。

连续发现窗口的 worker 必须沿用自己的生命周期与 session ID；启动棋盘采样分配新观察代际时，同一连续发现生命周期也推进到该代际，准备失败后的重新发现不能继续提交旧代际。读取代际与更新截图侧选定 HWND 必须在 worker 锁内完成有效性检查，不能让停止前的回调借用重启后的代际。

`MainForm.UpdateSelectedWindowHandle` 的 `NoOp` 只表示 Control Center 投影未变，不表示 HWND 未变；当前有效观察仍须切换实际窗口并清除旧标题绑定，`Stale` 仍拒绝。标题读取与截图必须指向同一选定窗口。

### 双向落子的截图确认

`PendingMoveState`（`readboard/Core/Models/SessionState.cs`）拥有请求入队、点击次数、截图观察窗口、整体期限及结果消费的状态转换。已完成但未消费的结果仍占用请求槽位。`SyncSessionCoordinator` 保留锁、worker、等待信号、几何准入及实际点击调用，协议响应仍通过 `HandlePlaceRequest` 返回。

`MoveVerifyMaxAttempts` 是物理点击的总次数上限：`1` 不重试，`2` 最多初次点击加一次重试；`0` 归一化为 `1`。它不代表截图观察次数。

- 开启落子验证时，每次成功发出点击后保留 500 毫秒的截图确认窗口。窗口内持续采样；看到目标交叉点已有棋子即可成功，旧盘面不立即判失败，也不再次点击。
- 窗口到期后，只有有效截图仍未看到目标棋子，才允许消耗剩余点击次数；次数耗尽则报告失败。截图或识别不可用不授权重试。
- 整次请求从入队起有 2 秒的确认期限，由单调时钟计时；等待结果的一方也检查期限，避免没有截图时无限等待。到达期限时优先判超时，即使同一时刻截图已看到目标棋子。
- 关闭验证时以点击执行结果结清。停止同步或取消请求会将排队／等待截图的请求结清为 `NoResponse`，不发送 `error place failed` 或 `placeComplete`；宿主通过 `stopsync` 退役确认权限，避免取消产生的失败回包先于停止通知删除本地棋子。取消结果在消费前仍占用请求槽位，也不会因晚到截图或期限到达而变成成功／失败。
- 已完成的真实成功、失败及超时结果不被后来的取消覆盖。正在执行的系统点击不提前结清；该点击返回后按原有执行结果结清。协议文本及宿主“匹配棋盘帧才是 ACK”的规则不变，重连仍以实际远端盘面为准。

时序回归集中在 `PendingMoveStateTests`，使用可控单调时钟覆盖延迟显子、重试边界、无有效截图超时、结果消费和停止／取消，不用 `Thread.Sleep` 等待目标时序。`PlaceRequestExecutionResultTests` 使用受控截图与点击适配器，经真实 `HandlePlaceRequest` 和 worker 验证响应、重试及停止边界；coordinator 编排测试保留会话停止／重启覆盖。这些验证不替代原生窗口点击验收。

## 宿主协议边界

`readboard/Core/Protocol/ProtocolKeywords.cs` 集中 wire 字符串，`LegacyProtocolAdapter.cs` 负责解析和生成。传输是 UTF-8 逐行文本。

修改协议、参数、包名、通道 schema 或发布目录时：

1. 保持 wire 文本逐字兼容，或同步修改 LizzieYzy-Next。
2. 更新 `ProtocolKeywords`、adapter、dispatcher/emitter。
3. 更新 `tests/Readboard.VerificationTests/Protocol/` 和必要的 `fixtures/protocol/`。
4. 核对宿主仓库 `src/main/java/featurecat/lizzie/analysis/ReadBoard.java`。
5. 跑协议与 transport focused tests。

```powershell
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~Protocol|FullyQualifiedName~Transport"
```

棋盘 snapshot 出站顺序不可随意调整：窗口 context → 可选 `forceRebuild` → 可选 `foxMoveNumber` → `lastMoveSource` → 棋盘行 → `end`。

WebView2 托管更新还要求宿主声明 `readboardUpdatePackageV2Supported`。ReadBoard 只负责检查、下载、SHA-256/ZIP 校验并发送 `readboardUpdateReady`；收到该 handoff 后，替换、回滚和重启归宿主所有。

## 配置

运行目录有三份配置：

- `config.readboard.json`：主配置；
- `config_readboard.txt`：旧主配置镜像；
- `config_readboard_others.txt`：旧扩展配置镜像。

`DualFormatAppConfigStore` 优先读 JSON；无可用 JSON 时导入匹配 machine key/protocol version 的旧配置。损坏 JSON 会隔离为 `.corrupt.<guid>`。

保存流程：

1. 在 `.readboard-config-transaction-*` 目录写完 JSON 和两份 legacy 内容。
2. 备份现有目标，再逐个替换三份文件。
3. 普通进程内替换失败时尝试恢复旧集合。
4. 成功或可恢复失败清理事务目录。
5. 回滚失败或事务目录清理失败抛出 `DurableConfigurationException`，并保留目录供诊断。

这不是跨文件的文件系统原子事务；进程崩溃仍可能留下混合集合。新增配置字段时必须明确由 Control Center 还是 Settings Draft 拥有，并更新 `AppConfig.CreateDefault`、JSON/legacy 映射和配置测试。

## 验证策略

先用最便宜、最贴近变更契约的层级：

| 层级 | 位置 | 适用范围 |
| --- | --- | --- |
| 模块测试 | `tests/Readboard.VerificationTests/Host`、`AutoPlay`、`Configuration` | intent、observation、effect、失败语义 |
| Wire/架构 fence | `Protocol/`、`ArchitectureContractFenceTests` | 精确协议文本、snapshot envelope |
| Coordinator 编排 | `Protocol/SyncSessionCoordinator*Tests` | transport、并发、停止/清盘、出站顺序 |
| Fixture replay | `Capture/`、`Recognition/`、`Placement/` | 像素、识别、平台落子路径 |
| DOM Playwright | `tests/WebView/app-rendering.spec.js` | HTML/CSS/JS snapshot 渲染 |
| 真实 host smoke | `tests/WebView/real-webview2-host.spec.js`、`real-webview2-authorization.spec.js` | `readboard.exe`、Evergreen、TCP fake host、原生输入/身份确认、CDP 生命周期 |
| Release acceptance | `benchmarks/Readboard.ProtocolConfigBenchmarks` | tag/打包与识别性能门槛 |

常用 focused tests：

```powershell
# 启动、握手、关闭
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~Launch|FullyQualifiedName~StartupProtocolHandshake|FullyQualifiedName~MainFormShutdown"

# WebView bridge、Control Center、Settings Draft
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~WebViewBridge|FullyQualifiedName~ControlCenter|FullyQualifiedName~SettingsDraft|FullyQualifiedName~ArchitectureContractFence"

# 捕获、识别、落子
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~Capture|FullyQualifiedName~Recognition|FullyQualifiedName~Placement"

# 自动落子与野狐身份
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~AutoPlay"

# 更新、配置、打包
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~HostedUpdate|FullyQualifiedName~GitHubUpdate|FullyQualifiedName~Configuration|FullyQualifiedName~Packaging"
```

异步测试使用 `VerificationCompletion` 的有界等待，不要增加 `Thread.Sleep`。

自动落子故障序列（[#87](https://github.com/qiyi71w/readboard/issues/87)）：

```powershell
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~AutoPlayFaultSequenceTests" --logger "console;verbosity=detailed"
```

`AutoPlayFaultSequenceHarness` 使用真实 `ControlCenterRuntime`、coordinator 和 `LegacyProtocolAdapter`，只控制窗口、棋手、时钟、捕获／识别与外部 I/O。显式识别屏障覆盖未知棋色／keepSync 抖动及恢复、关闭后的旧结果、目标切换后的旧 observation；每个案例输出事件、状态和 wire 顺序。同步启动 prime 不计入 worker 步数，停止必须等实际 host 停止回调，不能把 flags idle 当成 worker 已退休。

直发未知棋色不发新 `play>`，也不发送 `stopAutoPlay`；周期 FoxAuto 未知样本可撤销棋色授权，但不结束启用周期。两条路径分别验证，不统一授权规则。

真实 EOF 案例用 loopback TCP 宿主半关闭发送方向，等待生产 transport 的断链通知与协议会话关闭，再释放在途识别；最终状态已停止，关闭后的完整 wire 不得增加。该案例在独立的 `dotnet vstest` 子进程运行，未捕获的 worker 异常只让该案例失败，不中止其他案例；失败保留完整 transcript，不跳过测试或降低断言。

EOF 子进程启用 `RunConfiguration.TreatNoTestsAsError=true`，没有匹配用例时必须失败；父测试读取 TRX，要求 `total`、`executed`、`passed` 均为 1，不能只凭退出码或本地化控制台摘要判定成功。TRX 写入每次运行独立的临时目录，成功、失败或超时后都清理该目录。

TCP 和 pipe 的非预期 EOF／I/O 失败使 transport 先变为 disconnected，再在 transport 锁外同步通知 coordinator。订阅者在该连接的终止转换时固定；coordinator 的断链回调绑定到启动代次，旧连接通知即使跨过重启才送达，也不能关闭新会话。当前会话断链时 coordinator 立即关闭出入站入口、取消在途任务，再调度现有宿主退出路径；主动 `Stop`／`Dispose` 不触发非预期断链通知。不增加自动重连，也不改变 wire 文本或自动落子授权规则。该生命周期收口修复了基线 `d8078b75724f7f8563f7ddfaf733de71dae01781` 在 EOF 后写已释放 `NetworkStream` 的 `ObjectDisposedException`。

确定性测试不等同于真实 Fox 客户端／在线对局验收。原生退出 smoke 使用隔离候选中的真实 `readboard.exe`：收到 version 响应后注入 TCP 半关闭或 stdin EOF，进程应以退出码 0 结束。

### 真实 WebView2 host E2E

仅在原生 Windows checkout 运行，需要 Evergreen Runtime 和 Node 依赖：

```powershell
$env:DOTNET_EXE = "C:\path\to\dotnet.exe" # PATH 中的 dotnet 正确时可省略
npm run test:webview:host:core
npm run test:webview:host:extended
```

- `core`：首个权威 snapshot、version/platform 交互、Settings Save 后重启持久化。
- `extended`：Settings Cancel、analysis 权威观察、shell close 和有序 shutdown。
- 两组测试串行、单 worker、零 retry。
- `READBOARD_PUBLISH_DIRECTORY` 可指向已经 publish 的目录，避免重复构建；未设置时会 fresh publish。
- 失败产物在 `test-results` / `playwright-report`，包含 DOM、截图、console/page errors、TCP wire、进程输出、配置和 cleanup 状态。

只有首 snapshot、Settings save/restart、analysis observation、shell close 或真实 WebView2 生命周期变化才需要这层；不要把它扩成完整按钮矩阵。

### Benchmark acceptance

识别阈值、识别分配/性能、协议/配置 release acceptance 或正式发布时运行：

```powershell
dotnet run --project benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj
```

## CI

| Workflow | 触发 | 内容 |
| --- | --- | --- |
| `.github/workflows/ci.yml` | PR、匹配路径的 push、手动 | Windows 2022；完整 VerificationTests；WebView DOM Playwright |
| `.github/workflows/webview2-host-e2e.yml` | PR、手动 | 一次 Release publish；core 后运行 extended；Evergreen、单 worker、零 retry |
| `.github/workflows/package-release.yml` | `v*` tag、手动 | VerificationTests、benchmark、打包；tag 时发布 GitHub Release |

真实 host E2E 不在普通 `ci.yml` 中。tag release workflow 也不代替 host E2E。

## 打包与发布

唯一打包入口是 `scripts/package-readboard-release.local.ps1`，不要手写 publish/copy/zip 流程。

只生成 release 目录：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-readboard-release.local.ps1 -SkipZip
```

生成 GitHub Release 用 ZIP：

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-readboard-release.local.ps1
```

脚本执行 self-contained `win-x64` Release publish。v3.1.0+ 资产名为：

```text
release/readboard-webview2-vX.Y.Z.zip
release/readboard-webview2-vX.Y.Z.zip.sha256
```

发布目录内的应用位于 `readboard/` 子目录。必需内容包括 .NET 应用文件、四份语言文件、RTF 说明、OpenCvSharp native 依赖、WebView2 assemblies/loader 和完整 `WebView/` 静态资源。发布包不得携带 WebView2 Fixed Version Runtime（`msedgewebview2.exe`）；运行时使用系统 Evergreen。

脚本会打印：

- `PackageDir`
- `PackageZip`
- `PackageVersion`
- `PackageSha256`
- `PackageChecksumFile`

`-SkipZip` 会删除同名旧 ZIP 和 checksum，避免误用旧产物。

正式发布前：

1. 同步 `AssemblyVersion`、`AssemblyFileVersion`、`AssemblyInformationalVersion("vX.Y.Z")`。
2. 新增非空 `docs/releases/vX.Y.Z.md`，首个非空行必须是 `# vX.Y.Z`。
3. 运行 VerificationTests 和 benchmark acceptance。
4. 用打包脚本生成 ZIP，核对版本、资产名、`PackageDir`、SHA-256 和本次时间戳。
5. 推送同名 `vX.Y.Z` tag；workflow 校验 tag、informational version、changelog 和资产前缀一致。
6. Release 发布后，另开 PR 更新 `update-channels.json` 的 `latestTag`、`assetName` 和 `sha256`。客户端只读通道 manifest，不使用 GitHub `/releases/latest` 回退。

## 与 LizzieYzy-Next 对接

宿主侧入口：

```text
src/main/java/featurecat/lizzie/analysis/ReadBoard.java
```

以下改动必须同步核对宿主：

- 启动参数或 transport 选择；
- wire token、字段顺序或 capability handshake；
- `play>` / GMA 语义；
- Hosted Update handoff、包名前缀或通道 schema；
- release 目录结构、可执行文件名或必需文件。

宿主默认在工作目录的 `readboard/readboard.exe` 或 `readboard/readboard.bat` 查找外接程序。ReadBoard 与宿主跨仓库变化应先明确 wire 和包结构契约，再分别验证。

## 变更检查表

提交前只跑与改动对应的最窄验证；跨共享边界再扩大：

- UI snapshot/command：C# model + bridge + architecture fence + `app.js`；静态渲染变化加 DOM Playwright。
- Control Center：intent、snapshot、enablement、持久化失败语义和 adapter effect。
- Settings：Update/Reset/Cancel/Save、validation、persist failure、effect order、client size。
- 协议：精确 wire、fixture、出站顺序、宿主 `ReadBoard.java`。
- 捕获/识别/落子：平台 fixture、DPI/坐标、无效 snapshot、payload reuse；算法性能变化加 benchmark。
- 自动落子：`CanSendAutoPlayCommand` / `AutoPlayWireIssuer` 授权矩阵，未知身份和棋色必须不发 `play>`。
- 配置：默认值、JSON、legacy mirrors、Control Center 或 Settings 唯一所有权、失败/回滚测试。
- 打包/更新：脚本与 workflow 同步、WebView2Loader、无 Fixed Runtime、资产名、SHA-256、宿主 v2 capability。
- 窗口与桌面行为：非 100% DPI、多屏、真实目标客户端和宿主启动的人工验收。
