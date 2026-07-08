# ReadBoard WebView2 UI 重构设计

日期：2026-07-08

## 背景

ReadBoard 仍是 LizzieYzy-Next 调用的 Windows 棋盘同步工具。它负责截图、识别棋盘、同步棋盘状态，并通过旧文本协议把结果发给宿主；LizzieYzy-Next 负责引擎加载、棋盘显示、选点、胜率和分析。

本次用户确认的方向是全面改成 WebView2 UI，并且不再沿用旧 WinForms 视觉。参考界面是一个现代桌面工具面板：左侧导航、顶部状态、分组表单、底部日志输出；不显示棋盘、不显示胜率、不做引擎分析面板。`引擎条件` 区域保留参数输入，但去掉右侧“引擎决策落子时生效”提示。

## 已核对的现有约束

- `docs/DEVELOPMENT.md`: ReadBoard 是 LizzieYzy-Next 外接程序；启动参数、协议、发布结构变更必须检查宿主。
- `docs/specs/2026-04-21-mainform-theme-startup-parity-design.md`: 旧目标是统一到历史 WinForms 主题。本次用户明确要求全新 WebView2 视觉，因此该视觉目标被本设计覆盖。
- `docs/specs/2026-04-21-readboard-legacy-desktop-layout-design.md`: 旧目标是恢复 WinForms 桌面布局。本次不继续以旧 WinForms 布局为最终目标。
- `docs/specs/2026-04-23-mainform-state-boundaries.md`: `AppConfig` 是持久配置快照，不是所有 UI 编辑中间态的实时唯一来源。
- `docs/specs/2026-04-23-sync-session-state-locking.md`: `SyncSessionCoordinator` 的状态锁约定不因 UI 重构改变。
- `docs/specs/2026-04-23-protocol-keyword-constants.md`: wire 文本是公共合约；新增或改变协议必须同步测试和宿主解析。
- `docs/specs/2026-04-22-dotnet10-upgrade-design.md`: 旧 non-goal 写过“不迁移到 WPF/MAUI/Blazor”，未禁止 WebView2；“不改变与 LizzieYzy-Next 的通信协议”仍保留。
- `docs/specs/2026-05-04-board-debug-diagnostics-writer.md`: 调试诊断写盘是异步文件诊断，不应被 UI 日志面板改成同步热路径。
- `docs/specs/2026-05-01-readboard-hosted-auto-update-design.md`: 更新流程和宿主托管安装协议保持不变。
- `docs/specs/2026-05-20-fox-auto-play-color-detection-design.md`、`2026-06-24-gma-engine-decision-autoplay-design.md`、`2026-06-26-last-move-source-gma-turn-trust-design.md`: 自动落子和 GMA 是授权/参数/协议行为，不是 ReadBoard 内部引擎面板。

## 目标

- 用 WebView2 替换主窗口和用户可见对话框的视觉层。
- 保留现有 C# 核心：启动、配置、截图、识别、同步状态机、协议、自动落子、更新、诊断写盘。
- 保持 LizzieYzy-Next 启动方式和旧文本协议兼容。
- 主界面只展示同步工具需要的信息：宿主状态、平台/房间/手数、平台类型、棋盘规格、同步与自动落子、棋盘选择、日志输出。
- 底部新增紧凑 `日志输出`，显示最近运行事件，不替代调试诊断文件。
- 最终发布包不暴露旧 WinForms 样式作为用户可见 UI。

## 非目标

- 不在 ReadBoard 显示棋盘、胜率、候选点列表、PV 或引擎分析图。
- 不让 ReadBoard 启动或控制 KataGo。
- 不改变 `readboard.exe yzy <aiTime> <playouts> <firstPolicy> <transport> <language> <tcpPort>` 启动参数。
- 不改变现有 wire 文本，除非后续实现发现必须新增协议并另写规格。
- 不引入 Electron。
- 第一版不引入 React/Vite/前端构建链；静态 HTML/CSS/JS 足够覆盖这个工具面板。若后续 UI 状态复杂到静态脚本难维护，再单独评估。

## UI 结构

主窗口使用一个 WebView2 页面：

