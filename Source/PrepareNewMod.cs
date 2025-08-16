using System;
using System.IO;
using System.Windows.Forms;

namespace PrepareNewMod.Source
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Ensure we start in the folder containing the EXE (repo root).
                Directory.SetCurrentDirectory(AppPaths.ExeDir);

                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Prepare New Mod - Fatal Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
