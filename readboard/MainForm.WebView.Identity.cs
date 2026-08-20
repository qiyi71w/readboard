using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;

namespace readboard
{
    public partial class MainForm
    {
        private ReadBoardIdentityUiState webViewIdentityState = new ReadBoardIdentityUiState();
        internal static bool IsValidWebViewIdentityCommand(ReadBoardUiCommand command)
        {
            if (command == null)
                return false;
            switch (command.Type)
            {
                case "identity.open":
                case "identity.close":
                case "identity.clearSaved":
                    return HasEmptyPayload(command.Payload);
                case "identity.select":
                case "identity.useOnce":
                case "identity.saveAndUse":
                    JsonElement candidateId;
                    return command.Payload.ValueKind == JsonValueKind.Object
                        && CountProperties(command.Payload) == 1
                        && command.Payload.TryGetProperty("candidateId", out candidateId)
                        && candidateId.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(candidateId.GetString());
                default:
                    return false;
            }
        }

        internal static bool ShouldPublishWebViewIdentityResult(FoxIdentitySelectionResult result)
        {
            return result != null
                && result.Outcome != FoxIdentitySelectionActionOutcome.NoOp;
        }

        internal bool HandleWebViewIdentityCommand(ReadBoardUiCommand command)
        {
            if (!IsValidWebViewIdentityCommand(command))
                return false;
            switch (command.Type)
            {
                case "identity.open":
                    return OpenWebViewIdentity(false);
                case "identity.close":
                    return CloseWebViewIdentity(true);
                case "identity.clearSaved":
                {
                    FoxIdentitySelectionResult result = ClearSavedFoxAutoPlayIdentity();
                    webViewIdentityState = CreateWebViewIdentityState(result.Snapshot);
                    return ShouldPublishWebViewIdentityResult(result);
                }
                case "identity.select":
                {
                    FoxIdentitySelectionResult result = foxIdentitySelection.Select(
                        command.Payload.GetProperty("candidateId").GetString());
                    webViewIdentityState = CreateWebViewIdentityState(result.Snapshot);
                    return ShouldPublishWebViewIdentityResult(result);
                }
                case "identity.useOnce":
                    return UseWebViewIdentity(
                        command.Payload.GetProperty("candidateId").GetString(),
                        false);
                case "identity.saveAndUse":
                    return UseWebViewIdentity(
                        command.Payload.GetProperty("candidateId").GetString(),
                        true);
                default:
                    return false;
            }
        }

        private ReadBoardIdentityUiState GetWebViewIdentityState()
        {
            return ResolveWebViewIdentityState(
                webViewIdentityState,
                getLangStr,
                Program.GetDefaultLanguageText);
        }

        private bool OpenWebViewIdentity(bool resumeAutoPlay)
        {
            if (!IsFoxSyncType(CurrentSyncType))
                return true;

            SampleFoxMatchBar(
                hwnd,
                ResolveFoxWindowContext(),
                foxIdentitySelection.EffectiveIdentitySignature,
                true);
            FoxMatchBarReading reading = foxMatchBarLiveRecognition.CurrentReading;
            IList<FoxIdentityCandidate> candidates = FoxMatchBarIdentityCandidates.Build(
                reading.Players);
            FoxIdentitySelectionSnapshot snapshot = foxIdentitySelection.Open(
                candidates,
                resumeAutoPlay,
                lastManualAutoPlayColorMode);
            webViewIdentityState = CreateWebViewIdentityState(snapshot);
            return true;
        }


