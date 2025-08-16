using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;

namespace PrepareNewMod.Source
{
    public class MainForm : Form
    {
        private TextBox txtTemplateRoot;
        private Button btnBrowseTemplateRoot;

        private TextBox txtDestBase;
        private Button btnBrowseDestBase;

        private TextBox txtModName;
        private TextBox txtPkgPrefix;

        private CheckBox chkIncludeGit;
        private CheckBox chkOpenWhenDone;

        private Button btnDryRun;
        private Button btnRun;
        private Button btnCopyLog;

        // Console-like log
        private TextBox logBox;

        public MainForm()
        {
            Text = "Prepare New Mod (from ModTemplate)";
            Width = 960;
            Height = 600;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(880, 520);
            StartPosition = FormStartPosition.CenterScreen;

            // --- Defaults based on EXE location ---
            var exeDir = AppPaths.ExeDir;
            var exeParent = Directory.GetParent(exeDir)?.FullName ?? exeDir;

            // Prefer ..\Source\ when EXE sits in ...\ModTemplate\
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

            // Layout constants
            int leftLabel = 12, leftBox = 220, widthBox = 660, row = 18, vstep = 36;

            // Template root
            var lblTemplateRoot = new Label { Left = leftLabel, Top = row, Width = 200, Text = "Template root (source):" };
            txtTemplateRoot = new TextBox
            {
                Left = leftBox,
                Top = row - 4,
                Width = widthBox,
                Text = exeDir,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            btnBrowseTemplateRoot = new Button
            {
                Left = leftBox + widthBox + 8,
                Top = row - 6,
                Width = 80,
                Text = "Browse",
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnBrowseTemplateRoot.Click += (_, __) => BrowseFolderInto(txtTemplateRoot);
            row += vstep;

            // Destination base
            var lblDestBase = new Label { Left = leftLabel, Top = row, Width = 200, Text = "Destination base folder:" };
            txtDestBase = new TextBox
            {
                Left = leftBox,
                Top = row - 4,
                Width = widthBox,
                Text = destDefault,
                PlaceholderText = @"e.g., F:\Source",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            btnBrowseDestBase = new Button
            {
                Left = leftBox + widthBox + 8,
                Top = row - 6,
                Width = 80,
                Text = "Browse",
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnBrowseDestBase.Click += (_, __) => BrowseFolderInto(txtDestBase);
            row += vstep;

            // Mod name
            var lblModName = new Label { Left = leftLabel, Top = row, Width = 200, Text = "New mod name:" };
            txtModName = new TextBox
            {
                Left = leftBox,
                Top = row - 4,
                Width = widthBox,
                PlaceholderText = "e.g., JellysAwesomeMod",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            row += vstep;

            // Package prefix (author/org)
            var lblPkgPrefix = new Label { Left = leftLabel, Top = row, Width = 200, Text = "Package ID prefix (author/org):" };
            txtPkgPrefix = new TextBox { Left = leftBox, Top = row - 4, Width = 320, PlaceholderText = "e.g., jellypowered" };
            txtPkgPrefix.Text = "jellypowered";
            row += vstep;

            // Options
            chkIncludeGit = new CheckBox { Left = leftBox, Top = row - 8, Width = 260, Text = "Include .git (copy Git history)", Checked = false };
            chkOpenWhenDone = new CheckBox { Left = leftBox + 260, Top = row - 8, Width = 260, Text = "Open destination when done", Checked = true };
            row += vstep;

            // Actions
            btnDryRun = new Button { Left = leftBox, Top = row - 8, Width = 120, Text = "Dry Run" };
            btnRun = new Button { Left = leftBox + 130, Top = row - 8, Width = 140, Text = "Copy && Apply" };
            btnCopyLog = new Button { Left = leftBox + 280, Top = row - 8, Width = 120, Text = "Copy Log" };
            btnDryRun.Click += (_, __) => Run(apply: false);
            btnRun.Click += (_, __) => Run(apply: true);
            btnCopyLog.Click += (_, __) => { try { Clipboard.SetText(logBox.Text); } catch { } };
            row += vstep;

            // Console-like log
            logBox = new TextBox
            {
                Left = leftLabel,
                Top = row - 8,
                Width = 910,
                Height = 360,
                ReadOnly = true,
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point)
            };

            Controls.AddRange(new Control[]
            {
                lblTemplateRoot, txtTemplateRoot, btnBrowseTemplateRoot,
                lblDestBase, txtDestBase, btnBrowseDestBase,
                lblModName, txtModName,
                lblPkgPrefix, txtPkgPrefix,
                chkIncludeGit, chkOpenWhenDone,
                btnDryRun, btnRun, btnCopyLog,
                logBox
            });
        }

        // --- Logging helpers ---
        private void Log(string message)
        {
            if (InvokeRequired) { Invoke(new Action<string>(Log), message); return; }
            logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }
        private void LogBlank() => Log(string.Empty);
        private void ClearLog() { if (InvokeRequired) { Invoke(new Action(ClearLog)); return; } logBox.Clear(); }

        private static void BrowseFolderInto(TextBox tb)
        {
            using var fbd = new FolderBrowserDialog();
            var fallback = AppPaths.ExeDir;
            fbd.SelectedPath = Directory.Exists(tb.Text) ? tb.Text : fallback;
            if (fbd.ShowDialog() == DialogResult.OK) tb.Text = fbd.SelectedPath;
        }

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

                string destRoot = Path.Combine(destBase, modName);
                string templateSln = FindSolution(templateRoot, out _);

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
                    var overwrite = MessageBox.Show(
                        this,
                        $"Destination already exists:\r\n{destRoot}\r\n\r\nOverwrite its contents?",
                        "Destination Exists",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (overwrite == DialogResult.No)
                        throw new OperationCanceledException("Operation cancelled by user.");

                    TryDeleteDirectoryContents(destRoot);
                    Log("Cleaned existing destination.");
                }
                else
                {
                    Directory.CreateDirectory(destRoot);
                }

                CopyDirectory(templateRoot, destRoot, includeGit: chkIncludeGit.Checked);
                Log("Copied template.");
                Application.DoEvents();

                // 2) Work inside the copy
                string slnPath = FindSolution(destRoot, out _);
                string newSlnPath = Path.Combine(destRoot, $"{modName}.sln");

                // Update .sln project entry
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
                            string newName = modName;
                            string newPath = @".vscode\" + modName + ".csproj";
                            return $@"Project(""{{
{typeGuid}}}"") = ""{newName}"", ""{newPath}"", ""{{{projGuid}}}""";
                        }
                        return m.Value;
                    },
                    RegexOptions.IgnoreCase);

                // Rename .sln if needed, then write updated content
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

                // Rename .csproj in .vscode
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

                // Update RootNamespace/AssemblyName
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

                // 3) Update About/About.xml (+ author, clear PublishedFileId.txt)
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
                    var root = doc.CreateElement("ModMetaData");
                    doc.AppendChild(root);

                    void Add(string name, string value)
                    {
                        var n = doc.CreateElement(name);
                        n.InnerText = value;
                        root.AppendChild(n);
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

                // Clear PublishedFileId.txt if present
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

                if (chkOpenWhenDone.Checked)
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
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Log("ERROR: " + ex);
            }
        }

        private static void UpdateAboutXml(string path, string modName, string pkgPrefixRaw)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(path);

            var root = doc.SelectSingleNode("/ModMetaData") ?? doc.DocumentElement;
            if (root == null || !string.Equals(root.Name, "ModMetaData", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("About.xml does not contain <ModMetaData> root.");

            // <name>
            var nameNode = root.SelectSingleNode("name") ?? doc.CreateElement("name");
            nameNode.InnerText = modName;
            if (nameNode.ParentNode == null) root.AppendChild(nameNode);

            // <packageId>
            string packageId = BuildPackageId(pkgPrefixRaw, modName);
            var pkgNode = root.SelectSingleNode("packageId") ?? doc.CreateElement("packageId");
            pkgNode.InnerText = packageId;
            if (pkgNode.ParentNode == null) root.AppendChild(pkgNode);

            // <author>
            var authorNode = root.SelectSingleNode("author") ?? doc.CreateElement("author");
            authorNode.InnerText = string.IsNullOrWhiteSpace(pkgPrefixRaw) ? "author" : pkgPrefixRaw;
            if (authorNode.ParentNode == null) root.AppendChild(authorNode);

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
            var s = Regex.Replace(sb.ToString(), "_{2,}", "_");
            return s.Trim('_');
        }

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
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var name = Path.GetFileName(file);
                var destFile = Path.Combine(destDir, name);
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var name = Path.GetFileName(dir);

                if (!includeGit && name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals(".vs", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;

                var destSub = Path.Combine(destDir, name);
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
                    DirectoryInfo di = new DirectoryInfo(d) { Attributes = FileAttributes.Normal };
                    di.Delete(true);
                }
                catch { }
            }
        }
    }
}
