using System.Drawing;
using System.Windows.Forms;

namespace readboard
{
    internal static class UiTheme
    {
        public static Color WindowBackground
        {
            get { return IsDarkMode ? SystemColors.Window : Color.FromArgb(242, 245, 249); }
        }

        public static Color SurfaceBackground
        {
            get { return IsDarkMode ? SystemColors.Control : Color.FromArgb(242, 245, 249); }
        }

        public static Color InputBackground
        {
            get { return IsDarkMode ? SystemColors.Window : Color.FromArgb(252, 253, 255); }
        }

        public static Color SurfaceBorder
        {
            get { return IsDarkMode ? SystemColors.ControlDark : Color.FromArgb(203, 213, 225); }
        }

        public static Color PrimaryText
        {
            get { return IsDarkMode ? SystemColors.ControlText : Color.FromArgb(15, 23, 42); }
        }

        public static Color SecondaryText
        {
            get { return IsDarkMode ? SystemColors.GrayText : Color.FromArgb(71, 85, 105); }
        }

        public static Color Accent
        {
            get { return IsDarkMode ? SystemColors.Highlight : Color.FromArgb(3, 105, 161); }
        }

        public static Color AccentHover
        {
            get { return IsDarkMode ? SystemColors.HotTrack : Color.FromArgb(2, 132, 199); }
        }

        public static Color AccentPressed
        {
            get { return IsDarkMode ? SystemColors.Highlight : Color.FromArgb(7, 89, 133); }
        }

        public static Color Danger
        {
            get { return Color.FromArgb(185, 28, 28); }
        }

        public static Color DangerBack
        {
            get { return IsDarkMode ? Color.FromArgb(60, 20, 20) : Color.FromArgb(254, 242, 242); }
        }

        public static Color HighlightBack
        {
            get { return IsDarkMode ? SystemColors.ControlDark : Color.FromArgb(239, 246, 255); }
        }

        public static readonly Font BodyFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font SectionFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);

        public static bool IsDarkMode
        {
            get { return Application.SystemColorMode == SystemColorMode.Dark; }
        }

        public static void ApplyWindow(Form form)
        {
            form.BackColor = WindowBackground;
            form.ForeColor = PrimaryText;
            form.Font = BodyFont;
        }

        public static void StyleGroupBox(GroupBox groupBox)
        {
            groupBox.BackColor = SurfaceBackground;
            groupBox.ForeColor = PrimaryText;
            groupBox.Font = SectionFont;
            groupBox.Padding = new Padding(12, 10, 12, 12);
        }

        public static void StylePanelSurface(Control control)
        {
            control.BackColor = SurfaceBackground;
            control.ForeColor = PrimaryText;
            control.Font = BodyFont;
        }

        public static void StyleInput(TextBox textBox)
        {
            textBox.BackColor = InputBackground;
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.ForeColor = PrimaryText;
            textBox.Font = BodyFont;
        }

        public static void StyleOption(ButtonBase option)
        {
            option.FlatStyle = FlatStyle.Flat;
            option.FlatAppearance.BorderColor = SurfaceBorder;
            option.FlatAppearance.BorderSize = 1;
            option.FlatAppearance.CheckedBackColor = HighlightBack;
            option.FlatAppearance.MouseOverBackColor = HighlightBack;
            option.FlatAppearance.MouseDownBackColor = HighlightBack;
            option.UseVisualStyleBackColor = false;
            option.BackColor = SurfaceBackground;
            option.ForeColor = PrimaryText;
            option.Font = BodyFont;
            option.Cursor = Cursors.Hand;
        }

        public static void StylePrimaryButton(Button button)
        {
            StyleButton(button, Accent, Color.White, Accent, AccentHover, AccentPressed);
        }

        public static void StyleSecondaryButton(Button button)
        {
            StyleButton(button, SurfaceBackground, Accent, SurfaceBorder, HighlightBack, HighlightBack);
        }

        public static void StyleDangerButton(Button button)
        {
            StyleButton(button, DangerBack, Danger, IsDarkMode ? Color.FromArgb(120, 40, 40) : Color.FromArgb(254, 202, 202), DangerBack, DangerBack);
        }

        public static void StyleSubtleLabel(Label label)
        {
            label.ForeColor = SecondaryText;
            label.Font = BodyFont;
        }

        public static void StyleNoticeLabel(Label label)
        {
            label.BackColor = HighlightBack;
            label.BorderStyle = BorderStyle.FixedSingle;
            label.ForeColor = AccentPressed;
            label.Font = BodyFont;
            label.Padding = new Padding(8, 0, 8, 0);
        }

        private static void StyleButton(
            Button button,
            Color backColor,
            Color foreColor,
            Color borderColor,
            Color hoverColor,
            Color pressedColor)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = borderColor;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = hoverColor;
            button.FlatAppearance.MouseDownBackColor = pressedColor;
            button.UseVisualStyleBackColor = false;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.Font = BodyFont;
            button.Cursor = Cursors.Hand;
        }

    }
}
