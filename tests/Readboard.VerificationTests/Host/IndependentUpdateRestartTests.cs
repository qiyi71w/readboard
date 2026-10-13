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
        [Fact]
        public async Task AcceptedRegistrationBindsImmutableRequestAndDuplicatesAreIdempotent()
        {
            using var transaction = new Transaction();
            var sent = new List<string>();
            var protocol = new IndependentUpdateRestartProtocol(sent.Add);
            protocol.Handle("readboardSelfUpdateRestartSupported\t1");
            Task<IndependentUpdateAdoption> pending = protocol.PrepareAsync(transaction.Path, CancellationToken.None);
            protocol.Handle("readboardSelfUpdatePrepared\t1\tffffffffffffffffffffffffffffffff");
            Assert.False(pending.IsCompleted);
            transaction.Register();
            protocol.Handle("readboardSelfUpdatePrepared\t1\t" + transaction.Request.OperationId);
            var adopted = await pending;
            protocol.Handle("readboardSelfUpdatePrepared\t1\t" + transaction.Request.OperationId);
            Assert.Equal(transaction.RegistrationId, adopted.RegistrationId);
            Assert.Equal(42, adopted.HostProcessId);
            Assert.DoesNotContain("readboardSelfUpdateCancel\t1\t" + transaction.Request.OperationId, sent);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task CancellationOrTimeoutRetiresLateAcceptance(bool cancel)
        {
            using var transaction = new Transaction();
            var sent = new List<string>();
            var protocol = new IndependentUpdateRestartProtocol(sent.Add, TimeSpan.FromMilliseconds(10));
            protocol.Handle("readboardSelfUpdateRestartSupported\t1");
            using var cancellation = new CancellationTokenSource();
            var pending = protocol.PrepareAsync(transaction.Path, cancellation.Token);
            if (cancel)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            }
            else Assert.Null((await pending).RegistrationId);
            transaction.Register();
            protocol.Handle("readboardSelfUpdatePrepared\t1\t" + transaction.Request.OperationId);
            Assert.Contains("readboardSelfUpdateCancel\t1\t" + transaction.Request.OperationId, sent);
        }

        [Fact]
        public void ChangedRequestCannotReuseRegistration()
        {
            using var transaction = new Transaction();
            transaction.Register();
            transaction.Request.TargetTag = "v9.9.9";
            IndependentUpdateFiles.Publish(transaction.Path, transaction.Request);
            Assert.Throws<InvalidDataException>(() => IndependentUpdateRestart.ReadRegistration(transaction.Path, transaction.Request));
        }

        private sealed class Transaction : IDisposable
        {
            private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "readboard-restart-" + Guid.NewGuid().ToString("N"));
            internal readonly string RegistrationId = Guid.NewGuid().ToString("N");
            internal IndependentUpdateRequest Request;
            internal string Path;
            internal Transaction()
            {
                string install = System.IO.Path.Combine(root, "readboard");
                Directory.CreateDirectory(install);
                Request = new IndependentUpdateRequest
                {
                    OperationId = Guid.NewGuid().ToString("N"), SourceVersion = "v3.1.9", TargetTag = "v3.2.0",
                    AssetName = "readboard-webview2-v3.2.0.zip", ExpectedSha256 = new string('a', 64),
                    InstallDirectory = install, SourceProcessId = 123, SourceProcessStartTimeUtc = DateTime.UtcNow, Language = "en",
                    PackagePath = System.IO.Path.Combine(root, "readboard-webview2-v3.2.0.zip")
                };
                string operation = IndependentUpdateFiles.OperationDirectory(install, Request.OperationId);
                Directory.CreateDirectory(operation);
                Path = System.IO.Path.Combine(operation, "request.json");
                IndependentUpdateFiles.Publish(Path, Request);
            }
            internal void Register()
            {
                IndependentUpdateFiles.Publish(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "registration.json"),
                    new IndependentUpdateRegistration
                    {
                        OperationId = Request.OperationId, RegistrationId = RegistrationId, HostProcessId = 42,
                        HostProcessStartTimeUtc = DateTime.UtcNow, SourceProcessId = Request.SourceProcessId,
                        SourceProcessStartTimeUtc = Request.SourceProcessStartTimeUtc, InstallDirectory = Request.InstallDirectory,
                        RequestSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path))).ToLowerInvariant()
                    });
            }
            public void Dispose() => Directory.Delete(root, true);
        }

    }
}
