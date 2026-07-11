using System.Text.Json;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class SettingsBridgeTests
    {
        [Fact]
        public void CreateState_MapsPersistedSettingsWithoutDirtyDraft()
        {
            AppConfig config = AppConfig.CreateDefault("220430", "TEST");
            config.ColorMode = AppConfig.ColorModeDark;
            config.DebugDiagnosticsEnabled = true;

            ReadBoardSettingsUiState state = MainForm.CreateWebViewSettingsState(config);

            Assert.Equal("200", state.SyncInterval);
            Assert.Equal("50", state.GrayOffset);
            Assert.Equal("dark", state.Theme);
            Assert.True(state.BackgroundAnalysis);
            Assert.True(state.Diagnostics);
            Assert.False(state.Dirty);
            Assert.Empty(state.Errors);
        }

        [Fact]
        public void TryBuildConfig_ReportsFieldErrorsWithoutChangingCurrentConfig()
        {
            AppConfig current = AppConfig.CreateDefault("220430", "TEST");
            ReadBoardSettingsUiState state = MainForm.CreateWebViewSettingsState(current);
            state.SyncInterval = "10";
            state.GrayOffset = "256";
            state.BlackOffset = "256";
            state.WhiteOffset = "abc";
            state.WhitePercent = "-1";

            Assert.False(MainForm.TryBuildWebViewSettingsConfig(current, state, out AppConfig updated));
            Assert.Contains("不小于 20", state.Errors["syncInterval"]);
            Assert.Contains("0–255", state.Errors["grayOffset"]);
            Assert.Contains("0–255", state.Errors["blackOffset"]);
            Assert.Equal("请输入整数", state.Errors["whiteOffset"]);
            Assert.Contains("0–100", state.Errors["whitePercent"]);
            Assert.Equal(200, current.SyncIntervalMs);
            Assert.Equal(200, updated.SyncIntervalMs);
        }

        [Fact]
        public void TryBuildConfig_MapsDraftAndPreservesUnrelatedConfiguration()
        {
            AppConfig current = AppConfig.CreateDefault("220430", "TEST");
            current.BoardWidth = 13;
            ReadBoardSettingsUiState state = MainForm.CreateWebViewSettingsState(current);
            state.SyncInterval = "350";
            state.BackgroundAnalysis = false;
            state.Theme = "light";

            Assert.True(MainForm.TryBuildWebViewSettingsConfig(current, state, out AppConfig updated));
            Assert.Equal(350, updated.SyncIntervalMs);
            Assert.False(updated.PlayPonder);
            Assert.Equal(AppConfig.ColorModeLight, updated.ColorMode);
            Assert.Equal(13, updated.BoardWidth);
            Assert.Equal("220430", updated.ProtocolVersion);
            Assert.Equal("TEST", updated.MachineKey);
        }

        [Theory]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"diagnostics\",\"value\":true}}", true)]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"syncInterval\",\"value\":\"250\"}}", true)]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"theme\",\"value\":\"dark\"}}", true)]
        [InlineData("{\"type\":\"settings.save\",\"payload\":{}}", true)]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"diagnostics\",\"value\":\"true\"}}", false)]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"unknown\",\"value\":true}}", false)]
        [InlineData("{\"type\":\"settings.save\",\"payload\":{\"extra\":true}}", false)]
        public void CommandValidation_IsStrict(string json, bool expected)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            ReadBoardUiCommand command = JsonSerializer.Deserialize<ReadBoardUiCommand>(
                document.RootElement.GetRawText(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            Assert.Equal(expected, MainForm.IsValidWebViewSettingsCommand(command));
        }
    }
}
