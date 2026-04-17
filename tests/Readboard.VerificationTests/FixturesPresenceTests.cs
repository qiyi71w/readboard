using System.IO;
using Xunit;

namespace Readboard.VerificationTests
{
    public sealed class FixturesPresenceTests
    {
        [Fact]
        public void ProtocolConfigAndRecognitionFixturesMustExist()
        {
            Assert.True(
                File.Exists(VerificationFixtureLocator.FixturePath(Path.Combine("protocol", "legacy-inbound-cases.txt"))),
                "Expected protocol fixture catalog to exist.");

            Assert.True(
                File.Exists(VerificationFixtureLocator.FixturePath(Path.Combine("protocol", "legacy-outbound-cases.txt"))),
                "Expected outbound protocol fixture catalog to exist.");

            Assert.True(
                File.Exists(VerificationFixtureLocator.FixturePath(Path.Combine("config", "legacy", "config_readboard.txt"))),
                "Expected legacy main config fixture to exist.");

            Assert.True(
                File.Exists(VerificationFixtureLocator.FixturePath(Path.Combine("config", "legacy", "config_readboard_others.txt"))),
                "Expected legacy other config fixture to exist.");

            Assert.True(
                File.Exists(VerificationFixtureLocator.FixturePath(Path.Combine("recognition", "replay", "foreground-5x5.json"))),
                "Expected recognition replay manifest to exist.");

            Assert.True(
                File.Exists(VerificationFixtureLocator.FixturePath(Path.Combine("recognition", "replay", "foreground-5x5-base.ppm"))),
                "Expected base replay screenshot fixture to exist.");

            Assert.True(
                File.Exists(VerificationFixtureLocator.FixturePath(Path.Combine("recognition", "replay", "foreground-5x5-changed.ppm"))),
                "Expected changed replay screenshot fixture to exist.");
        }
    }
}
