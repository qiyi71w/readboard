# Simplify 计划（post-v3.0.0）

日期: 2026-04-23
分支: `simplify/post-v3`

## 已核对的设计文档

- `docs/specs/2026-04-23-color-mode-design.md`
- `docs/specs/2026-04-21-readboard-fox-title-status-design.md`
- `docs/superpowers/specs/2026-04-22-dotnet10-upgrade-design.md`
- `docs/superpowers/plans/2026-04-22-dotnet10-upgrade.md`（Phase 2 已规划测试/基准换 ProjectReference 的方案）
- `docs/superpowers/specs/2026-04-21-mainform-theme-*`
- `docs/specs/2026-04-23-test-project-reference-boundaries.md`
- `docs/specs/2026-04-23-mainform-state-boundaries.md`
- `docs/specs/2026-04-23-protocol-keyword-constants.md`

## 总原则

- 每条 simplify **单独一个 commit**，回滚粒度小、reviewer 友好。
- 每完成一条立即跑一轮自查（reuse / quality / efficiency 三方面），不要引入新问题。
- 引入新边界 / 新内部约定时，**必须把约定写进 `docs/specs/`**。
- 触碰公共合约（JSON 字段名、协议关键字、被回归测试锁定的方法名）前**必须**回到 docs 校对。

---

## 2026-04-23 本轮工作完成记录

- **B: ProjectReference 转换** —— commit `d16b7fc`：测试项目和 benchmark 项目从生产源码链接改为引用 `readboard.csproj`；保留测试支撑源码链接；新增 `InternalsVisibleTo`；把跨程序集不可用的测试侧 `partial` hook 改为生产程序集内的 `internal` 诊断入口；新增 `VerificationTests_Project_UsesProjectReferenceForProductionSources` 对称合约测试。
- **D: 协议关键字常量化** —— commit `5fef44a`：新增 `ProtocolKeywords`，`LegacyProtocolAdapter` 的 parse / emit 路径改用常量；已确认 `lizzieyzy-next` 没有共享 enum/常量，wire 文本逐字不变。
- **E: MainForm 静态镜像字段清理** —— commit `4dcd7fb`：删除 `ox1` / `oy1` / `type` 这三个历史 `public static` 镜像字段及同步赋值；保留 `boardW` / `boardH` 运行时字段和 `LegacyTypeToken = CurrentSyncType.ToString()` 协议行为。
- **superpowers 文档入库** —— commit `ce4cad7`：把原 `docs/superpowers/{specs,plans}/` 平迁到 `docs/specs/` 与 `docs/plans/`，让 mainform 主题、.NET 10 升级、legacy 桌面布局的设计与实施计划进入版本控制。
- **未继续执行 C**：`SyncSessionCoordinator` 拆 `OutboundProtocolDispatcher` 的问题真实存在，但属于拆类级大重构；本轮只记录复审结论，等待明确确认后再执行。
- **本轮最终验证**：`dotnet build readboard.sln -c Debug` 为 0 错误；`dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --no-build` 为 312 通过 / 0 失败；benchmark 项目独立 build 0 错误；`git status` 工作树干净。

---

## 优先级表

### ✅ 已完成

#### A. `SyncSessionCoordinator` 只读 getter 抽锁辅助
- **commit**: `3f03c2c`
- **改动**: 4 个 getter（`StartedSync` / `KeepSync` / `IsContinuousSyncing` / `SyncBoth`）走私有 `GetLockedSessionState<T>(Func<SessionState, T>)`
- **文件**: `readboard/Core/Protocol/SyncSessionCoordinator.cs`（+10 -20）
- **新文档**: `docs/specs/2026-04-23-sync-session-state-locking.md`（明确该辅助方法的边界：只覆盖 trivial getter，写操作 / 读写组合 / 多值快照 / 读+signal 仍 inline lock）
- **验证**: 309 个测试全部通过，0 错误

---

### 🟢 高 ROI · 中风险（推荐继续，但先处理阻塞）

#### B. 测试 / 基准项目改 ProjectReference + InternalsVisibleTo
- **状态**: ✅ 已完成（commit `d16b7fc`）
- **依据**: `docs/plans/2026-04-22-dotnet10-upgrade.md` Task 7-8（Phase 2 早已规划，本轮落地）
- **执行前的问题**: `tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj` 有 69 行生产源码链接，benchmark 项目有 48 行；最近 commit `956528c` 就是因为 benchmark 漏同步 `FoxWindowContext` 导致 CI 红 —— 每加一个 Core 类型要改 3 个文件。
- **补充边界**: 见 `docs/specs/2026-04-23-test-project-reference-boundaries.md`。`ProjectReference` 只替代生产源码链接，不替代测试支撑源码。
- **实际执行**:
  1. `readboard/Properties/AssemblyInfo.cs` 加 `[assembly: InternalsVisibleTo("Readboard.VerificationTests")]` 与 `[assembly: InternalsVisibleTo("Readboard.ProtocolConfigBenchmarks")]`
  2. 删除 `tests/.../SyncSessionCoordinator.TestHooks.cs`（跨程序集 partial 不可用），把 `WaitForPendingMoveAvailability` 作为 `internal` 诊断入口移到生产 `SyncSessionCoordinator`
  3. 测试和 benchmark 项目都删除 `..\..\readboard\...` 生产源码链接，保留 `..\Shared\...` / harness 测试支撑链接
  4. 两个项目都加 `<ProjectReference Include="..\..\readboard\readboard.csproj" />`
  5. `FrameworkContractTests` 改为锁定两个项目都用 `ProjectReference` 且不再链接生产源码（含新增的 `VerificationTests_Project_UsesProjectReferenceForProductionSources` 对称合约）
