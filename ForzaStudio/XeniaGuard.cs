using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ForzaStudio
{
    // Xenia and files in use, in one place (user 2026-11-14: "zipmanifest in use" was reported with the emulator closed;
    // Xenia open does not mean it runs this game, so ask).
    //  - Xenia open: the user is asked whether it is using the game or the packages being changed; "No" goes on (the
    //    lock check below still stops a write to a file Xenia really keeps open).
    //  - A file that cannot be opened for writing is either read-only (files copied from a DVD / ISO: the attribute is
    //    cleared, the file is written anyway) or kept open by a process, named through the Windows Restart Manager.
    internal static class XeniaGuard
    {
        // Every Xenia build: xenia, xenia_canary, xenia_edge, xenia_canary_netplay...
        public static string RunningProcess()
        {
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.ProcessName.StartsWith("xenia", StringComparison.OrdinalIgnoreCase))
                    {
                        return process.ProcessName;
                    }
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
            return null;
        }

        public static bool Running()
        {
            return RunningProcess() != null;
        }

        // True = go on. Xenia closed: true without asking. Xenia open: asks whether it is using what is about to be changed
        // (what = "the game's files", "the DLC packages"...); Yes (in use) = stop, with the advice to close it.
        public static bool Confirm(IWin32Window owner, string title, string what)
        {
            string process = RunningProcess();
            if (process == null)
            {
                return true;
            }
            DialogResult answer = MessageBox.Show(owner,
                "Xenia is open (" + process + ".exe).\n\nIs it running this game now, or using " + what + "?\n\n"
                + "Yes: stop here (close the game in Xenia, then try again).\nNo: go on (Xenia runs something else, or nothing).",
                title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
            return answer == DialogResult.No;
        }
    }

    internal static class FileLocks
    {
        // The files of the list that cannot be written, each with the reason ("zipmanifest.xml (in use by xenia_canary)").
        // A read-only file is made writable and not listed. name = how a file is shown (relative path...).
        public static List<string> Busy(IEnumerable<string> files, Func<string, string> name)
        {
            List<string> busy = new List<string>();
            foreach (string file in files)
            {
                if (!File.Exists(file))
                {
                    continue;
                }
                string why = WhyNotWritable(file);
                if (why != null)
                {
                    busy.Add(name(file) + " (" + why + ")");
                }
            }
            return busy;
        }

        // The message of the exception the writers throw: who keeps the files, and what to do.
        public static string Message(List<string> busy)
        {
            return "Files in use, nothing was written: " + string.Join(", ", busy.ToArray())
                + ". Close the program named, then try again.";
        }

        // null = writable (after clearing a read-only attribute); else why not.
        public static string WhyNotWritable(string file)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);   // copied from a DVD / ISO
                }
            }
            catch (Exception e)
            {
                if (!(e is IOException || e is UnauthorizedAccessException))
                {
                    throw;
                }
                return "read-only, and its attribute cannot be changed: " + e.Message;
            }
            try
            {
                using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return "no permission to write it (Windows file permissions)";
            }
            catch (IOException)
            {
                List<string> users = Users(file);
                if (users.Count == 0)
                {
                    return "in use by another program (antivirus, an editor, a backup tool...)";
                }
                string self = Process.GetCurrentProcess().ProcessName;
                List<string> named = new List<string>();
                foreach (string user in users)
                {
                    named.Add(string.Equals(user, self, StringComparison.OrdinalIgnoreCase) ? user + " = this program (another window has it open)" : user);
                }
                return "in use by " + string.Join(", ", named.ToArray());
            }
        }

        // Processes that keep the file open (Restart Manager, Windows Vista and later); empty when unknown.
        public static List<string> Users(string file)
        {
            List<string> names = new List<string>();
            uint session;
            string key = Guid.NewGuid().ToString();
            if (RmStartSession(out session, 0, key) != 0)
            {
                return names;
            }
            try
            {
                if (RmRegisterResources(session, 1, new string[] { file }, 0, null, 0, null) != 0)
                {
                    return names;
                }
                uint needed = 0, count = 0, reasons = 0;
                int result = RmGetList(session, out needed, ref count, null, ref reasons);
                if (result == 234 && needed > 0)   // ERROR_MORE_DATA
                {
                    RM_PROCESS_INFO[] info = new RM_PROCESS_INFO[needed];
                    count = needed;
                    if (RmGetList(session, out needed, ref count, info, ref reasons) == 0)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            string app = info[i].strAppName;
                            try
                            {
                                using (Process process = Process.GetProcessById(info[i].Process.dwProcessId))
                                {
                                    app = process.ProcessName;
                                }
                            }
                            catch (ArgumentException)
                            {
                            }
                            catch (InvalidOperationException)
                            {
                            }
                            if (!string.IsNullOrEmpty(app) && !names.Contains(app))
                            {
                                names.Add(app);
                            }
                        }
                    }
                }
            }
            finally
            {
                RmEndSession(session);
            }
            return names;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[] rgsFilenames, uint nApplications, RM_UNIQUE_PROCESS[] rgApplications, uint nServices, string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo, [In, Out] RM_PROCESS_INFO[] rgAffectedApps, ref uint lpdwRebootReasons);
    }
}
