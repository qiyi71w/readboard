using System;
using System.Diagnostics;
using System.Drawing;
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
        private const string DefaultDialogTitle = "Update";

        private readonly UpdateDialogModel model;

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
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            AcceptButton = btnDownload;
            CancelButton = btnClose;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            if (Program.uiThemeMode == Program.UiThemeOptimized)
                ApplyOptimizedUpdateTheme();
            else
                ApplyClassicUpdateTheme();
            ApplyInfoPanelLayout();
            ResumeLayout(false);
            PerformLayout();
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

        private void btnDownload_Click(object sender, EventArgs e)
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
                Process.Start(downloadUri.AbsoluteUri);
            }
            catch (Exception)
            {
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

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
