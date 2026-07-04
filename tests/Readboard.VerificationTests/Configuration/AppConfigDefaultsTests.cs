using Xunit;
using readboard;

namespace Readboard.VerificationTests
{
    public sealed class AppConfigDefaultsTests
    {
        [Fact]
        public void CreateDefault_SetsExpectedProtocolMetadataAndRuntimeDefaults()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST-MACHINE");

            Assert.Equal("220430", config.ProtocolVersion);
            Assert.Equal("TEST-MACHINE", config.MachineKey);
            Assert.Equal(19, config.BoardWidth);
            Assert.Equal(19, config.BoardHeight);
            Assert.Equal(200, config.SyncIntervalMs);
            Assert.True(config.PlayPonder);
            Assert.True(config.UseMagnifier);
            Assert.False(config.DisableShowInBoardShortcut);
            Assert.False(config.DebugDiagnosticsEnabled);
            Assert.Equal(AppConfig.DefaultMoveVerifyMaxAttempts, config.MoveVerifyMaxAttempts);
            Assert.Equal(SyncMode.Fox, config.SyncMode);
            Assert.Equal(1, config.UiThemeMode);
            Assert.Equal(0, config.ColorMode);
            Assert.Equal(-1, config.WindowPosX);
            Assert.Equal(-1, config.WindowPosY);
            Assert.Equal(AutoPlayColorMode.ManualBlack, config.AutoPlayColorMode);
            Assert.Equal(AutoPlayMoveMode.FirstCandidate, config.AutoPlayMoveMode);
            Assert.True(string.IsNullOrEmpty(config.FoxAutoPlayNickname));
            Assert.True(string.IsNullOrEmpty(config.FoxAutoPlayNicknameSignature));
        }

        [Fact]
        public void MoveVerifyMaxAttempts_DefaultsToInitialPlacementOnly()
        {
            Assert.Equal(1, AppConfig.DefaultMoveVerifyMaxAttempts);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(10, 10)]
        [InlineData(99, AppConfig.MaxMoveVerifyMaxAttempts)]
        public void MoveVerifyMaxAttempts_ResolvesConfiguredValueAsTotalPlacementAttempts(
            int configuredValue,
            int expectedTotalPlacementAttempts)
        {
            Assert.Equal(
                expectedTotalPlacementAttempts,
                AppConfig.ResolveMoveVerifyTotalPlacementAttempts(configuredValue));
        }

        [Fact]
        public void MoveVerifyMaxAttempts_WithoutConfiguredValueUsesDefaultTotalPlacementAttempts()
        {
            Assert.Equal(
                AppConfig.DefaultMoveVerifyMaxAttempts,
                AppConfig.ResolveMoveVerifyTotalPlacementAttempts(null));
        }
    }
}
