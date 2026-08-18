using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using readboard;
using Readboard.VerificationTests.Support;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class SyncSessionCoordinatorOrchestrationTests
    {
        [Fact]
        public void TryStartKeepSync_OwnsInitialProbeAndLegacyStartFlow()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());

            Invoke(coordinator, "AttachRuntime", runtime);

            bool started = (bool)Invoke(coordinator, "TryStartKeepSync");
            Assert.True(started);
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
            Assert.True(transport.WaitForLines(7));

            Invoke(coordinator, "StopSyncSession");

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
            Assert.Equal(
                new[]
                {
                    "notForeFoxWithInBoard",
                    "sync",
                    "start 19 19",
                    "syncPlatform generic",
                    "lastMoveSource none",
                    "re=foreground",
                    "end"
                },
                transport.SentLines.GetRange(0, 7));
            Assert.True(hostRecorder.SnapshotRequests >= 2);
        }

        [Fact]
        public void TryRunOneTimeSync_NotifiesHostOfRecognizedSnapshot()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");

            BoardRecognitionResult recognition = CreateResultWithBoardState(
                "re=foreground",
                BoardCellState.BlackLastMove);
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(recognition));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());

            Invoke(coordinator, "AttachRuntime", runtime);

            Assert.True((bool)Invoke(coordinator, "TryRunOneTimeSync"));

            Assert.Equal(1, hostRecorder.RecognizedSnapshotCount);
            Assert.Same(recognition.Snapshot, hostRecorder.LastRecognizedSnapshot);
        }

        [Theory]
        [InlineData(0, "play>black>0 0 0", 1)]
        [InlineData(1, "play>black>0 0 0 gma", 2)]
        public void KeepSync_ResendsPlayAfterFoxLiveRoomContextChanges(
            int moveModeValue,
            string playLine,
            int expectedBoardFrameCount)
        {
            AutoPlayMoveMode moveMode = (AutoPlayMoveMode)moveModeValue;
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("fox");
            coordinator.SetSyncBoth(true);
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, new IntPtr(5151));
            SetProperty(snapshot, "PlayColor", "black");
            SetProperty(snapshot, "AutoPlayMoveMode", moveMode);
            FoxLiveContextSequenceHostRecorder hostRecorder = new FoxLiveContextSequenceHostRecorder(
                snapshot,
                coordinator,
                CreateFoxLiveRoomContext("111号", 57),
                CreateFoxLiveRoomContext("222号", 57));
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            ScriptedBlockingRecognitionService recognitionService = new ScriptedBlockingRecognitionService(
                CreateResult("re=fox"),
                4);
            SetProperty(runtime, "RecognitionService", recognitionService);
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(
                descriptorInterfaceType,
                new DescriptorFactoryRecorder().HandleCall));

            Invoke(coordinator, "AttachRuntime", runtime);

            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            try
            {
                VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
                VerificationCompletion.Wait(recognitionService.BlockedRecognizeStarted, "Recognition did not block as expected.");
            }
            finally
            {
                recognitionService.Release();
                Invoke(coordinator, "StopSyncSession");
            }

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
            int secondRoomIndex = transport.SentLines.IndexOf("roomToken 222号");
            int firstPlayIndex = transport.SentLines.IndexOf(playLine);
            int firstBoardIndex = transport.SentLines.IndexOf("re=fox");
            int secondPlayIndex = transport.SentLines.IndexOf(playLine, firstPlayIndex + 1);
            int postPlayBoardIndex = transport.SentLines.IndexOf("re=fox", secondPlayIndex + 1);
            Assert.True(firstPlayIndex >= 0, "Initial keep sync should send the selected play color.");
            Assert.True(firstBoardIndex > firstPlayIndex, "Initial keep sync should send an authoritative board after play state replay.");
            Assert.True(secondRoomIndex >= 0, "The second Fox room context should be sent.");
            Assert.True(secondPlayIndex > secondRoomIndex, "Changing Fox live room should resend play after the new room context.");
            Assert.Equal(moveMode == AutoPlayMoveMode.GenmoveAnalyze, postPlayBoardIndex > secondPlayIndex);
            Assert.Equal(expectedBoardFrameCount, transport.CountLines("re=fox"));
        }

        [Fact]
        public void KeepSync_DoesNotSendPlayAfterFoxRoomAuthorizationIsInvalidated()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("fox");
            coordinator.SetSyncBoth(true);
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, new IntPtr(5151));
            SetProperty(snapshot, "PlayColor", "black");
            SetProperty(snapshot, "AutoPlayColorMode", AutoPlayColorMode.FoxAuto);
            FoxRoomAuthorizationSequenceHostRecorder hostRecorder = new FoxRoomAuthorizationSequenceHostRecorder(
                snapshot,
                coordinator,
                CreateFoxLiveRoomContext("111号", 57),
                CreateFoxLiveRoomContext("222号", 57));
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            ScriptedBlockingRecognitionService recognitionService = new ScriptedBlockingRecognitionService(
                CreateResult("re=fox"),
                2);
            SetProperty(runtime, "RecognitionService", recognitionService);
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(
                descriptorInterfaceType,
                new DescriptorFactoryRecorder().HandleCall));

            Invoke(coordinator, "AttachRuntime", runtime);

            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            try
            {
                VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
                VerificationCompletion.Wait(
                    recognitionService.BlockedRecognizeStarted,
                    "Current-room recognition did not block as expected.");
                Assert.Equal(1, transport.CountLines("stopAutoPlay"));
                recognitionService.Release();
            }
            finally
            {
                recognitionService.Release();
                Invoke(coordinator, "StopSyncSession");
            }

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
            Assert.Equal(1, transport.CountLines("play>black>0 0 0"));
            Assert.Equal(1, transport.CountLines("stopAutoPlay"));
        }

        [Theory]
        [InlineData(0, "play>black>0 0 0")]
        [InlineData(1, "play>black>0 0 0 gma")]
        public void KeepSync_RearmsUnchangedPlayAfterStopAutoPlay(
            int moveModeValue,
            string playLine)
        {
            AutoPlayMoveMode moveMode = (AutoPlayMoveMode)moveModeValue;
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncBoth(true);
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");

            AutoPlayToggleHostRecorder hostRecorder = new AutoPlayToggleHostRecorder(
                snapshotType,
                moveMode);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            ScriptedBlockingRecognitionService recognitionService = new ScriptedBlockingRecognitionService(
                CreateResult("re=foreground"),
                3);
            SetProperty(runtime, "RecognitionService", recognitionService);
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());

            Invoke(coordinator, "AttachRuntime", runtime);

            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            try
            {
                VerificationCompletion.Wait(recognitionService.BlockedRecognizeStarted, "Recognition did not block as expected.");
                Assert.Equal(1, transport.CountLines(playLine));

                hostRecorder.SetAutoPlayEnabled(false);
                coordinator.SendStopAutoPlay();
                recognitionService.Release();
                VerificationCompletion.Wait(hostRecorder.DisabledSnapshotCaptured, "Disabled snapshot was not captured.");
                Assert.Equal(1, transport.CountLines(playLine));

                hostRecorder.SetAutoPlayEnabled(true);
                VerificationCompletion.Wait(hostRecorder.PostReenableSamplesSettled, "Re-enabled samples did not settle.");
                Assert.Equal(2, transport.CountLines(playLine));

                int secondPlayIndex;
                lock (transport.SentLines)
                {
                    int firstPlayIndex = transport.SentLines.IndexOf(playLine);
                    int stopIndex = transport.SentLines.IndexOf("stopAutoPlay", firstPlayIndex + 1);
                    secondPlayIndex = transport.SentLines.IndexOf(playLine, stopIndex + 1);
                    Assert.True(stopIndex > firstPlayIndex);
                    Assert.True(secondPlayIndex > stopIndex);
                }
                lock (transport.SentLines)
                {
                    int postPlayBoardIndex = transport.SentLines.IndexOf("re=foreground", secondPlayIndex + 1);
                    Assert.Equal(
                        moveMode == AutoPlayMoveMode.GenmoveAnalyze,
                        postPlayBoardIndex > secondPlayIndex);
                }
            }
            finally
            {
                recognitionService.Release();
                Invoke(coordinator, "StopSyncSession");
            }

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
        }

        [Fact]
        public void KeepSync_ResendsPlayAfterFoxRecordViewContextChanges()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("fox");
            coordinator.SetSyncBoth(true);
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, new IntPtr(5151));
            SetProperty(snapshot, "PlayColor", "black");
            FoxLiveContextSequenceHostRecorder hostRecorder = new FoxLiveContextSequenceHostRecorder(
                snapshot,
                coordinator,
                CreateFoxRecordViewContext(10, 120, false, "game-1"),
                CreateFoxRecordViewContext(10, 120, false, "game-1-variant"),
                CreateFoxRecordViewContext(11, 120, false, "game-1-variant"));
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=fox")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(
                descriptorInterfaceType,
                new DescriptorFactoryRecorder().HandleCall));

            Invoke(coordinator, "AttachRuntime", runtime);

            bool started = (bool)Invoke(coordinator, "TryStartKeepSync");
            Assert.True(started);
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
            Assert.True(transport.WaitForLine("recordCurrentMove 11"));

            Invoke(coordinator, "StopSyncSession");

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
            int secondFingerprintIndex = transport.SentLines.IndexOf("recordTitleFingerprint game-1-variant");
            int firstPlayIndex = transport.SentLines.IndexOf("play>black>0 0 0");
            int secondPlayIndex = transport.SentLines.IndexOf("play>black>0 0 0", firstPlayIndex + 1);
            Assert.True(firstPlayIndex >= 0, "Initial keep sync should send the selected play color.");
            Assert.True(secondFingerprintIndex >= 0, "The second Fox record context should be sent.");
            Assert.True(secondPlayIndex > secondFingerprintIndex, "Changing Fox record fingerprint should resend play after the new record context.");
            Assert.Equal(2, transport.CountLines("play>black>0 0 0"));
        }

        [Fact]
        public void KeepSync_DoesNotResendPlayWhenOnlyFoxLiveMoveChanges()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("fox");
            coordinator.SetSyncBoth(true);
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, new IntPtr(5151));
            SetProperty(snapshot, "PlayColor", "black");
            FoxLiveContextSequenceHostRecorder hostRecorder = new FoxLiveContextSequenceHostRecorder(
                snapshot,
                coordinator,
                CreateFoxLiveRoomContext("111号", 57),
                CreateFoxLiveRoomContext("111号", 58));
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=fox")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(
                descriptorInterfaceType,
                new DescriptorFactoryRecorder().HandleCall));

            Invoke(coordinator, "AttachRuntime", runtime);

            bool started = (bool)Invoke(coordinator, "TryStartKeepSync");
            Assert.True(started);
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
            Assert.True(transport.WaitForLine("liveTitleMove 58"));

            Invoke(coordinator, "StopSyncSession");

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
            Assert.Equal(1, transport.CountLines("play>black>0 0 0"));
        }

        [Fact]
        public void KeepSync_DoesNotConsumeFoxRoomPlayRearmOnInvalidSnapshot()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("fox");
            coordinator.SetSyncBoth(true);
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, new IntPtr(5151));
            SetProperty(snapshot, "PlayColor", "black");
            FoxLiveContextSequenceHostRecorder hostRecorder = new FoxLiveContextSequenceHostRecorder(
                snapshot,
                coordinator,
                CreateFoxLiveRoomContext("111号", 57),
                CreateFoxLiveRoomContext("222号", 58),
                CreateFoxLiveRoomContext("222号", 58));
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(
                CreateResult("re=initial"),
                CreateInvalidResult("re=invalid"),
                CreateResult("re=fox")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(
                descriptorInterfaceType,
                new DescriptorFactoryRecorder().HandleCall));

            Invoke(coordinator, "AttachRuntime", runtime);

            bool started = (bool)Invoke(coordinator, "TryStartKeepSync");
            Assert.True(started);
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
            Assert.True(transport.WaitForLine("roomToken 222号"));

            Invoke(coordinator, "StopSyncSession");

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
            int secondRoomIndex = transport.SentLines.IndexOf("roomToken 222号");
            int firstPlayIndex = transport.SentLines.IndexOf("play>black>0 0 0");
            int secondPlayIndex = transport.SentLines.IndexOf("play>black>0 0 0", firstPlayIndex + 1);
            Assert.True(secondPlayIndex > secondRoomIndex, "Invalid snapshots must not consume the Fox room play rearm before the new room context is sent.");
            Assert.Equal(2, transport.CountLines("play>black>0 0 0"));
        }

        [Fact]
        public void TryStartKeepSync_YikeSendsYikeSyncControlCommands()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("yike");
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Yike, new IntPtr(4242));
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=yike")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(
                descriptorInterfaceType,
                new DescriptorFactoryRecorder().HandleCall));

            Invoke(coordinator, "AttachRuntime", runtime);

            bool started = (bool)Invoke(coordinator, "TryStartKeepSync");
            Assert.True(started);
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
            Assert.True(transport.WaitForLines(7));

            Invoke(coordinator, "StopSyncSession");

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
            int syncIndex = transport.SentLines.IndexOf("sync");
            int yikeStartIndex = transport.SentLines.IndexOf("yikeSyncStart");
            int startIndex = transport.SentLines.IndexOf("start 19 19 4242");
            Assert.True(syncIndex >= 0, "Yike keep sync should still send the legacy sync command.");
            Assert.True(yikeStartIndex > syncIndex, "Yike browser sync should start after legacy sync is enabled.");
            Assert.True(startIndex > yikeStartIndex, "Yike browser sync should start before the first board frame.");
            Assert.Contains("yikeSyncStop", transport.SentLines);
            Assert.True(
                transport.SentLines.IndexOf("yikeSyncStop") < transport.SentLines.IndexOf("stopsync"),
                "Yike browser sync should stop before the legacy readboard sync state is cleared.");
        }

        [Fact]
        public void YikeBrowserSyncStopRequest_StopsActiveYikeKeepSyncSession()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("yike");
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type protocolHostInterfaceType = RequireType(assembly, "readboard.IProtocolCommandHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Yike, new IntPtr(4242));
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=yike")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(
                descriptorInterfaceType,
                new DescriptorFactoryRecorder().HandleCall));

            Invoke(coordinator, "AttachRuntime", runtime);
            Invoke(coordinator, "AttachHost", CreateProxy(protocolHostInterfaceType, (method, args) =>
            {
                if (method.Name == "DispatchProtocolCommand")
                {
                    ((Action)args[0])();
                    return null;
                }
                return GetDefault(method.ReturnType);
            }));
            coordinator.Start();

            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
            Assert.True(transport.WaitForLines(7));

            transport.Emit("yikeBrowserSyncStop");

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
            Assert.False(coordinator.StartedSync);
            Assert.Contains("yikeSyncStop", transport.SentLines);
            Assert.Contains("stopsync", transport.SentLines);
        }

        [Fact]
        public void TryStartContinuousSync_UsesWindowLocatorAndCoreDescriptorFactory()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type locatorInterfaceType = RequireType(assembly, "readboard.ISyncWindowLocator");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, IntPtr.Zero);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            WindowLocatorRecorder locatorRecorder = new WindowLocatorRecorder(new IntPtr(4242));
            object locator = CreateProxy(locatorInterfaceType, locatorRecorder.HandleCall);
            DescriptorFactoryRecorder descriptorRecorder = new DescriptorFactoryRecorder();
            object descriptorFactory = CreateProxy(descriptorInterfaceType, descriptorRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=fox")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowLocator", locator);
            SetProperty(runtime, "WindowDescriptorFactory", descriptorFactory);

            Invoke(coordinator, "AttachRuntime", runtime);

            bool started = (bool)Invoke(coordinator, "TryStartContinuousSync");
            Assert.True(started);
            VerificationCompletion.Wait(hostRecorder.ContinuousStarted, "Continuous sync did not start.");
            Assert.True(transport.WaitForLines(7));

            Invoke(coordinator, "StopSyncSession");

            VerificationCompletion.Wait(hostRecorder.ContinuousStopped, "Continuous sync did not stop.");
            Assert.True(locatorRecorder.Calls > 0);
            Assert.Equal(new IntPtr(4242), hostRecorder.LastSelectedWindow);
            Assert.Equal(
                new[]
                {
                    "notForeFoxWithInBoard",
                    "sync",
                    "start 19 19 4242",
                    "syncPlatform generic",
                    "lastMoveSource none",
                    "re=fox",
                    "end"
                },
                transport.SentLines.GetRange(0, 7));
        }

        [Fact]
        public void CreateCaptureRequest_UsesEnhancedCaptureFlagFromHostSnapshot()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Background, new IntPtr(5151));
            SetProperty(snapshot, "UseEnhancedCapture", true);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            DescriptorFactoryRecorder descriptorFactoryRecorder = new DescriptorFactoryRecorder();
            object descriptorFactory = CreateProxy(descriptorInterfaceType, descriptorFactoryRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=background")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", descriptorFactory);

            Invoke(coordinator, "AttachRuntime", runtime);

            BoardCaptureRequest request = (BoardCaptureRequest)Invoke(coordinator, "CreateCaptureRequest", runtime, snapshot);

            Assert.NotNull(request);
            Assert.True(request.UseEnhancedCapture);
        }

        [Fact]
        public void TryStartKeepSync_StartsBackgroundWorkerThread()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "SampleIntervalMs", 1000);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());

            Invoke(coordinator, "AttachRuntime", runtime);

            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");

            Thread worker = ReadWorkerThread(coordinator, "keepSyncThread");
            Assert.True(worker.IsBackground);

            Invoke(coordinator, "StopSyncSession");
            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
        }

        [Fact]
        public void TryStartContinuousSync_StartsBackgroundWorkerThread()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type locatorInterfaceType = RequireType(assembly, "readboard.ISyncWindowLocator");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, IntPtr.Zero);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object locator = CreateProxy(locatorInterfaceType, (method, args) => IntPtr.Zero);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=fox")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowLocator", locator);

            Invoke(coordinator, "AttachRuntime", runtime);

            Assert.True((bool)Invoke(coordinator, "TryStartContinuousSync"));

            Thread worker = ReadWorkerThread(coordinator, "continuousSyncThread");
            Assert.True(worker.IsBackground);

            Invoke(coordinator, "StopSyncSession");
            VerificationCompletion.Wait(hostRecorder.ContinuousStopped, "Continuous sync did not stop.");
        }

        [Fact]
        public void StopSyncSession_ThenRestartContinuousSync_DoesNotLetStaleWorkerClearNewWorkerState()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type locatorInterfaceType = RequireType(assembly, "readboard.ISyncWindowLocator");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, IntPtr.Zero);
            BlockingContinuousSnapshotHostRecorder hostRecorder = new BlockingContinuousSnapshotHostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object locator = CreateProxy(locatorInterfaceType, (method, args) => IntPtr.Zero);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=fox")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowLocator", locator);

            Invoke(coordinator, "AttachRuntime", runtime);

            Assert.True((bool)Invoke(coordinator, "TryStartContinuousSync"));
            Thread staleWorker = ReadWorkerThread(coordinator, "continuousSyncThread");
            VerificationCompletion.Wait(hostRecorder.BlockedContinuousSnapshotStarted, "Continuous snapshot did not block as expected.");

            Invoke(coordinator, "StopSyncSession");

            hostRecorder.ContinuousStarted.Reset();
            Assert.True((bool)Invoke(coordinator, "TryStartContinuousSync"));
            VerificationCompletion.Wait(hostRecorder.ContinuousStarted, "Restarted continuous sync did not start.");

            Thread newWorker = ReadWorkerThread(coordinator, "continuousSyncThread");
            Assert.NotNull(newWorker);
            Assert.NotSame(staleWorker, newWorker);

            hostRecorder.ContinuousStopped.Reset();
            hostRecorder.ReleaseBlockedContinuousSnapshot();

            VerificationCompletion.Join(staleWorker, "Stale continuous worker did not exit.");
            Assert.Equal(0, hostRecorder.ContinuousStoppedCount);
            Assert.Same(newWorker, ReadWorkerThread(coordinator, "continuousSyncThread"));

            Invoke(coordinator, "StopSyncSession");

            VerificationCompletion.Wait(hostRecorder.ContinuousStopped, "Restarted continuous sync did not stop.");
            Assert.Equal(1, hostRecorder.ContinuousStoppedCount);
        }

        [Fact]
        public void Stop_DoesNotHangWhenKeepSyncWorkerBlocksInCapture()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            BlockingCaptureService captureService = new BlockingCaptureService(CreateFrame());
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "SampleIntervalMs", 0);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", captureService);
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(captureService.BlockedCaptureStarted, "Capture did not block as expected.");

            try
            {
                Task stopTask = StartDedicatedThread(
                    () => coordinator.Stop(),
                    "SyncSessionCoordinatorOrchestrationTests.Stop");
                AssertCompletes(
                    stopTask,
                    "Stop must return while capture is blocked.");
            }
            finally
            {
                captureService.Release();
            }

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
        }

        [Fact]
        public async Task Dispose_WaitsForBlockedKeepSyncWorkerBeforeReleasingWaitHandles()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            BlockingCaptureService captureService = new BlockingCaptureService(CreateFrame());
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "SampleIntervalMs", 0);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", captureService);
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(captureService.BlockedCaptureStarted, "Capture did not block as expected.");

            FieldInfo stopEventField = typeof(SyncSessionCoordinator).GetField(
                "keepSyncStopRequestedEvent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(stopEventField);
            ManualResetEventSlim stopRequested = (ManualResetEventSlim)stopEventField.GetValue(coordinator);
            Task disposeTask = StartDedicatedThread(
                () => coordinator.Dispose(),
                "SyncSessionCoordinatorOrchestrationTests.Dispose");
            VerificationCompletion.Wait(stopRequested, "Coordinator stop was not requested.");
            Assert.False(disposeTask.IsCompleted);
            Assert.False(hostRecorder.KeepStopped.IsSet);

            captureService.Release();

            await VerificationCompletion.WaitAsync(
                disposeTask,
                "Coordinator dispose did not complete.");
            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
        }

        [Fact]
        public async Task StopSyncSession_ReturnsBeforeBlockedKeepSyncWorkerFinishesCleanup()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            BlockingCaptureService captureService = new BlockingCaptureService(CreateFrame());
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "SampleIntervalMs", 0);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", captureService);
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(captureService.BlockedCaptureStarted, "Capture did not block as expected.");

            Task stopTask = StartDedicatedThread(
                () => Invoke(coordinator, "StopSyncSession"),
                "SyncSessionCoordinatorOrchestrationTests.StopSyncSession");
            try
            {
                AssertCompletes(
                    stopTask,
                    "StopSyncSession must return while capture cleanup is blocked.");
                Assert.False(hostRecorder.KeepStopped.IsSet);
            }
            finally
            {
                captureService.Release();
                await VerificationCompletion.WaitAsync(
                    stopTask,
                    "StopSyncSession did not complete after release.");
            }

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
        }

        [Fact]
        public void StopSyncSessionAndClearBoard_SendsStopBeforeClearWithoutWaitingForBlockedWorker()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            BlockingCaptureService captureService = new BlockingCaptureService(CreateFrame());
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "SampleIntervalMs", 0);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", captureService);
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(captureService.BlockedCaptureStarted, "Capture did not block as expected.");

            Invoke(coordinator, "StopSyncSessionAndClearBoard");

            Assert.Equal(
                new[] { "sync", "stopsync", "clearBoard" },
                transport.SentLines.GetRange(transport.SentLines.Count - 3, 3));

            captureService.Release();

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
            Assert.Equal(1, transport.CountLines("stopsync"));
            Assert.Equal("clearBoard", transport.SentLines[transport.SentLines.Count - 1]);
        }

        [Fact]
        public void StopSyncSessionAndClearBoard_DoesNotLetPreActivationSessionSendAfterClear()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "PlayColor", "black");
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);
            coordinator.SetSyncBoth(true);

            bool? startResult = null;
            Exception startException = null;
            ManualResetEventSlim startEntered = new ManualResetEventSlim(false);
            Thread startThread = new Thread(new ThreadStart(delegate
            {
                startEntered.Set();
                try
                {
                    startResult = coordinator.TryStartKeepSync();
                }
                catch (Exception ex)
                {
                    startException = ex;
                }
            }));
            startThread.IsBackground = true;

            object dispatcher = GetOutboundProtocolDispatcher(coordinator);
            Invoke(dispatcher, "ExecuteBatch", (Action)delegate
            {
                startThread.Start();
                VerificationCompletion.Wait(startEntered, "Concurrent startup thread did not enter.");

                coordinator.StopSyncSessionAndClearBoard();
            });

            VerificationCompletion.Join(startThread, "Concurrent startup thread did not exit.");
            Assert.Null(startException);
            Assert.False(startResult);
            Assert.Equal(new[] { "clearBoard" }, transport.SentLines);
        }

        [Fact]
        public void StopSyncSessionAndClearBoard_ThenRestart_DoesNotLetStaleWorkerStopNewSession()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "SampleIntervalMs", 1000);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            ScriptedBlockingCaptureService captureService = new ScriptedBlockingCaptureService(CreateFrame(), 2, false);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", captureService);
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);

            Thread staleWorker = null;
            try
            {
                Assert.True(coordinator.TryStartKeepSync());
                staleWorker = ReadWorkerThread(coordinator, "keepSyncThread");
                VerificationCompletion.Wait(captureService.BlockedCaptureStarted, "Capture did not block as expected.");

                coordinator.StopSyncSessionAndClearBoard();
                Assert.True(coordinator.TryStartKeepSync());
                Thread restartedWorker = ReadWorkerThread(coordinator, "keepSyncThread");

                captureService.Release();

                VerificationCompletion.Join(staleWorker, "Stale keep-sync worker did not exit.");
                Assert.True(restartedWorker.IsAlive);
                Assert.True(coordinator.StartedSync);
                Assert.Equal(1, transport.CountLines("stopsync"));

                hostRecorder.KeepStopped.Reset();
                coordinator.StopSyncSession();

                VerificationCompletion.Wait(hostRecorder.KeepStopped, "Restarted keep sync did not stop.");
                Assert.Equal(2, transport.CountLines("stopsync"));
            }
            finally
            {
                captureService.Release();
                coordinator.StopSyncSession();
            }
        }


        [Fact]
        public void StopThenRestart_ClearBoardDoesNotTreatClosedWorkerStopAsPending()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            BlockingCaptureService captureService = new BlockingCaptureService(CreateFrame());
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "SampleIntervalMs", 0);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", captureService);
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);

            coordinator.Start();
            try
            {
                Assert.True(coordinator.TryStartKeepSync());
                Thread worker = ReadWorkerThread(coordinator, "keepSyncThread");
                VerificationCompletion.Wait(captureService.BlockedCaptureStarted, "Capture did not block as expected.");

                coordinator.Stop();
                captureService.Release();

                VerificationCompletion.Join(worker, "Stopped worker did not exit before restart.");
                coordinator.Start();
                coordinator.StopSyncSessionAndClearBoard();

                Assert.Equal(new[] { "notForeFoxWithInBoard", "sync", "clearBoard" }, transport.SentLines);
            }
            finally
            {
                captureService.Release();
                coordinator.Stop();
            }
        }

        [Fact]
        public async Task StopSyncSessionAndClearBoard_DoesNotDuplicateStopAlreadyBeingSent()
        {
            BlockingLineTransport transport = new BlockingLineTransport("stopsync");
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            BlockingCaptureService captureService = new BlockingCaptureService(CreateFrame());
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "SampleIntervalMs", 0);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", captureService);
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);
            Assert.True(coordinator.TryStartKeepSync());
            VerificationCompletion.Wait(captureService.BlockedCaptureStarted, "Capture did not block as expected.");

            coordinator.StopSyncSession();
            captureService.Release();
            VerificationCompletion.Wait(transport.BlockedSendStarted, "Outbound send did not block as expected.");

            hostRecorder.SyncCachesReset.Reset();
            Task clearTask = StartDedicatedThread(
                () => coordinator.StopSyncSessionAndClearBoard(),
                "SyncSessionCoordinatorOrchestrationTests.StopSyncSessionAndClearBoard");
            try
            {
                VerificationCompletion.Wait(hostRecorder.SyncCachesReset, "Sync cache reset did not occur.");
            }
            finally
            {
                transport.Release();
            }

            await VerificationCompletion.WaitAsync(
                clearTask,
                "StopSyncSessionAndClearBoard did not complete.");
            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
            Assert.Equal(1, transport.CountLines("stopsync"));
            Assert.Equal("clearBoard", transport.SentLines[transport.SentLines.Count - 1]);
        }

        [Fact]
        public void StopSyncSession_DuringDiscoveredKeepSyncPrime_DoesNotRestartKeepSyncAfterStop()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type locatorInterfaceType = RequireType(assembly, "readboard.ISyncWindowLocator");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, IntPtr.Zero);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            WindowLocatorRecorder locatorRecorder = new WindowLocatorRecorder(new IntPtr(4242));
            object locator = CreateProxy(locatorInterfaceType, locatorRecorder.HandleCall);
            DescriptorFactoryRecorder descriptorFactory = new DescriptorFactoryRecorder();
            object runtime = Activator.CreateInstance(runtimeType);
            ScriptedBlockingCaptureService captureService = new ScriptedBlockingCaptureService(CreateFrame(), 1, false);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", captureService);
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=fox")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowLocator", locator);
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(descriptorInterfaceType, descriptorFactory.HandleCall));
            Invoke(coordinator, "AttachRuntime", runtime);

            Assert.True((bool)Invoke(coordinator, "TryStartContinuousSync"));
            VerificationCompletion.Wait(captureService.BlockedCaptureStarted, "Capture did not block as expected.");

            Invoke(coordinator, "StopSyncSession");
            captureService.Release();

            VerificationCompletion.Wait(hostRecorder.ContinuousStopped, "Continuous sync did not stop.");
            Assert.Equal(0, hostRecorder.KeepStartedCount);
            Assert.DoesNotContain("sync", transport.SentLines);
            Assert.DoesNotContain("start 19 19 4242", transport.SentLines);
            Assert.True(locatorRecorder.Calls > 0);
        }

        [Fact]
        public async Task Stop_DuringBlockedPlacement_CancelsPlacementBeforeActualClick()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "SampleIntervalMs", 0);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            ReflectiveCancellationAwareBlockingPlacementService placementService = new ReflectiveCancellationAwareBlockingPlacementService();
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", placementService);
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
            coordinator.SetSyncBoth(true);
            Assert.True(coordinator.TryQueuePendingMove(new MoveRequest { X = 1, Y = 1, VerifyMove = false }, 190, 19));
            VerificationCompletion.Wait(placementService.BlockedPlacementStarted, "Placement did not block as expected.");

            Task stopTask = StartDedicatedThread(
                () => coordinator.Stop(),
                "SyncSessionCoordinatorOrchestrationTests.StopDuringPlacement");
            try
            {
                AssertCompletes(
                    stopTask,
                    "Stop must return while placement is blocked.");
            }
            finally
            {
                placementService.Release();
                await VerificationCompletion.WaitAsync(
                    stopTask,
                    "Stop did not complete after placement release.");
            }

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
            Assert.Equal(1, placementService.PlaceCallCount);
            Assert.Equal(0, placementService.ActualPlacementCount);
        }

        [Fact]
        public async Task HandlePlaceRequest_WhenPlacementThrowsCompletesAsFailure()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            ThrowingPlacementService placementService = new ThrowingPlacementService();
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", placementService);
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);

            try
            {
                Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
                VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
                coordinator.SetSyncBoth(true);
                SetRuntimeBoardPixelWidth(coordinator, 190);

                Task<PlaceRequestExecutionResult> resultTask = StartDedicatedThread(
                    () => coordinator.HandlePlaceRequest(
                        new MoveRequest { X = 1, Y = 1, VerifyMove = false }),
                    "SyncSessionCoordinatorOrchestrationTests.HandlePlaceRequest");

                VerificationCompletion.Wait(placementService.PlaceCalled, "Placement was not called.");
                PlaceRequestExecutionResult result = await VerificationCompletion.WaitAsync(
                    resultTask,
                    "Place request result did not complete.");

                Assert.True(result.ShouldSendResponse);
                Assert.False(result.Success);
            }
            finally
            {
                coordinator.Stop();
            }
        }

        [Fact]
        public async Task StopSyncSession_AfterPlacementSideEffect_WaitsForActualPlacementResult()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            SideEffectThenBlockingPlacementService placementService = new SideEffectThenBlockingPlacementService();
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", placementService);
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
            coordinator.SetSyncBoth(true);
            SetRuntimeBoardPixelWidth(coordinator, 190);

            Task<PlaceRequestExecutionResult> resultTask = StartDedicatedThread(
                () => coordinator.HandlePlaceRequest(
                    new MoveRequest { X = 1, Y = 1, VerifyMove = false }),
                "SyncSessionCoordinatorOrchestrationTests.HandlePlaceRequest");

            VerificationCompletion.Wait(placementService.SideEffectApplied, "Placement side effect was not applied.");
            Invoke(coordinator, "StopSyncSession");

            Assert.False(resultTask.IsCompleted);

            placementService.Release();

            PlaceRequestExecutionResult result = await VerificationCompletion.WaitAsync(
                resultTask,
                "Place request result did not complete.");
            Assert.True(result.ShouldSendResponse);
            Assert.True(result.Success);
            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
        }

        [Fact]
        public async Task Stop_AfterPlacementSideEffect_WaitsForActualPlacementResult()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            SideEffectThenBlockingPlacementService placementService = new SideEffectThenBlockingPlacementService();
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", placementService);
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
            coordinator.SetSyncBoth(true);
            SetRuntimeBoardPixelWidth(coordinator, 190);

            Task<PlaceRequestExecutionResult> resultTask = StartDedicatedThread(
                () => coordinator.HandlePlaceRequest(
                    new MoveRequest { X = 1, Y = 1, VerifyMove = false }),
                "SyncSessionCoordinatorOrchestrationTests.HandlePlaceRequest");

            VerificationCompletion.Wait(placementService.SideEffectApplied, "Placement side effect was not applied.");
            coordinator.Stop();

            Assert.False(resultTask.IsCompleted);

            placementService.Release();

            PlaceRequestExecutionResult result = await VerificationCompletion.WaitAsync(
                resultTask,
                "Place request result did not complete.");
            Assert.True(result.ShouldSendResponse);
            Assert.True(result.Success);
            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
        }

        [Fact]
        public void StopSyncSession_ThenRestartKeepSync_DoesNotLetStaleWorkerCleanupStopNewSession()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "SampleIntervalMs", 1000);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            ScriptedBlockingCaptureService captureService = new ScriptedBlockingCaptureService(CreateFrame(), 2, true);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", captureService);
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
            Thread staleWorker = ReadWorkerThread(coordinator, "keepSyncThread");
            VerificationCompletion.Wait(captureService.BlockedCaptureStarted, "Capture did not block as expected.");

            Invoke(coordinator, "StopSyncSession");
            hostRecorder.KeepStarted.Reset();
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Restarted keep sync did not start.");

            captureService.Release();

            VerificationCompletion.Join(staleWorker, "Stale keep-sync worker did not exit.");
            Assert.True(coordinator.StartedSync);
            Assert.Equal(0, hostRecorder.KeepStoppedCount);
            Assert.Equal(0, transport.CountLines("stopsync"));

            hostRecorder.KeepStopped.Reset();
            Invoke(coordinator, "StopSyncSession");

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Restarted keep sync did not stop.");
            Assert.Equal(1, hostRecorder.KeepStoppedCount);
            Assert.Equal(1, transport.CountLines("stopsync"));
        }

        [Fact]
        public void StopSyncSession_ThenRestartKeepSync_PreservesLifecycleThroughStopAndRestart()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");
            IntPtr firstHandle = new IntPtr(1111);
            IntPtr secondHandle = new IntPtr(2222);
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, firstHandle);
            LightweightBindingRestartHostRecorder hostRecorder = new LightweightBindingRestartHostRecorder(snapshot, coordinator);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            ScriptedBlockingCaptureService captureService = new ScriptedBlockingCaptureService(CreateFrame(), 2, true);
            SingleLightweightPlacementService placementService = new SingleLightweightPlacementService();
            DescriptorFactoryRecorder descriptorFactory = new DescriptorFactoryRecorder();
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", captureService);
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=fox")));
            SetProperty(runtime, "PlacementService", placementService);
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(descriptorInterfaceType, descriptorFactory.HandleCall));
            Invoke(coordinator, "AttachRuntime", runtime);
            coordinator.SetSyncBoth(true);

            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
            Assert.True(hostRecorder.InitialMoveQueued);
            VerificationCompletion.Wait(placementService.PlaceCalled, "Initial move was not placed.");
            Thread staleWorker = ReadWorkerThread(coordinator, "keepSyncThread");
            VerificationCompletion.Wait(captureService.BlockedCaptureStarted, "Capture did not block as expected.");

            Invoke(coordinator, "StopSyncSession");
            SetProperty(snapshot, "SelectedWindowHandle", secondHandle);
            hostRecorder.KeepStarted.Reset();
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            VerificationCompletion.Wait(hostRecorder.KeepStarted, "Restarted keep sync did not start.");

            captureService.Release();

            VerificationCompletion.Join(staleWorker, "Stale keep-sync worker did not exit.");
            Assert.True(coordinator.StartedSync);
            Assert.Equal(0, hostRecorder.KeepStoppedCount);

            hostRecorder.KeepStopped.Reset();
            Invoke(coordinator, "StopSyncSession");

            VerificationCompletion.Wait(hostRecorder.KeepStopped, "Restarted keep sync did not stop.");
        }

        [Fact]
        public async Task StopSyncSession_WhileDispatchBatchWaitsForOutboundLock_DoesNotSendStaleBoardProtocol()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
            SetProperty(snapshot, "SampleIntervalMs", 1000);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            ScriptedBlockingRecognitionService recognitionService = new ScriptedBlockingRecognitionService(CreateResult("re=foreground"), 2);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", recognitionService);
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);

            ManualResetEventSlim releaseOutboundLock = new ManualResetEventSlim(false);
            ManualResetEventSlim outboundLockHeld = new ManualResetEventSlim(false);
            Task holdOutboundLockTask = null;

            try
            {
                Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
                VerificationCompletion.Wait(hostRecorder.KeepStarted, "Keep sync did not start.");
                VerificationCompletion.Wait(recognitionService.BlockedRecognizeStarted, "Recognition did not block as expected.");

                holdOutboundLockTask = StartDedicatedThread(delegate
                {
                    object dispatcher = GetOutboundProtocolDispatcher(coordinator);
                    Invoke(dispatcher, "ExecuteBatch", (Action)delegate
                    {
                        outboundLockHeld.Set();
                        releaseOutboundLock.Wait();
                    });
                }, "SyncSessionCoordinatorOrchestrationTests.OutboundLock");
                VerificationCompletion.Wait(outboundLockHeld, "Outbound lock was not acquired.");

                recognitionService.Release();
                recognitionService.WaitForCallCount(2);

                Invoke(coordinator, "StopSyncSession");

                releaseOutboundLock.Set();

                VerificationCompletion.Wait(hostRecorder.KeepStopped, "Keep sync did not stop.");
                Assert.DoesNotContain("start 19 19", transport.SentLines);
                Assert.DoesNotContain("syncPlatform generic", transport.SentLines);
                Assert.DoesNotContain("re=foreground", transport.SentLines);
                Assert.DoesNotContain("end", transport.SentLines);
                Assert.Equal(1, transport.CountLines("stopsync"));
            }
            finally
            {
                recognitionService.Release();
                releaseOutboundLock.Set();
                if (holdOutboundLockTask != null)
                    await VerificationCompletion.WaitAsync(
                        holdOutboundLockTask,
                        "Outbound lock holder did not exit.");
            }
        }

        [Fact]
        public void Stop_DoesNotWaitForBlockedWorkerThreadsDuringShutdown()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            using BlockingBackgroundThreadHarness keepSyncWorker = BlockingBackgroundThreadHarness.Start("ReadboardKeepSyncWorker");
            using BlockingBackgroundThreadHarness continuousSyncWorker = BlockingBackgroundThreadHarness.Start("ReadboardContinuousSyncWorker");
            SetField(coordinator, "keepSyncThread", keepSyncWorker.Thread);
            SetField(coordinator, "continuousSyncThread", continuousSyncWorker.Thread);

            coordinator.Stop();

            Assert.True(keepSyncWorker.Thread.IsAlive);
            Assert.True(continuousSyncWorker.Thread.IsAlive);
        }

        [Fact]
        public void TryCaptureSnapshot_ReturnsFalseWhenHostCancelsSnapshotCapture()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            object host = CreateProxy(hostInterfaceType, (method, args) =>
            {
                if (method.Name == "CaptureSnapshot")
                    throw new SnapshotCaptureCancelledException();
                return GetDefault(method.ReturnType);
            });
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);

            MethodInfo methodInfo = coordinator.GetType().GetMethod("TryCaptureSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(methodInfo != null, "Missing coordinator method: TryCaptureSnapshot");
            object[] methodArgs = new object[] { runtime, null };

            bool captured = (bool)methodInfo.Invoke(coordinator, methodArgs);

            Assert.False(captured);
            Assert.Null(methodArgs[1]);
        }

        [Fact]
        public void TryStartKeepSync_CaptureCancellationRevokesExistingAutoPlayAuthorization()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SendPlay(
                "black",
                AutoPlayColorMode.FoxAuto,
                "0",
                "0",
                "0");
            AttachCaptureCancellingRuntime(coordinator);

            Assert.False((bool)Invoke(coordinator, "TryStartKeepSync"));

            Assert.Equal(
                new[] { "play>black>0 0 0", "stopAutoPlay" },
                transport.SentLines);
        }

        [Fact]
        public void TryStartContinuousSync_CaptureCancellationRevokesExistingAutoPlayAuthorization()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SendPlay(
                "black",
                AutoPlayColorMode.FoxAuto,
                "0",
                "0",
                "0");
            AttachCaptureCancellingRuntime(coordinator);

            Assert.True((bool)Invoke(coordinator, "TryStartContinuousSync"));
            try
            {
                Assert.True(transport.WaitForLine("stopAutoPlay"));
            }
            finally
            {
                Invoke(coordinator, "StopSyncSession");
            }

            Assert.Equal(1, transport.CountLines("stopAutoPlay"));
        }

        [Fact]
        public void TryStartKeepSync_CaptureCancellationPreservesManualAutoPlayAuthorization()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SendPlay(
                "black",
                AutoPlayColorMode.ManualBlack,
                "0",
                "0",
                "0");
            AttachCaptureCancellingRuntime(coordinator);

            Assert.False((bool)Invoke(coordinator, "TryStartKeepSync"));

            Assert.Equal(
                new[] { "play>black>0 0 0" },
                transport.SentLines);
        }

        [Theory]
        [InlineData((int)AutoPlayColorMode.ManualBlack, 0)]
        [InlineData((int)AutoPlayColorMode.ManualWhite, 0)]
        [InlineData((int)AutoPlayColorMode.FoxAuto, 1)]
        public void TryRunOneTimeSync_FoxRecognitionFailureOnlyRevokesFoxAutoAuthorization(
            int authorizedColorModeValue,
            int expectedStopCount)
        {
            AutoPlayColorMode authorizedColorMode = (AutoPlayColorMode)authorizedColorModeValue;
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("fox");
            coordinator.SendPlay("black", authorizedColorMode, "0", "0", "0");
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");
            object snapshot = CreateSnapshot(snapshotType, SyncMode.Fox, new IntPtr(5151));
            SetProperty(snapshot, "PlayColor", "black");
            SetProperty(snapshot, "AutoPlayColorMode", AutoPlayColorMode.FoxAuto);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", CreateProxy(
                hostInterfaceType,
                new HostRecorder(snapshot).HandleCall));
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(
                (BoardRecognitionResult)null));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(
                descriptorInterfaceType,
                new DescriptorFactoryRecorder().HandleCall));
            Invoke(coordinator, "AttachRuntime", runtime);

            Assert.False((bool)Invoke(coordinator, "TryRunOneTimeSync"));

            Assert.Equal(expectedStopCount, transport.CountLines("stopAutoPlay"));
        }

        [Fact]
        public void TryRunOneTimeSync_ResetsReplayAndOverlayCachesBeforeEachRun()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Background, new IntPtr(5151));
            SetProperty(snapshot, "ShowInBoard", true);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            DescriptorFactoryRecorder descriptorFactoryRecorder = new DescriptorFactoryRecorder();
            object descriptorFactory = CreateProxy(descriptorInterfaceType, descriptorFactoryRecorder.HandleCall);
            FixedOverlayService overlayService = new FixedOverlayService("overlay-visible");
            BoardRecognitionResult recognitionResult = CreateResult("re=background");
            recognitionResult.Snapshot.FoxMoveNumber = 57;
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(recognitionResult));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", overlayService);
            SetProperty(runtime, "WindowDescriptorFactory", descriptorFactory);

            Invoke(coordinator, "AttachRuntime", runtime);

            Assert.True((bool)Invoke(coordinator, "TryRunOneTimeSync"));
            Assert.True((bool)Invoke(coordinator, "TryRunOneTimeSync"));

            Assert.Equal(2, overlayService.ResetCount);
            Assert.Equal(2, hostRecorder.SyncCachesResetCount);
            Assert.Equal(
                new[]
                {
                    "overlay-visible",
                    "start 19 19",
                    "syncPlatform generic",
                    "foxMoveNumber 57",
                    "lastMoveSource none",
                    "re=background",
                    "end",
                    "overlay-visible",
                    "start 19 19",
                    "syncPlatform generic",
                    "foxMoveNumber 57",
                    "lastMoveSource none",
                    "re=background",
                    "end"
                },
                transport.SentLines);
        }

        [Fact]
        public void TryRunOneTimeSync_YikePlatformSendsStartAndStopCommands()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("yike");
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Yike, new IntPtr(5151));
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=yike")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(
                descriptorInterfaceType,
                new DescriptorFactoryRecorder().HandleCall));

            Invoke(coordinator, "AttachRuntime", runtime);

            coordinator.Start();
            try
            {
                Assert.True((bool)Invoke(coordinator, "TryRunOneTimeSync"));
            }
            finally
            {
                coordinator.Stop();
            }

            int yikeStartIndex = transport.SentLines.IndexOf("yikeSyncStart");
            int startIndex = transport.SentLines.IndexOf("start 19 19 5151");
            int endIndex = transport.SentLines.LastIndexOf("end");
            int yikeStopIndex = transport.SentLines.LastIndexOf("yikeSyncStop");
            Assert.True(yikeStartIndex >= 0, "One-time yike sync should emit yikeSyncStart.");
            Assert.True(startIndex > yikeStartIndex, "yikeSyncStart should be sent before one-time snapshot frame.");
            Assert.True(yikeStopIndex > endIndex, "One-time yike sync should emit yikeSyncStop after snapshot frame.");
        }

        [Fact]
        public void TryRunOneTimeSync_YikeFailureStillSendsStopCommand()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            coordinator.SetSyncPlatform("yike");
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");
            Type descriptorInterfaceType = RequireType(assembly, "readboard.IWindowDescriptorFactory");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Yike, IntPtr.Zero);
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=yike")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            SetProperty(runtime, "WindowDescriptorFactory", CreateProxy(
                descriptorInterfaceType,
                new DescriptorFactoryRecorder().HandleCall));

            Invoke(coordinator, "AttachRuntime", runtime);

            coordinator.Start();
            try
            {
                Assert.False((bool)Invoke(coordinator, "TryRunOneTimeSync"));
            }
            finally
            {
                coordinator.Stop();
            }

            int yikeStartIndex = transport.SentLines.IndexOf("yikeSyncStart");
            int yikeStopIndex = transport.SentLines.LastIndexOf("yikeSyncStop");
            Assert.True(yikeStartIndex >= 0, "Failed one-time yike sync should still emit yikeSyncStart before validation.");
            Assert.True(yikeStopIndex > yikeStartIndex, "Failed one-time yike sync should still emit yikeSyncStop.");
        }

        [Fact]
        public void SendClear_NotifiesHostWhenSyncCachesReset()
        {
            RecordingTransport transport = new RecordingTransport();
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            Type snapshotType = RequireType(assembly, "readboard.SyncCoordinatorHostSnapshot");

            object snapshot = CreateSnapshot(snapshotType, SyncMode.Yike, new IntPtr(5151));
            HostRecorder hostRecorder = new HostRecorder(snapshot);
            object host = CreateProxy(hostInterfaceType, hostRecorder.HandleCall);
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=yike")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);

            coordinator.SendClear();

            Assert.Equal(1, hostRecorder.SyncCachesResetCount);
            Assert.Equal(new[] { "clear" }, transport.SentLines);
        }

        [Fact]
        public void ActiveKeepSyncCacheReset_ReusesActiveObservationGeneration()
        {
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(
                new RecordingTransport(),
                new LegacyProtocolAdapter());
            ObservationGenerationHost host = new ObservationGenerationHost();
            coordinator.AttachRuntime(new SyncSessionRuntimeDependencies
            {
                Host = host,
                CaptureService = new SequencedCaptureService(CreateFrame()),
                RecognitionService = new SequencedRecognitionService(CreateResult("re=foreground")),
                PlacementService = new PassivePlacementService(),
                OverlayService = new PassiveOverlayService()
            });
            coordinator.BeginKeepSync();
            SetField(coordinator, "activeKeepObservationGeneration", 41L);

            try
            {
                coordinator.ResetSyncCaches();
            }
            finally
            {
                coordinator.EndKeepSync();
            }

            Assert.Equal(41L, host.LastSyncCachesResetGeneration);
            Assert.Equal(0, host.AllocationCount);
        }

        [Fact]
        public void AreaChangeCacheReset_DoesNotReportRuntimeFrameCleared()
        {
            SyncSessionCoordinator coordinator = new SyncSessionCoordinator(
                new RecordingTransport(),
                new LegacyProtocolAdapter());
            WebViewHostRecorder host = new WebViewHostRecorder();
            SyncSessionRuntimeDependencies runtime = new SyncSessionRuntimeDependencies { Host = host };
            SetRuntimeBoardPixelDimensions(coordinator, 200, 200);

            Type sampleType = typeof(SyncSessionCoordinator).GetNestedType(
                "RecognizedSyncSample",
                BindingFlags.NonPublic);
            Assert.NotNull(sampleType);
            object sample = Activator.CreateInstance(
                sampleType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { 100, CreateFrame(), null },
                null);

            Invoke(
                coordinator,
                "BuildRecognizedSampleProtocolDispatch",
                new SyncCoordinatorHostSnapshot { BoardWidth = 19, BoardHeight = 19 },
                sample,
                false,
                0);

            Assert.Equal(0, host.RuntimeFrameClearedCount);

            Invoke(coordinator, "ClearRuntimeFrame", runtime, 0L);

            Assert.Equal(1, host.RuntimeFrameClearedCount);
        }

        private static object CreateProxy(Type interfaceType, Func<MethodInfo, object[], object> handler)
        {
            MethodInfo createMethod = null;
            MethodInfo[] methods = typeof(DispatchProxy).GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != "Create" || !candidate.IsGenericMethodDefinition)
                    continue;
                if (candidate.GetParameters().Length != 0)
                    continue;
                createMethod = candidate.MakeGenericMethod(interfaceType, typeof(ReflectionProxy));
                break;
            }
            Assert.True(createMethod != null, "DispatchProxy.Create<T,TProxy>() is required for orchestration tests.");
            object proxy = createMethod.Invoke(null, null);
            ((ReflectionProxy)proxy).Handler = handler;
            return proxy;
        }

        private static object CreateSnapshot(Type snapshotType, SyncMode syncMode, IntPtr handle)
        {
            object snapshot = Activator.CreateInstance(snapshotType);
            SetProperty(snapshot, "SyncMode", syncMode);
            SetProperty(snapshot, "BoardWidth", 19);
            SetProperty(snapshot, "BoardHeight", 19);
            SetProperty(snapshot, "SelectionBounds", new PixelRect(10, 20, 190, 190));
            SetProperty(snapshot, "SelectedWindowHandle", handle);
            SetProperty(snapshot, "DpiScale", 1f);
            SetProperty(snapshot, "LegacyTypeToken", "0");
            SetProperty(snapshot, "ShowInBoard", false);
            SetProperty(snapshot, "SupportsForegroundFoxInBoardProtocol", false);
            SetProperty(snapshot, "AutoMinimize", false);
            SetProperty(snapshot, "SampleIntervalMs", 5);
            return snapshot;
        }

        private static void AttachCaptureCancellingRuntime(SyncSessionCoordinator coordinator)
        {
            Assembly assembly = typeof(SyncSessionCoordinator).Assembly;
            Type runtimeType = RequireType(assembly, "readboard.SyncSessionRuntimeDependencies");
            Type hostInterfaceType = RequireType(assembly, "readboard.ISyncCoordinatorHost");
            object host = CreateProxy(hostInterfaceType, (method, args) =>
            {
                if (method.Name == "CaptureSnapshot")
                    throw new SnapshotCaptureCancelledException();
                return GetDefault(method.ReturnType);
            });
            object runtime = Activator.CreateInstance(runtimeType);
            SetProperty(runtime, "Host", host);
            SetProperty(runtime, "CaptureService", new SequencedCaptureService(CreateFrame()));
            SetProperty(runtime, "RecognitionService", new SequencedRecognitionService(CreateResult("re=foreground")));
            SetProperty(runtime, "PlacementService", new PassivePlacementService());
            SetProperty(runtime, "OverlayService", new PassiveOverlayService());
            Invoke(coordinator, "AttachRuntime", runtime);
        }

        private static BoardFrame CreateFrame()
        {
            return new BoardFrame
            {
                SyncMode = SyncMode.Foreground,
                BoardSize = new BoardDimensions(19, 19),
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(0, 0, 190, 190),
                    ScreenBounds = new PixelRect(0, 0, 190, 190),
                    CellWidth = 10d,
                    CellHeight = 10d
                }
            };
        }

        private static BoardRecognitionResult CreateResult(string protocolLine)
        {
            return new BoardRecognitionResult
            {
                Success = true,
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(0, 0, 190, 190),
                    ScreenBounds = new PixelRect(0, 0, 190, 190),
                    CellWidth = 10d,
                    CellHeight = 10d
                },
                Snapshot = new BoardSnapshot
                {
                    Width = 19,
                    Height = 19,
                    IsValid = true,
                    Payload = protocolLine,
                    ProtocolLines = new[] { protocolLine }
                }
            };
        }

        private static BoardRecognitionResult CreateResultWithBoardState(
            string protocolLine,
            params BoardCellState[] boardState)
        {
            BoardRecognitionResult result = CreateResult(protocolLine);
            result.Snapshot.BoardState = boardState;
            result.Snapshot.Width = boardState.Length;
            result.Snapshot.Height = 1;
            return result;
        }

        private static BoardRecognitionResult CreateInvalidResult(string protocolLine)
        {
            BoardRecognitionResult result = CreateResult(protocolLine);
            result.Snapshot.IsValid = false;
            return result;
        }

        private static Type RequireType(Assembly assembly, string typeName)
        {
            Type resolved = assembly.GetType(typeName);
            Assert.True(resolved != null, "Missing runtime contract type: " + typeName);
            return resolved;
        }

        private static object Invoke(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(method != null, "Missing coordinator method: " + methodName);
            return method.Invoke(target, args);
        }

        private static void SetProperty(object target, string propertyName, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(property != null, "Missing property: " + propertyName);
            property.SetValue(target, value, null);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(field != null, "Missing field: " + fieldName);
            field.SetValue(target, value);
        }

        private static void SetRuntimeBoardPixelWidth(SyncSessionCoordinator coordinator, int boardPixelWidth)
        {
            FieldInfo runtimeStateField = typeof(SyncSessionCoordinator).GetField("runtimeState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(runtimeStateField != null, "Missing coordinator field: runtimeState");
            object runtimeState = runtimeStateField.GetValue(coordinator);
            Assert.NotNull(runtimeState);
            SetProperty(runtimeState, "CurrentBoardPixelWidth", boardPixelWidth);
        }

        private static void SetRuntimeBoardPixelDimensions(
            SyncSessionCoordinator coordinator,
            int boardPixelWidth,
            int boardPixelHeight)
        {
            FieldInfo runtimeStateField = typeof(SyncSessionCoordinator).GetField("runtimeState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(runtimeStateField != null, "Missing coordinator field: runtimeState");
            object runtimeState = runtimeStateField.GetValue(coordinator);
            Assert.NotNull(runtimeState);
            SetProperty(runtimeState, "CurrentBoardPixelWidth", boardPixelWidth);
            SetProperty(runtimeState, "CurrentBoardPixelHeight", boardPixelHeight);
        }

        private static object GetOutboundProtocolDispatcher(SyncSessionCoordinator coordinator)
        {
            FieldInfo dispatcherField = typeof(SyncSessionCoordinator).GetField(
                "outboundProtocolDispatcher",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(dispatcherField != null, "Missing coordinator field: outboundProtocolDispatcher");
            object dispatcher = dispatcherField.GetValue(coordinator);
            Assert.NotNull(dispatcher);
            return dispatcher;
        }


        private static Thread ReadWorkerThread(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(field != null, "Missing worker thread field: " + fieldName);
            return (Thread)field.GetValue(target);
        }

        private static Task StartDedicatedThread(Action action, string name)
        {
            return StartDedicatedThread<object>(delegate
            {
                action();
                return null;
            }, name);
        }

        private static Task<T> StartDedicatedThread<T>(Func<T> action, string name)
        {
            TaskCompletionSource<T> completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Thread thread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            }));
            thread.IsBackground = true;
            thread.Name = name;
            thread.Start();
            return completion.Task;
        }

        private static void AssertCompletes(Task task, string message)
        {
            Assert.True(task.Wait(VerificationCompletion.WatchdogTimeout), message);
            task.GetAwaiter().GetResult();
        }



        private class ReflectionProxy : DispatchProxy
        {
            public Func<MethodInfo, object[], object> Handler { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                return Handler(targetMethod, args);
            }
        }

        private sealed class HostRecorder
        {
            private readonly object snapshot;

            public HostRecorder(object snapshot)
            {
                this.snapshot = snapshot;
            }

            public ManualResetEventSlim KeepStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim KeepStopped { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ContinuousStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ContinuousStopped { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim SyncCachesReset { get; } = new ManualResetEventSlim(false);
            public int SnapshotRequests { get; private set; }
            public IntPtr LastSelectedWindow { get; private set; }
            public int KeepStartedCount { get; private set; }
            public int KeepStoppedCount { get; private set; }
            public int ContinuousStartedCount { get; private set; }
            public int ContinuousStoppedCount { get; private set; }
            public int SyncCachesResetCount { get; private set; }
            public int RecognizedSnapshotCount { get; private set; }
            public BoardSnapshot LastRecognizedSnapshot { get; private set; }

            public object HandleCall(MethodInfo method, object[] args)
            {
                switch (method.Name)
                {
                    case "CaptureSnapshot":
                        SnapshotRequests++;
                        return snapshot;
                    case "UpdateSelectedWindowHandle":
                        LastSelectedWindow = (IntPtr)args[0];
                        SetProperty(snapshot, "SelectedWindowHandle", LastSelectedWindow);
                        return null;
                    case "OnKeepSyncStarted":
                        KeepStartedCount++;
                        KeepStarted.Set();
                        return null;
                    case "OnKeepSyncStopped":
                        KeepStoppedCount++;
                        KeepStopped.Set();
                        return null;
                    case "OnContinuousSyncStarted":
                        ContinuousStartedCount++;
                        ContinuousStarted.Set();
                        return null;
                    case "OnContinuousSyncStopped":
                        ContinuousStoppedCount++;
                        ContinuousStopped.Set();
                        return null;
                    case "OnSyncCachesReset":
                        SyncCachesResetCount++;
                        SyncCachesReset.Set();
                        return null;
                    case "OnBoardSnapshotRecognized":
                        RecognizedSnapshotCount++;
                        LastRecognizedSnapshot = (BoardSnapshot)args[0];
                        return null;
                    case "ShowMissingSyncSourceMessage":
                    case "ShowRecognitionFailureMessage":
                    case "MinimizeWindow":
                        return null;
                    default:
                        return GetDefault(method.ReturnType);
                }
            }
        }

        private sealed class WebViewHostRecorder : ISyncCoordinatorHost, IWebViewSyncCoordinatorHost
        {
            public int RuntimeFrameClearedCount { get; private set; }

            public SyncCoordinatorHostSnapshot CaptureSnapshot() { return new SyncCoordinatorHostSnapshot(); }
            public long AllocateSessionObservationGeneration() { return 0; }
            public void UpdateSelectedWindowHandle(IntPtr handle, long observationGeneration) { }
            public void OnKeepSyncStarted(long observationGeneration) { }
            public void OnKeepSyncStopped(bool continuousSyncActive, long observationGeneration) { }
            public void OnContinuousSyncStarted(long observationGeneration) { }
            public void OnContinuousSyncStopped(long observationGeneration) { }
            public void OnSyncCachesReset(long observationGeneration) { }
            public void OnBoardSnapshotRecognized(
                BoardSnapshot snapshot,
                TimeSpan duration,
                long observationGeneration) { }
            public void OnBoardSnapshotSent(BoardSnapshot snapshot, long observationGeneration) { }
            public void ShowMissingSyncSourceMessage() { }
            public void ShowRecognitionFailureMessage() { }
            public void MinimizeWindow() { }
            public bool TrySendPlaceProtocolError(string message) { return false; }

            public void OnRuntimeFrameCleared(long observationGeneration)
            {
                RuntimeFrameClearedCount++;
            }

            public void OnBoardFrameRecognized(
                BoardFrame frame,
                int boardPixelWidth,
                int boardPixelHeight,
                bool placementRegionResolved,
                long observationGeneration)
            {
            }
        }

        private sealed class ObservationGenerationHost : ISyncCoordinatorHost
        {
            public int AllocationCount { get; private set; }
            public long LastSyncCachesResetGeneration { get; private set; }

            public SyncCoordinatorHostSnapshot CaptureSnapshot()
            {
                return new SyncCoordinatorHostSnapshot();
            }

            public long AllocateSessionObservationGeneration()
            {
                AllocationCount++;
                return 100L + AllocationCount;
            }

            public void UpdateSelectedWindowHandle(IntPtr handle, long observationGeneration) { }
            public void OnKeepSyncStarted(long observationGeneration) { }
            public void OnKeepSyncStopped(bool continuousSyncActive, long observationGeneration) { }
            public void OnContinuousSyncStarted(long observationGeneration) { }
            public void OnContinuousSyncStopped(long observationGeneration) { }

            public void OnSyncCachesReset(long observationGeneration)
            {
                LastSyncCachesResetGeneration = observationGeneration;
            }

            public void OnBoardSnapshotRecognized(
                BoardSnapshot snapshot,
                TimeSpan duration,
                long observationGeneration) { }

            public void ShowMissingSyncSourceMessage() { }
            public void ShowRecognitionFailureMessage() { }
            public void MinimizeWindow() { }
            public bool TrySendPlaceProtocolError(string message) { return false; }
        }

        private sealed class AutoPlayToggleHostRecorder
        {
            private readonly Type snapshotType;
            private readonly AutoPlayMoveMode moveMode;
            private int autoPlayEnabled = 1;
            private int disabledSnapshotCaptured;
            private int postReenableSnapshotCount;

            public AutoPlayToggleHostRecorder(Type snapshotType, AutoPlayMoveMode moveMode)
            {
                this.snapshotType = snapshotType;
                this.moveMode = moveMode;
            }

            public ManualResetEventSlim KeepStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim KeepStopped { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim DisabledSnapshotCaptured { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim PostReenableSamplesSettled { get; } = new ManualResetEventSlim(false);

            public void SetAutoPlayEnabled(bool enabled)
            {
                Interlocked.Exchange(ref autoPlayEnabled, enabled ? 1 : 0);
            }

            public object HandleCall(MethodInfo method, object[] args)
            {
                switch (method.Name)
                {
                    case "CaptureSnapshot":
                        bool enabled = Volatile.Read(ref autoPlayEnabled) != 0;
                        object snapshot = CreateSnapshot(snapshotType, SyncMode.Foreground, IntPtr.Zero);
                        SetProperty(snapshot, "PlayColor", enabled ? "black" : null);
                        SetProperty(snapshot, "AutoPlayMoveMode", moveMode);
                        if (!enabled)
                        {
                            Interlocked.Exchange(ref disabledSnapshotCaptured, 1);
                            DisabledSnapshotCaptured.Set();
                        }
                        else if (Volatile.Read(ref disabledSnapshotCaptured) != 0
                            && Interlocked.Increment(ref postReenableSnapshotCount) == 3)
                        {
                            PostReenableSamplesSettled.Set();
                        }
                        return snapshot;
                    case "OnKeepSyncStarted":
                        KeepStarted.Set();
                        return null;
                    case "OnKeepSyncStopped":
                        KeepStopped.Set();
                        return null;
                    case "UpdateSelectedWindowHandle":
                    case "OnSyncCachesReset":
                    case "ShowMissingSyncSourceMessage":
                    case "ShowRecognitionFailureMessage":
                    case "MinimizeWindow":
                        return null;
                    default:
                        return GetDefault(method.ReturnType);
                }
            }
        }

        private sealed class FoxLiveContextSequenceHostRecorder
        {
            private readonly object snapshot;
            private readonly SyncSessionCoordinator coordinator;
            private readonly FoxWindowContext[] contexts;
            private int snapshotRequests;

            public FoxLiveContextSequenceHostRecorder(
                object snapshot,
                SyncSessionCoordinator coordinator,
                params FoxWindowContext[] contexts)
            {
                this.snapshot = snapshot;
                this.coordinator = coordinator;
                Assert.True(contexts != null && contexts.Length > 0);
                this.contexts = contexts;
            }

            public ManualResetEventSlim KeepStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim KeepStopped { get; } = new ManualResetEventSlim(false);

            public object HandleCall(MethodInfo method, object[] args)
            {
                switch (method.Name)
                {
                    case "CaptureSnapshot":
                        int contextIndex = Math.Min(
                            Interlocked.Increment(ref snapshotRequests) - 1,
                            contexts.Length - 1);
                        coordinator.SetFoxWindowContext(contexts[contextIndex]);
                        return snapshot;
                    case "OnKeepSyncStarted":
                        KeepStarted.Set();
                        return null;
                    case "OnKeepSyncStopped":
                        KeepStopped.Set();
                        return null;
                    case "UpdateSelectedWindowHandle":
                    case "OnSyncCachesReset":
                    case "ShowMissingSyncSourceMessage":
                    case "ShowRecognitionFailureMessage":
                    case "MinimizeWindow":
                        return null;
                    default:
                        return GetDefault(method.ReturnType);
                }
            }
        }

        private sealed class FoxRoomAuthorizationSequenceHostRecorder
        {
            private readonly object snapshot;
            private readonly SyncSessionCoordinator coordinator;
            private readonly FoxWindowContext[] contexts;
            private int snapshotRequests;

            public FoxRoomAuthorizationSequenceHostRecorder(
                object snapshot,
                SyncSessionCoordinator coordinator,
                params FoxWindowContext[] contexts)
            {
                this.snapshot = snapshot;
                this.coordinator = coordinator;
                Assert.True(contexts != null && contexts.Length > 0);
                this.contexts = contexts;
            }

            public ManualResetEventSlim KeepStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim KeepStopped { get; } = new ManualResetEventSlim(false);

            public object HandleCall(MethodInfo method, object[] args)
            {
                switch (method.Name)
                {
                    case "CaptureSnapshot":
                        int snapshotRequest = Interlocked.Increment(ref snapshotRequests);
                        int contextIndex = Math.Min(
                            snapshotRequest - 1,
                            contexts.Length - 1);
                        coordinator.SetFoxWindowContext(contexts[contextIndex]);
                        SetProperty(snapshot, "PlayColor", contextIndex == 0 ? "black" : null);
                        return snapshot;
                    case "OnKeepSyncStarted":
                        KeepStarted.Set();
                        return null;
                    case "OnKeepSyncStopped":
                        KeepStopped.Set();
                        return null;
                    case "UpdateSelectedWindowHandle":
                    case "OnSyncCachesReset":
                    case "ShowMissingSyncSourceMessage":
                    case "ShowRecognitionFailureMessage":
                    case "MinimizeWindow":
                        return null;
                    default:
                        return GetDefault(method.ReturnType);
                }
            }
        }

        private static FoxWindowContext CreateFoxLiveRoomContext(string roomToken, int liveTitleMove)
        {
            return new FoxWindowContext
            {
                Kind = FoxWindowKind.LiveRoom,
                LiveRoomState = FoxLiveRoomState.Playing,
                RoomToken = roomToken,
                LiveTitleMove = liveTitleMove
            };
        }

        private static FoxWindowContext CreateFoxRecordViewContext(
            int recordCurrentMove,
            int recordTotalMove,
            bool recordAtEnd,
            string titleFingerprint)
        {
            return new FoxWindowContext
            {
                Kind = FoxWindowKind.RecordView,
                RecordCurrentMove = recordCurrentMove,
                RecordTotalMove = recordTotalMove,
                RecordAtEnd = recordAtEnd,
                TitleFingerprint = titleFingerprint
            };
        }

        private sealed class LightweightBindingRestartHostRecorder
        {
            private readonly object snapshot;
            private readonly SyncSessionCoordinator coordinator;
            private bool queuedInitialMove;

            public LightweightBindingRestartHostRecorder(object snapshot, SyncSessionCoordinator coordinator)
            {
                this.snapshot = snapshot;
                this.coordinator = coordinator;
            }

            public ManualResetEventSlim KeepStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim KeepStopped { get; } = new ManualResetEventSlim(false);
            public int KeepStartedCount { get; private set; }
            public int KeepStoppedCount { get; private set; }
            public bool InitialMoveQueued { get; private set; }

            public object HandleCall(MethodInfo method, object[] args)
            {
                switch (method.Name)
                {
                    case "CaptureSnapshot":
                        return snapshot;
                    case "UpdateSelectedWindowHandle":
                        SetProperty(snapshot, "SelectedWindowHandle", (IntPtr)args[0]);
                        return null;
                    case "OnKeepSyncStarted":
                        KeepStartedCount++;
                        if (!queuedInitialMove)
                        {
                            queuedInitialMove = true;
                            InitialMoveQueued = coordinator.TryQueuePendingMove(
                                new MoveRequest { X = 1, Y = 1, VerifyMove = false },
                                190,
                                19);
                        }
                        KeepStarted.Set();
                        return null;
                    case "OnKeepSyncStopped":
                        KeepStoppedCount++;
                        KeepStopped.Set();
                        return null;
                    case "OnContinuousSyncStarted":
                    case "OnContinuousSyncStopped":
                    case "ShowMissingSyncSourceMessage":
                    case "ShowRecognitionFailureMessage":
                    case "MinimizeWindow":
                        return null;
                    default:
                        return GetDefault(method.ReturnType);
                }
            }
        }

        private sealed class BlockingContinuousSnapshotHostRecorder
        {
            private readonly object snapshot;
            private readonly ManualResetEventSlim releaseEvent = new ManualResetEventSlim(false);
            private int blockedThreadId;
            private bool blockedWorkerSeen;

            public BlockingContinuousSnapshotHostRecorder(object snapshot)
            {
                this.snapshot = snapshot;
            }

            public ManualResetEventSlim BlockedContinuousSnapshotStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ContinuousStarted { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim ContinuousStopped { get; } = new ManualResetEventSlim(false);
            public int ContinuousStartedCount { get; private set; }
            public int ContinuousStoppedCount { get; private set; }

            public object HandleCall(MethodInfo method, object[] args)
            {
                switch (method.Name)
                {
                    case "CaptureSnapshot":
                        return HandleCaptureSnapshot();
                    case "OnContinuousSyncStarted":
                        ContinuousStartedCount++;
                        ContinuousStarted.Set();
                        return null;
                    case "OnContinuousSyncStopped":
                        ContinuousStoppedCount++;
                        ContinuousStopped.Set();
                        return null;
                    case "OnKeepSyncStarted":
                    case "OnKeepSyncStopped":
                    case "UpdateSelectedWindowHandle":
                    case "ShowMissingSyncSourceMessage":
                    case "ShowRecognitionFailureMessage":
                    case "MinimizeWindow":
                        return null;
                    default:
                        return GetDefault(method.ReturnType);
                }
            }

            public void ReleaseBlockedContinuousSnapshot()
            {
                releaseEvent.Set();
            }

            private object HandleCaptureSnapshot()
            {
                if (IsContinuousWorkerThread())
                {
                    int currentThreadId = Thread.CurrentThread.ManagedThreadId;
                    if (!blockedWorkerSeen)
                    {
                        blockedWorkerSeen = true;
                        blockedThreadId = currentThreadId;
                        BlockedContinuousSnapshotStarted.Set();
                        releaseEvent.Wait();
                        throw new SnapshotCaptureCancelledException();
                    }

                    if (currentThreadId == blockedThreadId)
                        throw new SnapshotCaptureCancelledException();
                }

                return snapshot;
            }

            private static bool IsContinuousWorkerThread()
            {
                Thread currentThread = Thread.CurrentThread;
                return currentThread != null
                    && string.Equals(currentThread.Name, "ReadboardContinuousSyncWorker", StringComparison.Ordinal);
            }
        }

        private sealed class WindowLocatorRecorder
        {
            private readonly IntPtr handle;

            public WindowLocatorRecorder(IntPtr handle)
            {
                this.handle = handle;
            }

            public int Calls { get; private set; }

            public object HandleCall(MethodInfo method, object[] args)
            {
                Calls++;
                return handle;
            }
        }

        private sealed class DescriptorFactoryRecorder
        {
            public object HandleCall(MethodInfo method, object[] args)
            {
                args[1] = new WindowDescriptor
                {
                    Handle = (IntPtr)args[0],
                    Bounds = new PixelRect(100, 200, 190, 190),
                    ClassName = "FoxBoard",
                    Title = "Fox",
                    IsDpiAware = true,
                    DpiScale = 1d
                };
                return true;
            }
        }

        private sealed class RecordingTransport : IReadBoardTransport
        {
            private readonly ManualResetEventSlim lineEvent = new ManualResetEventSlim(false);

            public event EventHandler<string> MessageReceived;

            public List<string> SentLines { get; } = new List<string>();

            public bool IsConnected { get; private set; }

            public void Dispose()
            {
            }

            public void Emit(string rawLine)
            {
                MessageReceived?.Invoke(this, rawLine);
            }

            public void Send(string line)
            {
                lock (SentLines)
                {
                    SentLines.Add(line);
                    lineEvent.Set();
                }
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

            public bool WaitForLines(int expectedCount)
            {
                while (true)
                {
                    lock (SentLines)
                    {
                        if (SentLines.Count >= expectedCount)
                            return true;
                    }

                    VerificationCompletion.Wait(
                        lineEvent,
                        "Expected transport lines were not sent.");
                    lineEvent.Reset();
                }
            }
            public bool WaitForLine(string expectedLine)
            {
                while (true)
                {
                    lock (SentLines)
                    {
                        for (int index = 0; index < SentLines.Count; index++)
                        {
                            if (string.Equals(SentLines[index], expectedLine, StringComparison.Ordinal))
                                return true;
                        }
                    }

                    VerificationCompletion.Wait(
                        lineEvent,
                        "Expected transport line was not sent: " + expectedLine);
                    lineEvent.Reset();
                }
            }


            public int CountLines(string line)
            {
                int count = 0;
                lock (SentLines)
                {
                    for (int index = 0; index < SentLines.Count; index++)
                    {
                        if (string.Equals(SentLines[index], line, StringComparison.Ordinal))
                            count++;
                    }
                }
                return count;
            }
        }

        private sealed class BlockingLineTransport : IReadBoardTransport
        {
            private readonly string blockedLine;
            private readonly ManualResetEventSlim releaseEvent = new ManualResetEventSlim(false);

            public BlockingLineTransport(string blockedLine)
            {
                this.blockedLine = blockedLine;
            }

            public event EventHandler<string> MessageReceived
            {
                add { }
                remove { }
            }

            public ManualResetEventSlim BlockedSendStarted { get; } = new ManualResetEventSlim(false);
            public List<string> SentLines { get; } = new List<string>();
            public bool IsConnected { get; private set; }

            public void Dispose()
            {
            }

            public void Send(string line)
            {
                lock (SentLines)
                {
                    SentLines.Add(line);
                }
                if (!string.Equals(line, blockedLine, StringComparison.Ordinal))
                    return;

                BlockedSendStarted.Set();
                releaseEvent.Wait();
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

            public int CountLines(string line)
            {
                lock (SentLines)
                {
                    int count = 0;
                    for (int index = 0; index < SentLines.Count; index++)
                    {
                        if (string.Equals(SentLines[index], line, StringComparison.Ordinal))
                            count++;
                    }
                    return count;
                }
            }

            public void Release()
            {
                releaseEvent.Set();
            }
        }

        private sealed class SequencedCaptureService : IBoardCaptureService
        {
            private readonly BoardFrame frame;

            public SequencedCaptureService(BoardFrame frame)
            {
                this.frame = frame;
            }

            public BoardCaptureResult Capture(BoardCaptureRequest request)
            {
                return new BoardCaptureResult
                {
                    Success = true,
                    Frame = frame
                };
            }
        }

        private sealed class BlockingCaptureService : IBoardCaptureService
        {
            private readonly BoardFrame frame;
            private int captureCount;
            private readonly ManualResetEventSlim releaseEvent = new ManualResetEventSlim(false);

            public BlockingCaptureService(BoardFrame frame)
            {
                this.frame = frame;
            }

            public ManualResetEventSlim BlockedCaptureStarted { get; } = new ManualResetEventSlim(false);

            public BoardCaptureResult Capture(BoardCaptureRequest request)
            {
                if (Interlocked.Increment(ref captureCount) > 1)
                {
                    BlockedCaptureStarted.Set();
                    releaseEvent.Wait();
                }

                return new BoardCaptureResult
                {
                    Success = true,
                    Frame = frame
                };
            }

            public void Release()
            {
                releaseEvent.Set();
            }
        }

        private sealed class ScriptedBlockingCaptureService : IBoardCaptureService
        {
            private readonly BoardFrame frame;
            private readonly int blockedCallNumber;
            private readonly bool failAfterRelease;
            private readonly ManualResetEventSlim releaseEvent = new ManualResetEventSlim(false);
            private int captureCount;

            public ScriptedBlockingCaptureService(BoardFrame frame, int blockedCallNumber, bool failAfterRelease)
            {
                this.frame = frame;
                this.blockedCallNumber = blockedCallNumber;
                this.failAfterRelease = failAfterRelease;
            }

            public ManualResetEventSlim BlockedCaptureStarted { get; } = new ManualResetEventSlim(false);

            public BoardCaptureResult Capture(BoardCaptureRequest request)
            {
                int callNumber = Interlocked.Increment(ref captureCount);
                if (callNumber == blockedCallNumber)
                {
                    BlockedCaptureStarted.Set();
                    releaseEvent.Wait();
                    if (failAfterRelease)
                        return new BoardCaptureResult { Success = false, FailureReason = "blocked capture aborted" };
                }

                return new BoardCaptureResult
                {
                    Success = true,
                    Frame = frame
                };
            }

            public void Release()
            {
                releaseEvent.Set();
            }
        }

        private sealed class SequencedRecognitionService : IBoardRecognitionService
        {
            private readonly BoardRecognitionResult[] results;
            private int resultIndex;

            public SequencedRecognitionService(params BoardRecognitionResult[] results)
            {
                Assert.True(results != null && results.Length > 0);
                this.results = results;
            }

            public BoardRecognitionResult Recognize(BoardRecognitionRequest request)
            {
                int index = Math.Min(Interlocked.Increment(ref resultIndex) - 1, results.Length - 1);
                return results[index];
            }
        }

        private sealed class ScriptedBlockingRecognitionService : IBoardRecognitionService
        {
            private readonly BoardRecognitionResult result;
            private readonly int blockedCallNumber;
            private readonly ManualResetEventSlim releaseEvent = new ManualResetEventSlim(false);
            private int callCount;

            public ScriptedBlockingRecognitionService(BoardRecognitionResult result, int blockedCallNumber)
            {
                this.result = result;
                this.blockedCallNumber = blockedCallNumber;
            }

            public ManualResetEventSlim BlockedRecognizeStarted { get; } = new ManualResetEventSlim(false);
            private ManualResetEventSlim CallCountChanged { get; } = new ManualResetEventSlim(false);

            public int CallCount
            {
                get { return Volatile.Read(ref callCount); }
            }

            public void WaitForCallCount(int minimumCallCount)
            {
                while (CallCount < minimumCallCount)
                {
                    VerificationCompletion.Wait(CallCountChanged, "Recognition call count did not advance.");
                    CallCountChanged.Reset();
                }
            }

            public BoardRecognitionResult Recognize(BoardRecognitionRequest request)
            {
                int currentCall = Interlocked.Increment(ref callCount);
                CallCountChanged.Set();
                if (currentCall == blockedCallNumber)
                {
                    BlockedRecognizeStarted.Set();
                    releaseEvent.Wait();
                }

                return result;
            }

            public void Release()
            {
                releaseEvent.Set();
            }
        }

        private sealed class PassivePlacementService : IMovePlacementService
        {
            public bool CanResolvePlacementRegion(BoardFrame frame)
            {
                return false;
            }

            public MovePlacementResult Place(MovePlacementRequest request)
            {
                return new MovePlacementResult { Success = true };
            }
        }

        private sealed class ThrowingPlacementService : IMovePlacementService
        {
            public ManualResetEventSlim PlaceCalled { get; } = new ManualResetEventSlim(false);

            public bool CanResolvePlacementRegion(BoardFrame frame)
            {
                return false;
            }

            public MovePlacementResult Place(MovePlacementRequest request)
            {
                PlaceCalled.Set();
                throw new InvalidOperationException("placement failed unexpectedly");
            }
        }

        private sealed class SingleLightweightPlacementService : IMovePlacementService
        {
            public ManualResetEventSlim PlaceCalled { get; } = new ManualResetEventSlim(false);
            public int PlaceCallCount { get; private set; }

            public bool CanResolvePlacementRegion(BoardFrame frame)
            {
                return false;
            }

            public MovePlacementResult Place(MovePlacementRequest request)
            {
                PlaceCallCount++;
                PlaceCalled.Set();
                if (PlaceCallCount == 1)
                {
                    return new MovePlacementResult
                    {
                        Success = true,
                        PlacementPath = PlacementPathKind.Foreground
                    };
                }

                return new MovePlacementResult { Success = true };
            }
        }

        private sealed class ReflectiveCancellationAwareBlockingPlacementService : IMovePlacementService
        {
            private readonly ManualResetEventSlim releaseEvent = new ManualResetEventSlim(false);

            public ManualResetEventSlim BlockedPlacementStarted { get; } = new ManualResetEventSlim(false);
            public int PlaceCallCount { get; private set; }
            public int ActualPlacementCount { get; private set; }

            public bool CanResolvePlacementRegion(BoardFrame frame)
            {
                return false;
            }

            public MovePlacementResult Place(MovePlacementRequest request)
            {
                PlaceCallCount++;
                BlockedPlacementStarted.Set();
                releaseEvent.Wait();
                if (ShouldCancel(request))
                    return new MovePlacementResult { Success = false, FailureReason = "cancelled" };
                ActualPlacementCount++;
                return new MovePlacementResult { Success = true };
            }

            public void Release()
            {
                releaseEvent.Set();
            }

            private static bool ShouldCancel(MovePlacementRequest request)
            {
                PropertyInfo property = typeof(MovePlacementRequest).GetProperty("ShouldCancel");
                if (property == null)
                    return false;
                Func<bool> shouldCancel = property.GetValue(request, null) as Func<bool>;
                return shouldCancel != null && shouldCancel();
            }
        }

        private sealed class SideEffectThenBlockingPlacementService : IMovePlacementService
        {
            private readonly ManualResetEventSlim releaseEvent = new ManualResetEventSlim(false);

            public ManualResetEventSlim SideEffectApplied { get; } = new ManualResetEventSlim(false);

            public bool CanResolvePlacementRegion(BoardFrame frame)
            {
                return false;
            }

            public MovePlacementResult Place(MovePlacementRequest request)
            {
                SideEffectApplied.Set();
                releaseEvent.Wait();
                return new MovePlacementResult { Success = true };
            }

            public void Release()
            {
                releaseEvent.Set();
            }
        }

        private sealed class PassiveOverlayService : IOverlayService
        {
            public OverlayUpdateResult BuildUpdate(OverlayUpdateRequest request)
            {
                return new OverlayUpdateResult();
            }

            public void Reset()
            {
            }
        }

        private sealed class FixedOverlayService : IOverlayService
        {
            private readonly string protocolLine;

            public FixedOverlayService(string protocolLine)
            {
                this.protocolLine = protocolLine;
            }

            public int ResetCount { get; private set; }

            public OverlayUpdateResult BuildUpdate(OverlayUpdateRequest request)
            {
                return new OverlayUpdateResult { ProtocolLine = protocolLine };
            }

            public void Reset()
            {
                ResetCount++;
            }
        }

        private static object GetDefault(Type returnType)
        {
            if (returnType == typeof(void))
                return null;
            if (!returnType.IsValueType)
                return null;
            return Activator.CreateInstance(returnType);
        }
    }
}
