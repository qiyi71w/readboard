using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace readboard
{
    public partial class MainForm
    {
        private const string WebViewManualDownloadUrl =
            "https://github.com/qiyi71w/readboard/releases/latest";

        private ReadBoardUpdateUiState webViewUpdateState = CreateClosedWebViewUpdateState();
        private UpdateCheckResult webViewUpdateResult;
        private Uri webViewManualDownloadUri = GetWebViewManualDownloadUri();
        private bool webViewUpdateInitialized;
        private int webViewUpdateGeneration;

        internal void InitializeWebViewUpdateBridge()
        {
            if (webViewUpdateInitialized)
                return;

            webViewUpdateInitialized = true;
        }

        internal ReadBoardUpdateUiState GetWebViewUpdateState()
        {
            return ResolveWebViewUpdateState(
                webViewUpdateState,
                getLangStr,
                Program.GetDefaultLanguageText);
        }

        internal static ReadBoardUpdateUiState ResolveWebViewUpdateState(
            ReadBoardUpdateUiState state,
            Func<string, string> getLocalizedText,
            Func<string, string> getDefaultText)
        {
            if (state == null)
                return null;

            SemanticMessage dialogTitleMessage = state.DialogTitleMessage
                ?? SemanticMessage.Create("MainForm_btnCheckUpdate");
            SemanticMessage closeLabelMessage = state.CloseLabelMessage
                ?? SemanticMessage.Create("Update_close");
            SemanticMessage doneLabelMessage = state.DoneLabelMessage
                ?? SemanticMessage.Create("WebView_done");
            SemanticMessage currentVersionLabelMessage = state.CurrentVersionLabelMessage
                ?? SemanticMessage.Create("Update_currentVersion");
            SemanticMessage latestVersionLabelMessage = state.LatestVersionLabelMessage
                ?? SemanticMessage.Create("Update_latestVersion");
            SemanticMessage releaseDateLabelMessage = state.ReleaseDateLabelMessage
                ?? SemanticMessage.Create("Update_releaseDate");
            SemanticMessage releaseNotesLabelMessage = state.ReleaseNotesLabelMessage
                ?? SemanticMessage.Create("Update_releaseNotes");
            SemanticMessage downloadLabelMessage = state.DownloadLabelMessage
                ?? SemanticMessage.Create("Update_download");
            SemanticMessage downloadAndInstallLabelMessage = state.DownloadAndInstallLabelMessage
                ?? SemanticMessage.Create("Update_downloadAndInstall");
            SemanticMessage processingLabelMessage = state.ProcessingLabelMessage
                ?? SemanticMessage.Create("WebView_processing");

            List<ReadBoardUpdateStepUiState> steps = null;
            if (state.Steps != null)
            {
                steps = new List<ReadBoardUpdateStepUiState>();
                foreach (ReadBoardUpdateStepUiState step in state.Steps)
                {
                    steps.Add(new ReadBoardUpdateStepUiState
                    {
                        LabelMessage = step.LabelMessage,
                        Label = ResolveWebViewUpdateText(
                            step.LabelMessage,
                            step.Label,
                            null,
                            getLocalizedText,
                            getDefaultText),
                        Status = step.Status
                    });
                }
            }

            return new ReadBoardUpdateUiState
            {
                Open = state.Open,
                Status = state.Status,
                InstallEnabled = state.InstallEnabled,
                OpenDownloadEnabled = state.OpenDownloadEnabled,
                CloseEnabled = state.CloseEnabled,
                CurrentVersion = state.CurrentVersion,
                LatestVersion = state.LatestVersion,
                ReleaseDate = ResolveWebViewUpdateText(
                    state.ReleaseDateMessage,
                    state.ReleaseDate,
                    null,
                    getLocalizedText,
                    getDefaultText),
                ReleaseDateMessage = state.ReleaseDateMessage,
                ReleaseNotes = ResolveWebViewUpdateText(
                    state.ReleaseNotesMessage,
                    state.ReleaseNotes,
                    state.ReleaseNotesMessages,
                    getLocalizedText,
                    getDefaultText),
                ReleaseNotesMessage = state.ReleaseNotesMessage,
                Title = ResolveWebViewUpdateText(
                    state.TitleMessage,
                    state.Title,
                    null,
                    getLocalizedText,
                    getDefaultText),
                TitleMessage = state.TitleMessage,
                DialogTitle = ResolveWebViewUpdateText(
                    dialogTitleMessage,
                    state.DialogTitle,
                    null,
                    getLocalizedText,
                    getDefaultText),
                DialogTitleMessage = dialogTitleMessage,
                CloseLabel = ResolveWebViewUpdateText(
                    closeLabelMessage,
                    state.CloseLabel,
                    null,
                    getLocalizedText,
                    getDefaultText),
                CloseLabelMessage = closeLabelMessage,
                DoneLabel = ResolveWebViewUpdateText(
                    doneLabelMessage,
                    state.DoneLabel,
                    null,
                    getLocalizedText,
                    getDefaultText),
                DoneLabelMessage = doneLabelMessage,
                CurrentVersionLabel = ResolveWebViewUpdateText(
                    currentVersionLabelMessage,
                    state.CurrentVersionLabel,
                    null,
                    getLocalizedText,
                    getDefaultText),
                CurrentVersionLabelMessage = currentVersionLabelMessage,
                LatestVersionLabel = ResolveWebViewUpdateText(
                    latestVersionLabelMessage,
                    state.LatestVersionLabel,
                    null,
                    getLocalizedText,
                    getDefaultText),
                LatestVersionLabelMessage = latestVersionLabelMessage,
                ReleaseDateLabel = ResolveWebViewUpdateText(
                    releaseDateLabelMessage,
                    state.ReleaseDateLabel,
                    null,
                    getLocalizedText,
                    getDefaultText),
                ReleaseDateLabelMessage = releaseDateLabelMessage,
                ReleaseNotesLabel = ResolveWebViewUpdateText(
                    releaseNotesLabelMessage,
                    state.ReleaseNotesLabel,
                    null,
                    getLocalizedText,
                    getDefaultText),
                ReleaseNotesLabelMessage = releaseNotesLabelMessage,
                DownloadLabel = ResolveWebViewUpdateText(
                    downloadLabelMessage,
                    state.DownloadLabel,
                    null,
                    getLocalizedText,
                    getDefaultText),
                DownloadLabelMessage = downloadLabelMessage,
                DownloadAndInstallLabel = ResolveWebViewUpdateText(
                    downloadAndInstallLabelMessage,
                    state.DownloadAndInstallLabel,
                    null,
                    getLocalizedText,
                    getDefaultText),
                DownloadAndInstallLabelMessage = downloadAndInstallLabelMessage,
                ProcessingLabel = ResolveWebViewUpdateText(
                    processingLabelMessage,
                    state.ProcessingLabel,
                    null,
                    getLocalizedText,
                    getDefaultText),
                ProcessingLabelMessage = processingLabelMessage,
                Detail = ResolveWebViewUpdateText(
                    state.DetailMessage,
                    state.Detail,
                    state.DetailMessages,
                    getLocalizedText,
                    getDefaultText),
                DetailMessage = state.DetailMessage,
                DetailMessages = state.DetailMessages,
                Message = ResolveWebViewUpdateText(
                    state.MessageMessage,
                    state.Message,
                    null,
                    getLocalizedText,
                    getDefaultText),
                MessageMessage = state.MessageMessage,
                ErrorTitle = ResolveWebViewUpdateText(
                    state.ErrorTitleMessage,
                    state.ErrorTitle,
                    null,
                    getLocalizedText,
                    getDefaultText),
                ErrorTitleMessage = state.ErrorTitleMessage,
                Error = ResolveWebViewUpdateText(
                    state.ErrorMessage,
                    state.Error,
                    null,
                    getLocalizedText,
                    getDefaultText),
                ErrorMessage = state.ErrorMessage,
                ReleaseNotesMessages = state.ReleaseNotesMessages,
                Progress = state.Progress,
                Steps = steps
            };
        }

        private static string ResolveWebViewUpdateText(
            SemanticMessage message,
            string fallback,
            IReadOnlyList<SemanticMessage> appendedMessages,
            Func<string, string> getLocalizedText,
            Func<string, string> getDefaultText)
        {
            string result = message == null
                ? fallback
                : SemanticMessageResolver.Resolve(message, getLocalizedText, getDefaultText);
            if (appendedMessages == null)
                return result;

            foreach (SemanticMessage appendedMessage in appendedMessages)
            {
                string appended = SemanticMessageResolver.Resolve(
                    appendedMessage,
                    getLocalizedText,
                    getDefaultText);
                if (string.IsNullOrWhiteSpace(appended))
                    continue;
                result = string.IsNullOrWhiteSpace(result)
                    ? appended
                    : result + Environment.NewLine + Environment.NewLine + appended;
            }
            return result;
        }

        private Task CheckForWebViewUpdateAsync()
        {
            if (webViewUpdateState.Open
                && (webViewUpdateState.Status == "checking"
                    || webViewUpdateState.Status == "processing"))
                return Task.CompletedTask;

            return webViewUpdateCheckJourney.StartAsync(
                token => updateChecker.CheckAsync(token),
                CanOfferWebViewInstallForCurrentProcess);
        }

        private void OnWebViewUpdateCheckObservation(WebViewUpdateCheckObservation observation)
        {
            if (observation == null)
                throw new ArgumentNullException(nameof(observation));

            switch (observation.Kind)
            {
                case WebViewUpdateCheckObservationKind.Started:
                    InitializeWebViewUpdateBridge();
                    webViewUpdateResult = null;
                    webViewManualDownloadUri = GetWebViewManualDownloadUri();
                    webViewUpdateState = new ReadBoardUpdateUiState
                    {
                        Open = true,
                        Status = "checking",
                        CloseEnabled = true,
                        CurrentVersion = AppReleaseVersion.GetCurrentVersion(),
                        TitleMessage = SemanticMessage.Create("MainForm_btnCheckUpdate_Checking"),
                        DetailMessage = SemanticMessage.Create("WebView_updateFetching")
                    };
                    PostWebViewState();
                    return;
                case WebViewUpdateCheckObservationKind.Completed:
                    webViewManualDownloadUri = ResolveWebViewManualDownloadUri(observation.Result);
                    ApplyWebViewUpdateCheckResult(
                        observation.Result,
                        observation.HostedInstallAvailable);
                    return;
                case WebViewUpdateCheckObservationKind.Failed:
                    Trace.TraceError(observation.Exception.ToString());
                    webViewUpdateState = CreateWebViewUpdateCheckFailedState(
                        SemanticMessage.Create("Update_checkFailed"),
                        SemanticMessage.CreateWithDiagnostic(
                            "Update_unknownError",
                            string.IsNullOrWhiteSpace(observation.Exception.Message)
                                ? null
                                : observation.Exception.Message.Trim()));
                    PostWebViewState();
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(observation.Kind));
            }
        }

        internal bool CloseWebViewUpdate()
        {
            if (!webViewUpdateState.Open &&
                string.Equals(webViewUpdateState.Status, "closed", StringComparison.Ordinal))
                return false;

            bool processing = string.Equals(
                webViewUpdateState.Status,
                "processing",
                StringComparison.Ordinal);
            if (webViewUpdateState.Open && !webViewUpdateState.CloseEnabled)
                return true;

            if (independentUpdateJourney != null && independentUpdateJourney.Cancel())
                return false;
            if (processing && independentUpdateJourney != null)
                return true;

            int journeyGeneration = independentUpdateJourney == null ? 0 : independentUpdateJourney.Generation;
            if (journeyGeneration >= webViewUpdateGeneration)
                webViewUpdateGeneration = journeyGeneration + 1;
            webViewUpdateCheckJourney.Cancel();
            webViewUpdateState = CreateClosedWebViewUpdateState();
            return true;
        }
        internal static bool ShouldPublishWebViewUpdateOpenDownloadRejection(
            bool open,
            bool openDownloadEnabled)
        {
            return !open || !openDownloadEnabled;
        }

        internal Task InstallWebViewUpdateAsync()
        {
            if (!webViewUpdateState.Open || webViewUpdateState.Status != "available")
            {
                PostWebViewState();
                return Task.CompletedTask;
            }
            InitializeWebViewUpdateBridge();
            UpdateCheckResult result = webViewUpdateResult;
            if (!CanOfferWebViewInstallForCurrentProcess(result))
            {
                OpenWebViewUpdateDownload();
                return Task.CompletedTask;
            }

            if (System.Windows.Forms.MessageBox.Show(this,
                getLangStr("IndependentUpdate_confirm"), getLangStr("IndependentUpdate_caption"),
                System.Windows.Forms.MessageBoxButtons.OKCancel,
                System.Windows.Forms.MessageBoxIcon.Warning) != System.Windows.Forms.DialogResult.OK)
                return Task.CompletedTask;
            HostedUpdateRequest request = new HostedUpdateRequest(
                result.Tag,
                result.AssetName,
                result.AssetDownloadUrl,
                result.AssetSha256);
            return independentUpdateJourney.StartAsync(request);
        }

        internal void OpenWebViewUpdateDownload()
        {
            try
            {
                using (Process process = Process.Start(
                    CreateWebViewDownloadStartInfo(webViewManualDownloadUri)))
                {
                }
            }
            catch (Exception exception)
            {
                Trace.TraceError(exception.ToString());
                ActivateWebViewManualDownloadFallback(
                    SemanticMessage.Create("Update_openDownloadFailed"));
            }
        }

        private void OnIndependentUpdateObservation(IndependentUpdateObservation observation)
        {
            if (observation == null) return;
            InvokeUiHostAction(delegate
            {
                if (isShuttingDown || IsDisposed || Disposing) return;
                if (observation.Stage != IndependentUpdateStage.Committed && observation.Generation < webViewUpdateGeneration) return;
                webViewUpdateGeneration = observation.Generation;
                switch (observation.Stage)
                {
                    case IndependentUpdateStage.Downloading:
                        SetWebViewUpdateProcessing(observation.Message, 0);
                        break;
                    case IndependentUpdateStage.Verifying:
                        SetWebViewUpdateProcessing(observation.Message, 1);
                        break;
                    case IndependentUpdateStage.Preparing:
                        SetWebViewUpdateProcessing(observation.Message, 2);
                        break;
                    case IndependentUpdateStage.Committed:
                        SetWebViewUpdateProcessing(observation.Message, 3);
                        shutdown();
                        break;
                    case IndependentUpdateStage.Cancelled:
                        webViewUpdateState = CreateClosedWebViewUpdateState();
                        PostWebViewState();
                        break;
                    case IndependentUpdateStage.Failed:
                        ActivateWebViewManualDownloadFallback(observation.Message);
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(observation.Stage));
                }
            });
        }

        internal void DisposeWebViewUpdateBridge()
        {
            independentUpdateJourney?.Dispose();
            webViewUpdateCheckJourney.Cancel();
        }

        internal static bool CanOfferWebViewInstall(UpdateCheckResult result)
        {
            return result != null && result.Status == UpdateCheckStatus.UpdateAvailable &&
                !string.IsNullOrWhiteSpace(result.Tag) &&
                result.AssetName == "readboard-webview2-" + result.Tag + ".zip" &&
                Uri.TryCreate(result.AssetDownloadUrl, UriKind.Absolute, out Uri download) &&
                download.Scheme == Uri.UriSchemeHttps &&
                System.Text.RegularExpressions.Regex.IsMatch(result.AssetSha256 ?? "", "^[0-9a-f]{64}$");
        }

        internal static Uri GetWebViewManualDownloadUri()
        {
            return new Uri(WebViewManualDownloadUrl, UriKind.Absolute);
        }
        internal static ProcessStartInfo CreateWebViewDownloadStartInfo(Uri uri)
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri));

            return new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            };
        }

        internal static Uri ResolveWebViewManualDownloadUri(UpdateCheckResult result)
        {
            Uri uri;
            if (result != null
                && Uri.TryCreate(result.ReleaseUrl, UriKind.Absolute, out uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath.StartsWith("/qiyi71w/readboard/releases/", StringComparison.OrdinalIgnoreCase))
                return uri;
            return GetWebViewManualDownloadUri();
        }

        private void ApplyWebViewUpdateCheckResult(
            UpdateCheckResult result,
            bool hostedInstallAvailable)
        {
            if (result == null)
                throw new ArgumentNullException("result");
            webViewUpdateResult = result;
            webViewUpdateState = ResolveWebViewUpdateCheckResultState(
                result,
                hostedInstallAvailable);
            PostWebViewState();
        }

        internal static ReadBoardUpdateUiState ResolveWebViewUpdateCheckResultState(
            UpdateCheckResult result,
            bool hostedInstallAvailable)
        {
            if (result == null)
                throw new ArgumentNullException("result");

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                {
                    List<SemanticMessage> channelMessages = BuildWebViewChannelMessages(result);
                    string releaseNotes = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                        ? null
                        : result.ReleaseNotes.Trim();
                    return new ReadBoardUpdateUiState
                    {
                        Open = true,
                        Status = hostedInstallAvailable ? "available" : "manual",
                        InstallEnabled = hostedInstallAvailable,
                        OpenDownloadEnabled = !hostedInstallAvailable,
                        CloseEnabled = true,
                        CurrentVersion = result.CurrentVersion,
                        LatestVersion = result.LatestVersion,
                        ReleaseDate = result.PublishedAt.HasValue
                            ? result.PublishedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : null,
                        ReleaseDateMessage = result.PublishedAt.HasValue
                            ? null
                            : SemanticMessage.Create("Update_notProvided"),
                        ReleaseNotes = releaseNotes,
                        ReleaseNotesMessage = releaseNotes == null
                            ? SemanticMessage.Create("Update_releaseNotesUnavailable")
                            : null,
                        ReleaseNotesMessages = channelMessages,
                        TitleMessage = hostedInstallAvailable
                            ? null
                            : SemanticMessage.Create("IndependentUpdate_manualOnly"),
                        DetailMessages = channelMessages,
                        MessageMessage = hostedInstallAvailable
                            ? null
                            : SemanticMessage.Create("WebView_manualDownload")
                    };
                }
                case UpdateCheckStatus.UpToDate:
                {
                    string upToDateKey = string.Equals(
                        result.ChannelStatus,
                        "retired",
                        StringComparison.Ordinal)
                        ? "Update_upToDateRetired"
                        : "Update_upToDate";
                    SemanticMessage upToDateMessage = SemanticMessage.Create(upToDateKey);
                    return new ReadBoardUpdateUiState
                    {
                        Open = true,
                        Status = "latest",
                        CloseEnabled = true,
                        CurrentVersion = result.CurrentVersion,
                        LatestVersion = result.LatestVersion,
                        TitleMessage = upToDateMessage,
                        DetailMessage = upToDateMessage,
                        DetailMessages = BuildWebViewChannelMessages(result),
                        Message = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture)
                    };
                }
                case UpdateCheckStatus.OutsideChannel:
                    return CreateWebViewUpdateNoticeState(
                        result,
                        SemanticMessage.Create("Update_outsideChannel"),
                        BuildWebViewChannelMessages(result));
                case UpdateCheckStatus.NoMatchingChannel:
                    return CreateWebViewUpdateNoticeState(
                        result,
                        SemanticMessage.Create("Update_noMatchingChannel"),
                        BuildWebViewChannelMessages(result));
                case UpdateCheckStatus.Failed:
                    return CreateWebViewUpdateCheckFailedState(
                        SemanticMessage.Create("Update_checkFailed"),
                        SemanticMessage.CreateWithDiagnostic(
                            "Update_unknownError",
                            string.IsNullOrWhiteSpace(result.ErrorMessage)
                                ? null
                                : result.ErrorMessage.Trim()));
                default:
                    throw new ArgumentOutOfRangeException(nameof(result.Status));
            }
        }

        private static ReadBoardUpdateUiState CreateWebViewUpdateNoticeState(
            UpdateCheckResult result,
            SemanticMessage titleMessage,
            IReadOnlyList<SemanticMessage> detailMessages)
        {
            return new ReadBoardUpdateUiState
            {
                Open = true,
                Status = "notice",
                CloseEnabled = true,
                CurrentVersion = result.CurrentVersion,
                LatestVersion = result.LatestVersion,
                TitleMessage = titleMessage,
                DetailMessages = detailMessages
            };
        }

        private static ReadBoardUpdateUiState CreateWebViewUpdateCheckFailedState(
            SemanticMessage titleMessage,
            SemanticMessage detailMessage)
        {
            return new ReadBoardUpdateUiState
            {
                Open = true,
                CloseEnabled = true,
                Status = "check-failed",
                CurrentVersion = AppReleaseVersion.GetCurrentVersion(),
                TitleMessage = titleMessage,
                DetailMessage = detailMessage
            };
        }

        private static List<SemanticMessage> BuildWebViewChannelMessages(UpdateCheckResult result)
        {
            List<SemanticMessage> messages = new List<SemanticMessage>();
            if (string.Equals(result.ChannelStatus, "retired", StringComparison.Ordinal))
            {
                messages.Add(SemanticMessage.CreateWithDiagnostic(
                    "Update_retiredFinalVersion",
                    result.LatestVersion));
            }

            if (!string.IsNullOrWhiteSpace(result.IncompatibleNewerVersion)
                && !string.IsNullOrWhiteSpace(result.IncompatibleMinimumWindowsVersion))
            {
                messages.Add(SemanticMessage.CreateWithDiagnostic(
                    "Update_newerVersionRequiresWindows",
                    result.IncompatibleNewerVersion
                        + "; Windows "
                        + result.IncompatibleMinimumWindowsVersion));
            }
            return messages;
        }

        internal static bool IsWebViewUpdateProcessingCloseEnabled(int activeStep)
        {
            return activeStep < 3;
        }

        private void SetWebViewUpdateProcessing(
            SemanticMessage message,
            int activeStep)
        {
            webViewUpdateState = new ReadBoardUpdateUiState
            {
                Open = true,
                Status = "processing",
                CloseEnabled = IsWebViewUpdateProcessingCloseEnabled(activeStep),
                CurrentVersion = webViewUpdateResult == null ? null : webViewUpdateResult.CurrentVersion,
                LatestVersion = webViewUpdateResult == null ? null : webViewUpdateResult.LatestVersion,
                TitleMessage = message,
                DetailMessage = message,
                Steps = new[]
                {
                    CreateWebViewUpdateStep(SemanticMessage.Create("WebView_updateStepDownload"), activeStep, 0),
                    CreateWebViewUpdateStep(SemanticMessage.Create("WebView_updateStepVerify"), activeStep, 1),
                    CreateWebViewUpdateStep(SemanticMessage.Create("IndependentUpdate_preparing"), activeStep, 2),
                    CreateWebViewUpdateStep(SemanticMessage.Create("IndependentUpdate_installing"), activeStep, 3)
                }
            };
            PostWebViewState();
        }

        private static ReadBoardUpdateStepUiState CreateWebViewUpdateStep(
            SemanticMessage labelMessage,
            int activeStep,
            int step)
        {
            return new ReadBoardUpdateStepUiState
            {
                LabelMessage = labelMessage,
                Status = step < activeStep ? "done" : step == activeStep ? "active" : string.Empty
            };
        }

        private void ActivateWebViewManualDownloadFallback(SemanticMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            SemanticMessage headline = new SemanticMessage(message.Key, message.Arguments);
            webViewUpdateState = new ReadBoardUpdateUiState
            {
                Open = true,
                Status = "failed",
                OpenDownloadEnabled = true,
                CloseEnabled = true,
                CurrentVersion = webViewUpdateResult == null ? AppReleaseVersion.GetCurrentVersion() : webViewUpdateResult.CurrentVersion,
                LatestVersion = webViewUpdateResult == null ? null : webViewUpdateResult.LatestVersion,
                TitleMessage = headline,
                MessageMessage = SemanticMessage.Create("Update_manualDownloadFallback"),
                ErrorTitleMessage = headline,
                ErrorMessage = message
            };
            PostWebViewState();
        }

        private bool CanOfferWebViewInstallForCurrentProcess(UpdateCheckResult result)
        {
            return independentUpdateJourney != null && independentUpdateJourney.CanStart && CanOfferWebViewInstall(result);
        }

        private static ReadBoardUpdateUiState CreateClosedWebViewUpdateState()
        {
            return new ReadBoardUpdateUiState
            {
                Open = false,
                Status = "closed"
            };
        }


    }
}
