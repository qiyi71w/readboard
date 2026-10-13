using System;
using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class IndependentUpdateTransactionTests
    {
        [Theory]
        [InlineData("../escape.dll")]
        [InlineData("C:/escape.dll")]
        [InlineData("file.dll:stream")]
        [InlineData("CON.txt")]
        [InlineData("dir/file. ")]
        [InlineData("dir//file")]
        [InlineData("dir/./file")]
        [InlineData("dir/COM1.dll")]
        public void WindowsAliasesCannotBecomePackagePaths(string path)
        {
            Assert.Throws<InvalidDataException>(() => readboard.IndependentUpdateFiles.ValidateRelativePath(path));
        }

        [Fact]
        public void InterruptedTransactionPreventsBusinessStartup()
        {
            string root = Path.Combine(Path.GetTempPath(), "readboard-update-test-" + Guid.NewGuid().ToString("N"));
            string install = Path.Combine(root, "app");
            try
            {
                Directory.CreateDirectory(install);
                string records = readboard.IndependentUpdateFiles.RecordRoot(install);
                Directory.CreateDirectory(records);
                File.WriteAllText(Path.Combine(records, "active.json"), "{broken");
                Assert.ThrowsAny<Exception>(() => readboard.IndependentUpdateFiles.AssertLaunchable(install));
                Assert.True(Directory.Exists(install));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
