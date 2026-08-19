using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Text.Json;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Launch
{
    public sealed class SupportedWindowsVersionTests
    {
        [Theory]
        [InlineData(6, 1, 7601)]
        [InlineData(10, 0, 14393)]
        [InlineData(10, 0, 17134)]
        [InlineData(10, 0, 17762)]
        public void IsSupported_RejectsVersionsBelowInclusiveFloor(int major, int minor, int build)
        {
            Assert.False(SupportedWindowsVersion.IsSupported(new Version(major, minor, build)));
        }

        [Theory]
        [InlineData(10, 0, 17763)]
        [InlineData(10, 0, 17764)]
        [InlineData(10, 0, 19041)]
        [InlineData(10, 0, 22000)]
        public void IsSupported_AcceptsVersionsAtOrAboveInclusiveFloor(int major, int minor, int build)
        {
            Assert.True(SupportedWindowsVersion.IsSupported(new Version(major, minor, build)));
        }

        [Fact]
        public void IsSupported_TreatsMissingVersionAsUnsupported()
        {
            Assert.False(SupportedWindowsVersion.IsSupported(null));
        }

        [Fact]
        public void EnsureSupported_PromptsOnceAndStopsWhenVersionIsBelowFloor()
        {
            int promptCount = 0;

            bool supported = SupportedWindowsVersion.EnsureSupported(
                () => new Version(10, 0, 17762),
                delegate { promptCount++; });

            Assert.False(supported);
            Assert.Equal(1, promptCount);
        }

        [Fact]
        public void EnsureSupported_ContinuesWithoutPromptWhenVersionMeetsFloor()
        {
            int promptCount = 0;

            bool supported = SupportedWindowsVersion.EnsureSupported(
                () => new Version(10, 0, 17763),
                delegate { promptCount++; });

            Assert.True(supported);
            Assert.Equal(0, promptCount);
        }

        [Fact]
        public void Minimum_MatchesPublishedChannelAndSupportedOsPlatformVersion()
        {
            string root = VerificationFixtureLocator.RepositoryRoot();
            Assert.Equal(new Version(10, 0, 17763), SupportedWindowsVersion.Minimum);

            using (JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, "update-channels.json"))))
            {
                JsonElement main = document.RootElement
                    .GetProperty("channels")
                    .EnumerateArray()
                    .Single(channel => channel.GetProperty("id").GetString() == "main");
                Assert.Equal(
                    SupportedWindowsVersion.Minimum,
                    Version.Parse(main.GetProperty("minimumWindowsVersion").GetString()));
            }

            XDocument project = XDocument.Load(Path.Combine(root, "readboard", "readboard.csproj"));
            Version supportedOsPlatformVersion = Version.Parse(
                project.Descendants("SupportedOSPlatformVersion").Single().Value.Trim());
            Assert.Equal(SupportedWindowsVersion.Minimum.Major, supportedOsPlatformVersion.Major);
            Assert.Equal(SupportedWindowsVersion.Minimum.Minor, supportedOsPlatformVersion.Minor);
            Assert.Equal(SupportedWindowsVersion.Minimum.Build, supportedOsPlatformVersion.Build);
        }

        [Fact]
        public void ProgramMain_ChecksSupportedWindowsVersionAfterVisualStylesAndBeforeTransport()
        {
            string source = File.ReadAllText(Path.Combine(
                VerificationFixtureLocator.RepositoryRoot(),
                "readboard",
                "Program.cs"));
            string main = GetMethodSlice(source, "static void Main(string[] args)");

            Assert.Contains("LaunchOptions.TryParse(args, out options)", main);
            Assert.Contains("InitializeRuntime(options)", main);
            Assert.Contains("Application.EnableVisualStyles()", main);
            Assert.Contains("if (!EnsureSupportedWindowsVersion())", main);
            Assert.Contains("CreateTransport(options)", main);
            Assert.Contains("CreateMainForm(options, activeSessionCoordinator)", main);
            Assert.Contains("mainForm.EnsureWebViewRuntimeAvailable()", main);

            Assert.True(
                main.IndexOf("LaunchOptions.TryParse(args, out options)", StringComparison.Ordinal)
                < main.IndexOf("InitializeRuntime(options)", StringComparison.Ordinal));
            Assert.True(
                main.IndexOf("InitializeRuntime(options)", StringComparison.Ordinal)
                < main.IndexOf("Application.EnableVisualStyles()", StringComparison.Ordinal));
            Assert.True(
                main.IndexOf("Application.EnableVisualStyles()", StringComparison.Ordinal)
                < main.IndexOf("if (!EnsureSupportedWindowsVersion())", StringComparison.Ordinal));
            Assert.True(
                main.IndexOf("if (!EnsureSupportedWindowsVersion())", StringComparison.Ordinal)
                < main.IndexOf("CreateTransport(options)", StringComparison.Ordinal));
            Assert.True(
                main.IndexOf("CreateTransport(options)", StringComparison.Ordinal)
                < main.IndexOf("CreateMainForm(options, activeSessionCoordinator)", StringComparison.Ordinal));
            Assert.True(
                main.IndexOf("CreateMainForm(options, activeSessionCoordinator)", StringComparison.Ordinal)
                < main.IndexOf("mainForm.EnsureWebViewRuntimeAvailable()", StringComparison.Ordinal));
        }

        [Fact]
        public void UnsupportedWindowsPrompt_HasDefaultsAndAllLanguageOverrides()
        {
            string root = VerificationFixtureLocator.RepositoryRoot();
            string defaults = File.ReadAllText(Path.Combine(root, "readboard", "Program.cs"));
            string[] languages = { "cn", "en", "jp", "kr" };
            string[] keys =
            {
                "UnsupportedWindows_caption",
                "UnsupportedWindows_message"
            };

            foreach (string key in keys)
            {
                Assert.Contains("langItems[\"" + key + "\"]", defaults);
                foreach (string language in languages)
                {
                    string content = File.ReadAllText(Path.Combine(
                        root,
                        "readboard",
                        "language_" + language + ".txt"));
                    Assert.Contains(key + "=", content);
                }
            }
        }

        private static string GetMethodSlice(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing signature: " + signature);
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

            throw new InvalidOperationException("Could not slice method: " + signature);
        }
    }
}
