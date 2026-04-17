using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class ThinHostCoordinatorTakeoverTests
    {
        [Fact]
        public void Form1_DoesNotRetainLegacySyncLoopsOrRecognitionPipeline()
        {
            string source = LoadSource("readboard", "Form1.cs");

            Assert.DoesNotContain("private void startContinuous(", source);
            Assert.DoesNotContain("private void startContinuousSync(", source);
            Assert.DoesNotContain("private void OutPutTime(", source);
            Assert.DoesNotContain("private bool recognizeBoard(", source);
            Assert.DoesNotContain("private bool TryPrimeSyncFrame(", source);
            Assert.DoesNotContain("private bool TryCaptureAndRecognizeBoard(", source);
            Assert.DoesNotContain("new Thread(startContinuous)", source);
            Assert.DoesNotContain("new Thread(OutPutTime)", source);
        }

        [Fact]
        public void MainFormProtocol_DoesNotPerformPlacementDirectly()
        {
            string source = LoadSource("readboard", "MainForm.Protocol.cs");

            Assert.DoesNotContain("placeMove(", source);
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path);
        }
    }
}
