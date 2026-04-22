using System;
using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class MainFormThemeStartupParityRegressionTests
    {
        [Fact]
        public void MainForm_DoesNotUsePostTargetCommitLayoutProfileAbstractions()
        {
            string source = LoadSource("readboard", "Form1.cs");

            Assert.DoesNotContain("MainFormLayoutProfile", source);
            Assert.DoesNotContain("CreateMainFormLayoutProfile()", source);
            Assert.DoesNotContain("ApplyMainFormClientSizeProfile(", source);
        }

        [Fact]
        public void MainForm_ThemeChanges_RestoreClassicTypographyBaselineForDefaultTheme()
        {
            string formSource = LoadSource("readboard", "Form1.cs");
            string themeSource = LoadSource("readboard", "UiTheme.cs");
            string typographySlice = GetMethodSlice(formSource, "private void ApplyMainFormTypography()");
            string classicSlice = GetMethodSlice(formSource, "private void ApplyClassicMainFormTheme()");

            Assert.Contains("Font = UiTheme.BodyFont;", typographySlice);
            Assert.Contains("Font = Control.DefaultFont;", classicSlice);
            Assert.Contains("form.Font = BodyFont;", themeSource);
        }

        [Fact]
        public void ApplyMainFormUi_UsesSeparatedHeaderAnchorsAndWidthClamp()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string methodSlice = GetMethodSlice(source, "private void ApplyMainFormUi()");
            int clampIndex = IndexOfRequired(methodSlice, "ConstrainMainFormWidth();");
            int themeIndex = IndexOfRequired(methodSlice, "ApplyMainFormTheme();");
            int headerIndex = IndexOfRequired(methodSlice, "MainHeaderLayoutMetrics headerLayout = ArrangeMainHeader();");
            int boardTopIndex = IndexOfRequired(methodSlice, "int boardTop = headerLayout.UtilitiesInRightColumn");
            int boardIndex = IndexOfRequired(methodSlice, "int boardBottom = ArrangeMainBoardSection(boardTop, headerLayout);");
            int syncIndex = IndexOfRequired(methodSlice, "int syncBottom = ArrangeMainSyncSection(Math.Max(boardBottom, headerLayout.UtilityBottom) + ScaleValue(12));");
            int actionsIndex = IndexOfRequired(methodSlice, "ArrangeMainActions(syncBottom + ScaleValue(12));");

            Assert.True(themeIndex > clampIndex, "Theme visuals must be applied after width clamping.");
            Assert.True(headerIndex > themeIndex, "Header layout should run after theme application.");
            Assert.True(boardTopIndex > headerIndex, "Board top should be derived from header layout metrics.");
            Assert.True(boardIndex > boardTopIndex, "Board section should follow the board top calculation.");
            Assert.True(syncIndex > boardIndex, "Sync section should follow the board section.");
            Assert.True(actionsIndex > syncIndex, "Action row should follow the sync section.");
        }

        [Fact]
        public void MainForm_AppliesDistinctClassicAndNewThemeVisuals()
        {
            string source = LoadSource("readboard", "Form1.cs");
            string themeSlice = GetMethodSlice(source, "private void ApplyMainFormTheme()");
            string classicSlice = GetMethodSlice(source, "private void ApplyClassicMainFormTheme()");

            Assert.Contains("UiTheme.ApplyWindow(this);", themeSlice);
            Assert.Contains("ApplyOptimizedMainFormTheme();", themeSlice);
            Assert.Contains("BackColor = SystemColors.Control;", classicSlice);
            Assert.Contains("Font = Control.DefaultFont;", classicSlice);
            Assert.Contains("FlatStyle.System", classicSlice);
            Assert.DoesNotContain("ApplyOptimizedMainFormTheme();", classicSlice);
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
            Assert.True(index >= 0, $"Missing marker: {value}");
            return index;
        }
    }
}
