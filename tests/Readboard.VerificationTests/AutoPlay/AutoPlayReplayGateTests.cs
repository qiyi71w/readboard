using System;
using System.Collections.Generic;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class AutoPlayReplayGateTests
    {
        [Fact]
        public void RecognizedReplaySnapshot_WithAutoPlayEnabledAndKeepSyncOff_DoesNotEmitPlay()
        {
            ReplayFixture fixture = ReplayFixtureCatalog.LoadForeground5x5();
            LegacyBoardRecognitionService recognition = new LegacyBoardRecognitionService();
            BoardRecognitionResult recognized = recognition.Recognize(
                fixture.CreateRecognitionRequest(ReplayVariant.Base, inferLastMove: false));
            Assert.True(recognized.Success, recognized.FailureReason);

            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SendBoardSnapshot(recognized.Snapshot);

            AutoPlayWireIssuer.IssueIfAuthorized(
                new ControlCenterRuntimeSnapshot
                {
                    TwoWaySync = true,
                    AutoPlayEnabled = true,
                    AutoPlayColorMode = AutoPlayColorMode.ManualBlack,
                    AutoPlayMoveMode = AutoPlayMoveMode.FirstCandidate,
                    AutoPlayColorResolution = AutoPlayColorResolution.Known(
                        "black",
                        AutoPlayColorStatus.ManualBlack),
                    PlayColor = "black",
                    AiTimeValue = "5",
                    PlayoutsValue = "1000",
                    FirstPolicyValue = "0"
                },
                keepSync: false,
                coordinator);

            Assert.Equal(fixture.BaseProtocolLines, recognized.Snapshot.ProtocolLines);
            Assert.Contains("end", transport.SentLines);
            Assert.DoesNotContain(
                transport.SentLines,
                line => line.StartsWith("play>", StringComparison.Ordinal));
            Assert.DoesNotContain(
                transport.SentLines,
                line => line.StartsWith("stopAutoPlay", StringComparison.Ordinal));
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

            public void Send(string line)
            {
                SentLines.Add(line);
            }

            public void SendError(string message)
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
        }
    }
}
