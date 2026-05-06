using System;
using System.IO;
using System.Linq;
using System.Reflection;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class HostBoundaryClosureRegressionTests
    {
        [Fact]
        public void MainForm_DoesNotOwnCoreSyncRuntimeServices()
        {
            string source = LoadSource("readboard", "Form1.cs");

            Assert.DoesNotContain("IBoardCaptureService", source);
            Assert.DoesNotContain("IBoardRecognitionService", source);
            Assert.DoesNotContain("IMovePlacementService", source);
            Assert.DoesNotContain("IOverlayService", source);
            Assert.DoesNotContain("AttachRuntime(", source);
            Assert.DoesNotContain("BindSessionState(", source);
            Assert.Contains("ILegacySelectionCalibrationService", source);
            Assert.Contains("ISyncSessionCoordinator", source);
        }

        [Fact]
        public void MainForm_ReportsCapturedFoxMoveNumberThroughCoordinatorInterface()
        {
            string formSource = LoadSource("readboard", "Form1.cs");
            string coordinatorSource = LoadSource("readboard", "Core", "Protocol", "ISyncSessionCoordinator.cs");

            Assert.Contains("void SetCapturedFoxMoveNumber(int? foxMoveNumber);", coordinatorSource);
            Assert.Contains("sessionCoordinator.SetCapturedFoxMoveNumber(foxMoveNumber);", formSource);
            Assert.DoesNotContain("((SyncSessionCoordinator)sessionCoordinator)", formSource);
        }

        [Fact]
        public void MainForm_ReportsSyncPlatformAndFoxWindowContextThroughCoordinatorInterface()
        {
            string formSource = LoadSource("readboard", "Form1.cs");
            string protocolSource = LoadSource("readboard", "MainForm.Protocol.cs");
            string coordinatorSource = LoadSource("readboard", "Core", "Protocol", "ISyncSessionCoordinator.cs");

            Assert.Contains("void SetSyncPlatform(string platform);", coordinatorSource);
            Assert.Contains("void SetFoxWindowContext(FoxWindowContext context);", coordinatorSource);
            Assert.Contains("void SetYikeContext(YikeWindowContext context);", coordinatorSource);
            Assert.Contains("void ArmForceRebuild();", coordinatorSource);
            Assert.Contains("sessionCoordinator.SetSyncPlatform(syncPlatform);", formSource);
            Assert.Contains("sessionCoordinator.SetFoxWindowContext(foxWindowContext);", formSource);
            Assert.Contains("sessionCoordinator.SetYikeContext(lastYikeWindowContext);", protocolSource);
            Assert.DoesNotContain("sessionCoordinator.SetYikeContext(yikeWindowContext);", formSource);
            Assert.Contains("sessionCoordinator.ArmForceRebuild();", formSource);
            Assert.DoesNotContain("((SyncSessionCoordinator)sessionCoordinator)", formSource);
        }

        [Fact]
        public void Program_DefersMainFormRuntimeCompositionToDedicatedComposer()
        {
            string source = LoadSource("readboard", "Program.cs");

            Assert.DoesNotContain("CreateCaptureService(", source);
            Assert.DoesNotContain("CreateRecognitionService(", source);
            Assert.DoesNotContain("CreatePlacementService(", source);
            Assert.DoesNotContain("CreateOverlayService(", source);
            Assert.Contains("MainFormRuntimeComposer", source);
        }

        [Fact]
        public void Program_DisposesCoordinatorThroughInterfaceContract()
        {
            string programSource = LoadSource("readboard", "Program.cs");
            MethodInfo runMethod = typeof(SessionCoordinatorScope).GetMethod(
                "Run",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.True(
                typeof(ISyncSessionCoordinator).GetInterfaces().Contains(typeof(IDisposable)),
                "ISyncSessionCoordinator should implement IDisposable through its interface contract.");
            Assert.NotNull(runMethod);
            Assert.Equal(typeof(void), runMethod.ReturnType);
            Assert.Equal(
                new[]
                {
                    typeof(ISyncSessionCoordinator),
                    typeof(Action<ISyncSessionCoordinator>),
                    typeof(Action<ISyncSessionCoordinator>)
                },
                runMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
            Assert.True(
                programSource.Contains("SessionCoordinatorScope.Run("),
                "Program should route coordinator lifetime through SessionCoordinatorScope.Run.");
        }

        [Fact]
        public void MainFormRuntimeComposer_OwnsCoordinatorRuntimeBinding()
        {
            string source = LoadSource("readboard", "Core", "Protocol", "MainFormRuntimeComposer.cs");

            Assert.Contains("coordinator.AttachHost(host);", source);
            Assert.Contains("coordinator.AttachRuntime(CreateRuntimeDependencies(host));", source);
            Assert.Contains("coordinator.BindSessionState(sessionState);", source);
            Assert.Contains("new LegacyBoardCaptureService", source);
            Assert.Contains("new LegacyBoardRecognitionService", source);
            Assert.Contains("LegacyMovePlacementService.CreateDefault()", source);
            Assert.Contains("new LegacyOverlayService()", source);
            Assert.Contains("new BoardDebugDiagnosticsWriter(", source);
            Assert.Contains("BoardDebugDiagnosticsPaths.GetRootDirectory(AppDomain.CurrentDomain.BaseDirectory)", source);
            Assert.Contains("Program.CurrentConfig.DebugDiagnosticsEnabled", source);
        }

        [Fact]
        public void SyncSessionCoordinator_RecordsDebugDiagnosticsForCaptureAndRecognition()
        {
            string dependenciesSource = LoadSource("readboard", "Core", "Protocol", "SyncSessionRuntimeDependencies.cs");
            string orchestrationSource = LoadSource("readboard", "Core", "Protocol", "SyncSessionCoordinator.Orchestration.cs");
            string coordinatorSource = LoadSource("readboard", "Core", "Protocol", "SyncSessionCoordinator.cs");

            Assert.Contains("public BoardDebugDiagnosticsWriter DebugDiagnostics { get; set; }", dependenciesSource);
            Assert.Contains("runtime.DebugDiagnostics.RecordCaptureFailure(", orchestrationSource);
            Assert.Contains("runtime.DebugDiagnostics.RecordRecognitionFailure(", orchestrationSource);
            Assert.Contains("runtime.DebugDiagnostics.RecordRecognitionSuccess(", orchestrationSource);
            Assert.Contains("DisposeRuntimeDependencies();", coordinatorSource);
            Assert.Contains("runtime.DebugDiagnostics.Dispose();", coordinatorSource);
        }

        [Fact]
        public void SyncSessionCoordinator_BuildsRecognizedSampleDispatchBeforeSendingProtocol()
        {
            string orchestrationSource = LoadSource("readboard", "Core", "Protocol", "SyncSessionCoordinator.Orchestration.cs");

            Assert.Contains("BuildRecognizedSampleProtocolDispatch(", orchestrationSource);
            Assert.Contains("DispatchRecognizedSampleProtocol(dispatch, isOperationCurrent);", orchestrationSource);
            Assert.DoesNotContain("ProcessRecognizedSample(snapshot, sample, firstSample);", orchestrationSource);
        }

        [Fact]
        public void MainForm_SelectBoard_ShowsSelectionOverlayWithMainFormOwner()
        {
            string source = LoadSource("readboard", "Form1.cs");

            Assert.Contains("form2.ShowDialog(this);", source);
            Assert.DoesNotContain("form2.ShowDialog();", source);
        }

        [Fact]
        public void Form2_MouseUp_ClosesSafelyWhenMainFormHostHasBeenDisposed()
        {
            string source = LoadSource("readboard", "Form2.cs");

            Assert.Contains("MainForm mainForm = TryGetHost();", source);
            Assert.Contains("if (mainForm == null)", source);
            Assert.DoesNotContain("MainForm mainForm = GetHost();", source);
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path);
        }
    }
}
