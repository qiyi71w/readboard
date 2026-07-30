using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Architecture
{
    public sealed class ArchitectureContractFenceTests
    {
        private const string PromotedSha256 =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [Fact]
        public void WebViewStateEnvelope_ContainsCompleteAuthoritativeSnapshotShape()
        {
            ReadBoardUiState state = new ReadBoardUiState
            {
                Language = "en",
                Page = "controlCenter",
                Settings = new ReadBoardSettingsUiState(),
                Update = new ReadBoardUpdateUiState(),
                Identity = new ReadBoardIdentityUiState(),
                Dialog = new ReadBoardDialogUiState()
            };
            state.Shell.TargetWindowValid = null;
            state.ControlCenter.PreferencesSaved = false;
            state.ControlCenter.PersistenceError = "disk full";

            using (JsonDocument document = JsonDocument.Parse(MainForm.SerializeWebViewState(state)))
            {
                JsonElement payload = document.RootElement.GetProperty("payload");

                Assert.Equal("state", document.RootElement.GetProperty("type").GetString());
                AssertPropertyNames(
                    payload,
                    "page",
                    "language",
                    "text",
                    "shell",
                    "controlCenter",
                    "settings",
                    "update",
                    "identity",
                    "dialog",
                    "logs");
                Assert.Equal("en", payload.GetProperty("language").GetString());
                Assert.Equal(JsonValueKind.Object, payload.GetProperty("shell").ValueKind);
                Assert.Equal(JsonValueKind.Object, payload.GetProperty("controlCenter").ValueKind);
                Assert.Equal(JsonValueKind.Array, payload.GetProperty("logs").ValueKind);
                Assert.False(payload.GetProperty("controlCenter").GetProperty("preferencesSaved").GetBoolean());
                Assert.Equal(
                    "disk full",
                    payload.GetProperty("controlCenter").GetProperty("persistenceError").GetString());

                AssertPropertyNames(
                    payload.GetProperty("shell"),
                    "version",
                    "theme",
                    "connected",
                    "syncStatus",
                    "lastSync",
                    "stoneCount",
                    "duration",
                    "targetWindowValid",
                    "boardRegionRecognized",
                    "placementRegionResolved",
                    "maximized");
                AssertPropertyNames(
                    payload.GetProperty("controlCenter"),
                    "platform",
                    "room",
                    "moves",
                    "nextTurn",
                    "titleBound",
                    "boardSize",
                    "boardWidth",
                    "boardHeight",
                    "twoWaySync",
                    "autoPlay",
                    "color",
                    "placement",
                    "aiTime",
                    "aiTimeEnabled",
                    "playouts",
                    "playoutsEnabled",
                    "firstPolicy",
                    "firstPolicyEnabled",
                    "colorEnabled",
                    "autoColorEnabled",
                    "placementEnabled",
                    "autoPlayColorStatus",
                    "playColorKnown",
                    "showOnBoard",
                    "quickSyncActive",
                    "continuousSyncActive",
                    "quickSyncEnabled",
                    "continuousSyncEnabled",
                    "oneTimeSyncEnabled",
                    "syncInterval",
                    "analysisRunning",
                    "analysisStateAvailable",
                    "analysisToggleEnabled",
                    "swapOrderEnabled",
                    "forceRebuildEnabled",
                    "clearBoardEnabled",
                    "boardSelectionInsideEnabled",
                    "boardSelectionRectangleEnabled",
                    "boardSelectionLine1Enabled",
                    "configurationEnabled",
                    "twoWaySyncEnabled",
                    "autoPlayToggleEnabled",
                    "autoPlayControlsEnabled",
                    "customBoardSizeEnabled",
                    "customBoardDimensionsEnabled",
                    "preferencesSaved",
                    "persistenceError",
                    "identityEnabled",
                    "showOnBoardEnabled");
                AssertPropertyNames(
                    payload.GetProperty("settings"),
                    "autoMinimize",
                    "backgroundAnalysis",
                    "magnifier",
                    "enhancedCapture",
                    "placementValidation",
                    "syncInterval",
                    "grayOffset",
                    "blackOffset",
                    "blackPercent",
                    "whiteOffset",
                    "whitePercent",
                    "theme",
                    "language",
                    "diagnostics",
                    "dirty",
                    "errors");
                AssertPropertyNames(
                    payload.GetProperty("update"),
                    "open",
                    "status",
                    "currentVersion",
                    "latestVersion",
                    "releaseDate",
                    "releaseNotes",
                    "title",
                    "detail",
                    "message",
                    "errorTitle",
                    "error",
                    "progress",
                    "steps");
                AssertPropertyNames(
                    payload.GetProperty("identity"),
                    "open",
                    "selectedId",
                    "savedId",
                    "hasSavedIdentity",
                    "candidates");
                AssertPropertyNames(
                    payload.GetProperty("dialog"),
                    "open",
                    "kind",
                    "title",
                    "heading",
                    "message",
                    "confirmLabel",
                    "cancelLabel");
            }
        }

        [Fact]
        public void LegacyWireContract_RepresentativeLinesKeepExactTokensAndOrdering()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();
            string[] actual =
            {
                adapter.Serialize(adapter.CreateReadyMessage()),
                adapter.Serialize(adapter.CreateClearMessage()),
                adapter.Serialize(adapter.CreateClearBoardMessage()),
                adapter.Serialize(adapter.CreatePonderStatusMessage(true)),
                adapter.Serialize(adapter.CreatePonderStatusMessage(false)),
                adapter.Serialize(adapter.CreateSyncMessage()),
                adapter.Serialize(adapter.CreateStopSyncMessage()),
                adapter.Serialize(adapter.CreateEndSyncMessage()),
                adapter.Serialize(adapter.CreateBothSyncMessage(true)),
                adapter.Serialize(adapter.CreateBothSyncMessage(false)),
                adapter.Serialize(adapter.CreateSyncPlatformMessage("fox")),
                adapter.Serialize(adapter.CreateRoomTokenMessage("room-1")),
                adapter.Serialize(adapter.CreateForceRebuildMessage()),
                adapter.Serialize(adapter.CreateFoxMoveNumberMessage(57)),
                adapter.Serialize(adapter.CreateLastMoveSourceMessage(LastMoveSource.FoxCornerFlip)),
                adapter.Serialize(adapter.CreateStartMessage(19, 19, new IntPtr(424242), true)),
                adapter.Serialize(adapter.CreatePlayMessage("black", "5", "1000", "0")),
                adapter.Serialize(adapter.CreatePlayMessage(
                    "white",
                    "5",
                    "1000",
                    "0",
                    AutoPlayMoveMode.GenmoveAnalyze)),
                adapter.Serialize(adapter.CreateNoPonderMessage()),
                adapter.Serialize(adapter.CreateResumePonderMessage()),
                adapter.Serialize(adapter.CreateStopAutoPlayMessage()),
                adapter.Serialize(adapter.CreatePassMessage())
            };

            Assert.Equal(
                new[]
                {
                    "ready",
                    "clear",
                    "clearBoard",
                    "playponder on",
                    "playponder off",
                    "sync",
                    "stopsync",
                    "endsync",
                    "bothSync",
                    "nobothSync",
                    "syncPlatform fox",
                    "roomToken room-1",
                    "forceRebuild",
                    "foxMoveNumber 57",
                    "lastMoveSource foxCornerFlip",
                    "start 19 19 424242",
                    "play>black>5 1000 0",
                    "play>white>5 1000 0 gma",
                    "noponder",
                    "resumeponder",
                    "stopAutoPlay",
                    "pass"
                },
                actual);
        }

        [Fact]
        public void LegacyWireContract_UpdateReadyHasThreeOrderedTabFields()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();
            string line = adapter.Serialize(
                adapter.CreateReadboardUpdateReadyMessage(
                    "v3.1.0",
                    @"C:\updates\readboard-webview2-v3.1.0.zip"));

            string[] fields = line.Split('\t');

            Assert.Equal(3, fields.Length);
            Assert.Equal("readboardUpdateReady", fields[0]);
            Assert.Equal("v3.1.0", fields[1]);
            Assert.Equal(@"C:\updates\readboard-webview2-v3.1.0.zip", fields[2]);
            Assert.DoesNotContain("\r", line);
            Assert.DoesNotContain("\n", line);
        }

        [Fact]
        public void LegacyWireContract_InboundCapabilityTokensRemainDistinct()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();
            var cases = new[]
            {
                ("readboardUpdateSupported", ProtocolMessageKind.ReadboardUpdateSupported),
                ("readboardUpdatePackageV2Supported", ProtocolMessageKind.ReadboardUpdatePackageV2Supported),
                ("readboardUpdateInstalling", ProtocolMessageKind.ReadboardUpdateInstalling),
                ("readboardUpdateCancelled", ProtocolMessageKind.ReadboardUpdateCancelled),
                ("readboardUpdateFailed\tbad zip", ProtocolMessageKind.ReadboardUpdateFailed),
                ("analysisState running", ProtocolMessageKind.AnalysisState),
                ("analysisState paused", ProtocolMessageKind.AnalysisState)
            };

            foreach ((string line, ProtocolMessageKind kind) testCase in cases)
            {
                ProtocolMessage message = adapter.ParseInbound(testCase.line);

                Assert.NotNull(message);
                Assert.Equal(testCase.kind, message.Kind);
                Assert.Equal(testCase.line, message.RawText);
            }
        }

        [Fact]
        public void ConfigurationContract_SaveWritesBothLegacyMirrorsAndJsonThatReloads()
        {
            using (LegacyConfigWorkspace workspace = LegacyConfigWorkspace.Create())
            {
                DualFormatAppConfigStore store = new DualFormatAppConfigStore(
                    workspace.RootPath,
                    "CONTRACT-MACHINE",
                    "220430");
                AppConfig config = AppConfig.CreateDefault("220430", "CONTRACT-MACHINE");
                config.SyncMode = SyncMode.Yike;
                config.BoardWidth = 13;
                config.BoardHeight = 13;
                config.SyncBoth = true;
                config.AutoPlayColorMode = AutoPlayColorMode.FoxAuto;
                config.AutoPlayMoveMode = AutoPlayMoveMode.GenmoveAnalyze;
                config.LanguagePreference = "jp";
                config.WindowClientWidth = 1234;
                config.WindowClientHeight = 777;

                store.Save(config);

                string jsonPath = workspace.PathFor("config.readboard.json");
                string legacyMainPath = workspace.PathFor("config_readboard.txt");
                string legacyOtherPath = workspace.PathFor("config_readboard_others.txt");
                Assert.True(File.Exists(jsonPath));
                Assert.True(File.Exists(legacyMainPath));
                Assert.True(File.Exists(legacyOtherPath));

                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath)))
                {
                    Assert.Equal(6, document.RootElement.GetProperty("SyncMode").GetInt32());
                    Assert.Equal("jp", document.RootElement.GetProperty("LanguagePreference").GetString());
                }

                Assert.Equal(12, File.ReadAllText(legacyMainPath).Split('_').Length);
                Assert.Equal(23, File.ReadAllText(legacyOtherPath).Split('_').Length);

                AppConfig loaded = store.Load().Config;
                Assert.Equal(SyncMode.Yike, loaded.SyncMode);
                Assert.Equal(13, loaded.BoardWidth);
                Assert.Equal(13, loaded.BoardHeight);
                Assert.True(loaded.SyncBoth);
                Assert.Equal(AutoPlayColorMode.FoxAuto, loaded.AutoPlayColorMode);
                Assert.Equal(AutoPlayMoveMode.GenmoveAnalyze, loaded.AutoPlayMoveMode);
                Assert.Equal("jp", loaded.LanguagePreference);
                Assert.Equal(1234, loaded.WindowClientWidth);
                Assert.Equal(777, loaded.WindowClientHeight);

                File.Delete(jsonPath);
                AppConfig legacyLoaded = store.Load().Config;
                Assert.Equal(SyncMode.Yike, legacyLoaded.SyncMode);
                Assert.Equal(13, legacyLoaded.BoardWidth);
                Assert.Equal(13, legacyLoaded.BoardHeight);
                Assert.True(legacyLoaded.SyncBoth);
                Assert.Equal(AutoPlayColorMode.FoxAuto, legacyLoaded.AutoPlayColorMode);
                Assert.Equal(AutoPlayMoveMode.GenmoveAnalyze, legacyLoaded.AutoPlayMoveMode);
                Assert.Equal("jp", legacyLoaded.LanguagePreference);
                Assert.Equal(1234, legacyLoaded.WindowClientWidth);
                Assert.Equal(777, legacyLoaded.WindowClientHeight);
            }
        }

        [Theory]
        [InlineData("readboard-github-release-v3.0.9.zip", "v3.0.9", false, true)]
        [InlineData("readboard-webview2-v3.1.0.zip", "v3.1.0", false, false)]
        [InlineData("readboard-webview2-v3.1.0.zip", "v3.1.0", true, true)]
        public void HostedUpdateContract_UsesV1AndV2AssetGates(
            string assetName,
            string tag,
            bool packageV2Supported,
            bool expected)
        {
            UpdateCheckResult result = new UpdateCheckResult
            {
                Tag = tag,
                AssetName = assetName,
                AssetDownloadUrl = "https://github.com/qiyi71w/readboard/releases/download/" + tag + "/" + assetName,
                AssetSha256 = PromotedSha256
            };

            Assert.Equal(
                expected,
                MainForm.CanOfferWebViewHostedInstall(
                    TransportKind.Pipe,
                    true,
                    true,
                    packageV2Supported,
                    result));
        }

        [Fact]
        public void FoxAutoplayContract_UnknownOrWatchingStateRemainsFailClosed()
        {
            AutoPlayColorResolution unknown = FoxAutoPlayColorResolver.Resolve(
                AutoPlayColorMode.FoxAuto,
                SyncMode.Fox,
                "saved-signature",
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Playing
                },
                AutoPlayColorResolution.Unknown(AutoPlayColorStatus.NicknameNotMatched));
            AutoPlayColorResolution watching = FoxAutoPlayColorResolver.Resolve(
                AutoPlayColorMode.FoxAuto,
                SyncMode.Fox,
                "saved-signature",
                new FoxWindowContext
                {
                    Kind = FoxWindowKind.LiveRoom,
                    LiveRoomState = FoxLiveRoomState.Watching
                },
                AutoPlayColorResolution.Known("black", AutoPlayColorStatus.RecognizedBlack));

            Assert.False(unknown.IsKnown);
            Assert.Null(unknown.PlayColor);
            Assert.False(watching.IsKnown);
            Assert.Null(watching.PlayColor);
        }

        [Fact]
        public void GmaContract_RequiresTheNextAuthoritativeFrameAfterPlay()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(
                transport,
                new LegacyProtocolAdapter());
            BoardSnapshot snapshot = new BoardSnapshot
            {
                Payload = "payload-1",
                FoxMoveNumber = 57,
                ProtocolLines = new[] { "re=000", "re=111" }
            };

            coordinator.SendBoardSnapshot(snapshot);
            coordinator.SendPlay("black", "5", "1000", "0", AutoPlayMoveMode.GenmoveAnalyze);
            int playIndex = transport.SentLines.IndexOf("play>black>5 1000 0 gma");
            coordinator.SendBoardSnapshot(snapshot);
            coordinator.SendBoardSnapshot(snapshot);

            int firstPayloadAfterPlay = transport.SentLines.IndexOf("re=000", playIndex + 1);
            Assert.True(playIndex >= 0);
            Assert.True(firstPayloadAfterPlay > playIndex);
            Assert.Equal(2, transport.SentLines.Count(line => line == "re=000"));
        }

        [Fact]
        public void DesktopContract_UsesLogicalDpiAndMinimumClientBounds()
        {
            Assert.Equal(new Size(1100, 680), WebViewWindowLayoutPolicy.BaseLogicalClientSize);
            Assert.Equal(new Size(960, 600), WebViewWindowLayoutPolicy.MinimumLogicalClientSize);
            Assert.Equal(1d, WebViewWindowLayoutPolicy.ResolveScale(new Size(1100, 680)));
            Assert.Equal(0.8727272727d, WebViewWindowLayoutPolicy.ResolveScale(new Size(960, 600)), 8);
            Assert.Equal(new Size(1440, 900), WebViewWindowLayoutPolicy.ScaleLogicalSize(
                WebViewWindowLayoutPolicy.MinimumLogicalClientSize,
                144));
            Assert.Equal(
                new Rectangle(0, 0, 960, 600),
                WebViewWindowLayoutPolicy.ClampBoundsToWorkingArea(
                    new Rectangle(-100, -100, 400, 300),
                    new Rectangle(0, 0, 1920, 1080),
                    WebViewWindowLayoutPolicy.MinimumLogicalClientSize));
        }

        [Fact]
        public void PackagingContract_PreservesWebViewAssetsAndEvergreenRuntimeBoundary()
        {
            string root = VerificationFixtureLocator.RepositoryRoot();
            string script = File.ReadAllText(Path.Combine(root, "scripts", "package-readboard-release.local.ps1"));
            string project = File.ReadAllText(Path.Combine(root, "readboard", "readboard.csproj"));

            Assert.Contains("WebView\\index.html", script);
            Assert.Contains("WebView\\styles.css", script);
            Assert.Contains("WebView\\app.js", script);
            Assert.Contains("WebView2Loader.dll", script);
            Assert.Contains("Microsoft.Web.WebView2", project);
            Assert.Contains("1.0.4078.44", project);
            Assert.Contains("WebView2 Fixed Version Runtime", script);
        }

        private static void AssertPropertyNames(JsonElement element, params string[] expected)
        {
            Assert.Equal(
                expected.OrderBy(property => property),
                element.EnumerateObject().Select(property => property.Name).OrderBy(property => property));
        }

        private sealed class RecordingTransport : IReadBoardTransport
        {
            public event EventHandler<string> MessageReceived
            {
                add { }
                remove { }
            }

            public List<string> SentLines { get; } = new List<string>();

            public bool IsConnected { get; private set; }

            public void Dispose()
            {
            }

            public void Start()
            {
                IsConnected = true;
            }

            public void Stop()
            {
                IsConnected = false;
            }

            public void Send(string line)
            {
                SentLines.Add(line);
            }

            public void SendError(string message)
            {
            }
        }
    }
}
