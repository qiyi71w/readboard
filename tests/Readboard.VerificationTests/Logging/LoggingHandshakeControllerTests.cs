using System.Collections.Generic;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Logging
{
    public sealed class LoggingHandshakeControllerTests
    {
        private const string RequestOne = "cmVxdWVzdDE";
        private const string RequestTwo = "cmVxdWVzdDI";

        [Fact]
        public void EmitCapability_AfterContractReady_UsesLiveObservedPersistence()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(), fileSystem))
            {
                List<string> sent = new List<string>();
                LoggingHandshakeController handshake = new LoggingHandshakeController(
                    LoggingHarness.Contract(),
                    runtime,
                    sent.Add);

                handshake.EmitCapability();

                Assert.Single(sent);
                LoggingCapability capability;
                Assert.True(LoggingWireContract.TryParseCapability(sent[0], out capability));
                Assert.Equal(LoggingHarness.ProcessSessionId, capability.ProcessSessionId);
                Assert.Equal(LoggingToggle.Off, capability.Diagnostics);
                Assert.Equal(LoggingToggle.Off, capability.Capture);
                Assert.Equal(LoggingToggle.Off, capability.Trace);
                Assert.Equal(LoggingPersistenceHealth.Healthy, capability.Persistence);
                Assert.Equal(0, capability.DropCount);
            }
        }

        [Fact]
        public void EmitCapability_LegacyLaunch_SendsNothing()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Legacy(), fileSystem))
            {
                List<string> sent = new List<string>();
                LoggingHandshakeController handshake = new LoggingHandshakeController(
                    LoggingHarness.Legacy(),
                    runtime,
                    sent.Add);

                handshake.EmitCapability();
                Assert.True(handshake.TryHandleInbound("readboardLoggingSet " + RequestOne + " on off off"));

                Assert.Empty(sent);
            }
        }

        [Fact]
        public void EmitCapability_UnavailablePath_StillSendsDeterminateToggles()
        {
            LaunchOptions options = LoggingHarness.Parse(
                LoggingHarness.ProcessSessionId,
                "--log-dir",
                "relative\\logs",
                "--host-session-id",
                LoggingHarness.HostSessionId,
                "--logging-contract",
                "1",
                "--diagnostics",
                "on",
                "--capture",
                "off");
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(options, fileSystem))
            {
                List<string> sent = new List<string>();
                LoggingHandshakeController handshake = new LoggingHandshakeController(
                    options,
                    runtime,
                    sent.Add);

                handshake.EmitCapability();

                Assert.Single(sent);
                LoggingCapability capability;
                Assert.True(LoggingWireContract.TryParseCapability(sent[0], out capability));
                Assert.Equal(LoggingToggle.On, capability.Diagnostics);
                Assert.Equal(LoggingToggle.Off, capability.Capture);
                Assert.Equal(LoggingToggle.Off, capability.Trace);
                Assert.Equal(LoggingPersistenceHealth.Unavailable, capability.Persistence);
                Assert.NotEqual(LoggingToggle.Unknown, capability.Diagnostics);
            }
        }

        [Fact]
        public void TryHandleInbound_AppliesTogglesIndependentlyAndAcknowledgesRequestId()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(), fileSystem))
            {
                List<string> sent = new List<string>();
                LoggingHandshakeController handshake = new LoggingHandshakeController(
                    LoggingHarness.Contract(),
                    runtime,
                    sent.Add);

                Assert.True(handshake.TryHandleInbound("readboardLoggingSet " + RequestOne + " on off off"));
                Assert.True(handshake.TryHandleInbound("readboardLoggingSet " + RequestTwo + " off on off"));

                Assert.Equal(2, sent.Count);
                LoggingObserved first;
                LoggingObserved second;
                Assert.True(LoggingWireContract.TryParseObserved(sent[0], out first));
                Assert.True(LoggingWireContract.TryParseObserved(sent[1], out second));
                Assert.Equal(RequestOne, first.RequestId);
                Assert.Equal(LoggingToggle.On, first.Diagnostics);
                Assert.Equal(LoggingToggle.Off, first.Capture);
                Assert.Equal(LoggingToggle.Off, first.Trace);
                Assert.Equal(RequestTwo, second.RequestId);
                Assert.Equal(LoggingToggle.Off, second.Diagnostics);
                Assert.Equal(LoggingToggle.On, second.Capture);
                Assert.Equal(LoggingToggle.Off, second.Trace);
                Assert.Equal(LoggingHarness.ProcessSessionId, second.ProcessSessionId);
                Assert.Equal(LoggingToggle.On, runtime.Observe().Capture);
                Assert.Equal(LoggingToggle.Off, runtime.Observe().Diagnostics);
                Assert.Equal(LoggingToggle.Off, runtime.Observe().Trace);
            }
        }

        [Fact]
        public void TryHandleInbound_RepeatedSetIsIdempotent()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(), fileSystem))
            {
                List<string> sent = new List<string>();
                LoggingHandshakeController handshake = new LoggingHandshakeController(
                    LoggingHarness.Contract(),
                    runtime,
                    sent.Add);
                string line = "readboardLoggingSet " + RequestOne + " off off on";

                Assert.True(handshake.TryHandleInbound(line));
                Assert.True(handshake.TryHandleInbound(line));

                LoggingObserved first;
                LoggingObserved second;
                Assert.True(LoggingWireContract.TryParseObserved(sent[0], out first));
                Assert.True(LoggingWireContract.TryParseObserved(sent[1], out second));
                Assert.Equal(first.Diagnostics, second.Diagnostics);
                Assert.Equal(first.Capture, second.Capture);
                Assert.Equal(first.Trace, second.Trace);
                Assert.Equal(LoggingToggle.On, runtime.Observe().Trace);
                Assert.Equal(LoggingToggle.Off, runtime.Observe().Capture);
                Assert.Equal(LoggingToggle.Off, runtime.Observe().Diagnostics);
            }
        }

        [Fact]
        public void TryHandleInbound_CaptureWriterFaultKeepsToggleOnAndDegradesOnlyPersistence()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(), fileSystem))
            {
                List<string> sent = new List<string>();
                LoggingHandshakeController handshake = new LoggingHandshakeController(
                    LoggingHarness.Contract(),
                    runtime,
                    sent.Add);
                runtime.SetCaptureHealth(LoggingPersistenceHealth.Degraded);

                Assert.True(handshake.TryHandleInbound("readboardLoggingSet " + RequestOne + " off on off"));

                LoggingObserved observed;
                Assert.True(LoggingWireContract.TryParseObserved(sent[0], out observed));
                Assert.Equal(LoggingToggle.Off, observed.Diagnostics);
                Assert.Equal(LoggingToggle.On, observed.Capture);
                Assert.Equal(LoggingToggle.Off, observed.Trace);
                Assert.Equal(LoggingPersistenceHealth.Degraded, observed.Persistence);
                Assert.Equal(LoggingFailureReason.WriterFault, observed.Reason);
                Assert.Equal(0, observed.DropCount);
                Assert.Equal(LoggingToggle.On, runtime.Observe().Capture);
            }
        }

        [Fact]
        public void TryHandleInbound_MalformedControlLineDoesNotChangeToggles()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(), fileSystem))
            {
                List<string> sent = new List<string>();
                LoggingHandshakeController handshake = new LoggingHandshakeController(
                    LoggingHarness.Contract(),
                    runtime,
                    sent.Add);

                Assert.True(handshake.TryHandleInbound("readboardLoggingSet not valid on off off"));
                Assert.False(handshake.TryHandleInbound("place 3 4"));

                Assert.Empty(sent);
                Assert.Equal(LoggingToggle.Off, runtime.Observe().Diagnostics);
                Assert.Equal(LoggingToggle.Off, runtime.Observe().Capture);
                Assert.Equal(LoggingToggle.Off, runtime.Observe().Trace);
            }
        }

        [Fact]
        public void ApplySet_DoesNotWriteDebugDiagnosticsEnabled()
        {
            MemoryLoggingFileSystem fileSystem = new MemoryLoggingFileSystem();
            using (LoggingRuntime runtime = LoggingHarness.Start(LoggingHarness.Contract(), fileSystem))
            {
                LoggingSetRequest request;
                Assert.True(LoggingWireContract.TryParseSet(
                    "readboardLoggingSet " + RequestOne + " on on on",
                    out request));
                LoggingObserved observed = runtime.ApplySet(request);

                Assert.Equal(LoggingToggle.On, observed.Diagnostics);
                Assert.Equal(LoggingToggle.On, observed.Capture);
                Assert.Equal(LoggingToggle.On, observed.Trace);
                Assert.Equal(LoggingToggle.On, runtime.Observe().Diagnostics);
                Assert.Equal(LoggingToggle.On, runtime.Observe().Capture);
                Assert.Equal(LoggingToggle.On, runtime.Observe().Trace);
            }
        }
    }
}
