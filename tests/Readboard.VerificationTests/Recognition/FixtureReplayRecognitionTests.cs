using Xunit;
using readboard;

namespace Readboard.VerificationTests.Recognition
{
    public sealed class FixtureReplayRecognitionTests
    {
        [Fact]
        public void Recognize_BaseReplayFixtureMatchesExpectedSnapshot()
        {
            ReplayFixture fixture = ReplayFixtureCatalog.LoadForeground5x5();
            LegacyBoardRecognitionService service = new LegacyBoardRecognitionService();

            BoardRecognitionResult result = service.Recognize(
                fixture.CreateRecognitionRequest(ReplayVariant.Base, inferLastMove: false));

            Assert.True(result.Success, result.FailureReason);
            Assert.False(result.UsedCachedSnapshot);
            Assert.Equal(fixture.BaseProtocolLines, result.Snapshot.ProtocolLines);
            Assert.Equal(2, result.Snapshot.BlackStoneCount);
            Assert.Equal(2, result.Snapshot.WhiteStoneCount);
        }

        [Fact]
        public void Recognize_RepeatedReplayUsesCacheUntilFrameChanges()
        {
            ReplayFixture fixture = ReplayFixtureCatalog.LoadForeground5x5();
            LegacyBoardRecognitionService service = new LegacyBoardRecognitionService();

            BoardRecognitionResult first = service.Recognize(
                fixture.CreateRecognitionRequest(ReplayVariant.Base, inferLastMove: false));
            BoardRecognitionResult second = service.Recognize(
                fixture.CreateRecognitionRequest(ReplayVariant.Base, inferLastMove: false));
            BoardRecognitionResult changed = service.Recognize(
                fixture.CreateRecognitionRequest(ReplayVariant.Changed, inferLastMove: false));

            Assert.True(first.Success, first.FailureReason);
            Assert.True(second.Success, second.FailureReason);
            Assert.True(changed.Success, changed.FailureReason);
            Assert.True(second.UsedCachedSnapshot);
            Assert.False(changed.UsedCachedSnapshot);
            Assert.Equal(fixture.ChangedProtocolLines, changed.Snapshot.ProtocolLines);
        }
    }
}
