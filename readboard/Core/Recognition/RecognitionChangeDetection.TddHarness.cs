using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;

namespace readboard
{
    internal static class RecognitionChangeDetectionTddHarness
    {
        private sealed class QueuedCapturePlatform : IBoardCapturePlatform
        {
            private readonly Queue<Bitmap> captures;

            public QueuedCapturePlatform(IEnumerable<Bitmap> captures)
            {
                this.captures = new Queue<Bitmap>(captures);
            }

            public double GetDesktopDpiScale()
            {
                return 1.0d;
            }

            public PixelRect GetPrimaryScreenBounds()
            {
                return new PixelRect(0, 0, 1920, 1080);
            }

            public bool TryDescribeWindow(IntPtr handle, WindowDescriptor seed, out WindowDescriptor descriptor)
            {
                throw new NotSupportedException();
            }

            public Bitmap CaptureWindow(IntPtr handle)
            {
                throw new NotSupportedException();
            }

            public Bitmap CapturePrintWindow(IntPtr handle)
            {
                throw new NotSupportedException();
            }

            public Bitmap CaptureScreen(PixelRect bounds)
            {
                if (captures.Count == 0)
                    throw new InvalidOperationException("No queued capture available.");

                return CloneBitmap(captures.Dequeue());
            }
        }

        public static int Main()
        {
            try
            {
                VerifyCaptureMetadataMarksRepeatedFrames();
                VerifyRecognitionReusesRepeatedFrameSnapshot();
                VerifyRecognitionReusesPayloadForEquivalentBoardState();
                Console.WriteLine("GREEN");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("RED: " + ex.Message);
                return 1;
            }
        }

        private static void VerifyCaptureMetadataMarksRepeatedFrames()
        {
            Bitmap repeated = CreateCaptureBitmap();
            LegacyBoardCaptureService captureService = new LegacyBoardCaptureService(
                new QueuedCapturePlatform(new[] { repeated, repeated }));
            BoardCaptureRequest request = CreateCaptureRequest();

            BoardCaptureResult first = captureService.Capture(request);
            BoardCaptureResult second = captureService.Capture(request);

            Assert(first != null && first.Success && first.Frame != null, "First capture should succeed.");
            Assert(second != null && second.Success && second.Frame != null, "Second capture should succeed.");
            Assert(!ReadBoolProperty(first.Frame, "HasSameContentAsPreviousCapture"), "First frame should not be marked unchanged.");
            Assert(ReadBoolProperty(second.Frame, "HasSameContentAsPreviousCapture"), "Second identical frame should be marked unchanged.");
            Assert(ReadUInt64Property(first.Frame, "ContentSignature") != 0UL, "Frames should carry a content signature.");
            Assert(ReadUInt64Property(first.Frame, "ContentSignature") == ReadUInt64Property(second.Frame, "ContentSignature"), "Identical captures should share the same signature.");
        }

        private static void VerifyRecognitionReusesRepeatedFrameSnapshot()
        {
            Bitmap repeated = CreateCaptureBitmap();
            LegacyBoardCaptureService captureService = new LegacyBoardCaptureService(
                new QueuedCapturePlatform(new[] { repeated, repeated }));
            LegacyBoardRecognitionService recognitionService = new LegacyBoardRecognitionService();
            BoardRecognitionRequest request = new BoardRecognitionRequest { InferLastMove = false };

            BoardRecognitionResult first = RecognizeCapturedFrame(captureService, recognitionService, request);
            BoardRecognitionResult second = RecognizeCapturedFrame(captureService, recognitionService, request);

            Assert(first.Success && second.Success, "Repeated recognition should succeed.");
            Assert(!ReadBoolProperty(first.Snapshot, "IsUnchangedFromPrevious"), "First snapshot should not be marked unchanged.");
            Assert(ReadBoolProperty(second.Snapshot, "IsUnchangedFromPrevious"), "Second identical snapshot should be marked unchanged.");
            Assert(ReadBoolProperty(second.Snapshot, "ReusedPayload"), "Second identical snapshot should reuse payload objects.");
            Assert(ReadBoolProperty(second, "UsedCachedSnapshot"), "Second identical recognition should use the cache.");
            Assert(ReferenceEquals(first.Snapshot.BoardState, second.Snapshot.BoardState), "Repeated recognition should reuse board state.");
            Assert(ReferenceEquals(first.Snapshot.Payload, second.Snapshot.Payload), "Repeated recognition should reuse payload.");
            Assert(ReferenceEquals(first.Snapshot.ProtocolLines, second.Snapshot.ProtocolLines), "Repeated recognition should reuse protocol lines.");
        }

