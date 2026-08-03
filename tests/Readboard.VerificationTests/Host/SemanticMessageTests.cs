using readboard;
using System;
using System.Collections.Generic;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class SemanticMessageTests
    {
        [Fact]
        public void Resolve_FormatsTypedArgumentsAndAppendsDiagnosticDetail()
        {
            SemanticMessage message = SemanticMessage.CreateWithDiagnostic("test.range", "disk full", 20, 255);

            string result = SemanticMessageResolver.Resolve(
                message,
                delegate(string key) { return "Enter an integer between {0} and {1}"; },
                delegate(string key) { return "请输入 {0}–{1} 之间的整数"; });

            Assert.Equal("Enter an integer between 20 and 255: disk full", result);
        }

        [Fact]
        public void Resolve_UsesInternalDefaultWhenCurrentTemplateIsMalformedOrIncompatible()
        {
            SemanticMessage message = SemanticMessage.Create("test.range", 20, 255);

            string malformed = SemanticMessageResolver.Resolve(
                message,
                delegate(string key) { return "{0"; },
                delegate(string key) { return "请输入 {0}–{1} 之间的整数"; });
            string incompatible = SemanticMessageResolver.Resolve(
                message,
                delegate(string key) { return "Enter at least {1}"; },
                delegate(string key) { return "请输入 {0}–{1} 之间的整数"; });

            Assert.Equal("请输入 20–255 之间的整数", malformed);
            Assert.Equal("请输入 20–255 之间的整数", incompatible);
        }

        [Fact]
        public void Resolve_UsesInternalDefaultWhenCurrentResourceIsMissingOrBlank()
        {
            SemanticMessage message = SemanticMessage.Create("test.message");

            Assert.Equal(
                "Internal default",
                SemanticMessageResolver.Resolve(
                    message,
                    delegate(string key) { return null; },
                    delegate(string key) { return "Internal default"; }));
            Assert.Equal(
                "Internal default",
                SemanticMessageResolver.Resolve(
                    message,
                    delegate(string key) { return "  "; },
                    delegate(string key) { return "Internal default"; }));
        }

        [Fact]
        public void ResolveText_UsesInternalDefaultForMalformedCurrentResource()
        {
            Assert.Equal(
                "Internal default {0}",
                SemanticMessageResolver.ResolveText(
                    "test.message",
                    "Malformed {1}",
                    "Internal default {0}"));
        }

        [Fact]
        public void RetainedSemanticLog_ResolvesInTheCurrentLanguageWithTypedArguments()
        {
            ReadBoardUiLogEntry retained = new ReadBoardUiLogEntry
            {
                Time = "12:34:56",
                Level = "SYNC",
                Message = "stale text",
                MessageKey = "test.range",
                Arguments = new object[] { 20, 255 },
                DiagnosticDetail = "network"
            };

            List<ReadBoardUiLogEntry> cn = MainForm.ResolveWebViewLogs(
                new[] { retained },
                delegate(string key) { return "请输入 {0}–{1} 之间的整数"; },
                delegate(string key) { return "请输入 {0}–{1} 之间的整数"; });
            List<ReadBoardUiLogEntry> en = MainForm.ResolveWebViewLogs(
                new[] { retained },
                delegate(string key) { return "Enter an integer between {0} and {1}"; },
                delegate(string key) { return "请输入 {0}–{1} 之间的整数"; });

            Assert.Equal("请输入 20–255 之间的整数: network", cn[0].Message);
            Assert.Equal("Enter an integer between 20 and 255: network", en[0].Message);
            Assert.Equal("test.range", en[0].MessageKey);
            Assert.Equal("network", en[0].DiagnosticDetail);
        }
        [Fact]
        public void RetainedUpdateAndDialogSnapshots_RelocalizeSemanticMessages()
        {
            ReadBoardUpdateUiState retainedUpdate = new ReadBoardUpdateUiState
            {
                Open = true,
                Status = "check-failed",
                TitleMessage = new SemanticMessage("test.title"),
                DetailMessage = SemanticMessage.CreateWithDiagnostic("test.detail", "socket closed")
            };
            ReadBoardUpdateUiState cnUpdate = MainForm.ResolveWebViewUpdateState(
                retainedUpdate,
                delegate(string key)
                {
                    return key == "test.title" ? "检查更新失败" : "未知错误";
                },
                delegate(string key) { return "Internal default"; });
            ReadBoardUpdateUiState enUpdate = MainForm.ResolveWebViewUpdateState(
                retainedUpdate,
                delegate(string key)
                {
                    return key == "test.title" ? "Update check failed" : "Unknown error";
                },
                delegate(string key) { return "Internal default"; });

            Assert.Equal("检查更新失败", cnUpdate.Title);
            Assert.Equal("未知错误: socket closed", cnUpdate.Detail);
            Assert.Equal("Update check failed", enUpdate.Title);
            Assert.Equal("Unknown error: socket closed", enUpdate.Detail);

            ReadBoardDialogUiState retainedDialog = new ReadBoardDialogUiState
            {
                Open = true,
                TitleMessage = new SemanticMessage("test.title"),
                MessageMessage = new SemanticMessage("test.detail"),
                DetailMessage = new SemanticMessage("test.restore"),
                ConfirmLabelMessage = new SemanticMessage("test.confirm"),
                CancelLabelMessage = new SemanticMessage("test.cancel"),
                DontShowAgainLabelMessage = new SemanticMessage("test.dontShow")
            };
            ReadBoardDialogUiState enDialog = MainForm.ResolveWebViewDialogState(
                retainedDialog,
                delegate(string key)
                {
                    switch (key)
                    {
                        case "test.title": return "Update check failed";
                        case "test.detail": return "Unknown error";
                        case "test.restore": return "Restore move placement";
                        case "test.confirm": return "Confirm";
                        case "test.cancel": return "Cancel";
                        case "test.dontShow": return "Do not show again";
                        default: return key;
                    }
                },
                delegate(string key) { return "Internal default"; });

            Assert.Equal("Update check failed", enDialog.Title);
            Assert.Equal("Unknown error", enDialog.Message);
            Assert.Equal("Restore move placement", enDialog.Detail);
            Assert.Equal("Confirm", enDialog.ConfirmLabel);
            Assert.Equal("Cancel", enDialog.CancelLabel);
            Assert.Equal("Do not show again", enDialog.DontShowAgainLabel);
        }

        [Fact]
        public void UpdateAndIdentityLabels_RelocalizeFromSemanticSnapshotSources()
        {
            ReadBoardUpdateUiState update = MainForm.ResolveWebViewUpdateState(
                new ReadBoardUpdateUiState { Open = true, Status = "manual" },
                delegate(string key)
                {
                    switch (key)
                    {
                        case "MainForm_btnCheckUpdate": return "Check for updates";
                        case "Update_close": return "Close";
                        case "WebView_done": return "Done";
                        case "Update_currentVersion": return "Current version";
                        case "Update_latestVersion": return "Latest version";
                        case "Update_releaseDate": return "Release date";
                        case "Update_releaseNotes": return "Release notes";
                        case "Update_download": return "Download";
                        case "Update_downloadAndInstall": return "Download and install";
                        case "WebView_processing": return "Processing";
                        default: return key;
                    }
                },
                delegate(string key) { return "Internal default"; });

            Assert.Equal("Check for updates", update.DialogTitle);
            Assert.Equal("Close", update.CloseLabel);
            Assert.Equal("Done", update.DoneLabel);
            Assert.Equal("Current version", update.CurrentVersionLabel);
            Assert.Equal("Latest version", update.LatestVersionLabel);
            Assert.Equal("Release date", update.ReleaseDateLabel);
            Assert.Equal("Release notes", update.ReleaseNotesLabel);
            Assert.Equal("Download", update.DownloadLabel);
            Assert.Equal("Download and install", update.DownloadAndInstallLabel);
            Assert.Equal("Processing", update.ProcessingLabel);

            ReadBoardIdentityUiState identity = MainForm.ResolveWebViewIdentityState(
                new ReadBoardIdentityUiState
                {
                    Open = true,
                    HasSavedIdentity = true,
                    SavedId = "candidate-1",
                    Candidates = new List<ReadBoardIdentityCandidateUiState>
                    {
                        new ReadBoardIdentityCandidateUiState
                        {
                            Id = "candidate-1",
                            LabelMessage = SemanticMessage.Create("test.candidate")
                        }
                    }
                },
                delegate(string key)
                {
                    switch (key)
                    {
                        case "WebView_selectIdentity": return "Select Fox identity";
                        case "FoxAutoPlayIdentityDialog_lblPrompt": return "Choose your player row.";
                        case "FoxAutoPlayIdentityDialog_lblDetectedNicknames": return "Detected rows";
                        case "WebView_selectedIdentity": return "Selected:";
                        case "FoxAutoPlayIdentityDialog_noDetectedNicknames": return "No rows";
                        case "WebView_identityWindowHint": return "Show the Fox game window.";
                        case "FoxAutoPlayIdentityDialog_btnClearSavedIdentity": return "Clear saved";
                        case "FoxAutoPlayIdentityDialog_btnCancel": return "Cancel";
                        case "FoxAutoPlayIdentityDialog_btnUseOnce": return "Use once";
                        case "FoxAutoPlayIdentityDialog_btnSaveAndUse": return "Save and use";
                        case "WebView_unnamedCandidate": return "Unnamed";
                        case "WebView_saved": return "Saved";
                        case "WebView_candidateRow": return "Candidate row";
                        case "WebView_screenshot": return " screenshot";
                        case "test.candidate": return "Player row 1";
                        default: return key;
                    }
                },
                delegate(string key) { return "Internal default"; });

            Assert.Equal("Select Fox identity", identity.DialogTitle);
            Assert.Equal("Choose your player row.", identity.Prompt);
            Assert.Equal("Detected rows", identity.DetectedNicknamesLabel);
            Assert.Equal("Selected:", identity.SelectedLabel);
            Assert.Equal("No rows", identity.EmptyTitle);
            Assert.Equal("Show the Fox game window.", identity.WindowHint);
            Assert.Equal("Clear saved", identity.ClearSavedLabel);
            Assert.Equal("Cancel", identity.CancelLabel);
            Assert.Equal("Use once", identity.UseOnceLabel);
            Assert.Equal("Save and use", identity.SaveAndUseLabel);
            Assert.Equal("Saved", identity.SavedLabel);
            Assert.Equal("Player row 1", identity.Candidates[0].Label);
            Assert.Equal("Player row 1 screenshot", identity.Candidates[0].PreviewAlt);
        }

        [Fact]
        public void CreateLog_PreservesLevelSeparatelyFromSemanticContent()
        {
            SemanticMessage message = SemanticMessage.CreateLogWithDiagnostic(
                "WARN",
                "test.message",
                "diagnostic");

            Assert.Equal("test.message", message.Key);
            Assert.Equal("WARN", message.Level);
            Assert.Equal("diagnostic", message.DiagnosticDetail);
        }
    }
}
