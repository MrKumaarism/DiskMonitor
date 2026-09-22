using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DiskMonitor;

static class Program
{
    private const string AppGuid = "DiskMonitor-4DDA3865-5FD4-47F2-AB90-4803BCE34DB1";

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\" + AppGuid, out bool createdNew);
        if (!createdNew)
        {
            // Another instance is already running; exit cleanly
            return;
        }

        SetProcessDPIAware();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
