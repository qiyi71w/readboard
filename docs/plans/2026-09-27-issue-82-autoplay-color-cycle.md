# Issue #82：自动落子每次启用重新确认手动棋色

## 状态与范围

- 来源：[GitHub issue #82](https://github.com/qiyi71w/readboard/issues/82)。
- 分析基线：刚从 `origin/main` 获取的 `d6e38ae7af615dd7259a73dd79234f222d83e7df`。
- 工作树：`/home/dev/dev/weiqi/worktrees/readboard/issue-82-autoplay-color-20260927`。
- 分支：`plan/issue-82-autoplay-color`。
- 用户已回复“可以”批准实施。代码、聚焦验证及双轴审查已完成；真实 Fox 验收尚未完成，不代表 issue 全部验收或发布完成。
- 原主工作树有本地提交和未跟踪内容，保持原样；独立工作树直接从远程 main 创建，没有合并本地主分支。

## 目标与术语

每个“自动落子启用周期”从关闭转为开启开始，到再次关闭结束；关闭双向同步导致自动落子关闭，也结束周期。它不等同于一盘棋或持续同步任务。

区分三件事：

1. **持久棋色偏好**：记住用户最后使用的模式；不能证明本次允许执黑或执白。
2. **本次棋色选择**：手动黑、手动白、FoxAuto 或未选择。
3. **棋色授权**：本次明确选择了手动黑白，或当前野狐房间重新识别成功；真正发送仍必须满足持续同步、双向同步和自动落子等已有条件。

手动模式关闭后，黑白均回到未选择；重新开启不能产生 `PlayColor` 或发送 `play>`，直至用户再次选色。FoxAuto 可保持模式选择，但不能继承旧房间实际棋色。

## 已核实的现状

以下行号均对应分析基线。

| 环节 | 当前事实与影响 |
| --- | --- |
| 持久偏好 | `ControlCenterRuntime.cs:255-275,298-317` 从配置读取、向配置写入 `AutoPlayColorMode`。`AppConfig.cs:98,119-125` 默认及非法值回退均为 ManualBlack。 |
| Session | `ControlCenterRuntime.cs:150-209` 有 `AutoPlayEnabled`，没有独立的本次手动选择。 |
| 关闭路径 | `ControlCenterRuntime.cs:1234-1262` 中，关闭双向同步或自动落子会清除 Fox 上下文与识别结果，但不清手动偏好。 |
| 色彩解析 | `ControlCenterRuntime.cs:1537-1547` 开启后直接解析持久偏好；`FoxAutoPlayColorResolver.cs:12-15` 对 ManualBlack/ManualWhite 立即返回 Known。 |
| Intent 去重 | `ControlCenterRuntime.cs:1028-1042,1550-1563` 依赖 preference/session 相等性判断 NoOp。重新选择同一种手动棋色必须由 session 差异体现。 |
| 主窗体副作用 | `MainForm.ControlCenter.cs:43-105` 选色变化只比较 preference；开启时立即调用 `SendPlayCommandIfSelected()`。新增 session 选色必须进入差异检测。 |
| 直接签发 | `Form1.cs:127-143` 进入 `AutoPlayWireIssuer`；后者在 `AutoPlayWireIssuer.cs:23-35` 检查同步条件与 `IsKnown` 后发送。 |
| 持续同步签发 | `Form1.cs:578-598` 还把解析结果放入 `SyncCoordinatorHostSnapshot`；`SyncSessionCoordinator.Orchestration.cs:1627-1658` 会按 snapshot 授权。不能只修按钮触发的直接签发路径。 |
| WebView | `MainForm.WebView.cs:1321-1323` 输出只能是 auto/white/black；`WebView/app.js:377` 对空值回退 auto，而且只设置一个 radio，不主动清空全组。 |
| Fox 刷新 | `Form1.cs:459-465` 同时失效采样缓存、身份识别与 runtime observation；`FoxMatchBarLiveRecognition.cs:107-115` 清除已知结果；`FoxIdentitySelection.cs:326-347` 增加识别代次并拒绝旧代次结果。复用这些机制。 |
| 身份取消 | `MainForm.WebView.cs:989-994` 首次自动模式会先打开身份选择而不立即提交选色。`Form1.cs:77,313-319` 和 `MainForm.WebView.Identity.cs:100-103,318-328` 仍携带历史手动色恢复状态，需保证取消不会把历史值变成本次选择。 |

### 分析阶段运行证据

使用此工作树的 `index.html` 与生产 `app.js`，在隔离 Chromium 中经 `readboardPreview` 注入 snapshot；没有连接真实宿主或围棋客户端。

| 输入的 `controlCenter.color` | 实际 checked radio |
| --- | --- |
| `"black"` | black |
| `null`（在 black 之后） | auto |
| `"white"` | white |
| `""`（在 white 之后） | auto |

这证明未选态需要同时修改后端投影与前端渲染。浏览器默认启动遇到 relay 连接问题，改用明确的本地 Chromium；页面脚本未自动暴露 preview API，探针显式执行从本机 HTTP 读取的原始 `app.js` 后获得上述结果。该结果只证明渲染函数的空值行为，不是 WebView2 原生集成验收或完整视觉验收。临时服务器和浏览器已关闭。

分析阶段未运行 .NET 测试、Windows WebView2 或真实 Fox 验收；实施阶段结果见文末。

## 推荐设计

### 1. 在 runtime 表达本次选择，不迁移磁盘格式

给 `ControlCenterSessionState` 增加 nullable 的 `SelectedAutoPlayColorMode`，并在 runtime snapshot 中暴露同名的当前选择。

- 新 runtime：持久偏好为 FoxAuto 时初始化为 FoxAuto；ManualBlack/ManualWhite 均初始化为 null。
- 显式选色：更新本次选择；继续按现有偏好规则记住最后使用的模式。
- 关闭自动落子／关闭双向同步：撤销手动本次选择；FoxAuto 可保持为模式选择，同时清空实际识别授权。
- 真正由关闭转为开启：手动保持 null；FoxAuto 重新通过现有采样与识别路径取得当前房间结果。
- 重复 `SetAutoPlayEnabled(true)` 不是新周期，不应擦除已经有效的本次选择。
- `Clone`、构造约束、session 相等判断及 snapshot 投影一并更新。

`ResolveAutoPlayColor()` 只根据本次选择解析：未启用或 null 均返回 `Unknown(ColorUnknown)`；手动/自动有选择时才调用现有 resolver。不得通过持久偏好、enum 默认值或 `GetValueOrDefault()` 补回手动色。

现有非 nullable `AutoPlayColorMode` 可继续作为持久模式及 coordinator 模式元数据；UI 勾选和授权解析必须迁移到本次选择，不能以该元数据代替授权。`PlayColor` 与 `IsKnown` 保持由同一 resolution 派生。

**取舍**：采用 issue 提出的“session 级当前选择”方案，保留 JSON 与 legacy 文件中现有 0/1/2 数值，不新增 `None=3`，不更改 legacy token 位置。磁盘上的手动黑白只表示历史偏好，绝不恢复本次选择。彻底删除手动持久字段也是可行方案，但会扩大到配置迁移和旧版读取行为，本 issue 的安全目标不要求这项迁移。

配置保存、Settings Save/Cancel/Reset 不得把本次选择写入新的持久字段，也不得重新初始化当前 session 的选色。继续保存原偏好本身不是恢复授权。

### 2. 贯通两条签发路径和副作用

- `MainFormControlCenterSessionAdapter` 比较新 session 选择：null → 上次相同的黑/白也必须视为变化，并进入正常发送路径。
- 关闭仍通过现有 `SendStopAutoPlay()` 撤销宿主已授权状态，不以“未来不再发送”代替停止。
- `SendPlayCommandIfSelected()` 与 `CaptureSnapshotCore()` 均消费同一 runtime resolution。持续同步、恢复同步不能绕过未选态。
- 保留现有 coordinator 的 generation、去重和发送边界；不更改 `play>`、`stopAutoPlay` 文本或宿主参数。
- FoxAuto 复用当前缓存失效及识别代次机制；同房间停启也必须重新采样，而不只是换房间时刷新。
- 身份选择取消保持打开弹窗之前的**本周期**状态：原来未选就仍未选，原来已选黑/白则保持该选择。移除或迁移依赖 `lastManualAutoPlayColorMode` 的历史恢复逻辑；不能新建默认黑棋授权。保留首次自动模式身份选择的 pending 流程。

### 3. WebView 未选态

- `ReadBoardControlCenterState.Color` 使用 `""` 明确表达未选择；其余仍为 `"black"`、`"white"`、`"auto"`。不增加第四个 radio。
- 后端从本次选择投影，不能依据 `PlayColor` 投影：FoxAuto 尚未识别时仍应显示自动模式已选。
- JS 每次 render 对整组 radio 按值重新设置 checked，删除 `control.color || "auto"` 回退；空值全部取消选中。
- 入站 `control.update` 仍只接受 black/white/auto；用户无需发送一个清空命令，清空由 runtime 生命周期产生。
- 保持现有 enablement：自动落子关闭时不能预选，非 Fox 平台不能选择 FoxAuto。
- 本计划不新增提示文案或弹窗；现有“执黑／执白／自动”与全未选即可表达需求。因此语言文件与 `Program.AddDefaultLangItems` 可保持不变。若确认需增加“请选择棋色”等提示，必须同一变更更新全部受支持语言和默认词条。

### 4. 明确不扩大的行为

- 不自动判断“新的一盘棋”，不新增手动模式跨房间检测。
- 同一启用周期内单纯停止／恢复持续同步不自动撤销手动选择；已有停止协议保持原样。
- 同一启用周期切换平台不额外强制清除手动色。FoxAuto 在非 Fox 平台继续按现有 UnsupportedPlatform fail-closed，不能自动降为黑棋。
- 不改识别算法、重试间隔、窗口尺寸、DPI、布局或宿主协议；无需发布、打包或跨仓库协议迁移。

## 实施顺序与文件边界

这是一个有共享状态契约的端到端变更，建议以 #82 一个实现单元完成，不拆成分别可合并的“后端／UI”票据。实现前先确认本计划；实现后运行 Standards + Spec 审查和实际验收，不能以测试通过替代验收。

1. **Runtime 与契约回归**
   - `readboard/Core/ControlCenter/ControlCenterRuntime.cs`：新增本次选择、状态转移、解析门禁、clone/equality/snapshot。
   - `tests/Readboard.VerificationTests/Host/ControlCenterRuntimeTests.cs`：补本周期边界；持久偏好测试与本次授权测试分离。
   - 直接 resolver 不需要接受 null；未选守卫放在调用者，不把未知 enum 当 FoxAuto。
2. **生产调用方和授权链**
   - `readboard/MainForm.ControlCenter.cs`：比较 session 选择，保证同色重选有实际副作用。
   - `readboard/Form1.cs`：检查直接签发、周期 snapshot、Fox 模式判断与历史手动恢复。
   - `readboard/MainForm.WebView.Identity.cs`、必要时 `Core/AutoPlay/FoxIdentitySelection.cs`：取消只保留本次状态，迁移受影响调用方与测试。
   - `Core/AutoPlay/AutoPlayWireIssuer.cs`、`Core/Protocol/SyncSessionCoordinator.Orchestration.cs`：核对既有门禁足够，优先保持实现不变；使用真实 coordinator 和 RecordingTransport 证明无提前发送。
3. **UI 投影与渲染**
   - `readboard/MainForm.WebView.cs`、`ReadBoardUiModels.cs`、`WebView/app.js`：当前选择 → JSON → radio 全组状态。
   - `tests/WebView/app-rendering.spec.js`：选中 → 未选 → 重新选中，以及 FoxAuto 未识别仍显示 auto。
   - 受影响的 snapshot fixture、bridge 与身份交互测试同步迁移。遇到断言源码表达式的测试，删除对应实现细节断言，以实际 DOM/协议行为覆盖，不重新钉死字符串。
4. **聚焦门禁和实际验收**
   - 在 Windows 精确候选中运行 runtime、issuer、identity、coordinator 相关检查；执行下方验收矩阵。
   - WSL 执行 DOM 检查；原生 Windows FakeHost 验证真实按钮、C# snapshot 与协议行。
   - 完成后更新本计划的实际结果及用户行为说明；本计划中的预期不能直接改写为 PASS。

配置默认值、`DualFormatAppConfigStore`、SettingsDraft 实现、语言文件、宿主仓库与发布脚本预计不需修改。若实施选择发生变化，应先更新此边界及接受标准，而不是隐式扩大范围。

## 验收矩阵

| ID | 场景 | 可观察的通过标准 |
| --- | --- | --- |
| A1 | 历史 ManualBlack → 开启 → 选黑 → 关闭 → 开启 | 初次及再次启用均未选，`PlayColor=null`、`IsKnown=false`；不新增 `play>`。 |
| A2 | 上述流程使用 ManualWhite | 与 A1 对称，无白棋历史授权泄漏。 |
| A3 | 未选时已有持续同步与双向同步；持续接收 snapshot，并停止／恢复同步 | UI 全未选；直接 issuer 和 coordinator snapshot 路径都不能新增 `play>`。 |
| A4 | 再次显式选择上次相同的黑/白，或改选相反色 | session 发生真实变化，正常产生对应 `play>black>…`／`play>white>…`；同色重选不被误判为 NoOp。 |
| A5 | FoxAuto 已识别 → 关闭 → 同房间或另一房间重启用 | 保留 auto 勾选；关闭后旧授权失效；在当前房间重新识别前不得发送 `play>`，缓存必须要求新采样。 |
| A6 | A5 后当前房间、当前身份重新识别成功 | 正常产生当前棋色授权；旧代次识别结果不能替代新识别。 |
| B1 | 关闭双向同步后再开启双向同步和自动落子 | 等效结束旧周期，手动仍需重选；关闭动作确实撤销宿主自动落子。 |
| B2 | 有效手动选择后重复发送 enabled=true | 保留本周期选择，不误开新周期、不额外发送重复授权。 |
| B3 | 关闭状态收到选色 intent | 沿用既有拒绝规则，不提前建立选择。 |
| B4 | 未选／已选状态打开首次自动身份选择后取消 | 恢复原本周期状态；不得恢复历史色或默认黑色。 |
| B5 | 保存／取消／重置设置、偏好保存失败、重启加载旧 0/1/2 配置 | 不意外建立或撤销本周期选择；新进程手动未选，FoxAuto 仅记模式。持久化失败仍遵守“进程值立即生效，标记未保存”。 |
| B6 | DOM 连续收到 black → 空、white → 空、auto → 空，再选择黑/白 | 每次空 snapshot 后全部 radio 未选，不回退 auto、不遗留 checked。 |
| B7 | 非 Fox 平台保留 FoxAuto 模式 | 自动选项不可操作、解析未知、不发送 `play>`；不回退为手动黑棋。 |

A1–A6 对应 issue 六项明确回归要求。B 行覆盖状态分离容易漏掉的可达边界，不要求对同一行为堆叠多套重复测试。

### 验证入口

- C# 聚焦过滤器按实际受影响测试选择：`ControlCenterRuntimeTests`、`AutoPlayWireIssuerTests`、`FoxIdentitySelectionTests`、`SyncSessionCoordinatorOrchestrationTests`；跨 runtime → real coordinator 的行为用现有测试项目承载。
- `npm run test:webview` 是 DOM 验证，不是原生宿主验收。
- 原生入口为 `npm run test:webview:host` 及 `tests/WebView/real-webview2-host-fixture.js`；复用隔离配置和 FakeHost，增加相关场景或一次性 smoke，不必跑无关宿主案例。
- 真实 Fox 验收按桌面验收 skill 准备精确 Windows 候选，观察同房间重新采样、换房间后正确执子色及协议证据；确定性 fixture 不能代替这项原生证据。
- 开工前对变更导出符号运行 LSP references；实现后再决定是否需要扩大检查。不是默认全仓库构建、格式化或全套测试。

## 已批准的交付边界

用户批准上述 nullable session 选择、空字符串 JSON、保留磁盘格式和现有平台／同步边界；按一个端到端实现单元交付。

若产品希望同时“完全停止保存手动黑白”或“自动识别手动模式下的新棋局并撤销授权”，那是对本计划的实质扩展，需要单独确认，不能暗含在 #82 的实现中。

仓库根约定引用的 `docs/agents/issue-tracker.md` 在此远程 main 基线不存在；本次沿用实际 GitHub issue 与已有 `docs/plans/`，没有创建替代 tracker 或虚构本地票据规范。

## 实施记录与验证结果

实现增加 `SelectedAutoPlayColorMode`，把持久偏好与本周期授权分开。关闭自动落子或双向同步清空手动选择；重新开启使 Fox 识别缓存失效。UI 投影、直接签发与持续同步 snapshot 均使用本周期解析。身份取消保留当前选择，删除历史手动色恢复字段及接口参数。JSON 配置、协议、语言和布局不变。

Windows 检查使用此工作树源码的隔离镜像 `D:\dev\weiqi\worktrees\readboard\issue82-validation-bmZZoi`，SDK `10.0.104`；它包含基线加当前未提交改动，不冒称已提交的精确候选。

| 检查 | 实测结果 |
| --- | --- |
| Runtime 新周期回归 | 修改前 4 例失败，修改后同 4 例通过。 |
| Runtime、identity、候选身份、WebView bridge | 213 例通过，0 跳过；`issue82-integration.trx`。 |
| Runtime、真实 issuer、coordinator 持续同步 | 149 例通过，0 跳过；`issue82-authorization.trx`。 |
| Settings、Fox live recognition、replay gate、架构契约及相邻 bridge/observation | 98 例通过，0 跳过；`issue82-adjacent.trx`。 |
| `npm run test:webview` | 8 例通过；包括 black/white/auto → 空、实际 label 点击及不回发命令。 |
| 原生 WebView2 `manual autoplay color` 场景 | 1 例通过；真实 C# snapshot、首次及再次启用未选、同色重选、双向同步关闭、身份取消和重启。 |

上表 C# 过滤器存在交集，不相加为独立测试总数。TRX 在镜像 `tests/Readboard.VerificationTests/TestResults/`；原生截图在 `test-results/tests-WebView-real-webview-83e07-ection-per-enablement-cycle/manual-color-unselected.png`，已目视确认自动落子开启时三项棋色均未选。原生 FakeHost 没有真实棋盘同步目标，其证据不等同于真实 Fox 识别或落子。

A1/A2/A4/B1 的状态和协议行为由 runtime → issuer → real coordinator/RecordingTransport 覆盖；A3 由真实 coordinator 持续同步 worker 覆盖。A5/A6 已覆盖同房间与换房间状态失效、上下文更新和重新识别，以及既有识别代次回归；真实 Fox 场景仍未验收。B2/B3 由 runtime 检查，B4 由原生身份取消检查，B6 由 DOM 检查覆盖。B5 有既有 Settings 回归及原生重启证据，不宣称对每种持久化故障组合都做过原生验收。

**外部验收阻塞**：当前 Windows 未运行 Fox 对局窗口；缺少可识别的实际房间与配置身份。需要在精确提交候选上完成 A5/A6 的同房间／换房间采样及协议观察，不能用 fixture 替代。无窗口坐标、DPI 或布局改动，未扩展到无关多屏布局验收。

LSP references 两次返回无可用 language server；调用方迁移以当前源码与符号检索确认。删除旧身份恢复接口后无残留调用。未修改日常 Windows clone，未 push 或发布。

### 双轴审查

以 `d6e38ae7af615dd7259a73dd79234f222d83e7df` 为固定基线与审查时 HEAD，审查工作树全部 16 个受版本控制文件的差异及未跟踪计划；`.codegraph/.gitignore` 为本地生成索引文件，不纳入提交。独立 Standards、Spec 审查均未发现代码问题。审查前后 HEAD、diff、status、未跟踪清单及内容完全一致。

审查结论：`SUCCESS`，开放阻塞项 0、follow-up 项 0。A5/A6 真实 Fox 验收是仍未满足的原任务外部验收门禁，不作为已完成或转交后续票据；issue 保持未关闭。审查记录补入本文后只作交付记录变更，生产代码保持审查时状态。
