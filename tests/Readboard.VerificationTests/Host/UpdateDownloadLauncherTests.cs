using System;
using System.IO;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Host
{
    public sealed class UpdateDownloadLauncherTests
    {
        [Fact]
        public void CreateDownloadStartInfo_OpensUrlThroughShell()
        {
            Uri downloadUri = new Uri("https://github.com/qiyi71w/readboard/releases/tag/v3.0.1");

            var startInfo = FormUpdate.CreateDownloadStartInfo(downloadUri);

            Assert.Equal(downloadUri.AbsoluteUri, startInfo.FileName);
            Assert.True(startInfo.UseShellExecute);
        }

        [Fact]
        public void CreateDownloadStartInfo_ThrowsOnNullUri()
        {
            ArgumentNullException exception =
                Assert.Throws<ArgumentNullException>(() => FormUpdate.CreateDownloadStartInfo(null));

            Assert.Equal("downloadUri", exception.ParamName);
        }

        [Fact]
        public void DownloadClick_UsesShellLauncherAndLogsFailures()
        {
            string source = LoadReadboardSource("FormUpdate.cs");
            string clickSlice = GetMethodSlice(source, "private void btnDownload_Click(object sender, EventArgs e)");
            string openSlice = GetMethodSlice(source, "internal static void OpenDownloadUri(Uri downloadUri)");

            Assert.Contains("OpenDownloadUri(downloadUri);", clickSlice);
            Assert.Contains("catch (Exception exception)", clickSlice);
            Assert.Contains("Trace.TraceError(exception.ToString());", clickSlice);
            Assert.Contains("using (Process process = Process.Start(CreateDownloadStartInfo(downloadUri)))", openSlice);
        }

        private static string LoadReadboardSource(string fileName)
        {
            string directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                string candidate = Path.Combine(directory, "readboard", fileName);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            string assemblyDirectory = Path.GetDirectoryName(typeof(FormUpdate).Assembly.Location);
            string fallback = Path.Combine(assemblyDirectory ?? string.Empty, fileName);
            if (File.Exists(fallback))
            {
                return File.ReadAllText(fallback);
            }

            throw new FileNotFoundException("Could not locate readboard source file.", fileName);
        }

        private static string GetMethodSlice(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing method signature: " + signature);

            int braceStart = source.IndexOf('{', start);
            Assert.True(braceStart >= 0, "Missing method body: " + signature);

            int depth = 0;
            for (int index = braceStart; index < source.Length; index++)
            {
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(start, index - start + 1);
                    }
                }
            }

            throw new InvalidOperationException("Could not parse method body: " + signature);
        }
    }
}
