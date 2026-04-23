# MainForm 旧新版主题统一实施计划

> 执行方式：Inline Execution  
> 最终方案：A

## Goal

把默认主题和新版主题统一回历史“旧新版主题”样式，让两者在相同 DPI 和相同 `ClientSize` 下得到相同窗口尺寸、相同排版和相同视觉结果，同时继续兼容不同分辨率与 DPI。

## Architecture

主布局仍集中在 [`Form1.cs`](/mnt/d/dev/weiqi/readboard/readboard/Form1.cs)，共享测量继续由 [`MainForm.LayoutProfile.cs`](/mnt/d/dev/weiqi/readboard/readboard/MainForm.LayoutProfile.cs) 承担，但它只保留旧新版主题口径。主题应用层不再分成“经典视觉”和“新版视觉”两条主路径，而是统一复用旧新版主题样式；`legacy` 桌面布局恢复旧新版主题几何常量，自适应布局继续作为窄屏 / 高 DPI 兜底。

## File Map

- Modify: [`readboard/Form1.cs`](/mnt/d/dev/weiqi/readboard/readboard/Form1.cs)
  - 统一默认主题和新版主题的视觉入口
  - 恢复旧新版主题的头部 / 同步区几何下限
  - 保留自适应布局和 DPI 缩放
- Modify: [`readboard/MainForm.LayoutProfile.cs`](/mnt/d/dev/weiqi/readboard/readboard/MainForm.LayoutProfile.cs)
  - 继续作为共享测量入口，但仅使用旧新版主题字体口径
- Modify: [`tests/Readboard.VerificationTests/Host/MainFormThemeStartupParityRegressionTests.cs`](/mnt/d/dev/weiqi/readboard/tests/Readboard.VerificationTests/Host/MainFormThemeStartupParityRegressionTests.cs)
  - 锁定两个主题共用旧新版主题视觉入口
- Modify: [`tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs`](/mnt/d/dev/weiqi/readboard/tests/Readboard.VerificationTests/Host/MainFormThemeLayoutRegressionTests.cs)
  - 锁定旧新版主题的 header / sync 几何下限
- Modify: [`docs/superpowers/specs/2026-04-21-mainform-theme-startup-parity-design.md`](/mnt/d/dev/weiqi/readboard/docs/superpowers/specs/2026-04-21-mainform-theme-startup-parity-design.md)
  - 把设计说明切换到方案 A

## Tasks

- [x] 先写失败中的源码回归测试，锁定“两个主题共用旧新版主题视觉入口”和“legacy 布局使用旧新版主题几何下限”
- [x] 修改 `Form1.cs`，让默认主题和新版主题都走旧新版主题视觉，并恢复旧新版主题的头部 / 同步区节奏
- [x] 保留 `MainFormLayoutProfile` 与 `adaptive` 布局，确保高 DPI / 小窗口场景仍可用
- [x] 更新 spec / plan 文档，删除旧的“classic/new theme 取较大值”和“只统一布局、不统一视觉”表述
- [x] 运行 Host 回归并人工确认没有重新引入主题切换分叉

## Verification

计划中的验证顺序：

1. `MainFormThemeStartupParityRegressionTests`
2. `MainFormThemeLayoutRegressionTests`
3. `StartupAndShutdownRegressionTests`
4. `--filter "FullyQualifiedName~Readboard.VerificationTests.Host"`

## Notes

- 主题切换入口保留，但默认主题与新版主题最终会显示同一套旧新版主题样式。
- `legacy` 布局恢复的是“旧新版主题逻辑常量 + 当前缩放框架”，不是把老代码整段回滚。
- 任何后续主题改动都不能再把 `Control.DefaultFont` 或系统按钮样式引回主布局判定链路。
