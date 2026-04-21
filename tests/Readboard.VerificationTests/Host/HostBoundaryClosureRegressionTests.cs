using System.IO;
using System.Text.RegularExpressions;
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
            string coordinatorSource = LoadSource("readboard", "Core", "Protocol", "ISyncSessionCoordinator.cs");

            bool hasDisposableInterface = Regex.IsMatch(
                coordinatorSource,
                @"interface\s+ISyncSessionCoordinator\s*:\s*[\s\S]*?\bIDisposable\b",
                RegexOptions.Multiline);
            bool hasUsingCoordinator = Regex.IsMatch(
                programSource,
                @"using\s*\(\s*ISyncSessionCoordinator\s+\w+\s*=\s*",
                RegexOptions.Multiline);
            bool resetsCoordinatorReference = Regex.IsMatch(
                programSource,
                @"sessionCoordinator\s*=\s*null\s*;",
                RegexOptions.Multiline);
            bool manuallyStopsCoordinator = Regex.IsMatch(
                programSource,
                @"activeSessionCoordinator\s*\.\s*Stop\s*\(",
                RegexOptions.Multiline);

            Assert.True(
                hasDisposableInterface,
                "ISyncSessionCoordinator should implement IDisposable through its interface declaration.");
            Assert.True(
                hasUsingCoordinator,
                "Program should dispose ISyncSessionCoordinator via a using statement over the interface type.");
            Assert.False(
                manuallyStopsCoordinator,
                "Program should rely on disposal rather than manually stopping the active coordinator.");
            Assert.True(
                resetsCoordinatorReference,
                "Program should clear the sessionCoordinator reference after the using block completes.");
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