        private static ReadBoardIdentityUiState CreateWebViewIdentityState(
            FoxIdentitySelectionSnapshot snapshot)
        {
            ReadBoardIdentityUiState state = new ReadBoardIdentityUiState
            {
                Open = snapshot != null && snapshot.Open,
                SelectedId = snapshot == null ? null : snapshot.SelectedCandidateId,
                SavedId = snapshot == null ? null : snapshot.SavedCandidateId,
                HasSavedIdentity = snapshot != null && snapshot.HasSavedIdentity,
                CanUseOnce = snapshot != null
                    && snapshot.Open
                    && !string.IsNullOrWhiteSpace(snapshot.SelectedCandidateId),
                CanSaveAndUse = snapshot != null
                    && snapshot.Open
                    && !string.IsNullOrWhiteSpace(snapshot.SelectedCandidateId),
                DialogTitleMessage = SemanticMessage.Create("WebView_selectIdentity"),
                PromptMessage = SemanticMessage.Create("FoxAutoPlayIdentityDialog_lblPrompt"),
                DetectedNicknamesLabelMessage = SemanticMessage.Create("FoxAutoPlayIdentityDialog_lblDetectedNicknames"),
                SelectedLabelMessage = SemanticMessage.Create("WebView_selectedIdentity"),
                EmptyTitleMessage = SemanticMessage.Create("FoxAutoPlayIdentityDialog_noDetectedNicknames"),
                WindowHintMessage = SemanticMessage.Create("WebView_identityWindowHint"),
                ClearSavedLabelMessage = SemanticMessage.Create("FoxAutoPlayIdentityDialog_btnClearSavedIdentity"),
                CancelLabelMessage = SemanticMessage.Create("FoxAutoPlayIdentityDialog_btnCancel"),
                UseOnceLabelMessage = SemanticMessage.Create("FoxAutoPlayIdentityDialog_btnUseOnce"),
                SaveAndUseLabelMessage = SemanticMessage.Create("FoxAutoPlayIdentityDialog_btnSaveAndUse"),
                UnnamedCandidateLabelMessage = SemanticMessage.Create("WebView_unnamedCandidate"),
                SavedLabelMessage = SemanticMessage.Create("WebView_saved"),
                CandidateRowLabelMessage = SemanticMessage.Create("WebView_candidateRow"),
                ScreenshotLabelMessage = SemanticMessage.Create("WebView_screenshot")
            };
            if (snapshot == null)
                return state;

            for (int i = 0; i < snapshot.Candidates.Count; i++)
            {
                FoxIdentityCandidate candidate = snapshot.Candidates[i];
                state.Candidates.Add(new ReadBoardIdentityCandidateUiState
                {
                    Id = candidate.Id,
                    LabelMessage = candidate.LabelMessage,
                    PreviewUrl = candidate.PreviewUrl
                });
            }
            return state;
        }

