using System;
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

        [Theory]
        [InlineData(110)]
        [InlineData(249)]
        [InlineData(400)]
        public void LocatePlayerListPanel_ReturnsRowsWhenRoomPlayerPanelHeightChanges(int panelHeight)
        {
            using (Bitmap bitmap = CreateFoxPlayerListPanel(panelHeight))
            {
                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.LocatePlayerListPanel(bitmap);

                Assert.Equal(2, rows.Count);
                AssertRowsAreInsidePanel(bitmap, rows);
                Assert.InRange(rows[0].RowBounds.Height, 20, 36);
                Assert.InRange(rows[1].RowBounds.Height, 20, 36);
            }
        }

        [Theory]
        [InlineData(240)]
        [InlineData(300)]
        [InlineData(350)]
        [InlineData(520)]
        public void LocatePlayerListPanel_ReturnsRowsWhenRoomPlayerPanelWidthChanges(int panelWidth)
        {
            using (Bitmap bitmap = CreateFoxPlayerListPanel(panelWidth, 249))
            {
                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.LocatePlayerListPanel(bitmap);

                Assert.Equal(2, rows.Count);
                AssertRowsAreInsidePanel(bitmap, rows);
            }
        }

        [Theory]
        [InlineData(1.25f)]
        [InlineData(1.5f)]
        [InlineData(2.0f)]
        public void LocatePlayerListPanel_ReturnsRowsWhenRoomPlayerPanelIsScaled(float scale)
        {
            using (Bitmap bitmap = CreateScaledFoxPlayerListPanel(scale))
            {
                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.LocatePlayerListPanel(bitmap);

                Assert.Equal(2, rows.Count);
                AssertRowsAreInsidePanel(bitmap, rows);
            }
        }

        [Theory]
        [InlineData(240)]
        [InlineData(520)]
        public void DetectPlayerListPanel_ReturnsStoneColorWhenRoomPlayerPanelWidthChanges(int panelWidth)
        {
            using (Bitmap bitmap = CreateFoxPlayerListPanel(panelWidth, 249))
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

        [Fact]
        public void DetectPlayerListPanel_FindsEarlyStoneIconInWideRoomPlayerPanel()
        {
            using (Bitmap bitmap = CreateFoxPlayerListPanelWithEarlyStone(500, 249))
            {
                IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.LocatePlayerListPanel(bitmap);
                using (Bitmap nicknameSnippet = Crop(bitmap, rows[1].NicknameBounds))
                {
                    string signature = FoxPlayerNicknameSignature.FromBitmap(nicknameSnippet).Serialize();

                    AutoPlayColorResolution resolution = FoxAutoPlayColorDetector.DetectPlayerListPanel(bitmap, signature);

                    Assert.True(resolution.IsKnown);
                    Assert.Equal("black", resolution.PlayColor);
                    Assert.Equal(AutoPlayColorStatus.RecognizedBlack, resolution.Status);
                }
            }
        }

        [Fact]
        public void DetectPlayerListPanel_MatchesSavedSignatureWhenNicknameCropWidthChanges()
        {
            using (Bitmap sourceBitmap = CreateFoxPlayerListPanel(350, 249))
            using (Bitmap targetBitmap = CreateFoxPlayerListPanel(520, 249))
            {
                IList<FoxPlayerRowCandidate> sourceRows = FoxPlayerRowLocator.LocatePlayerListPanel(sourceBitmap);
                using (Bitmap nicknameSnippet = Crop(sourceBitmap, sourceRows[1].NicknameBounds))
                {
                    string signature = FoxPlayerNicknameSignature.FromBitmap(nicknameSnippet).Serialize();

                    AutoPlayColorResolution resolution = FoxAutoPlayColorDetector.DetectPlayerListPanel(targetBitmap, signature);

                    Assert.True(resolution.IsKnown);
                    Assert.Equal("white", resolution.PlayColor);
                    Assert.Equal(AutoPlayColorStatus.RecognizedWhite, resolution.Status);
                }
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
                    if (row % 2 == 0)
                    {
                        graphics.FillRectangle(glyphBrush, panelX + (int)(48 * scale), y + (int)(8 * scale), (int)(70 * scale), (int)(4 * scale));
                        graphics.FillRectangle(glyphBrush, panelX + (int)(48 * scale), y + (int)(16 * scale), (int)(86 * scale), (int)(4 * scale));
                    }
                    else
                    {
                        graphics.FillRectangle(glyphBrush, panelX + (int)(60 * scale), y + (int)(8 * scale), (int)(76 * scale), (int)(4 * scale));
                        graphics.FillRectangle(glyphBrush, panelX + (int)(48 * scale), y + (int)(16 * scale), (int)(56 * scale), (int)(4 * scale));
                    }
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
            return CreateFoxPlayerListPanel(350, 150);
        }

        private static Bitmap CreateFoxPlayerListPanel(int height)
        {
            return CreateFoxPlayerListPanel(350, height);
        }

        private static Bitmap CreateFoxPlayerListPanel(int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height);
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
                int flagLeft = GetPanelFlagLeft(bitmap.Width);
                graphics.DrawLine(separatorPen, 32, 0, 32, 84);
                graphics.DrawLine(separatorPen, flagLeft - 1, 0, flagLeft - 1, 84);
                DrawPanelRow(graphics, 28, bitmap.Width, panelBrush, purpleGlyphBrush, darkStoneBrush, null, flagBrush, false);
                DrawPanelRow(graphics, 56, bitmap.Width, alternateRowBrush, redGlyphBrush, whiteStoneBrush, whiteStoneShadowBrush, flagBrush, true);
            }
            return bitmap;
        }

        private static Bitmap CreateScaledFoxPlayerListPanel(float scale)
        {
            int width = (int)(350 * scale);
            int height = (int)(249 * scale);
            Bitmap bitmap = new Bitmap(width, height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen borderPen = new Pen(Color.FromArgb(185, 188, 190), scale))
            using (Pen separatorPen = new Pen(Color.FromArgb(205, 208, 208), scale))
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
                graphics.DrawLine(borderPen, 0, 27 * scale, bitmap.Width, 27 * scale);
                graphics.DrawLine(separatorPen, 32 * scale, 0, 32 * scale, 84 * scale);
                graphics.DrawLine(separatorPen, 206 * scale, 0, 206 * scale, 84 * scale);
                DrawScaledPanelRow(graphics, 28 * scale, scale, panelBrush, purpleGlyphBrush, darkStoneBrush, null, flagBrush, false);
                DrawScaledPanelRow(graphics, 56 * scale, scale, alternateRowBrush, redGlyphBrush, whiteStoneBrush, whiteStoneShadowBrush, flagBrush, true);
            }
            return bitmap;
        }

        private static Bitmap CreateFoxPlayerListPanelWithEarlyStone(int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height);
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
                DrawFixedPanelRow(graphics, 28, width, panelBrush, purpleGlyphBrush, whiteStoneBrush, whiteStoneShadowBrush, flagBrush, 140, false);
                DrawFixedPanelRow(graphics, 56, width, alternateRowBrush, redGlyphBrush, darkStoneBrush, null, flagBrush, 140, true);
            }
            return bitmap;
        }

        private static void DrawScaledPanelRow(
            Graphics graphics,
            float y,
            float scale,
            Brush rowBrush,
            Brush glyphBrush,
            Brush stoneBrush,
            Brush stoneShadowBrush,
            Brush flagBrush,
            bool alternateGlyphShape)
        {
            graphics.FillRectangle(rowBrush, 0, y, 350 * scale, 28 * scale);
            graphics.FillRectangle(glyphBrush, 38 * scale, y + 8 * scale, (alternateGlyphShape ? 92 : 72) * scale, 4 * scale);
            graphics.FillRectangle(glyphBrush, (alternateGlyphShape ? 52 : 38) * scale, y + 16 * scale, (alternateGlyphShape ? 62 : 84) * scale, 4 * scale);
            if (stoneShadowBrush != null)
                graphics.FillEllipse(stoneShadowBrush, 138 * scale, y + 5 * scale, 23 * scale, 23 * scale);
            graphics.FillEllipse(stoneBrush, 140 * scale, y + 6 * scale, 20 * scale, 20 * scale);
            graphics.FillRectangle(flagBrush, 207 * scale, y + 7 * scale, 22 * scale, 14 * scale);
        }

        private static void DrawFixedPanelRow(
            Graphics graphics,
            int y,
            int width,
            Brush rowBrush,
            Brush glyphBrush,
            Brush stoneBrush,
            Brush stoneShadowBrush,
            Brush flagBrush,
            int stoneLeft,
            bool alternateGlyphShape)
        {
            int nicknameLeft = Math.Max(38, width / 10 + 3);
            int firstGlyphWidth = Math.Min(alternateGlyphShape ? 84 : 72, Math.Max(20, stoneLeft - nicknameLeft - 10));
            int secondGlyphLeft = alternateGlyphShape ? nicknameLeft + 14 : nicknameLeft;
            int secondGlyphWidth = Math.Min(alternateGlyphShape ? 62 : 84, Math.Max(20, stoneLeft - secondGlyphLeft - 10));

            graphics.FillRectangle(rowBrush, 0, y, width, 28);
            if (alternateGlyphShape)
            {
                graphics.FillRectangle(glyphBrush, nicknameLeft + 18, y + 7, Math.Max(18, firstGlyphWidth / 2), 4);
                graphics.FillRectangle(glyphBrush, secondGlyphLeft, y + 16, secondGlyphWidth, 4);
                graphics.FillRectangle(glyphBrush, nicknameLeft + 6, y + 9, 4, 14);
            }
            else
            {
                graphics.FillRectangle(glyphBrush, nicknameLeft, y + 8, firstGlyphWidth, 4);
                graphics.FillRectangle(glyphBrush, secondGlyphLeft, y + 16, secondGlyphWidth, 4);
            }
            if (stoneShadowBrush != null)
                graphics.FillEllipse(stoneShadowBrush, stoneLeft - 2, y + 5, 23, 23);
            graphics.FillEllipse(stoneBrush, stoneLeft, y + 6, 20, 20);
            graphics.FillRectangle(flagBrush, 207, y + 7, 22, 14);
        }

        private static void DrawPanelRow(
            Graphics graphics,
            int y,
            int width,
            Brush rowBrush,
            Brush glyphBrush,
            Brush stoneBrush,
            Brush stoneShadowBrush,
            Brush flagBrush,
            bool alternateGlyphShape)
        {
            int nicknameLeft = Math.Max(38, width / 10 + 3);
            int stoneLeft = GetPanelStoneLeft(width);
            int firstGlyphWidth = Math.Min(alternateGlyphShape ? 92 : 72, Math.Max(20, stoneLeft - nicknameLeft - 10));
            int secondGlyphLeft = alternateGlyphShape ? nicknameLeft + 14 : nicknameLeft;
            int secondGlyphWidth = Math.Min(alternateGlyphShape ? 62 : 84, Math.Max(20, stoneLeft - secondGlyphLeft - 10));

            graphics.FillRectangle(rowBrush, 0, y, width, 28);
            if (alternateGlyphShape)
            {
                graphics.FillRectangle(glyphBrush, nicknameLeft + 18, y + 7, Math.Max(18, firstGlyphWidth / 2), 4);
                graphics.FillRectangle(glyphBrush, secondGlyphLeft, y + 16, secondGlyphWidth, 4);
                graphics.FillRectangle(glyphBrush, nicknameLeft + 6, y + 9, 4, 14);
            }
            else
            {
                graphics.FillRectangle(glyphBrush, nicknameLeft, y + 8, firstGlyphWidth, 4);
                graphics.FillRectangle(glyphBrush, secondGlyphLeft, y + 16, secondGlyphWidth, 4);
            }
            if (stoneShadowBrush != null)
                graphics.FillEllipse(stoneShadowBrush, stoneLeft - 2, y + 5, 23, 23);
            graphics.FillEllipse(stoneBrush, stoneLeft, y + 6, 20, 20);
            graphics.FillRectangle(flagBrush, GetPanelFlagLeft(width), y + 7, 22, 14);
        }

        private static int GetPanelStoneLeft(int width)
        {
            return Math.Min(width - 48, Math.Max(74, width * 40 / 100));
        }

        private static int GetPanelFlagLeft(int width)
        {
            return Math.Min(width - 32, Math.Max(GetPanelStoneLeft(width) + 30, width * 59 / 100));
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
