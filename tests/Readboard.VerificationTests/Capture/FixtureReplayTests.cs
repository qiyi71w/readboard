using Xunit;
using readboard;

namespace Readboard.VerificationTests.Capture
{
    public sealed class FixtureReplayTests
    {
        [Fact]
        public void ReplayFrames_ProduceStableCaptureSignaturesForEquivalentFixtures()
        {
            ReplayFixture fixture = ReplayFixtureCatalog.LoadForeground5x5();

            BoardCaptureResult first = fixture.CreateCaptureResult(ReplayVariant.Base);
            BoardCaptureResult second = fixture.CreateCaptureResult(ReplayVariant.Base);
            BoardCaptureResult changed = fixture.CreateCaptureResult(ReplayVariant.Changed);

            Assert.True(first.Success);
            Assert.True(second.Success);
            Assert.True(changed.Success);
            Assert.Equal(CapturePathKind.PixelBuffer, first.CapturePath);
            Assert.NotNull(first.Frame);
            Assert.Equal(first.Frame.ContentSignature, second.Frame.ContentSignature);
            Assert.NotEqual(first.Frame.ContentSignature, changed.Frame.ContentSignature);
        }
    }
}
