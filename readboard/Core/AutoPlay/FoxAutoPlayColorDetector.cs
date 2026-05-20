using System.Collections.Generic;
using System.Drawing;

namespace readboard
{
    internal static class FoxAutoPlayColorDetector
    {
        public static AutoPlayColorResolution Detect(Bitmap windowBitmap, SyncMode syncMode, string nicknameSignature)
        {
            FoxPlayerNicknameSignature signature = FoxPlayerNicknameSignature.FromString(nicknameSignature);
            if (!signature.IsValid)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.Unconfigured);

            IList<FoxPlayerRowCandidate> rows = FoxPlayerRowLocator.Locate(windowBitmap, syncMode);
            if (rows.Count == 0)
                return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.NicknameNotMatched);

            List<Bitmap> nicknameSnippets = new List<Bitmap>();
            try
            {
                foreach (FoxPlayerRowCandidate row in rows)
                    nicknameSnippets.Add(Crop(windowBitmap, row.NicknameBounds));

                FoxPlayerNicknameMatch match = signature.Match(nicknameSnippets);
                if (!match.IsReliable || match.Index < 0 || match.Index >= rows.Count)
                    return AutoPlayColorResolution.Unknown(AutoPlayColorStatus.NicknameNotMatched);

                using (Bitmap icon = Crop(windowBitmap, rows[match.Index].StoneIconBounds))
                    return FoxPlayerStoneIconDetector.Detect(icon);
            }
            finally
            {
                foreach (Bitmap bitmap in nicknameSnippets)
                {
                    if (bitmap != null)
                        bitmap.Dispose();
                }
            }
        }

        private static Bitmap Crop(Bitmap source, PixelRect bounds)
        {
            if (source == null || bounds == null || bounds.IsEmpty)
                return null;

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
