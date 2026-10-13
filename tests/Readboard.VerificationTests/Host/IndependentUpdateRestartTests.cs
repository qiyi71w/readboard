using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class IndependentUpdateRestartTests
    {
        [Theory]
        [InlineData("readboardSelfUpdateRestartSupported\t2")]
        [InlineData("readboardSelfUpdateRestartSupported\t1\textra")]
        [InlineData("readboardSelfUpdateRestartSupported\t1\r\n")]
        [InlineData("readboardSelfUpdateRestartSupported\t1\n\n")]
        public void MalformedCapabilityDoesNotEnableRestart(string line)
        {
            var protocol = new IndependentUpdateRestartProtocol(_ => { });
            Assert.True(protocol.Handle(line));
            Assert.False(protocol.Supported);
        }

        [Fact]
        public void LateAcceptanceIsCancelledAndCannotEnableRestart()
        {
            var sent = new List<string>();
            var protocol = new IndependentUpdateRestartProtocol(sent.Add);
            protocol.Handle("readboardSelfUpdatePrepared\t1\t0123456789abcdef0123456789abcdef\n");
            Assert.Equal(new[] { "readboardSelfUpdateCancel\t1\t0123456789abcdef0123456789abcdef" }, sent);
            Assert.False(protocol.Supported);
        }

        [Theory]
        [InlineData("--update-transaction")]
        [InlineData("--update-transaction", "relative/request.json")]
        [InlineData("--update-transaction", "C:\\request\n.json")]
        [InlineData("--update-transaction", "C:\\one.json", "--update-transaction", "C:\\two.json")]
        public void InvalidTransactionSuffixRejectsLaunch(params string[] suffix)
        {
            var args = new List<string> { "yzy", " ", " ", " ", "0", "en", "-1" };
            args.AddRange(suffix);
            Assert.False(LaunchOptions.TryParse(args.ToArray(), out _));
        }

        [Fact]
        public void TransactionSuffixRetainsPositionalAndLoggingArguments()
        {
            Assert.True(LaunchOptions.TryParse(new[] { "yzy", "2", "3", "4", "0", "en", "-1", "--update-transaction", "C:\\updates\\request.json" }, out var options));
            Assert.Equal("2", options.AiTime);
            Assert.Equal("3", options.Playouts);
            Assert.Equal("4", options.FirstPolicy);
            Assert.Equal("C:\\updates\\request.json", options.UpdateTransaction);
            Assert.Equal(LoggingLaunchKind.Legacy, options.LoggingKind);
        }

        [Fact]
        public void ConnectedReplyRequiresExactOperationField()
        {
            string observed = null;
            var protocol = new IndependentUpdateRestartProtocol(_ => { });
            protocol.Connected += value => observed = value;
            protocol.Handle("readboardSelfUpdateConnected\t1\twrong");
            protocol.Handle("readboardSelfUpdateConnected\t1\t0123456789abcdef0123456789abcdef\textra");
            Assert.Null(observed);
            protocol.Handle("readboardSelfUpdateConnected\t1\t0123456789abcdef0123456789abcdef\n");
            Assert.Equal("0123456789abcdef0123456789abcdef", observed);
        }
    }
}
