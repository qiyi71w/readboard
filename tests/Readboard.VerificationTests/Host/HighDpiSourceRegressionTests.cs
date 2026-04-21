using System;
using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class HighDpiSourceRegressionTests
    {
        [Fact]
        public void AppConfig_DeclaresPerMonitorV2AndAutoResizing()
        {
            string content = LoadSource("readboard", "App.config");

            Assert.Contains("DpiAwareness\" value=\"PerMonitorV2\"", content);
            Assert.Contains("EnableWindowsFormsHighDpiAutoResizing\" value=\"true\"", content);
            Assert.DoesNotContain("<configSections>", content);
            Assert.DoesNotContain("name=\"System.Windows.Forms.ApplicationConfigurationSection\"", content);
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
    }
}
