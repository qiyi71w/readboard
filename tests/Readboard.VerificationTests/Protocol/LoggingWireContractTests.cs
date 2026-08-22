using System.Collections.Generic;
using System.IO;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class LoggingWireContractTests
    {
        private const string ProcessSessionId = "dGVzdFByb2Nlc3NJRA";
        private const string HostSessionId = "dGVzdEhvc3RTZXNzaW9u";
        private const string RequestOne = "cmVxdWVzdDE";
        private const string RequestTwo = "cmVxdWVzdDI";
        private const string CapabilityLine =
            "readboardLoggingV1 dGVzdFByb2Nlc3NJRA on off off healthy 0";
        private const string SetLine = "readboardLoggingSet cmVxdWVzdDE on off on";
        private const string ObservedLine =
            "readboardLoggingObserved cmVxdWVzdDE dGVzdFByb2Nlc3NJRA on off on degraded 3 writer-fault";

        [Fact]
        public void CapabilitySetAndObserved_RoundTripIdenticalFields()
        {
            LoggingCapability capability;
            Assert.True(LoggingWireContract.TryParseCapability(CapabilityLine, out capability));
            Assert.Equal(ProcessSessionId, capability.ProcessSessionId);
            Assert.Equal(LoggingToggle.On, capability.Diagnostics);
            Assert.Equal(LoggingToggle.Off, capability.Capture);
            Assert.Equal(LoggingToggle.Off, capability.Trace);
            Assert.Equal(LoggingPersistenceHealth.Healthy, capability.Persistence);
            Assert.Equal(0, capability.DropCount);
            Assert.Equal(CapabilityLine, LoggingWireContract.FormatCapability(capability));

            LoggingSetRequest set;
            Assert.True(LoggingWireContract.TryParseSet(SetLine, out set));
            Assert.Equal(RequestOne, set.RequestId);
            Assert.Equal(LoggingToggle.On, set.Diagnostics);
            Assert.Equal(LoggingToggle.Off, set.Capture);
            Assert.Equal(LoggingToggle.On, set.Trace);
            Assert.Equal(SetLine, LoggingWireContract.FormatSet(set));

            LoggingObserved observed;
            Assert.True(LoggingWireContract.TryParseObserved(ObservedLine, out observed));
            Assert.Equal(RequestOne, observed.RequestId);
            Assert.Equal(ProcessSessionId, observed.ProcessSessionId);
            Assert.Equal(LoggingToggle.On, observed.Diagnostics);
            Assert.Equal(LoggingToggle.Off, observed.Capture);
            Assert.Equal(LoggingToggle.On, observed.Trace);
            Assert.Equal(LoggingPersistenceHealth.Degraded, observed.Persistence);
            Assert.Equal(3, observed.DropCount);
            Assert.Equal(LoggingFailureReason.WriterFault, observed.Reason);
            Assert.Equal(ObservedLine, LoggingWireContract.FormatObserved(observed));
        }

        [Theory]
        [InlineData("readboardLoggingV1 dGVzdFByb2Nlc3NJRA on off off healthy")]
        [InlineData("readboardLoggingV1 dGVzdFByb2Nlc3NJRA unknown off off healthy 0")]
        [InlineData("readboardLoggingV1 not valid on off off healthy 0")]
        [InlineData("readboardLoggingSet cmVxdWVzdDE on off")]
        [InlineData("readboardLoggingSet cmVxdWVzdDE unknown off off")]
        [InlineData("readboardLoggingObserved cmVxdWVzdDE dGVzdFByb2Nlc3NJRA on off off healthy 0")]
        [InlineData("readboardLoggingObserved cmVxdWVzdDE dGVzdFByb2Nlc3NJRA on off off healthy 0 java.io.IOException: C:\\x")]
        [InlineData("readboardLoggingObserved cmVxdWVzdDE dGVzdFByb2Nlc3NJRA on off off healthy -1 writer-fault")]
        public void IllegalLoggingLines_AreRejectedWithoutInventingFields(string line)
        {
            LoggingCapability capability;
            LoggingSetRequest set;
            LoggingObserved observed;

            Assert.False(LoggingWireContract.TryParseCapability(line, out capability));
            Assert.Null(capability);
            Assert.False(LoggingWireContract.TryParseSet(line, out set));
            Assert.Null(set);
            Assert.False(LoggingWireContract.TryParseObserved(line, out observed));
            Assert.Null(observed);
        }

        [Fact]
        public void StableReasons_NeverSerializeRawPathsOrExceptionText()
        {
            string[] reasons =
            {
                ProtocolKeywords.LoggingReasonApplied,
                ProtocolKeywords.LoggingReasonLegacyHelper,
                ProtocolKeywords.LoggingReasonCapabilityTimeout,
                ProtocolKeywords.LoggingReasonPathUnavailable,
                ProtocolKeywords.LoggingReasonWriterFault,
                ProtocolKeywords.LoggingReasonInvalidRequest
            };

            foreach (string reason in reasons)
            {
                Assert.DoesNotContain("\\", reason);
                Assert.DoesNotContain("/", reason);
                Assert.DoesNotContain("Exception", reason);
                Assert.DoesNotContain(" ", reason);
            }

            LoggingObserved observed = new LoggingObserved
            {
                RequestId = RequestOne,
                ProcessSessionId = ProcessSessionId,
                Diagnostics = LoggingToggle.On,
                Capture = LoggingToggle.On,
                Trace = LoggingToggle.Off,
                Persistence = LoggingPersistenceHealth.Unavailable,
                DropCount = 0,
                Reason = LoggingFailureReason.PathUnavailable
            };

            string line = LoggingWireContract.FormatObserved(observed);
            Assert.Equal(
                "readboardLoggingObserved cmVxdWVzdDE dGVzdFByb2Nlc3NJRA on on off unavailable 0 path-unavailable",
                line);
            Assert.DoesNotContain(@"C:\", line);
            Assert.DoesNotContain("IOException", line);
        }

        [Fact]
        public void Persistence_IsWorstWriterHealthAndDropCountIgnoresCapture()
        {
            Assert.Equal(
                LoggingPersistenceHealth.Unavailable,
                LoggingWireContract.WorstPersistence(
                    LoggingPersistenceHealth.Healthy,
                    LoggingPersistenceHealth.Degraded,
                    LoggingPersistenceHealth.Unavailable,
                    LoggingPersistenceHealth.Healthy));
            Assert.Equal(8, LoggingWireContract.CombineDropCount(3, 5));
            Assert.Equal(3, LoggingWireContract.CombineDropCount(3, 0));
        }

        [Theory]
        [InlineData("safe")]
        [InlineData("localPath")]
        [InlineData("localUrl")]
        [InlineData("userText")]
        [InlineData("sessionId")]
        [InlineData("secret")]
        public void PrivacyTokens_RoundTripAndRejectUnknown(string token)
        {
            LoggingPrivacy parsed;
            Assert.True(LoggingWireContract.TryParsePrivacy(token, out parsed));
            Assert.Equal(token, LoggingWireContract.FormatPrivacy(parsed));

            LoggingPrivacy unknown;
            Assert.False(LoggingWireContract.TryParsePrivacy("SAFE", out unknown));
            Assert.False(LoggingWireContract.TryParsePrivacy("path", out unknown));
        }

        [Fact]
        public void CompleteUncSharePath_IsContractLaunch()
        {
            LaunchOptions options;
            Assert.True(LaunchOptions.TryParse(
                new[]
                {
                    "yzy",
                    "10",
                    "20",
                    "policy",
                    "1",
                    "en",
                    "9527",
                    "--log-dir",
                    @"\\server\share\logs\readboard",
                    "--host-session-id",
                    HostSessionId,
                    "--logging-contract",
                    "1"
                },
                () => ProcessSessionId,
                out options));
            Assert.Equal(LoggingLaunchKind.Contract, options.LoggingKind);
        }

        [Fact]
        public void NewestRequestIdWins_AndTogglesApplyIndependently()
        {
            LoggingSetRequest first = MustParseSet("readboardLoggingSet cmVxdWVzdDE on off off");
            LoggingSetRequest second = MustParseSet("readboardLoggingSet cmVxdWVzdDI off on on");

            LoggingObserved firstObserved = LoggingWireContract.ApplySetIndependently(
                first,
                ProcessSessionId,
                LoggingPersistenceHealth.Healthy,
                LoggingPersistenceHealth.Healthy,
                LoggingPersistenceHealth.Healthy,
                LoggingPersistenceHealth.Degraded,
                1,
                2,
                LoggingFailureReason.WriterFault);
            Assert.Equal(LoggingToggle.On, firstObserved.Diagnostics);
            Assert.Equal(LoggingToggle.Off, firstObserved.Capture);
            Assert.Equal(LoggingToggle.Off, firstObserved.Trace);
            Assert.Equal(LoggingPersistenceHealth.Degraded, firstObserved.Persistence);
            Assert.Equal(3, firstObserved.DropCount);

            LoggingObserved secondObserved = LoggingWireContract.ApplySetIndependently(
                second,
                ProcessSessionId,
                LoggingPersistenceHealth.Healthy,
                LoggingPersistenceHealth.Healthy,
                LoggingPersistenceHealth.Healthy,
                LoggingPersistenceHealth.Healthy,
                1,
                2,
                LoggingFailureReason.Applied);

            LoggingRequestGate gate = new LoggingRequestGate();
            gate.NoteRequest(first.RequestId);
            gate.NoteRequest(second.RequestId);

            Assert.False(gate.TryAcceptObserved(firstObserved));
            Assert.True(gate.TryAcceptObserved(secondObserved));
            Assert.Equal(RequestTwo, gate.LatestRequestId);
            Assert.Equal(LoggingToggle.Off, secondObserved.Diagnostics);
            Assert.Equal(LoggingToggle.On, secondObserved.Capture);
            Assert.Equal(LoggingToggle.On, secondObserved.Trace);
        }

        [Fact]
        public void LegacyLaunch_DoesNotEmitCapabilityOrObservedLines()
        {
            LaunchOptions options;
            Assert.True(LaunchOptions.TryParse(
                new[] { "yzy", "10", "20", "policy", "0", "cn", "-1" },
                () => ProcessSessionId,
                out options));

            string capabilityLine;
            string observedLine;
            Assert.False(LoggingWireContract.TryFormatCapability(options, out capabilityLine));
            Assert.Null(capabilityLine);
            Assert.False(LoggingWireContract.TryFormatObserved(
                options,
                new LoggingObserved
                {
                    RequestId = RequestOne,
                    ProcessSessionId = ProcessSessionId,
                    Reason = LoggingFailureReason.Applied
                },
                out observedLine));
            Assert.Null(observedLine);
        }

        [Fact]
        public void UnavailableLaunch_StillEmitsCapabilityWithProcessSession()
        {
            LaunchOptions options;
            Assert.True(LaunchOptions.TryParse(
                new[]
                {
                    "yzy",
                    "10",
                    "20",
                    "policy",
                    "1",
                    "en",
                    "9527",
                    "--logging-contract",
                    "1"
                },
                () => ProcessSessionId,
                out options));

            string line;
            Assert.True(LoggingWireContract.TryFormatCapability(options, out line));
            Assert.Equal(
                "readboardLoggingV1 dGVzdFByb2Nlc3NJRA off off off unavailable 0",
                line);
        }

        [Fact]
        public void LoggingControlLines_DoNotChangeLegacyInboundSyncClassification()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();
            ProtocolMessage set = adapter.ParseInbound(SetLine);
            ProtocolMessage capability = adapter.ParseInbound(CapabilityLine);
            ProtocolMessage observed = adapter.ParseInbound(ObservedLine);

            Assert.Equal(ProtocolMessageKind.LegacyLine, set.Kind);
            Assert.Equal(ProtocolMessageKind.LegacyLine, capability.Kind);
            Assert.Equal(ProtocolMessageKind.LegacyLine, observed.Kind);
            Assert.True(LoggingWireContract.IsLoggingControlLine(SetLine));
            Assert.True(LoggingWireContract.IsLoggingControlLine(CapabilityLine));
            Assert.True(LoggingWireContract.IsLoggingControlLine(ObservedLine));
        }

        [Fact]
        public void CompatibilityFixture_CoversNewNewNewOldAndOldNew()
        {
            foreach (LoggingCompatFixtureCase fixture in LoadCompatCases())
            {
                string[] hostArgs = fixture.HostArgs.Split(' ');
                if (string.Equals(fixture.Helper, "old", System.StringComparison.Ordinal))
                {
                    string[] positional = new string[7];
                    System.Array.Copy(hostArgs, positional, 7);
                    LaunchOptions oldHelper;
                    Assert.True(LaunchOptions.TryParse(positional, () => ProcessSessionId, out oldHelper));
                    Assert.Equal(fixture.ExpectedKind, oldHelper.LoggingKind.ToString());
                    Assert.Equal(fixture.ExpectCapability, oldHelper.ShouldEmitLoggingCapability);
                    Assert.Equal(fixture.ExpectObserved, oldHelper.ShouldEmitLoggingObserved);
                    Assert.Equal(hostArgs[1], oldHelper.AiTime);
                    Assert.Equal(hostArgs[5], oldHelper.Language);
                    continue;
                }

                LaunchOptions helper;
                Assert.True(LaunchOptions.TryParse(hostArgs, () => ProcessSessionId, out helper));
                Assert.Equal(fixture.ExpectedKind, helper.LoggingKind.ToString());
                Assert.Equal(fixture.ExpectCapability, helper.ShouldEmitLoggingCapability);
                Assert.Equal(fixture.ExpectObserved, helper.ShouldEmitLoggingObserved);
                Assert.Equal(hostArgs[1], helper.AiTime);
                Assert.Equal(hostArgs[5], helper.Language);

                string capabilityLine;
                bool emitted = LoggingWireContract.TryFormatCapability(helper, out capabilityLine);
                Assert.Equal(fixture.ExpectCapability, emitted);
                if (emitted)
                    Assert.StartsWith("readboardLoggingV1 " + ProcessSessionId + " ", capabilityLine);
            }
        }

        private static LoggingSetRequest MustParseSet(string line)
        {
            LoggingSetRequest request;
            Assert.True(LoggingWireContract.TryParseSet(line, out request));
            return request;
        }

        private static IEnumerable<LoggingCompatFixtureCase> LoadCompatCases()
        {
            string path = VerificationFixtureLocator.FixturePath(
                Path.Combine("protocol", "logging-compat-cases.txt"));
            List<LoggingCompatFixtureCase> cases = new List<LoggingCompatFixtureCase>();
            foreach (string rawLine in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith("#", System.StringComparison.Ordinal))
                    continue;

                string[] columns = rawLine.Split('|');
                cases.Add(new LoggingCompatFixtureCase
                {
                    Pairing = columns[0],
                    Helper = columns[1],
                    HostArgs = columns[2],
                    ExpectedKind = columns[3],
                    ExpectCapability = columns[4] == "true",
                    ExpectObserved = columns[5] == "true"
                });
            }

            return cases;
        }

        private sealed class LoggingCompatFixtureCase
        {
            public string Pairing { get; set; }
            public string Helper { get; set; }
            public string HostArgs { get; set; }
            public string ExpectedKind { get; set; }
            public bool ExpectCapability { get; set; }
            public bool ExpectObserved { get; set; }
        }
    }
}
