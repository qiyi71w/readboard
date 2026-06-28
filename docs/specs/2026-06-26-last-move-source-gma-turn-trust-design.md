# ReadBoard 末手来源与 GMA 轮次可信度设计（2026-06-26）

## 背景

野狐让子棋和试下模式下，标题手数不能继续作为 side-to-play 的权威输入。尤其让子棋里，`foxMoveNumber` 只表达野狐标题里的手数元数据，不理解 `HA` / `AB` / `PL` / 白方首手。

当前 ReadBoard 会把多种末手推断都压成同一种棋盘状态：

- 红/蓝角标识别
- 野狐默认右下反色缺口
- 颜色偏差推断
- 棋子数量推断

下游 Lizzie 只能看到 `BlackLastMove` / `WhiteLastMove`，无法知道末手是否来自真实视觉标记。GMA 自动落子因此可能在不可信轮次上启动。

后续联调结论：真实视觉末手标记是辅助可信来源，不应成为跨平台唯一方案；Fox 让子初始局可由 Lizzie 在 `foxMoveNumber 0` + 全黑 setup 形态下按固定规则处理，ReadBoard 仍只输出棋盘和末手来源。

本设计已核对：

- `docs/specs/2026-06-24-gma-engine-decision-autoplay-design.md`
- `docs/specs/2026-05-20-fox-auto-play-color-detection-design.md`
- `docs/specs/2026-04-23-protocol-keyword-constants.md`
- `docs/specs/2026-05-04-sync-session-outbound-board-emitter-phase2.md`
- Lizzie `docs/SNAPSHOT_NODE_KIND.md`
- Lizzie `docs/specs/2026-06-24-readboard-gma-engine-decision-design.md`

## 目标

1. ReadBoard 明确输出当前末手来源，让下游区分真实视觉 marker 和启发式猜测。
2. 识别野狐默认不开手数时的右下角反色缺口，作为可信视觉来源。
3. 保持旧协议兼容：旧 Lizzie 忽略新增行，新 Lizzie 在缺少该行时保守处理 GMA 风险场景。
4. 不改变 GMA 的 `play>... gma` 协议形状，不新增第二条 Fox title/window polling 通道。

## Non-Goals

- 不改变 ReadBoard 的自动落子模式配置、GMA 参数语义或后台思考语义。
- 不改变 snapshot rebuild、history matching、conflict key 或 PASS/MOVE 语义。
- 不把启发式末手提升为真实视觉 marker。
- 不删除 `deviation` / `stoneCount` 兜底；下游不能把它们当成权威轮次。
- 不在 ReadBoard 里推断跨平台 side-to-play；手动“交换顺序”仍走既有 `pass` 协议。
- 不为非 Fox 平台引入特殊 title 轮询。

## 协议

新增 outbound line：

```text
lastMoveSource <token>
```

token：

| token | 含义 | 是否可作为视觉可信 marker |
| --- | --- | --- |
| `none` | 本帧没有末手 | 否 |
| `redBlueMarker` | 开启手数时的红/蓝角标 | 是 |
| `foxCornerFlip` | 野狐默认右下角反色缺口 | 是 |
| `deviation` | 颜色偏差推断 | 否 |
| `stoneCount` | 棋子数量推断 | 否 |

发送顺序：

```text
<window context lines>
forceRebuild
foxMoveNumber 5
lastMoveSource foxCornerFlip
<board lines>
end
```

`lastMoveSource` 应位于 `foxMoveNumber` 之后、棋盘 payload 行之前。当本帧没有 `foxMoveNumber` 时，它仍位于 window context / `forceRebuild` 之后、棋盘 payload 行之前。新 ReadBoard 对每个 outbound board snapshot 发送一条 `lastMoveSource`，没有末手时发送 `none`。

即使棋盘 payload 未变化，只要 source 变化也要让当前帧对下游可见，避免 source 被去重吞掉。

## ReadBoard 识别设计

新增轻量模型：

- `LastMoveSource`
- `LastMoveInference`，包含 `BoardCoordinate Coordinate` 和 `LastMoveSource Source`

`ApplyLastMoveInference(...)` 不再只返回坐标，而是按以下顺序返回带来源的结果：

