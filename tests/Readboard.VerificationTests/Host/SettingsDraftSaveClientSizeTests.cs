using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class SettingsDraftSaveClientSizeTests
    {
        [Fact]
        public void SavingLanguageOnlyLeavesClientSizeUnchangedAndAppliesLanguage()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            using (Form window = CreateClientWindow())
            {
                Size originalClientSize = window.ClientSize;
                MainForm.MainFormSettingsDraftRuntimeEffects effects =
                    CreateProductionShapedEffects(window, persistence.Events);
                SettingsDraftRuntime runtime = CreateRuntime(active, persistence, effects);

                runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.Language, "jp"));
                SettingsDraftOperationResult result = runtime.Save();

                Assert.Equal(SettingsDraftOperationOutcome.Saved, result.Outcome);
                Assert.Equal(originalClientSize, window.ClientSize);
                Assert.Equal(new[] { "persist", "replace", "language:jp", "title" }, persistence.Events);
                Assert.Equal("jp", persistence.Active.LanguagePreference);
                Assert.Equal("jp", result.State.Language);
                Assert.Equal(
                    "jp",
                    MainForm.WebViewSettingsStateProjector.Project(
                        result.State,
                        delegate(string key) { return key; },
                        delegate(string key) { return key; }).Language);
            }
        }

        [Fact]
        public void SavingThemeOnlyLeavesClientSizeUnchangedAndAppliesProcessColorMode()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            using (Form window = CreateClientWindow())
            {
                Size originalClientSize = window.ClientSize;
                MainForm.MainFormSettingsDraftRuntimeEffects effects =
                    CreateProductionShapedEffects(window, persistence.Events);
                SettingsDraftRuntime runtime = CreateRuntime(active, persistence, effects);

                runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.Theme, "dark"));
                SettingsDraftOperationResult result = runtime.Save();

                Assert.Equal(SettingsDraftOperationOutcome.Saved, result.Outcome);
                Assert.Equal(originalClientSize, window.ClientSize);
                Assert.Equal(new[] { "persist", "replace", "color:" + AppConfig.ColorModeDark }, persistence.Events);
                Assert.Equal(AppConfig.ColorModeDark, persistence.Active.ColorMode);
                Assert.Equal("dark", result.State.Theme);
                Assert.Equal(SystemColorMode.Dark, Program.GetSystemColorMode(AppConfig.ColorModeDark));
                Assert.Equal(SystemColorMode.Classic, Program.GetSystemColorMode(AppConfig.ColorModeLight));
                Assert.Equal(SystemColorMode.System, Program.GetSystemColorMode(AppConfig.ColorModeSystem));
            }
        }

        [Fact]
        public void SavingBackgroundAnalysisNotifiesHostWithoutCoveredCaption()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            using (Form window = CreateClientWindow())
            {
                Size originalClientSize = window.ClientSize;
                MainForm.MainFormSettingsDraftRuntimeEffects effects =
                    CreateProductionShapedEffects(window, persistence.Events);
                SettingsDraftRuntime runtime = CreateRuntime(active, persistence, effects);

                runtime.Update(SettingsDraftUpdate.Boolean(SettingsDraftField.BackgroundAnalysis, false));
                SettingsDraftOperationResult result = runtime.Save();

                Assert.Equal(SettingsDraftOperationOutcome.Saved, result.Outcome);
                Assert.Equal(originalClientSize, window.ClientSize);
                Assert.Equal(new[] { "persist", "replace", "ponder:False" }, persistence.Events);
                Assert.DoesNotContain("caption", persistence.Events);
                Assert.False(result.State.BackgroundAnalysis);
            }
        }

        [Fact]
        public void ProductionSettingsAndWindowPathsDoNotArrangeCoveredMainForm()
        {
            string settingsSource = LoadSource("readboard", "MainForm.WebView.Settings.cs");
            string formSource = LoadSource("readboard", "Form1.cs");
            string ensureSlice = GetMethodSlice(settingsSource, "private SettingsDraftRuntime EnsureWebViewSettingsDraft()");
            string ctorSlice = GetMethodSlice(formSource, "internal MainForm(");
            string handleCreatedSlice = GetMethodSlice(formSource, "protected override void OnHandleCreated(EventArgs e)");
            string dpiChangedSlice = GetMethodSlice(formSource, "protected override void OnDpiChanged(DpiChangedEventArgs e)");

            Assert.DoesNotContain("ApplyMainFormUi", ensureSlice);
            Assert.DoesNotContain("resetBtnKeepSyncName", ensureSlice);
            Assert.Contains("Program.ApplyColorMode", ensureSlice);
            Assert.Contains("SendPonderStatus", ensureSlice);
            Assert.DoesNotContain("ApplyMainFormUi();", ctorSlice);
            Assert.Contains("InitializeWebViewShell();", ctorSlice);
            Assert.DoesNotContain("ApplyMainFormUi();", handleCreatedSlice);
            Assert.DoesNotContain("ApplyMainFormUi();", dpiChangedSlice);
            Assert.Contains("UpdateWebViewMinimumSizeForCurrentDpi();", dpiChangedSlice);
        }

        private static Form CreateClientWindow()
        {
            return new Form
            {
                ClientSize = new Size(1100, 680)
            };
        }
        private static MainForm.MainFormSettingsDraftRuntimeEffects CreateProductionShapedEffects(
            Form window,
            IList<string> events)
        {
            return new MainForm.MainFormSettingsDraftRuntimeEffects(
                delegate(string preference)
                {
                    Size before = window.ClientSize;
                    events.Add("language:" + preference);
                    Assert.Equal(before, window.ClientSize);
                },
                delegate
                {
                    Size before = window.ClientSize;
                    events.Add("title");
                    Assert.Equal(before, window.ClientSize);
                },
                delegate(int colorMode)
                {
                    Size before = window.ClientSize;
                    events.Add("color:" + colorMode);
                    Program.ApplyColorMode(colorMode);
                    Assert.Equal(before, window.ClientSize);
                },
                delegate(bool enabled)
                {
                    Size before = window.ClientSize;
                    events.Add("ponder:" + enabled);
                    Assert.Equal(before, window.ClientSize);
                });
        }

        private static SettingsDraftRuntime CreateRuntime(
            AppConfig active,
            RecordingPersistence persistence,
            ISettingsDraftRuntimeEffects effects)
        {
            return new SettingsDraftRuntime(
                active,
                () => AppConfig.CreateDefault("220430", "TEST"),
                persistence,
                effects);
        }

        private static string LoadSource(params string[] segments)
        {
            return File.ReadAllText(Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments)));
        }

        private static string GetMethodSlice(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing signature: " + signature);
            int braceStart = source.IndexOf('{', start);
            int depth = 0;
            for (int index = braceStart; index < source.Length; index++)
            {
                if (source[index] == '{')
                    depth++;
                else if (source[index] == '}')
                    depth--;

                if (depth == 0)
                    return source.Substring(start, index - start + 1);
            }

            throw new InvalidOperationException("Could not slice method: " + signature);
        }

        private sealed class RecordingPersistence : ISettingsDraftPersistence
        {
            public RecordingPersistence(AppConfig active)
            {
                Active = active.Clone();
                Events = new List<string>();
            }

            public AppConfig Active { get; private set; }
            public IList<string> Events { get; private set; }

            public AppConfig GetLatestActiveConfig()
            {
                return Active.Clone();
            }

            public void Persist(AppConfig candidate)
            {
                Events.Add("persist");
                Active = candidate.Clone();
            }

            public void ReplaceActiveConfig(AppConfig candidate)
            {
                Events.Add("replace");
                Active = candidate.Clone();
            }
        }
    }
}
