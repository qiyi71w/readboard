# ReadBoard 更新通道与旧系统维护线设计

## 背景

v3.0.9 是最后一个使用现有 WinForms UI、明确支持旧版 Windows 的稳定版本。v3.1.0 起主线切换到 WebView2 UI，当前目标框架最低为 Windows 10 version 1809（build 17763）。项目后续保留独立的 WinForms 维护线，选择性回移修复和部分新功能。

现有更新器直接请求 GitHub `/releases/latest`，无法区分 WebView2 主线和旧系统维护线。现有 ReadBoard 与 LizzieYzy-Next 的托管安装器还共同硬编码了 `readboard-github-release-<tag>.zip`，因此仅修改 Release 包名会破坏宿主安装。

## 目标

- 根据当前 Windows 版本自动选择唯一兼容的更新通道。
- 让 v3.0.x WinForms 维护线和 v3.1.0+ WebView2 主线独立发布。
- 防止旧系统和旧客户端自动安装 WebView2 主线包。
- 保留 LizzieYzy-Next 托管替换安装，并明确新旧宿主的能力边界。
- 建立可审查、可撤回且不会提前暴露未发布版本的 Release 流程。
- 建立 Windows、WSL 和全部 worktree 共用的 ReadBoard Release skill。

## 已核对的既有契约

- `docs/specs/2026-05-01-readboard-hosted-auto-update-design.md`：ReadBoard 下载、校验并通知宿主，LizzieYzy-Next 负责替换安装；ReadBoard 不自我替换。
- `docs/specs/2026-04-24-update-download-link.md`：无托管安装能力时保留手动下载路径。
- WebView2 工作树的 `docs/specs/2026-07-08-webview2-ui-shell-design.md`：主线使用 Evergreen Runtime，不回退旧 WinForms UI，最低目标为 Windows 10 version 1809。
- WebView2 设计文档当前使用的 `legacy/win7` 已过时，实施时必须改为 `legacy/winforms`。
- 现有本地 `readboard-github-packaging` skill 只存在于 Windows 主工作副本且仍描述 .NET Framework 4.8/x86/MSBuild 契约，不能作为当前发布流程依据。

## 发布线与分支

### 主线

- Git 分支：`main`
- 更新通道：`main`
- 版本：v3.1.0 及以上
- UI：WebView2
- 最低系统：Windows 10 version 1809（10.0.17763）
- Release 资产：`readboard-webview2-<tag>.zip`

### 旧系统维护线

- Git 分支：`legacy/winforms`
- 更新通道：`legacy-windows`
- 版本：固定使用 v3.0.x
- UI：WinForms
- 系统范围：低于 Windows 10 version 1809
- Release 资产：继续使用 `readboard-github-release-<tag>.zip`

回移到旧系统维护线的新功能仍只递增 patch。共享修复和功能默认先在 `main` 实现，再按独立 commit 选择性 cherry-pick 到 `legacy/winforms`。分叉后禁止把整个 `main` 合并回维护分支。WinForms 或旧系统专属修复可直接起源于维护分支，并在主线同样受影响时单独向前移植。

## v3.0.9 过渡顺序

1. 在 `main` 完成并发布 v3.0.9。
2. v3.0.9 首次同时晋升到 `main` 和 `legacy-windows` 通道。
3. 从 v3.0.9 tag 创建 `legacy/winforms`。
4. 未完成的 `ui/webview2-rewrite` 合并 v3.0.9 后继续独立开发。
5. v3.1.0 发布后仅推进 `main`，`legacy-windows` 保持 v3.0.9，直到新的 v3.0.x 被晋升。

## 中央通道清单

仓库 `main` 分支根目录维护唯一的 `update-channels.json`。客户端从固定地址获取清单，不再使用 `/releases/latest` 推断可安装版本。

建议 schema：

