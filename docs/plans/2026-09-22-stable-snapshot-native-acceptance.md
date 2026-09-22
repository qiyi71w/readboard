# 稳定盘面确认帧：Windows 联合原生验收

日期：2026-09-22。范围：本地票据 `sync-snapshot-confirmation/02-native-joint-acceptance`。本记录只描述下列精确候选的验收，不代表当前分支已合入实现，也不是发布批准。

## 候选与运行身份

| 组件 | 精确提交 |
| --- | --- |
| ReadBoard | `4f2c4ccffb26111f13850968a26adb568cb204e0` |
| LizzieYzy | `c1308069813aa6091aceda751b24c612d372dcda` |

两组件通过 `prepare-windows-candidate` 从 WSL 精确提交本地传递并构建，两个 `candidate.json` 均为 BUILT。ReadBoard 使用 Windows .NET SDK 10.0.104；宿主运行时为 Java 21.0.11，WebView2 Runtime 为 153.0.4234.48。没有修改生产代码或重新定义同步协议。

- 两组件在独立 Windows 候选目录运行，配置位于隔离运行目录。
- 宿主启动后独立 Status 返回 READY，并检查了实际 Swing 窗口截图。
- 实际 helper 位于宿主候选的 `target/readboard/readboard.exe`；进程父子关系已核对，完整 helper 运行文件来自上述 ReadBoard 构建产物。
- 启动时独立配置未加载引擎；用户随后在候选中加载引擎。A1/A2 的引擎恢复结论来自实际 GTP 与吞吐日志，而不是启动配置。
- 人工操作由用户执行；代理检查截图、导出协议、接纳决策、日志及 SGF。账号凭据未进入本记录，日常 clone 和配置未改动。

## 证据索引

完整原生诊断包保存在上述运行目录的 `config\diagnostics`，同时归档于本地排除的 `.scratch/sync-snapshot-confirmation/evidence/`。这些本机路径用于复核，不是随 Git 分发的附件。

| 引用 | 文件或证据 |
| --- | --- |
| N1 | A1/A2 原生诊断包及用户对照截图：恢复同步与切换野狐 |
| N2 | 原生诊断包：无历史干扰边界及三次停启 |
| N3 | 原生诊断包及干扰前 SGF：有真实历史的遮挡保护 |
| N4 | 原生诊断包与用户确认：双向落子与停止，无重复、无回滚且停止后远端未落 M10 |
| F | 01 完成记录、`evidence/acceptance-test-map.txt`、`consumer-acceptance-test-map.txt`、`review-final.txt` 及对应 TRX/Surefire/协议 trace |

包内主要入口是 `snapshots/readboard-protocol.log`、`snapshots/recent-decisions.jsonl`、`snapshots/sync-context.json`、`logs/lizzie/app.log` 和 `logs/readboard/app.log`。下面时间均为 UTC；宿主文本日志当地时间比 UTC 少四小时。

## A1–A10 覆盖表

全部行对应上表同一对候选提交。F 是继承的确定性检查结果，非本次重跑；N 是本次原生操作。PASS 限于各行声明的范围。

