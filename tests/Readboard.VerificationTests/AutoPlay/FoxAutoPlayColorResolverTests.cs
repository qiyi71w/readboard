using Xunit;
using readboard;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxAutoPlayColorResolverTests
    {
        [Fact]
        public void Resolve_ReturnsManualBlack()
        {
            AutoPlayColorResolution resolution = FoxAutoPlayColorResolver.Resolve(
                AutoPlayColorMode.ManualBlack,
                SyncMode.Foreground,
                string.Empty,
                FoxWindowContext.Unknown(),
                null);

            Assert.True(resolution.IsKnown);
            Assert.Equal("black", resolution.PlayColor);
            Assert.Equal(AutoPlayColorStatus.ManualBlack, resolution.Status);
        }

        [Fact]
        public void Resolve_ReturnsManualWhite()
        {
            AutoPlayColorResolution resolution = FoxAutoPlayColorResolver.Resolve(
                AutoPlayColorMode.ManualWhite,
                SyncMode.Foreground,
                string.Empty,
                FoxWindowContext.Unknown(),
                null);

            Assert.True(resolution.IsKnown);
            Assert.Equal("white", resolution.PlayColor);
            Assert.Equal(AutoPlayColorStatus.ManualWhite, resolution.Status);
        }

        [Fact]
        public void Resolve_RejectsAutoOutsideFoxModes()
        {
            AutoPlayColorResolution resolution = ResolveAuto(
                SyncMode.Foreground,
                "sig",
                PlayingContext(),
                Known("black", AutoPlayColorStatus.RecognizedBlack));

            Assert.False(resolution.IsKnown);
            Assert.Null(resolution.PlayColor);
            Assert.Equal(AutoPlayColorStatus.UnsupportedPlatform, resolution.Status);
        }

        [Fact]
        public void Resolve_RejectsFoxWatchingTitle()
        {
            AutoPlayColorResolution resolution = ResolveAuto(
                SyncMode.Fox,
                "sig",
                new FoxWindowContext { Kind = FoxWindowKind.LiveRoom, LiveRoomState = FoxLiveRoomState.Watching },
                Known("black", AutoPlayColorStatus.RecognizedBlack));

            Assert.False(resolution.IsKnown);
            Assert.Null(resolution.PlayColor);
            Assert.Equal(AutoPlayColorStatus.Spectating, resolution.Status);
        }

        [Fact]
        public void Resolve_RejectsUnconfiguredAutoMode()
        {
            AutoPlayColorResolution resolution = ResolveAuto(
                SyncMode.Fox,
                string.Empty,
                PlayingContext(),
                Known("black", AutoPlayColorStatus.RecognizedBlack));

            Assert.False(resolution.IsKnown);
            Assert.Null(resolution.PlayColor);
            Assert.Equal(AutoPlayColorStatus.Unconfigured, resolution.Status);
        }

        [Theory]
        [InlineData("black", (int)AutoPlayColorStatus.RecognizedBlack)]
        [InlineData("white", (int)AutoPlayColorStatus.RecognizedWhite)]
        public void Resolve_ReturnsDetectedColor(string playColor, int statusValue)
        {
            AutoPlayColorStatus status = (AutoPlayColorStatus)statusValue;
            AutoPlayColorResolution resolution = ResolveAuto(
                SyncMode.FoxBackgroundPlace,
                "sig",
                PlayingContext(),
                Known(playColor, status));

            Assert.True(resolution.IsKnown);
            Assert.Equal(playColor, resolution.PlayColor);
            Assert.Equal(status, resolution.Status);
        }

        [Fact]
        public void Resolve_ReturnsUnknownWhenDetectionIsUnknown()
        {
            AutoPlayColorResolution resolution = ResolveAuto(
                SyncMode.Fox,
                "sig",
                PlayingContext(),
                AutoPlayColorResolution.Unknown(AutoPlayColorStatus.NicknameNotMatched));

            Assert.False(resolution.IsKnown);
            Assert.Null(resolution.PlayColor);
            Assert.Equal(AutoPlayColorStatus.NicknameNotMatched, resolution.Status);
        }

        private static AutoPlayColorResolution ResolveAuto(
            SyncMode syncMode,
            string savedNicknameSignature,
            FoxWindowContext context,
            AutoPlayColorResolution detected)
        {
            return FoxAutoPlayColorResolver.Resolve(
                AutoPlayColorMode.FoxAuto,
                syncMode,
                savedNicknameSignature,
                context,
                detected);
        }

        private static AutoPlayColorResolution Known(string playColor, AutoPlayColorStatus status)
        {
            return AutoPlayColorResolution.Known(playColor, status);
        }

        private static FoxWindowContext PlayingContext()
        {
            return new FoxWindowContext { Kind = FoxWindowKind.LiveRoom, LiveRoomState = FoxLiveRoomState.Playing };
        }
    }
}
