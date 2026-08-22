using System.IO;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Logging
{
    public sealed class LoggingPathResolverTests
    {
        private const string ProcessSessionId = "dGVzdFByb2Nlc3NJRA";
        private const string HostSessionId = "dGVzdEhvc3RTZXNzaW9u";
        private const string LocalAppData = @"C:\legacy-appdata";

        [Fact]
        public void LegacyLaunch_UsesLocalAppDataFallback()
        {
            LaunchOptions options = Parse();
            LoggingPathResolution resolution = LoggingPathResolver.Resolve(options, LocalAppData);

            Assert.Equal(LoggingLaunchKind.Legacy, options.LoggingKind);
            Assert.True(resolution.UseLegacyFallback);
            Assert.True(resolution.AllowsPersistence);
            Assert.Equal(
                Path.Combine(LocalAppData, "LizzieYzyNext", "ReadBoard", "logs"),
                resolution.CandidateRoot);
            Assert.Equal(LoggingFailureReason.LegacyHelper, resolution.InitialReason);
            Assert.Equal(ProcessSessionId, options.ProcessSessionId);
        }

        [Fact]
        public void ContractLaunch_UsesExactAbsoluteRoot()
        {
            LaunchOptions options = Parse(
                "--log-dir",
                @"C:\work\logs\readboard",
                "--host-session-id",
                HostSessionId,
                "--logging-contract",
                "1",
                "--diagnostics",
                "off",
                "--capture",
                "off");
            LoggingPathResolution resolution = LoggingPathResolver.Resolve(options, LocalAppData);

            Assert.Equal(LoggingLaunchKind.Contract, options.LoggingKind);
            Assert.False(resolution.UseLegacyFallback);
            Assert.Equal(@"C:\work\logs\readboard", resolution.CandidateRoot);
            Assert.Equal(LoggingFailureReason.Applied, resolution.InitialReason);
            Assert.False(resolution.CandidateRoot.StartsWith(LocalAppData));
        }

        [Theory]
        [InlineData(@"logs\readboard")]
        [InlineData("./logs/readboard")]
        [InlineData("/tmp/logs/readboard")]
        public void RelativeOrIncompleteLaunch_DoesNotFallback(string logDir)
        {
            LaunchOptions options = Parse(
                "--log-dir",
                logDir,
                "--host-session-id",
                HostSessionId,
                "--logging-contract",
                "1");
            LoggingPathResolution resolution = LoggingPathResolver.Resolve(options, LocalAppData);

            Assert.Equal(LoggingLaunchKind.Unavailable, options.LoggingKind);
            Assert.False(resolution.UseLegacyFallback);
            Assert.False(resolution.AllowsPersistence);
            Assert.Null(resolution.CandidateRoot);
            Assert.Equal(LoggingFailureReason.InvalidRequest, resolution.InitialReason);
        }

        [Fact]
        public void IncompleteSuffix_DoesNotFallback()
        {
            LaunchOptions options = Parse("--logging-contract", "1", "--diagnostics", "off");
            LoggingPathResolution resolution = LoggingPathResolver.Resolve(options, LocalAppData);

            Assert.Equal(LoggingLaunchKind.Unavailable, options.LoggingKind);
            Assert.Null(resolution.CandidateRoot);
            Assert.False(resolution.UseLegacyFallback);
            Assert.Equal(LoggingFailureReason.InvalidRequest, resolution.InitialReason);
        }

        [Fact]
        public void Runtime_ExplicitUnusablePathDoesNotTouchLegacyRoot()
        {
            LaunchOptions options = Parse(
                "--log-dir",
                @"C:\work\logs\readboard",
                "--host-session-id",
                HostSessionId,
                "--logging-contract",
                "1");
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            fileSystem.FailCreateDirectory = true;
            using (LoggingRuntime runtime = LoggingHarness.Start(options, fileSystem))
            {
                LoggingObservedSnapshot snapshot = runtime.Observe();
                Assert.Null(runtime.LogRoot);
                Assert.Equal(LoggingPersistenceHealth.Unavailable, snapshot.Persistence);
                Assert.Equal(LoggingFailureReason.PathUnavailable, snapshot.Reason);
                Assert.Equal(ProcessSessionId, snapshot.ProcessSessionId);
                Assert.False(fileSystem.HasPathPrefix(LocalAppData));
                Assert.False(fileSystem.DirectoryExists(Path.Combine(runtime.LogRoot ?? string.Empty, "capture")));
            }
        }

        [Fact]
        public void Runtime_UnavailableLaunchDoesNotCreateLegacyOrCapture()
        {
            LaunchOptions options = Parse("--logging-contract", "1");
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(options, fileSystem))
            {
                LoggingObservedSnapshot snapshot = runtime.Observe();
                Assert.Null(runtime.LogRoot);
                Assert.Equal(LoggingFailureReason.InvalidRequest, snapshot.Reason);
                Assert.Equal(LoggingPersistenceHealth.Unavailable, snapshot.Persistence);
                Assert.False(fileSystem.HasPathPrefix(LocalAppData));
                Assert.False(fileSystem.HasPathPrefix(@"C:\work"));
            }
        }

        [Fact]
        public void Runtime_ReusesLaunchProcessSessionId()
        {
            LaunchOptions options = Parse(
                "--log-dir",
                @"C:\work\logs\readboard",
                "--host-session-id",
                HostSessionId,
                "--logging-contract",
                "1");
            using (LoggingRuntime runtime = LoggingHarness.Start(options, new MemoryLoggingFileSystem()))
            {
                Assert.Equal(ProcessSessionId, runtime.ProcessSessionId);
                Assert.Equal(HostSessionId, runtime.HostSessionId);
                Assert.Equal(ProcessSessionId, runtime.Observe().ProcessSessionId);
            }
        }

        private static LaunchOptions Parse(params string[] extra)
        {
            return LoggingHarness.Parse(ProcessSessionId, extra);
        }
    }
}
