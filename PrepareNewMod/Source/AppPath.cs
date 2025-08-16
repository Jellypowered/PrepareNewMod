using System;
using System.IO;
using System.Windows.Forms;

namespace PrepareNewMod.Source
{
    internal static class AppPaths
    {
        // Physical folder where the published EXE resides
        public static readonly string ExeDir = GetExeDir();

        private static string GetExeDir()
        {
            // Environment.ProcessPath works in .NET 6+ (single-file safe)
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                exe = Application.ExecutablePath; // fallback

            return Path.GetDirectoryName(exe!)!;
        }
    }
}
