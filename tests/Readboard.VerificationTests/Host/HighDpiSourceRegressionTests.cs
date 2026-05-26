using System;
using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class HighDpiSourceRegressionTests
    {
        [Fact]
        public void ProgramMain_SetsHighDpiModePerMonitorV2()
        {
            string content = LoadSource("readboard", "Program.cs");

            Assert.Contains("SetHighDpiMode(HighDpiMode.PerMonitorV2)", content);
        }

        [Fact]
        public void Manifest_RemovesLegacyDpiAwareFlag()
        {
            string content = LoadSource("readboard", "Properties", "app.manifest");

            Assert.DoesNotContain("<dpiAware>true</dpiAware>", content);
        }

        [Theory]
        [InlineData("Form1.cs")]
        [InlineData("Form4.cs")]
        [InlineData("Form7.cs")]
        [InlineData("FormUpdate.cs")]
        public void HighDpiForms_DoNotDisableAutoscaling(string fileName)
        {
            string content = LoadSource("readboard", fileName);

            Assert.DoesNotContain("AutoScaleMode = AutoScaleMode.None", content);
        }

        [Theory]
        [InlineData("Form1.cs", "AutoScroll = true;", "ApplyMainFormClientHeight(chkShowInBoard.Bottom + ScaleValue(12));")]
        [InlineData("Form4.cs", "AutoScroll = true;", "ApplySettingsClientHeight(btnConfirm.Bottom + bottomPadding);")]
        [InlineData("Form7.cs", "AutoScroll = true;", "ApplyTipsClientHeight(Math.Max(btnConfirm.Bottom, btnNotAskAgain.Bottom) + bottomPadding);")]
        public void LayoutDrivenForms_ClampFinalHeightAndEnableScrollFallback(
            string fileName,
            string scrollMarker,
            string heightClampMarker)
        {
            string content = LoadSource("readboard", fileName);

            Assert.Contains(scrollMarker, content);
            Assert.Contains(heightClampMarker, content);
            Assert.Contains("AutoScrollMinSize = desiredHeight > constrainedHeight", content);
        }

        [Fact]
        public void MainForm_AutoPlayColorMode_IsMeasuredAndThemed()
        {
            string content = LoadSource("readboard", "Form1.cs");
            string legacySlice = GetMethodSlice(content, "private int ArrangeLegacyMainSyncSection(int top)");
            string adaptiveSlice = GetMethodSlice(content, "private int ArrangeAdaptiveMainSyncSection(int top)");
            string widthSlice = GetMethodSlice(content, "private int GetLegacyMainSyncRequiredWidth()");
            string optionsSlice = GetMethodSlice(content, "private IEnumerable<ButtonBase> MainThemeOptions()");
            string labelsSlice = GetMethodSlice(content, "private IEnumerable<Label> MainThemeLabels()");

            Assert.Contains("radioAutoPlayColor.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);", legacySlice);
            Assert.Contains("radioAutoPlayColor.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);", adaptiveSlice);
            Assert.Contains("lblAutoPlayColorStatus.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);", legacySlice);
            Assert.Contains("lblAutoPlayColorStatus.Margin = new Padding(0, ScaleValue(5), ScaleValue(12), 0);", adaptiveSlice);
            Assert.Contains("btnFoxAutoPlayIdentity.Margin = new Padding(0, ScaleValue(1), ScaleValue(12), 0);", legacySlice);
            Assert.Contains("btnFoxAutoPlayIdentity.Margin = new Padding(0, ScaleValue(1), ScaleValue(12), 0);", adaptiveSlice);
            Assert.Contains("GetLayoutOptionPreferredSize(radioAutoPlayColor).Width", widthSlice);
            Assert.Contains("lblAutoPlayColorStatus.PreferredSize.Width", widthSlice);
            Assert.Contains("GetLayoutOptionPreferredSize(btnFoxAutoPlayIdentity).Width", widthSlice);
            Assert.Contains("radioAutoPlayColor", optionsSlice);
            Assert.Contains("btnFoxAutoPlayIdentity", optionsSlice);
            Assert.Contains("lblAutoPlayColorStatus", labelsSlice);
        }

        [Fact]
        public void SettingsForm_PlacesOpenDebugDirectoryButtonInTopRightSlot()
        {
            string content = LoadSource("readboard", "Form4.cs");

            Assert.Contains("btnOpenDebugDiagnostics.SetBounds(buttonLeft, top, buttonWidth, buttonHeight);", content);
            Assert.Contains("currentTop = LayoutSingleOption(chkDebugDiagnostics, left, currentTop, optionRowGap);", content);
            Assert.DoesNotContain("LayoutOptionRow(chkDebugDiagnostics, btnOpenDebugDiagnostics", content);
        }

        [Fact]
        public void SettingsForm_FoxAutoPlayIdentityControls_AreNotInSettings()
        {
            string content = LoadSource("readboard", "Form4.cs");
            string designerSource = LoadSource("readboard", "Form4.Designer.cs");

            Assert.DoesNotContain("lblFoxAutoPlayNickname", designerSource);
            Assert.DoesNotContain("txtFoxAutoPlayNickname", designerSource);
            Assert.DoesNotContain("btnClearFoxAutoPlayIdentity", designerSource);
            Assert.DoesNotContain("LayoutFoxAutoPlayIdentityRow", content);
            Assert.DoesNotContain("MeasureButtonWidth(btnClearFoxAutoPlayIdentity", content);
        }

        [Fact]
        public void SelectionOverlay_UsesVirtualDesktopAndMonitorAwareMagnifierPlacement()
        {
            string content = LoadSource("readboard", "Form2.cs");

            Assert.Contains("DisplayScaling.GetVirtualScreenBounds()", content);
            Assert.Contains("Screen.FromPoint(anchorPoint).WorkingArea", content);
            Assert.DoesNotContain("Screen.PrimaryScreen.Bounds.Height", content);
        }

        [Theory]
        [InlineData("Core", "Protocol", "LegacyWindowDescriptorFactory.cs")]
        [InlineData("Core", "Capture", "IBoardCaptureService.cs")]
        public void DpiUnawareWindowDescriptors_ResolveScaleFromMonitorAwareFallback(
            string segment1,
            string segment2,
            string fileName)
        {
            string content = LoadSource("readboard", segment1, segment2, fileName);

            Assert.Contains("DisplayScaling.ResolveWindowScale(", content);
            Assert.Contains("DisplayScaling.GetScaleForWindowBounds(", content);
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path);
        }

        private static int IndexOfRequired(string source, string value)
        {
            int index = source.IndexOf(value, StringComparison.Ordinal);
            Assert.True(index >= 0, "Expected to find source fragment: " + value);
            return index;
        }

        private static string GetMethodSlice(string source, string methodSignature)
        {
            int startIndex = IndexOfRequired(source, methodSignature);
            int nextMethodIndex = source.IndexOf("\n        private ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            int publicMethodIndex = source.IndexOf("\n        public ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            int defaultMethodIndex = source.IndexOf("\n        void ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            int internalMethodIndex = source.IndexOf("\n        internal ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            if (publicMethodIndex >= 0 && (nextMethodIndex < 0 || publicMethodIndex < nextMethodIndex))
                nextMethodIndex = publicMethodIndex;
            if (defaultMethodIndex >= 0 && (nextMethodIndex < 0 || defaultMethodIndex < nextMethodIndex))
                nextMethodIndex = defaultMethodIndex;
            if (internalMethodIndex >= 0 && (nextMethodIndex < 0 || internalMethodIndex < nextMethodIndex))
                nextMethodIndex = internalMethodIndex;
            if (nextMethodIndex < 0)
                nextMethodIndex = source.Length;
            return source.Substring(startIndex, nextMethodIndex - startIndex);
        }
    }
}
