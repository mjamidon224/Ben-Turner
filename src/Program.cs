using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace TATimesheet
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // DPI awareness (crisp text on high-DPI screens) is declared in app.manifest.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool firstInstance;
            using (var mutex = new Mutex(true, BuildInfo.MutexName, out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show(BuildInfo.AppName + " is already open. Check your taskbar.", BuildInfo.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.ThreadException += (s, e) => ShowError(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);

                var store = new DataStore();
                AppData data;
                try
                {
                    data = store.Load();
                }
                catch (Exception ex)
                {
                    string moved = null;
                    try { moved = store.QuarantineBrokenFile(); } catch (Exception) { }
                    MessageBox.Show(
                        "Your timesheet data file couldn't be read:\n\n" + ex.Message + "\n\n" +
                        (moved != null ? "It was set aside as:\n" + moved + "\n\n" : "") +
                        "Starting fresh. Use File > Restore from backup to get your entries back " +
                        "(daily backups are in " + store.BackupFolder + ").",
                        BuildInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    data = AppData.CreateDefault();
                }

                Application.Run(new MainForm(store, data));
                GC.KeepAlive(mutex);
            }
        }

        static void ShowError(Exception ex)
        {
            if (ex == null) return;
            MessageBox.Show("Something went wrong:\n\n" + ex.Message + "\n\nYour saved entries are safe.",
                BuildInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
