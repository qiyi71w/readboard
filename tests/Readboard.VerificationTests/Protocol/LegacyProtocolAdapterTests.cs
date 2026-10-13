using Xunit;
using readboard;

namespace Readboard.VerificationTests
{
    public sealed class LegacyProtocolAdapterTests
    {
        [Fact]
        public void ParseInbound_MapsKnownLegacyCommandsFromFixtureCatalog()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();

            foreach (ProtocolFixtureCase fixtureCase in ProtocolFixtureCatalog.LoadInboundCases())
            {
                ProtocolMessage message = adapter.ParseInbound(fixtureCase.RawLine);

                Assert.NotNull(message);
                Assert.Equal(fixtureCase.ExpectedKind, message.Kind);
                Assert.Equal(fixtureCase.RawLine.Trim(), message.RawText);
                AssertMove(message.MoveRequest, fixtureCase.ExpectedX, fixtureCase.ExpectedY);
            }
        }

        [Fact]
        public void Serialize_ReturnsLegacyRawText()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();
            ProtocolMessage message = ProtocolMessage.CreateLegacyLine("ready");

            string line = adapter.Serialize(message);

            Assert.Equal("ready", line);
        }

        [Fact]
        public void CreateFoxMoveNumberMessage_SerializesLegacyRawText()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();

            string line = adapter.Serialize(adapter.CreateFoxMoveNumberMessage(57));

            Assert.Equal("foxMoveNumber 57", line);
        }

        [Fact]
        public void ClearBoardAndResumePonder_SerializeNewOutboundCommandsWithoutChangingClear()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();

            Assert.Equal("clear", adapter.Serialize(adapter.CreateClearMessage()));
            Assert.Equal("clearBoard", adapter.Serialize(adapter.CreateClearBoardMessage()));
            Assert.Equal("resumeponder", adapter.Serialize(adapter.CreateResumePonderMessage()));
        }

        [Theory]
        [InlineData("analysisState running", true)]
        [InlineData("analysisState paused", false)]
        public void ParseInbound_MapsHostAnalysisState(string line, bool running)
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();

            ProtocolMessage message = adapter.ParseInbound(line);

            Assert.Equal(ProtocolMessageKind.AnalysisState, message.Kind);
            Assert.Equal(running, message.AnalysisRunning);
        }

        [Theory]
        [InlineData(0, "lastMoveSource none")]
        [InlineData(1, "lastMoveSource redBlueMarker")]
        [InlineData(2, "lastMoveSource foxCornerFlip")]
        [InlineData(3, "lastMoveSource deviation")]
        [InlineData(4, "lastMoveSource stoneCount")]
        [InlineData(99, "lastMoveSource none")]
        [InlineData(-1, "lastMoveSource none")]
        public void CreateLastMoveSourceMessage_SerializesLegacyRawText(
            int source,
            string expected)
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();

            string line = adapter.Serialize(adapter.CreateLastMoveSourceMessage((LastMoveSource)source));

            Assert.Equal(expected, line);
        }

        [Theory]
        [InlineData("readboardUpdateSupported")]
        [InlineData("readboardUpdatePackageV2Supported")]
        [InlineData("readboardUpdateInstalling")]
        [InlineData("readboardUpdateCancelled")]
        [InlineData("readboardUpdateFailed\tbad zip")]
        public void OldHostedInstallationMessagesRemainUnrecognized(string line)
        {
            Assert.Equal(ProtocolMessageKind.LegacyLine, new LegacyProtocolAdapter().ParseInbound(line).Kind);
        }

        private static void AssertMove(MoveRequest moveRequest, int? expectedX, int? expectedY)
        {
            if (!expectedX.HasValue || !expectedY.HasValue)
            {
                Assert.Null(moveRequest);
                return;
            }

            Assert.NotNull(moveRequest);
            Assert.Equal(expectedX.Value, moveRequest.X);
            Assert.Equal(expectedY.Value, moveRequest.Y);
        }
    }
}
