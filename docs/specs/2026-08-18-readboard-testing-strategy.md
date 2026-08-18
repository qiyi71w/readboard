# ReadBoard 测试策略

日期：2026-08-18

状态：已按三步落地（PR CI、AutoPlay 签发合同、5x5 replay）。本文件仍不授权开 PR、打 tag 或接 LizzieYzy-Next。

对应架构合同：`docs/specs/2026-07-29-readboard-unified-target-architecture-design.md` 的 Testing Decisions。本策略分类并补洞，不另起一套测试树。

## Problem Statement

ReadBoard 已经有相当厚的验证层，但它们被一概叫成“单测”，真正的缺口被说成“缺 E2E”。两边都不对。

当前事实：

- 唯一的 C# 测试项目是 `tests/Readboard.VerificationTests`：`net10.0-windows`、WinForms、xUnit、`ProjectReference` 生产项目。约 78 个公开测试类，无 `[Trait]`、无 `Skip`、无 `Thread.Sleep`。
- 另有 `tests/WebView`：Playwright DOM（静态 Chromium）和真实 WebView2 host E2E（发布 `readboard.exe` + Evergreen Runtime + TCP fake host）。
- `ArchitectureContractFenceTests` 已经钉住 wire token、WebView state envelope、双格式配置、GMA 下一帧、Fox fail-closed、打包边界。
- Control Center、Settings Draft、Fox Identity Selection、Hosted Update 已是可测 module，不是隐藏 WinForms 控件仓库。
- `package-release.yml` 在 tag / dispatch 上跑完整 VerificationTests + benchmark。普通 PR **不跑** xUnit，也不跑 DOM Playwright。
- `webview2-host-e2e.yml` 在 PR（路径过滤）和 6 小时 cron 上跑真实 host E2E，不跑 VerificationTests。
- 识别 replay 只到 `PPM -> LegacyBoardRecognitionService -> re=` 棋盘行。没有一条测试把「识别结果 + 会话门闩 + 发出的 `play>`」串在同一条 transport 上。
- `CanSendAutoPlayCommand = keepSync && TwoWaySync && AutoPlayEnabled` 已有 8 格布尔测试。生产路径 `SendPlayCommandIfSelected()` 会查这个门闩，但 **transport 上从未断言**：自动落子开着、持续同步未开时不得出现 `play>`。

再堆 GUI E2E 填不了这个洞。`legacy/winforms` 和 WebView2 分叉只让整机点选更贵，不改变本仓该测的 seam。

## 范围

只覆盖 `ui/webview2-rewrite` 这一条维护线。

本策略管：

- 现有测试怎么分类、哪一层该投资
- PR / 日常 / release 各跑什么
- 最高风险缺口补哪一条合同
- 双产品线怎么避免第二套金字塔

本策略不管：

- LizzieYzy-Next 进程、假 GTP、跨仓联合 E2E
- 野狐 / 弈客真实窗口点击
- 为测试再造 `LegacyProtocolAdapter` 或 `SyncSessionCoordinator` 假实现
- 给 `legacy/winforms` 移植 WebView / Playwright

## 词汇

沿用 `CONTEXT.md` 的 Control Center、Settings Draft、Hosted Update、Fox Identity Selection、Semantic Message。

本文件额外使用：

| 词 | 含义 | 不要叫 |
| --- | --- | --- |
| **Module test** | 穿过一个 module 的 interface：intent / observation / result / effect / snapshot | unit test（除非它真的只测纯函数） |
| **Wire contract** | 对 `IReadBoardTransport` 发出或解析的逐行文本断言 | E2E |
| **Replay** | 用固定像素 / 快照驱动真实识别或 coordinator，断言协议行 | screenshot test |
| **Host smoke** | 启动真实 `readboard.exe` + WebView2 + fake host，只覆盖生命周期 | 全量 UI E2E |
| **Source-slice** | 锁定私有方法名、源码片段、HTML/CSS/JS 字面量或打包脚本文本 | 行为合同 |

`Readboard.VerificationTests` 是一个项目名，不是一层。项目里同时有 module test、wire contract、orchestration、replay 和少量 source-slice。

## 现有层（事实）

```text
Host smoke          tests/WebView/real-webview2-host.spec.js          7 条，串行，300s，Windows + Runtime
DOM rendering       tests/WebView/app-rendering.spec.js              静态 HTML + 伪造 chrome.webview
Release acceptance  benchmarks/Readboard.ProtocolConfigBenchmarks    协议解析 / 配置 / 识别 / 持续同步门槛
Orchestration       Protocol/SyncSessionCoordinator*                 真 coordinator + fake transport/host
Replay (partial)    Recognition + Capture fixture catalogs           5x5 PPM；不到 AutoPlay / play>
Module / contract   Control Center, Settings, Identity, Update,
                    Protocol adapter, Launch, Config, Placement
Source-slice        HighDpi / Title / WebViewUiPolish / 部分 Packaging
```

稳定 seam（已有生产 adapter + 测试 adapter，不要再加一层）：

