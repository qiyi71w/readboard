using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class MainWindowTitleSourceRegressionTests
    {
        [Fact]
        public void MainForm_CentralizesFoxTitleFormatting()
        {
            string source = LoadSource("readboard", "Form1.cs");

            Assert.Contains("private FoxWindowContext lastFoxWindowContext = FoxWindowContext.Unknown();", source);
            Assert.Contains("private FoxWindowBinding foxWindowBinding = null;", source);
            Assert.Contains("private bool hasRetainedFoxTitleSnapshot = false;", source);
            Assert.Contains("private void UpdateMainWindowTitle(FoxWindowContext foxWindowContext)", source);
            Assert.Contains("private void RefreshMainWindowTitleFromCurrentWindow()", source);
            Assert.Contains("private void ResetMainWindowTitle()", source);
            Assert.Contains("private MainWindowTitleDisplayMode ResolveMainWindowTitleDisplayMode()", source);
            Assert.Contains("MainWindowTitleFormatter.Format(", source);
            Assert.Contains(
                "UpdateMainWindowTitle(foxWindowContext);",
                GetMethodSlice(source, "private SyncCoordinatorHostSnapshot CaptureSnapshotCore()"));
        }

        [Fact]
        public void MainForm_UsesBindingCacheInsteadOfHeavyFoxTitleResolutionInsideSnapshotCapture()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string resolveSlice = GetMethodSlice(source, "private FoxWindowContext ResolveFoxWindowContext()");
            string applyTitleSlice = GetMethodSlice(source, "private void ApplyMainWindowTitle()");

            Assert.Contains("TryRefreshFoxWindowContextFromBinding(out foxWindowContext)", resolveSlice);
            Assert.Contains("TryResolveFoxWindowBinding(out foxWindowContext)", resolveSlice);
            Assert.DoesNotContain("FoxWindowContextResolver.Resolve(", resolveSlice);
            Assert.DoesNotContain("FoxWindowDescriptorFactory", resolveSlice);
            Assert.Contains("string title = MainWindowTitleFormatter.Format(", applyTitleSlice);
            Assert.Contains("ApplyMainWindowTitleText(title);", applyTitleSlice);
            Assert.Contains(
                "if (string.Equals(lastAppliedMainWindowTitle, title, StringComparison.Ordinal))",
                GetMethodSlice(source, "private void ApplyMainWindowTitleText(string title)"));
            Assert.Contains(
                "lastAppliedMainWindowTitle = title;",
                GetMethodSlice(source, "private void ApplyMainWindowTitleText(string title)"));
        }

        [Fact]
        public void MainForm_ManagesRetainedTitleSnapshotAcrossWindowChangesAndForceRebuild()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string updateHandleSlice = GetMethodSlice(source, "void ISyncCoordinatorHost.UpdateSelectedWindowHandle(IntPtr handle)");
            string forceRebuildSlice = GetMethodSlice(source, "private void btnForceRebuild_Click(object sender, EventArgs e)");

            Assert.Contains(
                "hasRetainedFoxTitleSnapshot = false;",
                updateHandleSlice);
            Assert.Contains(
                "lastFoxWindowContext = FoxWindowContext.Unknown();",
                updateHandleSlice);
            Assert.Contains(
                "InvalidateFoxWindowBinding();",
                updateHandleSlice);
            Assert.Contains(
                "RefreshMainWindowTitleFromCurrentWindow();",
                GetMethodSlice(source, "private void ApplyKeepSyncStartedUi()"));
            Assert.Contains(
                "RefreshMainWindowTitleFromCurrentWindow();",
                GetMethodSlice(source, "private void ApplyContinuousSyncStartedUi()"));
            Assert.Contains(
                "if (HasActiveSyncOperation())",
                forceRebuildSlice);
            Assert.Contains(
                "InvalidateFoxWindowBinding();",
                forceRebuildSlice);
            Assert.Contains(
                "RefreshMainWindowTitleFromCurrentWindow();",
                forceRebuildSlice);
            Assert.Contains(
                "ResetMainWindowTitle();",
                GetMethodSlice(source, "private void ApplyKeepSyncStoppedUi(bool continuousSyncActive)"));
            Assert.Contains(
                "ResetMainWindowTitle();",
                GetMethodSlice(source, "public void shutdown(bool persistConfiguration)"));
        }

        [Fact]
        public void MainForm_OneTimeSyncRetainsOnlySuccessfulFoxSnapshots()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string oneTimeSyncSlice = GetMethodSlice(source, "private void oneTimeSync()");

            Assert.Contains("hasRetainedFoxTitleSnapshot = false;", oneTimeSyncSlice);
            Assert.Contains("sessionCoordinator.TryRunOneTimeSync();", oneTimeSyncSlice);
            Assert.Contains("ResetMainWindowTitle();", oneTimeSyncSlice);
            Assert.Contains("if (IsFoxSyncType(CurrentSyncType))", oneTimeSyncSlice);
            Assert.Contains("hasRetainedFoxTitleSnapshot = true;", oneTimeSyncSlice);
            Assert.Contains("ApplyMainWindowTitle();", oneTimeSyncSlice);
        }

        [Fact]
        public void ProgramAndLanguageFiles_DefineFoxTitleStatusTagsAndMoveFormats()
        {
            string programSource = LoadSource("readboard", "Program.cs");
            string cnSource = LoadSource("readboard", "language_cn.txt");
            string enSource = LoadSource("readboard", "language_en.txt");
            string jpSource = LoadSource("readboard", "language_jp.txt");
            string krSource = LoadSource("readboard", "language_kr.txt");

            string[] keys =
            {
                "MainForm_titleTagFox",
                "MainForm_titleTagRoom",
                "MainForm_titleTagRecord",
                "MainForm_titleTagSyncing",
                "MainForm_titleTagTitleMissing",
                "MainForm_titleTagRecordEnd",
                "MainForm_titleMoveFormatSingle",
                "MainForm_titleMoveFormatRecord"
            };

            for (int i = 0; i < keys.Length; i++)
            {
                Assert.Contains(keys[i], programSource);
                Assert.Contains(keys[i] + "=", cnSource);
                Assert.Contains(keys[i] + "=", enSource);
                Assert.Contains(keys[i] + "=", jpSource);
                Assert.Contains(keys[i] + "=", krSource);
            }
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path);
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
