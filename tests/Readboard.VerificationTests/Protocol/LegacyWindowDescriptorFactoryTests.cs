using System;
using System.IO;
using Xunit;

namespace Readboard.VerificationTests.Protocol
{
    public sealed class LegacyWindowDescriptorFactoryTests
    {
        [Fact]
        public void GetSupportDpiState_DisposesProcessWithinDpiProbeScope()
        {
            string source = LoadSource("readboard", "Core", "Protocol", "LegacyWindowDescriptorFactory.cs");
            string methodSlice = GetMethodSlice(source, "private static bool GetSupportDpiState(IntPtr handle)");

            Assert.Contains("using (Process process = Process.GetProcessById(processId))", methodSlice);
            Assert.Contains("GetProcessDpiAwareness(process.Handle, out awareness)", methodSlice);
        }

        private static string LoadSource(params string[] segments)
        {
            string path = Path.Combine(VerificationFixtureLocator.RepositoryRoot(), Path.Combine(segments));
            return File.ReadAllText(path);
        }

        private static int IndexOfRequired(string source, string value)
        {
            int index = source.IndexOf(value, StringComparison.Ordinal);
            Assert.True(index >= 0, "Expected to find source fragment: " + value);
            return index;
        }

        private static string GetMethodSlice(string source, string methodSignature)
        {
            int startIndex = IndexOfRequired(source, methodSignature);
            int nextMethodIndex = source.IndexOf("\n        private ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            int publicMethodIndex = source.IndexOf("\n        public ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            int internalMethodIndex = source.IndexOf("\n        internal ", startIndex + methodSignature.Length, StringComparison.Ordinal);
            if (publicMethodIndex >= 0 && (nextMethodIndex < 0 || publicMethodIndex < nextMethodIndex))
                nextMethodIndex = publicMethodIndex;
            if (internalMethodIndex >= 0 && (nextMethodIndex < 0 || internalMethodIndex < nextMethodIndex))
                nextMethodIndex = internalMethodIndex;
            if (nextMethodIndex < 0)
                nextMethodIndex = source.Length;
            return source.Substring(startIndex, nextMethodIndex - startIndex);
        }
    }
}