- `ControlCenterRuntime` + session / persistence / action adapter
- `SettingsDraftRuntime`、`FoxIdentitySelection`、`HostedUpdateJourney`
- `IReadBoardTransport`（`RecordingTransport` / `FakeTransport` / JS TCP FakeHost）
- `IBoardCaptureService` / `IBoardRecognitionService` / `IMovePlacementService`
- WebView JSON：`TryParseWebViewCommand` / `SerializeWebViewState`（不是 COM host object）

不要发明的 seam：

- fake `LegacyProtocolAdapter`（wire 文本就是合同，测试用真 adapter）
- 为测试拆开 `SyncSessionCoordinator` 内部锁 / 队列 / generation
- 隐藏 WinForms 控件（Designer 可视树已空）
- 进程级 named-pipe fake Lizzie（本策略不接宿主；TCP FakeHost 已覆盖启动 / ready / close）

## 目标金字塔

投资顺序从上到下变贵，失败时先看下一层是否已能锁住同一合同。

1. **Module test**  
   Control Center intent、Settings Draft commit、Fox Identity fail-closed、Hosted Update 所有权、纯识别 / 颜色解析 / Launch / 配置事务。继续走现有 interface，不锁 `PostWebViewState` 调用点。

2. **Wire contract**  
   `ProtocolKeywords`、inbound/outbound fixture、`lastMoveSource` 顺序、GMA 尾 token、`stopAutoPlay`、hosted update 三字段 tab 行。`ArchitectureContractFenceTests` 保持为跨切面护栏，详细 case 留在 Protocol / Hosted Update 测试里。

3. **Orchestration**  
   真 `SyncSessionCoordinator` + fake transport/host。已覆盖 keep/one-time/continuous、Fox 房间撤权、`stopAutoPlay` 再武装、worker stop、inbound `place`。新测试优先走公开 API；能删 reflection 再删，不为分类而重写。

4. **Replay**  
   现有 5x5 PPM 继续锁识别金线。新增的一条必须跨到 transport：会话门闩 + 已识别棋盘 → 有或没有 `play>`。不要先追求「截图 → 识别 → 落子点击 → 同步 → AutoPlay」一条龙。

5. **DOM rendering**  
   保持薄：snapshot 动态文本、语言、校验、a11y、shell 等完整 state。不驱动真实 C#。

6. **Host smoke**  
   保持现有 7 条：首个权威 snapshot、版本/平台交换、Settings Save/Cancel、analysis 观察、shell close。不按按钮矩阵膨胀。

7. **Release acceptance**  
   tag 继续跑 VerificationTests + benchmark + 打包脚本。打包合同已由 `PackageReleaseScriptTests` 覆盖。

## 双产品线

| 线 | 测什么 | 不测什么 |
| --- | --- | --- |
| `ui/webview2-rewrite` | 本文件全部层 | 旧 WinForms 控件树 |
| `legacy/winforms` | 若回修，只跑该线已有的 Core / Protocol / 识别测试 | WebView JSON、Playwright、Control Center runtime、WebView2 zip 名 |

两边共用的合同仍是启动参数和 wire 文本。一边改 token，另一边只在真的 cherry-pick 时补同一条 wire 测试。不为 legacy 养第二套 host E2E。

## 必须补上的合同

历史回归形状（本仓内可独立锁死）：

> 自动落子已开，持续同步未开 → `IReadBoardTransport` 不得出现 `play>`。

现状：

- `ControlCenterRuntimeTests.Snapshot_CanSendAutoPlayCommandRequiresKeepSyncTwoWayAndAutoPlay` 只锁布尔。
- `MainForm.SendPlayCommandIfSelected()` 会查 `CanSendAutoPlayCommand(sessionCoordinator.KeepSync)`，未过门则返回；FoxAuto 还会 `RevokeAutoPlayIfAuthorized()`。
- `MainFormControlCenterSessionAdapter` 在 `AutoPlayEnabled` 从关到开时调用 `SendPlayCommandIfSelected()`，**自身不再查** `KeepSync`（换色 / 换落子方式那条路径会先查）。
- 现有 orchestration 的 `play>` 用例都先 `KeepSync`。

因此缺的不是 GUI E2E，而是一条 **composition / replay**：

1. 构造 `ControlCenterRuntime`（`AutoPlayEnabled = true`，双向同步开或关，颜色已知或未知）。
2. 构造真 `SyncSessionCoordinator` + `RecordingTransport`，`KeepSync` 为 false 或 true。
3. 走与生产相同的签发决策（`CanSendAutoPlayCommand` + 已知颜色才 `SendPlay`，否则 revoke）。
4. 断言 transport 行：无 `play>` / 有完整 `play>` / 仅 `stopAutoPlay`。

最低 case 集：

| KeepSync | TwoWay | AutoPlay | 颜色已知 | 期望 wire |
| --- | --- | --- | --- | --- |
| false | true | true | 是 | 无 `play>`；FoxAuto 可有一次 `stopAutoPlay` |
| true | false | true | 是 | 无 `play>` |
| true | true | false | 是 | 无 `play>`；关自动落子走 `stopAutoPlay` |
| true | true | true | 否 | 无 `play>`；revoke |
| true | true | true | 是 | 恰好一条完整 `play>`（含当前 GMA token） |

