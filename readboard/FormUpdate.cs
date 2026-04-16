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
            lblCurrentVersionValue.Text = NormalizeValue(model.CurrentVersion, model.UnavailableText);
            lblLatestVersionValue.Text = NormalizeValue(model.LatestVersion, model.UnavailableText);
            lblReleaseDateValue.Text = NormalizeValue(model.ReleaseDate, model.UnavailableText);
            txtReleaseNotes.Text = NormalizeReleaseNotes(model.ReleaseNotes, model.EmptyReleaseNotesText);
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

        private static void ResetButtonTheme(Button button)
        {
            button.FlatStyle = FlatStyle.System;
            button.UseVisualStyleBackColor = true;
            button.Font = Control.DefaultFont;
            button.Cursor = Cursors.Default;
        }

        private void btnDownload_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(model.DownloadUrl))
            {
                MessageBox.Show(
                    this,
                    NormalizeFallbackText(model.MissingDownloadUrlMessage, DefaultMissingDownloadUrlMessage),
                    GetDialogTitle(),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Process.Start(model.DownloadUrl);
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
