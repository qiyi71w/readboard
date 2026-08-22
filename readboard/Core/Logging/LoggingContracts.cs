using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace readboard
{
    internal sealed class LoggingField
    {
        public object Value { get; set; }
        public LoggingPrivacy Privacy { get; set; }

        public static LoggingField Safe(object value)
        {
            return new LoggingField
            {
                Value = value,
                Privacy = LoggingPrivacy.Safe
            };
        }

        public static LoggingField Tagged(object value, LoggingPrivacy privacy)
        {
            return new LoggingField
            {
                Value = value,
                Privacy = privacy
            };
        }
    }

    internal sealed class LoggingRecord
    {
        public DateTime TimestampUtc { get; set; }
        public LogLevel Level { get; set; }
        public string Stream { get; set; }
        public string EventId { get; set; }
        public string Module { get; set; }
        public string HostSessionId { get; set; }
        public string ProcessSessionId { get; set; }
        public string CorrelationId { get; set; }
        public IDictionary<string, LoggingField> Fields { get; set; }
        public string SemanticKey { get; set; }
        public IDictionary<string, LoggingField> SemanticArgs { get; set; }
        public Exception Exception { get; set; }
        public bool DiagnosticOnly { get; set; }
        public IList<LoggingRecord> CrashTail { get; set; }
    }

    internal sealed class LoggingObservedSnapshot
    {
        public string ProcessSessionId { get; set; }
        public string HostSessionId { get; set; }
        public string LogRoot { get; set; }
        public LoggingToggle Diagnostics { get; set; }
        public LoggingToggle Capture { get; set; }
        public LoggingToggle Trace { get; set; }
        public LoggingPersistenceHealth AppHealth { get; set; }
        public LoggingPersistenceHealth TraceHealth { get; set; }
        public LoggingPersistenceHealth CrashHealth { get; set; }
        public LoggingPersistenceHealth CaptureHealth { get; set; }
        public LoggingPersistenceHealth Persistence { get; set; }
        public int RuntimeDropCount { get; set; }
        public int TraceDropCount { get; set; }
        public int DropCount { get; set; }
        public LoggingFailureReason Reason { get; set; }
    }

    internal interface ILoggingClock
    {
        DateTime UtcNow { get; }
    }

    internal sealed class SystemLoggingClock : ILoggingClock
    {
        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }
    }

    internal interface ILoggingFileSystem
    {
        bool TryCreateDirectory(string path);
        bool DirectoryExists(string path);
        bool FileExists(string path);
        long GetLength(string path);
        DateTime GetLastWriteUtc(string path);
        bool TryReadAllBytes(string path, out byte[] bytes);
        bool TryWriteAllBytes(string path, byte[] bytes);
        bool TryAppend(string path, byte[] bytes);
        bool TryMove(string sourcePath, string destinationPath);
        bool TryDelete(string path);
        bool TryCreateGzip(string sourcePath, string destinationPath);
        bool TryListFiles(string directory, out IList<string> files);
    }

    internal sealed class RealLoggingFileSystem : ILoggingFileSystem
    {
        public bool TryCreateDirectory(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return false;
                Directory.CreateDirectory(path);
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public bool DirectoryExists(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public bool FileExists(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public long GetLength(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return 0;
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        public DateTime GetLastWriteUtc(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public bool TryReadAllBytes(string path, out byte[] bytes)
        {
            bytes = null;
            try
            {
                if (!File.Exists(path))
                    return false;
                bytes = File.ReadAllBytes(path);
                return true;
            }
            catch
            {
                bytes = null;
                return false;
            }
        }

        public bool TryWriteAllBytes(string path, byte[] bytes)
        {
            try
            {
                if (bytes == null)
                    return false;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryAppend(string path, byte[] bytes)
        {
            try
            {
                if (bytes == null)
                    return false;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                using (FileStream stream = new FileStream(
                    path,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite))
                {
                    stream.Write(bytes, 0, bytes.Length);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryMove(string sourcePath, string destinationPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);
                File.Move(sourcePath, destinationPath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return !File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public bool TryCreateGzip(string sourcePath, string destinationPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                using (FileStream input = File.OpenRead(sourcePath))
                using (FileStream output = File.Create(destinationPath))
                using (GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal))
                {
                    input.CopyTo(gzip);
                }
                return File.Exists(destinationPath);
            }
            catch
            {
                return false;
            }
        }

        public bool TryListFiles(string directory, out IList<string> files)
        {
            files = new List<string>();
            try
            {
                if (!Directory.Exists(directory))
                    return true;
                string[] found = Directory.GetFiles(directory);
                for (int i = 0; i < found.Length; i++)
                    files.Add(found[i]);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
