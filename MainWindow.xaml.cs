using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml;
using Microsoft.Win32;

namespace PrepareNewMod.Source
{
    public partial class MainWindow : Window
    {
        private readonly string settingsPath = Path.Combine(AppPaths.ExeDir, "settings.json");

        public MainWindow()
        {
            InitializeComponent();

            var exeDir = AppPaths.ExeDir;
            var exeParent = Directory.GetParent(exeDir)?.FullName ?? exeDir;

            string destDefault = exeParent;
            try
            {
                var hereName = new DirectoryInfo(exeDir).Name;
                var parent = Directory.GetParent(exeDir)?.FullName ?? exeParent;
                if (hereName.Equals("ModTemplate", StringComparison.OrdinalIgnoreCase))
                {
                    var sourceCandidate = Path.Combine(parent, "Source");
                    destDefault = Directory.Exists(sourceCandidate) ? sourceCandidate : parent;
                }
            }
            catch { destDefault = exeParent; }

            txtTemplateRoot.Text = exeDir;
            txtDestBase.Text = destDefault;
            txtPkgPrefix.Text = "jellypowered";

            LoadSettings();
        }

        // --- Logging ---

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                logBox.ScrollToEnd();
            });
        }

        private void LogBlank() => Log(string.Empty);

        private void ClearLog() => Dispatcher.Invoke(() => logBox.Clear());

        // --- Browse buttons ---

        private void BrowseTemplateRoot_Click(object sender, RoutedEventArgs e) =>
            BrowseFolderInto(txtTemplateRoot);

        private void BrowseDestBase_Click(object sender, RoutedEventArgs e) =>
            BrowseFolderInto(txtDestBase);

        private void BrowseFolderInto(System.Windows.Controls.TextBox tb)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select folder",
                InitialDirectory = Directory.Exists(tb.Text) ? tb.Text : AppPaths.ExeDir
            };
            if (dialog.ShowDialog(this) == true)
                tb.Text = dialog.FolderName;
        }

        // --- Action buttons ---

        private void BtnDryRun_Click(object sender, RoutedEventArgs e) => Run(apply: false);
        private void BtnRun_Click(object sender, RoutedEventArgs e) => Run(apply: true);

        private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(logBox.Text); } catch { }
        }

        // --- Core logic ---

        private void Run(bool apply)
        {
            try
            {
                ClearLog();

                string templateRoot = RequireDir(txtTemplateRoot.Text, "Template root (source) is invalid.");
                string destBase = RequireDir(txtDestBase.Text, "Destination base folder is invalid.");
                string modName = SanitizeFileName(txtModName.Text.Trim());
                string pkgPrefixRaw = txtPkgPrefix.Text.Trim();

                if (string.IsNullOrWhiteSpace(modName))
                    throw new InvalidOperationException("Please enter a mod name.");

                SaveSettings();

                string destRoot = Path.Combine(destBase, modName);
                FindSolution(templateRoot, out _);

                Log($"Template root : {templateRoot}");
                Log($"Destination   : {destRoot}");
                Log($"Mod name      : {modName}");
                Log($"Pkg prefix    : {pkgPrefixRaw}");
                LogBlank();

                if (!apply)
                {
                    Log("- Would COPY template to destination (skipping .git/.vs/bin/obj by default)");
                    Log($"- Would RENAME solution: ModTemplate.sln -> {modName}.sln");
                    Log("- Would UPDATE .sln project entry (name/path)");
                    Log($"- Would RENAME .vscode\\mod.csproj -> .vscode\\{modName}.csproj");
                    Log("- Would EDIT RootNamespace/AssemblyName in .csproj");
                    Log("- Would UPDATE About/About.xml <name>, <packageId>, <author>, and clear PublishedFileId.txt");
                    return;
                }

                // 1) Copy template
                if (Directory.Exists(destRoot))
                {
                    var result = MessageBox.Show(
                        this,
                        $"Destination already exists:\r\n{destRoot}\r\n\r\nOverwrite its contents?",
                        "Destination Exists",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                        throw new OperationCanceledException("Operation cancelled by user.");

                    TryDeleteDirectoryContents(destRoot);
                    Log("Cleaned existing destination.");
                }
                else
                {
                    Directory.CreateDirectory(destRoot);
                }

                CopyDirectory(templateRoot, destRoot, includeGit: chkIncludeGit.IsChecked == true);
                Log("Copied template.");

                // 2) Work inside the copy
                string slnPath = FindSolution(destRoot, out _);
                string newSlnPath = Path.Combine(destRoot, $"{modName}.sln");

                string slnText = File.ReadAllText(slnPath, Encoding.UTF8);
                string pattern = @"Project\(""\{([A-F0-9\-]{36})\}""\)\s=\s""([^""]+)"",\s""([^""]+)"",\s""\{([A-F0-9\-]{36})\}""";

                string replaced = Regex.Replace(
                    slnText,
                    pattern,
                    m =>
                    {
                        string projPath = m.Groups[3].Value;
                        if (projPath.Replace('/', '\\').StartsWith(@".vscode\", StringComparison.OrdinalIgnoreCase)
                            && projPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                        {
                            string typeGuid = m.Groups[1].Value;
                            string projGuid = m.Groups[4].Value;
                            string newPath = @".vscode\" + modName + ".csproj";
                            return $@"Project(""{{{typeGuid}}}"") = ""{modName}"", ""{newPath}"", ""{{{projGuid}}}""";
                        }
                        return m.Value;
                    },
                    RegexOptions.IgnoreCase);

                if (!slnPath.Equals(newSlnPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(newSlnPath))
                        throw new IOException($"Target solution already exists: {newSlnPath}");
                    File.Move(slnPath, newSlnPath);
                    slnPath = newSlnPath;
                    Log($"Renamed solution -> {Path.GetFileName(slnPath)}");
                }
                File.WriteAllText(slnPath, replaced, new UTF8Encoding(false));
                Log("Updated solution project entry.");

                string vsCodeDir = Path.Combine(destRoot, ".vscode");
                if (!Directory.Exists(vsCodeDir))
                    throw new InvalidOperationException("Expected '.vscode' directory not found in the destination.");

                string csprojOld = Path.Combine(vsCodeDir, "mod.csproj");
                if (!File.Exists(csprojOld))
                {
                    var candidates = Directory.GetFiles(vsCodeDir, "*.csproj");
                    if (candidates.Length == 1) csprojOld = candidates[0];
                }
                if (!File.Exists(csprojOld))
                    throw new InvalidOperationException("Could not find '.vscode\\mod.csproj' (or a single .csproj) in the destination.");

                string csprojNew = Path.Combine(vsCodeDir, modName + ".csproj");
                if (!csprojOld.Equals(csprojNew, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(csprojNew))
                        throw new IOException($"Target csproj already exists: {csprojNew}");
                    File.Move(csprojOld, csprojNew);
                    Log($"Renamed csproj -> {Path.GetFileName(csprojNew)}");
                }

                var xmlProj = new XmlDocument { PreserveWhitespace = true };
                xmlProj.Load(csprojNew);

                bool changedProj = false;
                foreach (XmlNode pg in xmlProj.SelectNodes("/Project/PropertyGroup")!)
                {
                    var rn = pg.SelectSingleNode("RootNamespace");
                    var an = pg.SelectSingleNode("AssemblyName");
                    if (rn != null && rn.InnerText != modName) { rn.InnerText = modName; changedProj = true; }
                    if (an != null && an.InnerText != modName) { an.InnerText = modName; changedProj = true; }
                }
                if (changedProj)
                {
                    using var writer = new XmlTextWriter(csprojNew, new UTF8Encoding(false)) { Formatting = Formatting.None };
                    xmlProj.Save(writer);
                    Log("Updated RootNamespace/AssemblyName.");
                }
                else
                {
                    Log("RootNamespace/AssemblyName already correct.");
                }

                // 3) Update About/About.xml
                string aboutDir = Path.Combine(destRoot, "About");
                string aboutXml = Path.Combine(aboutDir, "About.xml");
                if (File.Exists(aboutXml))
                {
                    UpdateAboutXml(aboutXml, modName, pkgPrefixRaw);
                    Log("Patched About/About.xml (name/packageId/author).");
                }
                else
                {
                    Directory.CreateDirectory(aboutDir);
                    var doc = new XmlDocument();
                    var decl = doc.CreateXmlDeclaration("1.0", "utf-8", null);
                    doc.AppendChild(decl);
                    var xmlRoot = doc.CreateElement("ModMetaData");
                    doc.AppendChild(xmlRoot);

                    void Add(string name, string value)
                    {
                        var n = doc.CreateElement(name);
                        n.InnerText = value;
                        xmlRoot.AppendChild(n);
                    }

                    Add("name", modName);
                    Add("packageId", BuildPackageId(pkgPrefixRaw, modName));
                    Add("author", string.IsNullOrWhiteSpace(pkgPrefixRaw) ? "author" : pkgPrefixRaw);
                    Add("description", "TODO: mod description");
                    doc.Save(aboutXml);
                    Log("Created About/About.xml.");

                    var pubIdPathNew = Path.Combine(aboutDir, "PublishedFileId.txt");
                    try { File.WriteAllText(pubIdPathNew, string.Empty, new UTF8Encoding(false)); } catch { }
                }

                var pubIdPath = Path.Combine(aboutDir, "PublishedFileId.txt");
                if (File.Exists(pubIdPath))
                {
                    try { File.WriteAllText(pubIdPath, string.Empty, new UTF8Encoding(false)); Log("Cleared PublishedFileId.txt."); } catch { }
                }

                LogBlank();
                Log("DONE.");
                Log($"- Copied to: {destRoot}");
                Log($"- Solution : {Path.GetFileName(slnPath)}");
                Log($"- Project  : {Path.GetFileName(Path.Combine(vsCodeDir, modName + ".csproj"))}");

                if (chkOpenWhenDone.IsChecked == true)
                {
                    try { Process.Start("explorer.exe", destRoot); } catch { }
                }
            }
            catch (OperationCanceledException)
            {
                Log("Cancelled.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Prepare New Mod - Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Log("ERROR: " + ex);
            }
        }

        // --- XML helpers ---

        private static void UpdateAboutXml(string path, string modName, string pkgPrefixRaw)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(path);

            var xmlRoot = doc.SelectSingleNode("/ModMetaData") ?? doc.DocumentElement;
            if (xmlRoot == null || !string.Equals(xmlRoot.Name, "ModMetaData", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("About.xml does not contain <ModMetaData> root.");

            var nameNode = xmlRoot.SelectSingleNode("name") ?? doc.CreateElement("name");
            nameNode.InnerText = modName;
            if (nameNode.ParentNode == null) xmlRoot.AppendChild(nameNode);

            string packageId = BuildPackageId(pkgPrefixRaw, modName);
            var pkgNode = xmlRoot.SelectSingleNode("packageId") ?? doc.CreateElement("packageId");
            pkgNode.InnerText = packageId;
            if (pkgNode.ParentNode == null) xmlRoot.AppendChild(pkgNode);

            var authorNode = xmlRoot.SelectSingleNode("author") ?? doc.CreateElement("author");
            authorNode.InnerText = string.IsNullOrWhiteSpace(pkgPrefixRaw) ? "author" : pkgPrefixRaw;
            if (authorNode.ParentNode == null) xmlRoot.AppendChild(authorNode);

            using var writer = new XmlTextWriter(path, new UTF8Encoding(false)) { Formatting = Formatting.Indented };
            doc.Save(writer);
        }

        private static string BuildPackageId(string prefixRaw, string modName)
        {
            string prefix = Slug(prefixRaw, allowDot: false);
            if (string.IsNullOrEmpty(prefix)) prefix = "author";

            string modPart = Slug(modName, allowDot: false);
            if (string.IsNullOrEmpty(modPart)) modPart = "newmod";

            return $"{prefix}.{modPart}".ToLowerInvariant();
        }

        private static string Slug(string input, bool allowDot)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            var sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); continue; }
                if (c == '_' || c == '-') { sb.Append('_'); continue; }
                if (allowDot && c == '.') { sb.Append('.'); continue; }
            }
#pragma warning disable
            var s = Regex.Replace(sb.ToString(), "_{2,}", "_");
#pragma warning restore
            return s.Trim('_');
        }

        // --- File system helpers ---

        private static string RequireDir(string path, string errorIfInvalid)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                throw new InvalidOperationException(errorIfInvalid);
            return Path.GetFullPath(path);
        }

        private static string FindSolution(string root, out string originalName)
        {
            string preferred = Path.Combine(root, "ModTemplate.sln");
            if (File.Exists(preferred)) { originalName = "ModTemplate.sln"; return preferred; }

            var slns = Directory.GetFiles(root, "*.sln", SearchOption.TopDirectoryOnly);
            if (slns.Length == 1) { originalName = Path.GetFileName(slns[0]); return slns[0]; }

            throw new InvalidOperationException(
                "Could not find a solution file in the target copy (expected 'ModTemplate.sln' or exactly one '*.sln').");
        }

        private static void CopyDirectory(string sourceDir, string destDir, bool includeGit)
        {
            string selfPath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            string selfBaseName = Path.GetFileNameWithoutExtension(selfPath);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string name = Path.GetFileName(file);
                string nameNoExt = Path.GetFileNameWithoutExtension(file);

                if (nameNoExt.Equals(selfBaseName, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("settings.json", StringComparison.OrdinalIgnoreCase)) continue;

                File.Copy(file, Path.Combine(destDir, name), overwrite: true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string name = Path.GetFileName(dir);

                if (!includeGit && name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals(".vs", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("_dist", StringComparison.OrdinalIgnoreCase)) continue;

                string destSub = Path.Combine(destDir, name);
                Directory.CreateDirectory(destSub);
                CopyDirectory(dir, destSub, includeGit);
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        private static void TryDeleteDirectoryContents(string dir)
        {
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { }
            }
            foreach (var d in Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    DirectoryInfo di = new(d) { Attributes = FileAttributes.Normal };
                    di.Delete(true);
                }
                catch { }
            }
        }

        // --- Settings ---

        private sealed class UiSettings
        {
            public string? TemplateRoot { get; set; }
            public string? DestBase { get; set; }
            public string? PkgPrefix { get; set; }
            public bool IncludeGit { get; set; }
            public bool OpenWhenDone { get; set; }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath)) return;
                var json = File.ReadAllText(settingsPath, Encoding.UTF8);
                var s = System.Text.Json.JsonSerializer.Deserialize<UiSettings>(json);
                if (s is null) return;

                if (!string.IsNullOrWhiteSpace(s.TemplateRoot)) txtTemplateRoot.Text = s.TemplateRoot!;
                if (!string.IsNullOrWhiteSpace(s.DestBase)) txtDestBase.Text = s.DestBase!;
                if (!string.IsNullOrWhiteSpace(s.PkgPrefix)) txtPkgPrefix.Text = s.PkgPrefix!;
                chkIncludeGit.IsChecked = s.IncludeGit;
                chkOpenWhenDone.IsChecked = s.OpenWhenDone;
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                var s = new UiSettings
                {
                    TemplateRoot = txtTemplateRoot.Text,
                    DestBase = txtDestBase.Text,
                    PkgPrefix = txtPkgPrefix.Text,
                    IncludeGit = chkIncludeGit.IsChecked == true,
                    OpenWhenDone = chkOpenWhenDone.IsChecked == true
                };
#pragma warning disable
                var json = System.Text.Json.JsonSerializer.Serialize(s, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
#pragma warning restore
                File.WriteAllText(settingsPath, json, new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
