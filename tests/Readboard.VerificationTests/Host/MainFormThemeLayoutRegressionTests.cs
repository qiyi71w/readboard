using System;
using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class MainFormThemeLayoutRegressionTests
    {
        [Fact]
        public void MainForm_SplitsHeaderLayoutBetweenPlatformAndUtilities()
        {
            string source = LoadSource("readboard", "Form1.cs");

            Assert.Contains("private readonly struct MainHeaderLayoutMetrics", source);
            Assert.Contains("public MainHeaderLayoutMetrics(int platformBottom, int utilityBottom, int platformWidth, bool utilitiesInRightColumn)", source);
            Assert.Contains("private MainHeaderLayoutMetrics ArrangeMainHeader()", source);
            Assert.Contains("private MainHeaderLayoutMetrics ArrangeLegacyMainHeader()", source);
            Assert.Contains("private MainHeaderLayoutMetrics ArrangeAdaptiveMainHeader()", source);
        }

        [Fact]
        public void MainForm_RestoresTargetCommitHeaderBoardAndOptionMeasurementFlow()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string legacyHeader = GetMethodSlice(source, "private MainHeaderLayoutMetrics ArrangeLegacyMainHeader()");
            string adaptiveHeader = GetMethodSlice(source, "private MainHeaderLayoutMetrics ArrangeAdaptiveMainHeader()");
            string legacyBoard = GetMethodSlice(source, "private int ArrangeLegacyMainBoardSection(int top)");
            string adaptiveBoard = GetMethodSlice(source, "private int ArrangeAdaptiveMainBoardSection(int top, MainHeaderLayoutMetrics headerLayout)");
            string optionsRow = GetMethodSlice(source, "private int LayoutOptionsRow(ButtonBase[] options, GroupBox groupBox, int startX, int startY, int itemGap, int rowGap)");
            string optionsWidth = GetMethodSlice(source, "private int MeasureOptionsWidth(ButtonBase[] options, int itemGap)");

            Assert.Contains("int settingsWidth = MeasureButtonWidth(btnSettings, 72);", legacyHeader);
            Assert.Contains("rdoFox.Location = new Point(optionLeft, optionTop);", legacyHeader);
            Assert.Contains("btnKomi65.SetBounds(settingsLeft, top + buttonHeight + utilityGap, utilityRight - settingsLeft, buttonHeight);", legacyHeader);
            Assert.Contains("return new MainHeaderLayoutMetrics(groupBox1.Bottom, btnCheckUpdate.Bottom, groupBox1.Width, true);", legacyHeader);

            Assert.Contains("int minimumPlatformWidth = Math.Min(contentWidth, MeasureOptionsWidth(new ButtonBase[] { rdoFox, rdoFoxBack, rdoTygem, rdoSina, rdoBack, rdoFore }, optionGap) + ScaleValue(28));", adaptiveHeader);
            Assert.Contains("int groupBottom = LayoutOptionsRow(new ButtonBase[] { rdoFox, rdoFoxBack, rdoTygem, rdoSina, rdoBack, rdoFore }, groupBox1, optionLeft, optionTop, optionGap, rowGap);", adaptiveHeader);
            Assert.Contains("return new MainHeaderLayoutMetrics(groupBox1.Bottom, btnCheckUpdate.Bottom, groupBox1.Width, true);", adaptiveHeader);
            Assert.Contains("return new MainHeaderLayoutMetrics(groupBox1.Bottom, btnCheckUpdate.Bottom, contentWidth, false);", adaptiveHeader);

            Assert.Contains("lblBoardSize.SetBounds(sectionPadding, ScaleValue(30), Math.Max(lblBoardSize.PreferredSize.Width, ScaleValue(52)), ScaleValue(20));", legacyBoard);
            Assert.Contains("int groupWidth = headerLayout.UtilitiesInRightColumn ? headerLayout.PlatformWidth : contentWidth;", adaptiveBoard);
            Assert.Contains("groupBox2.SetBounds(left, top, groupWidth, 0);", adaptiveBoard);
            Assert.Contains("rdo19x19.Location = new System.Drawing.Point(lblBoardSize.Right + ScaleValue(6), optionTop);", adaptiveBoard);

            Assert.Contains("Size preferredSize = GetLayoutOptionPreferredSize(option);", optionsRow);
            Assert.Contains("width += GetLayoutOptionPreferredSize(option).Width;", optionsWidth);

            Assert.DoesNotContain("MeasureMainLayoutButtonWidth", source);
            Assert.DoesNotContain("MeasureMainLayoutOptionWidth", source);
            Assert.DoesNotContain("MeasureMainLayoutLabelWidth", source);
            Assert.DoesNotContain("widthSelector", source);
        }

        [Fact]
        public void MainForm_RestoresTargetCommitSyncGeometry()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string legacySync = GetMethodSlice(source, "private int ArrangeLegacyMainSyncSection(int top)");
            string adaptiveSync = GetMethodSlice(source, "private int ArrangeAdaptiveMainSyncSection(int top)");
            string visitsLabel = GetMethodSlice(source, "private int GetSharedMainSyncVisitsLabelWidth()");
            string adaptiveVisits = GetMethodSlice(source, "private int GetAdaptiveMainSyncVisitsPanelWidth()");

            Assert.Contains("int sharedLegacyVisitsPanelWidth = GetLegacyMainSyncVisitsPanelWidth();", legacySync);
            Assert.Contains("lblTotalVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(18));", legacySync);
            Assert.Contains("lblBestMoveVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(18));", legacySync);
            Assert.Contains("textBox1.Size = new Size(ScaleValue(68), rowHeight);", legacySync);
            Assert.Contains("textBox2.Size = new Size(ScaleValue(92), rowHeight);", legacySync);
            Assert.Contains("textBox3.Size = new Size(ScaleValue(92), rowHeight);", legacySync);

            Assert.Contains("panel1.Size = new System.Drawing.Size(lblPlayCondition.PreferredSize.Width + ScaleValue(18) + ScaleValue(68) + timeFieldGap, rowHeight);", adaptiveSync);
            Assert.Contains("panel3.Size = new System.Drawing.Size(lblTime.PreferredSize.Width + ScaleValue(18) + ScaleValue(92), rowHeight);", adaptiveSync);
            Assert.Contains("lblTotalVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(20));", adaptiveSync);
            Assert.Contains("lblBestMoveVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(20));", adaptiveSync);
            Assert.Contains("textBox1.Size = new System.Drawing.Size(ScaleValue(68), rowHeight);", adaptiveSync);
            Assert.Contains("textBox2.Size = new System.Drawing.Size(ScaleValue(92), rowHeight);", adaptiveSync);
            Assert.Contains("textBox3.Size = new System.Drawing.Size(ScaleValue(92), rowHeight);", adaptiveSync);

            Assert.Contains("return Math.Max(lblTotalVisits.PreferredSize.Width, lblBestMoveVisits.PreferredSize.Width);", visitsLabel);
            Assert.Contains("return GetSharedMainSyncVisitsLabelWidth() + ScaleValue(18) + ScaleValue(92);", adaptiveVisits);

            Assert.DoesNotContain("GetSharedMainSyncToggleWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncColorWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncConditionLabelWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncTimeLabelWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncTimeSlotWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncConditionSlotWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncVisitsPanelWidth()", source);
        }

        [Fact]
        public void MainForm_RestoresTargetCommitClientSizeHelpers()
        {
            string source = LoadSource("readboard", "Form1.cs");

            Assert.Contains("private void ConstrainMainFormWidth()", source);
            Assert.Contains("private Size ScaleSize(Size logicalSize)", source);
            Assert.DoesNotContain("private void ApplyMainFormClientSizeProfile(", source);
        }

        [Fact]
        public void MainForm_ClassicThemeRestores637SystemThemeBranch()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string classicSlice = GetMethodSlice(source, "private void ApplyClassicMainFormTheme()");

            Assert.Contains("BackColor = SystemColors.Control;", classicSlice);
            Assert.Contains("ForeColor = SystemColors.ControlText;", classicSlice);
            Assert.Contains("Font = Control.DefaultFont;", classicSlice);
            Assert.Contains("option.FlatStyle = FlatStyle.Standard;", classicSlice);
            Assert.Contains("button.FlatStyle = FlatStyle.System;", classicSlice);
            Assert.Contains("textBox.BorderStyle = BorderStyle.Fixed3D;", classicSlice);
        }

        [Fact]
        public void MainForm_UsesThemeNeutralOptionMetricsForSharedLayoutParity()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string optionMetricsSlice = GetMethodSlice(source, "private Size GetLayoutOptionPreferredSize(ButtonBase option)");
            string probeSlice = GetMethodSlice(source, "private static Size MeasureLayoutOptionPreferredSize(ButtonBase option, FlatStyle flatStyle)");
            string optionsRowSlice = GetMethodSlice(source, "private int LayoutOptionsRow(ButtonBase[] options, GroupBox groupBox, int startX, int startY, int itemGap, int rowGap)");
            string optionsWidthSlice = GetMethodSlice(source, "private int MeasureOptionsWidth(ButtonBase[] options, int itemGap)");
            string legacyBoardSlice = GetMethodSlice(source, "private int GetLegacyMainBoardRequiredWidth()");
            string legacySyncSlice = GetMethodSlice(source, "private int GetLegacyMainSyncRequiredWidth()");
            string legacyActionsSlice = GetMethodSlice(source, "private int GetLegacyMainActionsRequiredWidth()");
            string adaptiveActionsSlice = GetMethodSlice(source, "private void ArrangeAdaptiveMainActions(int top)");

            Assert.Contains("MeasureLayoutOptionPreferredSize(option, FlatStyle.Standard)", optionMetricsSlice);
            Assert.Contains("MeasureLayoutOptionPreferredSize(option, FlatStyle.Flat)", optionMetricsSlice);
            Assert.Contains("Math.Max(standardSize.Width, flatSize.Width)", optionMetricsSlice);
            Assert.Contains("Math.Max(standardSize.Height, flatSize.Height)", optionMetricsSlice);

            Assert.Contains("if (option is RadioButton radioButton)", probeSlice);
            Assert.Contains("if (option is CheckBox checkBox)", probeSlice);
            Assert.Contains("throw new NotSupportedException", probeSlice);

            Assert.Contains("Size preferredSize = GetLayoutOptionPreferredSize(option);", optionsRowSlice);
            Assert.Contains("width += GetLayoutOptionPreferredSize(option).Width;", optionsWidthSlice);
            Assert.Contains("GetLayoutOptionPreferredSize(rdo19x19).Width", legacyBoardSlice);
            Assert.Contains("GetLayoutOptionPreferredSize(chkBothSync).Width", legacySyncSlice);
            Assert.Contains("GetLayoutOptionPreferredSize(chkShowInBoard).Width", legacyActionsSlice);
            Assert.Contains("int showInBoardWidth = GetLayoutOptionPreferredSize(chkShowInBoard).Width;", adaptiveActionsSlice);
        }

        [Fact]
        public void ThemeResources_RenameOptimizedThemeToNewTheme()
        {
            Assert.Contains("langItems[\"MainForm_themeOptimized\"] = \"新版主题\";", LoadSource("readboard", "Program.cs"));
            Assert.Contains("MainForm_themeOptimized=新版主题", LoadSource("readboard", "language_cn.txt"));
            Assert.Contains("MainForm_themeOptimized=New Theme", LoadSource("readboard", "language_en.txt"));
            Assert.Contains("MainForm_themeOptimized=新テーマ", LoadSource("readboard", "language_jp.txt"));
            Assert.Contains("MainForm_themeOptimized=새 테마", LoadSource("readboard", "language_kr.txt"));

            Assert.DoesNotContain("修复版主题", LoadSource("readboard", "Program.cs"));
            Assert.DoesNotContain("MainForm_themeOptimized=修复版主题", LoadSource("readboard", "language_cn.txt"));
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path);
        }

        private static string GetMethodSlice(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing signature: {signature}");
            int braceStart = source.IndexOf('{', start);
            int depth = 0;
            for (int index = braceStart; index < source.Length; index++)
            {
                if (source[index] == '{')
                    depth++;
                else if (source[index] == '}')
                    depth--;

                if (depth == 0)
                    return source.Substring(start, index - start + 1);
            }

            throw new InvalidOperationException($"Could not slice method: {signature}");
        }
    }
}
