# 测试/基准 ProjectReference 转换边界

日期: 2026-04-23

## 背景

`.NET 10` 升级计划 Phase 2 要把测试项目和基准项目从生产源码链接切换为 `ProjectReference`。当前项目文件里的 `<Compile Include>` 分两类：

- 生产源码链接：`..\..\readboard\...`
- 测试支撑源码链接：`tests/Shared/...`、测试 harness、benchmark 复用的验收工具

`ProjectReference` 只替代生产程序集源码链接，不能替代测试支撑源码。

## 约定

- 只删除指向 `readboard` 生产源码的 `<Compile Include="..\..\readboard\..." />`。
- 保留测试支撑源码链接，除非先建立独立测试支撑项目并迁移引用。
- `tests/Readboard.VerificationTests/Protocol/SyncSessionCoordinator.TestHooks.cs` 这种测试项目里的生产类型 `partial` 扩展不能跨程序集工作；切换前必须删除、改写测试，或把极小的 `internal` 诊断入口放入生产程序集并通过 `InternalsVisibleTo` 暴露给测试。
- `DispatchProxy` 代理内部接口的测试必须在切换后实际编译验证；如访问受限，不得把生产接口改成 `public`，应优先用 `InternalsVisibleTo` 下的内部测试替身或反射构造。
- `ProgramShim.cs`、`WindowsFormsScreenShim.cs` 等 shim 只在文件实际存在时删除；不存在时该步骤是 no-op，不要为满足计划临时创建再删除。
- `InternalsVisibleTo` 只用于测试/基准访问内部类型，不作为扩大生产 API 的理由。

## 验证

- `dotnet build readboard.sln -c Debug`
- `dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj`
- benchmark 项目至少执行 `dotnet build benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj -c Debug`

## 2026-04-23 转换结果

- `tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj` 已删除指向 `..\..\readboard\...` 的生产源码链接，并改用 `<ProjectReference Include="..\..\readboard\readboard.csproj" />`。
- `benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj` 已删除指向 `..\..\readboard\...` 的生产源码链接，并改用 `<ProjectReference Include="..\..\readboard\readboard.csproj" />`。
- 测试共享源码和 benchmark harness 链接已保留；本轮没有抽新的测试支撑项目。
- `readboard/Properties/AssemblyInfo.cs` 已为 `Readboard.VerificationTests` 和 `Readboard.ProtocolConfigBenchmarks` 加 `InternalsVisibleTo`。
- `tests/Readboard.VerificationTests/Protocol/SyncSessionCoordinator.TestHooks.cs` 不再提供跨程序集 `partial` 扩展；对应能力改为 `SyncSessionCoordinator.WaitForPendingMoveAvailability(TimeSpan timeout)` 的 `internal` 诊断入口。
- `FrameworkContractTests` 已改为锁定 benchmark 项目使用 `ProjectReference`，并确认不再链接生产源码。
- 验证结果：solution build 0 错误；主测试项目通过；benchmark 项目 build 0 错误。

## 不在范围内

| 项 | 决策 | 原因 |
|---|---|---|
| 把所有测试共享源码一次性抽成新项目 | 不做 | 可作为后续清理，本轮目标是移除生产源码重复编译 |
| 将生产内部接口改成 `public` | 不做 | 会扩大 API 面，违背 `InternalsVisibleTo` 的目标 |
| 同步修改 LizzieYzy-Next | 不做 | ProjectReference 是 readboard 仓库内部测试结构变更 |
