# ReadBoard 统一架构迁移前合同护栏

日期：2026-07-29

对应 ticket：`.scratch/readboard-unified-target-architecture/issues/01-architecture-contract-fence.md`

## 边界

本护栏只锁定迁移前的可观察合同，不引入 Control Center、Settings Draft、Fox Identity Selection 或 Hosted Update 新模块，也不改变生产行为。WebView 合同测试沿用现有命令解析 seam，锁定已支持的合法命令和非法 JSON/payload shape。

本轮没有改动 LizzieYzy-Next 的启动参数或 wire 文本，没有改动 `SyncSessionCoordinator` 的锁、队列、generation 或生命周期，也没有改变 JSON schema、legacy 配置字段、更新通道选择、GMA/Fox 识别和打包产物。

## 本轮开始记录

以下是开始 ticket 之前记录的状态，不能当作后续提交后的状态：

| 项目 | 记录 |
| --- | --- |
| branch | `ui/webview2-rewrite` |
| HEAD | `e4b7cb53d2be563c1da3133c1e2aaf1ad1b9d02f` |
| 用户已有未跟踪文件 | `CONTEXT.md`、`docs/releases/v3.1.0.md`、`docs/specs/2026-07-29-readboard-unified-target-architecture-design.md`；本轮保留，未纳入提交 |
| WSL 环境 | Linux 工作区；无 Linux `dotnet` |
| Windows 验证环境 | Windows 10.0.26200.0、PowerShell 7.6.4、.NET SDK 10.0.104 (`C:\Users\admin\.dotnet\dotnet.exe`) |
| 既有测试基线 | Windows `dotnet test --no-restore -c Debug`：725 passed / 0 failed |

## 合同覆盖

| 合同 | 可执行护栏 |
| --- | --- |
| WebView JSON 合法/非法 shape | `Host/WebViewBridgeTests` 锁定现有命令解析 seam 的合法命令和非法 JSON/payload shape；`ArchitectureContractFenceTests.WebViewStateEnvelope_ContainsCompleteAuthoritativeSnapshotShape` 锁定完整 state envelope |
| LizzieYzy-Next inbound/outbound wire | `ArchitectureContractFenceTests.LegacyWireContract_*` 加上逐字 token、tab 三字段、顺序；`Protocol/LegacyOutboundProtocolContractTests`、`Protocol/LegacyProtocolAdapterTests` 保留详细协议 fixture |
| JSON + 两份 legacy 配置 | `ArchitectureContractFenceTests.ConfigurationContract_SaveWritesBothLegacyMirrorsAndJsonThatReloads` 先从 JSON、再删除 JSON 后从刚写出的两份 legacy 镜像 reload；`Configuration/DualFormatAppConfigStoreTests` 保留字段和旧 fixture round-trip |
| Hosted Update channel/capability/hash/ZIP | `ArchitectureContractFenceTests.HostedUpdateContract_*`；`Host/GitHubUpdateCheckerTests`、`HostedUpdatePackageDownloaderTests`、`HostedUpdatePackageVerifierTests`、`UpdateDownloadLauncherTests` 覆盖通道、v1/v2、SHA-256、ZIP 和通知顺序 |
| Fox 自动棋色/GMA/fail-closed | `ArchitectureContractFenceTests.FoxAutoplayContract_*`、`GmaContract_*`；`AutoPlay/*`、`Protocol/SyncSessionCoordinator*` 和 `Recognition/*` 保留识别、授权、末手来源及 authoritative frame 回归 |
| DPI/最小客户区/多屏/Runtime/打包 | `ArchitectureContractFenceTests.DesktopContract_*`；`Host/WebViewBridgeTests` 的 clamp/runtime 检查、`Host/HighDpiSourceRegressionTests`、`Packaging/FrameworkContractTests` 和 `Packaging/PackageReleaseScriptTests` 保留外部边界 |
| 确定性 | 本轮新增测试只使用纯函数、内存 transport、临时目录和可控 fixture；不使用 `Thread.Sleep`、固定短窗口或线程池碰运气 |

## 暂存的 source-slice 护栏

以下测试目前仍锁定 private 方法名、源码片段或发布调用位置，迁移期间保留但不应视为稳定 interface 合同：

| 当前 source-slice 测试 | 计划替代 |
| --- | --- |
| `Host/UpdateDownloadLauncherTests` 与本轮 `HostedUpdateContract_SourceOrdersHashZipAndHostHandoff` | Ticket 02/03 的 Hosted Update module completion、ownership 和 effect-order interface tests |
| `Host/HostBoundaryClosureRegressionTests`、`Host/StartupAndShutdownRegressionTests`、`Host/ThinHostCoordinatorTakeoverTests` | Ticket 05-10 的 Control Center/runtime adapter 与 shell publication tests |
| `Host/WebViewBridgeTests` 中源码资源检查、`Host/WebViewUiPolishTests` | Ticket 14/15 的 semantic localization 和 WebView rendering/interface tests |
| `Host/HighDpiSourceRegressionTests`、`MainFormTheme*RegressionTests` | Ticket 16 的 Windows desktop/DPI/layout acceptance；真实 GUI 证据仍是外部门禁 |
| `Host/MainWindowTitleSourceRegressionTests`、`YikeMainFormIntegrationRegressionTests` 及协议定位 source slices | 对应 runtime/protocol adapter interface tests；在替代测试通过前不删除 |

本轮不以放宽断言的方式修复任何 baseline 失败。后续 ticket 必须先增加等价的行为测试，再删除对应 source slice。
