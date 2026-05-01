using System;

namespace readboard
{
    internal sealed class MainFormRuntimeComposer
    {
        private readonly SessionState sessionState;

        public MainFormRuntimeComposer(SessionState sessionState)
        {
            if (sessionState == null)
                throw new ArgumentNullException("sessionState");

            this.sessionState = sessionState;
        }

        public MainForm Compose(LaunchOptions launchOptions, ISyncSessionCoordinator coordinator)
        {
            if (launchOptions == null)
                throw new ArgumentNullException("launchOptions");
            if (coordinator == null)
                throw new ArgumentNullException("coordinator");

            coordinator.BindSessionState(sessionState);
            MainForm host = new MainForm(
                launchOptions,
                coordinator,
                new LegacySelectionCalibrationService());
            coordinator.AttachHost(host);
            coordinator.AttachRuntime(CreateRuntimeDependencies(host));
            return host;
        }

        private static SyncSessionRuntimeDependencies CreateRuntimeDependencies(ISyncCoordinatorHost host)
        {
            return new SyncSessionRuntimeDependencies
            {
                Host = host,
                CaptureService = new LegacyBoardCaptureService(new Win32BoardCapturePlatform()),
                RecognitionService = new LegacyBoardRecognitionService(),
                PlacementService = LegacyMovePlacementService.CreateDefault(),
                OverlayService = new LegacyOverlayService(),
                DebugDiagnostics = new BoardDebugDiagnosticsWriter(
                    BoardDebugDiagnosticsPaths.GetRootDirectory(AppDomain.CurrentDomain.BaseDirectory),
                    delegate { return Program.CurrentConfig.DebugDiagnosticsEnabled; })
            };
        }
    }
}
