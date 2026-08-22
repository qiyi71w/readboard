using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Logging
{
    public sealed class LoggingRuntimeWriterTests
    {
        [Fact]
        public void BoundedQueue_DropsLowPriorityWithoutBlockingProducer()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            fileSystem.BlockAppend = new ManualResetEventSlim(false);
            using (LoggingRuntime runtime = LoggingHarness.Start(
                LoggingHarness.Contract(),
                fileSystem,
                startWorkers: true))
            {
                try
                {
                    for (int i = 0; i < LoggingLimits.QueueCapacity + 64; i++)
                    {
                        runtime.Write(new LoggingRecord
                        {
                            Level = LogLevel.Information,
                            EventId = "runtime.noise",
                            Stream = LoggingStreams.App
                        });
                    }

                    Assert.True(runtime.Observe().DropCount >= 32);
                    Assert.Equal(runtime.Observe().RuntimeDropCount, runtime.Observe().DropCount);
                    Assert.Equal(0, runtime.Observe().TraceDropCount);
                }
                finally
                {
                    fileSystem.BlockAppend.Set();
                }
            }
        }

        [Fact]
        public void BoundedQueue_EvictsLowPriorityToAdmitWarning()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(), fileSystem))
            {
                for (int i = 0; i < LoggingLimits.QueueCapacity; i++)
                {
                    runtime.Write(new LoggingRecord
                    {
                        Level = LogLevel.Information,
                        EventId = "runtime.noise",
                        Stream = LoggingStreams.App
                    });
                }
                runtime.Write(new LoggingRecord
                {
                    Level = LogLevel.Warning,
                    EventId = "runtime.kept",
                    Stream = LoggingStreams.App
                });

                Assert.Equal(1, runtime.Observe().DropCount);
                runtime.Drain();
                string text = fileSystem.ReadAllText(Path.Combine(LoggingHarness.ContractRoot, "app.log"));
                Assert.Contains("runtime.kept", text);
            }
        }

        [Fact]
        public void Crash_WritesDirectlyAndKeepsFixedTail()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            bool terminated = false;
            using (LoggingRuntime runtime = LoggingHarness.Start(
                LoggingHarness.Contract(),
                fileSystem,
                terminate: delegate { terminated = true; }))
            {
                for (int i = 0; i < 300; i++)
                {
                    runtime.Write(new LoggingRecord
                    {
                        Level = LogLevel.Information,
                        EventId = "runtime.item." + i,
                        Stream = LoggingStreams.App
                    });
                }

                runtime.HandleUnhandledException(new InvalidOperationException("boom\nplay>secret-payload"), true);
                Assert.True(terminated);
                terminated = false;
                runtime.HandleUnobservedTaskException(new InvalidOperationException("background"));
                Assert.False(terminated);

                string crash = fileSystem.ReadAllText(Path.Combine(LoggingHarness.ContractRoot, "crash.log"));
                Assert.Contains("runtime.unhandled", crash);
                Assert.Contains("\"stream\":\"crash\"", crash);
                Assert.Contains("play>redacted", crash);
                Assert.DoesNotContain("secret-payload", crash);
                Assert.Contains("\"eventId\":\"runtime.item.299\"", crash);
                Assert.DoesNotContain("\"eventId\":\"runtime.item.0\"", crash);
            }
        }

        [Fact]
        public void UnobservedTaskException_DoesNotTerminate()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            bool terminated = false;
            using (LoggingRuntime runtime = LoggingHarness.Start(
                LoggingHarness.Contract(),
                fileSystem,
                terminate: delegate { terminated = true; }))
            {
                runtime.HandleUnobservedTaskException(new InvalidOperationException("background"));
                runtime.Drain();
                Assert.False(terminated);
                Assert.Contains("unobserved", fileSystem.ReadAllText(Path.Combine(LoggingHarness.ContractRoot, "crash.log")));
            }
        }

        [Fact]
        public void Rolling_ArchivesGzipAndEnforcesAgeAndClassCap()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            FakeLoggingClock clock = new FakeLoggingClock(new DateTime(2026, 8, 21, 17, 3, 0, DateTimeKind.Utc));
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(), fileSystem, clock))
            {
                string active = Path.Combine(LoggingHarness.ContractRoot, "app.log");
                string archive = Path.Combine(LoggingHarness.ContractRoot, "archive");
                runtime.Write(new LoggingRecord { Level = LogLevel.Information, EventId = "before-roll", Stream = LoggingStreams.App });
                runtime.Drain();
                fileSystem.SetReportedLength(active, LoggingLimits.RollBytes);
                runtime.Write(new LoggingRecord { Level = LogLevel.Information, EventId = "after-roll", Stream = LoggingStreams.App });
                runtime.Drain();

                string[] archives = new System.Collections.Generic.List<string>(fileSystem.ListFiles(archive)).ToArray();
                Assert.NotEmpty(archives);
                byte[] gzipBytes;
                Assert.True(fileSystem.TryReadAllBytes(archives[0], out gzipBytes));
                Assert.True(gzipBytes.Length >= 2);
                Assert.Equal(0x1f, gzipBytes[0]);
                Assert.Equal(0x8b, gzipBytes[1]);
                using (MemoryStream input = new MemoryStream(gzipBytes))
                using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
                using (MemoryStream output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    Assert.Contains("before-roll", Encoding.UTF8.GetString(output.ToArray()));
                }
                Assert.Contains("after-roll", fileSystem.ReadAllText(active));

                string oldArchive = Path.Combine(archive, "app.20260101T000000Z.log.gz");
                Assert.True(fileSystem.TryWriteAllBytes(oldArchive, new byte[] { 1, 2, 3 }));
                fileSystem.SetLastWriteUtc(oldArchive, clock.UtcNow.AddDays(-8));
                fileSystem.SetReportedLength(oldArchive, 1024);

                string largeOne = Path.Combine(archive, "app.20260820T000000Z.log.gz");
                string largeTwo = Path.Combine(archive, "app.20260820T010000Z.log.gz");
                Assert.True(fileSystem.TryWriteAllBytes(largeOne, new byte[] { 4 }));
                Assert.True(fileSystem.TryWriteAllBytes(largeTwo, new byte[] { 5 }));
                fileSystem.SetLastWriteUtc(largeOne, clock.UtcNow.AddHours(-3));
                fileSystem.SetLastWriteUtc(largeTwo, clock.UtcNow.AddHours(-2));
                fileSystem.SetReportedLength(largeOne, 60L * 1024 * 1024);
                fileSystem.SetReportedLength(largeTwo, 60L * 1024 * 1024);

                fileSystem.SetReportedLength(active, LoggingLimits.RollBytes);
                runtime.Write(new LoggingRecord { Level = LogLevel.Information, EventId = "cleanup", Stream = LoggingStreams.App });
                runtime.Drain();

                Assert.False(fileSystem.FileExists(oldArchive));
                long remaining = 0;
                foreach (string path in fileSystem.ListFiles(archive))
                    remaining += fileSystem.GetLength(path);
                remaining += fileSystem.GetLength(active);
                Assert.True(remaining <= LoggingLimits.ClassTotalBytes);
            }
        }

        [Fact]
        public void Observed_AggregatesHealthAndIgnoresCaptureDrops()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(diagnostics: true), fileSystem))
            {
                LoggingObservedSnapshot healthy = runtime.Observe();
                Assert.Equal(LoggingToggle.On, healthy.Diagnostics);
                Assert.Equal(LoggingToggle.Off, healthy.Capture);
                Assert.Equal(LoggingToggle.Off, healthy.Trace);
                Assert.Equal(LoggingPersistenceHealth.Healthy, healthy.Persistence);
                Assert.Equal(LoggingFailureReason.Applied, healthy.Reason);
                Assert.False(fileSystem.DirectoryExists(Path.Combine(LoggingHarness.ContractRoot, "capture")));

                runtime.SetCaptureHealth(LoggingPersistenceHealth.Degraded);
                Assert.Equal(LoggingPersistenceHealth.Degraded, runtime.Observe().Persistence);
                runtime.SetCaptureHealth(LoggingPersistenceHealth.Unavailable);
                Assert.Equal(LoggingPersistenceHealth.Unavailable, runtime.Observe().Persistence);
                runtime.SetCaptureHealth(LoggingPersistenceHealth.Healthy);

                runtime.WriteDiagnostic(new LoggingRecord
                {
                    Level = LogLevel.Information,
                    EventId = "diagnostic.snapshot",
                    Stream = LoggingStreams.App
                });
                runtime.Write(new LoggingRecord
                {
                    Level = LogLevel.Debug,
                    EventId = "trace.hidden",
                    Stream = LoggingStreams.Trace
                });
                runtime.Drain();
                string app = fileSystem.ReadAllText(Path.Combine(LoggingHarness.ContractRoot, "app.log"));
                Assert.Contains("diagnostic.snapshot", app);
                Assert.False(fileSystem.FileExists(Path.Combine(LoggingHarness.ContractRoot, "trace.log")));
            }
        }

        [Fact]
        public void DiagnosticsOff_StillWritesRuntimeAndCrashButNotDiagnosticEvents()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(diagnostics: false), fileSystem))
            {
                runtime.Write(new LoggingRecord
                {
                    Level = LogLevel.Information,
                    EventId = "runtime.keep",
                    Stream = LoggingStreams.App
                });
                runtime.WriteDiagnostic(new LoggingRecord
                {
                    Level = LogLevel.Information,
                    EventId = "diagnostic.skip",
                    Stream = LoggingStreams.App
                });
                runtime.RecordCrash(new InvalidOperationException("crash"), "unhandled");
                runtime.Drain();

                string app = fileSystem.ReadAllText(Path.Combine(LoggingHarness.ContractRoot, "app.log"));
                Assert.Contains("runtime.keep", app);
                Assert.DoesNotContain("diagnostic.skip", app);
                Assert.Contains("runtime.unhandled", fileSystem.ReadAllText(Path.Combine(LoggingHarness.ContractRoot, "crash.log")));
                Assert.Equal(LoggingToggle.Off, runtime.Observe().Trace);
            }
        }

        [Fact]
        public void SemanticMessage_PersistsKeyAndDoesNotCreateCapture()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(), fileSystem))
            {
                runtime.WriteSemantic(
                    SemanticMessage.CreateLogWithDiagnostic("WARN", "test.range", "disk full", 20, 255),
                    "ui");
                runtime.Drain();
                string app = fileSystem.ReadAllText(Path.Combine(LoggingHarness.ContractRoot, "app.log"));
                Assert.Contains("\"key\":\"test.range\"", app);
                Assert.Contains("\"privacy\":\"userText\"", app);
                Assert.DoesNotContain("Enter an integer", app);
                Assert.False(fileSystem.DirectoryExists(Path.Combine(LoggingHarness.ContractRoot, "capture")));
            }
        }

        [Fact]
        public void WriterFault_DoesNotThrowToCaller()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(), fileSystem))
            {
                fileSystem.FailAppend = true;
                runtime.Write(new LoggingRecord
                {
                    Level = LogLevel.Error,
                    EventId = "runtime.fail",
                    Stream = LoggingStreams.App
                });
                runtime.Drain();
                LoggingObservedSnapshot snapshot = runtime.Observe();
                Assert.Equal(LoggingPersistenceHealth.Unavailable, snapshot.AppHealth);
                Assert.Equal(LoggingFailureReason.WriterFault, snapshot.Reason);
            }
        }

        [Fact]
        public void LegacyLaunch_ReportsLegacyReasonAndDoesNotRequireContractRoot()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Legacy(), fileSystem))
            {
                LoggingObservedSnapshot snapshot = runtime.Observe();
                Assert.Equal(LoggingFailureReason.LegacyHelper, snapshot.Reason);
                Assert.Equal(
                    Path.Combine(LoggingHarness.LocalAppData, "LizzieYzyNext", "ReadBoard", "logs"),
                    runtime.LogRoot);
                runtime.Write(new LoggingRecord
                {
                    Level = LogLevel.Information,
                    EventId = "legacy.event",
                    Stream = LoggingStreams.App
                });
                runtime.Drain();
                Assert.Contains(
                    "legacy.event",
                    fileSystem.ReadAllText(Path.Combine(runtime.LogRoot, "app.log")));
                Assert.False(fileSystem.HasPathPrefix(LoggingHarness.ContractRoot));
            }
        }

        [Fact]
        public void CleanupListFailure_MarksPersistenceDegraded()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            fileSystem.FailListFiles = true;
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(), fileSystem))
            {
                LoggingObservedSnapshot snapshot = runtime.Observe();
                Assert.Equal(LoggingPersistenceHealth.Degraded, snapshot.AppHealth);
                Assert.Equal(LoggingPersistenceHealth.Degraded, snapshot.Persistence);
                Assert.Equal(LoggingFailureReason.WriterFault, snapshot.Reason);
            }
        }
    }
}
