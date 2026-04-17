using System.IO;
using Xunit;
using readboard;

namespace Readboard.VerificationTests
{
    public sealed class DualFormatAppConfigStoreTests
    {
        private const string ProtocolVersion = "220430";
        private const string FixtureMachineKey = "MACHINE-001";
        private const string SaveMachineKey = "SECONDARY-HOST";

        [Fact]
        public void Load_ImportsLegacyFixturesAndWritesJsonMirror()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                workspace.CopyLegacyFixtures();
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.True(result.HasExistingConfig);
                AssertImportedFixtureConfig(result.Config);
                AssertJsonMirror(workspace.PathFor("config.readboard.json"));
            }
        }

        [Fact]
        public void Save_WritesJsonAndLegacyMirrorWithUpdatedMetadata()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, SaveMachineKey, ProtocolVersion);
                AppConfig config = AppConfig.CreateDefault("legacy", "legacy-host");
                config.BoardWidth = 9;
                config.BoardHeight = 9;
                config.SyncMode = SyncMode.Foreground;
                config.SyncBoth = true;
                config.UseEnhanceScreen = true;
                config.PlayPonder = false;
                config.DisableShowInBoardShortcut = true;
                config.UiThemeMode = 7;

                store.Save(config);

                string json = File.ReadAllText(workspace.PathFor("config.readboard.json"));
                string legacyMain = File.ReadAllText(workspace.PathFor("config_readboard.txt"));
                string legacyOther = File.ReadAllText(workspace.PathFor("config_readboard_others.txt"));

                Assert.Contains("\"ProtocolVersion\":\"220430\"", json);
                Assert.Contains("\"MachineKey\":\"SECONDARY-HOST\"", json);
                Assert.Equal("96_33_96_33_1_1_1_0_1_1_SECONDARY-HOST_5", legacyMain);
                Assert.Equal("220430_9_9_-1_-1_200_1_50_-1_-1_1_0_1_7", legacyOther);
            }
        }

        [Fact]
        public void Load_IgnoresLegacyOtherConfigWhenMainConfigBelongsToDifferentMachine()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config_readboard.txt"),
                    "101_42_77_18_1_0_1_0_1_1_SOME-OTHER-MACHINE_4");
                File.WriteAllText(
                    workspace.PathFor("config_readboard_others.txt"),
                    "220430_13_13_15_16_150_1_61_320_240_1_0_1_1");
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.False(result.HasExistingConfig);
                AssertDefaultConfig(result.Config);
                Assert.False(File.Exists(workspace.PathFor("config.readboard.json")));
            }
        }

        private static void AssertImportedFixtureConfig(AppConfig config)
        {
            Assert.Equal(101, config.BlackOffset);
            Assert.Equal(42, config.BlackPercent);
            Assert.Equal(77, config.WhiteOffset);
            Assert.Equal(18, config.WhitePercent);
            Assert.True(config.UseMagnifier);
            Assert.False(config.VerifyMove);
            Assert.Equal(SyncMode.FoxBackgroundPlace, config.SyncMode);
            Assert.Equal(13, config.BoardWidth);
            Assert.Equal(13, config.BoardHeight);
            Assert.Equal(15, config.CustomBoardWidth);
            Assert.Equal(16, config.CustomBoardHeight);
            Assert.Equal(150, config.SyncIntervalMs);
            Assert.True(config.SyncBoth);
            Assert.Equal(61, config.GrayOffset);
            Assert.Equal(320, config.WindowPosX);
            Assert.Equal(240, config.WindowPosY);
            Assert.True(config.UseEnhanceScreen);
            Assert.False(config.PlayPonder);
            Assert.False(config.DisableShowInBoardShortcut);
            Assert.Equal(1, config.UiThemeMode);
            Assert.Equal(ProtocolVersion, config.ProtocolVersion);
            Assert.Equal(FixtureMachineKey, config.MachineKey);
        }

        private static void AssertJsonMirror(string jsonPath)
        {
            Assert.True(File.Exists(jsonPath));

            string json = File.ReadAllText(jsonPath);
            Assert.Contains("\"ProtocolVersion\":\"220430\"", json);
            Assert.Contains("\"MachineKey\":\"MACHINE-001\"", json);
            Assert.Contains("\"BoardWidth\":13", json);
            Assert.Contains("\"SyncBoth\":true", json);
            Assert.Contains("\"PlayPonder\":false", json);
            Assert.Contains("\"DisableShowInBoardShortcut\":false", json);
        }

        private static void AssertDefaultConfig(AppConfig config)
        {
            Assert.Equal(96, config.BlackOffset);
            Assert.Equal(33, config.BlackPercent);
            Assert.Equal(96, config.WhiteOffset);
            Assert.Equal(33, config.WhitePercent);
            Assert.True(config.UseMagnifier);
            Assert.True(config.VerifyMove);
            Assert.Equal(SyncMode.Fox, config.SyncMode);
            Assert.Equal(19, config.BoardWidth);
            Assert.Equal(19, config.BoardHeight);
            Assert.Equal(-1, config.CustomBoardWidth);
            Assert.Equal(-1, config.CustomBoardHeight);
            Assert.Equal(200, config.SyncIntervalMs);
            Assert.False(config.SyncBoth);
            Assert.Equal(50, config.GrayOffset);
            Assert.Equal(-1, config.WindowPosX);
            Assert.Equal(-1, config.WindowPosY);
            Assert.False(config.UseEnhanceScreen);
            Assert.True(config.PlayPonder);
            Assert.False(config.DisableShowInBoardShortcut);
            Assert.Equal(1, config.UiThemeMode);
            Assert.Equal(ProtocolVersion, config.ProtocolVersion);
            Assert.Equal(FixtureMachineKey, config.MachineKey);
        }
    }
}
