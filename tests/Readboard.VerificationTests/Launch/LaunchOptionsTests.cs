using Xunit;
using readboard;

namespace Readboard.VerificationTests.Launch
{
    public sealed class LaunchOptionsTests
    {
        [Fact]
        public void TryParse_ParsesTcpLaunchArguments()
        {
            LaunchOptions options;
            bool parsed = LaunchOptions.TryParse(
                new[] { "yzy", "10", "20", "policy", "1", "en", "9527" },
                out options);

            Assert.True(parsed);
            Assert.Equal("10", options.AiTime);
            Assert.Equal("20", options.Playouts);
            Assert.Equal("policy", options.FirstPolicy);
            Assert.Equal(TransportKind.Tcp, options.TransportKind);
            Assert.Equal("en", options.Language);
            Assert.Equal(9527, options.TcpPort);
        }

        [Fact]
        public void TryParse_DefaultsBlankLanguageToCnForPipeLaunch()
        {
            LaunchOptions options;
            bool parsed = LaunchOptions.TryParse(
                new[] { "yzy", " ", " ", " ", "0", " ", "0" },
                out options);

            Assert.True(parsed);
            Assert.Equal(TransportKind.Pipe, options.TransportKind);
            Assert.Equal("cn", options.Language);
            Assert.Equal(0, options.TcpPort);
        }
        [Fact]
        public void TryParse_KeepsPositionalFieldsWhenNamedLoggingSuffixIsPresent()
        {
            string[] args =
            {
                "yzy",
                "10",
                "20",
                "policy",
                "1",
                "en",
                "9527",
                "--log-dir",
                @"C:\work\logs\readboard",
                "--host-session-id",
                "dGVzdEhvc3RTZXNzaW9u",
                "--logging-contract",
                "1",
                "--diagnostics",
                "on",
                "--capture",
                "off"
            };

            LaunchOptions withSuffix;
            LaunchOptions positionalOnly;
            Assert.True(LaunchOptions.TryParse(args, () => "dGVzdFByb2Nlc3NJRA", out withSuffix));
            Assert.True(LaunchOptions.TryParse(new[] { "yzy", "10", "20", "policy", "1", "en", "9527" }, out positionalOnly));

            Assert.Equal(positionalOnly.AiTime, withSuffix.AiTime);
            Assert.Equal(positionalOnly.Playouts, withSuffix.Playouts);
            Assert.Equal(positionalOnly.FirstPolicy, withSuffix.FirstPolicy);
            Assert.Equal(positionalOnly.TransportKind, withSuffix.TransportKind);
            Assert.Equal(positionalOnly.Language, withSuffix.Language);
            Assert.Equal(positionalOnly.TcpPort, withSuffix.TcpPort);
        }

        [Fact]
        public void TryParse_TreatsSevenPositionalArgsAsLegacyLaunch()
        {
            LaunchOptions options;
            Assert.True(LaunchOptions.TryParse(
                new[] { "yzy", "10", "20", "policy", "0", "cn", "-1" },
                () => "dGVzdFByb2Nlc3NJRA",
                out options));

            Assert.Equal(LoggingLaunchKind.Legacy, options.LoggingKind);
            Assert.True(options.UsesLegacyLogFallback);
            Assert.False(options.ShouldEmitLoggingCapability);
            Assert.False(options.ShouldEmitLoggingObserved);
            Assert.Equal("dGVzdFByb2Nlc3NJRA", options.ProcessSessionId);
            Assert.Null(options.LogDirectory);
            Assert.Null(options.HostSessionId);
        }

        [Fact]
        public void TryParse_ParsesCompleteContractLaunch()
        {
            LaunchOptions options;
            Assert.True(LaunchOptions.TryParse(
                new[]
                {
                    "yzy",
                    "10",
                    "20",
                    "policy",
                    "1",
                    "en",
                    "9527",
                    "--log-dir",
                    @"C:\work\logs\readboard",
                    "--host-session-id",
                    "dGVzdEhvc3RTZXNzaW9u",
                    "--logging-contract",
                    "1",
                    "--diagnostics",
                    "on",
                    "--capture",
                    "off"
                },
                () => "dGVzdFByb2Nlc3NJRA",
                out options));

            Assert.Equal(LoggingLaunchKind.Contract, options.LoggingKind);
            Assert.False(options.UsesLegacyLogFallback);
            Assert.True(options.ShouldEmitLoggingCapability);
            Assert.True(options.ShouldEmitLoggingObserved);
            Assert.Equal(@"C:\work\logs\readboard", options.LogDirectory);
            Assert.Equal("dGVzdEhvc3RTZXNzaW9u", options.HostSessionId);
            Assert.Equal("1", options.LoggingContractVersion);
            Assert.True(options.DiagnosticsEnabled);
            Assert.False(options.CaptureEnabled);
            Assert.Equal("dGVzdFByb2Nlc3NJRA", options.ProcessSessionId);
        }

