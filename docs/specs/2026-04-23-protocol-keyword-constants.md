# Protocol Keyword Constants Boundary

日期: 2026-04-23

## 背景

`readboard` 和 `lizzieyzy-next` 之间的同步协议仍是逐行字符串协议。`readboard/Core/Protocol/LegacyProtocolAdapter.cs` 负责发出和解析这些旧协议文本，`lizzieyzy-next/src/main/java/featurecat/lizzie/analysis/ReadBoard.java` 直接用字符串字面值做 `startsWith` / `equals` 解析。

2026-04-23 扫描 `D:\dev\weiqi\lizzieyzy-next` 后确认：集成端没有可复用的跨仓库 enum、proto 或共享常量。readboard 内部常量化只能作为本仓库内的别名，不能改变协议。

## 边界规则

- `ProtocolKeywords` 是内部实现细节，不是新的公共 API。
- 常量类型必须保持为 `string`。不要改成 enum，因为 wire 文本本身才是公共合约。
- 常量值必须与旧裸字符串逐字相同，包括大小写、空格、拼写和前后缀。
- 前缀常量要保留协议里已经存在的尾随空格，例如 `syncPlatform `、`roomToken `、`start `。
- 不修正历史拼写差异，例如 `noinboard` 与 `notinboard` 都是有效旧协议文本。
- 新增协议关键字时，必须同时更新本文件、`ProtocolKeywords`、协议契约测试，并重新核对 `lizzieyzy-next` 解析端。
- Lizzie parser 必须容忍并消费 ReadBoard 新增的 outbound 行；旧端不能因为未知 `lastMoveSource` 行破坏普通同步。
- 仅常量化不允许改 `LegacyProtocolAdapter` 的 parse / emit 语义。

## 2026-07-12 增量控制命令

WebView2 控制中心新增三组可选能力，所有既有 wire 文本保持逐字不变：

- `clearBoard`：ReadBoard 请求宿主停止同步后的显式主棋盘清空。它不能复用旧 `clear`；旧 `clear` 仍只重置同步缓存和临时状态。
- `resumeponder`：ReadBoard 请求宿主恢复主引擎分析。暂停继续使用既有 `noponder`。
- `analysisState running` / `analysisState paused`：宿主在 native ReadBoard ready 后及执行暂停/恢复后回传实际分析状态，同时作为恢复能力声明。

新 ReadBoard 连接旧宿主时，`noponder` 继续可用；没有收到 `analysisState` 时不会发送 `resumeponder`。旧宿主可能按历史 `startsWith("clear")` 逻辑把 `clearBoard` 降级为缓存清理，因此真正清空 Lizzie 主棋盘要求宿主与 ReadBoard 同时升级。Java 简易版 fallback 不接收这些新增能力行。

## 2026-04-23 实现结果

- 新增 `readboard/Core/Protocol/ProtocolKeywords.cs`，集中定义旧协议 wire 文本。
- `readboard/Core/Protocol/LegacyProtocolAdapter.cs` 的 parse / emit 路径已改为引用 `ProtocolKeywords`。
- 新增 `ProtocolKeywords_DefineStableLegacyWireTokens` 回归测试，锁定每个常量的字面值。
- 本轮只改 readboard 内部实现；`lizzieyzy-next` 仍按既有字符串解析，不需要同步代码变更。
- 验证结果：solution build 0 错误；主测试项目通过。

## 已锁定的 wire 文本

| 常量类别 | wire 文本 |
|---|---|
| inbound command | `place` |
| inbound command | `loss` |
| inbound/outbound command | `notinboard` |
| inbound command | `version` |
| inbound command | `quit` |
| outbound command | `ready` |
| outbound command | `clear` |
| outbound command | `clearBoard` |
| outbound command | `end` |
| outbound command | `playponder on` |
| outbound command | `playponder off` |
| outbound prefix | `version: ` |
| outbound command | `sync` |
| outbound command | `stopsync` |
| outbound command | `endsync` |
| outbound command | `bothSync` |
| outbound command | `nobothSync` |
| outbound command | `foreFoxWithInBoard` |
| outbound command | `notForeFoxWithInBoard` |
| outbound prefix | `syncPlatform ` |
| outbound fallback | `generic` |
| outbound prefix | `roomToken ` |
| outbound prefix | `liveTitleMove ` |
| outbound prefix | `recordCurrentMove ` |
| outbound prefix | `recordTotalMove ` |
| outbound command | `recordAtEnd 1` |
| outbound command | `recordAtEnd 0` |
| outbound prefix | `recordTitleFingerprint ` |
| outbound command | `forceRebuild` |
| outbound prefix | `foxMoveNumber ` |
| outbound prefix | `lastMoveSource ` |
| outbound token | `none` |
| outbound token | `redBlueMarker` |
| outbound token | `foxCornerFlip` |
| outbound token | `deviation` |
| outbound token | `stoneCount` |
| outbound prefix | `start ` |
| outbound prefix | `play>` |
| outbound separator | `>` |
| outbound token | `gma` |
| outbound command | `noinboard` |
| outbound command | `placeComplete` |
| outbound command | `error place failed` |
| outbound prefix | `timechanged ` |
| outbound prefix | `playoutschanged ` |
| outbound prefix | `firstchanged ` |
| outbound command | `noponder` |
| outbound command | `resumeponder` |
| inbound command | `analysisState running` |
| inbound command | `analysisState paused` |
| outbound command | `stopAutoPlay` |
| outbound command | `pass` |
| outbound command | `yikeSyncStart` |
| outbound command | `yikeSyncStop` |
| inbound command | `yikeBrowserSyncStop` |
| outbound fallback | `0` |
