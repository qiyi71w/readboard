using Xunit;
using readboard;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class FoxMoveNumberTitleParsingTests
    {
        [Theory]
        [InlineData("第57手", 57)]
        [InlineData("第 57 手", 57)]
        [InlineData("Fox 对局 第128手", 128)]
        public void Parse_ReturnsMoveNumberFromFoxTitle(string title, int expectedMoveNumber)
        {
            Assert.Equal(expectedMoveNumber, FoxMoveNumberParser.Parse(title));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Fox 对局")]
        [InlineData("第 手")]
        public void Parse_ReturnsNullWhenTitleDoesNotContainMoveNumber(string title)
        {
            Assert.Null(FoxMoveNumberParser.Parse(title));
        }
    }
}