        [Theory]
        [InlineData(@"logs\readboard")]
        [InlineData("./logs/readboard")]
        [InlineData("readboard")]
        [InlineData("/tmp/logs/readboard")]
        [InlineData(@"\\server")]
        [InlineData(@"\readboard")]
        public void TryParse_MarksRelativeLogDirAsUnavailableWithoutLegacyFallback(string relativeDir)
        {
            LaunchOptions options;
            Assert.True(LaunchOptions.TryParse(
                new[]
                {
                    "yzy",
                    "10",
                    "20",
                    "policy",
                    "1",
                    "en",
                    "9527",
                    "--log-dir",
                    relativeDir,
                    "--host-session-id",
                    "dGVzdEhvc3RTZXNzaW9u",
                    "--logging-contract",
                    "1"
                },
                () => "dGVzdFByb2Nlc3NJRA",
                out options));

            Assert.Equal(LoggingLaunchKind.Unavailable, options.LoggingKind);
            Assert.False(options.UsesLegacyLogFallback);
            Assert.True(options.ShouldEmitLoggingCapability);
            Assert.Equal("dGVzdFByb2Nlc3NJRA", options.ProcessSessionId);
        }

        [Fact]
        public void TryParse_MarksIncompleteLoggingSuffixAsUnavailable()
        {
            LaunchOptions options;
            Assert.True(LaunchOptions.TryParse(
                new[]
                {
                    "yzy",
                    "10",
                    "20",
                    "policy",
                    "1",
                    "en",
                    "9527",
                    "--logging-contract",
                    "1",
                    "--diagnostics",
                    "off"
                },
                () => "dGVzdFByb2Nlc3NJRA",
                out options));

            Assert.Equal(LoggingLaunchKind.Unavailable, options.LoggingKind);
            Assert.False(options.UsesLegacyLogFallback);
            Assert.True(options.ShouldEmitLoggingCapability);
            Assert.False(options.DiagnosticsEnabled);
            Assert.Equal("dGVzdFByb2Nlc3NJRA", options.ProcessSessionId);
        }

        [Fact]
        public void TryParse_MarksMalformedToggleAsUnavailable()
        {
            LaunchOptions options;
            Assert.True(LaunchOptions.TryParse(
                new[]
                {
                    "yzy",
                    "10",
                    "20",
                    "policy",
                    "1",
                    "en",
                    "9527",
                    "--log-dir",
                    @"C:\work\logs\readboard",
                    "--host-session-id",
                    "dGVzdEhvc3RTZXNzaW9u",
                    "--logging-contract",
                    "1",
                    "--diagnostics",
                    "maybe"
                },
                () => "dGVzdFByb2Nlc3NJRA",
                out options));

            Assert.Equal(LoggingLaunchKind.Unavailable, options.LoggingKind);
            Assert.False(options.UsesLegacyLogFallback);
            Assert.Null(options.DiagnosticsEnabled);
        }

        [Fact]
        public void TryParse_IgnoresUnknownNamedPairsAfterConsumingKnownValues()
        {
            LaunchOptions options;
            Assert.True(LaunchOptions.TryParse(
                new[]
                {
                    "yzy",
                    "10",
                    "20",
                    "policy",
                    "1",
                    "en",
                    "9527",
                    "--log-dir",
                    @"C:\work\logs\readboard",
                    "--host-session-id",
                    "dGVzdEhvc3RTZXNzaW9u",
                    "--logging-contract",
                    "1",
                    "--future-flag",
                    "xyz"
                },
                () => "dGVzdFByb2Nlc3NJRA",
                out options));

            Assert.Equal(LoggingLaunchKind.Contract, options.LoggingKind);
            Assert.Equal(@"C:\work\logs\readboard", options.LogDirectory);
            Assert.False(options.DiagnosticsEnabled);
            Assert.False(options.CaptureEnabled);
        }

        [Fact]
        public void TryParse_DoesNotReinterpretOrphanValueAsPositionalArgument()
        {
            LaunchOptions options;
            Assert.True(LaunchOptions.TryParse(
                new[] { "yzy", "10", "20", "policy", "0", "cn", "-1", "leftover" },
                out options));

            Assert.Equal(TransportKind.Pipe, options.TransportKind);
            Assert.Equal("cn", options.Language);
            Assert.Equal(-1, options.TcpPort);
            Assert.Equal(LoggingLaunchKind.Legacy, options.LoggingKind);
        }

        [Fact]
        public void TryParse_StillFailsWhenHostMarkerOrPositionalArityIsMissing()
        {
            LaunchOptions options;
            Assert.False(LaunchOptions.TryParse(null, out options));
            Assert.False(LaunchOptions.TryParse(new string[0], out options));
            Assert.False(LaunchOptions.TryParse(new[] { "nope", "10", "20", "policy", "1", "en", "9527" }, out options));
        }
    }
}