```json
{
  "schemaVersion": 1,
  "channels": [
    {
      "id": "legacy-windows",
      "status": "active",
      "maximumWindowsVersionExclusive": "10.0.17763",
      "latestTag": "v3.0.9",
      "assetName": "readboard-github-release-v3.0.9.zip",
      "sha256": "<64 lowercase hex characters>"
    },
    {
      "id": "main",
      "status": "active",
      "minimumWindowsVersion": "10.0.17763",
      "latestTag": "v3.0.9",
      "assetName": "readboard-github-release-v3.0.9.zip",
      "sha256": "<64 lowercase hex characters>"
    }
  ]
}
```

规则：

- `schemaVersion` 不受支持时检查失败。
- 未知字段可忽略；必填字段缺失、重复通道 ID、非法版本、非法 SHA-256 或非法状态均使清单无效。
- Windows 范围使用下限包含、上限排除语义。
- 当前系统必须且只能匹配一个通道；重叠范围使清单无效，没有匹配项表示当前系统没有维护通道。
- 通道状态仅允许 `active` 和 `retired`。
- `retired` 不再接受新版本晋升，但仍允许用户安装最后一个已晋升稳定版本，并明确提示这是最终维护版本。
- 普通用户不能手动切换通道。
- 清单下载、解析或验证失败时只报告检查失败，不回退 `/releases/latest`、缓存清单或其他自动选版逻辑。

.NET 10 使用 `Environment.OSVersion.Version` 获取实际 Windows 版本；不增加 `RtlGetVersion` P/Invoke。

## Release 解析与更新状态

客户端根据通道中的 `latestTag` 请求对应 GitHub Release，并验证：

- Release 已发布，不是 draft 或 prerelease。
- Release tag 与 `latestTag` 完全一致。
- Release 中存在 `assetName` 指定的 HTTPS 资产。
- 下载完成后 SHA-256 与清单完全一致。
- SHA-256 通过后再执行现有 ZIP 路径安全和必需文件校验。

更新结果至少区分：

- 当前版本低于通道版本：可更新。
- 当前版本等于通道版本：已是最新；retired 通道显示最终版本提示。
- 当前版本高于通道版本：当前版本已不在该通道中，不自动降级。
- 没有匹配通道：当前系统没有维护通道。
- 清单或 Release 无效：检查失败。

旧系统用户手动检查更新时，可以看到主线存在更高版本及其最低系统要求，但不显示主线安装按钮或直接下载入口。当前更新检查仍只由用户点击触发，不新增启动自动检查。

## 托管安装兼容

### v1

- 宿主声明：`readboardUpdateSupported`
- 支持资产：`readboard-github-release-<tag>.zip`

### v2

- 宿主额外声明：`readboardUpdatePackageV2Supported`
- 支持资产：旧前缀和 `readboard-webview2-<tag>.zip`
- `readboardUpdateReady\t<tag>\t<absolute-path>` 保持不变。
- 宿主只接受两种明确前缀，不放宽为任意 ZIP 名称。

ReadBoard 只有收到 v2 能力后才为 WebView2 命名资产提供“下载并安装”。旧宿主仍可安装 v3.0.x 资产；面对 WebView2 资产时只提供手动下载。

包含 v2 能力的 LizzieYzy-Next 必须先发布并完成真实安装验证，之后才能把 v3.1.0 晋升到 `main`。

## WebView2 Runtime 分发

v3.1.0 只发布一个 Evergreen 包：`readboard-webview2-<tag>.zip`。

- 包含 WebView2 SDK 程序集、`WebView2Loader.dll` 和本地 UI 静态资源。
- 不携带完整 Fixed Version Runtime。
- 启动前检查系统共享 Evergreen Runtime。
- 缺失时使用原生 WinForms 提示，提供打开微软官方安装页面、重试和退出。
- 不静默安装 Runtime，不回退旧 WinForms UI。
- Fixed Version 离线包仅在出现明确离线需求后另行设计。

## Release 与通道晋升

### Release PR

