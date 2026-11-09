using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ZenithFanUtility
{
    internal static class Program
    {
        private const string InstanceMutexName = @"Global\ZenithFanUtility";
        private const string AppName = "Zenith Fan Utility";
        private const uint MSGFLT_ADD = 1;
        private const int ErrorCancelled = 1223;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        private static extern bool ChangeWindowMessageFilter(uint message, uint flag);

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // the scheduled task runs "-launch" as SYSTEM (on demand, and at logon/resume/every few minutes when
            // "Start with Windows" is on); it only starts the real app, in the user's session, when it isn't running
            if (args.Contains("-launch"))
            {
                if (!IsAlreadyRunning())
                    StartupTask.LaunchInUserSession();
                return;
            }

            // session 0 has no visible desktop, so an instance there has no tray icon and can't be closed;
            // exit and let the next "-launch" run start it in the signed-in user's session
            if (Process.GetCurrentProcess().SessionId == 0)
                return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // the fan driver only answers SYSTEM on newer asus drivers, so a normal start hands off to the task
            // (one uac prompt), which starts the app as SYSTEM. "-nosystem" keeps the old run-as-you behaviour.
            if (!SystemLauncher.IsSystem && !args.Contains("-nosystem"))
            {
                StartThroughTask(args);
                return;
            }

            Mutex instanceMutex;
            try
            {
                instanceMutex = new Mutex(true, InstanceMutexName, out bool createdNew);
                if (!createdNew)
                {
                    NotifyAlreadyRunning(args);
                    return;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // a SYSTEM instance owns the mutex and this unelevated one can't open it
                NotifyAlreadyRunning(args);
                return;
            }

            // explorer broadcasts TaskbarCreated when it (re)starts, and NotifyIcon re-adds the icon on it, but
            // windows drops that message on its way into an elevated/SYSTEM process unless it is allowed through
            ChangeWindowMessageFilter(RegisterWindowMessage("TaskbarCreated"), MSGFLT_ADD);

            Application.Run(new Form1(args));
            GC.KeepAlive(instanceMutex);
        }

        private static void StartThroughTask(string[] args)
        {
            if (IsAlreadyRunning())
            {
                NotifyAlreadyRunning(args);
                return;
            }

            if (!SystemLauncher.IsElevated)
            {
                // registering and running a SYSTEM task needs admin: ask once, the elevated copy does the rest
                var psi = new ProcessStartInfo(Application.ExecutablePath, string.Join(" ", args.Select(a => $"\"{a}\"")))
                {
                    UseShellExecute = true,
                    Verb = "runas",
                };
                try
                {
                    Process.Start(psi)?.Dispose();
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
                {
                    MessageBox.Show($"{AppName} needs administrator rights to control the fans.", AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            if (!StartupTask.EnsureRegistered() || !StartupTask.RunNow())
            {
                MessageBox.Show($"{AppName} could not register or start its SYSTEM task.\n\n" +
                    "You can still run it as administrator without SYSTEM rights by starting it with -nosystem, " +
                    "but newer ASUS drivers ignore fan commands from anything but SYSTEM.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // the task starts the app in the tray; once it is up, ask it to show its window
            if (args.Contains("-tray"))
                return;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (SystemLauncher.SignalShow())
                    return;
                Thread.Sleep(250);
            }
        }

        private static bool IsAlreadyRunning()
        {
            try
            {
                if (Mutex.TryOpenExisting(InstanceMutexName, out Mutex existing))
                {
                    existing.Dispose();
                    return true;
                }
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        private static void NotifyAlreadyRunning(string[] args)
        {
            // tray starts come from the startup task and must stay silent
            if (args.Contains("-tray"))
                return;

            // bring the running instance's window up; fall back to a hint if it is too old to listen
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (SystemLauncher.SignalShow())
                    return;
                Thread.Sleep(250);
            }
            MessageBox.Show($"{AppName} is already running. Use its tray icon to open it.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
