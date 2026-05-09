using System.Drawing;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.Recognition
{
    public sealed class SyncModeViewportAcceptanceTests
    {
        [Fact]
        public void Yike_enum_value_is_six()
        {
            Assert.Equal(6, (int)SyncMode.Yike);
        }

        [Theory]
        [InlineData((int)SyncMode.Fox, 2, 3, 12, 12)]
        [InlineData((int)SyncMode.FoxBackgroundPlace, 2, 3, 12, 12)]
        [InlineData((int)SyncMode.Tygem, 0, 0, 20, 20)]
        [InlineData((int)SyncMode.Sina, 3, 2, 15, 15)]
        public void TryResolveViewport_UsesExpectedBoundsForSyncMode(
            int syncModeValue,
            int x,
            int y,
            int width,
            int height)
        {
            SyncMode syncMode = (SyncMode)syncModeValue;
            BoardFrame frame = new BoardFrame
            {
                SyncMode = syncMode,
                BoardSize = new BoardDimensions(5, 5),
                Image = CreateModeBitmap(syncMode)
            };
            BoardRecognitionRequest request = new BoardRecognitionRequest
            {
                Frame = frame,
                InferLastMove = false
            };

            BoardRecognitionFailureKind failureKind;
            string failureReason;
            LegacyPixelMap pixels;
            bool created = LegacyPixelMap.TryCreate(frame, out pixels, out failureKind, out failureReason);
            bool resolved = LegacyBoardLocator.TryResolveViewport(request, pixels, out BoardViewport viewport, out string locatorFailure);

            Assert.True(created, failureReason);
            Assert.True(resolved, locatorFailure);
            Assert.Equal(x, viewport.SourceBounds.X);
            Assert.Equal(y, viewport.SourceBounds.Y);
            Assert.Equal(width, viewport.SourceBounds.Width);
            Assert.Equal(height, viewport.SourceBounds.Height);
            Assert.True(viewport.CellWidth > 0d);
            Assert.True(viewport.CellHeight > 0d);
        }

        [Theory]
        [InlineData((int)SyncMode.Fox, 2, 3, 12, 12)]
        [InlineData((int)SyncMode.FoxBackgroundPlace, 2, 3, 12, 12)]
        [InlineData((int)SyncMode.Sina, 3, 2, 15, 15)]
        public void TryResolveViewport_IgnoresFullWindowSeedBoundsForNativeSyncMode(
            int syncModeValue,
            int x,
            int y,
            int width,
            int height)
        {
            SyncMode syncMode = (SyncMode)syncModeValue;
            Bitmap bitmap = CreateModeBitmap(syncMode);
            BoardFrame frame = new BoardFrame
            {
                SyncMode = syncMode,
                BoardSize = new BoardDimensions(5, 5),
                Image = bitmap,
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height),
                    ScreenBounds = new PixelRect(10, 20, bitmap.Width, bitmap.Height),
                    CellWidth = bitmap.Width / 5d,
                    CellHeight = bitmap.Height / 5d
                }
            };

            BoardViewport viewport = ResolveViewport(frame);

            Assert.Equal(x, viewport.SourceBounds.X);
            Assert.Equal(y, viewport.SourceBounds.Y);
            Assert.Equal(width, viewport.SourceBounds.Width);
            Assert.Equal(height, viewport.SourceBounds.Height);
        }

        [Theory]
        [InlineData((int)SyncMode.Tygem, 0, 0, 20, 20)]
        [InlineData((int)SyncMode.Sina, 3, 2, 15, 15)]
        public void TryResolveViewport_UsesEquivalentBoundsForBitmapAndPixelBuffer(
            int syncModeValue,
            int x,
            int y,
            int width,
            int height)
        {
            SyncMode syncMode = (SyncMode)syncModeValue;
            Bitmap bitmap = CreateModeBitmap(syncMode);
            BoardViewport bitmapViewport = ResolveViewport(new BoardFrame
            {
                SyncMode = syncMode,
                BoardSize = new BoardDimensions(5, 5),
                Image = bitmap
            });
            BoardViewport pixelBufferViewport = ResolveViewport(new BoardFrame
            {
                SyncMode = syncMode,
                BoardSize = new BoardDimensions(5, 5),
                PixelBuffer = PixelBufferConverter.FromBitmap(bitmap)
            });

            Assert.Equal(x, bitmapViewport.SourceBounds.X);
            Assert.Equal(y, bitmapViewport.SourceBounds.Y);
            Assert.Equal(width, bitmapViewport.SourceBounds.Width);
            Assert.Equal(height, bitmapViewport.SourceBounds.Height);
            Assert.Equal(bitmapViewport.SourceBounds.X, pixelBufferViewport.SourceBounds.X);
            Assert.Equal(bitmapViewport.SourceBounds.Y, pixelBufferViewport.SourceBounds.Y);
            Assert.Equal(bitmapViewport.SourceBounds.Width, pixelBufferViewport.SourceBounds.Width);
            Assert.Equal(bitmapViewport.SourceBounds.Height, pixelBufferViewport.SourceBounds.Height);
        }

        [Fact]
        public void Recognize_ReusedNativeSyncViewportPreservesLocatedBoardBounds()
        {
            LegacyBoardRecognitionService service = new LegacyBoardRecognitionService();
            BoardFrame firstFrame = CreateFullWindowSeedFrame(SyncMode.Fox);
            BoardFrame secondFrame = CreateFullWindowSeedFrame(SyncMode.Fox);

            BoardRecognitionResult first = service.Recognize(new BoardRecognitionRequest
            {
                Frame = firstFrame,
                InferLastMove = false
            });
            BoardRecognitionResult second = service.Recognize(new BoardRecognitionRequest
            {
                Frame = secondFrame,
                InferLastMove = false
            });

            Assert.True(first.Success, first.FailureReason);
            Assert.True(second.Success, second.FailureReason);
            Assert.True(second.UsedCachedSnapshot);
            Assert.Equal(2, second.Viewport.SourceBounds.X);
            Assert.Equal(3, second.Viewport.SourceBounds.Y);
            Assert.Equal(12, second.Viewport.SourceBounds.Width);
            Assert.Equal(12, second.Viewport.SourceBounds.Height);
        }

        [Fact]
        public void Recognize_ReusedNativeSyncViewportRefreshesCurrentScreenBounds()
        {
            LegacyBoardRecognitionService service = new LegacyBoardRecognitionService();
            BoardFrame firstFrame = CreateFullWindowSeedFrame(SyncMode.Fox, screenX: 10, screenY: 20);
            BoardFrame secondFrame = CreateFullWindowSeedFrame(SyncMode.Fox, screenX: 120, screenY: 230);

            BoardRecognitionResult first = service.Recognize(new BoardRecognitionRequest
            {
                Frame = firstFrame,
                InferLastMove = false
            });
            BoardRecognitionResult second = service.Recognize(new BoardRecognitionRequest
            {
                Frame = secondFrame,
                InferLastMove = false
            });

            Assert.True(first.Success, first.FailureReason);
            Assert.True(second.Success, second.FailureReason);
            Assert.True(second.UsedCachedSnapshot);
            Assert.Equal(120, second.Viewport.ScreenBounds.X);
            Assert.Equal(230, second.Viewport.ScreenBounds.Y);
            Assert.Equal(secondFrame.Viewport.ScreenBounds.Width, second.Viewport.ScreenBounds.Width);
            Assert.Equal(secondFrame.Viewport.ScreenBounds.Height, second.Viewport.ScreenBounds.Height);
        }

        [Fact]
        public void TryResolveViewport_NormalizesProjectedBackgroundSelectionToFullCapturedImage()
        {
            BoardViewport viewport = ResolveViewport(new BoardFrame
            {
                SyncMode = SyncMode.Background,
                BoardSize = new BoardDimensions(5, 5),
                Image = CreateBlankBitmap(),
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(5, 4, 20, 20),
                    ScreenBounds = new PixelRect(120, 230, 20, 20),
                    CellWidth = 4d,
                    CellHeight = 4d
                }
            });

            Assert.Equal(0, viewport.SourceBounds.X);
            Assert.Equal(0, viewport.SourceBounds.Y);
            Assert.Equal(20, viewport.SourceBounds.Width);
            Assert.Equal(20, viewport.SourceBounds.Height);
            Assert.Equal(120, viewport.ScreenBounds.X);
            Assert.Equal(230, viewport.ScreenBounds.Y);
        }

        private static BoardViewport ResolveViewport(BoardFrame frame)
        {
            BoardRecognitionRequest request = new BoardRecognitionRequest
            {
                Frame = frame,
                InferLastMove = false
            };

            BoardRecognitionFailureKind failureKind;
            string failureReason;
            LegacyPixelMap pixels;
            bool created = LegacyPixelMap.TryCreate(frame, out pixels, out failureKind, out failureReason);
            bool resolved = LegacyBoardLocator.TryResolveViewport(request, pixels, out BoardViewport viewport, out string locatorFailure);

            Assert.True(created, failureReason);
            Assert.True(resolved, locatorFailure);
            return viewport;
        }

        private static BoardFrame CreateFullWindowSeedFrame(SyncMode syncMode, int screenX = 10, int screenY = 20)
        {
            Bitmap bitmap = CreateModeBitmap(syncMode);
            return new BoardFrame
            {
                SyncMode = syncMode,
                BoardSize = new BoardDimensions(5, 5),
                Image = bitmap,
                PixelBuffer = PixelBufferConverter.FromBitmap(bitmap),
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height),
                    ScreenBounds = new PixelRect(screenX, screenY, bitmap.Width, bitmap.Height),
                    CellWidth = bitmap.Width / 5d,
                    CellHeight = bitmap.Height / 5d
                }
            };
        }

        private static Bitmap CreateModeBitmap(SyncMode syncMode)
        {
            Bitmap bitmap = CreateBlankBitmap();
            if (syncMode == SyncMode.Tygem)
            {
                bitmap.SetPixel(0, 0, Color.FromArgb(235, 205, 130));
                return bitmap;
            }

            if (syncMode == SyncMode.Sina)
            {
                PaintSinaPattern(bitmap, x: 4, upperY: 3, lowerY: 14);
                return bitmap;
            }

            PaintFoxPattern(bitmap, leftX: 2, rightX: 14, y: 3);
            return bitmap;
        }

        private static Bitmap CreateBlankBitmap()
        {
            Bitmap bitmap = new Bitmap(20, 20);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.White);
            return bitmap;
        }

        private static void PaintSinaPattern(Bitmap bitmap, int x, int upperY, int lowerY)
        {
            Color edge = Color.FromArgb(251, 218, 162);
            bitmap.SetPixel(x, upperY, edge);
            bitmap.SetPixel(x, upperY + 1, edge);
            bitmap.SetPixel(x, upperY + 2, edge);
            bitmap.SetPixel(x, lowerY, edge);
            bitmap.SetPixel(x, lowerY + 1, edge);
            bitmap.SetPixel(x, lowerY + 2, edge);
        }

        private static void PaintFoxPattern(Bitmap bitmap, int leftX, int rightX, int y)
        {
            Color anchor = Color.FromArgb(49, 49, 49);
            Color edge = Color.FromArgb(46, 46, 46);
            bitmap.SetPixel(leftX, y, anchor);
            bitmap.SetPixel(leftX, y + 1, edge);
            bitmap.SetPixel(leftX, y + 2, edge);
            bitmap.SetPixel(leftX + 1, y, edge);
            bitmap.SetPixel(leftX + 2, y, edge);
            bitmap.SetPixel(rightX, y, anchor);
            bitmap.SetPixel(rightX, y + 1, edge);
            bitmap.SetPixel(rightX, y + 2, edge);
            bitmap.SetPixel(rightX - 1, y, edge);
            bitmap.SetPixel(rightX - 2, y, edge);
        }
    }
}
