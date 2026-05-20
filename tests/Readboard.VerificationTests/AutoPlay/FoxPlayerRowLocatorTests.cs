using System.Collections.Generic;
using System.Drawing;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxPlayerRowLocatorTests
    {
        [Fact]
        public void Locate_ReturnsVisibleRowsFromFoxRightPanel()
        {
            using (Bitmap bitmap = CreateFoxLikeWindow(2, 1.0f))
            {
                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.Locate(bitmap, SyncMode.Fox);

                Assert.Equal(2, rows.Count);
                AssertRowsAreInsideRightPanel(bitmap, rows);
            }
        }

        [Fact]
        public void Locate_DoesNotAssumeTwoRows()
        {
            using (Bitmap bitmap = CreateFoxLikeWindow(4, 1.0f))
            {
                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.Locate(bitmap, SyncMode.FoxBackgroundPlace);

                Assert.Equal(4, rows.Count);
                AssertRowsAreInsideRightPanel(bitmap, rows);
            }
        }

        [Fact]
        public void Locate_ReturnsNoRowsForBlankBitmap()
        {
            using (Bitmap bitmap = new Bitmap(900, 600))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(58, 58, 58));

                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.Locate(bitmap, SyncMode.Fox);

                Assert.Empty(rows);
            }
        }

        [Fact]
        public void Locate_ReturnsNoRowsOutsideFoxModes()
        {
            using (Bitmap bitmap = CreateFoxLikeWindow(2, 1.0f))
            {
                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.Locate(bitmap, SyncMode.Yike);

                Assert.Empty(rows);
            }
        }

        [Fact]
        public void Locate_KeepsCandidateProportionsWhenScaled()
        {
            using (Bitmap bitmap = CreateFoxLikeWindow(4, 1.5f))
            {
                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.Locate(bitmap, SyncMode.Fox);

                Assert.Equal(4, rows.Count);
                AssertRowsAreInsideRightPanel(bitmap, rows);
                Assert.InRange(rows[0].RowBounds.Height, 42, 54);
                Assert.InRange(rows[0].StoneIconBounds.Width, 24, 42);
            }
        }

        [Fact]
        public void LocatePlayerListPanel_ReturnsVisibleRowsFromRoomPlayerPanel()
        {
            using (Bitmap bitmap = CreateFoxPlayerListPanel())
            {
                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.LocatePlayerListPanel(bitmap);

                Assert.Equal(2, rows.Count);
                AssertRowsAreInsidePanel(bitmap, rows);
                Assert.InRange(rows[0].NicknameBounds.X, 28, 48);
                Assert.InRange(rows[0].StoneIconBounds.X, 120, 152);
            }
        }

        [Fact]
        public void Detect_ReturnsStoneColorForMatchedNicknameSignature()
        {
            using (Bitmap bitmap = CreateFoxLikeWindow(2, 1.0f))
            {
                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.Locate(bitmap, SyncMode.Fox);
                using (Bitmap nicknameSnippet = Crop(bitmap, rows[1].NicknameBounds))
                {
                    string signature = FoxPlayerNicknameSignature.FromBitmap(nicknameSnippet).Serialize();

                    AutoPlayColorResolution resolution = FoxAutoPlayColorDetector.Detect(bitmap, SyncMode.Fox, signature);

                    Assert.True(resolution.IsKnown);
                    Assert.Equal("white", resolution.PlayColor);
                    Assert.Equal(AutoPlayColorStatus.RecognizedWhite, resolution.Status);
                }
            }
        }

        [Fact]
        public void DetectPlayerListPanel_ReturnsStoneColorForMatchedNicknameSignature()
        {
            using (Bitmap bitmap = CreateFoxPlayerListPanel())
            {
                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.LocatePlayerListPanel(bitmap);
                using (Bitmap nicknameSnippet = Crop(bitmap, rows[1].NicknameBounds))
                {
                    string signature = FoxPlayerNicknameSignature.FromBitmap(nicknameSnippet).Serialize();

                    AutoPlayColorResolution resolution = FoxAutoPlayColorDetector.DetectPlayerListPanel(bitmap, signature);

                    Assert.True(resolution.IsKnown);
                    Assert.Equal("white", resolution.PlayColor);
                    Assert.Equal(AutoPlayColorStatus.RecognizedWhite, resolution.Status);
                }
            }
        }

        private static void AssertRowsAreInsideRightPanel(Bitmap bitmap, IList<FoxPlayerRowCandidate> rows)
        {
            foreach (FoxPlayerRowCandidate row in rows)
            {
                Assert.True(row.RowBounds.X > bitmap.Width / 2);
                Assert.True(row.RowBounds.Width > bitmap.Width / 5);
                Assert.True(row.NicknameBounds.X > row.RowBounds.X);
                Assert.True(row.NicknameBounds.Y >= row.RowBounds.Y);
                Assert.True(row.NicknameBounds.X + row.NicknameBounds.Width <= row.RowBounds.X + row.RowBounds.Width);
                Assert.True(row.NicknameBounds.Y + row.NicknameBounds.Height <= row.RowBounds.Y + row.RowBounds.Height);
                Assert.True(row.StoneIconBounds.X > row.RowBounds.X);
                Assert.True(row.StoneIconBounds.Y >= row.RowBounds.Y);
                Assert.True(row.StoneIconBounds.X + row.StoneIconBounds.Width <= row.RowBounds.X + row.RowBounds.Width);
                Assert.True(row.StoneIconBounds.Y + row.StoneIconBounds.Height <= row.RowBounds.Y + row.RowBounds.Height);
            }
        }

        private static void AssertRowsAreInsidePanel(Bitmap bitmap, IList<FoxPlayerRowCandidate> rows)
        {
            foreach (FoxPlayerRowCandidate row in rows)
            {
                Assert.True(row.RowBounds.X >= 0);
                Assert.True(row.RowBounds.Y >= 0);
                Assert.True(row.RowBounds.X + row.RowBounds.Width <= bitmap.Width);
                Assert.True(row.RowBounds.Y + row.RowBounds.Height <= bitmap.Height);
                Assert.True(row.NicknameBounds.X > row.RowBounds.X);
                Assert.True(row.NicknameBounds.Y >= row.RowBounds.Y);
                Assert.True(row.NicknameBounds.X + row.NicknameBounds.Width <= row.RowBounds.X + row.RowBounds.Width);
                Assert.True(row.NicknameBounds.Y + row.NicknameBounds.Height <= row.RowBounds.Y + row.RowBounds.Height);
                Assert.True(row.StoneIconBounds.X > row.NicknameBounds.X);
                Assert.True(row.StoneIconBounds.Y >= row.RowBounds.Y);
                Assert.True(row.StoneIconBounds.X + row.StoneIconBounds.Width <= row.RowBounds.X + row.RowBounds.Width);
                Assert.True(row.StoneIconBounds.Y + row.StoneIconBounds.Height <= row.RowBounds.Y + row.RowBounds.Height);
            }
        }

        private static Bitmap CreateFoxLikeWindow(int rowCount, float scale)
        {
            int width = (int)(900 * scale);
            int height = (int)(600 * scale);
            Bitmap bitmap = new Bitmap(width, height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen borderPen = new Pen(Color.FromArgb(186, 190, 190), 1 * scale))
            using (Brush rowBrush = new SolidBrush(Color.FromArgb(242, 242, 238)))
            using (Brush alternateRowBrush = new SolidBrush(Color.FromArgb(232, 235, 235)))
            using (Brush glyphBrush = new SolidBrush(Color.FromArgb(220, 30, 210)))
            using (Brush darkStoneBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
            using (Brush whiteStoneBrush = new SolidBrush(Color.FromArgb(245, 245, 238)))
            {
                graphics.Clear(Color.FromArgb(55, 55, 55));
                int panelX = (int)(585 * scale);
                int panelY = (int)(190 * scale);
                int panelWidth = (int)(270 * scale);
                int rowHeight = (int)(34 * scale);
                graphics.FillRectangle(Brushes.WhiteSmoke, panelX, panelY, panelWidth, rowHeight * rowCount);
                for (int row = 0; row < rowCount; row++)
                {
                    int y = panelY + row * rowHeight;
                    graphics.FillRectangle(row % 2 == 0 ? rowBrush : alternateRowBrush, panelX, y, panelWidth, rowHeight);
                    graphics.DrawRectangle(borderPen, panelX, y, panelWidth, rowHeight);
                    graphics.FillRectangle(glyphBrush, panelX + (int)(48 * scale), y + (int)(8 * scale), (int)(70 * scale), (int)(4 * scale));
                    graphics.FillRectangle(glyphBrush, panelX + (int)(48 * scale), y + (int)(16 * scale), (int)(86 * scale), (int)(4 * scale));
                    Brush stoneBrush = row % 2 == 0 ? darkStoneBrush : whiteStoneBrush;
                    if (row % 2 != 0)
                    {
                        using (Brush blueBrush = new SolidBrush(Color.FromArgb(64, 104, 130)))
                            graphics.FillEllipse(blueBrush, panelX + (int)(168 * scale), y + (int)(4 * scale), (int)(30 * scale), (int)(26 * scale));
                    }
                    graphics.FillEllipse(stoneBrush, panelX + (int)(172 * scale), y + (int)(6 * scale), (int)(22 * scale), (int)(22 * scale));
                }
            }
            return bitmap;
        }

        private static Bitmap CreateFoxPlayerListPanel()
        {
            Bitmap bitmap = new Bitmap(350, 150);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen borderPen = new Pen(Color.FromArgb(185, 188, 190)))
            using (Pen separatorPen = new Pen(Color.FromArgb(205, 208, 208)))
            using (Brush panelBrush = new SolidBrush(Color.FromArgb(242, 242, 238)))
            using (Brush alternateRowBrush = new SolidBrush(Color.FromArgb(233, 235, 235)))
            using (Brush purpleGlyphBrush = new SolidBrush(Color.FromArgb(190, 50, 220)))
            using (Brush redGlyphBrush = new SolidBrush(Color.FromArgb(230, 40, 35)))
            using (Brush darkStoneBrush = new SolidBrush(Color.FromArgb(20, 20, 20)))
            using (Brush whiteStoneBrush = new SolidBrush(Color.FromArgb(248, 248, 242)))
            using (Brush whiteStoneShadowBrush = new SolidBrush(Color.FromArgb(150, 156, 160)))
            using (Brush flagBrush = new SolidBrush(Color.FromArgb(222, 42, 30)))
            {
                graphics.Clear(Color.FromArgb(242, 242, 238));
                graphics.FillRectangle(panelBrush, 0, 0, bitmap.Width, bitmap.Height);
                graphics.DrawLine(borderPen, 0, 27, bitmap.Width, 27);
                graphics.DrawLine(separatorPen, 32, 0, 32, 84);
                graphics.DrawLine(separatorPen, 206, 0, 206, 84);
                DrawPanelRow(graphics, 28, panelBrush, purpleGlyphBrush, darkStoneBrush, null, flagBrush, false);
                DrawPanelRow(graphics, 56, alternateRowBrush, redGlyphBrush, whiteStoneBrush, whiteStoneShadowBrush, flagBrush, true);
            }
            return bitmap;
        }

        private static void DrawPanelRow(
            Graphics graphics,
            int y,
            Brush rowBrush,
            Brush glyphBrush,
            Brush stoneBrush,
            Brush stoneShadowBrush,
            Brush flagBrush,
            bool alternateGlyphShape)
        {
            graphics.FillRectangle(rowBrush, 0, y, 350, 28);
            graphics.FillRectangle(glyphBrush, 38, y + 8, alternateGlyphShape ? 92 : 72, 4);
            graphics.FillRectangle(glyphBrush, alternateGlyphShape ? 52 : 38, y + 16, alternateGlyphShape ? 62 : 84, 4);
            if (stoneShadowBrush != null)
                graphics.FillEllipse(stoneShadowBrush, 138, y + 5, 23, 23);
            graphics.FillEllipse(stoneBrush, 140, y + 6, 20, 20);
            graphics.FillRectangle(flagBrush, 207, y + 7, 22, 14);
        }

        private static Bitmap Crop(Bitmap source, PixelRect bounds)
        {
            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(
                    source,
                    new Rectangle(0, 0, bounds.Width, bounds.Height),
                    new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    GraphicsUnit.Pixel);
            }
            return bitmap;
        }
    }
}
