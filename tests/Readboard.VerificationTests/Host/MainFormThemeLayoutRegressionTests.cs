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

            Assert.Contains("int minimumPlatformWidth = Math.Min(contentWidth, MeasureOptionsWidth(new ButtonBase[] { rdoFox, rdoFoxBack, rdoYike, rdoTygem, rdoSina, rdoBack, rdoFore }, optionGap) + ScaleValue(28));", adaptiveHeader);
            Assert.Contains("int groupBottom = LayoutOptionsRow(new ButtonBase[] { rdoFox, rdoFoxBack, rdoYike, rdoTygem, rdoSina, rdoBack, rdoFore }, groupBox1, optionLeft, optionTop, optionGap, rowGap);", adaptiveHeader);
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
            string conditionLabel = GetMethodSlice(source, "private int GetMainSyncConditionTimeLabelWidth()");
            string timeLabelPanel = GetMethodSlice(source, "private int GetMainSyncTimeLabelPanelWidth()");
            string conditionSlot = GetMethodSlice(source, "private int GetMainSyncConditionTimeSlotWidth()");
            string timeRowVisitsMargin = GetMethodSlice(source, "private int GetMainSyncTimeRowVisitsLeftMargin()");
            string syncWidth = GetMethodSlice(source, "private int GetLegacyMainSyncRequiredWidth()");

            Assert.Contains("int sharedLegacyVisitsPanelWidth = GetLegacyMainSyncVisitsPanelWidth();", legacySync);
            Assert.Contains("int conditionLabelWidth = GetMainSyncConditionTimeLabelWidth();", legacySync);
            Assert.Contains("panel1.Size = new Size(GetMainSyncConditionTimeSlotWidth(), rowHeight);", legacySync);
            Assert.Contains("panel4.Margin = new Padding(GetMainSyncTimeRowVisitsLeftMargin(), ScaleValue(2), 0, 0);", legacySync);
            Assert.Contains("panel3.Size = new Size(GetMainSyncTimeLabelPanelWidth(), rowHeight);", legacySync);
            Assert.Contains("lblPlayCondition.SetBounds(0, ScaleValue(3), conditionLabelWidth, ScaleValue(18));", legacySync);
            Assert.Contains("lblTotalVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(18));", legacySync);
            Assert.Contains("lblTime.SetBounds(0, ScaleValue(3), lblTime.PreferredSize.Width, ScaleValue(18));", legacySync);
            Assert.Contains("lblBestMoveVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(18));", legacySync);
            Assert.Contains("lblTime.TextAlign = ContentAlignment.MiddleLeft;", legacySync);
            Assert.Contains("textBox1.Size = new Size(ScaleValue(68), rowHeight);", legacySync);
            Assert.Contains("textBox2.Size = new Size(ScaleValue(92), rowHeight);", legacySync);
            Assert.Contains("textBox3.Size = new Size(ScaleValue(92), rowHeight);", legacySync);

            Assert.Contains("int conditionLabelWidth = GetMainSyncConditionTimeLabelWidth();", adaptiveSync);
            Assert.Contains("panel1.Size = new System.Drawing.Size(GetMainSyncConditionTimeSlotWidth(), rowHeight);", adaptiveSync);
            Assert.Contains("panel4.Margin = new Padding(GetMainSyncTimeRowVisitsLeftMargin(), ScaleValue(2), 0, 0);", adaptiveSync);
            Assert.Contains("panel3.Size = new System.Drawing.Size(GetMainSyncTimeLabelPanelWidth(), rowHeight);", adaptiveSync);
            Assert.Contains("lblPlayCondition.SetBounds(0, ScaleValue(3), conditionLabelWidth, ScaleValue(20));", adaptiveSync);
            Assert.Contains("lblTotalVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(20));", adaptiveSync);
            Assert.Contains("lblTime.SetBounds(0, ScaleValue(3), lblTime.PreferredSize.Width, ScaleValue(20));", adaptiveSync);
            Assert.Contains("lblBestMoveVisits.SetBounds(0, ScaleValue(3), sharedVisitsLabelWidth, ScaleValue(20));", adaptiveSync);
            Assert.Contains("lblTime.TextAlign = ContentAlignment.MiddleLeft;", adaptiveSync);
            Assert.Contains("textBox1.Size = new System.Drawing.Size(ScaleValue(68), rowHeight);", adaptiveSync);
            Assert.Contains("textBox2.Size = new System.Drawing.Size(ScaleValue(92), rowHeight);", adaptiveSync);
            Assert.Contains("textBox3.Size = new System.Drawing.Size(ScaleValue(92), rowHeight);", adaptiveSync);

            Assert.Contains("return Math.Max(lblTotalVisits.PreferredSize.Width, lblBestMoveVisits.PreferredSize.Width);", visitsLabel);
            Assert.Contains("return GetSharedMainSyncVisitsLabelWidth() + ScaleValue(18) + ScaleValue(92);", adaptiveVisits);
            Assert.Contains("return Math.Max(lblPlayCondition.PreferredSize.Width, lblTime.PreferredSize.Width);", conditionLabel);
            Assert.Contains("return lblTime.PreferredSize.Width + ScaleValue(18);", timeLabelPanel);
            Assert.Contains("return GetMainSyncConditionTimeLabelWidth() + ScaleValue(18) + ScaleValue(8) + ScaleValue(68);", conditionSlot);
            Assert.Contains("int usedWidth = GetMainSyncTimeLabelPanelWidth() + ScaleValue(8) + ScaleValue(68);", timeRowVisitsMargin);
            Assert.Contains("return ScaleValue(12) + Math.Max(0, GetMainSyncConditionTimeSlotWidth() - usedWidth);", timeRowVisitsMargin);
            Assert.Contains("+ GetMainSyncConditionTimeSlotWidth()", syncWidth);
            Assert.Contains("ArrangeMainSyncFlowOrder();", legacySync);
            Assert.Contains("ArrangeMainSyncFlowOrder();", adaptiveSync);
            Assert.Contains("pnlAutoPlayColorStatus.Margin = new Padding(0, 0, 0, 0);", legacySync);
            Assert.Contains("pnlAutoPlayColorStatus.Margin = new Padding(0, 0, 0, 0);", adaptiveSync);
            Assert.Contains("pnlFoxAutoPlayIdentity.Margin = new Padding(0, 0, 0, 0);", legacySync);
            Assert.Contains("pnlFoxAutoPlayIdentity.Margin = new Padding(0, 0, 0, 0);", adaptiveSync);
            Assert.Contains("ArrangeMainSyncAutoStatusColumn(rowHeight);", legacySync);
            Assert.Contains("ArrangeMainSyncAutoStatusColumn(rowHeight);", adaptiveSync);

            Assert.DoesNotContain("GetSharedMainSyncToggleWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncColorWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncConditionLabelWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncTimeLabelWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncTimeSlotWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncConditionSlotWidth()", source);
            Assert.DoesNotContain("GetSharedMainSyncVisitsPanelWidth()", source);
        }

        [Fact]
        public void MainForm_SyncFlowPlacesAutoColorAboveIdentityColumn()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string orderSlice = GetMethodSlice(source, "private void ArrangeMainSyncFlowOrder()");
            string columnSlice = GetMethodSlice(source, "private void ArrangeMainSyncAutoStatusColumn(int rowHeight)");
            string columnWidthSlice = GetMethodSlice(source, "private int GetMainSyncAutoStatusColumnWidth()");
            string widthSlice = GetMethodSlice(source, "private int GetLegacyMainSyncRequiredWidth()");

            int autoIndex = IndexOfRequired(orderSlice, "flowLayoutPanel1.Controls.SetChildIndex(pnlAutoPlayColorStatus, 2);");
            int conditionIndex = IndexOfRequired(orderSlice, "flowLayoutPanel1.Controls.SetChildIndex(panel1, 3);");
            int identityIndex = IndexOfRequired(orderSlice, "flowLayoutPanel2.Controls.SetChildIndex(pnlFoxAutoPlayIdentity, 2);");
            int timeIndex = IndexOfRequired(orderSlice, "flowLayoutPanel2.Controls.SetChildIndex(panel3, 3);");
            int timeInputIndex = IndexOfRequired(orderSlice, "flowLayoutPanel2.Controls.SetChildIndex(textBox1, 4);");
            int visitsIndex = IndexOfRequired(orderSlice, "flowLayoutPanel2.Controls.SetChildIndex(panel4, 5);");
            int visitsInputIndex = IndexOfRequired(orderSlice, "flowLayoutPanel2.Controls.SetChildIndex(textBox3, 6);");

            Assert.True(autoIndex < conditionIndex);
            Assert.True(identityIndex < timeIndex);
            Assert.True(timeIndex < timeInputIndex);
            Assert.True(timeInputIndex < visitsIndex);
            Assert.True(visitsIndex < visitsInputIndex);
            Assert.Contains("int columnHeight = Math.Max(rowHeight, btnFoxAutoPlayIdentity.PreferredSize.Height + ScaleValue(2));", columnSlice);
            Assert.Contains("pnlAutoPlayColorStatus.Size = new Size(columnWidth, columnHeight);", columnSlice);
            Assert.Contains("pnlFoxAutoPlayIdentity.Size = new Size(columnWidth, columnHeight);", columnSlice);
            Assert.Contains("radioAutoPlayColor.Location = new Point(0, Math.Max(0, (columnHeight - radioAutoPlayColor.PreferredSize.Height) / 2));", columnSlice);
            Assert.Contains("lblAutoPlayColorStatus.Location = new Point(", columnSlice);
            Assert.Contains("btnFoxAutoPlayIdentity.Location = new Point(0, Math.Max(0, (columnHeight - btnFoxAutoPlayIdentity.PreferredSize.Height) / 2));", columnSlice);
            Assert.Contains("int autoStatusWidth = GetLayoutOptionPreferredSize(radioAutoPlayColor).Width + ScaleValue(6) + GetMainSyncAutoPlayStatusTextWidth();", columnWidthSlice);
            Assert.Contains("return Math.Max(autoStatusWidth, identityWidth);", columnWidthSlice);
            Assert.Contains("+ GetMainSyncAutoStatusColumnWidth()", widthSlice);
        }

        [Fact]
        public void MainForm_AutoPlayStatusTextDoesNotRefreshLayout()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string columnWidthSlice = GetMethodSlice(source, "private int GetMainSyncAutoStatusColumnWidth()");
            string statusWidthSlice = GetMethodSlice(source, "private int GetMainSyncAutoPlayStatusTextWidth()");

            Assert.DoesNotContain("RefreshMainSyncLayoutFromStatusText", source);
            Assert.DoesNotContain("lblAutoPlayColorStatus.PreferredSize.Width", columnWidthSlice);
            Assert.Contains("GetMainSyncAutoPlayStatusTextWidth()", columnWidthSlice);
            Assert.Contains("MainForm_autoPlayColorStatusUnconfigured", statusWidthSlice);
            Assert.Contains("MainForm_autoPlayColorStatusBlack", statusWidthSlice);
            Assert.Contains("MainForm_autoPlayColorStatusWhite", statusWidthSlice);
            Assert.Contains("MainForm_autoPlayColorStatusUnsupported", statusWidthSlice);
            Assert.Contains("MainForm_autoPlayColorStatusSpectating", statusWidthSlice);
            Assert.Contains("MainForm_autoPlayColorStatusWaiting", statusWidthSlice);
            Assert.Contains("TextRenderer.MeasureText(statusTexts[i], lblAutoPlayColorStatus.Font).Width", statusWidthSlice);
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

        private static int IndexOfRequired(string source, string value)
        {
            int index = source.IndexOf(value, StringComparison.Ordinal);
            Assert.True(index >= 0, "Expected to find source fragment: " + value);
            return index;
        }
    }
}
