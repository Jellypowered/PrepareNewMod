using System;
using System.IO;
using System.Windows;

namespace PrepareNewMod.Source
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try { Directory.SetCurrentDirectory(AppPaths.ExeDir); } catch { }
        }
    }
}