- 顶部：`ReadBoard / 棋盘同步工具`、版本、连接/同步状态、最后同步时间、棋子数、耗时、设置/帮助/主题/检查更新入口。
- 状态条：平台、房间或标题上下文、手数、下一手、标题绑定状态。
- 左侧导航：控制中心、参数设置、规则说明、关于；快速操作包括快速同步、持续同步、单次同步、分析/停止、交换顺序、强制重建、清空棋盘。
- 主区域：
  - `平台类型`: 野狐、野狐(后台落子)、弈客、弈城、新浪、其他(后台)、其他(前台)。
  - `棋盘规格`: 19x19、13x13、9x9、自定义宽高。
  - `同步与自动落子`: 双向同步、自动落子、执黑/执白/自动、身份按钮、落子方式。
  - `引擎条件`: 每手用时、最大计算量、首选计算量。标题右侧不显示额外说明胶囊。
  - `棋盘选择方式`: 点击棋盘内部、框选棋盘、框选 1 路线、原棋盘上显示选点。
  - `日志输出`: 4-6 行默认可见，保留最近事件，可滚动。

设置、规则说明、关于、更新、野狐身份选择最终也应使用 WebView2 视图或 WebView2 弹层。实现可以分阶段迁移，但最终 release 不应混用旧 WinForms 对话框视觉。

## 架构

保留 WinForms 作为进程入口和 WebView2 宿主：

- `Program.cs` 仍解析宿主启动参数、创建 transport、初始化配置和语言。
- `MainFormRuntimeComposer` 仍装配同步运行时依赖。
- 新主窗体只承载 WebView2、窗口生命周期和 C# / JS 桥接。
- `Core` 下的同步、协议、截图、识别、自动落子、落子执行和诊断写盘不迁移到 JavaScript。

WebView2 资源放在本地静态目录，随应用输出：

```text
readboard/WebView/index.html
readboard/WebView/styles.css
readboard/WebView/app.js
```

只加载本地资源。WebView2 初始化后阻止外部导航；需要打开 GitHub、帮助文件或目录时，经 C# 命令处理。

## C# / JavaScript 桥

桥接使用 WebView2 的 JSON message：

- JS -> C#: 用户命令，例如同步、停止、选择平台、修改棋盘规格、修改自动落子参数、选择棋盘、打开设置、检查更新。
- C# -> JS: UI 状态快照和增量事件，例如连接状态、同步状态、平台上下文、棋盘规格、自动落子状态、按钮启用状态、更新状态、日志行。

第一版桥接只需要两个内部模型：

- `ReadBoardUiState`: 当前 UI 可渲染状态。
- `ReadBoardUiCommand`: 用户操作命令。

不要为每个按钮建立独立接口或一组单实现 service。命令分发留在主窗体附近，等重复和复杂度真实出现后再拆。

## 日志输出

UI 日志是内存环形缓冲，默认保留最近 100 行。来源只包括面向用户有价值的事件：

- 宿主连接和协议 ready。
- 开始/停止持续同步。
- 单次同步成功或失败。
- 棋盘捕获、识别成功/失败摘要。
- 已发送同步结果。
- 自动落子授权、跳过和失败摘要。
- 更新检查和托管安装状态。

日志不写入新文件，不替代 `BoardDebugDiagnosticsWriter`。调试诊断仍由现有设置控制并异步写盘。

## 兼容性

- 旧 wire 协议逐字保持；协议测试继续锁定 `ProtocolKeywords`。
- 配置仍由 `DualFormatAppConfigStore` 读写 JSON 和 legacy 镜像。
- 启动失败、无宿主参数、pipe/TCP 选择逻辑保持不变。
- 语言资源第一版复用现有 `Program.langItems` 注入前端，避免维护第二套本地化源。
- WebView2 运行时不可用时给出明确错误提示；不回退到旧 WinForms UI。

## 实施顺序

1. 加 WebView2 依赖和本地静态资源复制规则，建立空 WebView2 shell。
2. 抽当前主窗体可渲染状态，先渲染静态主界面和按钮启用状态。
3. 接入最小命令：快速同步、持续同步、单次同步、停止/分析、交换顺序、强制重建、清空棋盘。
4. 接入平台、棋盘规格、同步与自动落子、引擎条件、棋盘选择。
5. 接入日志输出和更新状态。
6. 迁移设置、规则说明、关于、更新、野狐身份选择等对话框。
7. 删除或隔离旧 WinForms UI 视觉路径，保留必要的 non-visual 逻辑。

## 验收

- `dotnet test .\readboard.sln` 通过。
- `scripts/run-readboard-ui-debug.ps1` 能模拟宿主启动并显示 WebView2 UI。
- pipe 和 TCP 启动路径不变。
- 主界面无棋盘、无胜率、无引擎分析区域。
- `日志输出` 可见且不会阻塞持续同步热路径。
- `引擎条件` 右侧不显示旧提示。
- 100% / 125% / 150% DPI 下主界面不裁切、不重叠。
- 打包脚本产物包含 WebView2 静态资源，并仍生成 `readboard.exe`。

