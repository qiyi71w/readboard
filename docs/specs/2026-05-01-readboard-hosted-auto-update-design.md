# readboard 宿主托管自动更新设计

日期: 2026-05-01

## 背景

当前 `readboard` 的检查更新弹窗只把“去下载”按钮指向 GitHub release 页面。`docs/specs/2026-04-24-update-download-link.md` 明确把 in-app download、installer execution、auto-update 列为不在范围内。

这次需求重新打开该范围，但只针对 LizzieYzy-Next 内置的 Windows 原生 `readboard/`。用户仍从 `readboard` 自己的检查更新弹窗点更新，实际替换由宿主 LizzieYzy-Next 完成。

## 目标

- 用户在 `readboard` 更新弹窗内点击更新后，不再需要手动打开浏览器、下载 zip、替换目录。
- `readboard` 负责检查 release、选择并下载自己的发布包、做基础校验。
- LizzieYzy-Next 负责停止当前 `readboard` 进程、备份现有 `readboard/`、替换文件、重启原生同步工具。
- 替换失败时保留旧版本，尽量回滚，并给用户一个可理解的失败提示。

## 已核对的现有约束

- `docs/specs/2026-04-24-update-download-link.md`: 旧设计只打开 GitHub release 页；本设计作为后续设计覆盖其 auto-update non-goal。
- `docs/specs/2026-04-23-protocol-keyword-constants.md`: readboard 和 LizzieYzy-Next 的 wire 文本是兼容边界；新增消息必须同步更新两侧解析和协议契约测试。
- `docs/DEVELOPMENT.md`: readboard 是 LizzieYzy-Next 的外接程序；涉及接口、目录结构、发布产物、启动方式时必须检查宿主。
- `GitHubUpdateChecker`: 当前只读取 latest release 的 `html_url`，没有解析 release assets。
- `FormUpdate`: 当前只校验 http/https URL，然后用 Windows shell 打开。
- LizzieYzy-Next `ReadBoard.java`: Windows 原生同步工具默认解析到工作目录或 jpackage 布局中的 `readboard/readboard.exe`，进程工作目录也是该 `readboard/`。
- LizzieYzy-Next `LizzieFrame.java`: 已有 `openBoardSync`、`reopenReadBoard`、`shutdownReadBoard`、`startReadBoard` 生命周期，可复用停止和重启逻辑。

## 方案选择

### 方案 A: readboard 直接覆盖自身目录

readboard 下载 zip 后自己停止、替换 `AppDomain.CurrentDomain.BaseDirectory`，再重新启动。

优点是宿主改动少。缺点是正在运行的 exe/dll 很容易被文件锁挡住，readboard 也无法可靠知道宿主真实选择的是哪个 `readboard/` 目录；失败后恢复路径复杂。

不采用。

### 方案 B: readboard 准备更新包，宿主完成替换

readboard 在检查更新弹窗中下载 release zip 到用户缓存目录，校验后通过现有 pipe 文本协议通知宿主。宿主确认后停止 readboard、备份、替换、重启。

优点是职责边界清楚：子进程不替换自己，宿主掌握安装目录和生命周期。缺点是要同时改 readboard 和 LizzieYzy-Next，并新增一个协议消息。

采用。

### 方案 C: 改为全量 LizzieYzy-Next 更新器

readboard 按钮触发宿主全量更新 LizzieYzy-Next，包括主程序和内置 readboard。

优点是长期统一。缺点是范围明显变大，涉及签名、主程序替换、权限、跨平台发布和完整回滚，不适合作为本次 readboard 更新入口。

不采用。

## 用户流程

1. 用户在 readboard 主窗口点击 `检查更新`。
2. 如果发现新版本，弹窗显示版本、日期和 release notes。
3. 按钮文案在内置宿主模式下显示为 `下载并安装`；不能确认宿主支持时仍显示为 `去下载` 并保留打开 GitHub release 页的旧行为。
4. 用户点击 `下载并安装` 后，readboard 下载 release zip，显示下载/准备状态，按钮禁用避免重复点击。
5. readboard 校验成功后向宿主发送 `readboardUpdateReady` 消息。
6. 宿主弹出确认，说明即将重启棋盘同步工具。
7. 用户确认后，宿主停止当前 readboard，备份旧目录，替换为新目录，重新启动原生 readboard。
8. 成功后宿主提示更新完成；失败时提示失败原因，旧版本继续可用或已恢复。

