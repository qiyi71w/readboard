using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;
using readboard;

namespace Readboard.VerificationTests
{
    public sealed class DualFormatAppConfigStoreTests
    {
        private const string ProtocolVersion = "220430";
        private const string FixtureMachineKey = "MACHINE-001";
        private const string SaveMachineKey = "SECONDARY-HOST";

        private enum FailurePoint
        {
            BeforeStaging,
            StageJson,
            StageLegacyMain,
            StageLegacyOther,
            CommitJson,
            CommitLegacyMain,
            CommitLegacyOther,
            RollbackJson,
            RollbackLegacyMain,
            RollbackLegacyOther,
            Cleanup
        }

        [Theory]
        [InlineData((int)FailurePoint.BeforeStaging)]
        [InlineData((int)FailurePoint.StageJson)]
        [InlineData((int)FailurePoint.StageLegacyMain)]
        [InlineData((int)FailurePoint.StageLegacyOther)]
        public void Save_FailureDuringStagingLeavesExistingFilesUnchangedAndCleansTransactionArtifacts(
            int failingStepValue)
        {
            FailurePoint failingStep = (FailurePoint)failingStepValue;
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                string oldJson = "{\"MachineKey\":\"SECONDARY-HOST\",\"BoardWidth\":19}";
                string oldLegacyMain = "old-main";
                string oldLegacyOther = "old-other";
                File.WriteAllText(workspace.PathFor("config.readboard.json"), oldJson);
                File.WriteAllText(workspace.PathFor("config_readboard.txt"), oldLegacyMain);
                File.WriteAllText(workspace.PathFor("config_readboard_others.txt"), oldLegacyOther);

                FailureInjectingConfigFileSystem fileSystem =
                    new FailureInjectingConfigFileSystem(failingStep);
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(
                    workspace.RootPath, SaveMachineKey, ProtocolVersion, fileSystem);

                Assert.Throws<IOException>(delegate
                {
                    store.Save(AppConfig.CreateDefault(ProtocolVersion, SaveMachineKey));
                });

                Assert.Equal(oldJson, File.ReadAllText(workspace.PathFor("config.readboard.json")));
                Assert.Equal(oldLegacyMain, File.ReadAllText(workspace.PathFor("config_readboard.txt")));
                Assert.Equal(oldLegacyOther, File.ReadAllText(workspace.PathFor("config_readboard_others.txt")));
                AssertNoTransactionArtifacts(workspace.RootPath);
            }
        }

