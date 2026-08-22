using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace readboard
{
    internal sealed class BoardDebugDiagnosticRecord
    {
        public SyncMode SyncMode { get; set; }
        public int BoardWidth { get; set; }
        public int BoardHeight { get; set; }
        public CapturePathKind CapturePath { get; set; }
        public BoardFrame Frame { get; set; }
        public BoardSnapshot Snapshot { get; set; }
        public BoardCoordinate PlacementCoordinate { get; set; }
        public PlacementPathKind PlacementPath { get; set; }
        public IntPtr PlacementTargetHandle { get; set; }
        public int? PlacementClientX { get; set; }
        public int? PlacementClientY { get; set; }
        public int? PlacementMouseLParam { get; set; }
        public string FailureReason { get; set; }
    }

    internal sealed class BoardDebugDiagnosticsWriterOptions
    {
        public string RootDirectory { get; set; }
        public Func<bool> IsEnabled { get; set; }
        public ILoggingClock Clock { get; set; }
        public ILoggingFileSystem FileSystem { get; set; }
        public Action<LoggingPersistenceHealth> ReportHealth { get; set; }
        public long? MaxPngBytes { get; set; }
        public int? RetentionDays { get; set; }
        public long? MaxTotalBytes { get; set; }
    }

    internal sealed class BoardDebugDiagnosticsWriter : IDisposable
    {
        public const string PngSizeCapReason = "png-size-cap";

        private readonly string rootDirectory;
        private readonly Func<bool> isEnabled;
        private readonly ILoggingClock clock;
        private readonly ILoggingFileSystem fileSystem;
        private readonly Action<LoggingPersistenceHealth> reportHealth;
        private readonly long maxPngBytes;
        private readonly int retentionDays;
        private readonly long maxTotalBytes;
        private readonly object syncRoot = new object();
        private readonly Queue<PendingWrite> pendingWrites = new Queue<PendingWrite>();
        private readonly AutoResetEvent pendingWriteSignal = new AutoResetEvent(false);
        private Thread workerThread;
        private int eventCounter;
        private ulong lastFrameSignature;
        private ulong lastSnapshotSignature;
        private bool hasLastSuccess;
        private bool disposeRequested;
        private bool disposed;

        public BoardDebugDiagnosticsWriter(string rootDirectory, Func<bool> isEnabled)
            : this(new BoardDebugDiagnosticsWriterOptions
            {
                RootDirectory = rootDirectory,
                IsEnabled = isEnabled
            })
        {
        }

        internal BoardDebugDiagnosticsWriter(BoardDebugDiagnosticsWriterOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");
            if (options.IsEnabled == null)
                throw new ArgumentNullException("isEnabled");

            rootDirectory = options.RootDirectory;
            isEnabled = options.IsEnabled;
            clock = options.Clock ?? new SystemLoggingClock();
            fileSystem = options.FileSystem ?? new RealLoggingFileSystem();
            reportHealth = options.ReportHealth;
            maxPngBytes = options.MaxPngBytes ?? LoggingLimits.CaptureMaxPngBytes;
            retentionDays = options.RetentionDays ?? LoggingLimits.CaptureRetentionDays;
            maxTotalBytes = options.MaxTotalBytes ?? LoggingLimits.CaptureClassTotalBytes;
        }

        public void RecordCaptureFailure(BoardDebugDiagnosticRecord record)
        {
            lock (syncRoot)
            {
                EnqueueEvent("capture-failure", record, false);
            }
        }

        public void RecordRecognitionFailure(BoardDebugDiagnosticRecord record)
        {
            lock (syncRoot)
            {
                EnqueueEvent("recognition-failure", record, true);
            }
        }

        public void RecordRecognitionSuccess(BoardDebugDiagnosticRecord record)
        {
            lock (syncRoot)
            {
                if (IsDuplicateSuccess(record))
                    return;

                if (EnqueueEvent("recognition-success", record, true))
                    RememberSuccess(record);
            }
        }

        public void RecordPlacementSuccess(BoardDebugDiagnosticRecord record)
        {
            lock (syncRoot)
            {
                EnqueueEvent("placement-success", record, false);
            }
        }

        public void RecordPlacementFailure(BoardDebugDiagnosticRecord record)
        {
            lock (syncRoot)
            {
                EnqueueEvent("placement-failure", record, false);
            }
        }

        public void RecordPlacementSkipped(BoardDebugDiagnosticRecord record)
        {
            lock (syncRoot)
            {
                EnqueueEvent("placement-skipped", record, false);
            }
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (disposed)
                    return;

                disposed = true;
                disposeRequested = true;
            }

            pendingWriteSignal.Set();
            Thread workerToJoin = workerThread;
            if (workerToJoin != null && workerToJoin != Thread.CurrentThread)
                workerToJoin.Join();
            pendingWriteSignal.Dispose();
        }

        private bool IsDuplicateSuccess(BoardDebugDiagnosticRecord record)
        {
            if (!hasLastSuccess || record == null)
                return false;

            ulong frameSignature = ResolveFrameSignature(record.Frame);
            ulong snapshotSignature = ResolveSnapshotSignature(record.Snapshot);
            return frameSignature != 0UL
                && snapshotSignature != 0UL
                && frameSignature == lastFrameSignature
                && snapshotSignature == lastSnapshotSignature;
        }

        private void RememberSuccess(BoardDebugDiagnosticRecord record)
        {
            lastFrameSignature = ResolveFrameSignature(record == null ? null : record.Frame);
            lastSnapshotSignature = ResolveSnapshotSignature(record == null ? null : record.Snapshot);
            hasLastSuccess = lastFrameSignature != 0UL || lastSnapshotSignature != 0UL;
        }

        private bool EnqueueEvent(string eventName, BoardDebugDiagnosticRecord record, bool includeFrame)
        {
            if (disposeRequested || !isEnabled() || string.IsNullOrWhiteSpace(rootDirectory))
                return false;

            try
            {
                DateTime timestampUtc = clock.UtcNow;
                PendingWrite pendingWrite = CreatePendingWrite(eventName, timestampUtc, record, includeFrame);
                EnsureWorkerStarted();
                pendingWrites.Enqueue(pendingWrite);
                pendingWriteSignal.Set();
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Failed to enqueue readboard debug diagnostics: " + ex);
                return false;
            }
        }

        private void EnsureWorkerStarted()
        {
            if (workerThread != null)
                return;

            workerThread = new Thread(RunWriteLoop);
            workerThread.IsBackground = true;
            workerThread.Name = "ReadboardDebugDiagnosticsWriter";
            workerThread.Start();
        }

        private PendingWrite CreatePendingWrite(
            string eventName,
            DateTime timestampUtc,
            BoardDebugDiagnosticRecord record,
            bool includeFrame)
        {
            string eventDirectoryName = CreateEventDirectoryName(eventName, timestampUtc);
            string metadataJson = JsonSerializer.Serialize(CreateMetadata(eventName, timestampUtc, record));
            string recognitionText = record != null && record.Snapshot != null
                ? FormatRecognition(record.Snapshot)
                : null;
            string logLine = FormatLogLine(eventName, timestampUtc, record);
            PendingFrame frame = includeFrame ? SnapshotFrame(record == null ? null : record.Frame) : null;
            return new PendingWrite(eventDirectoryName, metadataJson, recognitionText, logLine, frame);
        }

        private void RunWriteLoop()
        {
            while (true)
            {
                PendingWrite pendingWrite = null;
                lock (syncRoot)
                {
                    if (pendingWrites.Count > 0)
                    {
                        pendingWrite = pendingWrites.Dequeue();
                    }
                    else if (disposeRequested)
                    {
                        return;
                    }
                }

                if (pendingWrite == null)
                {
                    pendingWriteSignal.WaitOne();
                    continue;
                }

                try
                {
                    WritePendingWrite(pendingWrite);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("Failed to write readboard debug diagnostics: " + ex);
                    ReportHealth(LoggingPersistenceHealth.Degraded);
                }
                finally
                {
                    pendingWrite.Dispose();
                }
            }
        }

        private void WritePendingWrite(PendingWrite pendingWrite)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                ReportHealth(LoggingPersistenceHealth.Unavailable);
                return;
            }

            if (!fileSystem.TryCreateDirectory(rootDirectory))
            {
                ReportHealth(LoggingPersistenceHealth.Degraded);
                return;
            }

            string eventDirectory = Path.Combine(rootDirectory, pendingWrite.EventDirectoryName);
            if (!fileSystem.TryCreateDirectory(eventDirectory))
            {
                ReportHealth(LoggingPersistenceHealth.Degraded);
                return;
            }

            string metadataJson = pendingWrite.MetadataJson;
            if (pendingWrite.Frame != null)
            {
                byte[] png = EncodePng(pendingWrite.Frame);
                if (png != null)
                {
                    if (png.Length > maxPngBytes)
                    {
                        metadataJson = WithFrameOmittedReason(metadataJson);
                    }
                    else if (!fileSystem.TryWriteAllBytes(Path.Combine(eventDirectory, "frame.png"), png))
                    {
                        ReportHealth(LoggingPersistenceHealth.Degraded);
                    }
                }
            }

            if (!fileSystem.TryWriteAllBytes(
                Path.Combine(eventDirectory, "metadata.json"),
                EncodeUtf8(metadataJson)))
            {
                ReportHealth(LoggingPersistenceHealth.Degraded);
            }

            if (!string.IsNullOrWhiteSpace(pendingWrite.RecognitionText)
                && !fileSystem.TryWriteAllBytes(
                    Path.Combine(eventDirectory, "recognition.txt"),
                    EncodeUtf8(pendingWrite.RecognitionText)))
            {
                ReportHealth(LoggingPersistenceHealth.Degraded);
            }

            if (!fileSystem.TryAppend(Path.Combine(rootDirectory, "debug.log"), EncodeUtf8(pendingWrite.LogLine)))
                ReportHealth(LoggingPersistenceHealth.Degraded);

            CleanupQuota();
        }

        private void ReportHealth(LoggingPersistenceHealth health)
        {
            if (reportHealth != null)
                reportHealth(health);
        }

        private static byte[] EncodeUtf8(string text)
        {
            return Encoding.UTF8.GetBytes(text ?? string.Empty);
        }

        private static string WithFrameOmittedReason(string metadataJson)
        {
            if (string.IsNullOrEmpty(metadataJson))
                return "{\"FrameOmittedReason\":\"" + PngSizeCapReason + "\"}";

            int closer = metadataJson.LastIndexOf('}');
            if (closer < 0)
                return metadataJson;

            string prefix = metadataJson.Substring(0, closer).TrimEnd();
            if (prefix.EndsWith("{", StringComparison.Ordinal))
                return prefix + "\"FrameOmittedReason\":\"" + PngSizeCapReason + "\"}";
            return prefix + ",\"FrameOmittedReason\":\"" + PngSizeCapReason + "\"}";
        }

        private void CleanupQuota()
        {
            try
            {
                IList<string> directories;
                if (!fileSystem.TryListDirectories(rootDirectory, out directories))
                {
                    ReportHealth(LoggingPersistenceHealth.Degraded);
                    return;
                }

                DateTime cutoff = clock.UtcNow.ToUniversalTime().AddDays(-retentionDays);
                List<CaptureRetentionEntry> kept = new List<CaptureRetentionEntry>();
                bool sizingFailed = false;
                for (int i = 0; i < directories.Count; i++)
                {
                    string directory = directories[i];
                    DateTime timestamp = ResolveEventTimestamp(directory);
                    long size;
                    if (!TrySumDirectoryFiles(directory, out size))
                    {
                        sizingFailed = true;
                        size = 0;
                    }

                    if (timestamp <= cutoff)
                    {
                        if (!fileSystem.TryDeleteDirectory(directory))
                            ReportHealth(LoggingPersistenceHealth.Degraded);
                        continue;
                    }

                    CaptureRetentionEntry entry = new CaptureRetentionEntry();
                    entry.Path = directory;
                    entry.TimestampUtc = timestamp;
                    entry.Size = size;
                    kept.Add(entry);
                }

                long rootSize;
                if (!TrySumDirectoryFiles(rootDirectory, out rootSize))
                    sizingFailed = true;
                if (sizingFailed)
                {
                    ReportHealth(LoggingPersistenceHealth.Degraded);
                    return;
                }

                long total = rootSize;
                for (int i = 0; i < kept.Count; i++)
                    total += kept[i].Size;

                kept.Sort(CompareOldestFirst);
                int index = 0;
                while (total > maxTotalBytes && index < kept.Count - 1)
                {
                    if (!fileSystem.TryDeleteDirectory(kept[index].Path))
                    {
                        ReportHealth(LoggingPersistenceHealth.Degraded);
                        index++;
                        continue;
                    }

                    total -= kept[index].Size;
                    index++;
                }

                if (total > maxTotalBytes)
                    ReportHealth(LoggingPersistenceHealth.Degraded);
            }
            catch
            {
                ReportHealth(LoggingPersistenceHealth.Degraded);
            }
        }

        private DateTime ResolveEventTimestamp(string directory)
        {
            DateTime parsed;
            if (TryParseEventDirectoryTimestamp(Path.GetFileName(directory), out parsed))
                return parsed;

            IList<string> files;
            DateTime latest = DateTime.MinValue;
            if (fileSystem.TryListFiles(directory, out files) && files != null)
            {
                for (int i = 0; i < files.Count; i++)
                {
                    DateTime write = fileSystem.GetLastWriteUtc(files[i]);
                    if (write > latest)
                        latest = write;
                }
            }

            if (latest == DateTime.MinValue)
                return clock.UtcNow.ToUniversalTime();
            return latest;
        }

        private static bool TryParseEventDirectoryTimestamp(string directoryName, out DateTime timestampUtc)
        {
            timestampUtc = DateTime.MinValue;
            if (string.IsNullOrEmpty(directoryName) || directoryName.Length < 19)
                return false;
            return DateTime.TryParseExact(
                directoryName.Substring(0, 19),
                "yyyyMMdd-HHmmss-fff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out timestampUtc);
        }

        private bool TrySumDirectoryFiles(string directory, out long total)
        {
            total = 0;
            IList<string> files;
            if (!fileSystem.TryListFiles(directory, out files) || files == null)
                return false;

            for (int i = 0; i < files.Count; i++)
                total += fileSystem.GetLength(files[i]);
            return true;
        }

        private static int CompareOldestFirst(CaptureRetentionEntry left, CaptureRetentionEntry right)
        {
            int compared = left.TimestampUtc.CompareTo(right.TimestampUtc);
            if (compared != 0)
                return compared;
            return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class CaptureRetentionEntry
        {
            public string Path;
            public DateTime TimestampUtc;
            public long Size;
        }

        private string CreateEventDirectoryName(string eventName, DateTime timestampUtc)
        {
            int currentCounter = Interlocked.Increment(ref eventCounter);
            return timestampUtc.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                + "-"
                + currentCounter.ToString("0000", CultureInfo.InvariantCulture)
                + "-"
                + eventName;
        }

        private static object CreateMetadata(string eventName, DateTime timestampUtc, BoardDebugDiagnosticRecord record)
        {
            BoardFrame frame = record == null ? null : record.Frame;
            BoardSnapshot snapshot = record == null ? null : record.Snapshot;
            return new
            {
                EventName = eventName,
                TimestampUtc = timestampUtc.ToString("o", CultureInfo.InvariantCulture),
                SyncMode = record == null ? null : record.SyncMode.ToString(),
                BoardWidth = record == null ? 0 : record.BoardWidth,
                BoardHeight = record == null ? 0 : record.BoardHeight,
                CapturePath = record == null ? null : record.CapturePath.ToString(),
                FailureReason = record == null ? null : record.FailureReason,
                FrameWidth = ResolveFrameWidth(frame),
                FrameHeight = ResolveFrameHeight(frame),
                FrameSignature = ResolveFrameSignature(frame),
                SnapshotWidth = snapshot == null ? 0 : snapshot.Width,
                SnapshotHeight = snapshot == null ? 0 : snapshot.Height,
                SnapshotSignature = ResolveSnapshotSignature(snapshot),
                BlackStoneCount = snapshot == null ? 0 : snapshot.BlackStoneCount,
                WhiteStoneCount = snapshot == null ? 0 : snapshot.WhiteStoneCount,
                LastMove = snapshot == null || snapshot.LastMove == null ? null : snapshot.LastMove.ToString(),
                LastMoveSource = snapshot == null ? null : LastMoveSourceToToken(snapshot.LastMoveSource),
                PlacementX = record == null || record.PlacementCoordinate == null ? null : (int?)record.PlacementCoordinate.X,
                PlacementY = record == null || record.PlacementCoordinate == null ? null : (int?)record.PlacementCoordinate.Y,
                PlacementPath = record == null || record.PlacementPath == PlacementPathKind.Unknown ? null : record.PlacementPath.ToString(),
                PlacementTargetHandle = record == null ? 0L : record.PlacementTargetHandle.ToInt64(),
                PlacementClientX = record == null ? null : record.PlacementClientX,
                PlacementClientY = record == null ? null : record.PlacementClientY,
                PlacementMouseLParam = record == null ? null : record.PlacementMouseLParam
            };
        }

        private static string FormatRecognition(BoardSnapshot snapshot)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("payload=" + (snapshot.Payload ?? string.Empty));
            builder.AppendLine("valid=" + snapshot.IsValid);
            builder.AppendLine("black=" + snapshot.BlackStoneCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("white=" + snapshot.WhiteStoneCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("allBlack=" + snapshot.IsAllBlack);
            builder.AppendLine("allWhite=" + snapshot.IsAllWhite);
            builder.AppendLine("stateSignature=" + snapshot.StateSignature.ToString(CultureInfo.InvariantCulture));
            if (snapshot.LastMove != null)
                builder.AppendLine("lastMove=" + snapshot.LastMove);
            builder.AppendLine("lastMoveSource=" + LastMoveSourceToToken(snapshot.LastMoveSource));
            return builder.ToString();
        }

        private static string LastMoveSourceToToken(LastMoveSource source)
        {
            switch (source)
            {
                case LastMoveSource.RedBlueMarker:
                    return ProtocolKeywords.LastMoveSourceRedBlueMarker;
                case LastMoveSource.FoxCornerFlip:
                    return ProtocolKeywords.LastMoveSourceFoxCornerFlip;
                case LastMoveSource.Deviation:
                    return ProtocolKeywords.LastMoveSourceDeviation;
                case LastMoveSource.StoneCount:
                    return ProtocolKeywords.LastMoveSourceStoneCount;
                default:
                    return ProtocolKeywords.LastMoveSourceNone;
            }
        }

        private static string FormatLogLine(string eventName, DateTime timestampUtc, BoardDebugDiagnosticRecord record)
        {
            return timestampUtc.ToString("o", CultureInfo.InvariantCulture)
                + " "
                + eventName
                + " mode="
                + (record == null ? string.Empty : record.SyncMode.ToString())
                + " failure="
                + (record == null ? string.Empty : record.FailureReason ?? string.Empty)
                + " placement="
                + FormatBoardCoordinate(record)
                + " client="
                + FormatClientPoint(record)
                + Environment.NewLine;
        }

        private static string FormatBoardCoordinate(BoardDebugDiagnosticRecord record)
        {
            if (record == null || record.PlacementCoordinate == null)
                return string.Empty;
            return record.PlacementCoordinate.X.ToString(CultureInfo.InvariantCulture)
                + ","
                + record.PlacementCoordinate.Y.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatClientPoint(BoardDebugDiagnosticRecord record)
        {
            if (record == null || !record.PlacementClientX.HasValue || !record.PlacementClientY.HasValue)
                return string.Empty;
            return record.PlacementClientX.Value.ToString(CultureInfo.InvariantCulture)
                + ","
                + record.PlacementClientY.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static int ResolveFrameWidth(BoardFrame frame)
        {
            if (frame == null)
                return 0;
            if (frame.PixelBuffer != null)
                return frame.PixelBuffer.Width;
            return frame.Image == null ? 0 : frame.Image.Width;
        }

        private static int ResolveFrameHeight(BoardFrame frame)
        {
            if (frame == null)
                return 0;
            if (frame.PixelBuffer != null)
                return frame.PixelBuffer.Height;
            return frame.Image == null ? 0 : frame.Image.Height;
        }

        private static PendingFrame SnapshotFrame(BoardFrame frame)
        {
            if (frame == null)
                return null;
            if (frame.Image != null)
                return PendingFrame.FromBitmap(new Bitmap(frame.Image));

            PixelBuffer buffer = frame.PixelBuffer;
            if (buffer == null
                || buffer.Pixels == null
                || buffer.Format != PixelBufferFormat.Rgb24
                || buffer.Width <= 0
                || buffer.Height <= 0
                || buffer.Stride < buffer.Width * 3
                || buffer.Pixels.Length < buffer.Stride * buffer.Height)
            {
                return null;
            }

            byte[] copiedPixels = new byte[buffer.Stride * buffer.Height];
            Buffer.BlockCopy(buffer.Pixels, 0, copiedPixels, 0, copiedPixels.Length);
            return PendingFrame.FromPixelBuffer(buffer.Width, buffer.Height, buffer.Stride, copiedPixels);
        }

        private static byte[] EncodePng(PendingFrame frame)
        {
            Bitmap bitmap = CreateBitmap(frame);
            if (bitmap == null)
                return null;

            using (bitmap)
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static Bitmap CreateBitmap(PendingFrame frame)
        {
            if (frame == null)
                return null;
            if (frame.Bitmap != null)
                return frame.DetachBitmap();
            return CreateBitmap(frame.Width, frame.Height, frame.Stride, frame.Pixels);
        }

        private static Bitmap CreateBitmap(int width, int height, int stride, byte[] pixels)
        {
            if (pixels == null || width <= 0 || height <= 0 || stride < width * 3 || pixels.Length < stride * height)
                return null;

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            BitmapData bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                byte[] rowBuffer = new byte[bitmapData.Stride];
                for (int y = 0; y < height; y++)
                {
                    int sourceRow = y * stride;
                    Array.Clear(rowBuffer, 0, rowBuffer.Length);
                    for (int x = 0; x < width; x++)
                    {
                        int sourceIndex = sourceRow + x * 3;
                        int destinationIndex = x * 3;
                        rowBuffer[destinationIndex] = pixels[sourceIndex + 2];
                        rowBuffer[destinationIndex + 1] = pixels[sourceIndex + 1];
                        rowBuffer[destinationIndex + 2] = pixels[sourceIndex];
                    }

                    Marshal.Copy(
                        rowBuffer,
                        0,
                        IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride),
                        rowBuffer.Length);
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }

        private static ulong ResolveFrameSignature(BoardFrame frame)
        {
            if (frame == null)
                return 0UL;
            if (frame.ContentSignature != 0UL)
                return frame.ContentSignature;
            return BoardContentHash.Compute(frame.PixelBuffer);
        }

        private static ulong ResolveSnapshotSignature(BoardSnapshot snapshot)
        {
            if (snapshot == null)
                return 0UL;
            return snapshot.StateSignature;
        }

        private sealed class PendingWrite : IDisposable
        {
            public PendingWrite(
                string eventDirectoryName,
                string metadataJson,
                string recognitionText,
                string logLine,
                PendingFrame frame)
            {
                EventDirectoryName = eventDirectoryName;
                MetadataJson = metadataJson;
                RecognitionText = recognitionText;
                LogLine = logLine;
                Frame = frame;
            }

            public string EventDirectoryName { get; private set; }
            public string MetadataJson { get; private set; }
            public string RecognitionText { get; private set; }
            public string LogLine { get; private set; }
            public PendingFrame Frame { get; private set; }

            public void Dispose()
            {
                if (Frame != null)
                {
                    Frame.Dispose();
                    Frame = null;
                }
            }
        }

        private sealed class PendingFrame : IDisposable
        {
            private PendingFrame()
            {
            }

            public Bitmap Bitmap { get; private set; }
            public int Width { get; private set; }
            public int Height { get; private set; }
            public int Stride { get; private set; }
            public byte[] Pixels { get; private set; }

            public static PendingFrame FromBitmap(Bitmap bitmap)
            {
                return new PendingFrame
                {
                    Bitmap = bitmap
                };
            }

            public static PendingFrame FromPixelBuffer(int width, int height, int stride, byte[] pixels)
            {
                return new PendingFrame
                {
                    Width = width,
                    Height = height,
                    Stride = stride,
                    Pixels = pixels
                };
            }

            public void Dispose()
            {
                if (Bitmap != null)
                {
                    Bitmap.Dispose();
                    Bitmap = null;
                }
            }

            public Bitmap DetachBitmap()
            {
                Bitmap bitmap = Bitmap;
                Bitmap = null;
                return bitmap;
            }
        }
    }
}
