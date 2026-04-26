using System;
using System.Collections.Generic;
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

        private void BrowseExistingModRoot_Click(object sender, RoutedEventArgs e) =>
            BrowseFolderInto(txtExistingModRoot);

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
        private void BtnUpdateDryRun_Click(object sender, RoutedEventArgs e) => RunUpdateExisting(apply: false);
        private void BtnUpdateExisting_Click(object sender, RoutedEventArgs e) => RunUpdateExisting(apply: true);

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
                string namespaceOverrideRaw = txtNamespace.Text.Trim();
                string namespaceName = string.IsNullOrWhiteSpace(namespaceOverrideRaw)
                    ? BuildCSharpNamespace(modName, allowDots: false)
                    : BuildCSharpNamespace(namespaceOverrideRaw, allowDots: true);
                string pkgPrefixRaw = txtPkgPrefix.Text.Trim();

                if (string.IsNullOrWhiteSpace(modName))
                    throw new InvalidOperationException("Please enter a mod name.");

                if (string.IsNullOrWhiteSpace(namespaceName))
                    throw new InvalidOperationException("Mod name does not produce a valid C# namespace.");

                SaveSettings();

                string destRoot = Path.Combine(destBase, modName);
                FindSolution(templateRoot, out _);

                Log($"Template root : {templateRoot}");
                Log($"Destination   : {destRoot}");
                Log($"Mod name      : {modName}");
                Log($"Namespace     : {namespaceName}");
                if (!string.IsNullOrWhiteSpace(namespaceOverrideRaw))
                    Log($"Namespace src : custom override '{namespaceOverrideRaw}'");
                Log($"Pkg prefix    : {pkgPrefixRaw}");
                LogBlank();

                if (!apply)
                {
                    Log("- Would COPY template to destination (skipping .git/.vs/bin/obj by default)");
                    Log($"- Would RENAME solution: ModTemplate.sln -> {modName}.sln");
                    Log("- Would UPDATE .sln project entry (name/path)");
                    Log($"- Would RENAME .vscode\\mod.csproj -> .vscode\\{modName}.csproj");
                    Log("- Would EDIT RootNamespace/AssemblyName in .csproj");
                    string templateNamespace = ReadTemplateRootNamespace(templateRoot);
                    Log($"- Would REWRITE C# namespaces: {templateNamespace} -> {namespaceName}");
                    Log("- Would REWRITE C# namespace/usings in copied .cs files");
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
                string oldRootNamespace = string.Empty;
                foreach (XmlNode pg in xmlProj.SelectNodes("/Project/PropertyGroup")!)
                {
                    var rn = pg.SelectSingleNode("RootNamespace");
                    var an = pg.SelectSingleNode("AssemblyName");
                    if (string.IsNullOrWhiteSpace(oldRootNamespace) && rn != null && !string.IsNullOrWhiteSpace(rn.InnerText))
                        oldRootNamespace = rn.InnerText.Trim();

                    if (rn != null && rn.InnerText != namespaceName) { rn.InnerText = namespaceName; changedProj = true; }
                    if (an != null && an.InnerText != namespaceName) { an.InnerText = namespaceName; changedProj = true; }
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

                int rewrittenNamespaceFiles = RewriteTemplateNamespaces(destRoot, oldRootNamespace, namespaceName);
                if (rewrittenNamespaceFiles > 0)
                    Log($"Rewrote namespace/usings in {rewrittenNamespaceFiles} source file(s).");
                else
                    Log("No C# namespace rewrites were needed.");

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

        private void RunUpdateExisting(bool apply)
        {
            try
            {
                ClearLog();

                string templateRoot = RequireDir(txtTemplateRoot.Text, "Template root (source) is invalid.");
                string existingModRoot = RequireDir(txtExistingModRoot.Text, "Existing mod folder is invalid.");
                string modName = new DirectoryInfo(existingModRoot).Name;

                SaveSettings();

                Log("UPDATE EXISTING MOD MODE");
                Log($"Template root : {templateRoot}");
                Log($"Target mod    : {existingModRoot}");
                Log($"Mod name      : {modName}");
                LogBlank();

                var plan = BuildTemplateUpdatePlan(templateRoot, existingModRoot, includeGit: chkIncludeGit.IsChecked == true);
                int overwriteCount = 0;
                int createCount = 0;
                foreach (var item in plan)
                {
                    if (File.Exists(item.DestinationPath)) overwriteCount++;
                    else createCount++;
                }

                Log($"Planned updates: {plan.Count} file(s) ({createCount} new, {overwriteCount} overwrite)");
                Log("Preserved: About/, Source/, mod content folders, .sln, and .csproj identity files.");

                if (plan.Count == 0)
                {
                    Log("Nothing to update.");
                    return;
                }

                if (!apply)
                {
                    foreach (var item in plan)
                        Log($"- Would update: {item.RelativePath}");
                    LogBlank();
                    ValidateAndFixCsprojExcludeAssets(existingModRoot, applyChanges: false);
                    return;
                }

                var confirm = MessageBox.Show(
                    this,
                    "Apply template tooling updates to this existing mod?\r\n\r\n" +
                    $"Target: {existingModRoot}\r\n" +
                    $"Files: {plan.Count} ({createCount} new, {overwriteCount} overwrite)",
                    "Update Existing Mod",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                    throw new OperationCanceledException("Update cancelled by user.");

                foreach (var item in plan)
                {
                    string? destDir = Path.GetDirectoryName(item.DestinationPath);
                    if (!string.IsNullOrWhiteSpace(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(item.SourcePath, item.DestinationPath, overwrite: true);
                }

                // Validate and fix csproj ExcludeAssets settings
                ValidateAndFixCsprojExcludeAssets(existingModRoot, applyChanges: true);

                LogBlank();
                Log("DONE.");
                Log($"- Updated mod: {existingModRoot}");
                Log($"- Files synced: {plan.Count}");

                if (chkOpenWhenDone.IsChecked == true)
                {
                    try { Process.Start("explorer.exe", existingModRoot); } catch { }
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

        private void ValidateAndFixCsprojExcludeAssets(string modRoot, bool applyChanges = true)
        {
            try
            {
                // Find the mod's csproj file
                string vsCodeDir = Path.Combine(modRoot, ".vscode");
                if (!Directory.Exists(vsCodeDir))
                {
                    Log("- .vscode directory not found; skipping csproj validation.");
                    return;
                }

                string csprojPath = Path.Combine(vsCodeDir, "mod.csproj");
                if (!File.Exists(csprojPath))
                {
                    var candidates = Directory.GetFiles(vsCodeDir, "*.csproj", SearchOption.TopDirectoryOnly);
                    if (candidates.Length != 1)
                    {
                        Log("- Could not locate unique .csproj file; skipping validation.");
                        return;
                    }
                    csprojPath = candidates[0];
                }

                var xmlDoc = new XmlDocument { PreserveWhitespace = true };
                xmlDoc.Load(csprojPath);

                // Find ItemGroup with Lib.Harmony PackageReference
                XmlNode? libHarmonyRef = null;
                XmlNode? itemGroupParent = null;

                var itemGroups = xmlDoc.SelectNodes("/Project/ItemGroup");
                if (itemGroups != null)
                {
                    foreach (XmlNode itemGroup in itemGroups)
                    {
                        var pkgRefs = itemGroup.SelectNodes("PackageReference");
                        if (pkgRefs != null)
                        {
                            foreach (XmlNode pkgRef in pkgRefs)
                            {
                                var includeAttr = pkgRef.Attributes?["Include"];
                                if (includeAttr?.Value == "Lib.Harmony")
                                {
                                    libHarmonyRef = pkgRef;
                                    itemGroupParent = itemGroup;
                                    break;
                                }
                            }
                        }
                        if (libHarmonyRef != null) break;
                    }
                }

                if (libHarmonyRef == null)
                {
                    Log("- Lib.Harmony PackageReference not found in csproj.");
                    return;
                }

                bool needsSave = false;

                // Check for ExcludeAssets="runtime" attribute
                var excludeAssetsAttr = libHarmonyRef.Attributes?["ExcludeAssets"];
                if (excludeAssetsAttr == null || excludeAssetsAttr.Value != "runtime")
                {
                    if (excludeAssetsAttr == null)
                        excludeAssetsAttr = xmlDoc.CreateAttribute("ExcludeAssets");
                    excludeAssetsAttr.Value = "runtime";
                    libHarmonyRef.Attributes?.SetNamedItem(excludeAssetsAttr);
                    needsSave = true;
                    Log("  - Would add ExcludeAssets=\"runtime\" to Lib.Harmony PackageReference.");
                }

                // Check for PrivateAssets child element
                var privateAssetsNode = libHarmonyRef.SelectSingleNode("PrivateAssets");
                if (privateAssetsNode == null)
                {
                    privateAssetsNode = xmlDoc.CreateElement("PrivateAssets");
                    privateAssetsNode.InnerText = "all";
                    libHarmonyRef.AppendChild(privateAssetsNode);
                    needsSave = true;
                    Log("  - Would add <PrivateAssets>all</PrivateAssets> to Lib.Harmony PackageReference.");
                }
                else if (privateAssetsNode.InnerText != "all")
                {
                    privateAssetsNode.InnerText = "all";
                    needsSave = true;
                    Log("  - Would update PrivateAssets value to 'all'.");
                }

                if (needsSave)
                {
                    if (applyChanges)
                    {
                        using var writer = new XmlTextWriter(csprojPath, new UTF8Encoding(false)) { Formatting = Formatting.Indented };
                        xmlDoc.Save(writer);
                        Log("- Lib.Harmony ExcludeAssets settings validated and fixed.");
                    }
                    else
                    {
                        Log("- Lib.Harmony ExcludeAssets needs to be fixed.");
                    }
                }
                else
                {
                    Log("- Lib.Harmony ExcludeAssets settings already correct.");
                }
            }
            catch (Exception ex)
            {
                Log($"- Warning: Failed to validate csproj ExcludeAssets: {ex.Message}");
            }
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

        private sealed class TemplateUpdateItem
        {
            public string SourcePath { get; set; } = string.Empty;
            public string DestinationPath { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
        }

        private static List<TemplateUpdateItem> BuildTemplateUpdatePlan(string templateRoot, string targetRoot, bool includeGit)
        {
            var result = new List<TemplateUpdateItem>();
            string selfPath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            string selfBaseName = Path.GetFileNameWithoutExtension(selfPath);

            var stack = new Stack<(string SourceDir, string RelativeDir)>();
            stack.Push((templateRoot, string.Empty));

            while (stack.Count > 0)
            {
                var (sourceDir, relativeDir) = stack.Pop();

                foreach (string dir in Directory.GetDirectories(sourceDir))
                {
                    string dirName = Path.GetFileName(dir);
                    string rel = string.IsNullOrEmpty(relativeDir) ? dirName : relativeDir + "/" + dirName;
                    if (ShouldSkipUpdateDirectory(rel, includeGit)) continue;
                    stack.Push((dir, rel));
                }

                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    string fileName = Path.GetFileName(file);
                    string rel = string.IsNullOrEmpty(relativeDir) ? fileName : relativeDir + "/" + fileName;
                    if (!ShouldIncludeUpdateFile(rel, fileName, selfBaseName)) continue;

                    string dest = Path.Combine(targetRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    result.Add(new TemplateUpdateItem
                    {
                        SourcePath = file,
                        DestinationPath = dest,
                        RelativePath = rel
                    });
                }
            }

            return result;
        }

        private static bool ShouldSkipUpdateDirectory(string relativeDir, bool includeGit)
        {
            string[] segments = relativeDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return false;
            string firstSegment = segments[0];

            foreach (string segment in segments)
            {
                if (segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)) return true;
                if (segment.Equals("bin", StringComparison.OrdinalIgnoreCase)) return true;
                if (segment.Equals("obj", StringComparison.OrdinalIgnoreCase)) return true;
                if (segment.Equals("_dist", StringComparison.OrdinalIgnoreCase)) return true;
            }

            if (!includeGit && firstSegment.Equals(".git", StringComparison.OrdinalIgnoreCase)) return true;

            // Preserve mod identity/content folders on update.
            if (firstSegment.Equals("About", StringComparison.OrdinalIgnoreCase)) return true;
            if (firstSegment.Equals("Source", StringComparison.OrdinalIgnoreCase)) return true;
            if (firstSegment.Equals("Assemblies", StringComparison.OrdinalIgnoreCase)) return true;
            if (firstSegment.Equals("Defs", StringComparison.OrdinalIgnoreCase)) return true;
            if (firstSegment.Equals("Languages", StringComparison.OrdinalIgnoreCase)) return true;
            if (firstSegment.Equals("Patches", StringComparison.OrdinalIgnoreCase)) return true;
            if (firstSegment.Equals("Textures", StringComparison.OrdinalIgnoreCase)) return true;
            if (firstSegment.Equals("Sounds", StringComparison.OrdinalIgnoreCase)) return true;
            if (firstSegment.Equals("Fonts", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static bool ShouldIncludeUpdateFile(string relativePath, string fileName, string selfBaseName)
        {
            string rel = relativePath.Replace('\\', '/');
            string[] segments = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return false;
            string firstSegment = segments[0];
            string extension = Path.GetExtension(fileName);
            string fileNameNoExt = Path.GetFileNameWithoutExtension(fileName);

            // Preserve commonly customized root files for existing mods.
            if (segments.Length == 1 && fileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase)) return false;
            if (segments.Length == 1 && fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase)) return false;

            foreach (string segment in segments)
            {
                if (segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)) return false;
                if (segment.Equals("bin", StringComparison.OrdinalIgnoreCase)) return false;
                if (segment.Equals("obj", StringComparison.OrdinalIgnoreCase)) return false;
                if (segment.Equals("_dist", StringComparison.OrdinalIgnoreCase)) return false;
            }

            if (fileNameNoExt.Equals(selfBaseName, StringComparison.OrdinalIgnoreCase)) return false;
            if (fileName.Equals("settings.json", StringComparison.OrdinalIgnoreCase)) return false;

            // Keep existing mod/project identity untouched.
            if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)) return false;
            if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)) return false;
            if (extension.Equals(".user", StringComparison.OrdinalIgnoreCase)) return false;

            if (firstSegment.Equals("About", StringComparison.OrdinalIgnoreCase)) return false;
            if (firstSegment.Equals("Source", StringComparison.OrdinalIgnoreCase)) return false;

            // Keep .vscode project file naming as-is.
            if (firstSegment.Equals(".vscode", StringComparison.OrdinalIgnoreCase)
                && extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        private static string BuildCSharpNamespace(string name, bool allowDots)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            var sb = new StringBuilder(name.Length + 1);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
                else if (allowDots && c == '.')
                {
                    sb.Append('.');
                }
                else
                {
                    sb.Append('_');
                }
            }

            string ns = sb.ToString();
            if (allowDots)
            {
                ns = Regex.Replace(ns, "\\.+", ".").Trim('.');
                string[] parts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = Regex.Replace(parts[i], "_+", "_").Trim('_');
                    if (string.IsNullOrWhiteSpace(p)) return "";
                    if (!char.IsLetter(p[0]) && p[0] != '_') p = "_" + p;
                    parts[i] = p;
                }
                return string.Join('.', parts);
            }

            ns = Regex.Replace(ns, "_+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(ns)) return "";
            if (!char.IsLetter(ns[0]) && ns[0] != '_') ns = "_" + ns;
            return ns;
        }

        private static string ReadTemplateRootNamespace(string templateRoot)
        {
            string vsCodeDir = Path.Combine(templateRoot, ".vscode");
            if (!Directory.Exists(vsCodeDir)) return "Template";

            string csproj = Path.Combine(vsCodeDir, "mod.csproj");
            if (!File.Exists(csproj))
            {
                var candidates = Directory.GetFiles(vsCodeDir, "*.csproj", SearchOption.TopDirectoryOnly);
                if (candidates.Length == 1) csproj = candidates[0];
            }

            if (!File.Exists(csproj)) return "Template";

            try
            {
                var xmlProj = new XmlDocument();
                xmlProj.Load(csproj);
                var rn = xmlProj.SelectSingleNode("/Project/PropertyGroup/RootNamespace");
                if (rn != null && !string.IsNullOrWhiteSpace(rn.InnerText))
                    return rn.InnerText.Trim();
            }
            catch
            {
                // Return default token if parsing fails.
            }

            return "Template";
        }

        private static int RewriteTemplateNamespaces(string rootDir, string oldNamespace, string newNamespace)
        {
            if (string.IsNullOrWhiteSpace(newNamespace)) return 0;

            int changedFiles = 0;
            foreach (string file in EnumerateCSharpFiles(rootDir))
            {
                string original = File.ReadAllText(file, Encoding.UTF8);
                string updated = original.Replace("__MOD_NAMESPACE__", newNamespace, StringComparison.Ordinal);

                if (!string.IsNullOrWhiteSpace(oldNamespace) && !oldNamespace.Equals(newNamespace, StringComparison.Ordinal))
                {
                    string escapedOld = Regex.Escape(oldNamespace);
                    updated = Regex.Replace(
                        updated,
                        @"(?m)^(\s*namespace\s+)" + escapedOld + @"(\b(?:\.[A-Za-z_][A-Za-z0-9_]*)?)",
                        "$1" + newNamespace + "$2");

                    updated = Regex.Replace(
                        updated,
                        @"(?m)^(\s*using\s+)" + escapedOld + @"(\b(?:\.[A-Za-z_][A-Za-z0-9_]*)?\s*;)",
                        "$1" + newNamespace + "$2");
                }

                if (!string.Equals(original, updated, StringComparison.Ordinal))
                {
                    File.WriteAllText(file, updated, new UTF8Encoding(false));
                    changedFiles++;
                }
            }

            return changedFiles;
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateCSharpFiles(string rootDir)
        {
            var stack = new System.Collections.Generic.Stack<string>();
            stack.Push(rootDir);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();

                foreach (string sub in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(sub);
                    if (name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals(".vs", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
                    stack.Push(sub);
                }

                foreach (string cs in Directory.GetFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
                    yield return cs;
            }
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
            public string? ExistingModRoot { get; set; }
            public string? NamespaceOverride { get; set; }
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
                if (!string.IsNullOrWhiteSpace(s.ExistingModRoot)) txtExistingModRoot.Text = s.ExistingModRoot!;
                if (!string.IsNullOrWhiteSpace(s.NamespaceOverride)) txtNamespace.Text = s.NamespaceOverride!;
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
                    ExistingModRoot = txtExistingModRoot.Text,
                    NamespaceOverride = txtNamespace.Text,
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
