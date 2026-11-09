using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace ZenithFanUtility
{
    // replaces "psexec -s -i": newer asus drivers only accept fan commands from SYSTEM, so the scheduled task
    // (which runs as SYSTEM in session 0) copies its own token, points the copy at the signed-in user's
    // session and starts the app with it there, where it has a tray icon and a window.
    internal static class SystemLauncher
    {
        public const string ShowEventName = @"Global\ZenithFanUtility_Show";

        public static bool IsSystem
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    return identity.User != null && identity.User.IsWellKnown(WellKnownSidType.LocalSystemSid);
            }
        }

        public static bool IsElevated
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        // starts this exe as SYSTEM in the given session; only works when the caller is SYSTEM
        public static void StartAsSystemInSession(int sessionId, string arguments)
        {
            EnablePrivileges("SeTcbPrivilege", "SeAssignPrimaryTokenPrivilege", "SeIncreaseQuotaPrivilege");

            IntPtr ownToken = IntPtr.Zero, token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_QUERY, out ownToken))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken");
                if (!DuplicateTokenEx(ownToken, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out token))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx");
                if (!SetTokenInformation(token, TokenSessionId, ref sessionId, sizeof(int)))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation");

                string exe = Application.ExecutablePath;
                CreateProcessWithToken(token, exe, $"\"{exe}\" {arguments}", Path.GetDirectoryName(exe), IntPtr.Zero);
            }
            finally
            {
                if (token != IntPtr.Zero) CloseHandle(token);
                if (ownToken != IntPtr.Zero) CloseHandle(ownToken);
            }
        }

        // opens a link in the signed-in user's browser. a browser started by the SYSTEM instance would itself run
        // as SYSTEM, and its save/open dialogs would then hand full control of the pc to whoever is at the keyboard
        public static void OpenUrl(string url)
        {
            if (!IsSystem)
            {
                Process.Start(url);
                return;
            }

            IntPtr userToken = IntPtr.Zero, environment = IntPtr.Zero;
            try
            {
                EnablePrivileges("SeTcbPrivilege");
                int sessionId = Process.GetCurrentProcess().SessionId;
                if (!WTSQueryUserToken(sessionId, out userToken))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken");
                if (!CreateEnvironmentBlock(out environment, userToken, false))
                    environment = IntPtr.Zero;

                string rundll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rundll32.exe");
                CreateProcessWithToken(userToken, rundll, $"\"{rundll}\" url.dll,FileProtocolHandler {url}",
                    Environment.GetFolderPath(Environment.SpecialFolder.System), environment);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OpenUrl failed: " + ex.Message);
                try { Clipboard.SetText(url); } catch (ExternalException) { }
                MessageBox.Show($"Could not open the link in your browser, so it was copied to the clipboard:\n\n{url}",
                    "Zenith Fan Utility", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
                if (userToken != IntPtr.Zero) CloseHandle(userToken);
            }
        }

        // the running instance waits on this event; any later launch (even unelevated) sets it to bring the window up
        public static EventWaitHandle CreateShowEvent()
        {
            var security = new EventWaitHandleSecurity();
            security.AddAccessRule(new EventWaitHandleAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), EventWaitHandleRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new EventWaitHandleAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), EventWaitHandleRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new EventWaitHandleAccessRule(
                new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
                EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, AccessControlType.Allow));
            return new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out _, security);
        }

        public static bool SignalShow()
        {
            try
            {
                using (var showEvent = EventWaitHandle.OpenExisting(ShowEventName, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize))
                {
                    // we were just started by the user, so we may hand our right to take the foreground to the app
                    AllowSetForegroundWindow(ASFW_ANY);
                    return showEvent.Set();
                }
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException || ex is UnauthorizedAccessException || ex is IOException)
            {
                return false;
            }
        }

        private static void CreateProcessWithToken(IntPtr token, string application, string commandLine, string workingDirectory, IntPtr environment)
        {
            var startup = new STARTUPINFO
            {
                cb = Marshal.SizeOf(typeof(STARTUPINFO)),
                lpDesktop = @"winsta0\default",
            };
            uint flags = environment != IntPtr.Zero ? CREATE_UNICODE_ENVIRONMENT : 0;
            if (!CreateProcessAsUser(token, application, commandLine, IntPtr.Zero, IntPtr.Zero, false, flags,
                    environment, workingDirectory, ref startup, out PROCESS_INFORMATION process))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser");
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
        }

        private static void EnablePrivileges(params string[] names)
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr token))
                return;
            try
            {
                foreach (string name in names)
                {
                    if (!LookupPrivilegeValue(null, name, out long luid))
                        continue;
                    var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                    AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }

        private const uint TOKEN_DUPLICATE = 0x0002;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint MAXIMUM_ALLOWED = 0x02000000;
        private const int SecurityImpersonation = 2;
        private const int TokenPrimary = 1;
        private const int TokenSessionId = 12;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        private const int ASFW_ANY = -1;

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetTokenInformation(IntPtr token, int infoClass, ref int info, int length);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string system, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, int length, IntPtr previous, IntPtr returnLength);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessAsUser(IntPtr token, string application, string commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string currentDirectory,
            ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQueryUserToken(int sessionId, out IntPtr token);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool DestroyEnvironmentBlock(IntPtr environment);
    }
}
