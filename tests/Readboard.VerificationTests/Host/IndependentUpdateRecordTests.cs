using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
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

        [Theory]
        [InlineData(null, false)]
        [InlineData("preparation.json", true)]
        [InlineData("progress.json", true)]
        public void SourceCleanupRetainsRecordsOnceUpdaterOwnsPreparation(string record, bool retained)
        {
            string operation = Path.Combine(root, "operation");
            Directory.CreateDirectory(operation);
            File.WriteAllText(Path.Combine(operation, "request.json"), "immutable request");
            if (record != null) File.WriteAllText(Path.Combine(operation, record), "durable updater evidence");
            var committer = new IndependentUpdateCommitter(() => "en", () => Array.Empty<string>());
            typeof(IndependentUpdateCommitter).GetField("preparedOperation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(committer, operation);
            committer.CleanupPreparation();
            Assert.Equal(retained, Directory.Exists(operation));
            if (retained) Assert.Equal("immutable request", File.ReadAllText(Path.Combine(operation, "request.json")));
        }

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

        [Theory]
        [InlineData("inherited")]
        [InlineData("explicit-deny")]
        [InlineData("protected")]
        [InlineData("legacy-inheritance")]
        public void AtomicPublicationPreservesHeldReaderSnapshotAndExposesNewRecord(string mode)
        {
            using var identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier sid = identity.User;
            var parent = new DirectorySecurity();
            parent.SetAccessRuleProtection(true, false);
            parent.SetOwner(sid);
            parent.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(root).SetAccessControl(parent);

            string path = Path.Combine(root, "active.json");
            string first = new string('a', 32), second = new string('b', 32);
            IndependentUpdateFiles.Publish(path, new IndependentUpdateActive { OperationId = first });
            bool deniedWrite = mode == "explicit-deny" || mode == "protected";
            FileSecurity security = new FileInfo(path).GetAccessControl();
            if (deniedWrite)
            {
                if (mode == "protected") security.SetAccessRuleProtection(true, true);
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.WriteData, AccessControlType.Deny));
                new FileInfo(path).SetAccessControl(security);
            }
            if (mode == "legacy-inheritance")
            {
                // Legacy ACLs can acquire AUTO_INHERITED during ReplaceFile without changing access.
                var descriptor = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0);
                descriptor.SetFlags(descriptor.ControlFlags & ~ControlFlags.DiscretionaryAclAutoInherited);
                byte[] bytes = new byte[descriptor.BinaryLength];
                descriptor.GetBinaryForm(bytes, 0);
                Assert.True(SetFileSecurity(path, 4 /* DACL_SECURITY_INFORMATION */, bytes),
                    "Setting isolated legacy DACL failed: " + Marshal.GetLastWin32Error());
            }
            security = new FileInfo(path).GetAccessControl();
            var beforeRules = AccessRules(security);
            Assert.Equal(mode == "protected", security.AreAccessRulesProtected);
            Assert.Equal(sid, security.GetOwner(typeof(SecurityIdentifier)));
            Assert.Equal(deniedWrite ? 2 : 1, beforeRules.Length);
            if (deniedWrite)
                Assert.Equal((sid.Value, FileSystemRights.WriteData, AccessControlType.Deny, false,
                    InheritanceFlags.None, PropagationFlags.None), beforeRules[0]);
            Assert.Equal((sid.Value, FileSystemRights.FullControl, AccessControlType.Allow, mode != "protected",
                InheritanceFlags.None, PropagationFlags.None), beforeRules[^1]);
            if (mode == "legacy-inheritance")
                Assert.False(new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0)
                    .ControlFlags.HasFlag(ControlFlags.DiscretionaryAclAutoInherited));
            AssertRecordAccess(path, deniedWrite);
            using var held = IndependentUpdateFiles.OpenReadRecord(path);
            IndependentUpdateFiles.Publish(path, new IndependentUpdateActive { OperationId = second });
            using var snapshot = JsonDocument.Parse(held);
            Assert.Equal(first, snapshot.RootElement.GetProperty("operationId").GetString());
            Assert.Equal(second, IndependentUpdateFiles.Read<IndependentUpdateActive>(path).OperationId);
            FileSecurity after = new FileInfo(path).GetAccessControl();
            Assert.Equal(security.GetOwner(typeof(SecurityIdentifier)), after.GetOwner(typeof(SecurityIdentifier)));
            Assert.Equal(security.GetGroup(typeof(SecurityIdentifier)), after.GetGroup(typeof(SecurityIdentifier)));
            Assert.Equal(security.AreAccessRulesProtected, after.AreAccessRulesProtected);
            Assert.Equal(beforeRules, AccessRules(after));
            AssertRecordAccess(path, deniedWrite);
            Assert.Empty(Directory.GetFiles(root, "*.tmp-*"));
        }

        private static (string Sid, FileSystemRights Rights, AccessControlType Type, bool Inherited,
            InheritanceFlags Inheritance, PropagationFlags Propagation)[] AccessRules(FileSecurity security) =>
            security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                .Select(rule => (rule.IdentityReference.Value, rule.FileSystemRights, rule.AccessControlType,
                    rule.IsInherited, rule.InheritanceFlags, rule.PropagationFlags)).ToArray();

        private static void AssertRecordAccess(string path, bool deniedWrite)
        {
            using (var readable = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                Assert.True(readable.CanRead);
            if (deniedWrite)
                Assert.Throws<UnauthorizedAccessException>(() =>
                {
                    using var unexpected = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                });
            else
            {
                using var writable = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                Assert.True(writable.CanWrite);
            }
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetFileSecurity(string path, uint information, byte[] descriptor);

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