- **风险记录**: 中偏高；实际编译通过，无可见性破坏
- **新文档**: `docs/specs/2026-04-23-test-project-reference-boundaries.md`
- **验证**:
  - `dotnet build readboard.sln -c Debug` —— 0 错误
  - `dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --no-build` —— 312 通过 / 0 失败
  - `dotnet build benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj -c Debug` —— 0 错误

#### C. `SyncSessionCoordinator` 拆 OutboundProtocolDispatcher
- **状态**: 已复审，问题真实存在，但属于大重构；按仓库规则需确认后再执行
- **当前**: `SyncSessionCoordinator.cs`（892 行）+ `.Orchestration.cs`（926 行），承担状态机、pending move 生命周期、协议下发、窗口上下文构建多重职责
- **方案**: 抽 `OutboundProtocolDispatcher` —— 把 `outboundProtocolSyncRoot` 锁住的 send/serialize 路径搬出来，coordinator 只调度
- **风险**: 中。改动面大，但有 309 个测试覆盖
- **必须新建 docs**: `docs/specs/2026-04-2X-sync-coordinator-decomposition.md` 说明边界（哪些状态/事件留 coordinator、哪些搬走）
- **建议**: 先做 B，B 完了如果还有时间精力再做
- **本轮复审记录**: 当前文件为 `SyncSessionCoordinator.cs` 887 行 + `.Orchestration.cs` 926 行；`outboundProtocolSyncRoot` 仍集中保护发送/序列化路径，未发现已有 docs 将该项定义为不做。由于这是拆类级别的大重构，本轮只记录复审结论，不直接改代码。

---

### 🟡 中 ROI · 需谨慎（要先校对外部影响）

#### D. 协议关键字常量化
- **状态**: ✅ 已完成（commit `5fef44a`）
- **执行前的问题**: `Core/Protocol/LegacyProtocolAdapter.cs` 散落 `"sync"` / `"start"` / `"place"` / `"end"` / `"placeComplete"` / `"notinboard"` / `"version"` / `"quit"` / `"loss"` / `"bothSync"` / `"foreFoxWithInBoard"` 等裸字符串
- **依据**: dotnet10-upgrade-design.md 明确 **Non-Goals: 不改变与 LizzieYzy-Next 的通信协议** —— 常量值必须**逐字相同**
- **补充边界**: 见 `docs/specs/2026-04-23-protocol-keyword-constants.md`。`ProtocolKeywords` 只是内部字符串别名，不是新公共 API；所有值必须与旧 wire 文本逐字相同。
- **行动前已完成**: 已 grep `D:\dev\weiqi\lizzieyzy-next`，确认集成端 `src/main/java/featurecat/lizzie/analysis/ReadBoard.java` 直接用字面值 `startsWith` / `equals` 解析，没有共享代码或跨仓库 enum 可复用。
- **实际执行**: 加 `Core/Protocol/ProtocolKeywords.cs` 静态 internal 类（保持 string 类型，wire 协议本身才是公共合约），`LegacyProtocolAdapter` 的 parse / emit 改为引用常量；新增 `ProtocolKeywords_DefineStableLegacyWireTokens` 锁定每个常量的字面值
- **新文档**: `docs/specs/2026-04-23-protocol-keyword-constants.md`
- **验证**:
  - `dotnet build readboard.sln -c Debug` —— 0 错误
  - `dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --no-build` —— 312 通过 / 0 失败

#### E. 清理 `Form1` 的 `public static` 可变字段
- **状态**: ✅ 已完成（commit `4dcd7fb`）
- **执行前的问题**: `Form1.cs:22, 25, 38` —— `public static int ox1, oy1, type`，外加 `boardW`/`boardH`（`Form1.cs:42-43`）与 `Config.BoardWidth/Height` 双份同步
- **补充边界**: 见 `docs/specs/2026-04-23-mainform-state-boundaries.md`。`AppConfig` 是持久化配置快照，不是所有 UI 编辑中间态的实时唯一来源。
- **实际执行**:
  1. 删除 `ox1` / `oy1` 字段和 `UpdateSelectionBounds` 中的镜像赋值（不迁移到 Config）
  2. 删除 `type` 字段和 `SetCurrentSyncType` 中的镜像赋值，保留 `CurrentSyncType` 与 `LegacyTypeToken = CurrentSyncType.ToString()` 行为
  3. `boardW` / `boardH` 本轮不动，作为后续单独 commit 候选
