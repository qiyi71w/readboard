using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class LegacyOutboundProtocolContractTests
    {
        [Fact]
        public void SerializeOutbound_PreservesLegacyControlLinesFromFixtureCatalog()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();

            foreach (ProtocolOutboundFixtureCase fixtureCase in ProtocolFixtureCatalog.LoadOutboundCases())
            {
                string serialized = adapter.Serialize(ProtocolMessage.CreateLegacyLine(fixtureCase.RawLine));

                Assert.Equal(fixtureCase.RawLine, serialized);
            }
        }

        [Fact]
        public void SendLine_PreservesLegacySessionControlOrder()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            string[] expectedLines = ProtocolFixtureCatalog.LoadOutboundLines("session-control-order").ToArray();

            foreach (string line in expectedLines)
                coordinator.SendLine(line);

            Assert.Equal(expectedLines, transport.SentLines);
        }

        [Fact]
        public void SendClear_ResetsReplayAndOverlayCaches()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            LegacyOverlayService overlayService = new LegacyOverlayService();
            string startLine = ProtocolFixtureCatalog.LoadOutboundLines("start").Single();
            string clearLine = ProtocolFixtureCatalog.LoadOutboundLines("clear").Single();
            string expectedVisibleLine = ProtocolFixtureCatalog.LoadOutboundLines("overlay-visible").Single();
            string hiddenLine = ProtocolFixtureCatalog.LoadOutboundLines("overlay-hidden").Single();
            string[] replayLines = ProtocolFixtureCatalog.LoadOutboundLines("board-replay").ToArray();
            string visibleLine = BuildVisibleOverlayLine(overlayService);
            BoardSnapshot snapshot = new BoardSnapshot
            {
                Payload = "fixture-board",
                ProtocolLines = replayLines
            };

            Assert.Equal(expectedVisibleLine, visibleLine);
            coordinator.SendLine(startLine);
            coordinator.SendOverlayLine(visibleLine);
            coordinator.SendBoardSnapshot(snapshot);
            coordinator.SendOverlayLine(visibleLine);
            coordinator.SendBoardSnapshot(snapshot);
            coordinator.SendClear();
            coordinator.SendOverlayLine(hiddenLine);
            coordinator.SendOverlayLine(visibleLine);
            coordinator.SendBoardSnapshot(snapshot);

            Assert.Equal(
                new[]
                {
                    startLine,
                    visibleLine,
                    "syncPlatform generic",
                    replayLines[0],
                    replayLines[1],
                    "end",
                    clearLine,
                    hiddenLine,
                    visibleLine,
                    "syncPlatform generic",
                    replayLines[0],
                    replayLines[1],
                    "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void ExtendedSyncContextMessages_SerializeWithStableLegacyTokens()
        {
            LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();

            Assert.Equal("syncPlatform fox", adapter.Serialize(adapter.CreateSyncPlatformMessage("fox")));
            Assert.Equal("roomToken 23|890号", adapter.Serialize(adapter.CreateRoomTokenMessage("23|890号")));
            Assert.Equal("liveTitleMove 89", adapter.Serialize(adapter.CreateLiveTitleMoveMessage(89)));
            Assert.Equal("recordCurrentMove 333", adapter.Serialize(adapter.CreateRecordCurrentMoveMessage(333)));
            Assert.Equal("recordTotalMove 333", adapter.Serialize(adapter.CreateRecordTotalMoveMessage(333)));
            Assert.Equal("recordAtEnd 1", adapter.Serialize(adapter.CreateRecordAtEndMessage(true)));
            Assert.Equal("recordTitleFingerprint abc123", adapter.Serialize(adapter.CreateRecordTitleFingerprintMessage("abc123")));
            Assert.Equal("forceRebuild", adapter.Serialize(adapter.CreateForceRebuildMessage()));
        }

        private static string BuildVisibleOverlayLine(LegacyOverlayService overlayService)
        {
            OverlayUpdateResult update = overlayService.BuildUpdate(new OverlayUpdateRequest
            {
                Visibility = OverlayVisibility.Visible,
                LegacyTypeToken = "5",
                Frame = new BoardFrame
                {
                    Window = new WindowDescriptor
                    {
                        Bounds = new PixelRect(0, 0, 100, 100),
                        IsDpiAware = true,
                        DpiScale = 1d
                    },
                    Viewport = new BoardViewport
                    {
                        SourceBounds = new PixelRect(10, 20, 30, 40)
                    }
                }
            });

            return update.ProtocolLine;
        }

        [Fact]
        public void BuildUpdate_BackgroundSelection_UsesScreenBoundsWhenRecognitionNormalizesSourceBounds()
        {
            LegacyOverlayService overlayService = new LegacyOverlayService();

            OverlayUpdateResult update = overlayService.BuildUpdate(new OverlayUpdateRequest
            {
                Visibility = OverlayVisibility.Visible,
                LegacyTypeToken = "3",
                Frame = new BoardFrame
                {
                    SyncMode = SyncMode.Background,
                    Window = new WindowDescriptor
                    {
                        Bounds = new PixelRect(300, 200, 100, 100),
                        IsDpiAware = true,
                        DpiScale = 1d
                    },
                    Viewport = new BoardViewport
                    {
                        SourceBounds = new PixelRect(0, 0, 50, 50),
                        ScreenBounds = new PixelRect(320, 240, 50, 50)
                    }
                }
            });

            Assert.Equal("inboard 320 240 50 50 3", update.ProtocolLine);
        }

        [Fact]
        public void BuildUpdate_DpiUnawareBackgroundSelectionScalesBoundsAndAddsDpiToken()
        {
            LegacyOverlayService overlayService = new LegacyOverlayService();

            OverlayUpdateResult update = overlayService.BuildUpdate(new OverlayUpdateRequest
            {
                Visibility = OverlayVisibility.Visible,
                LegacyTypeToken = "3",
                Frame = new BoardFrame
                {
                    SyncMode = SyncMode.Background,
                    Window = new WindowDescriptor
                    {
                        Bounds = new PixelRect(300, 200, 150, 150),
                        IsDpiAware = false,
                        DpiScale = 1.5d
                    },
                    Viewport = new BoardViewport
                    {
                        ScreenBounds = new PixelRect(320, 240, 50, 50)
                    }
                }
            });

            Assert.Equal(
                string.Format(CultureInfo.CurrentCulture, "inboard 213 160 33 33 99_{0}_3", 1.5d),
                update.ProtocolLine);
        }

        private sealed class RecordingTransport : IReadBoardTransport
        {
            public event System.EventHandler<string> MessageReceived;

            public List<string> SentLines { get; } = new List<string>();

            public bool IsConnected
            {
                get { return true; }
            }

            public void Dispose()
            {
            }

            public void Send(string line)
            {
                SentLines.Add(line);
            }

            public void Emit(string line)
            {
                MessageReceived?.Invoke(this, line);
            }

            public void SendError(string message)
            {
            }

            public void Start()
            {
            }

            public void Stop()
            {
            }
        }
    }
}
