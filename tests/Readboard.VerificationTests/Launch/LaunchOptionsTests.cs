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
    }
}
