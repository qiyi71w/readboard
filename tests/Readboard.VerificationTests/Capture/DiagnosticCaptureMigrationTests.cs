using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using readboard;
using Readboard.VerificationTests.Logging;
using Xunit;

namespace Readboard.VerificationTests.Capture
{
    public sealed class DiagnosticCaptureMigrationTests
    {
        private const string LegacyDebugDiagnostics = @"C:\install\debug-diagnostics";

        [Fact]
        public void CaptureLimits_MatchSpec()
        {
            Assert.Equal(32L * 1024L * 1024L, LoggingLimits.CaptureMaxPngBytes);
            Assert.Equal(500L * 1024L * 1024L, LoggingLimits.CaptureClassTotalBytes);
            Assert.Equal(7, LoggingLimits.CaptureRetentionDays);
            Assert.Equal("png-size-cap", BoardDebugDiagnosticsWriter.PngSizeCapReason);
        }

        [Fact]
        public void ContractCaptureOn_WritesEventDirectoryUnderLogDirCapture()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            SeedLegacyDebugDiagnostics(fileSystem);
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(capture: true), fileSystem))
            using (BoardDebugDiagnosticsWriter writer = runtime.CreateCaptureWriter(() => true))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
            }

            string captureRoot = Path.Combine(LoggingHarness.ContractRoot, "capture");
            IList<string> eventDirectories = ListDirectories(fileSystem, captureRoot);
            string eventDirectory = Assert.Single(eventDirectories);
            Assert.True(fileSystem.FileExists(Path.Combine(eventDirectory, "frame.png")));
            Assert.True(fileSystem.FileExists(Path.Combine(eventDirectory, "metadata.json")));
            Assert.True(fileSystem.FileExists(Path.Combine(eventDirectory, "recognition.txt")));
            Assert.Contains("recognition-success", fileSystem.ReadAllText(Path.Combine(captureRoot, "debug.log")));
            Assert.False(fileSystem.FileExists(Path.Combine(LoggingHarness.ContractRoot, "app.log")));
            Assert.False(fileSystem.FileExists(Path.Combine(LoggingHarness.ContractRoot, "crash.log")));
            Assert.False(fileSystem.FileExists(Path.Combine(LoggingHarness.ContractRoot, "trace.log")));
            Assert.False(fileSystem.HasPathPrefix(Path.Combine(LoggingHarness.ContractRoot, "debug-diagnostics")));
            Assert.Equal("keep", fileSystem.ReadAllText(Path.Combine(LegacyDebugDiagnostics, "keep.txt")));
        }

        [Fact]
        public void ContractCaptureOff_IgnoresLegacyDebugDiagnosticsEnabled()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(capture: false), fileSystem))
            using (BoardDebugDiagnosticsWriter writer = runtime.CreateCaptureWriter(() => true))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
                LoggingObservedSnapshot snapshot = runtime.Observe();
                Assert.Equal(LoggingToggle.Off, snapshot.Capture);
                Assert.Equal(LoggingToggle.Off, snapshot.Diagnostics);
                Assert.Equal(LoggingToggle.Off, snapshot.Trace);
            }

            Assert.False(fileSystem.DirectoryExists(Path.Combine(LoggingHarness.ContractRoot, "capture")));
        }

        [Fact]
        public void LegacyEnabled_WritesCaptureUnderLocalAppDataAndStaysUnconfirmed()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            SeedLegacyDebugDiagnostics(fileSystem);
            LaunchOptions options = LoggingHarness.Legacy();
            using (LoggingRuntime runtime = LoggingHarness.Start(options, fileSystem))
            using (BoardDebugDiagnosticsWriter writer = runtime.CreateCaptureWriter(() => true))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
                LoggingObservedSnapshot snapshot = runtime.Observe();
                Assert.Equal(LoggingToggle.Off, snapshot.Capture);
                Assert.Equal(LoggingFailureReason.LegacyHelper, snapshot.Reason);
                Assert.Equal(LoggingHarness.ProcessSessionId, snapshot.ProcessSessionId);
                Assert.False(options.ShouldEmitLoggingCapability);
                Assert.False(options.ShouldEmitLoggingObserved);
            }

            string captureRoot = Path.Combine(
                LoggingHarness.LocalAppData,
                "LizzieYzyNext",
                "ReadBoard",
                "logs",
                "capture");
            Assert.Single(ListDirectories(fileSystem, captureRoot));
            Assert.False(fileSystem.HasPathPrefix(LoggingHarness.ContractRoot));
            Assert.Equal("keep", fileSystem.ReadAllText(Path.Combine(LegacyDebugDiagnostics, "keep.txt")));
        }

        [Fact]
        public void CaptureWrite_DoesNotToggleDiagnosticsOrTraceOrIncrementDropCount()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(capture: true), fileSystem))
            using (BoardDebugDiagnosticsWriter writer = runtime.CreateCaptureWriter(() => false))
            {
                LoggingObservedSnapshot before = runtime.Observe();
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
                LoggingObservedSnapshot after = runtime.Observe();

                Assert.Equal(LoggingToggle.On, after.Capture);
                Assert.Equal(before.Diagnostics, after.Diagnostics);
                Assert.Equal(before.Trace, after.Trace);
                Assert.Equal(LoggingToggle.Off, after.Diagnostics);
                Assert.Equal(LoggingToggle.Off, after.Trace);
                Assert.Equal(0, after.DropCount);
                Assert.Equal(0, after.RuntimeDropCount);
                Assert.Equal(0, after.TraceDropCount);
                Assert.Equal(LoggingHarness.ProcessSessionId, after.ProcessSessionId);
            }

            Assert.Single(ListDirectories(fileSystem, Path.Combine(LoggingHarness.ContractRoot, "capture")));
        }

        [Fact]
        public void UnavailableLaunch_DoesNotCreateCaptureOrTouchLegacyDirectories()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            SeedLegacyDebugDiagnostics(fileSystem);
            LaunchOptions options = LoggingHarness.Parse(LoggingHarness.ProcessSessionId, "--logging-contract", "1");
            using (LoggingRuntime runtime = LoggingHarness.Start(options, fileSystem))
            using (BoardDebugDiagnosticsWriter writer = runtime.CreateCaptureWriter(() => true))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
                LoggingObservedSnapshot snapshot = runtime.Observe();
                Assert.Equal(LoggingPersistenceHealth.Unavailable, snapshot.CaptureHealth);
                Assert.Equal(LoggingToggle.Off, snapshot.Capture);
            }

            Assert.False(fileSystem.HasPathPrefix(Path.Combine(LoggingHarness.LocalAppData, "LizzieYzyNext")));
            Assert.False(fileSystem.HasPathPrefix(@"C:\work"));
            Assert.Equal("keep", fileSystem.ReadAllText(Path.Combine(LegacyDebugDiagnostics, "keep.txt")));
        }

        [Fact]
        public void OversizePng_IsOmittedWithReasonAndSafeTextRemains()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (BoardDebugDiagnosticsWriter writer = CreateWriter(fileSystem, maxPngBytes: 1))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
            }

            string eventDirectory = Assert.Single(ListDirectories(fileSystem, @"C:\cap"));
            Assert.False(fileSystem.FileExists(Path.Combine(eventDirectory, "frame.png")));
            string metadata = fileSystem.ReadAllText(Path.Combine(eventDirectory, "metadata.json"));
            Assert.Contains("\"FrameOmittedReason\":\"png-size-cap\"", metadata);
            Assert.Contains("\"EventName\":\"recognition-success\"", metadata);
            Assert.Contains("payload=XO.", fileSystem.ReadAllText(Path.Combine(eventDirectory, "recognition.txt")));
            Assert.Contains("recognition-success", fileSystem.ReadAllText(Path.Combine(@"C:\cap", "debug.log")));
        }

        [Fact]
        public void DefaultPngCap_WritesFramePng()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (BoardDebugDiagnosticsWriter writer = CreateWriter(fileSystem))
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));

            string eventDirectory = Assert.Single(ListDirectories(fileSystem, @"C:\cap"));
            Assert.True(fileSystem.FileExists(Path.Combine(eventDirectory, "frame.png")));
            Assert.DoesNotContain(
                "png-size-cap",
                fileSystem.ReadAllText(Path.Combine(eventDirectory, "metadata.json")));
        }

        [Fact]
        public void Quota_DeletesEventDirectoriesOlderThanSevenDays()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            FakeLoggingClock clock = new FakeLoggingClock(new DateTime(2026, 8, 21, 17, 3, 0, DateTimeKind.Utc));
            using (BoardDebugDiagnosticsWriter writer = CreateWriter(fileSystem, clock: clock))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
                clock.UtcNow = clock.UtcNow.AddDays(8);
                writer.RecordRecognitionSuccess(CreateRecord(43UL, 8UL));
            }

            IList<string> remaining = ListDirectories(fileSystem, @"C:\cap");
            Assert.Single(remaining);
            Assert.Contains("20260829", remaining[0]);
        }

        [Fact]
        public void Quota_DeletesOldestCompleteEventDirectoryWhenOverCap()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (BoardDebugDiagnosticsWriter writer = CreateWriter(fileSystem, maxTotalBytes: 1))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
                writer.RecordRecognitionSuccess(CreateRecord(43UL, 8UL));
            }

            IList<string> remaining = ListDirectories(fileSystem, @"C:\cap");
            Assert.Single(remaining);
            Assert.Contains("0002", remaining[0]);
        }

        [Fact]
        public void QuotaListFailure_MarksDegradedKeepsEventAndDoesNotIncreaseDropCount()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            fileSystem.FailListDirectories = true;
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(capture: true), fileSystem))
            {
                using (BoardDebugDiagnosticsWriter writer = CreateWriter(
                    fileSystem,
                    rootDirectory: runtime.CaptureDirectory,
                    reportHealth: runtime.SetCaptureHealth))
                {
                    writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
                }

                LoggingObservedSnapshot snapshot = runtime.Observe();
                Assert.Equal(LoggingPersistenceHealth.Degraded, snapshot.CaptureHealth);
                Assert.Equal(LoggingPersistenceHealth.Degraded, snapshot.Persistence);
                Assert.Equal(0, snapshot.DropCount);
                Assert.Equal(LoggingToggle.On, snapshot.Capture);
                Assert.Equal(LoggingToggle.Off, snapshot.Diagnostics);
                Assert.Equal(LoggingToggle.Off, snapshot.Trace);
                Assert.Contains(
                    "recognition-success",
                    fileSystem.ReadAllText(Path.Combine(runtime.CaptureDirectory, "debug.log")));
            }
        }

        [Fact]
        public void PngWriteFailure_MarksDegradedAndStillWritesSafeText()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            fileSystem.FailWritePng = true;
            LoggingPersistenceHealth? health = null;
            using (BoardDebugDiagnosticsWriter writer = CreateWriter(
                fileSystem,
                reportHealth: value => health = value))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
            }

            string eventDirectory = Assert.Single(ListDirectories(fileSystem, @"C:\cap"));
            Assert.False(fileSystem.FileExists(Path.Combine(eventDirectory, "frame.png")));
            Assert.True(fileSystem.FileExists(Path.Combine(eventDirectory, "metadata.json")));
            Assert.True(fileSystem.FileExists(Path.Combine(eventDirectory, "recognition.txt")));
            Assert.Equal(LoggingPersistenceHealth.Degraded, health);
        }

        [Fact]
        public void Dispose_DrainsQueuedEventsBeforeQuotaCleanup()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (BoardDebugDiagnosticsWriter writer = CreateWriter(fileSystem, maxTotalBytes: 1))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
                writer.RecordRecognitionSuccess(CreateRecord(43UL, 8UL));
                writer.RecordCaptureFailure(new BoardDebugDiagnosticRecord
                {
                    SyncMode = SyncMode.Background,
                    FailureReason = "Capture failed."
                });
            }

            Assert.Contains("Capture failed.", fileSystem.ReadAllText(Path.Combine(@"C:\cap", "debug.log")));
            Assert.Single(ListDirectories(fileSystem, @"C:\cap"));
        }

        [Fact]
        public void QuotaFileListFailure_MarksDegradedAndKeepsAdmittedEvent()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            fileSystem.FailListFiles = true;
            LoggingPersistenceHealth? health = null;
            using (BoardDebugDiagnosticsWriter writer = CreateWriter(
                fileSystem,
                reportHealth: value => health = value))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
            }

            Assert.Equal(LoggingPersistenceHealth.Degraded, health);
            Assert.Contains("recognition-success", fileSystem.ReadAllText(Path.Combine(@"C:\cap", "debug.log")));
        }

        [Fact]
        public void Quota_WhenNewestEventExceedsCap_MarksDegradedAndKeepsEvent()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            LoggingPersistenceHealth? health = null;
            using (BoardDebugDiagnosticsWriter writer = CreateWriter(
                fileSystem,
                maxTotalBytes: 1,
                reportHealth: value => health = value))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
            }

            Assert.Equal(LoggingPersistenceHealth.Degraded, health);
            Assert.Single(ListDirectories(fileSystem, @"C:\cap"));
        }

        [Fact]
        public void CaptureDirectoryCreateFailure_MarksDegradedWithoutThrowing()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            fileSystem.FailCreateDirectory = true;
            LoggingPersistenceHealth? health = null;
            using (BoardDebugDiagnosticsWriter writer = CreateWriter(
                fileSystem,
                reportHealth: value => health = value))
            {
                writer.RecordRecognitionSuccess(CreateRecord(42UL, 7UL));
            }

            Assert.Equal(LoggingPersistenceHealth.Degraded, health);
            Assert.False(fileSystem.DirectoryExists(@"C:\cap"));
        }


        private static BoardDebugDiagnosticsWriter CreateWriter(
            MemoryLoggingFileSystem fileSystem,
            FakeLoggingClock clock = null,
            Action<LoggingPersistenceHealth> reportHealth = null,
            long? maxPngBytes = null,
            int? retentionDays = null,
            long? maxTotalBytes = null,
            string rootDirectory = @"C:\cap")
        {
            return new BoardDebugDiagnosticsWriter(new BoardDebugDiagnosticsWriterOptions
            {
                RootDirectory = rootDirectory,
                IsEnabled = delegate { return true; },
                Clock = clock ?? new FakeLoggingClock(new DateTime(2026, 8, 21, 17, 3, 0, DateTimeKind.Utc)),
                FileSystem = fileSystem,
                ReportHealth = reportHealth,
                MaxPngBytes = maxPngBytes,
                RetentionDays = retentionDays,
                MaxTotalBytes = maxTotalBytes
            });
        }

        private static void SeedLegacyDebugDiagnostics(MemoryLoggingFileSystem fileSystem)
        {
            fileSystem.TryCreateDirectory(LegacyDebugDiagnostics);
            fileSystem.TryWriteAllBytes(
                Path.Combine(LegacyDebugDiagnostics, "keep.txt"),
                Encoding.UTF8.GetBytes("keep"));
        }

        private static IList<string> ListDirectories(MemoryLoggingFileSystem fileSystem, string directory)
        {
            IList<string> listed;
            Assert.True(fileSystem.TryListDirectories(directory, out listed));
            return listed;
        }

        private static BoardDebugDiagnosticRecord CreateRecord(ulong frameSignature, ulong snapshotSignature)
        {
            return new BoardDebugDiagnosticRecord
            {
                SyncMode = SyncMode.Fox,
                BoardWidth = 19,
                BoardHeight = 19,
                CapturePath = CapturePathKind.PixelBuffer,
                Frame = new BoardFrame
                {
                    SyncMode = SyncMode.Fox,
                    BoardSize = new BoardDimensions(19, 19),
                    PixelBuffer = new PixelBuffer
                    {
                        Format = PixelBufferFormat.Rgb24,
                        Width = 2,
                        Height = 2,
                        Stride = 6,
                        Pixels = new byte[]
                        {
                            255, 0, 0, 0, 255, 0,
                            0, 0, 255, 255, 255, 255
                        }
                    },
                    ContentSignature = frameSignature
                },
                Snapshot = new BoardSnapshot
                {
                    Width = 3,
                    Height = 1,
                    IsValid = true,
                    BlackStoneCount = 1,
                    WhiteStoneCount = 1,
                    LastMoveSource = LastMoveSource.FoxCornerFlip,
                    Payload = "XO.",
                    StateSignature = snapshotSignature
                }
            };
        }
    }
}