        private static void VerifyRecognitionReusesPayloadForEquivalentBoardState()
        {
            LegacyBoardRecognitionService recognitionService = new LegacyBoardRecognitionService();
            BoardRecognitionRequest request = new BoardRecognitionRequest { InferLastMove = false };
            BoardFrame firstFrame = CreateManualFrame(15);
            BoardFrame secondFrame = CreateManualFrame(90);

            BoardRecognitionResult first = recognitionService.Recognize(CloneRecognitionRequest(request, firstFrame));
            BoardRecognitionResult second = recognitionService.Recognize(CloneRecognitionRequest(request, secondFrame));

            Assert(first.Success && second.Success, "Equivalent board-state recognition should succeed.");
            Assert(ReadBoolProperty(second.Snapshot, "IsUnchangedFromPrevious"), "Equivalent board state should be marked unchanged.");
            Assert(ReadBoolProperty(second.Snapshot, "ReusedPayload"), "Equivalent board state should reuse payload objects.");
            Assert(!ReadBoolProperty(second, "UsedCachedSnapshot"), "Equivalent board state should still exercise the analysis path.");
            Assert(ReferenceEquals(first.Snapshot.BoardState, second.Snapshot.BoardState), "Equivalent board state should reuse board-state storage.");
            Assert(ReferenceEquals(first.Snapshot.Payload, second.Snapshot.Payload), "Equivalent board state should reuse payload.");
            Assert(ReferenceEquals(first.Snapshot.ProtocolLines, second.Snapshot.ProtocolLines), "Equivalent board state should reuse protocol lines.");
        }

        private static BoardRecognitionResult RecognizeCapturedFrame(
            LegacyBoardCaptureService captureService,
            LegacyBoardRecognitionService recognitionService,
            BoardRecognitionRequest recognitionTemplate)
        {
            BoardCaptureResult capture = captureService.Capture(CreateCaptureRequest());
            Assert(capture != null && capture.Success && capture.Frame != null, "Capture should succeed for recognition.");
            return recognitionService.Recognize(CloneRecognitionRequest(recognitionTemplate, capture.Frame));
        }

        private static BoardRecognitionRequest CloneRecognitionRequest(BoardRecognitionRequest template, BoardFrame frame)
        {
            return new BoardRecognitionRequest
            {
                Frame = frame,
                Thresholds = template.Thresholds == null ? null : template.Thresholds.Clone(),
                InferLastMove = template.InferLastMove
            };
        }

        private static BoardCaptureRequest CreateCaptureRequest()
        {
            return new BoardCaptureRequest
            {
                SyncMode = SyncMode.Foreground,
                BoardSize = new BoardDimensions(2, 2),
                SelectionBounds = new PixelRect(0, 0, 6, 6)
            };
        }

        private static Bitmap CreateCaptureBitmap()
        {
            Bitmap bitmap = new Bitmap(6, 6, PixelFormat.Format24bppRgb);
            FillRect(bitmap, 0, 0, 3, 3, Color.Black);
            FillRect(bitmap, 3, 0, 3, 3, Color.White);
            FillRect(bitmap, 0, 3, 3, 3, Color.FromArgb(120, 80, 40));
            FillRect(bitmap, 3, 3, 3, 3, Color.Black);
            return bitmap;
        }

        private static BoardFrame CreateManualFrame(int borderShade)
        {
            Bitmap bitmap = new Bitmap(8, 8, PixelFormat.Format24bppRgb);
            FillRect(bitmap, 0, 0, 8, 8, Color.FromArgb(borderShade, 0, 0));
            FillRect(bitmap, 1, 1, 3, 3, Color.Black);
            FillRect(bitmap, 4, 1, 3, 3, Color.White);
            FillRect(bitmap, 1, 4, 3, 3, Color.FromArgb(120, 80, 40));
            FillRect(bitmap, 4, 4, 3, 3, Color.Black);
            PixelBuffer pixelBuffer = PixelBufferConverter.FromBitmap(bitmap);

            return new BoardFrame
            {
                SyncMode = SyncMode.Foreground,
                BoardSize = new BoardDimensions(2, 2),
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(1, 1, 6, 6),
                    ScreenBounds = new PixelRect(0, 0, 6, 6),
                    CellWidth = 3d,
                    CellHeight = 3d
                },
                Image = bitmap,
                PixelBuffer = pixelBuffer
            };
        }

        private static void FillRect(Bitmap bitmap, int x, int y, int width, int height, Color color)
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush brush = new SolidBrush(color))
            {
                graphics.FillRectangle(brush, x, y, width, height);
            }
        }

        private static Bitmap CloneBitmap(Bitmap bitmap)
        {
            Bitmap clone = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(clone))
            {
                graphics.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
            }

            return clone;
        }

        private static bool ReadBoolProperty(object target, string propertyName)
        {
            return (bool)ReadProperty(target, propertyName);
        }

        private static ulong ReadUInt64Property(object target, string propertyName)
        {
            return (ulong)ReadProperty(target, propertyName);
        }

        private static object ReadProperty(object target, string propertyName)
        {
            Assert(target != null, "Property target is required.");
            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert(property != null, "Expected property '" + propertyName + "' on " + target.GetType().Name + ".");
            return property.GetValue(target, null);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
