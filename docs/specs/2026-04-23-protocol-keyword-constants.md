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
- 仅常量化不允许改 `LegacyProtocolAdapter` 的 parse / emit 语义。

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
| outbound prefix | `start ` |
| outbound prefix | `play>` |
| outbound separator | `>` |
| outbound command | `noinboard` |
| outbound command | `placeComplete` |
| outbound command | `error place failed` |
| outbound prefix | `timechanged ` |
| outbound prefix | `playoutschanged ` |
| outbound prefix | `firstchanged ` |
| outbound command | `noponder` |
| outbound command | `stopAutoPlay` |
| outbound command | `pass` |
| outbound command | `yikeSyncStart` |
| outbound command | `yikeSyncStop` |
| inbound command | `yikeBrowserSyncStop` |
| outbound fallback | `0` |
