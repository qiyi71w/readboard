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

        internal bool HandleWebViewIdentityCommand(ReadBoardUiCommand command)
        {
            if (!IsValidWebViewIdentityCommand(command))
                return false;
            switch (command.Type)
            {
                case "identity.open":
                    OpenWebViewIdentity(false);
                    break;
                case "identity.close":
                    CloseWebViewIdentity(true);
                    break;
                case "identity.clearSaved":
                {
                    FoxIdentitySelectionResult result = ClearSavedFoxAutoPlayIdentity();
                    webViewIdentityState = CreateWebViewIdentityState(result.Snapshot);
                    break;
                }
                case "identity.select":
                {
                    FoxIdentitySelectionResult result = foxIdentitySelection.Select(
                        command.Payload.GetProperty("candidateId").GetString());
                    webViewIdentityState = CreateWebViewIdentityState(result.Snapshot);
                    break;
                }
                case "identity.useOnce":
                    UseWebViewIdentity(command.Payload.GetProperty("candidateId").GetString(), false);
                    break;
                case "identity.saveAndUse":
                    UseWebViewIdentity(command.Payload.GetProperty("candidateId").GetString(), true);
                    break;
            }
            return true;
        }

        private ReadBoardIdentityUiState GetWebViewIdentityState()
        {
            return ResolveWebViewIdentityState(
                webViewIdentityState,
                getLangStr,
                Program.GetDefaultLanguageText);
        }

        private void OpenWebViewIdentity(bool resumeAutoPlay)
        {
            if (!IsFoxSyncType(CurrentSyncType))
                return;

            List<FoxIdentityCandidate> candidates = new List<FoxIdentityCandidate>();
            IntPtr boardHandle = ResolveFoxAutoPlayIdentityBoardHandle();
            IntPtr captureHandle = ResolveFoxAutoPlayCaptureHandle(boardHandle);
            if (captureHandle != IntPtr.Zero)
            {
                using (Bitmap bitmap = foxAutoPlayCapturePlatform.CaptureWindow(captureHandle))
                {
                    IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.LocatePlayerListPanel(bitmap);
                    for (int i = 0; i < rows.Count; i++)
                    {
                        string signature;
                        using (Bitmap nicknameSnippet = CropBitmap(bitmap, rows[i].NicknameBounds))
                            signature = FoxPlayerNicknameSignature.FromBitmap(nicknameSnippet).Serialize();
                        if (string.IsNullOrWhiteSpace(signature))
                            continue;

                        string previewUrl;
                        using (Bitmap rowPreview = CropBitmap(bitmap, rows[i].RowBounds))
                            previewUrl = EncodeIdentityPreview(rowPreview);
                        candidates.Add(new FoxIdentityCandidate(
                            "candidate-" + (candidates.Count + 1),
                            SemanticMessage.Create("WebView_candidateRowNumber", i + 1),
                            signature,
                            previewUrl));
                    }
                }
            }

            FoxIdentitySelectionSnapshot snapshot = foxIdentitySelection.Open(
                candidates,
                resumeAutoPlay,
                lastManualAutoPlayColorMode);
            webViewIdentityState = CreateWebViewIdentityState(snapshot);
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

        private void UseWebViewIdentity(string candidateId, bool save)
        {
            FoxIdentitySelectionResult selectResult = foxIdentitySelection.Select(candidateId);
            if (selectResult.Outcome == FoxIdentitySelectionActionOutcome.Rejected)
                return;
            bool resumeAutomaticColor = foxIdentitySelection.IsFirstAutomaticSelectionPending;
            FoxIdentitySelectionResult result = save
                ? foxIdentitySelection.SaveAndUse()
                : foxIdentitySelection.UseOnce();
            if (!result.Accepted)
                return;

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
        }

        private void CloseWebViewIdentity(bool cancelled)
        {
            if (cancelled)
            {
                FoxIdentitySelectionResult result = foxIdentitySelection.Cancel();
                if (result.RestorePreviousManualMode)
                    ApplyAutoPlayColorMode(result.RestoredManualMode);
            }
            webViewIdentityState = new ReadBoardIdentityUiState();
        }
    }
}
