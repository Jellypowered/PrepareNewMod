using System;
using System.IO;
using System.Reflection;

namespace PrepareNewMod.Source
{
    internal static class AppPaths
    {
        // Physical folder where the executable resides.
        public static readonly string ExeDir = GetExeDir();

        private static string GetExeDir()
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                exe = Assembly.GetExecutingAssembly().Location;

            return Path.GetDirectoryName(exe!)!;
        }
    }
}