1. `TryApplyMarkerLastMove(...)`：红/蓝强色唯一候选，source = `redBlueMarker`
2. `TryApplyFoxCornerFlipLastMove(...)`：右下反色缺口唯一候选，source = `foxCornerFlip`
3. `TryApplyDeviationLastMove(...)`：颜色偏差唯一候选，source = `deviation`
4. `TryApplyStoneCountLastMove(...)`：棋子数量兜底，source = `stoneCount`
5. 无候选：source = `none`

`PromoteLastMove(...)` 仍只负责把棋子状态提升为 `BlackLastMove` / `WhiteLastMove`；source 由调用层包装，避免把识别来源塞进棋盘格枚举。

## `foxCornerFlip` detector

输入只使用已识别为棋子的候选点，不重新决定棋子颜色。

每个候选棋子检查右下象限内的内圆/三角区域：

- 黑子末手：右下区域存在足量浅色/白色楔形像素。
- 白子末手：右下区域存在足量深色/黑色楔形像素。

约束：

- 阈值按 cell size 归一化，不使用只适配单一截图尺寸的绝对像素数。
- 必须唯一候选，并且最佳候选与第二候选有明确 margin。
- 多候选、低分、噪声或棋子主体颜色不稳定时，不返回 `foxCornerFlip`。
- detector 只负责 source 可信度，不改变 `BoardCellState.Black` / `White` 的分类结果。

当前用户样本裁切已保存为参考素材：

- `docs/specs/assets/2026-06-26-last-move-source/fox-corner-flip-black-last-move.png`
- `docs/specs/assets/2026-06-26-last-move-source/fox-corner-flip-white-last-move.png`
- `docs/specs/assets/2026-06-26-last-move-source/fox-numbered-blue-marker-white-last-move.png`
- `docs/specs/assets/2026-06-26-last-move-source/fox-numbered-red-marker-black-last-move.png`

这些图片先作为设计和测试素材候选；实现阶段再决定是否直接纳入 fixture，或转成更小的 synthetic fixture。

## 代码改动范围

ReadBoard 侧预计改动：

- `readboard/Core/Recognition/IBoardRecognitionService.cs`
- `readboard/Core/Models/BoardSnapshot.cs`
- `readboard/Core/Protocol/OutboundBoardSnapshotEmitter.cs`
- `readboard/Core/Protocol/IReadBoardProtocolAdapter.cs`
- `readboard/Core/Protocol/LegacyProtocolAdapter.cs`
- `readboard/Core/Protocol/ProtocolKeywords.cs`
- `readboard/Core/Protocol/SyncSessionCoordinator*.cs`
- `docs/specs/2026-04-23-protocol-keyword-constants.md`
- `tests/Readboard.VerificationTests/**`

Lizzie 侧 companion spec 位于 Lizzie feature worktree：

- `/home/dev/.config/superpowers/worktrees/lizzieyzy-next/kata-genmove-analyze-sync/docs/specs/2026-06-26-readboard-last-move-source-gma-turn-trust-design.md`

## 测试要求

ReadBoard：

1. 红/蓝角标识别输出 `redBlueMarker`。
2. 黑棋/白棋右下反色缺口识别输出 `foxCornerFlip`。
3. deviation fallback 输出 `deviation`。
4. stone-count fallback 输出 `stoneCount`。
5. 多候选或噪声图不输出可信视觉 source。
6. outbound 协议包含 `lastMoveSource`，顺序在 `foxMoveNumber` 后、棋盘行前。
7. `ProtocolKeywords` 锁定新 wire token。
8. `docs/specs/2026-04-23-protocol-keyword-constants.md` 记录新 wire token。
9. payload 未变化但 source 变化时仍发送可见帧。
10. `foxCornerFlip` 至少覆盖黑/白正例、双候选负例和低 margin 负例。

Lizzie 联调目标：

1. `redBlueMarker` / `foxCornerFlip` 与 `foxMoveNumber` 奇偶冲突时，Lizzie 以视觉 marker 推断 side-to-play。
2. `deviation` / `stoneCount` 不作为让子棋/GMA 权威轮次。
3. 旧协议缺少 `lastMoveSource` 时普通同步保持兼容。

## 成功标准

- ReadBoard wire output 可以区分视觉末手和启发式末手。
- 野狐默认右下角缺口能在唯一候选场景下稳定输出 `foxCornerFlip`。
- Lizzie GMA 不会因为让子棋标题手数或 stone-count 误判而错误启动。
- 现有 GMA、Fox title/status、outbound emitter 和协议关键字测试继续通过。
