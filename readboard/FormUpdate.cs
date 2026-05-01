using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace readboard
{
    public partial class FormUpdate : Form
    {
        private const string DefaultText = "N/A";
        private const string DefaultReleaseNotes = "No release notes.";
        private const string DefaultMissingDownloadUrlMessage = "Download link is unavailable.";
        private const string DefaultInvalidDownloadUrlFormatMessage = "Download link format is invalid.";
        private const string DefaultUnsupportedDownloadUrlSchemeMessage =
            "Download link must use http or https.";
        private const string DefaultOpenDownloadUrlFailedMessage = "Unable to open the download link.";
        private const string DefaultDownloadButtonText = "Download";
        private const string DefaultDownloadAndInstallButtonText = "Download and Install";
        private const string DefaultDownloadingButtonText = "Downloading...";
        private const string DefaultWaitingForHostInstallText = "Waiting for Host Install...";
        private const string DefaultHostCancelledText = "Host installation was cancelled.";
        private const string DefaultHostFailedText = "Host installation failed.";
        private const string DefaultHostTimedOutText = "Host did not respond in time.";
        private const string DefaultManualDownloadFallbackMessage =
            "Falling back to manual download. Click Download to open the release page.";
        private const string DefaultDialogTitle = "Update";
        private const int HostedUpdateResponseTimeoutMilliseconds = 15000;
        private static readonly Size UpdateDefaultClientSize = new Size(640, 419);
        private static readonly Size UpdateMinimumClientSize = new Size(560, 420);

        private readonly UpdateDialogModel model;
        private readonly Timer hostedUpdateResponseTimer = new Timer();
        private bool isApplyingUpdateLayout;
        private bool hostedInstallFallbackActive;

        public FormUpdate(UpdateDialogModel model)
        {
            if (model == null)
            {
                throw new ArgumentNullException("model");
            }

            this.model = model;
            InitializeComponent();
            ApplyModel();
            ApplyUpdateFormUi();
            InitializeHostedUpdateState();
        }

        private void ApplyModel()
        {
            lblCurrentVersionValue.Text = NormalizeValue(
                UpdateDialogFormatter.FormatVersion(model.CurrentVersion),
                model.UnavailableText);
            lblLatestVersionValue.Text = NormalizeValue(
                UpdateDialogFormatter.FormatVersion(model.LatestVersion),
                model.UnavailableText);
            lblReleaseDateValue.Text = NormalizeValue(
                UpdateDialogFormatter.FormatReleaseDate(model.PublishedAt),
                model.UnavailableText);
            txtReleaseNotes.Text = NormalizeReleaseNotes(
                UpdateDialogFormatter.FormatReleaseNotes(model.ReleaseNotes),
                model.EmptyReleaseNotesText);
        }

        private static string NormalizeReleaseNotes(string value, string fallbackText)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return NormalizeFallbackText(fallbackText, DefaultReleaseNotes);
            }

            return value.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }

        private static string NormalizeValue(string value, string fallbackText)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return NormalizeFallbackText(fallbackText, DefaultText);
            }

            return value.Trim();
        }

        private static string NormalizeFallbackText(string value, string defaultValue)
        {
            return string.IsNullOrWhiteSpace(value)
                ? defaultValue
                : value.Trim();
        }

        private string GetDialogTitle()
        {
            return string.IsNullOrWhiteSpace(Text)
                ? DefaultDialogTitle
                : Text;
        }

        private void ApplyUpdateFormUi()
        {
            if (isApplyingUpdateLayout)
                return;

            isApplyingUpdateLayout = true;
            SuspendLayout();
            try
            {
                DoubleBuffered = true;
                AcceptButton = btnDownload;
                CancelButton = btnClose;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ConstrainUpdateDialogSize();
                if (Program.uiThemeMode == Program.UiThemeOptimized)
                    ApplyOptimizedUpdateTheme();
                else
                    ApplyClassicUpdateTheme();
                ApplyInfoPanelLayout();
            }
            finally
            {
                ResumeLayout(false);
                PerformLayout();
                isApplyingUpdateLayout = false;
            }
        }

        private void InitializeHostedUpdateState()
        {
            hostedUpdateResponseTimer.Interval = HostedUpdateResponseTimeoutMilliseconds;
            hostedUpdateResponseTimer.Tick += HostedUpdateResponseTimer_Tick;
            btnDownload.Enabled = true;
            btnDownload.Text = GetInitialDownloadButtonText();
        }

        private string GetInitialDownloadButtonText()
        {
            return CanUseHostedInstall()
                ? NormalizeFallbackText(model.DownloadAndInstallButtonText, DefaultDownloadAndInstallButtonText)
                : NormalizeFallbackText(model.DownloadButtonText, DefaultDownloadButtonText);
        }

        private bool CanUseHostedInstall()
        {
            return model.HostedInstallAvailable &&
                !hostedInstallFallbackActive &&
                model.PrepareHostedUpdateAsync != null &&
                model.NotifyHostedUpdateReady != null &&
                !string.IsNullOrWhiteSpace(model.HostedReleaseTag) &&
                !string.IsNullOrWhiteSpace(model.HostedAssetName) &&
                !string.IsNullOrWhiteSpace(model.HostedAssetDownloadUrl);
        }

        private void ApplyOptimizedUpdateTheme()
        {
            UiTheme.ApplyWindow(this);
            rootPanel.BackColor = UiTheme.WindowBackground;
            infoPanel.BackColor = UiTheme.SurfaceBackground;
            buttonPanel.BackColor = UiTheme.WindowBackground;
            StyleUpdateTitle(UiTheme.PrimaryText, new Font(UiTheme.BodyFont.FontFamily, 12F, FontStyle.Bold, GraphicsUnit.Point));
            StyleUpdateFields(UiTheme.SecondaryText, UiTheme.PrimaryText, UiTheme.BodyFont);
            UiTheme.StyleInput(txtReleaseNotes);
            UiTheme.StyleSecondaryButton(btnClose);
            UiTheme.StylePrimaryButton(btnDownload);
        }

        private void ApplyClassicUpdateTheme()
        {
            BackColor = SystemColors.Control;
            ForeColor = SystemColors.ControlText;
            Font = Control.DefaultFont;
            rootPanel.BackColor = SystemColors.Control;
            infoPanel.BackColor = SystemColors.Control;
            buttonPanel.BackColor = SystemColors.Control;
            StyleUpdateTitle(SystemColors.ControlText, new Font(Control.DefaultFont, FontStyle.Bold));
            StyleUpdateFields(SystemColors.ControlText, SystemColors.ControlText, Control.DefaultFont);
            txtReleaseNotes.BackColor = SystemColors.Window;
            txtReleaseNotes.ForeColor = SystemColors.WindowText;
            txtReleaseNotes.Font = Control.DefaultFont;
            txtReleaseNotes.BorderStyle = BorderStyle.Fixed3D;
            ResetButtonTheme(btnClose);
            ResetButtonTheme(btnDownload);
        }

        private void StyleUpdateTitle(Color foreColor, Font font)
        {
            lblTitle.BackColor = Color.Transparent;
            lblTitle.ForeColor = foreColor;
            lblTitle.Font = font;
        }

        private void StyleUpdateFields(Color labelColor, Color valueColor, Font font)
        {
            foreach (Label label in new[] { lblCurrentVersion, lblLatestVersion, lblReleaseDate, lblReleaseNotes })
                StyleUpdateLabel(label, labelColor, font);
            foreach (Label label in new[] { lblCurrentVersionValue, lblLatestVersionValue, lblReleaseDateValue })
                StyleUpdateLabel(label, valueColor, font);
        }

        private static void StyleUpdateLabel(Label label, Color foreColor, Font font)
        {
            label.BackColor = Color.Transparent;
            label.ForeColor = foreColor;
            label.Font = font;
            label.BorderStyle = BorderStyle.None;
            label.Padding = Padding.Empty;
        }

        private void ApplyInfoPanelLayout()
        {
            infoPanel.AutoSize = true;
            infoPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            UpdateDialogLayoutMetrics.EnsureInfoRowCapacity(infoPanel.RowStyles.Count);
            ConfigureInfoRow(0, lblCurrentVersion, lblCurrentVersionValue);
            ConfigureInfoRow(1, lblLatestVersion, lblLatestVersionValue);
            ConfigureInfoRow(2, lblReleaseDate, lblReleaseDateValue);
        }

        private void ConfigureInfoRow(int rowIndex, Label label, Label valueLabel)
        {
            infoPanel.RowStyles[rowIndex].SizeType = SizeType.Absolute;
            infoPanel.RowStyles[rowIndex].Height = UpdateDialogLayoutMetrics.CalculateInfoRowHeight(
                label.PreferredHeight,
                valueLabel.PreferredHeight);
        }

        private static void ResetButtonTheme(Button button)
        {
            button.FlatStyle = FlatStyle.System;
            button.UseVisualStyleBackColor = true;
            button.Font = Control.DefaultFont;
            button.Cursor = Cursors.Default;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyUpdateFormUi();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            ApplyUpdateFormUi();
        }

        private void ConstrainUpdateDialogSize()
        {
            Rectangle workingArea = GetCurrentWorkingArea();
            Size defaultSize = ScaleSize(UpdateDefaultClientSize);
            Size minimumSize = ScaleSize(UpdateMinimumClientSize);
            int availableWidth = Math.Max(ScaleValue(360), workingArea.Width - ScaleValue(48));
            int availableHeight = Math.Max(ScaleValue(280), workingArea.Height - ScaleValue(64));
            int minimumWidth = Math.Min(minimumSize.Width, availableWidth);
            int minimumHeight = Math.Min(minimumSize.Height, availableHeight);
            ClientSize = new Size(
                Math.Max(minimumWidth, Math.Min(defaultSize.Width, availableWidth)),
                Math.Max(minimumHeight, Math.Min(defaultSize.Height, availableHeight)));
            MinimumSize = new Size(minimumWidth, minimumHeight);
        }

        private Rectangle GetCurrentWorkingArea()
        {
            Point referencePoint = IsHandleCreated
                ? new Point(Left + Width / 2, Top + Height / 2)
                : new Point(Location.X, Location.Y);
            return DisplayScaling.GetScreenWorkingAreaFromPoint(referencePoint);
        }

        private int ScaleValue(int logicalValue)
        {
            double scale = IsHandleCreated
                ? DisplayScaling.GetScaleForWindow(Handle)
                : DisplayScaling.DefaultScale;
            return (int)Math.Round(logicalValue * DisplayScaling.NormalizeScale(scale));
        }

        private Size ScaleSize(Size logicalSize)
        {
            return new Size(ScaleValue(logicalSize.Width), ScaleValue(logicalSize.Height));
        }

        private async void btnDownload_Click(object sender, EventArgs e)
        {
            if (CanUseHostedInstall())
            {
                await BeginHostedInstallAsync();
                return;
            }

            OpenManualDownload();
        }

        private async Task BeginHostedInstallAsync()
        {
            UpdateDownloadButtonState(false, model.DownloadingButtonText, DefaultDownloadingButtonText);

            try
            {
                string zipPath = await model.PrepareHostedUpdateAsync(model);
                if (IsDisposed || Disposing)
                    return;

                model.NotifyHostedUpdateReady(model.HostedReleaseTag, zipPath);
                UpdateDownloadButtonState(false, model.WaitingForHostInstallText, DefaultWaitingForHostInstallText);
                hostedUpdateResponseTimer.Stop();
                hostedUpdateResponseTimer.Start();
            }
            catch (Exception exception)
            {
                Trace.TraceError(exception.ToString());
                ActivateManualDownloadFallback(
                    BuildHostedUpdateMessage(
                        exception.Message,
                        null,
                        model.ManualDownloadFallbackMessage,
                        DefaultManualDownloadFallbackMessage),
                    MessageBoxIcon.Error);
            }
        }

        private void OpenManualDownload()
        {
            Uri downloadUri;
            string validationMessage;
            if (!TryCreateDownloadUri(out downloadUri, out validationMessage))
            {
                MessageBox.Show(
                    this,
                    validationMessage,
                    GetDialogTitle(),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                OpenDownloadUri(downloadUri);
            }
            catch (Exception exception)
            {
                Trace.TraceError(exception.ToString());
                MessageBox.Show(
                    this,
                    NormalizeFallbackText(
                        model.OpenDownloadUrlFailedMessage,
                        DefaultOpenDownloadUrlFailedMessage),
                    GetDialogTitle(),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        internal void MarkHostedInstalling()
        {
            if (IsDisposed || Disposing || hostedInstallFallbackActive)
                return;

            hostedUpdateResponseTimer.Stop();
            UpdateDownloadButtonState(false, model.WaitingForHostInstallText, DefaultWaitingForHostInstallText);
        }

        internal void MarkHostedCancelled()
        {
            if (IsDisposed || Disposing || hostedInstallFallbackActive)
                return;

            ActivateManualDownloadFallback(
                BuildHostedUpdateMessage(
                    model.HostCancelledText,
                    null,
                    model.ManualDownloadFallbackMessage,
                    DefaultManualDownloadFallbackMessage),
                MessageBoxIcon.Information);
        }

        internal void MarkHostedFailed(string message)
        {
            if (IsDisposed || Disposing || hostedInstallFallbackActive)
                return;

            ActivateManualDownloadFallback(
                BuildHostedUpdateMessage(
                    model.HostFailedText,
                    SanitizeHostedDetail(message),
                    model.ManualDownloadFallbackMessage,
                    DefaultManualDownloadFallbackMessage),
                MessageBoxIcon.Error);
        }

        private void MarkHostedTimedOut()
        {
            if (IsDisposed || Disposing)
                return;

            ActivateManualDownloadFallback(
                BuildHostedUpdateMessage(
                    model.HostTimedOutText,
                    null,
                    model.ManualDownloadFallbackMessage,
                    DefaultManualDownloadFallbackMessage),
                MessageBoxIcon.Warning);
        }

        private void HostedUpdateResponseTimer_Tick(object sender, EventArgs e)
        {
            hostedUpdateResponseTimer.Stop();
            MarkHostedTimedOut();
        }

        private void ActivateManualDownloadFallback(string message, MessageBoxIcon icon)
        {
            hostedUpdateResponseTimer.Stop();
            hostedInstallFallbackActive = true;
            UpdateDownloadButtonState(true, model.DownloadButtonText, DefaultDownloadButtonText);

            if (!string.IsNullOrWhiteSpace(message))
            {
                MessageBox.Show(
                    this,
                    message,
                    GetDialogTitle(),
                    MessageBoxButtons.OK,
                    icon);
            }
        }

        internal static void OpenDownloadUri(Uri downloadUri)
        {
            using (Process process = Process.Start(CreateDownloadStartInfo(downloadUri)))
            {
            }
        }

        internal static ProcessStartInfo CreateDownloadStartInfo(Uri downloadUri)
        {
            if (downloadUri == null)
            {
                throw new ArgumentNullException(nameof(downloadUri));
            }

            return new ProcessStartInfo(downloadUri.AbsoluteUri)
            {
                UseShellExecute = true
            };
        }

        private bool TryCreateDownloadUri(out Uri downloadUri, out string validationMessage)
        {
            downloadUri = null;
            validationMessage = null;
            if (string.IsNullOrWhiteSpace(model.DownloadUrl))
            {
                validationMessage = NormalizeFallbackText(
                    model.MissingDownloadUrlMessage,
                    DefaultMissingDownloadUrlMessage);
                return false;
            }

            string normalizedUrl = model.DownloadUrl.Trim();
            Uri parsedUri;
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out parsedUri))
            {
                validationMessage = BuildDownloadUrlValidationMessage(
                    model.InvalidDownloadUrlFormatMessage,
                    DefaultInvalidDownloadUrlFormatMessage,
                    normalizedUrl);
                return false;
            }

            if (!IsSupportedDownloadScheme(parsedUri))
            {
                validationMessage = BuildDownloadUrlValidationMessage(
                    model.UnsupportedDownloadUrlSchemeMessage,
                    DefaultUnsupportedDownloadUrlSchemeMessage,
                    normalizedUrl);
                return false;
            }

            downloadUri = parsedUri;
            return true;
        }

        private static bool IsSupportedDownloadScheme(Uri uri)
        {
            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildDownloadUrlValidationMessage(
            string configuredMessage,
            string defaultMessage,
            string detail)
        {
            string message = NormalizeFallbackText(configuredMessage, defaultMessage);
            return string.IsNullOrWhiteSpace(detail)
                ? message
                : message + Environment.NewLine + detail.Trim();
        }

        private void UpdateDownloadButtonState(bool enabled, string configuredText, string defaultText)
        {
            btnDownload.Enabled = enabled;
            btnDownload.Text = NormalizeFallbackText(configuredText, defaultText);
        }

        internal static string BuildHostedUpdateMessage(
            string headline,
            string detail,
            string fallbackMessage,
            string defaultFallbackMessage)
        {
            string resolvedHeadline = NormalizeFallbackText(headline, string.Empty);
            string resolvedFallback = NormalizeFallbackText(fallbackMessage, defaultFallbackMessage);
            string resolvedDetail = NormalizeFallbackText(detail, string.Empty);

            if (string.IsNullOrWhiteSpace(resolvedHeadline))
                resolvedHeadline = resolvedDetail;

            if (string.IsNullOrWhiteSpace(resolvedHeadline))
                return resolvedFallback;

            if (string.IsNullOrWhiteSpace(resolvedDetail) ||
                string.Equals(resolvedHeadline, resolvedDetail, StringComparison.Ordinal))
            {
                return resolvedHeadline + Environment.NewLine + resolvedFallback;
            }

            return resolvedHeadline + Environment.NewLine +
                resolvedDetail + Environment.NewLine +
                resolvedFallback;
        }

        private static string SanitizeHostedDetail(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
                return string.Empty;

            return detail
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ")
                .Trim();
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            hostedUpdateResponseTimer.Stop();
            base.OnFormClosed(e);
        }
    }
}
