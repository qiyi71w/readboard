using System;
using System.Collections.Generic;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class AutoPlayWireIssuerTests
    {
        [Fact]
        public void IssueIfAuthorized_KeepSyncOff_DoesNotEmitPlay()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            AutoPlayWireIssuer.IssueIfAuthorized(
                CreateSnapshot(keepTwoWay: true, autoPlayEnabled: true, colorKnown: true),
                keepSync: false,
                coordinator);

            Assert.Empty(transport.SentLines);
        }

        [Fact]
        public void IssueIfAuthorized_KeepSyncOffAfterFoxAutoPlay_RevokesOnceWithoutAnotherPlay()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            ControlCenterRuntimeSnapshot snapshot = CreateSnapshot(
                keepTwoWay: true,
                autoPlayEnabled: true,
                colorKnown: true,
                AutoPlayColorMode.FoxAuto);

            AutoPlayWireIssuer.IssueIfAuthorized(snapshot, keepSync: true, coordinator);
            AutoPlayWireIssuer.IssueIfAuthorized(snapshot, keepSync: false, coordinator);

            Assert.Equal(
                new[] { "play>black>5 1000 0", "stopAutoPlay" },
                transport.SentLines);
        }

        [Fact]
        public void IssueIfAuthorized_TwoWaySyncOff_DoesNotEmitPlay()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            AutoPlayWireIssuer.IssueIfAuthorized(
                CreateSnapshot(keepTwoWay: false, autoPlayEnabled: true, colorKnown: true),
                keepSync: true,
                coordinator);

            Assert.Empty(transport.SentLines);
        }

        [Fact]
        public void IssueIfAuthorized_AutoPlayDisabled_DoesNotEmitPlay()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            AutoPlayWireIssuer.IssueIfAuthorized(
                CreateSnapshot(keepTwoWay: true, autoPlayEnabled: false, colorKnown: true),
                keepSync: true,
                coordinator);

            Assert.Empty(transport.SentLines);
        }

        [Fact]
        public void IssueIfAuthorized_AutoPlayDisabledAfterFoxAutoPlay_RevokesWithoutAnotherPlay()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            AutoPlayWireIssuer.IssueIfAuthorized(
                CreateSnapshot(
                    keepTwoWay: true,
                    autoPlayEnabled: true,
                    colorKnown: true,
                    AutoPlayColorMode.FoxAuto),
                keepSync: true,
                coordinator);
            AutoPlayWireIssuer.IssueIfAuthorized(
                CreateSnapshot(
                    keepTwoWay: true,
                    autoPlayEnabled: false,
                    colorKnown: true,
                    AutoPlayColorMode.FoxAuto),
                keepSync: true,
                coordinator);

            Assert.Equal(
                new[] { "play>black>5 1000 0", "stopAutoPlay" },
                transport.SentLines);
        }

        [Fact]
        public void IssueIfAuthorized_UnknownColor_DoesNotEmitPlayAndRevokesAuthorization()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            AutoPlayWireIssuer.IssueIfAuthorized(
                CreateSnapshot(
                    keepTwoWay: true,
                    autoPlayEnabled: true,
                    colorKnown: true,
                    AutoPlayColorMode.FoxAuto),
                keepSync: true,
                coordinator);
            AutoPlayWireIssuer.IssueIfAuthorized(
                CreateSnapshot(
                    keepTwoWay: true,
                    autoPlayEnabled: true,
                    colorKnown: false,
                    AutoPlayColorMode.FoxAuto),
                keepSync: true,
                coordinator);

            Assert.Equal(
                new[] { "play>black>5 1000 0", "stopAutoPlay" },
                transport.SentLines);
        }

        [Fact]
        public void IssueIfAuthorized_AuthorizedManualBlack_EmitsOneCompletePlay()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());

            AutoPlayWireIssuer.IssueIfAuthorized(
                CreateSnapshot(keepTwoWay: true, autoPlayEnabled: true, colorKnown: true),
                keepSync: true,
                coordinator);

            Assert.Equal(new[] { "play>black>5 1000 0" }, transport.SentLines);
        }

        [Fact]
        public void IssueIfAuthorized_AuthorizedGma_EmitsPlayWithGmaToken()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            ControlCenterRuntimeSnapshot snapshot = CreateSnapshot(
                keepTwoWay: true,
                autoPlayEnabled: true,
                colorKnown: true);
            snapshot.AutoPlayMoveMode = AutoPlayMoveMode.GenmoveAnalyze;

            AutoPlayWireIssuer.IssueIfAuthorized(snapshot, keepSync: true, coordinator);

            Assert.Equal(new[] { "play>black>5 1000 0 gma" }, transport.SentLines);
        }

        [Fact]
        public void IssueIfAuthorized_BlankEngineValues_SerializeAsZero()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            ControlCenterRuntimeSnapshot snapshot = CreateSnapshot(
                keepTwoWay: true,
                autoPlayEnabled: true,
                colorKnown: true);
            snapshot.AiTimeValue = " ";
            snapshot.PlayoutsValue = null;
            snapshot.FirstPolicyValue = string.Empty;

            AutoPlayWireIssuer.IssueIfAuthorized(snapshot, keepSync: true, coordinator);

            Assert.Equal(new[] { "play>black>0 0 0" }, transport.SentLines);
        }

        private static ControlCenterRuntimeSnapshot CreateSnapshot(
            bool keepTwoWay,
            bool autoPlayEnabled,
            bool colorKnown,
            AutoPlayColorMode colorMode = AutoPlayColorMode.ManualBlack)
        {
            return new ControlCenterRuntimeSnapshot
            {
                TwoWaySync = keepTwoWay,
                AutoPlayEnabled = autoPlayEnabled,
                AutoPlayColorMode = colorMode,
                AutoPlayMoveMode = AutoPlayMoveMode.FirstCandidate,
                AutoPlayColorResolution = colorKnown
                    ? AutoPlayColorResolution.Known("black", AutoPlayColorStatus.ManualBlack)
                    : AutoPlayColorResolution.Unknown(AutoPlayColorStatus.ColorUnknown),
                PlayColor = colorKnown ? "black" : null,
                AiTimeValue = "5",
                PlayoutsValue = "1000",
                FirstPolicyValue = "0"
            };
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
