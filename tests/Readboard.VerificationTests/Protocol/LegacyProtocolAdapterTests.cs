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
