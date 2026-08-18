using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;
using readboard;
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
        public void HighDpiForms_DoNotDisableAutoscaling(string fileName)
        {
            string content = LoadSource("readboard", fileName);

            Assert.DoesNotContain("AutoScaleMode = AutoScaleMode.None", content);
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

        [Fact]
        public void ResolveClientSizeFromOuterBounds_RemovesNativeFrameWithoutCumulativeGrowth()
        {
            Size clientSize = new Size(1100, 600);
            Size nonClientSize = new Size(16, 39);
            Size outerSize = new Size(
                clientSize.Width + nonClientSize.Width,
                clientSize.Height + nonClientSize.Height);

            Size restoredClientSize = MainForm.ResolveClientSizeFromOuterBounds(outerSize, nonClientSize);

            Assert.Equal(clientSize, restoredClientSize);
        }

        [Fact]
        public void Manifest_UsesAsInvokerForHostLaunchedReadboard()
        {
            string content = LoadSource("readboard", "Properties", "app.manifest");
            XDocument manifest = XDocument.Parse(content);
            XNamespace assemblyNamespace = "urn:schemas-microsoft-com:asm.v1";
            XNamespace trustNamespace = "urn:schemas-microsoft-com:asm.v2";
            XNamespace privilegesNamespace = "urn:schemas-microsoft-com:asm.v3";

            XElement assembly = GetRequiredElement(manifest, assemblyNamespace + "assembly");
            XElement trustInfo = GetRequiredElement(assembly, trustNamespace + "trustInfo");
            XElement security = GetRequiredElement(trustInfo, trustNamespace + "security");
            XElement requestedPrivileges = GetRequiredElement(
                security,
                privilegesNamespace + "requestedPrivileges");
            XElement requestedExecutionLevel = GetRequiredSingleElement(
                requestedPrivileges,
                privilegesNamespace + "requestedExecutionLevel");

            Assert.Equal("asInvoker", (string)requestedExecutionLevel.Attribute("level"));
            Assert.Equal("false", (string)requestedExecutionLevel.Attribute("uiAccess"));
        }

        private static XElement GetRequiredElement(XContainer parent, XName name)
        {
            XElement element = parent.Element(name);
            Assert.NotNull(element);
            return element;
        }

        private static XElement GetRequiredSingleElement(XContainer parent, XName name)
        {
            XElement[] elements = parent.Elements(name).ToArray();
            Assert.Single(elements);
            return elements[0];
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path);
        }

    }
}
