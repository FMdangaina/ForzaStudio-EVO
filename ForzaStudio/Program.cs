namespace ForzaStudio
{
    using System;
    using System.Windows.Forms;

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ForzaTheme.UseAppIconForAllForms();
            TempCleanup.RunInBackground();
            // The version that last ran, in [App] of ForzaStudio.ini (release 0.0.1).
            if (AppIni.GetApp("Version", "") != Application.ProductVersion)
            {
                AppIni.SetApp("Version", Application.ProductVersion);
            }
            Application.Run(new ArchiveEditorForm());
            TempCleanup.Run();
        }
    }
}