- 目标分支按产品线选择 `main` 或 `legacy/winforms`。
- 修改程序集版本、代码和 `docs/releases/<tag>.md`。
- Release 描述由 skill 先生成草稿并展示，用户确认后才写文件。
- Release PR 标题和正文另行展示，用户确认后才创建远程 PR。
- 每个版本永久保留独立 changelog；workflow 使用 tag 对应文件作为 `body_path`。

### Tag 与正式 Release

- tag 指向最终通过验证的最新 commit，不要求版本号修改是最后一个 commit。
- skill 展示 tag、目标分支、完整 commit SHA、changelog、通道和预期资产后等待独立确认。
- tag 推送触发 Actions；Actions 校验版本、测试、打包并直接创建正式 Release。
- 正式通道禁止指向 draft 或 prerelease。

### Channel Promotion

- Release 和资产确认可用后，创建独立通道晋升 PR。
- 无论发布来自哪个代码分支，晋升 PR 始终目标 `main`。
- PR 更新中央清单中的 tag、资产名和 SHA-256。
- manifest diff 和 PR 正文必须展示并等待用户确认。
- 晋升 PR 合并后客户端才发现新版本。

### 紧急撤回

- 通过紧急 PR 把通道指针退回上一个稳定 Release，停止更多安装。
- 已安装更高版本的客户端显示“当前版本已不在通道中”，不自动降级。
- 不移动旧 tag，不替换同名资产。
- 修复后使用更高 patch 发布并正常晋升。

## 共享 Release Skill

创建共享 personal skill：

```text
C:\Users\admin\.codex\skills\readboard-release
```

WSL 的 `~/.codex` 与 Windows 共用，因此 Windows、WSL 和所有 worktree 使用同一份 skill，不在各 worktree 复制文件。

skill 负责：

- 区分普通 PR、Release PR 和 Channel Promotion PR。
- 检查当前分支、目标产品线、版本系列和资产命名。
- 生成并确认 Release 描述。
- 生成并确认 PR 标题和正文。
- 执行确定性测试与打包验证。
- 在用户确认后 commit/push/创建 PR。
- PR 合并后重新核对最终 commit，并在用户独立确认后创建和推送 tag。
- 检查 Release 是否正式发布、workflow 是否成功、资产是否存在，并计算 SHA-256。
- 生成并确认通道晋升 PR。
- 支持紧急撤回通道指针，但不自动降级或改写 tag。

现有被忽略且过时的 `readboard-github-packaging` 本地 skill 在有效规则迁移后删除，避免重复触发和旧契约误导。通用 `github-pr` skill 保持不变，普通 ReadBoard PR 继续使用通用流程。

## 非目标

- 不增加用户可选更新通道。
- 不新增启动自动检查。
- 不自动安装或捆绑完整 WebView2 Runtime。
- 不建立 prerelease 通道。
- 不支持自动降级。
- 本次 SHA-256 只锁定通道晋升 PR 已审查的资产内容，不替代代码签名，也不引入签名基础设施。
- SHA-256 不承诺抵御能够同时修改中央通道清单和 GitHub Release 资产的攻击者。
- 不把 WebView2 主线整体合并回 `legacy/winforms`。
- 不在本设计阶段直接创建远程 PR、tag、Release 或分支。

## 验证要求

- 通道 schema：有效范围、重叠范围、无匹配范围、未知 schema、非法状态、非法 SHA-256。
- 更新比较：低于、等于、高于通道版本；active 和 retired 组合。
- Release 解析：draft、prerelease、tag 不匹配、资产缺失、非 HTTPS URL。
- 下载校验：SHA-256 成功与失败、失败后删除临时文件、校验顺序早于宿主通知。
- 宿主能力：v1 旧包、v1 新包回退、v2 新旧包、非法包名拒绝。
- 发布 workflow：tag/程序集版本/changelog/资产名一致，Release 描述来自 `docs/releases/<tag>.md`。
- 跨仓库集成：使用真实 LizzieYzy-Next v2 宿主完成 WebView2 包下载、通知、替换和重新启动验证。
