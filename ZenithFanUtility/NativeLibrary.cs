using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ZenithFanUtility
{
    // AsusWinIO64.dll ships inside the exe. it is written to disk once and loaded by full path before the first
    // fan call, so the [DllImport("AsusWinIO64.dll")] declarations bind to the already-loaded module.
    internal static class NativeLibrary
    {
        private const string DllName = "AsusWinIO64.dll";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);

        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

        public static void LoadAsusWinIO()
        {
            byte[] data;
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(DllName))
            {
                if (resource == null)
                    throw new FileNotFoundException("The embedded fan driver library is missing from this build.", DllName);
                data = new byte[resource.Length];
                int read = 0;
                while (read < data.Length)
                    read += resource.Read(data, read, data.Length - read);
            }

            string hash;
            using (var sha = SHA256.Create())
                hash = string.Concat(sha.ComputeHash(data).Take(8).Select(b => b.ToString("x2")));

            // per-account folder first (the SYSTEM instance gets the protected systemprofile one);
            // a fresh temp folder if that one can't be written or holds a different file
            string[] folders =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZenithFanUtility", "native", hash),
                Path.Combine(Path.GetTempPath(), "ZenithFanUtility-" + Guid.NewGuid().ToString("N")),
            };

            Exception lastError = null;
            foreach (string folder in folders)
            {
                try
                {
                    if (TryLoadFrom(folder, data))
                        return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Win32Exception)
                {
                    lastError = ex;
                }
            }
            throw new InvalidOperationException("Could not load the fan driver library.", lastError);
        }

        private static bool TryLoadFrom(string folder, byte[] data)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, DllName);

            if (!File.Exists(path) || !SameBytes(path, data))
            {
                File.Delete(path);
                File.WriteAllBytes(path, data);
            }

            // keep the file open without write/delete sharing while checking and loading it,
            // so nothing can swap it between the check and LoadLibrary
            using (var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!SameBytes(guard, data))
                    return false;

                if (LoadLibraryEx(path, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH) == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return true;
            }
        }

        private static bool SameBytes(string path, byte[] data)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return SameBytes(stream, data);
        }

        private static bool SameBytes(Stream stream, byte[] data)
        {
            if (stream.Length != data.Length)
                return false;
            var buffer = new byte[81920];
            int offset = 0, n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    if (buffer[i] != data[offset + i])
                        return false;
                }
                offset += n;
            }
            return offset == data.Length;
        }
    }
}
