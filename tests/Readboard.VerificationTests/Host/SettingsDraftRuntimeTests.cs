using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class SettingsDraftRuntimeTests
    {
        [Fact]
        public void DraftRuntimeRemainsIndependentFromTypedNavigationIntents()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, new RecordingEffects());
            SettingsDraftRuntime draft = runtime;
            string[] pages = { "controlCenter", "settings", "rules", "about", "settings" };

            for (int i = 0; i < pages.Length; i++)
            {
                ReadBoardUiCommand command;
                Assert.True(MainForm.TryParseWebViewCommand(
                    "{\"type\":\"navigate\",\"payload\":{\"page\":\"" + pages[i] + "\"}}",
                    out command));
                WebViewNavigationIntent intent;
                Assert.True(MainForm.TryCreateWebViewNavigationIntent(command, out intent));
                if (intent.Page == WebViewPage.Settings)
                    draft.Update(SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "350"));
            }

            SettingsDraftState finalSnapshot = draft.Snapshot;
            Assert.Equal("350", finalSnapshot.SyncInterval);
            Assert.True(finalSnapshot.Dirty);
            Assert.Equal(200, persistence.Active.SyncIntervalMs);
            Assert.Empty(persistence.Events);
        }


        [Fact]
        public void SameValueUpdateDoesNotPublishSnapshot()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, new RecordingEffects());

            SettingsDraftOperationResult result = runtime.Update(
                SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "200"));

            Assert.False(result.ShouldPublishSnapshot);
            Assert.False(result.State.Dirty);
            Assert.Empty(persistence.Events);
        }
        [Fact]
        public void CleanSaveDoesNotPersistOrPublishSnapshot()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, new RecordingEffects());

            SettingsDraftOperationResult result = runtime.Save();
            Assert.False(result.ShouldPublishSnapshot);

            Assert.Equal(SettingsDraftOperationOutcome.Applied, result.Outcome);
            Assert.Empty(persistence.Events);
        }

        [Fact]
        public void RevertedDraftDoesNotPersistOrPublishSnapshot()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, new RecordingEffects());

            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "350"));
            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "200"));
            SettingsDraftOperationResult result = runtime.Save();

            Assert.False(result.ShouldPublishSnapshot);
            Assert.Equal(SettingsDraftOperationOutcome.Applied, result.Outcome);
            Assert.False(result.State.Dirty);
            Assert.Empty(persistence.Events);
        }

        [Fact]
        public void ResetAndCancelNoOpsDoNotPublishSnapshots()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, new RecordingEffects());

            SettingsDraftOperationResult cancel = runtime.Cancel();
            Assert.False(cancel.ShouldPublishSnapshot);

            runtime.Reset();
            SettingsDraftOperationResult reset = runtime.Reset();
            Assert.False(reset.ShouldPublishSnapshot);
            Assert.False(reset.State.Dirty);
        }
        [Fact]
        public void ResetUsesProductDefaultsWithoutPersistenceOrEffects()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            active.LanguagePreference = "kr";
            active.ColorMode = AppConfig.ColorModeDark;
            active.DebugDiagnosticsEnabled = true;
            active.PlayPonder = false;
            RecordingPersistence persistence = new RecordingPersistence(active);
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, new RecordingEffects());

            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "350"));
            SettingsDraftOperationResult result = runtime.Reset();
            Assert.True(result.ShouldPublishSnapshot);

            Assert.Equal(SettingsDraftOperationOutcome.Applied, result.Outcome);
            Assert.Equal("host", result.State.Language);
            Assert.Equal("system", result.State.Theme);
            Assert.False(result.State.Diagnostics);
            Assert.True(result.State.BackgroundAnalysis);
            Assert.True(result.State.Dirty);
            Assert.Equal("kr", persistence.Active.LanguagePreference);
            Assert.Equal(AppConfig.ColorModeDark, persistence.Active.ColorMode);
            Assert.Empty(persistence.Events);
        }
        [Fact]
        public void ResetToActiveDefaultsClearsDirtyBeforeNoOpSave()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, new RecordingEffects());

            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "350"));
            SettingsDraftOperationResult reset = runtime.Reset();

            Assert.True(reset.ShouldPublishSnapshot);
            Assert.False(reset.State.Dirty);

            SettingsDraftOperationResult save = runtime.Save();
            Assert.False(save.ShouldPublishSnapshot);
            Assert.Equal(SettingsDraftOperationOutcome.Applied, save.Outcome);
            Assert.Empty(persistence.Events);
        }


        [Fact]
        public void CancelReloadsLatestActiveConfig()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, new RecordingEffects());

            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "350"));
            persistence.Active.SyncIntervalMs = 410;
            persistence.Active.LanguagePreference = "jp";
            persistence.Active.BoardWidth = 13;

            SettingsDraftOperationResult result = runtime.Cancel();
            Assert.True(result.ShouldPublishSnapshot);
            Assert.Equal(SettingsDraftOperationOutcome.Applied, result.Outcome);
            Assert.Equal("410", result.State.SyncInterval);
            Assert.Equal("jp", result.State.Language);
            Assert.False(result.State.Dirty);
            Assert.Empty(persistence.Events);
        }

        [Fact]
        public void SaveOverlaysLatestConfigAndAppliesOwnedEffectsOnceInOrder()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            RecordingEffects effects = new RecordingEffects(persistence.Events);
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, effects);
            persistence.Active.BoardWidth = 13;
            persistence.Active.SyncBoth = true;

            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "350"));
            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.Language, "kr"));
            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.Theme, "dark"));
            runtime.Update(SettingsDraftUpdate.Boolean(SettingsDraftField.BackgroundAnalysis, false));
            runtime.Update(SettingsDraftUpdate.Boolean(SettingsDraftField.Diagnostics, true));

            SettingsDraftOperationResult result = runtime.Save();
            Assert.True(result.ShouldPublishSnapshot);

            Assert.Equal(SettingsDraftOperationOutcome.Saved, result.Outcome);
            Assert.Equal(new[] { "persist", "replace", "language:kr", "theme:1", "background:false" }, persistence.Events);
            Assert.Equal(350, persistence.Persisted.SyncIntervalMs);
            Assert.Equal(13, persistence.Active.BoardWidth);
            Assert.True(persistence.Active.SyncBoth);
            Assert.Equal("kr", persistence.Active.LanguagePreference);
            Assert.Equal(AppConfig.ColorModeDark, persistence.Active.ColorMode);
            Assert.False(persistence.Active.PlayPonder);
            Assert.True(persistence.Active.DebugDiagnosticsEnabled);
            Assert.False(result.State.Dirty);
            Assert.Empty(result.State.Errors);
            Assert.Null(result.State.SaveError);
        }

        [Fact]
        public void ValidationFailureRetainsDraftAndUsesSemanticMessages()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            RecordingEffects effects = new RecordingEffects();
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, effects);
            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.GrayOffset, "not-an-integer"));

            SettingsDraftOperationResult result = runtime.Save();
            Assert.True(result.ShouldPublishSnapshot);

            Assert.Equal(SettingsDraftOperationOutcome.ValidationFailed, result.Outcome);
            Assert.True(result.State.Dirty);
            Assert.Equal("not-an-integer", result.State.GrayOffset);
            Assert.Equal("SettingsForm_mustBeInteger", result.State.Errors["grayOffset"].Key);
            Assert.Empty(result.State.Errors["grayOffset"].Arguments);
            Assert.Equal(200, persistence.Active.SyncIntervalMs);
            Assert.Empty(persistence.Events);
            Assert.Empty(effects.Events);
        }
        [Fact]
        public void ValidationMessagesCarryTypedArguments()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, new RecordingEffects());
            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "10"));
            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.GrayOffset, "256"));

            SettingsDraftOperationResult result = runtime.Save();

            Assert.Equal(SettingsDraftOperationOutcome.ValidationFailed, result.Outcome);
            Assert.Equal("WebView_integerAtLeast", result.State.Errors["syncInterval"].Key);
            Assert.Equal(new object[] { 20 }, result.State.Errors["syncInterval"].Arguments);
            Assert.Equal("WebView_integerRange", result.State.Errors["grayOffset"].Key);
            Assert.Equal(new object[] { 0, 255 }, result.State.Errors["grayOffset"].Arguments);
            Assert.Empty(persistence.Events);
        }

        [Fact]
        public void PersistenceFailureRetainsDraftAndCanRetry()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active) { Fail = true };
            RecordingEffects effects = new RecordingEffects();
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, effects);
            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "350"));

            SettingsDraftOperationResult failed = runtime.Save();
            Assert.True(failed.ShouldPublishSnapshot);

            Assert.Equal(SettingsDraftOperationOutcome.PersistenceFailed, failed.Outcome);
            Assert.Equal("350", failed.State.SyncInterval);
            Assert.True(failed.State.Dirty);
            Assert.Equal("WebView_settingsSaveFailed", failed.State.SaveError.Key);
            Assert.Equal("disk full", failed.State.SaveError.DiagnosticDetail);
            Assert.Equal(new[] { "persist" }, persistence.Events);
            Assert.Equal(200, persistence.Active.SyncIntervalMs);
            Assert.Empty(effects.Events);

            persistence.Fail = false;
            SettingsDraftOperationResult retried = runtime.Save();

            Assert.True(retried.ShouldPublishSnapshot);
            Assert.Equal(SettingsDraftOperationOutcome.Saved, retried.Outcome);
            Assert.Equal(new[] { "persist", "persist", "replace" }, persistence.Events);
            Assert.Equal(350, persistence.Active.SyncIntervalMs);
            Assert.False(retried.State.Dirty);
            Assert.Null(retried.State.SaveError);
        }

        [Fact]
        public void DurablePersistenceFailureUsesDistinctSemanticOutcome()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active) { DurableFail = true };
            RecordingEffects effects = new RecordingEffects();
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, effects);
            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "350"));

            SettingsDraftOperationResult result = runtime.Save();
            Assert.True(result.ShouldPublishSnapshot);

            Assert.Equal(SettingsDraftOperationOutcome.DurablePersistenceFailed, result.Outcome);
            Assert.True(result.State.Dirty);
            Assert.Equal("WebView_settingsDurableSaveFailed", result.State.SaveError.Key);
            Assert.Contains("configuration transaction failed", result.State.SaveError.DiagnosticDetail);
            Assert.Equal(new[] { "persist" }, persistence.Events);
            Assert.Equal(200, persistence.Active.SyncIntervalMs);
            Assert.Empty(effects.Events);
        }

        [Fact]
        public void EffectFailureRetainsRecoveryAndRetriesOnlyFailedEffect()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            RecordingEffects effects = new RecordingEffects(persistence.Events) { ThrowOn = "theme" };
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, effects);
            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.Language, "kr"));
            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.Theme, "dark"));
            runtime.Update(SettingsDraftUpdate.Boolean(SettingsDraftField.BackgroundAnalysis, false));
            runtime.Update(SettingsDraftUpdate.Boolean(SettingsDraftField.Diagnostics, true));

            SettingsDraftOperationResult result = runtime.Save();
            Assert.True(result.ShouldPublishSnapshot);

            Assert.Equal(SettingsDraftOperationOutcome.EffectsFailed, result.Outcome);
            Assert.False(result.State.Dirty);
            Assert.Equal("WebView_settingsEffectFailed", result.State.SaveError.Key);
            Assert.Equal("theme failed", result.State.SaveError.DiagnosticDetail);
            Assert.Equal(
                new[] { "persist", "replace", "language:kr", "theme:1", "background:false" },
                persistence.Events);
            Assert.Equal("kr", persistence.Active.LanguagePreference);
            Assert.Equal(AppConfig.ColorModeDark, persistence.Active.ColorMode);
            Assert.False(persistence.Active.PlayPonder);
            Assert.True(persistence.Active.DebugDiagnosticsEnabled);

            effects.ThrowOn = null;
            SettingsDraftOperationResult retried = runtime.Save();
            Assert.True(retried.ShouldPublishSnapshot);

            Assert.Equal(SettingsDraftOperationOutcome.Saved, retried.Outcome);
            Assert.Equal(
                new[]
                {
                    "persist", "replace", "language:kr", "theme:1", "background:false",
                    "theme:1"
                },
                persistence.Events);
            Assert.Null(retried.State.SaveError);
        }
        [Fact]
        public void LanguageEffectFailureRetriesUnfinishedRefreshOnly()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            RecordingPersistence persistence = new RecordingPersistence(active);
            RecordingEffects effects = new RecordingEffects(persistence.Events) { ThrowOn = "language" };
            SettingsDraftRuntime runtime = CreateRuntime(active, persistence, effects);
            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.Language, "kr"));

            SettingsDraftOperationResult failed = runtime.Save();
            Assert.Equal(SettingsDraftOperationOutcome.EffectsFailed, failed.Outcome);
            Assert.Equal("language failed", failed.State.SaveError.DiagnosticDetail);
            Assert.Equal(new[] { "persist", "replace", "language:kr" }, persistence.Events);

            effects.ThrowOn = null;
            SettingsDraftOperationResult retried = runtime.Save();

            Assert.Equal(SettingsDraftOperationOutcome.Saved, retried.Outcome);
            Assert.Equal(new[] { "persist", "replace", "language:kr", "language:kr" }, persistence.Events);
            Assert.Equal("kr", persistence.Active.LanguagePreference);
            Assert.Null(retried.State.SaveError);
        }
        [Fact]
        public void LanguageCatalogFailureCanRetrySameEffectiveLanguage()
        {
            string currentLanguage = "cn";
            int reloadAttempts = 0;
            Action<string> setLanguage = delegate(string value) { currentLanguage = value; };
            Action reload = delegate
            {
                reloadAttempts++;
                if (reloadAttempts == 1)
                    throw new IOException("language catalog failed");
            };

            Assert.Throws<IOException>(delegate
            {
                Program.ApplyLanguagePreferenceValue("en", "en", currentLanguage, setLanguage, reload);
            });
            Assert.Equal("en", currentLanguage);
            Assert.False(Program.ApplyLanguagePreferenceValue(
                "en",
                "en",
                currentLanguage,
                setLanguage,
                reload));
            Assert.Equal(2, reloadAttempts);
        }

        [Theory]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"diagnostics\",\"value\":true}}", true)]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"syncInterval\",\"value\":\"250\"}}", true)]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"theme\",\"value\":\"dark\"}}", true)]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"language\",\"value\":\"jp\"}}", true)]
        [InlineData("{\"type\":\"settings.save\",\"payload\":{}}", true)]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"diagnostics\",\"value\":\"true\"}}", false)]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"unknown\",\"value\":true}}", false)]
        [InlineData("{\"type\":\"settings.update\",\"payload\":{\"key\":\"language\",\"value\":\"unsupported\"}}", false)]
        [InlineData("{\"type\":\"settings.save\",\"payload\":{\"extra\":true}}", false)]
        public void CommandValidation_IsStrict(string json, bool expected)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            ReadBoardUiCommand command = JsonSerializer.Deserialize<ReadBoardUiCommand>(
                document.RootElement.GetRawText(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            Assert.Equal(expected, MainForm.IsValidWebViewSettingsCommand(command));
        }

        [Fact]
        public void SettingsSaveClearsControlCenterPersistenceError()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            ControlCenterRuntime controlCenter = new ControlCenterRuntime(ControlCenterPreferences.FromConfig(active), new NoOpControlCenterSessionAdapter(), new NoOpControlCenterPreferencePersistence(), new RejectingControlCenterActionAdapter());
            controlCenter.MarkPersistenceFailed(new IOException("old failure"));

            AppConfig current = active.Clone();
            IList<string> events = new List<string>();
            MainForm.MainFormSettingsDraftPersistence settingsPersistence =
                new MainForm.MainFormSettingsDraftPersistence(
                    delegate { return current.Clone(); },
                    delegate(AppConfig candidate) { events.Add("persist"); },
                    delegate(AppConfig candidate)
                    {
                        current = candidate;
                        events.Add("replace");
                    },
                    controlCenter.MarkPersistenceSucceeded);
            SettingsDraftRuntime runtime = new SettingsDraftRuntime(
                active,
                () => AppConfig.CreateDefault("220430", "TEST"),
                settingsPersistence,
                new RecordingEffects(events));

            runtime.Update(SettingsDraftUpdate.Text(SettingsDraftField.SyncInterval, "350"));
            SettingsDraftOperationResult result = runtime.Save();

            Assert.Equal(SettingsDraftOperationOutcome.Saved, result.Outcome);
            Assert.Equal(new[] { "persist", "replace" }, events);
            Assert.Equal(350, current.SyncIntervalMs);
            Assert.True(controlCenter.Snapshot.PreferencesSaved);
            Assert.Null(controlCenter.Snapshot.PersistenceError);
        }

        [Fact]
        public void SettingsProjectionAdapterLocalizesSemanticSaveError()
        {
            AppConfig active = AppConfig.CreateDefault("220430", "TEST");
            SettingsDraftState draft = SettingsDraftState.FromConfig(active);
            draft.SaveError = SemanticMessage.CreateWithDiagnostic(
                SettingsDraftMessageKeys.SaveFailed,
                "disk full");

            ReadBoardSettingsUiState projected = MainForm.WebViewSettingsStateProjector.Project(
                draft,
                delegate(string key)
                {
                    return key == SettingsDraftMessageKeys.SaveFailed
                        ? "保存失败"
                        : null;
                },
                delegate(string key)
                {
                    return key == SettingsDraftMessageKeys.SaveFailed
                        ? "Failed to save"
                        : null;
                });

            Assert.Equal("保存失败: disk full", projected.SaveError);
        }

        [Fact]
        public void RuntimeEffectsAdapterAppliesOnlyOwnedThemeAndBackgroundEffects()
        {
            IList<string> events = new List<string>();
            MainForm.MainFormSettingsDraftRuntimeEffects effects =
                new MainForm.MainFormSettingsDraftRuntimeEffects(
                    delegate(string preference)
                    {
                        events.Add("language:" + preference);
                        throw new IOException("catalog failed");
                    },
                    delegate { events.Add("title"); },
                    delegate(int colorMode) { events.Add("theme:" + colorMode); },
                    delegate(bool enabled) { events.Add("background:" + enabled); });

            Assert.Throws<IOException>(delegate { effects.ApplyLanguagePreference("en"); });
            Assert.Equal(new[] { "language:en", "title" }, events);

            events.Clear();
            effects = new MainForm.MainFormSettingsDraftRuntimeEffects(
                delegate(string preference) { },
                delegate { events.Add("title"); },
                delegate(int colorMode) { events.Add("theme:" + colorMode); },
                delegate(bool enabled) { events.Add("background:" + enabled); });
            effects.ApplyTheme(AppConfig.ColorModeDark);
            effects.ApplyBackgroundAnalysis(false);

            Assert.Equal(
                new[] { "theme:" + AppConfig.ColorModeDark, "background:False" },
                events);
        }

        private static SettingsDraftRuntime CreateRuntime(
            AppConfig active,
            RecordingPersistence persistence,
            RecordingEffects effects)
        {
            return new SettingsDraftRuntime(
                active,
                () => AppConfig.CreateDefault("220430", "TEST"),
                persistence,
                effects);
        }

        private sealed class NoOpControlCenterSessionAdapter : IControlCenterSessionAdapter
        {
            public bool HasActiveSyncOperation
            {
                get { return false; }
            }

            public void Apply(
                ControlCenterPreferences preferences,
                ControlCenterSessionState sessionState)
            {
            }
        }

        private sealed class NoOpControlCenterPreferencePersistence : IControlCenterPreferencePersistence
        {
            public void Save(ControlCenterPreferences preferences)
            {
            }
        }

        private sealed class RecordingPersistence : ISettingsDraftPersistence
        {
            public RecordingPersistence(AppConfig active)
            {
                Active = active.Clone();
            }

            public AppConfig Active { get; set; }
            public AppConfig Persisted { get; private set; }
            public bool Fail { get; set; }
            public bool DurableFail { get; set; }
            public IList<string> Events { get; } = new List<string>();

            public AppConfig GetLatestActiveConfig()
            {
                return Active.Clone();
            }

            public void Persist(AppConfig candidate)
            {
                Events.Add("persist");
                if (DurableFail)
                    throw new DurableConfigurationException(
                        "configuration transaction failed",
                        new IOException("disk full"),
                        new IOException("rollback failed"),
                        "transaction");
                if (Fail)
                    throw new IOException("disk full");
                Persisted = candidate.Clone();
            }

            public void ReplaceActiveConfig(AppConfig candidate)
            {
                Events.Add("replace");
                Active = candidate.Clone();
            }
        }

        private sealed class RecordingEffects : ISettingsDraftRuntimeEffects
        {
            private readonly IList<string> events;

            public RecordingEffects()
                : this(new List<string>())
            {
            }

            public RecordingEffects(IList<string> events)
            {
                this.events = events;
            }

            public IList<string> Events
            {
                get { return events; }
            }

            public string ThrowOn { get; set; }

            public void ApplyLanguagePreference(string preference)
            {
                events.Add("language:" + preference);
                if (ThrowOn == "language")
                    throw new InvalidOperationException("language failed");
            }

            public void ApplyTheme(int colorMode)
            {
                events.Add("theme:" + colorMode);
                if (ThrowOn == "theme")
                    throw new InvalidOperationException("theme failed");
            }

            public void ApplyBackgroundAnalysis(bool enabled)
            {
                events.Add("background:" + enabled.ToString().ToLowerInvariant());
            }

        }
    }
}
