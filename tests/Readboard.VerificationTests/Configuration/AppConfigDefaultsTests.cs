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
            Assert.Equal(SyncMode.Fox, config.SyncMode);
            Assert.Equal(1, config.UiThemeMode);
            Assert.Equal(0, config.ColorMode);
            Assert.Equal(-1, config.WindowPosX);
            Assert.Equal(-1, config.WindowPosY);
        }
    }
}