## readboard 侧设计

### Release asset 选择

`GitHubUpdateChecker` 继续使用 GitHub latest release API，但扩展解析 `assets`。目标 asset 规则：

- release `tag_name` 使用 `vX.Y.Z` 格式；语义版本比较继续使用规范化后的 `X.Y.Z`。
- 文件名必须等于 `readboard-github-release-<tag_name>.zip`，例如 `readboard-github-release-v3.0.2.zip`。
- 文件名里的 tag 必须和 release `tag_name` 一致。
- `browser_download_url` 必须是 absolute `https` URL。
- 如果找不到匹配 asset，更新弹窗回退到旧的 GitHub release 页面下载行为。

`UpdateCheckResult` 增加可选字段保存 asset 下载 URL、asset 文件名和 asset size。`ReleaseUrl` 继续保留 release 页面 URL，兼容旧按钮路径。

### 下载和校验

下载目录固定为用户缓存，不写入程序目录：

```text
%LOCALAPPDATA%\LizzieYzyNext\readboard-updates\<tag_name>\
```

下载完成后 readboard 做轻量校验：

- 文件名与目标版本一致。
- zip 可打开。
- 解压预检能找到顶层目录或根目录中的 `readboard.exe`。
- 包内包含 `readboard.dll`、`readboard.runtimeconfig.json`、`readboard.deps.json`、`language_cn.txt`。
- 不接受包含绝对路径或 `..` 路径穿越的 zip entry。

readboard 不解压到宿主安装目录，只能解压到同一缓存目录下的 staging/preflight 目录，或仅通过 zip API 做结构校验。

### 通知宿主

新增一条 outbound wire 文本：

```text
readboardUpdateReady <version>\t<absoluteZipPath>
```

规则：

- `<version>` 使用 release tag，即 `vX.Y.Z` 格式。
- `<absoluteZipPath>` 必须是下载完成后的本地绝对路径。
- 使用 tab 分隔路径，避免 Windows 路径中的空格破坏解析。
- readboard 发送消息后不退出、不替换自身、不主动关闭 UI。
- 如果宿主不认识该消息，最坏结果只是忽略；readboard 弹窗需要在超时或无确认时提示“请手动下载”并保留 release 页面入口。

新增协议关键字时必须更新 `ProtocolKeywords`、协议契约测试，并在 LizzieYzy-Next 的 `ReadBoard.parseLine` 中加入解析。

## LizzieYzy-Next 侧设计

宿主新增 readboard 更新安装服务，职责只限 Windows 原生 `readboard/`：

1. 解析 `readboardUpdateReady`。
2. 校验版本和 zip 路径：
   - 路径必须存在且是文件。
   - 文件名必须匹配 `readboard-github-release-<version>.zip`，其中 `<version>` 是 `vX.Y.Z` tag。
   - zip 内必须包含可执行的 readboard release 文件集合。
3. 在 Swing 线程提示用户确认。
4. 用户确认后，后台线程执行安装：
   - 调用现有 `shutdownReadBoard(existingReadBoard)`。
   - 等待 readboard 进程退出。
   - 解析当前宿主实际使用的 native readboard 目录。
   - 将当前 `readboard/` 移动或复制为 `readboard.backup-<yyyyMMdd-HHmmss>`。
   - 解压 zip 到同级 staging 目录。
   - 原子性尽量高地把 staging 改名为 `readboard/`。
   - 调用现有 `startReadBoard(this::createNativeReadBoard)` 或 `openBoardSync()` 重启。
5. 成功后删除 staging；保留最近一次 backup 供人工排查。
6. 失败时删除 staging，若 `readboard/` 不可用则恢复 backup，并提示错误。

