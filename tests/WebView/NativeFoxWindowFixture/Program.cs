using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Text;

// External native input only: production window discovery and UI Automation
// read this process. No ReadBoard references or state/bridge injection.
internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string title,
        int style, int x, int y, int width, int height, IntPtr parent,
        IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int capacity);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        using var window = new Form
        {
            Text = "Native acceptance > 123号 对弈中",
            ClientSize = new Size(700, 480),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(720, 40)
        };
        var players = new GroupBox
        {
            Text = "CRoomPlayerListPanel",
            Location = new Point(490, 20),
            Size = new Size(190, 220)
        };
        string[] names = { "NativeWhite", "9段", "NativeBlack", "9段" };
        for (int i = 0; i < names.Length; i++)
            players.Controls.Add(new Label
            {
                Text = names[i], AutoSize = true,
                Location = new Point(15, 35 + i * 35), TabIndex = i
            });
        window.Controls.Add(players);
        window.Shown += (_, _) =>
        {
            // The locator requires Fox's real board class/title combination.
            IntPtr board = CreateWindowEx(0, "#32770", "CChessboardPanel",
                0x50000000, 10, 10, 460, 460, window.Handle,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (board == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Console.WriteLine("NATIVE_FOX_READY " + board.ToInt64());
            var caption = new StringBuilder(256);
            GetWindowText(players.Handle, caption, caption.Capacity);
            Console.WriteLine("PLAYERS_PANEL visible=" + IsWindowVisible(players.Handle) + " caption=" + caption);
            Console.Out.Flush();
        };
        Application.Run(window);
    }
}
