using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace readboard
{
    internal sealed class RollingFileSink
    {
        private readonly string streamName;
        private readonly string root;
        private readonly string archiveDirectory;
        private readonly string activePath;
        private readonly ILoggingClock clock;
        private readonly ILoggingFileSystem fileSystem;
        private readonly object sync = new object();
        private LoggingPersistenceHealth health;
        private bool writerFault;

        public RollingFileSink(
            string streamName,
            string root,
            ILoggingClock clock,
            ILoggingFileSystem fileSystem,
            LoggingPersistenceHealth initialHealth)
        {
            this.streamName = streamName;
            this.root = root;
            this.clock = clock;
            this.fileSystem = fileSystem;
            health = initialHealth;
            archiveDirectory = root == null ? null : Path.Combine(root, LoggingStreams.ArchiveDirectoryName);
            activePath = root == null ? null : Path.Combine(root, streamName + ".log");
        }

        public LoggingPersistenceHealth Health
        {
            get
            {
                lock (sync)
                    return health;
            }
        }

        public bool HasWriterFault
        {
            get
            {
                lock (sync)
                    return writerFault;
            }
        }

        public void Cleanup()
        {
            if (activePath == null)
                return;
            lock (sync)
                TryCleanupLocked();
        }

        public bool TryWriteLine(string line)
        {
            if (activePath == null || string.IsNullOrEmpty(line))
                return false;

            lock (sync)
            {
                if (health == LoggingPersistenceHealth.Unavailable)
                    return false;
                try
                {
                    if (fileSystem.GetLength(activePath) >= LoggingLimits.RollBytes
                        && !TryRollLocked())
                    {
                        MarkDegradedLocked();
                    }
                }
                catch
                {
                    MarkDegradedLocked();
                    return false;
                }
            }

            try
            {
                byte[] payload = Encoding.UTF8.GetBytes(line + "\n");
                if (fileSystem.TryAppend(activePath, payload))
                    return true;
                lock (sync)
                    MarkUnavailableLocked();
                return false;
            }
            catch
            {
                lock (sync)
                    MarkDegradedLocked();
                return false;
            }
        }

        private bool TryRollLocked()
        {
            if (!fileSystem.FileExists(activePath) && fileSystem.GetLength(activePath) <= 0)
                return true;
            if (!fileSystem.TryCreateDirectory(archiveDirectory))
            {
                MarkDegradedLocked();
                return false;
            }

            string stamp = clock.UtcNow.ToUniversalTime().ToString(
                "yyyyMMddTHHmmssZ",
                CultureInfo.InvariantCulture);
            string destination = Path.Combine(archiveDirectory, streamName + "." + stamp + ".log.gz");
            int suffix = 1;
            while (fileSystem.FileExists(destination))
            {
                destination = Path.Combine(
                    archiveDirectory,
                    streamName + "." + stamp + "-" + suffix.ToString(CultureInfo.InvariantCulture) + ".log.gz");
                suffix++;
            }

            if (!fileSystem.TryCreateGzip(activePath, destination))
            {
                MarkDegradedLocked();
                return false;
            }

            if (!fileSystem.TryDelete(activePath) && !fileSystem.TryWriteAllBytes(activePath, new byte[0]))
            {
                MarkDegradedLocked();
                return false;
            }

            TryCleanupLocked();
            return true;
        }

        private void TryCleanupLocked()
        {
            try
            {
                List<ArchiveEntry> archives;
                if (!TryListArchivesLocked(out archives))
                {
                    MarkDegradedLocked();
                    return;
                }
                DateTime cutoff = clock.UtcNow.ToUniversalTime().AddDays(-LoggingLimits.RetentionDays);
                for (int i = archives.Count - 1; i >= 0; i--)
                {
                    if (archives[i].LastWriteUtc <= cutoff)
                    {
                        if (!fileSystem.TryDelete(archives[i].Path))
                        {
                            MarkDegradedLocked();
                            continue;
                        }
                        archives.RemoveAt(i);
                    }
                }

                long total = fileSystem.GetLength(activePath);
                for (int i = 0; i < archives.Count; i++)
                    total += archives[i].Length;

                archives.Sort(CompareOldestFirst);
                int index = 0;
                while (total > LoggingLimits.ClassTotalBytes && index < archives.Count)
                {
                    if (!fileSystem.TryDelete(archives[index].Path))
                    {
                        MarkDegradedLocked();
                        index++;
                        continue;
                    }
                    total -= archives[index].Length;
                    index++;
                }
            }
            catch
            {
                MarkDegradedLocked();
            }
        }

        private bool TryListArchivesLocked(out List<ArchiveEntry> archives)
        {
            archives = new List<ArchiveEntry>();
            if (archiveDirectory == null)
                return true;

            IList<string> files;
            if (!fileSystem.TryListFiles(archiveDirectory, out files))
                return false;

            string prefix = streamName + ".";
            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                string name = Path.GetFileName(path);
                if (name == null
                    || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".log.gz", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                archives.Add(new ArchiveEntry
                {
                    Path = path,
                    Length = fileSystem.GetLength(path),
                    LastWriteUtc = fileSystem.GetLastWriteUtc(path)
                });
            }
            return true;
        }

        private static int CompareOldestFirst(ArchiveEntry left, ArchiveEntry right)
        {
            int byTime = left.LastWriteUtc.CompareTo(right.LastWriteUtc);
            if (byTime != 0)
                return byTime;
            return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
        }

        private void MarkDegradedLocked()
        {
            writerFault = true;
            if (health == LoggingPersistenceHealth.Healthy)
                health = LoggingPersistenceHealth.Degraded;
        }

        private void MarkUnavailableLocked()
        {
            writerFault = true;
            health = LoggingPersistenceHealth.Unavailable;
        }

        private sealed class ArchiveEntry
        {
            public string Path { get; set; }
            public long Length { get; set; }
            public DateTime LastWriteUtc { get; set; }
        }
    }
}
