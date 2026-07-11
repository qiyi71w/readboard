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
        private readonly Dictionary<string, string> webViewIdentitySignatures = new Dictionary<string, string>(StringComparer.Ordinal);
        private ReadBoardIdentityUiState webViewIdentityState = new ReadBoardIdentityUiState();
        private bool resumeAutoPlayAfterIdentitySelection;

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
                    ClearSavedFoxAutoPlayIdentity();
                    webViewIdentityState.SavedId = null;
                    webViewIdentityState.HasSavedIdentity = false;
                    ClearFoxAutoPlayColorDetectionState();
                    break;
                case "identity.select":
                    SelectWebViewIdentity(command.Payload.GetProperty("candidateId").GetString());
                    break;
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

            webViewIdentitySignatures.Clear();
            AppConfig config = Program.CurrentContext.Config;
            ReadBoardIdentityUiState state = new ReadBoardIdentityUiState
            {
                Open = true,
                HasSavedIdentity = config != null
                    && (!string.IsNullOrWhiteSpace(config.FoxAutoPlayNickname)
                        || !string.IsNullOrWhiteSpace(config.FoxAutoPlayNicknameSignature))
            };
            string currentSignature = ResolveCurrentFoxAutoPlayNicknameSignature();
            string savedSignature = Program.CurrentContext.Config.FoxAutoPlayNicknameSignature;
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

                        string id = "candidate-" + (state.Candidates.Count + 1);
                        string previewUrl;
                        using (Bitmap rowPreview = CropBitmap(bitmap, rows[i].RowBounds))
                            previewUrl = EncodeIdentityPreview(rowPreview);
                        webViewIdentitySignatures.Add(id, signature);
                        state.Candidates.Add(new ReadBoardIdentityCandidateUiState
                        {
                            Id = id,
                            Label = "玩家行 " + (i + 1),
                            PreviewUrl = previewUrl
                        });
                        if (string.Equals(signature, currentSignature, StringComparison.Ordinal))
                            state.SelectedId = id;
                        if (string.Equals(signature, savedSignature, StringComparison.Ordinal))
                            state.SavedId = id;
                    }
                }
            }

            resumeAutoPlayAfterIdentitySelection = resumeAutoPlay;
            webViewIdentityState = state;
            PostWebViewState();
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

        private void SelectWebViewIdentity(string candidateId)
        {
            if (webViewIdentitySignatures.ContainsKey(candidateId))
                webViewIdentityState.SelectedId = candidateId;
        }

        private void UseWebViewIdentity(string candidateId, bool save)
        {
            string signature;
            if (!webViewIdentitySignatures.TryGetValue(candidateId, out signature))
                return;

            currentFoxAutoPlayNicknameSignature = signature;
            if (save)
            {
                AppConfig updatedConfig = Program.CurrentConfig.Clone();
                updatedConfig.FoxAutoPlayNickname = string.Empty;
                updatedConfig.FoxAutoPlayNicknameSignature = signature;
                Program.SaveAppConfig(updatedConfig);
            }
            ClearFoxAutoPlayColorDetectionState();
            CloseWebViewIdentity(false);
            if (radioAutoPlayColor.Checked)
            {
                ResolveCurrentAutoPlayColor(ResolveFoxWindowContext());
                if (sessionCoordinator.KeepSync && !isInitializingProtocolState)
                    SendPlayCommandIfSelected();
            }
        }

        private void CloseWebViewIdentity(bool cancelled)
        {
            bool restoreManualMode = cancelled && resumeAutoPlayAfterIdentitySelection;
            resumeAutoPlayAfterIdentitySelection = false;
            webViewIdentitySignatures.Clear();
            webViewIdentityState = new ReadBoardIdentityUiState();
            if (restoreManualMode)
                ApplyAutoPlayColorMode(lastManualAutoPlayColorMode);
            PostWebViewState();
        }
    }
}