| ID | 来源及结果 | 覆盖限制 |
| --- | --- | --- |
| A1 | **PASS，N1 + F**。其他平台停止期间增加黑 R3、白 R16；恢复后两份完整八子盘，05:02:59.228 HOLD，05:02:59.453 自动接纳，差异两子、移除零子。用户反馈及截图确认自动补齐。 | 无额外落子、预清盘或人工重建；多手静态语义另由 F 的跨端 bridge 证明。 |
| A2 | **PASS，N1 + F**。保留八子旧盘切到野狐空盘；05:05:42.958 自动采用，决策记录移除八子、`forceRebuildRequested=false`。 | 原生为 FOX/LIVE_ROOM、第 0 手，走现有上下文接纳路径；FOX UNKNOWN/无手数只由 F 覆盖。 |
| A3 | **PASS，F；N3 补充**。真实 coordinator 的错误首帧重复分发不制造确认，消费者保持原盘。 | 恢复首帧、同采样重放的精确时序未在桌面强制造出。 |
| A4 | **PASS，N3 + F**。05:16:04.624/04.857 两个不同错误盘面各差 48 子，均 HOLD；05:16:05.099/05.319 正确盘面恢复为 MOVE/NO_CHANGE。 | N3 覆盖实际短暂遮挡，不是所有遮挡可靠性保证；相同冲突重复收敛及交替序列由 F 补全。 |
| A5 | **PASS，F**。marker/source 变化的可见性、归一化及轮次信任检查通过。 | 未独立制造原生 marker 抖动。 |
| A6 | **PASS，N1/N2 + F**。A1 两帧后静止约 12 秒，A2 两帧后约 90 秒无更多批次；每条主路径重建、分析恢复各一次。停启同盘为 NO_CHANGE。 | 节点 identity、采样有界与同盘恢复的全部确定性断言继承 F。 |
| A7 | **PASS，N2 + F**。三次停止到重启间隔约 1.60、1.60、1.11 秒，接纳始终为同盘 NO_CHANGE，最后约 25 秒稳定。 | 旧 worker/分发/清理晚到和同平台重复设置的强制交错由 F 覆盖；原生未声称观察到所有竞态。 |
| A8 | **PASS，N4 + F**。K10 先出现在 05:19:40.788 完整采样中，05:19:40.796 才收到唯一一次 placeComplete；Q6 是对方回应。05:19:44.499 停止，随后本地 M10 未落到远端，用户明确确认无重复或回滚。 | 用户观察证明物理落子和停止效果；日志证明采样/完成顺序，不把通知次数当作物理点击计数。旧截图等待窗口、缓存重放和超时由 F 覆盖。 |
| A9 | **PASS，F**。强制重建一次性消费，普通确认不再携带该指令。 | 按票据继承确定性证据，未独立进行原生强制重建。 |
| A10 | **PASS，F；N1 补充有引擎路径**。F 覆盖引擎可用/不可用；原生 A1 clear_board/loadsgf 各一次，分析恢复一次后访问量持续增长。 | 未独立原生验收无引擎恢复；初始无引擎启动不作为该分支通过证据。 |

N1 解析检查取得四个完整 19 行批次：八子盘两帧、空盘两帧，两组各自 payload 相同。N3 最终仍是 `moveNumber=12,kind=MOVE`，整个干扰段未重建。N4 完整采样依次为含 K10 的 13 子盘两帧、含对方 Q6 的 14 子盘两帧；M10 在这些远端帧中为空。用户在停止后本地下 M10，并明确确认远端 M10 保持为空；这是日志停止采样后所需的人工观察证据。

## 干扰边界与继承验证

N2 的第一轮遮挡操作处于无真实手顺的静态盘面，错误识别曾被采用，移开后恢复；该现象保留，不能报告为 HOLD 保护通过。宿主既有 `ReadBoard.shouldHoldConflictingSnapshot` 调用 `SyncSnapshotRebuildPolicy.shouldRebuildImmediatelyWithoutHistory`，无父节点时允许立即接纳；本次未放宽该规则。随后保持同步真实新增黑 R15、白 F3，SGF 记录 `B[qe];W[fq]`，建立 MOVE 历史再执行 N3，两个不同错误冲突均被 HOLD。验收范围是保留原有会进入 HOLD 的保护，不扩建全量防遮挡机制。

01 的最终交付与本次候选 SHA 一致，最终双轴审查 SUCCESS、零后续项，转交本票的 OWNED finding 为 **none**。继承验证为 producer 171/171、consumer 202/202；审查补证后只重跑了 producer 10/10、consumer 94/94（0 skipped），不把较早的广泛门禁冒充补证后重跑。跨端证据包含实际 coordinator 导出的 generic、fox-known、fox-unknown 和 conflict-safety trace，共七个桥接案例。

本次没有源码修复，因此没有新增代码审查或重跑上述确定性套件。原生必需场景已通过；记录的 Standards/Spec 完成审查与提交信息由本地票据保存。发布、合并和 03 的实际执行不属于本次操作。
