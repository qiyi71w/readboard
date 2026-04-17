using System;
using System.Drawing;
using System.Windows.Forms;

namespace readboard
{
    public partial class MagnifierForm : Form
    {
        private const int CaptureSize = 20;
        private const int CaptureOffset = CaptureSize / 2;
        private const int ZoomSize = 120;
        private const int CrosshairThickness = 5;
        private const int CrosshairHalfLength = 15;

        private static readonly Size CaptureBitmapSize = new Size(CaptureSize, CaptureSize);
        private static readonly Rectangle ZoomBounds = new Rectangle(0, 0, ZoomSize, ZoomSize);

        private Bitmap captureBitmap;
        private Bitmap zoomBitmap;
        private Graphics captureGraphics;
        private Graphics zoomGraphics;
        private Pen crosshairPen;
        private readonly MainForm host;

        internal MagnifierForm(MainForm host)
        {
            InitializeComponent();
            this.host = RequireHost(host);
            this.Text = getLangStr("MagnifierForm_title");
            InitializeMagnifierBuffers();
        }

        private String getLangStr(String itemName)
        {
            String result = "";
            try
            {
                result = Program.CurrentContext.LanguageItems[itemName].ToString();
            }
            catch (Exception e)
            {
                GetHost().SendError(e.ToString());
            }
            return result;
        }

        public void setPic(int x, int y)
        {
            CaptureScreenArea(x, y);
            RenderMagnifierFrame();
            pictureBox1.Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ReleaseMagnifierBuffers();
            base.OnFormClosed(e);
        }

        private void InitializeMagnifierBuffers()
        {
            captureBitmap = new Bitmap(CaptureSize, CaptureSize);
            captureGraphics = Graphics.FromImage(captureBitmap);
            zoomBitmap = new Bitmap(ZoomSize, ZoomSize);
            zoomGraphics = Graphics.FromImage(zoomBitmap);
            crosshairPen = new Pen(Color.Red, CrosshairThickness);

            zoomGraphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            zoomGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            zoomGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            pictureBox1.Image = zoomBitmap;
        }

        private void CaptureScreenArea(int x, int y)
        {
            captureGraphics.CopyFromScreen(x - CaptureOffset, y - CaptureOffset, 0, 0, CaptureBitmapSize);
        }

        private void RenderMagnifierFrame()
        {
            zoomGraphics.Clear(Color.Transparent);
            zoomGraphics.DrawImage(captureBitmap, ZoomBounds, 0, 0, CaptureSize, CaptureSize, GraphicsUnit.Pixel);
            DrawCrosshair();
        }

        private void DrawCrosshair()
        {
            int center = ZoomSize / 2;
            zoomGraphics.DrawLine(crosshairPen, center - CrosshairHalfLength, center, center + CrosshairHalfLength, center);
            zoomGraphics.DrawLine(crosshairPen, center, center - CrosshairHalfLength, center, center + CrosshairHalfLength);
        }

        private void ReleaseMagnifierBuffers()
        {
            if (pictureBox1 != null && !pictureBox1.IsDisposed)
            {
                pictureBox1.Image = null;
            }

            if (zoomGraphics != null)
            {
                zoomGraphics.Dispose();
                zoomGraphics = null;
            }

            if (captureGraphics != null)
            {
                captureGraphics.Dispose();
                captureGraphics = null;
            }

            if (zoomBitmap != null)
            {
                zoomBitmap.Dispose();
                zoomBitmap = null;
            }

            if (captureBitmap != null)
            {
                captureBitmap.Dispose();
                captureBitmap = null;
            }

            if (crosshairPen != null)
            {
                crosshairPen.Dispose();
                crosshairPen = null;
            }
        }

        private MainForm GetHost()
        {
            if (host.IsDisposed)
                throw new InvalidOperationException("MainForm host is unavailable.");
            return host;
        }

        private static MainForm RequireHost(MainForm host)
        {
            if (host == null || host.IsDisposed)
                throw new InvalidOperationException("MainForm host is unavailable.");
            return host;
        }
    }
}
