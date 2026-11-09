using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;

namespace ZenithFanUtility
{
    // a scheduled task that runs "ZenithFanUtility.exe -launch" as SYSTEM. that short run starts the real app
    // as SYSTEM in the signed-in user's session (see SystemLauncher), which newer asus drivers require.
    // the task always exists once the app has been started: without triggers it is only run on demand when the
    // exe is opened; "Start with Windows" adds logon, boot+5 min and resume triggers to the same task.
    internal static class StartupTask
    {
        private const string TaskName = "ZenithFanUtility";
        private const string LegacyRunValue = "AsusFanControl";
        private const int ErrorCancelled = 1223;

        public static bool Exists()
        {
            return RunSchtasks($"/Query /TN \"{TaskName}\"", elevate: false, out _) == 0;
        }

        public static bool IsStartupEnabled()
        {
            return RunSchtasks($"/Query /TN \"{TaskName}\" /XML", elevate: false, out string xml) == 0
                && xml.Contains("<LogonTrigger");
        }

        // returns false if the user cancelled the uac prompt or schtasks failed
        public static bool Set(bool enable, out string message)
        {
            RemoveLegacyRunEntry();

            int result = Register(enable);
            if (result != 0)
            {
                message = result == ErrorCancelled ? null : $"Could not update the startup task (schtasks exit code {result}).";
                return false;
            }

            message = enable
                ? "Zenith Fan Utility will start at logon, after sleep or hibernation, and again within 5 minutes if it ever closes."
                : null;
            return true;
        }

        // makes sure the task exists and points at this exe, keeping the current startup choice
        public static bool EnsureRegistered()
        {
            RemoveLegacyRunEntry();
            return Register(IsStartupEnabled()) == 0;
        }

        public static bool RunNow()
        {
            return RunSchtasks($"/Run /TN \"{TaskName}\"", elevate: true, out _) == 0;
        }

        // re-registers an existing task so its definition matches this build; needs no prompt when already elevated
        public static void RefreshIfElevated()
        {
            try
            {
                if (SystemLauncher.IsElevated && Exists())
                    EnsureRegistered();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Startup task refresh failed: " + ex.Message);
            }
        }

        // called from the task's "-launch" run (SYSTEM, session 0): put the app into the console session as SYSTEM
        public static void LaunchInUserSession()
        {
            int? sessionId = FindUserSessionId();
            if (sessionId == null)
                return; // nobody signed in yet; the next logon/resume/5-minute run tries again

            try
            {
                SystemLauncher.StartAsSystemInSession(sessionId.Value, "-tray");
            }
            catch (Win32Exception ex)
            {
                Debug.WriteLine("Launching into the user session failed: " + ex.Message);
            }
        }

        private static int Register(bool startWithWindows)
        {
            string xmlPath = Path.Combine(Path.GetTempPath(), "ZenithFanUtility_task_" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(xmlPath, BuildTaskXml(startWithWindows), System.Text.Encoding.Unicode);
            try
            {
                return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F", elevate: true, out _);
            }
            finally
            {
                try { File.Delete(xmlPath); } catch (IOException) { }
            }
        }

        // a short-lived "-launch" run as SYSTEM (no console flash) which starts the app only if it isn't
        // already running. no trigger is tied to a user name: the launcher finds the signed-in session itself,
        // and a name read from the wrong session once registered a logon trigger for SYSTEM that never fired
        private static string BuildTaskXml(bool startWithWindows)
        {
            string triggers = "";
            if (startWithWindows)
            {
                // Power-Troubleshooter 1: resumed from sleep or hibernate; Kernel-Power 507: left modern standby
                string resumeQuery = SecurityElement.Escape(
                    "<QueryList><Query Id=\"0\" Path=\"System\">" +
                    "<Select Path=\"System\">*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]</Select>" +
                    "<Select Path=\"System\">*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=507]]</Select>" +
                    "</Query></QueryList>");

                triggers = $@"
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT10S</Delay>
    </LogonTrigger>
    <BootTrigger>
      <Enabled>true</Enabled>
      <Delay>PT1M</Delay>
      <Repetition>
        <Interval>PT5M</Interval>
        <StopAtDurationEnd>false</StopAtDurationEnd>
      </Repetition>
    </BootTrigger>
    <EventTrigger>
      <Enabled>true</Enabled>
      <Subscription>{resumeQuery}</Subscription>
      <Delay>PT5S</Delay>
    </EventTrigger>";
            }

            string exePath = Application.ExecutablePath;
            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Starts Zenith Fan Utility as SYSTEM so it can control the fans.</Description>
  </RegistrationInfo>
  <Triggers>{triggers}
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{SecurityElement.Escape(exePath)}</Command>
      <Arguments>-launch</Arguments>
      <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exePath))}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
        }

        // the physical console's session if someone is signed in there, otherwise any active signed-in session
        private static int? FindUserSessionId()
        {
            int console = unchecked((int)WTSGetActiveConsoleSessionId());
            if (console > 0 && !string.IsNullOrEmpty(QuerySessionString(console, WtsUserName)))
                return console;

            if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out IntPtr sessions, out int count))
                return null;
            try
            {
                int size = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                for (int i = 0; i < count; i++)
                {
                    var info = Marshal.PtrToStructure<WTS_SESSION_INFO>(sessions + i * size);
                    if (info.SessionId > 0 && info.State == WtsActive && !string.IsNullOrEmpty(QuerySessionString(info.SessionId, WtsUserName)))
                        return info.SessionId;
                }
                return null;
            }
            finally
            {
                WTSFreeMemory(sessions);
            }
        }

        private static void RemoveLegacyRunEntry()
        {
            // older builds used the run key, which starts the app unelevated so fan control never applied
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                key?.DeleteValue(LegacyRunValue, false);
            }
        }

        private static int RunSchtasks(string arguments, bool elevate, out string output)
        {
            output = "";
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };

            if (elevate && !SystemLauncher.IsElevated)
            {
                psi.UseShellExecute = true;
                psi.Verb = "runas";
            }
            else
            {
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
            }

            try
            {
                using (var process = Process.Start(psi))
                {
                    if (!psi.UseShellExecute)
                    {
                        var error = process.StandardError.ReadToEndAsync();
                        output = process.StandardOutput.ReadToEnd();
                        error.Wait();
                    }
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return ErrorCancelled;
            }
        }

        private const int WtsUserName = 5;
        private const int WtsActive = 0;

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(IntPtr hServer, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

        [DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr memory);

        [StructLayout(LayoutKind.Sequential)]
        private struct WTS_SESSION_INFO
        {
            public int SessionId;
            public IntPtr WinStationName;
            public int State;
        }

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSEnumerateSessions(IntPtr hServer, int reserved, int version, out IntPtr sessionInfo, out int count);

        [DllImport("kernel32.dll")]
        private static extern uint WTSGetActiveConsoleSessionId();

        private static string QuerySessionString(int sessionId, int infoClass)
        {
            if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out IntPtr buffer, out _))
                return null;
            try
            {
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }
    }
}
