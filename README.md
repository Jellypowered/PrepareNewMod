# 🛠️ PrepareNewMod - RimWorld Mod Template Generator

![App — Default](App%20Default.png)
A tiny Windows tool that clones your ModTemplate and rewires everything for a brand-new mod—names, project files, and About.xml—in one click. Visit the official template repository at [Rimworld Mod Template](https://github.com/Rimworld-Mods/Template)

## 🚀 What It Does

When you click **Dry Run**, the tool:

- Simulates the entire mod creation process without making any actual changes to your files.
- Shows you what would happen to your project files, including new folder names, updated project references, and the generated About.xml content.
- Allows you to preview the final output before committing any changes, ensuring you're confident about the mod's name, version, and description.
- Helps avoid accidental overwrites or misconfigurations by providing a safe, visual preview of the mod setup.
  ![App — Dry Run](App%20Screenshot%20Dry%20Run.png)

When you click **Copy & Apply**, the tool:
![App — Copy & Apply](App%20Screenshot%20Copy%20and%20Apply.png)

- Clones your template to a new folder (`<Destination base>\<New Mod Name>`)
- Skips `.vs`, `bin`, and `obj` folders
- Optionally copies `.git` if you check **Include .git**
- Renames the solution from `ModTemplate.sln` → `<New Mod Name>.sln`
- Updates the `.sln` project entry to point to:
  - `<New Mod Name>`
  - `.vscode\<New Mod Name>.csproj`
- Renames the project file: `.vscode\mod.csproj` → `.vscode\<New Mod Name>.csproj`
- Updates project metadata in the `.csproj`:
  - `<RootNamespace>` and `<AssemblyName>` → `<New Mod Name>`
- Updates `About/About.xml` (creates it if missing):
  - `<name>` → `<New Mod Name>`
  - `<packageId>` → `<author/org>.<new_mod_name>` (slugged)
  - `<author>` → prefix you provide
- Clears `About/PublishedFileId.txt` to prevent overwriting existing Workshop items
- Opens the destination folder when done (optional)
- Logs every step in a scrollable console at the bottom of the window (copy log to clipboard)

## ✅ Settings

🔧 **Template Path**
Set the root folder of your mod template (e.g., `C:\RimWorldMods\ModTemplate`).
_This path is saved to `settings.json` and reused on future runs._

📁 **Destination Base**
Choose the base folder where new mods will be created (e.g., `C:\RimWorldMods\MyMods`).
_Default: Same as Template Path if not specified._

✅ **Include .git**
Check this box to copy the `.git` folder into the new mod. Disabled by Default.
_Useful for version control during development._

🏷️ **Author Prefix**
Enter your author name or organization (e.g., `MyDevTeam`).
_Used in `packageId` and `author` fields in `About.xml`._

📌 **Auto-Open Destination**
Enable to automatically open the newly created folder after completion.
_Helps you quickly navigate to your new mod._

💾 **Settings Persistence**
After setting values, the app saves all configurations to `settings.json` in the same directory as the executable.
Next time you launch the app, it will load these settings automatically, no reconfiguration needed!

💡 Helpful Info:
Keep your template folder clean and well-organized. The app will skip the app, it's settings, and common build folders (`.vs`, `bin`, `obj`) to avoid clutter.

## 🔧 Usage

1. Place `PrepareNewMod.exe` in your ModTemplate root folder (where `ModTemplate.sln` lives)
2. Run the EXE
3. The app auto-detects its location and fills in the template root
4. Set your destination base and new mod name
5. Optionally, provide your package ID prefix (e.g., `jellypowered`)
6. Click **Dry Run** to preview changes, or **Copy & Apply** to perform them

> 💡 Tip: Names are sanitized to be valid Windows file names. The package ID is lowercased and slugged.

## 📂 Expected Template Layout

Your template should look roughly like this at the root:

```
ModTemplate/
├── ModTemplate.sln
├── .vscode/
│   └── mod.csproj
├── About/
│   ├── About.xml
│   └── PublishedFileId.txt (optional)
└── Source/
    └── ...
```

> The tool will still create `About/About.xml` if it's missing.

## ⚙️ Requirements

- **Windows 10/11 x64**
- No .NET installation required (self-contained app)

## 🏗️ Build from Source

The project targets `net8.0-windows` (WinForms). To publish a single-file EXE:

1. Open `PrepareNewMod.sln` in Visual Studio
2. Build the solution
3. The EXE will be generated directly in the `PrepareNewMod` folder
4. Copy it into your ModTemplate folder before running

## 📝 Notes & Details

- The app is DPI-aware and resizable
- The log area scrolls and timestamps each step
- The app detects the real EXE location (handles single-file extraction) and uses that as the default Template root
- Copy excludes: `The App and it's settings` `.vs/`, `bin/`, `obj/` (and `.git/` unless you check **Include .git**)

## 🚨 Troubleshooting

| Issue                          | Solution                                                                                                             |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------- |
| Buttons clipped / UI looks off | Ensure Windows display scaling is set correctly (the app is DPI-aware)                                               |
| Access denied errors           | Run from a folder with write permissions and ensure the destination isn't open in another program                    |
| No `ModTemplate.sln` found     | Make sure the EXE is in your mod template root or that the template root you choose contains exactly one `.sln` file |

## 📄 License

MIT License — Copyright (c) 2025 Jellypowered

## 🙌 Credits

Built for quickly spinning up RimWorld mods from a reusable template. Happy modding! 🛠️💙

[![GitHub](https://img.shields.io/badge/github-121212?style=for-the-badge&logo=github&logoColor=white)](https://github.com/jellypowered/preparenewmod)
[![Windows](https://img.shields.io/badge/windows-121212?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/jellypowered/preparenewmod)
