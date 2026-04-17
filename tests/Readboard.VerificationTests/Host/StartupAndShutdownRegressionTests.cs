using System;
using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class StartupAndShutdownRegressionTests
    {
        [Fact]
        public void MainFormRuntimeComposer_BindsSessionStateBeforeCreatingMainForm()
        {
            string source = LoadSource("readboard", "Core", "Protocol", "MainFormRuntimeComposer.cs");

            int bindIndex = IndexOfRequired(source, "coordinator.BindSessionState(sessionState);");
            int createHostIndex = IndexOfRequired(source, "MainForm host = new MainForm(");

            Assert.True(bindIndex < createHostIndex, "SessionState must be bound before MainForm applies loaded configuration.");
        }

        [Fact]
        public void Program_ReplaysStartupProtocolStateAfterReadyHandshake()
        {
            string source = LoadSource("readboard", "Program.cs");

            int readyIndex = IndexOfRequired(source, "mainForm.NotifyProtocolReady();");
            int replayIndex = IndexOfRequired(source, "mainForm.ReplayStartupProtocolState();");

            Assert.True(replayIndex > readyIndex, "Startup protocol replay should happen after the ready/playponder handshake.");
        }

        [Theory]
        [InlineData("private void textbox1_TextChanged(object sender, EventArgs e)")]
        [InlineData("private void textBox2_TextChanged(object sender, EventArgs e)")]
        [InlineData("private void textBox3_TextChanged(object sender, EventArgs e)")]
        [InlineData("private void checkBox1_CheckedChanged_1(object sender, EventArgs e)")]
        public void InitializationSensitiveHandlers_GuardProtocolSideEffects(string methodSignature)
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, methodSignature);

            Assert.Contains("if (isInitializingProtocolState)", methodSlice);
        }

        [Theory]
        [InlineData("private void radioButton5_CheckedChanged(object sender, EventArgs e)")]
        [InlineData("private void radioButton6_CheckedChanged(object sender, EventArgs e)")]
        [InlineData("private void radioButton7_CheckedChanged(object sender, EventArgs e)")]
        [InlineData("private void radioButton8_CheckedChanged(object sender, EventArgs e)")]
        public void StartupBoardSelectionHandlers_GuardPersistenceDuringInitialization(string methodSignature)
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, methodSignature);

            Assert.Contains("if (isInitializingProtocolState)", methodSlice);
        }

        [Fact]
        public void ApplyLoadedConfiguration_ReappliesShowInBoardConstraintsAfterRestoringConfig()
        {
            string source = LoadSource("readboard", "MainForm.Configuration.cs");

            int restoreIndex = IndexOfRequired(source, "chkShowInBoard.Checked = Program.showInBoard;");
            int normalizeIndex = source.LastIndexOf("ApplySyncModeControlState();", StringComparison.Ordinal);

            Assert.True(normalizeIndex > restoreIndex, "Loaded show-in-board state must be normalized after restoring the saved checkbox value.");
        }

        [Fact]
        public void ShowInBoardToggle_RejectsManualSelectionSyncModes()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void chkShowInBoard_CheckedChanged(object sender, EventArgs e)");

            Assert.Contains("UsesManualSelectionType(CurrentSyncType)", methodSlice);
            Assert.Contains("chkShowInBoard.Checked = false;", methodSlice);
        }

        [Fact]
        public void ShowInBoardHotkey_OnlyTogglesWhenCurrentModeSupportsIt()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void HookListener_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)");

            Assert.Contains("SupportsShowInBoard()", methodSlice);
        }

        [Fact]
        public void ReplayStartupProtocolState_SkipsBlankNumericOverrides()
        {
            string source = LoadSource("readboard", "MainForm.Protocol.cs");
            string methodSlice = GetMethodSlice(source, "public void ReplayStartupProtocolState()");

            Assert.Contains("if (!string.IsNullOrWhiteSpace(textBox1.Text))", methodSlice);
            Assert.Contains("if (!string.IsNullOrWhiteSpace(textBox2.Text))", methodSlice);
            Assert.Contains("if (!string.IsNullOrWhiteSpace(textBox3.Text))", methodSlice);
        }

        [Fact]
        public void ReplayStartupProtocolState_ReusesSyncBothAwareInBoardStateChange()
        {
            string source = LoadSource("readboard", "MainForm.Protocol.cs");
            string methodSlice = GetMethodSlice(source, "public void ReplayStartupProtocolState()");

            Assert.Contains("SendBothSyncStateChange();", methodSlice);
            Assert.DoesNotContain("SendCurrentForegroundFoxInBoardCommand();", methodSlice);
        }

        [Fact]
        public void SettingsReset_UsesShutdownWithoutPersistingCurrentWindowState()
        {
            string source = LoadSource("readboard", "Form4.cs");

            Assert.Contains("GetHost().shutdown(false);", source);
            Assert.DoesNotContain("GetHost().shutdown();", source);
        }

        [Fact]
        public void MainForm_ShutdownSupportsSkippingPersistence()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "public void shutdown(bool persistConfiguration)");

            Assert.Contains("if (persistConfiguration)", methodSlice);
        }

        [Fact]
        public void MainForm_ShutdownDefersCloseUntilHandleExists()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "public void shutdown(bool persistConfiguration)");

            Assert.Contains("if (!IsHandleCreated)", methodSlice);
            Assert.Contains("closeRequestedBeforeHandle = true;", methodSlice);
        }

        [Fact]
        public void MainForm_OnHandleCreated_ClosesPendingStartupShutdown()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "protected override void OnHandleCreated(EventArgs e)");

            Assert.Contains("if (!closeRequestedBeforeHandle || IsDisposed)", methodSlice);
            Assert.Contains("BeginInvoke((Action)Close);", methodSlice);
        }

        [Fact]
        public void MainForm_ShutdownStopsAndDisposesGlobalHooks()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string shutdownSlice = GetMethodSlice(source, "public void shutdown(bool persistConfiguration)");
            string helperSlice = GetMethodSlice(source, "private void DisposeInputHooks()");

            Assert.Contains("DisposeInputHooks();", shutdownSlice);
            Assert.Contains("hookListener.KeyDown -= HookListener_KeyDown;", helperSlice);
            Assert.Contains("hookListener.KeyUp -= HookListener_KeyUp;", helperSlice);
            Assert.Contains("hookListener.Stop();", helperSlice);
            Assert.Contains("hookListener.Dispose();", helperSlice);
            Assert.Contains("mh.MouseMove -= mh_MouseMoveEvent;", helperSlice);
            Assert.Contains("mh.MouseClick -= mh_MouseMoveEvent2;", helperSlice);
            Assert.Contains("mh.Enabled = false;", helperSlice);
            Assert.Contains("mh.Stop();", helperSlice);
            Assert.Contains("mh.Dispose();", helperSlice);
        }

        [Fact]
        public void MainForm_ShutdownUsesCoordinatorStopInsteadOfBlockingStopSyncSession()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string shutdownSlice = GetMethodSlice(source, "public void shutdown(bool persistConfiguration)");

            Assert.DoesNotContain("sessionCoordinator.StopSyncSession();", shutdownSlice);
            Assert.Contains("sessionCoordinator.Stop();", shutdownSlice);
        }

        [Fact]
        public void SelectionMagnifier_DoesNotUseSelectionOverlayAsShowOwner()
        {
            string source = LoadSource("readboard", "Form2.cs");

            Assert.Contains("form5.Show();", source);
            Assert.DoesNotContain("form5.Show(this);", source);
        }

        [Fact]
        public void Program_StopsStartupHandshakeAfterShutdownRequest()
        {
            string source = LoadSource("readboard", "Program.cs");

            Assert.Equal(3, CountOccurrences(source, "if (mainForm.IsShutdownRequested)"));
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path);
        }

        private static int IndexOfRequired(string source, string value)
        {
            int index = source.IndexOf(value, StringComparison.Ordinal);
            Assert.True(index >= 0, "Expected to find source fragment: " + value);
            return index;
        }

        private static string GetMethodSlice(string source, string methodSignature)
        {
            int startIndex = IndexOfRequired(source, methodSignature);
            int nextMethodIndex = source.IndexOf("\n        private ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            int publicMethodIndex = source.IndexOf("\n        public ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            if (publicMethodIndex >= 0 && (nextMethodIndex < 0 || publicMethodIndex < nextMethodIndex))
                nextMethodIndex = publicMethodIndex;
            if (nextMethodIndex < 0)
                nextMethodIndex = source.Length;
            return source.Substring(startIndex, nextMethodIndex - startIndex);
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }
    }
}
