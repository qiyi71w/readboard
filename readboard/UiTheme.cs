using System;
using System.Drawing;
using System.Windows.Forms;

namespace readboard
{
    internal static class UiTheme
    {
        public static Color WindowBackground
        {
            get { return IsDarkMode ? Color.FromArgb(30, 30, 30) : Color.FromArgb(242, 245, 249); }
        }

        public static Color SurfaceBackground
        {
            get { return IsDarkMode ? Color.FromArgb(40, 40, 40) : Color.FromArgb(242, 245, 249); }
        }

        public static Color InputBackground
        {
            get { return IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.FromArgb(252, 253, 255); }
        }

        public static Color SurfaceBorder
        {
            get { return IsDarkMode ? Color.FromArgb(55, 55, 55) : Color.FromArgb(203, 213, 225); }
        }

        public static Color PrimaryText
        {
            get { return IsDarkMode ? Color.FromArgb(210, 210, 210) : Color.FromArgb(15, 23, 42); }
        }

        public static Color SecondaryText
        {
            get { return IsDarkMode ? Color.FromArgb(140, 140, 140) : Color.FromArgb(71, 85, 105); }
        }

        public static Color Accent
        {
            get { return IsDarkMode ? Color.FromArgb(80, 140, 200) : Color.FromArgb(3, 105, 161); }
        }

        public static Color AccentHover
        {
            get { return IsDarkMode ? Color.FromArgb(70, 70, 70) : Color.FromArgb(2, 132, 199); }
        }

        public static Color AccentPressed
        {
            get { return IsDarkMode ? Color.FromArgb(60, 60, 60) : Color.FromArgb(7, 89, 133); }
        }

        public static Color Danger
        {
            get { return IsDarkMode ? Color.FromArgb(190, 80, 80) : Color.FromArgb(185, 28, 28); }
        }

        public static Color DangerBack
        {
            get { return IsDarkMode ? Color.FromArgb(55, 30, 30) : Color.FromArgb(254, 242, 242); }
        }

        public static Color HighlightBack
        {
            get { return IsDarkMode ? Color.FromArgb(35, 55, 80) : Color.FromArgb(239, 246, 255); }
        }

        public static readonly Font BodyFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font SectionFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);

        public static bool IsDarkMode
        {
            get
            {
                int colorMode = AppConfig.ColorModeSystem;
                try
                {
                    if (Program.CurrentContext != null && Program.CurrentConfig != null)
                        colorMode = Program.CurrentConfig.ColorMode;
                }
                catch (NullReferenceException)
                {
                    // Fall back to system mode if runtime is not initialized (designer/tests).
                }
                if (colorMode == AppConfig.ColorModeDark)
                    return true;
                if (colorMode == AppConfig.ColorModeLight)
                    return false;
                return Application.SystemColorMode == SystemColorMode.Dark;
            }
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
            if (IsDarkMode)
            {
                if (option is RadioButton radio)
                {
                    radio.CheckedChanged -= OnOptionCheckedChanged;
                    radio.CheckedChanged += OnOptionCheckedChanged;
                }
                else if (option is CheckBox checkBox)
                {
                    checkBox.CheckedChanged -= OnOptionCheckedChanged;
                    checkBox.CheckedChanged += OnOptionCheckedChanged;
                }
                ApplyOptionCheckedVisual(option);
            }
        }

        public static void ResetOption(ButtonBase option)
        {
            if (option is RadioButton radio)
                radio.CheckedChanged -= OnOptionCheckedChanged;
            else if (option is CheckBox checkBox)
                checkBox.CheckedChanged -= OnOptionCheckedChanged;
        }

        private static void OnOptionCheckedChanged(object sender, EventArgs e)
        {
            ApplyOptionCheckedVisual((ButtonBase)sender);
        }

        private static void ApplyOptionCheckedVisual(ButtonBase option)
        {
            bool isChecked = false;
            if (option is RadioButton radio)
                isChecked = radio.Checked;
            else if (option is CheckBox checkBox)
                isChecked = checkBox.Checked;

            if (isChecked)
            {
                option.ForeColor = Color.FromArgb(110, 175, 235);
                option.FlatAppearance.BorderColor = Accent;
            }
            else
            {
                option.ForeColor = PrimaryText;
                option.FlatAppearance.BorderColor = SurfaceBorder;
            }
        }

        public static void StylePrimaryButton(Button button)
        {
            if (IsDarkMode)
                StyleButton(button, Color.FromArgb(40, 70, 110), Color.FromArgb(180, 210, 240), Color.FromArgb(50, 80, 120), Color.FromArgb(50, 85, 130), Color.FromArgb(35, 60, 95));
            else
                StyleButton(button, Accent, Color.White, Accent, Color.FromArgb(2, 132, 199), Color.FromArgb(7, 89, 133));
        }

        public static void StyleSecondaryButton(Button button)
        {
            if (IsDarkMode)
                StyleButton(button, Color.FromArgb(55, 55, 55), PrimaryText, SurfaceBorder, Color.FromArgb(65, 65, 65), Color.FromArgb(50, 50, 50));
            else
                StyleButton(button, SurfaceBackground, Accent, SurfaceBorder, HighlightBack, HighlightBack);
        }

        public static void StyleDangerButton(Button button)
        {
            StyleButton(button, DangerBack, Danger, IsDarkMode ? Color.FromArgb(70, 35, 35) : Color.FromArgb(254, 202, 202), DangerBack, DangerBack);
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
            label.ForeColor = IsDarkMode ? SecondaryText : Color.FromArgb(7, 89, 133);
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
