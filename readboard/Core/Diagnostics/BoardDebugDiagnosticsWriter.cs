using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
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
        public string FailureReason { get; set; }
    }

    internal sealed class BoardDebugDiagnosticsWriter
    {
        private readonly string rootDirectory;
        private readonly Func<bool> isEnabled;
        private readonly object syncRoot = new object();
        private int eventCounter;
        private ulong lastFrameSignature;
        private ulong lastSnapshotSignature;
        private bool hasLastSuccess;

        public BoardDebugDiagnosticsWriter(string rootDirectory, Func<bool> isEnabled)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Debug diagnostics directory is required.", "rootDirectory");
            if (isEnabled == null)
                throw new ArgumentNullException("isEnabled");

            this.rootDirectory = rootDirectory;
            this.isEnabled = isEnabled;
        }

        public void RecordCaptureFailure(BoardDebugDiagnosticRecord record)
        {
            lock (syncRoot)
            {
                WriteEvent("capture-failure", record, false);
            }
        }

        public void RecordRecognitionFailure(BoardDebugDiagnosticRecord record)
        {
            lock (syncRoot)
            {
                WriteEvent("recognition-failure", record, true);
            }
        }

        public void RecordRecognitionSuccess(BoardDebugDiagnosticRecord record)
        {
            lock (syncRoot)
            {
                if (IsDuplicateSuccess(record))
                    return;

                WriteEvent("recognition-success", record, true);
                RememberSuccess(record);
            }
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

        private void WriteEvent(string eventName, BoardDebugDiagnosticRecord record, bool includeFrame)
        {
            if (!isEnabled())
                return;

            try
            {
                Directory.CreateDirectory(rootDirectory);
                DateTime timestampUtc = DateTime.UtcNow;
                string eventDirectory = CreateEventDirectory(eventName, timestampUtc);
                Directory.CreateDirectory(eventDirectory);

                if (includeFrame)
                    SaveFrame(record == null ? null : record.Frame, Path.Combine(eventDirectory, "frame.png"));

                File.WriteAllText(
                    Path.Combine(eventDirectory, "metadata.json"),
                    JsonSerializer.Serialize(CreateMetadata(eventName, timestampUtc, record)),
                    Encoding.UTF8);

                if (record != null && record.Snapshot != null)
                    File.WriteAllText(Path.Combine(eventDirectory, "recognition.txt"), FormatRecognition(record.Snapshot), Encoding.UTF8);

                AppendLog(eventName, timestampUtc, record);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Failed to write readboard debug diagnostics: " + ex);
            }
        }

        private string CreateEventDirectory(string eventName, DateTime timestampUtc)
        {
            int currentCounter = Interlocked.Increment(ref eventCounter);
            string name = timestampUtc.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                + "-"
                + currentCounter.ToString("0000", CultureInfo.InvariantCulture)
                + "-"
                + eventName;
            return Path.Combine(rootDirectory, name);
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
                LastMove = snapshot == null || snapshot.LastMove == null ? null : snapshot.LastMove.ToString()
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
            return builder.ToString();
        }

        private void AppendLog(string eventName, DateTime timestampUtc, BoardDebugDiagnosticRecord record)
        {
            string line = timestampUtc.ToString("o", CultureInfo.InvariantCulture)
                + " "
                + eventName
                + " mode="
                + (record == null ? string.Empty : record.SyncMode.ToString())
                + " failure="
                + (record == null ? string.Empty : record.FailureReason ?? string.Empty)
                + Environment.NewLine;
            File.AppendAllText(Path.Combine(rootDirectory, "debug.log"), line, Encoding.UTF8);
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

        private static void SaveFrame(BoardFrame frame, string path)
        {
            Bitmap bitmap = CreateBitmap(frame);
            if (bitmap == null)
                return;

            using (bitmap)
            {
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static Bitmap CreateBitmap(BoardFrame frame)
        {
            if (frame == null)
                return null;
            if (frame.Image != null)
                return new Bitmap(frame.Image);
            return CreateBitmap(frame.PixelBuffer);
        }

        private static Bitmap CreateBitmap(PixelBuffer buffer)
        {
            if (buffer == null || buffer.Pixels == null || buffer.Format != PixelBufferFormat.Rgb24)
                return null;
            if (buffer.Width <= 0 || buffer.Height <= 0 || buffer.Stride < buffer.Width * 3)
                return null;
            if (buffer.Pixels.Length < buffer.Stride * buffer.Height)
                return null;

            Bitmap bitmap = new Bitmap(buffer.Width, buffer.Height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < buffer.Height; y++)
            {
                int row = y * buffer.Stride;
                for (int x = 0; x < buffer.Width; x++)
                {
                    int index = row + x * 3;
                    bitmap.SetPixel(x, y, Color.FromArgb(buffer.Pixels[index], buffer.Pixels[index + 1], buffer.Pixels[index + 2]));
                }
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
    }
}
