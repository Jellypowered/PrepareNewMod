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
        private readonly TextBox txtTemplateRoot;
        private readonly Button btnBrowseTemplateRoot;

        private readonly TextBox txtDestBase;
        private readonly Button btnBrowseDestBase;

        private readonly TextBox txtModName;
        private readonly TextBox txtPkgPrefix;

        private readonly CheckBox chkIncludeGit;
        private readonly CheckBox chkOpenWhenDone;

        private readonly Button btnDryRun;
        private readonly Button btnRun;
        private readonly Button btnCopyLog;

        // Console-like log
        private readonly TextBox logBox;
        private readonly string settingsPath = Path.Combine(AppPaths.ExeDir, "settings.json");


        public MainForm()
        {
            Text = "Prepare New Mod (from ModTemplate)";
            StartPosition = FormStartPosition.CenterScreen;

            // DPI-friendly
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            MinimumSize = new Size(880, 540);
            Size = new Size(980, 600);
            Padding = new Padding(12);

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

            // ===== Root layout: 2 rows (controls / log) =====
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Controls.Add(root);

            // ===== Top grid: labels, fields, browse buttons =====
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                Padding = new Padding(0, 0, 0, 6)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200f));   // labels
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));    // text boxes stretch
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90f));    // browse button
            root.Controls.Add(grid, 0, 0);

            // helpers
            static Label L(string t) => new()
            {
                Text = t,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 6)
            };
            static TextBox T(string text = "", string ph = "")
            {
                var tb = new TextBox
                {
                    Text = text,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Margin = new Padding(0, 3, 8, 3)
                };
                try { tb.PlaceholderText = ph; } catch { /* older WinForms? ignore */ }
                return tb;
            }
            static Button B(string t) => new()
            {
                Text = t,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 3, 0, 3),
                AutoSize = true
            };

            // Row 0: Template root
            grid.Controls.Add(L("Template root (source):"), 0, 0);
            txtTemplateRoot = T(exeDir);
            grid.Controls.Add(txtTemplateRoot, 1, 0);
            btnBrowseTemplateRoot = B("Browse");
            btnBrowseTemplateRoot.Click += (_, __) => BrowseFolderInto(txtTemplateRoot);
            grid.Controls.Add(btnBrowseTemplateRoot, 2, 0);

            // Row 1: Destination base
            grid.Controls.Add(L("Destination base folder:"), 0, 1);
            txtDestBase = T(destDefault, @"e.g., F:\Source");
            grid.Controls.Add(txtDestBase, 1, 1);
            btnBrowseDestBase = B("Browse");
            btnBrowseDestBase.Click += (_, __) => BrowseFolderInto(txtDestBase);
            grid.Controls.Add(btnBrowseDestBase, 2, 1);

            // Row 2: Mod name
            grid.Controls.Add(L("New mod name:"), 0, 2);
            txtModName = T("", "e.g., JellysAwesomeMod");
            grid.Controls.Add(txtModName, 1, 2);
            grid.Controls.Add(new Panel { Width = 1 }, 2, 2); // spacer

            // Row 3: Package prefix
            grid.Controls.Add(L("Package ID prefix (author/org):"), 0, 3);
            txtPkgPrefix = T("jellypowered", "e.g., jellypowered");
            grid.Controls.Add(txtPkgPrefix, 1, 3);
            grid.Controls.Add(new Panel { Width = 1 }, 2, 3);

            // Row 4: options (span all columns)
            var opts = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(200, 4, 0, 0) // align under inputs
            };
            chkIncludeGit = new CheckBox { Text = "Include .git (copy Git history)", AutoSize = true, Margin = new Padding(0, 0, 24, 0) };
            chkOpenWhenDone = new CheckBox { Text = "Open destination when done", AutoSize = true, Checked = true };
            opts.Controls.Add(chkIncludeGit);
            opts.Controls.Add(chkOpenWhenDone);
            grid.Controls.Add(opts, 0, 4);
            grid.SetColumnSpan(opts, 3);

            // Row 5: action buttons (right-aligned)
            var actions = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(200, 6, 0, 6)
            };
            btnCopyLog = new Button { Text = "Copy Log", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
            btnRun = new Button { Text = "Copy && Apply", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
            btnDryRun = new Button { Text = "Dry Run", AutoSize = true };
            btnCopyLog.Click += (_, __) => { try { Clipboard.SetText(logBox.Text); } catch { } };
            btnRun.Click += (_, __) => Run(apply: true);
            btnDryRun.Click += (_, __) => Run(apply: false);
            actions.Controls.AddRange([btnCopyLog, btnRun, btnDryRun]);
            grid.Controls.Add(actions, 0, 5);
            grid.SetColumnSpan(actions, 3);

            // ===== Log area (fills remaining space) =====
            logBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point)
            };
            root.Controls.Add(logBox, 0, 1);
            
            LoadSettings();
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

                SaveSettings(); // persist current UI values

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
                            string typeGuid = m.Groups[1].Value; // 36 chars, no braces
                            string projGuid = m.Groups[4].Value; // 36 chars, no braces
                            string newName = modName;
                            string newPath = @".vscode\" + modName + ".csproj";
                            // Write both GUIDs with braces:
                            return $@"Project(""{{{typeGuid}}}"") = ""{newName}"", ""{newPath}"", ""{{{projGuid}}}""";
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
#pragma warning disable
            var s = Regex.Replace(sb.ToString(), "_{2,}", "_");
#pragma warning restore
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
                if (name.Equals("PrepareNewMod.exe", StringComparison.OrdinalIgnoreCase)) continue; // Don't copy the Exe. 

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
                chkIncludeGit.Checked = s.IncludeGit;
                chkOpenWhenDone.Checked = s.OpenWhenDone;
            }
            catch { /* ignore */ }
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
                    IncludeGit = chkIncludeGit.Checked,
                    OpenWhenDone = chkOpenWhenDone.Checked
                };
#pragma warning disable
                var json = System.Text.Json.JsonSerializer.Serialize(s, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
#pragma warning restore
                File.WriteAllText(settingsPath, json, new UTF8Encoding(false));
            }
            catch { /* ignore */ }
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
    }
}
