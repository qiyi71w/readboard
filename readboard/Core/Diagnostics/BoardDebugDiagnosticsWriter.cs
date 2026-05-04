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
        public string FailureReason { get; set; }
    }

    internal sealed class BoardDebugDiagnosticsWriter : IDisposable
    {
        private readonly string rootDirectory;
        private readonly Func<bool> isEnabled;
        private readonly object syncRoot = new object();
        private readonly Queue<PendingWrite> pendingWrites = new Queue<PendingWrite>();
        private readonly AutoResetEvent pendingWriteSignal = new AutoResetEvent(false);
        private readonly Thread workerThread;
        private int eventCounter;
        private ulong lastFrameSignature;
        private ulong lastSnapshotSignature;
        private bool hasLastSuccess;
        private bool disposeRequested;
        private bool disposed;

        public BoardDebugDiagnosticsWriter(string rootDirectory, Func<bool> isEnabled)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Debug diagnostics directory is required.", "rootDirectory");
            if (isEnabled == null)
                throw new ArgumentNullException("isEnabled");

            this.rootDirectory = rootDirectory;
            this.isEnabled = isEnabled;
            workerThread = new Thread(RunWriteLoop);
            workerThread.IsBackground = true;
            workerThread.Name = "ReadboardDebugDiagnosticsWriter";
            workerThread.Start();
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
            if (workerThread != Thread.CurrentThread)
                workerThread.Join();
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
            if (disposeRequested || !isEnabled())
                return false;

            try
            {
                DateTime timestampUtc = DateTime.UtcNow;
                pendingWrites.Enqueue(CreatePendingWrite(eventName, timestampUtc, record, includeFrame));
                pendingWriteSignal.Set();
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Failed to enqueue readboard debug diagnostics: " + ex);
                return false;
            }
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
                }
                finally
                {
                    pendingWrite.Dispose();
                }
            }
        }

        private void WritePendingWrite(PendingWrite pendingWrite)
        {
            Directory.CreateDirectory(rootDirectory);
            string eventDirectory = Path.Combine(rootDirectory, pendingWrite.EventDirectoryName);
            Directory.CreateDirectory(eventDirectory);

            if (pendingWrite.Frame != null)
                SaveFrame(pendingWrite.Frame, Path.Combine(eventDirectory, "frame.png"));

            File.WriteAllText(
                Path.Combine(eventDirectory, "metadata.json"),
                pendingWrite.MetadataJson,
                Encoding.UTF8);

            if (!string.IsNullOrWhiteSpace(pendingWrite.RecognitionText))
            {
                File.WriteAllText(
                    Path.Combine(eventDirectory, "recognition.txt"),
                    pendingWrite.RecognitionText,
                    Encoding.UTF8);
            }

            File.AppendAllText(Path.Combine(rootDirectory, "debug.log"), pendingWrite.LogLine, Encoding.UTF8);
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

        private static string FormatLogLine(string eventName, DateTime timestampUtc, BoardDebugDiagnosticRecord record)
        {
            return timestampUtc.ToString("o", CultureInfo.InvariantCulture)
                + " "
                + eventName
                + " mode="
                + (record == null ? string.Empty : record.SyncMode.ToString())
                + " failure="
                + (record == null ? string.Empty : record.FailureReason ?? string.Empty)
                + Environment.NewLine;
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

        private static void SaveFrame(PendingFrame frame, string path)
        {
            Bitmap bitmap = CreateBitmap(frame);
            if (bitmap == null)
                return;

            using (bitmap)
            {
                bitmap.Save(path, ImageFormat.Png);
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