        internal static ReadBoardIdentityUiState ResolveWebViewIdentityState(
            ReadBoardIdentityUiState state,
            Func<string, string> getLocalizedText,
            Func<string, string> getDefaultText)
        {
            if (state == null)
                return null;

            SemanticMessage dialogTitleMessage = state.DialogTitleMessage
                ?? SemanticMessage.Create("WebView_selectIdentity");
            SemanticMessage promptMessage = state.PromptMessage
                ?? SemanticMessage.Create("FoxAutoPlayIdentityDialog_lblPrompt");
            SemanticMessage detectedNicknamesLabelMessage = state.DetectedNicknamesLabelMessage
                ?? SemanticMessage.Create("FoxAutoPlayIdentityDialog_lblDetectedNicknames");
            SemanticMessage selectedLabelMessage = state.SelectedLabelMessage
                ?? SemanticMessage.Create("WebView_selectedIdentity");
            SemanticMessage emptyTitleMessage = state.EmptyTitleMessage
                ?? SemanticMessage.Create("FoxAutoPlayIdentityDialog_noDetectedNicknames");
            SemanticMessage windowHintMessage = state.WindowHintMessage
                ?? SemanticMessage.Create("WebView_identityWindowHint");
            SemanticMessage clearSavedLabelMessage = state.ClearSavedLabelMessage
                ?? SemanticMessage.Create("FoxAutoPlayIdentityDialog_btnClearSavedIdentity");
            SemanticMessage cancelLabelMessage = state.CancelLabelMessage
                ?? SemanticMessage.Create("FoxAutoPlayIdentityDialog_btnCancel");
            SemanticMessage useOnceLabelMessage = state.UseOnceLabelMessage
                ?? SemanticMessage.Create("FoxAutoPlayIdentityDialog_btnUseOnce");
            SemanticMessage saveAndUseLabelMessage = state.SaveAndUseLabelMessage
                ?? SemanticMessage.Create("FoxAutoPlayIdentityDialog_btnSaveAndUse");
            SemanticMessage unnamedCandidateLabelMessage = state.UnnamedCandidateLabelMessage
                ?? SemanticMessage.Create("WebView_unnamedCandidate");
            SemanticMessage savedLabelMessage = state.SavedLabelMessage
                ?? SemanticMessage.Create("WebView_saved");
            SemanticMessage candidateRowLabelMessage = state.CandidateRowLabelMessage
                ?? SemanticMessage.Create("WebView_candidateRow");
            SemanticMessage screenshotLabelMessage = state.ScreenshotLabelMessage
                ?? SemanticMessage.Create("WebView_screenshot");

            ReadBoardIdentityUiState resolved = new ReadBoardIdentityUiState
            {
                Open = state.Open,
                SelectedId = state.SelectedId,
                SavedId = state.SavedId,
                HasSavedIdentity = state.HasSavedIdentity,
                CanUseOnce = state.CanUseOnce,
                CanSaveAndUse = state.CanSaveAndUse,
                DialogTitle = ResolveWebViewIdentityText(dialogTitleMessage, state.DialogTitle, getLocalizedText, getDefaultText),
                DialogTitleMessage = dialogTitleMessage,
                Prompt = ResolveWebViewIdentityText(promptMessage, state.Prompt, getLocalizedText, getDefaultText),
                PromptMessage = promptMessage,
                DetectedNicknamesLabel = ResolveWebViewIdentityText(detectedNicknamesLabelMessage, state.DetectedNicknamesLabel, getLocalizedText, getDefaultText),
                DetectedNicknamesLabelMessage = detectedNicknamesLabelMessage,
                SelectedLabel = ResolveWebViewIdentityText(selectedLabelMessage, state.SelectedLabel, getLocalizedText, getDefaultText),
                SelectedLabelMessage = selectedLabelMessage,
                EmptyTitle = ResolveWebViewIdentityText(emptyTitleMessage, state.EmptyTitle, getLocalizedText, getDefaultText),
                EmptyTitleMessage = emptyTitleMessage,
                WindowHint = ResolveWebViewIdentityText(windowHintMessage, state.WindowHint, getLocalizedText, getDefaultText),
                WindowHintMessage = windowHintMessage,
                ClearSavedLabel = ResolveWebViewIdentityText(clearSavedLabelMessage, state.ClearSavedLabel, getLocalizedText, getDefaultText),
                ClearSavedLabelMessage = clearSavedLabelMessage,
                CancelLabel = ResolveWebViewIdentityText(cancelLabelMessage, state.CancelLabel, getLocalizedText, getDefaultText),
                CancelLabelMessage = cancelLabelMessage,
                UseOnceLabel = ResolveWebViewIdentityText(useOnceLabelMessage, state.UseOnceLabel, getLocalizedText, getDefaultText),
                UseOnceLabelMessage = useOnceLabelMessage,
                SaveAndUseLabel = ResolveWebViewIdentityText(saveAndUseLabelMessage, state.SaveAndUseLabel, getLocalizedText, getDefaultText),
                SaveAndUseLabelMessage = saveAndUseLabelMessage,
                UnnamedCandidateLabel = ResolveWebViewIdentityText(unnamedCandidateLabelMessage, state.UnnamedCandidateLabel, getLocalizedText, getDefaultText),
                UnnamedCandidateLabelMessage = unnamedCandidateLabelMessage,
                SavedLabel = ResolveWebViewIdentityText(savedLabelMessage, state.SavedLabel, getLocalizedText, getDefaultText),
                SavedLabelMessage = savedLabelMessage,
                CandidateRowLabel = ResolveWebViewIdentityText(candidateRowLabelMessage, state.CandidateRowLabel, getLocalizedText, getDefaultText),
                CandidateRowLabelMessage = candidateRowLabelMessage,
                ScreenshotLabel = ResolveWebViewIdentityText(screenshotLabelMessage, state.ScreenshotLabel, getLocalizedText, getDefaultText),
                ScreenshotLabelMessage = screenshotLabelMessage
            };

            if (state.Candidates == null)
                return resolved;

            foreach (ReadBoardIdentityCandidateUiState candidate in state.Candidates)
            {
                if (candidate == null)
                    continue;
                SemanticMessage labelMessage = candidate.LabelMessage;
                if (labelMessage == null && string.IsNullOrWhiteSpace(candidate.Label))
                    labelMessage = unnamedCandidateLabelMessage;
                string label = ResolveWebViewIdentityText(
                    labelMessage,
                    candidate.Label,
                    getLocalizedText,
                    getDefaultText);
                if (string.IsNullOrWhiteSpace(label))
                    label = resolved.UnnamedCandidateLabel;
                resolved.Candidates.Add(new ReadBoardIdentityCandidateUiState
                {
                    Id = candidate.Id,
                    Label = label,
                    LabelMessage = labelMessage,
                    PreviewUrl = candidate.PreviewUrl,
                    PreviewAlt = label + resolved.ScreenshotLabel
                });
            }
            return resolved;
        }

