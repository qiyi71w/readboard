using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class MainWindowTitleSourceRegressionTests
    {
        [Fact]
        public void MainForm_ManagesRetainedTitleSnapshotAcrossWindowChangesAndForceRebuild()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string updateHandleSlice = GetMethodSlice(source, "void ISyncCoordinatorHost.UpdateSelectedWindowHandle(\n            IntPtr handle,");
            string forceRebuildSlice = GetMethodSlice(source, "private void ArmForceRebuildAction()");

            Assert.Contains("hasRetainedFoxTitleSnapshot = false;", updateHandleSlice);
            Assert.Contains("lastFoxWindowContext = FoxWindowContext.Unknown();", updateHandleSlice);
            Assert.Contains("InvalidateFoxWindowBinding();", updateHandleSlice);
            Assert.Contains("RefreshMainWindowTitleFromCurrentWindow();", GetMethodSlice(source, "private void ApplyKeepSyncStartedUi()"));
            Assert.Contains("RefreshMainWindowTitleFromCurrentWindow();", GetMethodSlice(source, "private void ApplyContinuousSyncStartedUi()"));
            Assert.Contains("if (HasActiveSyncOperation())", forceRebuildSlice);
            Assert.Contains("InvalidateFoxWindowBinding();", forceRebuildSlice);
            Assert.Contains("RefreshMainWindowTitleFromCurrentWindow();", forceRebuildSlice);
            Assert.Contains("ResetMainWindowTitle();", GetMethodSlice(source, "private void ApplyKeepSyncStoppedUi(bool continuousSyncActive)"));
        }

        [Fact]
        public void MainForm_OneTimeSyncRetainsOnlySuccessfulFoxSnapshots()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string oneTimeSyncSlice = GetMethodSlice(source, "private bool TryRunOneTimeSyncAction()");

            Assert.Contains("hasRetainedFoxTitleSnapshot = false;", oneTimeSyncSlice);
            Assert.Contains("sessionCoordinator.TryRunOneTimeSync();", oneTimeSyncSlice);
            Assert.Contains("ResetMainWindowTitle();", oneTimeSyncSlice);
            Assert.Contains("if (IsFoxSyncType(CurrentSyncType))", oneTimeSyncSlice);
            Assert.Contains("hasRetainedFoxTitleSnapshot = true;", oneTimeSyncSlice);
            Assert.Contains("ApplyMainWindowTitle();", oneTimeSyncSlice);
        }

        [Fact]
        public void MainForm_KeepSyncStartPreservesRecognizedTurnIndicator()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string keepStartedSlice = GetMethodSlice(source, "private void ApplyKeepSyncStartedUi()");

            Assert.Contains("if (lastMainWindowTitleTurn == MainWindowTitleTurn.None)", keepStartedSlice);
            Assert.Contains("lastMainWindowTitleTurn = MainWindowTitleTurn.Unknown;", keepStartedSlice);
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }

        private static string GetMethodSlice(string source, string methodSignature)
        {
            int start = source.IndexOf(methodSignature);
            Assert.True(start >= 0, "Missing method: " + methodSignature);

            int braceStart = source.IndexOf('{', start);
            Assert.True(braceStart >= 0, "Missing opening brace for: " + methodSignature);

            int depth = 0;
            for (int i = braceStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new Xunit.Sdk.XunitException("Unbalanced braces for: " + methodSignature);
        }
    }
}
