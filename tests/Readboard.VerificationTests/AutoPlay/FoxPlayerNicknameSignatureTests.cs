using System.Drawing;
using Xunit;
using readboard;

namespace Readboard.VerificationTests.AutoPlay
{
    public sealed class FoxPlayerNicknameSignatureTests
    {
        [Fact]
        public void FromBitmap_MatchesIdenticalSnippet()
        {
            using (Bitmap source = CreateNicknameSnippet(Color.Red, 0, false))
            {
                FoxPlayerNicknameSignature signature = FoxPlayerNicknameSignature.FromBitmap(source);

                FoxPlayerNicknameMatch match = signature.Match(new[] { source });

                Assert.True(signature.IsValid);
                Assert.True(match.IsReliable);
                Assert.Equal(0, match.Index);
                Assert.True(match.Score > 0.95);
            }
        }

        [Fact]
        public void FromBitmap_MatchesSmallBrightnessVariation()
        {
            using (Bitmap source = CreateNicknameSnippet(Color.FromArgb(230, 40, 40), 0, false))
            using (Bitmap candidate = CreateNicknameSnippet(Color.FromArgb(250, 70, 70), 1, false))
            {
                FoxPlayerNicknameSignature signature = FoxPlayerNicknameSignature.FromBitmap(source);

                FoxPlayerNicknameMatch match = signature.Match(new[] { candidate });

                Assert.True(match.IsReliable);
                Assert.True(match.Score > 0.75);
            }
        }

        [Fact]
        public void FromBitmap_DoesNotMatchDifferentGlyphShape()
        {
            using (Bitmap source = CreateNicknameSnippet(Color.Red, 0, false))
            using (Bitmap different = CreateNicknameSnippet(Color.Red, 0, true))
            {
                FoxPlayerNicknameSignature signature = FoxPlayerNicknameSignature.FromBitmap(source);

                FoxPlayerNicknameMatch match = signature.Match(new[] { different });

                Assert.False(match.IsReliable);
                Assert.True(match.Score < 0.65);
            }
        }

        [Fact]
        public void FromBitmap_ReturnsInvalidForBlankSnippet()
        {
            using (Bitmap blank = new Bitmap(96, 24))
            using (Graphics graphics = Graphics.FromImage(blank))
            {
                graphics.Clear(Color.FromArgb(245, 245, 240));

                FoxPlayerNicknameSignature signature = FoxPlayerNicknameSignature.FromBitmap(blank);

                Assert.False(signature.IsValid);
                Assert.Equal(string.Empty, signature.Serialize());
            }
        }

        [Fact]
        public void Match_SelectsOnlyCandidateWithClearMargin()
        {
            using (Bitmap source = CreateNicknameSnippet(Color.Fuchsia, 0, false))
            using (Bitmap row1 = CreateNicknameSnippet(Color.Fuchsia, 0, true))
            using (Bitmap row2 = CreateNicknameSnippet(Color.BlueViolet, 0, true))
            using (Bitmap row3 = CreateNicknameSnippet(Color.FromArgb(220, 0, 220), 2, false))
            using (Bitmap row4 = CreateNicknameSnippet(Color.Fuchsia, 0, true))
            {
                FoxPlayerNicknameSignature signature = FoxPlayerNicknameSignature.FromBitmap(source);

                FoxPlayerNicknameMatch match = signature.Match(new[] { row1, row2, row3, row4 });

                Assert.True(match.IsReliable);
                Assert.Equal(2, match.Index);
                Assert.True(match.Score > 0.75);
                Assert.True(match.Margin > 0.25);
            }
        }

        private static Bitmap CreateNicknameSnippet(Color glyphColor, int offsetX, bool alternateShape)
        {
            Bitmap bitmap = new Bitmap(96, 24);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(glyphColor, 2))
            {
                graphics.Clear(Color.FromArgb(245, 245, 240));
                if (alternateShape)
                {
                    graphics.DrawLine(pen, 10 + offsetX, 5, 60 + offsetX, 5);
                    graphics.DrawLine(pen, 12 + offsetX, 14, 70 + offsetX, 18);
                    graphics.DrawLine(pen, 30 + offsetX, 3, 32 + offsetX, 20);
                }
                else
                {
                    graphics.DrawLine(pen, 8 + offsetX, 6, 72 + offsetX, 6);
                    graphics.DrawLine(pen, 8 + offsetX, 6, 8 + offsetX, 18);
                    graphics.DrawLine(pen, 20 + offsetX, 18, 76 + offsetX, 18);
                    graphics.DrawLine(pen, 42 + offsetX, 4, 54 + offsetX, 20);
                }
            }
            return bitmap;
        }
    }
}
