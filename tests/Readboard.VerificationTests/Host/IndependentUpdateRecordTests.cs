using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class IndependentUpdateRecordTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "readboard-record-" + Guid.NewGuid().ToString("N"));
        public IndependentUpdateRecordTests() => Directory.CreateDirectory(root);
        public void Dispose() => Directory.Delete(root, true);

        [Fact]
        public void LiteralTildeInstallationLaunchesAndSharesItsAliasIdentity()
        {
            string install = Path.Combine(root, "literal~installation");
            Directory.CreateDirectory(install);
            IndependentUpdateFiles.AssertLaunchable(install);
            var alias = new StringBuilder(32768);
            Assert.NotEqual(0u, GetShortPathName(install, alias, (uint)alias.Capacity));
            Assert.Equal(IndependentUpdateFiles.InstallKey(install), IndependentUpdateFiles.InstallKey(alias.ToString()));
            Assert.Equal(IndependentUpdateFiles.RecordRoot(install), IndependentUpdateFiles.RecordRoot(alias.ToString()));
            Assert.Throws<InvalidDataException>(() => IndependentUpdateFiles.ValidateRelativePath("PROGRA~1/file.dll"));
            Assert.Throws<InvalidDataException>(() => IndependentUpdateFiles.ValidateRelativePath("../file.dll"));
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(string path, StringBuilder shortPath, uint length);

        [Fact]
        public void AtomicPublicationPreservesHeldReaderSnapshotAndExposesNewRecord()
        {
            string path = Path.Combine(root, "active.json");
            string first = new string('a', 32), second = new string('b', 32);
            IndependentUpdateFiles.Publish(path, new IndependentUpdateActive { OperationId = first });
            byte[] security = new FileInfo(path).GetAccessControl().GetSecurityDescriptorBinaryForm();
            using var held = IndependentUpdateFiles.OpenReadRecord(path);
            IndependentUpdateFiles.Publish(path, new IndependentUpdateActive { OperationId = second });
            using var snapshot = JsonDocument.Parse(held);
            Assert.Equal(first, snapshot.RootElement.GetProperty("operationId").GetString());
            Assert.Equal(second, IndependentUpdateFiles.Read<IndependentUpdateActive>(path).OperationId);
            Assert.Equal(security, new FileInfo(path).GetAccessControl().GetSecurityDescriptorBinaryForm());
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        }

        [Fact]
        public void PermanentReplacementDenialPreservesOriginalRecordAndPropagatesFailure()
        {
            string path = Path.Combine(root, "active.json");
            IndependentUpdateFiles.Publish(path, new IndependentUpdateActive { OperationId = new string('a', 32) });
            byte[] before = File.ReadAllBytes(path);
            using (var denied = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsAny<IOException>(() => IndependentUpdateFiles.Publish(path, new IndependentUpdateActive { OperationId = new string('b', 32) }));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"schemaVersion\":1,\"operationId\":\"a\",\"operationId\":\"b\"}")]
        [InlineData("{broken")]
        public void InvalidConnectedRecordLeavesPresentationAndAcknowledgementUntouched(string record)
        {
            string request = Path.Combine(root, "request.json");
            File.WriteAllText(request, record);
            MainForm form = CreateForm(request);
            var state = new ReadBoardUpdateUiState { Status = "idle" };
            Field("webViewUpdateState").SetValue(form, state);
            Invoke(form, "ReceiveUpdateConnected", new string('a', 32));
            Assert.Same(state, Field("webViewUpdateState").GetValue(form));
            Assert.Null(Field("pendingUpdatePresentation").GetValue(form));
            Assert.False((bool)Field("updateResultPresented").GetValue(form));
            Assert.False(File.Exists(Path.Combine(root, "presented.json")));
        }

        [Fact]
        public void FailedPresentationPublicationCanBeAcknowledgedOnceAfterAccessReturns()
        {
            MainForm form = CreateForm(Path.Combine(root, "request.json"));
            var pending = new IndependentUpdatePresented { OperationId = new string('a', 32) };
            Field("pendingUpdatePresentation").SetValue(form, pending);
            string path = Path.Combine(root, "presented.json");
            Directory.CreateDirectory(path);
            using var payload = JsonDocument.Parse("{\"operationId\":\"" + pending.OperationId + "\"}");
            Assert.False((bool)Invoke(form, "AcknowledgeUpdatePresentation", payload.RootElement));
            Assert.False((bool)Field("updateResultPresented").GetValue(form));
            Assert.Same(pending, Field("pendingUpdatePresentation").GetValue(form));
            Directory.Delete(path);
            Assert.True((bool)Invoke(form, "AcknowledgeUpdatePresentation", payload.RootElement));
            Assert.Equal(pending.OperationId, IndependentUpdateFiles.Read<IndependentUpdatePresented>(path).OperationId);
            byte[] published = File.ReadAllBytes(path);
            Assert.False((bool)Invoke(form, "AcknowledgeUpdatePresentation", payload.RootElement));
            Assert.Equal(published, File.ReadAllBytes(path));
        }

        private static MainForm CreateForm(string request)
        {
            var form = (MainForm)RuntimeHelpers.GetUninitializedObject(typeof(MainForm));
            GC.SuppressFinalize(form); // This record-boundary probe has no constructed WinForms resources.
            Assert.True(LaunchOptions.TryParse(new[] { "yzy", " ", " ", " ", "0", "en", "-1", "--update-transaction", request }, out var options));
            Field("launchOptions").SetValue(form, options);
            Field("updateReadySent").SetValue(form, true);
            return form;
        }
        private static FieldInfo Field(string name) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        private static object Invoke(MainForm form, string name, object argument) => typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new[] { argument });
    }
}
