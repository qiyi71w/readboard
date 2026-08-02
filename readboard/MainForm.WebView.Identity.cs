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
            return webViewIdentityState;
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
                            string.Format(getLangStr("WebView_candidateRowNumber"), i + 1),
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
                HasSavedIdentity = snapshot != null && snapshot.HasSavedIdentity
            };
            if (snapshot == null)
                return state;

            for (int i = 0; i < snapshot.Candidates.Count; i++)
            {
                FoxIdentityCandidate candidate = snapshot.Candidates[i];
                state.Candidates.Add(new ReadBoardIdentityCandidateUiState
                {
                    Id = candidate.Id,
                    Label = candidate.Label,
                    PreviewUrl = candidate.PreviewUrl
                });
            }
            return state;
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
