using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            Assert.True(hostRecorder.KeepStarted.Wait(TimeSpan.FromSeconds(1)));
            Assert.True(transport.WaitForLines(6, TimeSpan.FromSeconds(1)));

            Invoke(coordinator, "StopSyncSession");

            Assert.True(hostRecorder.KeepStopped.Wait(TimeSpan.FromSeconds(1)));
            Assert.Equal(
                new[]
                {
                    "notForeFoxWithInBoard",
                    "sync",
                    "start 19 19",
                    "syncPlatform generic",
                    "re=foreground",
                    "end"
                },
                transport.SentLines.GetRange(0, 6));
            Assert.True(hostRecorder.SnapshotRequests >= 2);
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
            Assert.True(hostRecorder.ContinuousStarted.Wait(TimeSpan.FromSeconds(1)));
            Assert.True(transport.WaitForLines(6, TimeSpan.FromSeconds(1)));

            Invoke(coordinator, "StopSyncSession");

            Assert.True(hostRecorder.ContinuousStopped.Wait(TimeSpan.FromSeconds(1)));
            Assert.True(locatorRecorder.Calls > 0);
            Assert.Equal(new IntPtr(4242), hostRecorder.LastSelectedWindow);
            Assert.Equal(
                new[]
                {
                    "notForeFoxWithInBoard",
                    "sync",
                    "start 19 19 4242",
                    "syncPlatform generic",
                    "re=fox",
                    "end"
                },
                transport.SentLines.GetRange(0, 6));
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
            Assert.True(hostRecorder.KeepStarted.Wait(TimeSpan.FromSeconds(1)));

            Thread worker = WaitForWorkerThread(coordinator, "keepSyncThread");
            Assert.True(worker.IsBackground);

            Invoke(coordinator, "StopSyncSession");
            Assert.True(hostRecorder.KeepStopped.Wait(TimeSpan.FromSeconds(1)));
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

            Thread worker = WaitForWorkerThread(coordinator, "continuousSyncThread");
            Assert.True(worker.IsBackground);

            Invoke(coordinator, "StopSyncSession");
            Assert.True(hostRecorder.ContinuousStopped.Wait(TimeSpan.FromSeconds(1)));
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
            Thread staleWorker = WaitForWorkerThread(coordinator, "continuousSyncThread");
            Assert.True(hostRecorder.BlockedContinuousSnapshotStarted.Wait(TimeSpan.FromSeconds(1)));

            Invoke(coordinator, "StopSyncSession");

            Assert.True((bool)Invoke(coordinator, "TryStartContinuousSync"));
            Assert.True(WaitForCondition(() => hostRecorder.ContinuousStartedCount == 2, TimeSpan.FromSeconds(1)));

            Thread newWorker = null;
            Assert.True(WaitForCondition(delegate
            {
                Thread candidate = ReadWorkerThread(coordinator, "continuousSyncThread");
                if (candidate == null || candidate == staleWorker)
                    return false;
                newWorker = candidate;
                return true;
            }, TimeSpan.FromSeconds(1)));

            hostRecorder.ReleaseBlockedContinuousSnapshot();

            Assert.True(WaitForCondition(() => !staleWorker.IsAlive, TimeSpan.FromSeconds(1)));
            Assert.Equal(0, hostRecorder.ContinuousStoppedCount);
            Assert.Same(newWorker, ReadWorkerThread(coordinator, "continuousSyncThread"));

            Invoke(coordinator, "StopSyncSession");

            Assert.True(WaitForCondition(() => hostRecorder.ContinuousStoppedCount == 1, TimeSpan.FromSeconds(1)));
            Assert.Equal(1, hostRecorder.ContinuousStoppedCount);
        }

        [Fact]
        public async Task Stop_DoesNotHangWhenKeepSyncWorkerBlocksInCapture()
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
            Assert.True(captureService.BlockedCaptureStarted.Wait(TimeSpan.FromSeconds(1)));

            try
            {
                Task stopTask = Task.Run(() => coordinator.Stop());
                Task finishedTask = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(1)));
                Assert.Same(stopTask, finishedTask);
                await stopTask;
            }
            finally
            {
                captureService.Release();
            }

            Assert.True(hostRecorder.KeepStopped.Wait(TimeSpan.FromSeconds(1)));
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
            Assert.True(captureService.BlockedCaptureStarted.Wait(TimeSpan.FromSeconds(1)));

            Task disposeTask = Task.Run(() => coordinator.Dispose());
            Task completedTask = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromMilliseconds(250)));

            Assert.NotSame(disposeTask, completedTask);
            Assert.False(hostRecorder.KeepStopped.IsSet);

            captureService.Release();

            await disposeTask.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(hostRecorder.KeepStopped.Wait(TimeSpan.FromSeconds(1)));
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
            Assert.True(captureService.BlockedCaptureStarted.Wait(TimeSpan.FromSeconds(1)));

            Task stopTask = Task.Run(() => Invoke(coordinator, "StopSyncSession"));
            Task finishedTask = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromMilliseconds(250)));

            Assert.Same(stopTask, finishedTask);
            Assert.False(hostRecorder.KeepStopped.IsSet);

            captureService.Release();

            await stopTask;
            Assert.True(hostRecorder.KeepStopped.Wait(TimeSpan.FromSeconds(1)));
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
            Assert.True(captureService.BlockedCaptureStarted.Wait(TimeSpan.FromSeconds(1)));

            Invoke(coordinator, "StopSyncSession");
            captureService.Release();

            Assert.True(hostRecorder.ContinuousStopped.Wait(TimeSpan.FromSeconds(1)));
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
            Assert.True(hostRecorder.KeepStarted.Wait(TimeSpan.FromSeconds(1)));
            coordinator.SetSyncBoth(true);
            Assert.True(coordinator.TryQueuePendingMove(new MoveRequest { X = 1, Y = 1, VerifyMove = false }, 190, 19));
            Assert.True(placementService.BlockedPlacementStarted.Wait(TimeSpan.FromSeconds(1)));

            Task stopTask = Task.Run(() => coordinator.Stop());
            Task finishedTask = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(stopTask, finishedTask);
            await stopTask;

            placementService.Release();

            Assert.True(hostRecorder.KeepStopped.Wait(TimeSpan.FromSeconds(1)));
            Assert.Equal(1, placementService.PlaceCallCount);
            Assert.Equal(0, placementService.ActualPlacementCount);
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
            Assert.True(hostRecorder.KeepStarted.Wait(TimeSpan.FromSeconds(1)));
            coordinator.SetSyncBoth(true);
            SetRuntimeBoardPixelWidth(coordinator, 190);

            Task<PlaceRequestExecutionResult> resultTask = Task.Run(delegate
            {
                return coordinator.HandlePlaceRequest(new MoveRequest { X = 1, Y = 1, VerifyMove = false });
            });

            Assert.True(placementService.SideEffectApplied.Wait(TimeSpan.FromSeconds(1)));
            Invoke(coordinator, "StopSyncSession");

            Task completedTask = await Task.WhenAny(resultTask, Task.Delay(TimeSpan.FromMilliseconds(150)));
            Assert.NotSame(resultTask, completedTask);

            placementService.Release();

            PlaceRequestExecutionResult result = await resultTask.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(result.ShouldSendResponse);
            Assert.True(result.Success);
            Assert.True(hostRecorder.KeepStopped.Wait(TimeSpan.FromSeconds(1)));
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
            Assert.True(hostRecorder.KeepStarted.Wait(TimeSpan.FromSeconds(1)));
            coordinator.SetSyncBoth(true);
            SetRuntimeBoardPixelWidth(coordinator, 190);

            Task<PlaceRequestExecutionResult> resultTask = Task.Run(delegate
            {
                return coordinator.HandlePlaceRequest(new MoveRequest { X = 1, Y = 1, VerifyMove = false });
            });

            Assert.True(placementService.SideEffectApplied.Wait(TimeSpan.FromSeconds(1)));
            coordinator.Stop();

            Task completedTask = await Task.WhenAny(resultTask, Task.Delay(TimeSpan.FromMilliseconds(150)));
            Assert.NotSame(resultTask, completedTask);

            placementService.Release();

            PlaceRequestExecutionResult result = await resultTask.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(result.ShouldSendResponse);
            Assert.True(result.Success);
            Assert.True(hostRecorder.KeepStopped.Wait(TimeSpan.FromSeconds(1)));
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
            Assert.True(WaitForCondition(() => hostRecorder.KeepStartedCount == 1, TimeSpan.FromSeconds(1)));
            Thread staleWorker = WaitForWorkerThread(coordinator, "keepSyncThread");
            Assert.True(captureService.BlockedCaptureStarted.Wait(TimeSpan.FromSeconds(1)));

            Invoke(coordinator, "StopSyncSession");
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            Assert.True(WaitForCondition(() => hostRecorder.KeepStartedCount == 2, TimeSpan.FromSeconds(1)));

            captureService.Release();

            Assert.True(WaitForCondition(() => !staleWorker.IsAlive, TimeSpan.FromSeconds(1)));
            Assert.True(coordinator.StartedSync);
            Assert.Equal(0, hostRecorder.KeepStoppedCount);
            Assert.Equal(0, transport.CountLines("stopsync"));

            Invoke(coordinator, "StopSyncSession");

            Assert.True(WaitForCondition(() => hostRecorder.KeepStoppedCount == 1, TimeSpan.FromSeconds(1)));
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
            Assert.True(hostRecorder.KeepStarted.Wait(TimeSpan.FromSeconds(1)));
            Assert.True(hostRecorder.InitialMoveQueued);
            Assert.True(WaitForCondition(() => placementService.PlaceCallCount == 1, TimeSpan.FromSeconds(1)));
            Thread staleWorker = WaitForWorkerThread(coordinator, "keepSyncThread");
            Assert.True(captureService.BlockedCaptureStarted.Wait(TimeSpan.FromSeconds(1)));

            Invoke(coordinator, "StopSyncSession");
            SetProperty(snapshot, "SelectedWindowHandle", secondHandle);
            Assert.True((bool)Invoke(coordinator, "TryStartKeepSync"));
            Assert.True(WaitForCondition(() => hostRecorder.KeepStartedCount == 2, TimeSpan.FromSeconds(1)));

            captureService.Release();

            Assert.True(WaitForCondition(() => !staleWorker.IsAlive, TimeSpan.FromSeconds(1)));
            Assert.True(coordinator.StartedSync);
            Assert.Equal(0, hostRecorder.KeepStoppedCount);

            Invoke(coordinator, "StopSyncSession");

            Assert.True(WaitForCondition(() => hostRecorder.KeepStoppedCount == 1, TimeSpan.FromSeconds(1)));
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
                Assert.True(hostRecorder.KeepStarted.Wait(TimeSpan.FromSeconds(1)));
                Assert.True(recognitionService.BlockedRecognizeStarted.Wait(TimeSpan.FromSeconds(1)));

                holdOutboundLockTask = Task.Run(delegate
                {
                    object dispatcher = GetOutboundProtocolDispatcher(coordinator);
                    Invoke(dispatcher, "ExecuteBatch", (Action)delegate
                    {
                        outboundLockHeld.Set();
                        releaseOutboundLock.Wait();
                    });
                });
                Assert.True(outboundLockHeld.Wait(TimeSpan.FromSeconds(1)));

                recognitionService.Release();
                Assert.True(WaitForCallCountToStabilizeAtLeast(() => recognitionService.CallCount, 2, TimeSpan.FromSeconds(1)));

                Invoke(coordinator, "StopSyncSession");

                releaseOutboundLock.Set();

                Assert.True(hostRecorder.KeepStopped.Wait(TimeSpan.FromSeconds(1)));
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
                    await holdOutboundLockTask.WaitAsync(TimeSpan.FromSeconds(1));
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

            Stopwatch stopwatch = Stopwatch.StartNew();
            coordinator.Stop();
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromMilliseconds(250),
                "Application shutdown should not wait for background sync workers to finish.");
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
            Assert.Equal(
                new[]
                {
                    "overlay-visible",
                    "start 19 19",
                    "syncPlatform generic",
                    "foxMoveNumber 57",
                    "re=background",
                    "end",
                    "overlay-visible",
                    "start 19 19",
                    "syncPlatform generic",
                    "foxMoveNumber 57",
                    "re=background",
                    "end"
                },
                transport.SentLines);
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

        private static Thread WaitForWorkerThread(object target, string fieldName)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(1);
            while (DateTime.UtcNow < deadline)
            {
                Thread worker = ReadWorkerThread(target, fieldName);
                if (worker != null)
                    return worker;
                Thread.Sleep(10);
            }

            Assert.Fail("Expected worker thread field to be populated: " + fieldName);
            return null;
        }

        private static Thread ReadWorkerThread(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(field != null, "Missing worker thread field: " + fieldName);
            return (Thread)field.GetValue(target);
        }

        private static bool WaitForCondition(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                Thread.Sleep(10);
            }

            return condition();
        }

        private static bool WaitForCallCountToStabilizeAtLeast(Func<int> readCallCount, int minimumCallCount, TimeSpan timeout)
        {
            int lastCallCount = -1;
            return WaitForCondition(delegate
            {
                int currentCallCount = readCallCount();
                bool isStable = currentCallCount == lastCallCount;
                lastCallCount = currentCallCount;
                return isStable && currentCallCount >= minimumCallCount;
            }, timeout);
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
            public int SnapshotRequests { get; private set; }
            public IntPtr LastSelectedWindow { get; private set; }
            public int KeepStartedCount { get; private set; }
            public int KeepStoppedCount { get; private set; }
            public int ContinuousStartedCount { get; private set; }
            public int ContinuousStoppedCount { get; private set; }

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
                    case "ShowMissingSyncSourceMessage":
                    case "ShowRecognitionFailureMessage":
                    case "MinimizeWindow":
                        return null;
                    default:
                        return GetDefault(method.ReturnType);
                }
            }
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

            public bool WaitForLines(int expectedCount, TimeSpan timeout)
            {
                DateTime deadline = DateTime.UtcNow.Add(timeout);
                while (DateTime.UtcNow < deadline)
                {
                    lock (SentLines)
                    {
                        if (SentLines.Count >= expectedCount)
                            return true;
                    }

                    lineEvent.Wait(TimeSpan.FromMilliseconds(25));
                    lineEvent.Reset();
                }

                return false;
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
            private readonly BoardRecognitionResult result;

            public SequencedRecognitionService(BoardRecognitionResult result)
            {
                this.result = result;
            }

            public BoardRecognitionResult Recognize(BoardRecognitionRequest request)
            {
                return result;
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

            public int CallCount
            {
                get { return Volatile.Read(ref callCount); }
            }

            public BoardRecognitionResult Recognize(BoardRecognitionRequest request)
            {
                int currentCall = Interlocked.Increment(ref callCount);
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
            public MovePlacementResult Place(MovePlacementRequest request)
            {
                return new MovePlacementResult { Success = true };
            }
        }

        private sealed class SingleLightweightPlacementService : IMovePlacementService
        {
            public int PlaceCallCount { get; private set; }

            public MovePlacementResult Place(MovePlacementRequest request)
            {
                PlaceCallCount++;
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