宿主必须复用 `ReadBoard.legacyNativeReadBoardDirectory()` 或等价解析逻辑，不在 readboard 侧硬编码宿主路径。

## 错误处理

- GitHub latest API 失败：保持现有“检查更新失败”路径。
- 找不到可自动安装 asset：按钮继续打开 release 页面。
- 下载失败：弹窗显示失败，按钮恢复可点击，保留手动下载入口。
- 校验失败：不通知宿主，提示包无效并保留手动下载入口。
- 宿主不支持新协议：readboard 在提示中说明当前宿主需要手动下载，或等待宿主升级后使用自动安装。
- 安装中 readboard 退出失败：宿主取消替换，提示稍后重试。
- 文件替换失败：宿主尝试恢复 backup；恢复失败时提示用户打开 readboard 目录手动处理。

## UI 文案

readboard 更新弹窗新增状态，不新增单独设置项：

- `去下载`: 旧路径，打开 GitHub release 页。
- `下载并安装`: 有匹配 asset 且可尝试宿主安装。
- `下载中...`: 下载进行中。
- `等待宿主安装...`: 已通知宿主。

宿主确认弹窗文案由 LizzieYzy-Next 本地化资源维护，核心意思是“将重启棋盘同步工具以安装 readboard vX.Y.Z”。

## 不在范围内

- 不做 readboard 自我覆盖安装。
- 不做后台静默更新。
- 不做全量 LizzieYzy-Next 更新。
- 不做 macOS/Linux readboard 自动更新。
- 不做签名校验或私钥发布流程；第一版只做 HTTPS 下载、版本文件名匹配、zip 结构校验和路径穿越防护。
- 不改变现有 release zip 的目录结构，除非后续实现时发现脚本和 GitHub asset 结构不一致。
- 不把安装目录写进 readboard 配置。

## 测试计划

readboard 仓库：

- `GitHubUpdateChecker` 解析 `assets`，能选中 `readboard-github-release-vX.Y.Z.zip`。
- 找不到匹配 asset 时仍返回 release 页面 URL。
- asset URL 非 https、文件名版本不匹配、缺少 required fields 时走失败或回退路径。
- 下载校验拒绝路径穿越 zip。
- `FormUpdate` 在可安装和不可安装两种模型下按钮行为正确。
- 协议测试锁定 `readboardUpdateReady` wire 文本。

LizzieYzy-Next 仓库：

- `ReadBoard.parseLine` 能识别 `readboardUpdateReady` 并把版本和 zip path 交给安装服务。
- 安装服务能从 jpackage `app/readboard` 和普通工作目录 `readboard` 两种布局解析目标目录。
- 成功安装时备份旧目录、替换新目录、重启 native readboard。
- 替换失败时恢复 backup。
- 恶意 zip entry、缺少 `readboard.exe`、版本文件名不匹配时拒绝安装。

集成验证：

- 用本地构造的 `readboard-github-release-vX.Y.Z.zip` 走完整下载准备到宿主替换流程。
- 验证更新后宿主仍能启动原生同步工具并完成 `version` 握手。

## 交付顺序

1. readboard: 扩展 update result、release asset 解析、下载和预检，但保留旧浏览器下载 fallback。
2. readboard: 新增 `readboardUpdateReady` 协议发送和弹窗状态。
3. LizzieYzy-Next: 新增协议解析和 readboard 安装服务。
4. LizzieYzy-Next: 接入现有 readboard 生命周期，完成停止、备份、替换、重启。
5. 两仓分别补测试，再做本地集成验证。

## 自审记录

- 已避免让 readboard 替换自身目录，降低文件锁和半安装风险。
- 新协议只在用户点击更新后发送，不影响常规同步、棋盘识别和自动落子。
- 旧宿主或找不到 asset 时保留 GitHub release 页 fallback。
- 需要用户确认：第一版是否接受“无签名，仅 HTTPS + 文件名版本 + zip 结构校验”的安全边界。