        [Theory]
        [InlineData((int)FailurePoint.CommitJson)]
        [InlineData((int)FailurePoint.CommitLegacyMain)]
        [InlineData((int)FailurePoint.CommitLegacyOther)]
        public void Save_CommitFailureRestoresThePreviousCompleteFileSet(
            int failingStepValue)
        {
            FailurePoint failingStep = (FailurePoint)failingStepValue;
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                string oldJson = "{\"MachineKey\":\"SECONDARY-HOST\",\"BoardWidth\":19}";
                string oldLegacyMain = "old-main";
                string oldLegacyOther = "old-other";
                File.WriteAllText(workspace.PathFor("config.readboard.json"), oldJson);
                File.WriteAllText(workspace.PathFor("config_readboard.txt"), oldLegacyMain);
                File.WriteAllText(workspace.PathFor("config_readboard_others.txt"), oldLegacyOther);

                FailureInjectingConfigFileSystem fileSystem =
                    new FailureInjectingConfigFileSystem(failingStep);
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(
                    workspace.RootPath, SaveMachineKey, ProtocolVersion, fileSystem);

                Exception failure = Assert.Throws<IOException>(delegate
                {
                    store.Save(AppConfig.CreateDefault(ProtocolVersion, SaveMachineKey));
                });

                Assert.IsNotType<DurableConfigurationException>(failure);
                Assert.Equal(oldJson, File.ReadAllText(workspace.PathFor("config.readboard.json")));
                Assert.Equal(oldLegacyMain, File.ReadAllText(workspace.PathFor("config_readboard.txt")));
                Assert.Equal(oldLegacyOther, File.ReadAllText(workspace.PathFor("config_readboard_others.txt")));
                AssertNoTransactionArtifacts(workspace.RootPath);
            }
        }

        [Fact]
        public void Save_RollbackFailureRaisesDurableConfigurationError()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(workspace.PathFor("config.readboard.json"), "old-json");
                File.WriteAllText(workspace.PathFor("config_readboard.txt"), "old-main");
                File.WriteAllText(workspace.PathFor("config_readboard_others.txt"), "old-other");

                FailureInjectingConfigFileSystem fileSystem =
                    new FailureInjectingConfigFileSystem(
                        FailurePoint.CommitLegacyOther,
                        FailurePoint.RollbackLegacyMain);
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(
                    workspace.RootPath, SaveMachineKey, ProtocolVersion, fileSystem);

                DurableConfigurationException failure = Assert.Throws<DurableConfigurationException>(delegate
                {
                    store.Save(AppConfig.CreateDefault(ProtocolVersion, SaveMachineKey));
                });

                Assert.NotNull(failure.PrimaryFailure);
                Assert.NotNull(failure.RecoveryFailure);
                Assert.Contains("rollback failed", failure.Message);
                AssertTransactionDirectoryRetained(workspace, failure);
            }
        }

        [Fact]
        public void Save_CleanupFailureRaisesDurableConfigurationErrorAndRetainsTransactionDirectory()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                FailureInjectingConfigFileSystem fileSystem =
                    new FailureInjectingConfigFileSystem(FailurePoint.Cleanup);
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(
                    workspace.RootPath, SaveMachineKey, ProtocolVersion, fileSystem);

                DurableConfigurationException failure = Assert.Throws<DurableConfigurationException>(delegate
                {
                    store.Save(AppConfig.CreateDefault(ProtocolVersion, SaveMachineKey));
                });

                Assert.Null(failure.PrimaryFailure);
                Assert.NotNull(failure.RecoveryFailure);
                AssertTransactionDirectoryRetained(workspace, failure);
                Assert.Contains(
                    "\"MachineKey\": \"SECONDARY-HOST\"",
                    File.ReadAllText(workspace.PathFor("config.readboard.json")));
                Assert.Equal(12, File.ReadAllText(workspace.PathFor("config_readboard.txt")).Split('_').Length);
                Assert.Equal(23, File.ReadAllText(workspace.PathFor("config_readboard_others.txt")).Split('_').Length);
            }
        }

        [Fact]
        public void Save_CommitFailureWithNoPreviousFilesRestoresTheAbsentFileSet()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                FailureInjectingConfigFileSystem fileSystem =
                    new FailureInjectingConfigFileSystem(FailurePoint.CommitLegacyMain);
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(
                    workspace.RootPath, SaveMachineKey, ProtocolVersion, fileSystem);

                Exception failure = Assert.Throws<IOException>(delegate
                {
                    store.Save(AppConfig.CreateDefault(ProtocolVersion, SaveMachineKey));
                });

                Assert.IsNotType<DurableConfigurationException>(failure);
                Assert.False(File.Exists(workspace.PathFor("config.readboard.json")));
                Assert.False(File.Exists(workspace.PathFor("config_readboard.txt")));
                Assert.False(File.Exists(workspace.PathFor("config_readboard_others.txt")));
                AssertNoTransactionArtifacts(workspace.RootPath);
            }
        }

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
                config.DebugDiagnosticsEnabled = true;
                config.UiThemeMode = 7;
                config.ColorMode = AppConfig.ColorModeDark;
                config.AutoPlayColorMode = AutoPlayColorMode.FoxAuto;
                config.AutoPlayMoveMode = AutoPlayMoveMode.GenmoveAnalyze;
                config.FoxAutoPlayNickname = "野狐高段9D";
                config.FoxAutoPlayNicknameSignature = "sig-abc";

                store.Save(config);

                string json = File.ReadAllText(workspace.PathFor("config.readboard.json"));
                string legacyMain = File.ReadAllText(workspace.PathFor("config_readboard.txt"));
                string legacyOther = File.ReadAllText(workspace.PathFor("config_readboard_others.txt"));

                Assert.Contains("\"ProtocolVersion\"", json);
                Assert.Contains("220430", json);
                Assert.Contains("\"MachineKey\"", json);
                Assert.Contains("SECONDARY-HOST", json);
                Assert.Contains("\"DebugDiagnosticsEnabled\"", json);
                Assert.Contains("\"AutoPlayColorMode\"", json);
                Assert.Contains("\"AutoPlayMoveMode\"", json);
                Assert.Contains("\"FoxAutoPlayNickname\"", json);
                Assert.Contains("\"FoxAutoPlayNicknameSignature\"", json);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    Assert.Equal(
                        AppConfig.DefaultMoveVerifyMaxAttempts,
                        doc.RootElement.GetProperty("MoveVerifyMaxAttempts").GetInt32());
                }
                Assert.Equal("96_33_96_33_1_1_1_0_1_1_SECONDARY-HOST_5", legacyMain);
                Assert.Equal("220430_9_9_-1_-1_200_1_50_-1_-1_1_0_1_7_1_2_野狐高段9D_sig-abc_1_1100_680_0_host", legacyOther);
                AssertNoTransactionArtifacts(workspace.RootPath);
            }
        }

        [Fact]
        public void Save_RoundTripsYikeSyncModeAcrossJsonAndLegacyMirror()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, SaveMachineKey, ProtocolVersion);
                AppConfig config = AppConfig.CreateDefault(ProtocolVersion, SaveMachineKey);
                config.SyncMode = SyncMode.Yike;

                store.Save(config);
                AppConfig loaded = store.Load().Config;

                string json = File.ReadAllText(workspace.PathFor("config.readboard.json"));
                string legacyMain = File.ReadAllText(workspace.PathFor("config_readboard.txt"));
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    Assert.Equal(6, doc.RootElement.GetProperty("SyncMode").GetInt32());
                }
                Assert.Equal(SyncMode.Yike, loaded.SyncMode);
                Assert.EndsWith("_6", legacyMain);
            }
        }

        [Fact]
        public void Save_RoundTripsLogicalWindowBoundsAndMaximizedState()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, SaveMachineKey, ProtocolVersion);
                AppConfig config = AppConfig.CreateDefault(ProtocolVersion, SaveMachineKey);
                config.WindowPosX = 320;
                config.WindowPosY = 180;
                config.WindowClientWidth = 1234;
                config.WindowClientHeight = 777;
                config.WindowMaximized = true;

                store.Save(config);
                AppConfig loaded = store.Load().Config;

                Assert.Equal(320, loaded.WindowPosX);
                Assert.Equal(180, loaded.WindowPosY);
                Assert.Equal(1234, loaded.WindowClientWidth);
                Assert.Equal(777, loaded.WindowClientHeight);
                Assert.True(loaded.WindowMaximized);
                Assert.EndsWith("_1234_777_1_host", File.ReadAllText(workspace.PathFor("config_readboard_others.txt")));
            }
        }

        [Fact]
        public void Save_RoundTripsLanguagePreferenceAcrossJsonAndLegacyMirror()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, SaveMachineKey, ProtocolVersion);
                AppConfig config = AppConfig.CreateDefault(ProtocolVersion, SaveMachineKey);
                config.LanguagePreference = "jp";

                store.Save(config);
                AppConfig loaded = store.Load().Config;

                using (JsonDocument doc = JsonDocument.Parse(
                    File.ReadAllText(workspace.PathFor("config.readboard.json"))))
                {
                    Assert.Equal("jp", doc.RootElement.GetProperty("LanguagePreference").GetString());
                }
                Assert.Equal("jp", loaded.LanguagePreference);
                Assert.EndsWith("_jp", File.ReadAllText(workspace.PathFor("config_readboard_others.txt")));
            }
        }

        [Theory]
        [InlineData("220430_9_9_-1_-1_200_1_50_-1_-1_1_0_1_7_1_2_野狐高段9D_sig-abc_1_1100_680_0", "host")]
        [InlineData("220430_9_9_-1_-1_200_1_50_-1_-1_1_0_1_7_1_2_野狐高段9D_sig-abc_1_1100_680_0_jp", "jp")]
        public void Load_ImportsLanguagePreferenceFromLegacyOtherWithoutJson(
            string legacyOther,
            string expectedLanguage)
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config_readboard.txt"),
                    "96_33_96_33_1_1_1_0_1_1_SECONDARY-HOST_5");
                File.WriteAllText(
                    workspace.PathFor("config_readboard_others.txt"),
                    legacyOther);
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(
                    workspace.RootPath,
                    SaveMachineKey,
                    ProtocolVersion);

                AppConfig loaded = store.Load().Config;

                Assert.Equal(expectedLanguage, loaded.LanguagePreference);
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

        [Fact]
        public void Load_RecoversFromCorruptJsonByBackingItUpAndImportingLegacyConfig()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                workspace.CopyLegacyFixtures();
                File.WriteAllText(workspace.PathFor("config.readboard.json"), "{broken json");
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.True(result.HasExistingConfig);
                AssertImportedFixtureConfig(result.Config);
                Assert.Single(Directory.GetFiles(workspace.RootPath, "config.readboard.json.corrupt.*"));
                AssertJsonMirror(workspace.PathFor("config.readboard.json"));
            }
        }

        [Fact]
        public void Load_IgnoresJsonConfigWhenItBelongsToDifferentMachine()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config.readboard.json"),
                    "{\"ProtocolVersion\":\"220430\",\"MachineKey\":\"OTHER-MACHINE\",\"BoardWidth\":9,\"BoardHeight\":9,\"SyncMode\":5,\"BlackOffset\":123}");
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.False(result.HasExistingConfig);
                AssertDefaultConfig(result.Config);
                Assert.Equal(
                    "{\"ProtocolVersion\":\"220430\",\"MachineKey\":\"OTHER-MACHINE\",\"BoardWidth\":9,\"BoardHeight\":9,\"SyncMode\":5,\"BlackOffset\":123}",
                    File.ReadAllText(workspace.PathFor("config.readboard.json")));
            }
        }

        [Fact]
        public void Load_AppliesPartialJsonAsDefaultOverride()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config.readboard.json"),
                    "{\"MachineKey\":\"MACHINE-001\",\"BoardWidth\":9,\"VerifyMove\":false,\"SyncMode\":4,\"AutoPlayColorMode\":2,\"FoxAutoPlayNickname\":\"鳕鱼の让子\",\"FoxAutoPlayNicknameSignature\":\"sig-xyz\"}");
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.True(result.HasExistingConfig);
                Assert.Equal(ProtocolVersion, result.Config.ProtocolVersion);
                Assert.Equal(FixtureMachineKey, result.Config.MachineKey);
                Assert.Equal(9, result.Config.BoardWidth);
                Assert.Equal(19, result.Config.BoardHeight);
                Assert.False(result.Config.VerifyMove);
                Assert.Equal(AppConfig.DefaultMoveVerifyMaxAttempts, result.Config.MoveVerifyMaxAttempts);
                Assert.Equal(SyncMode.FoxBackgroundPlace, result.Config.SyncMode);
                Assert.Equal(200, result.Config.SyncIntervalMs);
                Assert.True(result.Config.PlayPonder);
                Assert.True(result.Config.UseMagnifier);
                Assert.Equal(-1, result.Config.WindowPosX);
                Assert.Equal(-1, result.Config.WindowPosY);
                Assert.Equal(AutoPlayColorMode.FoxAuto, result.Config.AutoPlayColorMode);
                Assert.Equal(AutoPlayMoveMode.FirstCandidate, result.Config.AutoPlayMoveMode);
                Assert.Equal("鳕鱼の让子", result.Config.FoxAutoPlayNickname);
                Assert.Equal("sig-xyz", result.Config.FoxAutoPlayNicknameSignature);
            }
        }

        [Fact]
        public void Load_AppliesAutoPlayMoveModeFromPartialJson()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config.readboard.json"),
                    "{\"MachineKey\":\"MACHINE-001\",\"AutoPlayMoveMode\":1}");
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.Equal(AutoPlayMoveMode.GenmoveAnalyze, result.Config.AutoPlayMoveMode);
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(99)]
        public void Load_NormalizesInvalidAutoPlayColorModeFromJson(int configuredValue)
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config.readboard.json"),
                    "{\"MachineKey\":\"MACHINE-001\",\"AutoPlayColorMode\":" + configuredValue + "}");
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.Equal(AutoPlayColorMode.ManualBlack, result.Config.AutoPlayColorMode);
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(99)]
        public void Load_NormalizesInvalidAutoPlayMoveModeFromJson(int configuredValue)
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config.readboard.json"),
                    "{\"MachineKey\":\"MACHINE-001\",\"AutoPlayMoveMode\":" + configuredValue + "}");
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.Equal(AutoPlayMoveMode.FirstCandidate, result.Config.AutoPlayMoveMode);
            }
        }

        [Theory]
        [InlineData(-1, AppConfig.MinMoveVerifyMaxAttempts)]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(99, AppConfig.MaxMoveVerifyMaxAttempts)]
        public void Load_ClampsMoveVerifyMaxAttemptsFromJson(int configuredValue, int expectedValue)
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config.readboard.json"),
                    "{\"MachineKey\":\"MACHINE-001\",\"MoveVerifyMaxAttempts\":" + configuredValue + "}");
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.Equal(expectedValue, result.Config.MoveVerifyMaxAttempts);
            }
        }

        [Fact]
        public void Load_ResetsLegacyWindowPositionWhenItLooksLikeAMinimizedWindow()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config_readboard.txt"),
                    "101_42_77_18_1_0_1_0_1_1_MACHINE-001_4");
                File.WriteAllText(
                    workspace.PathFor("config_readboard_others.txt"),
                    "220430_13_13_15_16_150_1_61_-32000_-32000_1_0_1_1");
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.True(result.HasExistingConfig);
                Assert.Equal(-1, result.Config.WindowPosX);
                Assert.Equal(-1, result.Config.WindowPosY);
                string json = File.ReadAllText(workspace.PathFor("config.readboard.json"));
                Assert.Contains("\"WindowPosX\"", json);
                Assert.Contains("-1", json);
                Assert.Contains("\"WindowPosY\"", json);
            }
        }

        [Fact]
        public void Load_ImportsLegacyAutoPlayIdentityFields()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config_readboard.txt"),
                    "101_42_77_18_1_0_1_0_1_1_MACHINE-001_4");
                File.WriteAllText(
                    workspace.PathFor("config_readboard_others.txt"),
                    "220430_13_13_15_16_150_1_61_320_240_1_0_1_1_0_2_鳕鱼の让子_sig-xyz_1");
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.True(result.HasExistingConfig);
                Assert.Equal(AutoPlayColorMode.FoxAuto, result.Config.AutoPlayColorMode);
                Assert.Equal(AutoPlayMoveMode.GenmoveAnalyze, result.Config.AutoPlayMoveMode);
                Assert.Equal("鳕鱼の让子", result.Config.FoxAutoPlayNickname);
                Assert.Equal("sig-xyz", result.Config.FoxAutoPlayNicknameSignature);
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(99)]
        public void Load_NormalizesInvalidAutoPlayColorModeFromLegacyOtherConfig(int configuredValue)
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config_readboard.txt"),
                    "101_42_77_18_1_0_1_0_1_1_MACHINE-001_4");
                File.WriteAllText(
                    workspace.PathFor("config_readboard_others.txt"),
                    "220430_13_13_15_16_150_1_61_320_240_1_0_1_1_0_" + configuredValue);
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.True(result.HasExistingConfig);
                Assert.Equal(AutoPlayColorMode.ManualBlack, result.Config.AutoPlayColorMode);
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(99)]
        public void Load_NormalizesInvalidAutoPlayMoveModeFromLegacyOtherConfig(int configuredValue)
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config_readboard.txt"),
                    "101_42_77_18_1_0_1_0_1_1_MACHINE-001_4");
                File.WriteAllText(
                    workspace.PathFor("config_readboard_others.txt"),
                    "220430_13_13_15_16_150_1_61_320_240_1_0_1_1_0_2_鳕鱼の让子_sig-xyz_" + configuredValue);
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.True(result.HasExistingConfig);
                Assert.Equal(AutoPlayMoveMode.FirstCandidate, result.Config.AutoPlayMoveMode);
            }
        }

        [Fact]
        public void Load_ResetsJsonWindowPositionWhenSavedMonitorNoLongerExists()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                File.WriteAllText(
                    workspace.PathFor("config.readboard.json"),
                    "{\"ProtocolVersion\":\"220430\",\"MachineKey\":\"MACHINE-001\",\"WindowPosX\":4096,\"WindowPosY\":240,\"BoardWidth\":19,\"BoardHeight\":19}");
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, FixtureMachineKey, ProtocolVersion);

                AppConfigLoadResult result = store.Load();

                Assert.True(result.HasExistingConfig);
                Assert.Equal(-1, result.Config.WindowPosX);
                Assert.Equal(-1, result.Config.WindowPosY);
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
            Assert.Equal(AppConfig.DefaultMoveVerifyMaxAttempts, config.MoveVerifyMaxAttempts);
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
            Assert.False(config.DebugDiagnosticsEnabled);
            Assert.Equal(1, config.UiThemeMode);
            Assert.Equal(ProtocolVersion, config.ProtocolVersion);
            Assert.Equal(FixtureMachineKey, config.MachineKey);
        }

        private static void AssertJsonMirror(string jsonPath)
        {
            Assert.True(File.Exists(jsonPath));

            string json = File.ReadAllText(jsonPath);
            Assert.Contains("\"ProtocolVersion\"", json);
            Assert.Contains("220430", json);
            Assert.Contains("\"MachineKey\"", json);
            Assert.Contains("MACHINE-001", json);
            Assert.Contains("\"BoardWidth\"", json);
            Assert.Contains("\"SyncBoth\"", json);
            Assert.Contains("\"PlayPonder\"", json);
            Assert.Contains("\"MoveVerifyMaxAttempts\"", json);
            Assert.Contains("\"DisableShowInBoardShortcut\"", json);
        }

        private static void AssertDefaultConfig(AppConfig config)
        {
            Assert.Equal(96, config.BlackOffset);
            Assert.Equal(33, config.BlackPercent);
            Assert.Equal(96, config.WhiteOffset);
            Assert.Equal(33, config.WhitePercent);
            Assert.True(config.UseMagnifier);
            Assert.True(config.VerifyMove);
            Assert.Equal(AppConfig.DefaultMoveVerifyMaxAttempts, config.MoveVerifyMaxAttempts);
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
            Assert.False(config.DebugDiagnosticsEnabled);
            Assert.Equal(1, config.UiThemeMode);
            Assert.Equal(0, config.ColorMode);
            Assert.Equal(AutoPlayMoveMode.FirstCandidate, config.AutoPlayMoveMode);
            Assert.Equal(ProtocolVersion, config.ProtocolVersion);
            Assert.Equal(FixtureMachineKey, config.MachineKey);
        }

        [Theory]
        [InlineData(AppConfig.ColorModeSystem)]
        [InlineData(AppConfig.ColorModeDark)]
        [InlineData(AppConfig.ColorModeLight)]
        public void Save_RoundTripsColorMode(int colorMode)
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, SaveMachineKey, ProtocolVersion);
                AppConfig config = AppConfig.CreateDefault(ProtocolVersion, SaveMachineKey);
                config.ColorMode = colorMode;

                store.Save(config);
                string json = File.ReadAllText(workspace.PathFor("config.readboard.json"));
                AppConfig loaded = store.Load().Config;

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    Assert.Equal(colorMode, doc.RootElement.GetProperty("ColorMode").GetInt32());
                }
                Assert.Equal(colorMode, loaded.ColorMode);
            }
        }

        [Fact]
        public void Save_RoundTripsDebugDiagnosticsEnabledThroughJsonOnly()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(workspace.RootPath, SaveMachineKey, ProtocolVersion);
                AppConfig config = AppConfig.CreateDefault(ProtocolVersion, SaveMachineKey);
                config.DebugDiagnosticsEnabled = true;

                store.Save(config);
                string json = File.ReadAllText(workspace.PathFor("config.readboard.json"));
                string legacyOther = File.ReadAllText(workspace.PathFor("config_readboard_others.txt"));
                AppConfig loaded = store.Load().Config;

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    Assert.True(doc.RootElement.GetProperty("DebugDiagnosticsEnabled").GetBoolean());
                }
                Assert.True(loaded.DebugDiagnosticsEnabled);
                Assert.Equal(23, legacyOther.Split('_').Length);
            }
        }

        private static void AssertNoTransactionArtifacts(string rootPath)
        {
            Assert.Empty(Directory.GetDirectories(rootPath, ".readboard-config-transaction-*"));
        }

        private static void AssertTransactionDirectoryRetained(
            LegacyConfigWorkspace workspace,
            DurableConfigurationException failure)
        {
            Assert.False(string.IsNullOrWhiteSpace(failure.TransactionDirectory));
            Assert.True(Directory.Exists(failure.TransactionDirectory));
            Assert.Equal(
                failure.TransactionDirectory,
                Assert.Single(Directory.GetDirectories(workspace.RootPath, ".readboard-config-transaction-*")));
            Assert.Contains("Transaction directory:", failure.Message);
        }

        private sealed class FailureInjectingConfigFileSystem : IConfigFileSystem
        {
            private readonly IConfigFileSystem physicalFileSystem = new PhysicalConfigFileSystem();
            private readonly HashSet<FailurePoint> failurePoints;

            public FailureInjectingConfigFileSystem(params FailurePoint[] failurePoints)
            {
                this.failurePoints = new HashSet<FailurePoint>(failurePoints);
            }

            public void CreateDirectory(string path)
            {
                ThrowIf(FailurePoint.BeforeStaging);
                physicalFileSystem.CreateDirectory(path);
            }

            public bool DirectoryExists(string path)
            {
                return physicalFileSystem.DirectoryExists(path);
            }

            public void WriteAllText(string path, string content)
            {
                ThrowIf(StageFailureFor(Path.GetFileName(path)));
                physicalFileSystem.WriteAllText(path, content);
            }

            public bool FileExists(string path)
            {
                return physicalFileSystem.FileExists(path);
            }

            public void Copy(string sourcePath, string destinationPath)
            {
                physicalFileSystem.Copy(sourcePath, destinationPath);
            }

            public void ReplaceOrMove(string sourcePath, string destinationPath)
            {
                string sourceFileName = Path.GetFileName(sourcePath);
                string destinationFileName = Path.GetFileName(destinationPath);
                if (sourceFileName.EndsWith(".backup", StringComparison.OrdinalIgnoreCase))
                    ThrowIf(RollbackFailureFor(destinationFileName));
                else
                    ThrowIf(CommitFailureFor(destinationFileName));
                physicalFileSystem.ReplaceOrMove(sourcePath, destinationPath);
            }

            public void DeleteFile(string path)
            {
                ThrowIf(RollbackFailureFor(Path.GetFileName(path)));
                physicalFileSystem.DeleteFile(path);
            }

            public void DeleteDirectory(string path)
            {
                ThrowIf(FailurePoint.Cleanup);
                physicalFileSystem.DeleteDirectory(path);
            }

            private void ThrowIf(FailurePoint? point)
            {
                if (point.HasValue && failurePoints.Contains(point.Value))
                    throw new IOException("Injected configuration file-system failure at " + point.Value + ".");
            }

            private static FailurePoint? StageFailureFor(string fileName)
            {
                switch (fileName)
                {
                    case "config.readboard.json":
                        return FailurePoint.StageJson;
                    case "config_readboard.txt":
                        return FailurePoint.StageLegacyMain;
                    case "config_readboard_others.txt":
                        return FailurePoint.StageLegacyOther;
                    default:
                        return null;
                }
            }

            private static FailurePoint? CommitFailureFor(string fileName)
            {
                switch (fileName)
                {
                    case "config.readboard.json":
                        return FailurePoint.CommitJson;
                    case "config_readboard.txt":
                        return FailurePoint.CommitLegacyMain;
                    case "config_readboard_others.txt":
                        return FailurePoint.CommitLegacyOther;
                    default:
                        return null;
                }
            }

            private static FailurePoint? RollbackFailureFor(string fileName)
            {
                switch (fileName)
                {
                    case "config.readboard.json":
                        return FailurePoint.RollbackJson;
                    case "config_readboard.txt":
                        return FailurePoint.RollbackLegacyMain;
                    case "config_readboard_others.txt":
                        return FailurePoint.RollbackLegacyOther;
                    default:
                        return null;
                }
            }
        }
    }
}