实现时优先复用现有 issuer，而不是再抄一份 `if`。若签发逻辑仍散在 `MainForm`，把它收到一个 internal issuer，让 session adapter 和测试走同一入口。这不是新业务 module，只是把 adapter 里不该拥有的协议授权 effect 放到可测 interface 上。不要为它引入第二套 coordinator。

可选第二条 replay（识别金线接到同一 transport）：用 `ReplayFixtureCatalog` 的 5x5 识别结果作为 `BoardSnapshot`，在「AutoPlay 开 + KeepSync 关」下 `SendBoardSnapshot`，断言只有棋盘行、没有 `play>`。先不要把真实 `Place` 点选加进去。

## CI

| 时机 | 跑什么 | 不跑什么 |
| --- | --- | --- |
| 本地小改 | 相关 `FullyQualifiedName~` 过滤 | host E2E、benchmark |
| 普通 PR | Windows：`dotnet test` 全部 VerificationTests；`npm run test:webview`（DOM）。两项都跑且挡合并 | benchmark；不要把 host E2E 塞进同一 job |
| PR host E2E | `webview2-host-e2e` Core 每条 PR 必跑且挡合并；Extended 继续在 PR/dispatch 跑，不挡合并 | 不要把 Extended 标成 required |
| 定时 | 已有 6 小时 host E2E | 不必再加 GUI 矩阵 |
| tag / 正式打包 | 已有 VerificationTests + benchmark + `package-readboard-release.local.ps1` | 不要用 host E2E 挡打包，除非 host smoke 当时是红的且改的是 shell 生命周期 |

新增的 PR 工作流只做 restore / build / test，不 publish、不打包、不上传 release 资产。`pull_request` 不再用路径过滤跳过 Verification Tests 和 Host E2E Core，避免 required check 缺失而卡住无关 PR。`push` 仍可按路径过滤。

VerificationTests 的 TFM 是 Windows。WSL 上的 `dotnet test` 结果必须标明「非 Windows」，不能当 PR 门。DOM Playwright 可以在有 Node 的环境跑；真实 host E2E 只能在原生 Windows checkout + Evergreen Runtime 上跑，且禁止从 WSL UNC 经 `npm.cmd` 启动。

第一轮不加 `[Trait]`。现有 release 已经整包跑 VerificationTests；PR 门与之对齐。只有出现稳定可复现的时序失败，才把 performance / 30s watchdog orchestration 标出去，而不是预先拆套件。

## 规则

- 最高稳定 seam 是测试面。新测试断言 intent、observation、result、effect 顺序和 transport / snapshot，不断言私有方法名。
- 异步用 generation 和 `VerificationCompletion`（30s watchdog）。不引入 `Thread.Sleep` 或固定 1 秒窗口。
- source-slice 只在还没有等价行为测试时保留；替代测试绿了再删。本策略不批量改写 `WebViewUiPolishTests` 或 DPI source 锁。
- 改 wire 文本必须同时改 `ProtocolKeywords`、契约测试，并在规格里记增量 token。本策略不改 wire。
- 识别 / 自动落子授权 fail-closed：未知颜色、旁观、非 Fox、身份不唯一 → 无 `play>`。
- GMA：旧三字段仍是首选模式；`gma` 只在约定位置；`play>` 之后必须再等下一帧权威识别。已有测试保留。
- Host smoke 继续：fresh publish 或 `READBOARD_PUBLISH_DIRECTORY`、独立 profile、单 worker、零 retry、失败上传 DOM / 截图 / TCP wire / 进程输出。

## 明确不做

- 不把「加大量 E2E」当下一阶段目标。
- 不启动 Lizzie，不测引擎独占弹窗，不测宿主安装 ReadBoard 包。
- 不自动化框选、放大镜、跨窗口真实点击。
- 不按平台 × DPI × 主题 × 语言展开 host E2E。
- 不把 VerificationTests 再拆成第二个 solution 项目来「看起来更像金字塔」。
- 不为测试把生产 internal 改成 public；继续 `InternalsVisibleTo`。

## 若进入实现

只做下面三步，做完再停。不顺手加假 GTP、跨仓 CI 或更多 Playwright 旅程。

1. **PR 门**  
   新增 Windows workflow：`dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj` + `npm run test:webview`。不打包。

2. **AutoPlay 签发合同**  
   按「必须补上的合同」在 VerificationTests 里加 transport 断言。若签发仍只活在 `MainForm`，先收到 internal issuer，再测 issuer。生产行为应保持：无 KeepSync 时启用自动落子仍然不发 `play>`。

3. **一条识别 replay → 无 `play>`**  
   复用 `foreground-5x5`，不要新截用户图。

完成标准：PR 上 VerificationTests 变红能挡住回归；「AutoPlay 开 + KeepSync 关」在 transport 上失败可复现；host E2E 条数不增加。

本文件不授权提交、推送、开 PR、打 tag 或改 release 通道。
