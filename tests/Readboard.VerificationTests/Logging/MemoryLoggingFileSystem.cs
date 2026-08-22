using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using readboard;

namespace Readboard.VerificationTests.Logging
{
    internal sealed class FakeLoggingClock : ILoggingClock
    {
        public FakeLoggingClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; set; }
    }

    internal sealed class MemoryLoggingFileSystem : ILoggingFileSystem
    {
        private readonly Dictionary<string, MemoryFile> files = new Dictionary<string, MemoryFile>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object sync = new object();
        public bool FailCreateDirectory { get; set; }
        public bool FailAppend { get; set; }
        public bool FailGzip { get; set; }
        public bool FailDelete { get; set; }
        public bool FailListFiles { get; set; }
        public ManualResetEventSlim BlockAppend { get; set; }

        public bool TryCreateDirectory(string path)
        {
            if (FailCreateDirectory || string.IsNullOrWhiteSpace(path))
                return false;
            lock (sync)
            {
                AddDirectory(path);
                return true;
            }
        }

        public bool DirectoryExists(string path)
        {
            lock (sync)
                return directories.Contains(Normalize(path));
        }

        public bool FileExists(string path)
        {
            lock (sync)
                return files.ContainsKey(Normalize(path));
        }

        public long GetLength(string path)
        {
            lock (sync)
            {
                MemoryFile file;
                if (!files.TryGetValue(Normalize(path), out file))
                    return 0;
                return file.Length;
            }
        }

        public DateTime GetLastWriteUtc(string path)
        {
            lock (sync)
            {
                MemoryFile file;
                if (!files.TryGetValue(Normalize(path), out file))
                    return DateTime.MinValue;
                return file.LastWriteUtc;
            }
        }

        public bool TryReadAllBytes(string path, out byte[] bytes)
        {
            lock (sync)
            {
                MemoryFile file;
                if (!files.TryGetValue(Normalize(path), out file))
                {
                    bytes = null;
                    return false;
                }
                bytes = file.Content;
                return true;
            }
        }

        public bool TryWriteAllBytes(string path, byte[] bytes)
        {
            if (bytes == null)
                return false;
            lock (sync)
            {
                string normalized = Normalize(path);
                AddDirectory(Parent(normalized));
                files[normalized] = new MemoryFile
                {
                    Content = (byte[])bytes.Clone(),
                    LastWriteUtc = DateTime.UtcNow
                };
                return true;
            }
        }

        public bool TryAppend(string path, byte[] bytes)
        {
            if (BlockAppend != null)
                BlockAppend.Wait();
            if (FailAppend || bytes == null)
                return false;
            lock (sync)
            {
                string normalized = Normalize(path);
                AddDirectory(Parent(normalized));
                MemoryFile file;
                if (!files.TryGetValue(normalized, out file))
                {
                    file = new MemoryFile
                    {
                        Content = new byte[0],
                        LastWriteUtc = DateTime.UtcNow
                    };
                    files[normalized] = file;
                }
                byte[] next = new byte[file.Content.Length + bytes.Length];
                Buffer.BlockCopy(file.Content, 0, next, 0, file.Content.Length);
                Buffer.BlockCopy(bytes, 0, next, file.Content.Length, bytes.Length);
                file.Content = next;
                if (file.ReportedLength.HasValue)
                    file.ReportedLength = file.ReportedLength.Value + bytes.Length;
                file.LastWriteUtc = DateTime.UtcNow;
                return true;
            }
        }

        public bool TryMove(string sourcePath, string destinationPath)
        {
            lock (sync)
            {
                string source = Normalize(sourcePath);
                string destination = Normalize(destinationPath);
                MemoryFile file;
                if (!files.TryGetValue(source, out file))
                    return false;
                AddDirectory(Parent(destination));
                files[destination] = file;
                files.Remove(source);
                return true;
            }
        }

        public bool TryDelete(string path)
        {
            if (FailDelete)
                return false;
            lock (sync)
            {
                files.Remove(Normalize(path));
                return true;
            }
        }

        public bool TryCreateGzip(string sourcePath, string destinationPath)
        {
            if (FailGzip)
                return false;
            byte[] source;
            if (!TryReadAllBytes(sourcePath, out source))
                return false;
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, true))
                    gzip.Write(source, 0, source.Length);
                return TryWriteAllBytes(destinationPath, output.ToArray());
            }
        }

        public bool TryListFiles(string directory, out IList<string> listed)
        {
            if (FailListFiles)
            {
                listed = new List<string>();
                return false;
            }

            lock (sync)
            {
                string prefix = Normalize(directory);
                if (!prefix.EndsWith("\\", StringComparison.Ordinal))
                    prefix += "\\";
                List<string> matches = new List<string>();
                foreach (string path in files.Keys)
                {
                    if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string remainder = path.Substring(prefix.Length);
                    if (remainder.Length > 0 && remainder.IndexOf('\\') < 0)
                        matches.Add(path);
                }
                listed = matches;
                return true;
            }
        }

        public IList<string> ListFiles(string directory)
        {
            IList<string> listed;
            TryListFiles(directory, out listed);
            return listed;
        }

        public void SetReportedLength(string path, long length)
        {
            lock (sync)
            {
                string normalized = Normalize(path);
                MemoryFile file;
                if (!files.TryGetValue(normalized, out file))
                {
                    file = new MemoryFile
                    {
                        Content = new byte[0],
                        LastWriteUtc = DateTime.UtcNow
                    };
                    files[normalized] = file;
                    AddDirectory(Parent(normalized));
                }
                file.ReportedLength = length;
            }
        }

        public void SetLastWriteUtc(string path, DateTime utc)
        {
            lock (sync)
            {
                MemoryFile file;
                if (files.TryGetValue(Normalize(path), out file))
                    file.LastWriteUtc = utc;
            }
        }

        public bool HasPathPrefix(string prefix)
        {
            string normalized = Normalize(prefix);
            lock (sync)
            {
                foreach (string directory in directories)
                {
                    if (directory.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                foreach (string path in files.Keys)
                {
                    if (path.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        public string ReadAllText(string path)
        {
            byte[] bytes;
            if (!TryReadAllBytes(path, out bytes))
                return null;
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private void AddDirectory(string path)
        {
            string current = Normalize(path);
            while (!string.IsNullOrEmpty(current))
            {
                directories.Add(current);
                string parent = Parent(current);
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = parent;
            }
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            return path.Replace('/', '\\').TrimEnd('\\');
        }

        private static string Parent(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            int index = path.LastIndexOf('\\');
            if (index <= 0)
                return path;
            if (index == 2 && path.Length >= 2 && path[1] == ':')
                return path.Substring(0, 2) + "\\";
            return path.Substring(0, index);
        }

        private sealed class MemoryFile
        {
            public byte[] Content { get; set; }
            public long? ReportedLength { get; set; }
            public DateTime LastWriteUtc { get; set; }

            public long Length
            {
                get { return ReportedLength ?? Content.Length; }
            }
        }
    }
}
