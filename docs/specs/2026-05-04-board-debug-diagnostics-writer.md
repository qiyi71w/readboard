# BoardDebugDiagnosticsWriter 异步写盘边界

日期: 2026-05-04

## 背景

`BoardDebugDiagnosticsWriter` 由持续同步识别路径直接调用。原实现会在调用线程里同步执行目录创建、PNG 编码、`metadata.json` / `recognition.txt` / `debug.log` 写盘，容易把调试诊断的磁盘 I/O 叠加到 keep-sync 热路径上。

## 约定

- `RecordCaptureFailure` / `RecordRecognitionFailure` / `RecordRecognitionSuccess` 在调用线程里只做：
  - 开关检查
  - success 去重判定
  - 最小写盘快照复制
  - 入队后台写盘 worker
- 真正的目录创建、PNG 编码、文件写入由 writer 自己的单后台线程串行完成。
- `recognition-success` 的去重语义保持不变：仍以 `FrameSignature + SnapshotSignature` 为键，只跳过连续重复 success。
- 产物结构保持不变：事件目录、`frame.png`、`metadata.json`、`recognition.txt`、`debug.log` 的文件名和内容格式都不改。

## 生命周期

- `BoardDebugDiagnosticsWriter` 自己拥有后台 worker，并实现 `IDisposable`。
- `Dispose()` 必须 drain 已入队任务并等待 worker 退出，不能直接丢弃最后一批诊断。
- `SyncSessionCoordinator.Dispose()` 负责在应用关闭路径上释放 `runtimeDependencies.DebugDiagnostics`。

## 实现边界

- 第一轮只做单 worker 串行写盘，不引入多 worker、优先级队列或丢弃策略。
- 第一轮不改变 `DebugDiagnosticsEnabled` 的配置语义，也不改 `Form4` 的设置项行为。
- `PixelBuffer -> Bitmap` 转换允许优化实现细节，但不能改变颜色通道语义。

## 不在范围内

| 项 | 决策 | 原因 |
|---|---|---|
| 加入有界队列与丢弃策略 | 不做 | 会改变“每条诊断都尽量落盘”的语义，先保行为兼容 |
| 把调试诊断改为二进制归档格式 | 不做 | 当前排障依赖直接可读的 png/json/txt |
| 让 `SyncSessionCoordinator` 感知 writer 队列长度 | 不做 | 会把 diagnostics backpressure 重新耦合回同步状态机 |
