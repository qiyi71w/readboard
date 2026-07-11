using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace readboard
{
    public partial class MainForm
    {
        private const int WebViewHostedUpdateResponseTimeoutMilliseconds = 15000;
        private const string WebViewManualDownloadUrl =
            "https://github.com/qiyi71w/readboard/releases/latest";

        private readonly Timer webViewHostedUpdateResponseTimer = new Timer();
        private ReadBoardUpdateUiState webViewUpdateState = CreateClosedWebViewUpdateState();
        private UpdateCheckResult webViewUpdateResult;
        private Uri webViewManualDownloadUri = GetWebViewManualDownloadUri();
        private bool webViewUpdateInitialized;
        private bool webViewHostedInstallFallbackActive;
        private int webViewUpdateOperationId;

        internal void InitializeWebViewUpdateBridge()
        {
            if (webViewUpdateInitialized)
                return;

            webViewHostedUpdateResponseTimer.Interval =
                WebViewHostedUpdateResponseTimeoutMilliseconds;
            webViewHostedUpdateResponseTimer.Tick += WebViewHostedUpdateResponseTimer_Tick;
            webViewUpdateInitialized = true;
        }

        internal ReadBoardUpdateUiState GetWebViewUpdateState()
        {
            return webViewUpdateState;
        }

        internal async Task CheckForWebViewUpdateAsync()
        {
            if (webViewUpdateState.Open
                && (webViewUpdateState.Status == "checking"
                    || webViewUpdateState.Status == "processing"))
                return;
            int operationId = ++webViewUpdateOperationId;
            InitializeWebViewUpdateBridge();
            webViewHostedUpdateResponseTimer.Stop();
            webViewHostedInstallFallbackActive = false;
            webViewUpdateResult = null;
            webViewManualDownloadUri = GetWebViewManualDownloadUri();
            webViewUpdateState = new ReadBoardUpdateUiState
            {
                Open = true,
                Status = "checking",
                CurrentVersion = AppReleaseVersion.GetCurrentVersion(),
                Title = getLangStr("MainForm_btnCheckUpdate_Checking"),
                Detail = "正在获取最新版本信息…"
            };
            PostWebViewState();

            try
            {
                UpdateCheckResult result = await updateChecker.CheckAsync();
                if (operationId != webViewUpdateOperationId)
                    return;
                if (result == null)
                    throw new InvalidOperationException("Update check returned no result.");

                webViewUpdateResult = result;
                webViewManualDownloadUri = ResolveWebViewManualDownloadUri(result);
                ApplyWebViewUpdateCheckResult(result);
            }
            catch (Exception exception)
            {
                if (operationId != webViewUpdateOperationId)
                    return;
                Trace.TraceError(exception.ToString());
                ActivateWebViewManualDownloadFallback(
                    getLangStr("Update_checkFailed"),
                    NormalizeWebViewUpdateText(exception.Message, getLangStr("Update_unknownError")));
            }
        }

        internal void CloseWebViewUpdate()
        {
            webViewUpdateOperationId++;
            webViewHostedUpdateResponseTimer.Stop();
            webViewUpdateState.Open = false;
            PostWebViewState();
        }

        internal async Task InstallWebViewUpdateAsync()
        {
            if (!webViewUpdateState.Open || webViewUpdateState.Status != "available")
                return;
            int operationId = ++webViewUpdateOperationId;
            InitializeWebViewUpdateBridge();
            UpdateCheckResult result = webViewUpdateResult;
            if (!CanOfferWebViewHostedInstall(
                launchOptions.TransportKind,
                sessionCoordinator.IsProtocolSessionActive,
                hostedUpdateSupported,
                result))
            {
                OpenWebViewUpdateDownload();
                return;
            }

            SetWebViewUpdateProcessing(
                getLangStr("Update_downloadingPackage"),
                0);
            try
            {
                HostedUpdatePackageDownloader downloader = new HostedUpdatePackageDownloader();
                string zipPath = await downloader.DownloadAsync(
                    result.Tag,
                    result.AssetName,
                    result.AssetDownloadUrl);
                if (operationId != webViewUpdateOperationId)
                    return;

                SetWebViewUpdateProcessing(
                    getLangStr("Update_verifyingPackage"),
                    1);
                new HostedUpdatePackageVerifier().Verify(result.Tag, zipPath);

                SetWebViewUpdateProcessing(
                    getLangStr("Update_notifyingHost"),
                    2);
                sessionCoordinator.SendReadboardUpdateReady(result.Tag, zipPath);

                SetWebViewUpdateProcessing(
                    getLangStr("Update_waitingForHostInstall"),
                    2);
                webViewHostedUpdateResponseTimer.Stop();
                webViewHostedUpdateResponseTimer.Start();
            }
            catch (Exception exception)
            {
                if (operationId != webViewUpdateOperationId)
                    return;
                Trace.TraceError(exception.ToString());
                ActivateWebViewManualDownloadFallback(
                    getLangStr("Update_hostFailed"),
                    SanitizeWebViewHostedDetail(exception.Message));
            }
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
                    getLangStr("Update_openDownloadFailed"),
                    string.Empty);
            }
        }

        internal void MarkWebViewHostedUpdateInstalling()
        {
            if (!IsWebViewHostedUpdatePending())
                return;

            webViewHostedUpdateResponseTimer.Stop();
            SetWebViewUpdateProcessing(
                getLangStr("Update_hostInstalling"),
                3);
        }

        internal void MarkWebViewHostedUpdateCancelled()
        {
            if (!IsWebViewHostedUpdatePending())
                return;

            ActivateWebViewManualDownloadFallback(
                getLangStr("Update_hostCancelled"),
                string.Empty);
        }

        internal void MarkWebViewHostedUpdateFailed(string message)
        {
            if (!IsWebViewHostedUpdatePending())
                return;

            ActivateWebViewManualDownloadFallback(
                getLangStr("Update_hostFailed"),
                SanitizeWebViewHostedDetail(message));
        }

        internal void DisposeWebViewUpdateBridge()
        {
            webViewUpdateOperationId++;
            webViewHostedUpdateResponseTimer.Stop();
            webViewHostedUpdateResponseTimer.Dispose();
        }

        internal static bool CanOfferWebViewHostedInstall(
            TransportKind transportKind,
            bool protocolSessionActive,
            bool hostSupportsUpdate,
            UpdateCheckResult result)
        {
            return transportKind == TransportKind.Pipe &&
                protocolSessionActive &&
                hostSupportsUpdate &&
                result != null &&
                !string.IsNullOrWhiteSpace(result.Tag) &&
                !string.IsNullOrWhiteSpace(result.AssetName) &&
                !string.IsNullOrWhiteSpace(result.AssetDownloadUrl);
        }

        internal static Uri GetWebViewManualDownloadUri()
        {
            return new Uri(WebViewManualDownloadUrl, UriKind.Absolute);
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

        internal static ProcessStartInfo CreateWebViewDownloadStartInfo(Uri uri)
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri));

            return new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            };
        }

        private void ApplyWebViewUpdateCheckResult(UpdateCheckResult result)
        {
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    bool hostedInstallAvailable = CanOfferWebViewHostedInstall(
                        launchOptions.TransportKind,
                        sessionCoordinator.IsProtocolSessionActive,
                        hostedUpdateSupported,
                        result);
                    webViewUpdateState = new ReadBoardUpdateUiState
                    {
                        Open = true,
                        Status = hostedInstallAvailable ? "available" : "manual",
                        CurrentVersion = result.CurrentVersion,
                        LatestVersion = result.LatestVersion,
                        ReleaseDate = result.PublishedAt.HasValue
                            ? result.PublishedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : getLangStr("Update_notProvided"),
                        ReleaseNotes = NormalizeWebViewUpdateText(
                            result.ReleaseNotes,
                            getLangStr("Update_releaseNotesUnavailable"))
                    };
                    break;
                case UpdateCheckStatus.UpToDate:
                    webViewUpdateState = new ReadBoardUpdateUiState
                    {
                        Open = true,
                        Status = "latest",
                        CurrentVersion = result.CurrentVersion,
                        LatestVersion = result.LatestVersion,
                        Detail = getLangStr("Update_upToDate"),
                        Message = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture)
                    };
                    break;
                case UpdateCheckStatus.Failed:
                    ActivateWebViewManualDownloadFallback(
                        getLangStr("Update_checkFailed"),
                        NormalizeWebViewUpdateText(
                            result.ErrorMessage,
                            getLangStr("Update_unknownError")));
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result.Status));
            }

            PostWebViewState();
        }

        private void SetWebViewUpdateProcessing(string detail, int activeStep)
        {
            webViewUpdateState = new ReadBoardUpdateUiState
            {
                Open = true,
                Status = "processing",
                CurrentVersion = webViewUpdateResult == null ? null : webViewUpdateResult.CurrentVersion,
                LatestVersion = webViewUpdateResult == null ? null : webViewUpdateResult.LatestVersion,
                Title = detail,
                Detail = detail,
                Steps = new[]
                {
                    CreateWebViewUpdateStep("下载更新包", activeStep, 0),
                    CreateWebViewUpdateStep("校验更新包", activeStep, 1),
                    CreateWebViewUpdateStep("通知宿主", activeStep, 2),
                    CreateWebViewUpdateStep("宿主安装", activeStep, 3)
                }
            };
            PostWebViewState();
        }

        private static ReadBoardUpdateStepUiState CreateWebViewUpdateStep(
            string label,
            int activeStep,
            int step)
        {
            return new ReadBoardUpdateStepUiState
            {
                Label = label,
                Status = step < activeStep ? "done" : step == activeStep ? "active" : string.Empty
            };
        }

        private bool IsWebViewHostedUpdatePending()
        {
            return webViewUpdateState.Open &&
                string.Equals(webViewUpdateState.Status, "processing", StringComparison.Ordinal) &&
                !webViewHostedInstallFallbackActive;
        }

        private void ActivateWebViewManualDownloadFallback(string headline, string detail)
        {
            webViewHostedUpdateResponseTimer.Stop();
            webViewHostedInstallFallbackActive = true;
            webViewUpdateState = new ReadBoardUpdateUiState
            {
                Open = true,
                Status = "failed",
                CurrentVersion = webViewUpdateResult == null ? AppReleaseVersion.GetCurrentVersion() : webViewUpdateResult.CurrentVersion,
                LatestVersion = webViewUpdateResult == null ? null : webViewUpdateResult.LatestVersion,
                Title = headline,
                Message = getLangStr("Update_manualDownloadFallback"),
                ErrorTitle = headline,
                Error = detail
            };
            PostWebViewState();
        }

        private void WebViewHostedUpdateResponseTimer_Tick(object sender, EventArgs e)
        {
            webViewHostedUpdateResponseTimer.Stop();
            ActivateWebViewManualDownloadFallback(
                getLangStr("Update_hostTimedOut"),
                string.Empty);
        }

        private static ReadBoardUpdateUiState CreateClosedWebViewUpdateState()
        {
            return new ReadBoardUpdateUiState
            {
                Open = false,
                Status = "closed"
            };
        }

        private static string NormalizeWebViewUpdateText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string SanitizeWebViewHostedDetail(string detail)
        {
            return string.IsNullOrWhiteSpace(detail)
                ? string.Empty
                : detail.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        }
    }
}