- **行动前已完成**: grep 全仓 + `D:\dev\weiqi\lizzieyzy-next`，未发现 `MainForm.ox1`、`MainForm.oy1`、`MainForm.type`、`readboard.MainForm` 或相关 reflection 字段访问
- **新文档**: `docs/specs/2026-04-23-mainform-state-boundaries.md`
- **验证**:
  - 新增 `MainForm_RemovesLegacyPublicStaticSelectionAndTypeMirrors` 源码级回归测试，锁定字段不能复活
  - `dotnet build readboard.sln -c Debug` —— 0 错误
  - `dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --no-build` —— 312 通过 / 0 失败

---

### 🔴 撤回（违反 docs，不要做）

#### F. ~~重构 `DualFormatAppConfigStore` 的 legacy parser~~
- **被禁理由**: `docs/specs/2026-04-23-color-mode-design.md` "不在范围内" 段明确：
  > `DualFormatAppConfigStore` 重构为强类型 DTO 或 `Dictionary<string, JsonElement>` —— **不修复**。现有 `value.ToString()` + `*.TryParse` 模式正确处理 `JsonElement`，已被 13 个 store 测试验证。
- 且 `ColorMode` 在 legacy 文件位置 14 是公共合约，写出永远 15 字段 —— 任何动字段位置/数量都会破 `Save_RoundTripsColorMode` 与 `Save_WritesJsonAndLegacyMirrorWithUpdatedMetadata`，并让 v3.0.0 之前的旧版读出错位字段

#### G. ~~主题策略抽 `IThemeStrategy`~~
- **被禁理由**: color-mode-design.md "命名说明" 段明确 `ApplyOptimizedMainFormTheme` / `ApplyClassicMainFormTheme` 名称被回归测试 `MainFormThemeStartupParityRegressionTests.MainForm_AppliesDistinctClassicAndNewThemeVisuals` 锁定，且测试要求 Classic vs Optimized 视觉**必须明显不同**
- 即使保留方法名做内部抽取，`UiTheme.StyleXxx` 内部已按 `IsDarkMode` 分支，Classic 走 `SystemColors`、Optimized 走 `UiTheme.*` 的语义差异很难干净统一
- **降级**: 改为低优先级，如真要做必须保入口名 + 跑 parity 回归测试

#### H. ~~删 legacy 配置格式~~
- v3.0.0 刚发，用户基础未迁移完。**至少再留 1-2 个版本**

#### I. ~~`Form1` partial 合并/重拆~~
- `MainForm.Configuration.cs` / `MainForm.Protocol.cs` 各 ~150 行，改动机会成本高于收益

#### J. ~~把 `RuntimeContext.BoardBitmap` 抽 `IPixelSource` 去掉 System.Drawing~~
- 理论洁癖。Windows-only 项目，无 headless 复用需求

#### K. ~~清理仓库根目录 `temp-*-build/` `_*.log` 等~~
- 已在 `.gitignore` / 本地 exclude 中，不占 Git 空间

---

## 推荐执行顺序

1. ✅ **A** —— 已完成（commit `3f03c2c`）
2. ✅ **B** —— ProjectReference + InternalsVisibleTo（commit `d16b7fc`）
3. ✅ **D** —— 协议关键字常量化（commit `5fef44a`）
4. ✅ **E** —— 清理 `Form1.public static` 字段（commit `4dcd7fb`）
5. **C** —— 拆 `OutboundProtocolDispatcher`（最大改动面，待用户确认后再上）

每完成一条：跑 `dotnet build` + `dotnet test`，确认测试不回退；自查不引入新问题；commit；继续下一条。

## 验证基线

- `dotnet build readboard.sln -c Debug` —— 0 错误（已确认）
- `dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --no-build` —— 312 通过 / 0 失败（已确认；A/D/E 各新增 1 个回归测试，B 增 1 个对称合约测试）
- benchmark 项目独立 build 0 错误（已确认）
- 每轮 simplify 后必须维持这三个数字

## 不在范围内（评审后决策为不做）

| 项 | 决策 | 原因 |
|---|---|---|
| 重写 OpenCvSharp 调用为 SkiaSharp | 不做 | 改造面巨大，识别管线已被 fixture replay 覆盖且稳定 |
| 删 `RuntimeContext` 改注入 | 不做 | 全局可变上下文是 WinForms 历史包袱，重构面积远超本轮目标 |
| `Form4` 设置面板重写为 PropertyGrid | 不做 | 现有 UI 已被多个回归测试锁定（视觉差异、theme parity） |
