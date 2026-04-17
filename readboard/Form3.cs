using System;
using System.Drawing;
using System.Windows.Forms;

namespace readboard
{
    public partial class Form3 : Form
    {
        public  Form3()
        {
            InitializeComponent();
            this.ShowInTaskbar = false;
        }
        public IntPtr setPic() {
          //  System.IO.FileStream pFileStream = new System.IO.FileStream("screen.bmp", System.IO.FileMode.Open);
            pictureBox1.Image = Program.bitmap;
            // pFileStream.Close();
            // pFileStream.Dispose();

            Size newSize = new Size(pictureBox1.Size.Width+20, pictureBox1.Size.Height+50);
            this.Size = newSize;
           
            return this.pictureBox1.Handle;
        }
        public int getHwnd()
        {
            return (int)this.Handle;
        }
    }
}