        private static string ResolveWebViewIdentityText(
            SemanticMessage message,
            string fallback,
            Func<string, string> getLocalizedText,
            Func<string, string> getDefaultText)
        {
            if (message == null)
                return fallback;
            return SemanticMessageResolver.Resolve(message, getLocalizedText, getDefaultText);
        }

        internal static string EncodeIdentityPreview(Bitmap bitmap)
        {
            if (bitmap == null)
                return null;
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
            }
        }

        private bool UseWebViewIdentity(string candidateId, bool save)
        {
            FoxIdentitySelectionResult selectResult = foxIdentitySelection.Select(candidateId);
            if (selectResult.Outcome == FoxIdentitySelectionActionOutcome.Rejected)
                return ShouldPublishWebViewIdentityResult(selectResult);
            bool resumeAutomaticColor = foxIdentitySelection.IsFirstAutomaticSelectionPending;
            FoxIdentitySelectionResult result = save
                ? foxIdentitySelection.SaveAndUse()
                : foxIdentitySelection.UseOnce();
            if (!result.Accepted)
                return ShouldPublishWebViewIdentityResult(result);

            ClearFoxAutoPlayColorDetectionState();
            controlCenterRuntime.UpdateAutoPlayObservation(
                foxIdentitySelection.EffectiveIdentitySignature,
                ResolveFoxWindowContext(),
                null);
            ControlCenterApplyResult modeResult = null;
            if (resumeAutomaticColor)
            {
                modeResult = ApplyControlCenterIntent(
                    ControlCenterIntent.SetAutoPlayColor(AutoPlayColorMode.FoxAuto));
            }
            CloseWebViewIdentity(false);
            if (controlCenterRuntime.Snapshot.AutoPlayColorMode == AutoPlayColorMode.FoxAuto)
            {
                ResolveCurrentAutoPlayColor(ResolveFoxWindowContext());
                bool modeChangeMayHaveSentPlay = modeResult != null
                    && modeResult.Outcome == ControlCenterApplyOutcome.Changed;
                if (!modeChangeMayHaveSentPlay
                    && sessionCoordinator.KeepSync
                    && !isInitializingProtocolState)
                    SendPlayCommandIfSelected();
            }
            return true;
        }

        private bool CloseWebViewIdentity(bool cancelled)
        {
            bool wasOpen = webViewIdentityState.Open;
            bool changed = false;
            if (cancelled)
            {
                FoxIdentitySelectionResult result = foxIdentitySelection.Cancel();
                changed = ShouldPublishWebViewIdentityResult(result);
                if (result.RestorePreviousManualMode)
                    ApplyAutoPlayColorMode(result.RestoredManualMode);
            }
            webViewIdentityState = new ReadBoardIdentityUiState();
            return changed || wasOpen;
        }
    }
}
