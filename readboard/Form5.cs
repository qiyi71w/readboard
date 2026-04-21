using System;
using System.Drawing;
using System.Windows.Forms;

namespace readboard
{
    public partial class MagnifierForm : Form
    {
        private const int CaptureLogicalSize = 20;
        private const int ZoomLogicalSize = 120;
        private const int CrosshairLogicalThickness = 5;
        private const int CrosshairLogicalHalfLength = 15;
        private const int WindowLogicalExtraHeight = 1;

        private Bitmap captureBitmap;
        private Bitmap zoomBitmap;
        private Graphics captureGraphics;
        private Graphics zoomGraphics;
        private Pen crosshairPen;
        private readonly MainForm host;
        private Size captureBitmapSize = new Size(CaptureLogicalSize, CaptureLogicalSize);
        private Rectangle zoomBounds = new Rectangle(0, 0, ZoomLogicalSize, ZoomLogicalSize);
        private int captureOffset = CaptureLogicalSize / 2;
        private int crosshairHalfLength = CrosshairLogicalHalfLength;
        private bool isApplyingMagnifierLayout;

        internal MagnifierForm(MainForm host)
        {
            InitializeComponent();
            this.host = RequireHost(host);
            this.Text = getLangStr("MagnifierForm_title");
            ApplyMagnifierLayout();
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
            ReleaseMagnifierBuffers();
            captureBitmap = new Bitmap(captureBitmapSize.Width, captureBitmapSize.Height);
            captureGraphics = Graphics.FromImage(captureBitmap);
            zoomBitmap = new Bitmap(zoomBounds.Width, zoomBounds.Height);
            zoomGraphics = Graphics.FromImage(zoomBitmap);
            crosshairPen = new Pen(Color.Red, Math.Max(1, ScaleValue(CrosshairLogicalThickness)));

            zoomGraphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            zoomGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            zoomGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            pictureBox1.Image = zoomBitmap;
        }

        private void CaptureScreenArea(int x, int y)
        {
            captureGraphics.CopyFromScreen(x - captureOffset, y - captureOffset, 0, 0, captureBitmapSize);
        }

        private void RenderMagnifierFrame()
        {
            zoomGraphics.Clear(Color.Transparent);
            zoomGraphics.DrawImage(
                captureBitmap,
                zoomBounds,
                0,
                0,
                captureBitmapSize.Width,
                captureBitmapSize.Height,
                GraphicsUnit.Pixel);
            DrawCrosshair();
        }

        private void DrawCrosshair()
        {
            int center = zoomBounds.Width / 2;
            zoomGraphics.DrawLine(crosshairPen, center - crosshairHalfLength, center, center + crosshairHalfLength, center);
            zoomGraphics.DrawLine(crosshairPen, center, center - crosshairHalfLength, center, center + crosshairHalfLength);
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyMagnifierLayout();
            InitializeMagnifierBuffers();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            ApplyMagnifierLayout();
            InitializeMagnifierBuffers();
        }

        private void ApplyMagnifierLayout()
        {
            if (isApplyingMagnifierLayout)
                return;

            isApplyingMagnifierLayout = true;
            SuspendLayout();
            try
            {
                DoubleBuffered = true;
                captureBitmapSize = CreateScaledSquare(CaptureLogicalSize);
                captureOffset = captureBitmapSize.Width / 2;
                int zoomSize = Math.Max(ScaleValue(ZoomLogicalSize), captureBitmapSize.Width * 2);
                zoomBounds = new Rectangle(0, 0, zoomSize, zoomSize);
                crosshairHalfLength = Math.Max(ScaleValue(CrosshairLogicalHalfLength), ScaleValue(8));
                pictureBox1.Location = Point.Empty;
                pictureBox1.Size = zoomBounds.Size;
                ClientSize = new Size(zoomBounds.Width, zoomBounds.Height + ScaleValue(WindowLogicalExtraHeight));
            }
            finally
            {
                ResumeLayout(false);
                PerformLayout();
                isApplyingMagnifierLayout = false;
            }
        }

        private Size CreateScaledSquare(int logicalSize)
        {
            int size = Math.Max(ScaleValue(logicalSize), 8);
            if ((size & 1) != 0)
                size++;
            return new Size(size, size);
        }

        private int ScaleValue(int logicalValue)
        {
            double scale = IsHandleCreated
                ? DisplayScaling.GetScaleForWindow(Handle)
                : DisplayScaling.DefaultScale;
            return (int)Math.Round(logicalValue * DisplayScaling.NormalizeScale(scale));
        }

        private static MainForm RequireHost(MainForm host)
        {
            if (host == null || host.IsDisposed)
                throw new InvalidOperationException("MainForm host is unavailable.");
            return host;
        }
    }
}
