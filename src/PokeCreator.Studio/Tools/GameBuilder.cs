using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PokeCreator.Data;
using PokeCreator.Projects;

namespace PokeCreator.Studio.Tools
{
    /// <summary>
    /// Makes a folder that players can run: the game program and its libraries, plus the project's data,
    /// graphics, audio, fonts and scripts. No Studio, importer or editor files.
    /// </summary>
    public class GameBuilder
    {
        public string OutputParent;        // the folder the game folder goes in
        public bool NameExeAfterGame = true;
        public bool MakeZip = true;

        /// <summary>Project folders the game reads.</summary>
        public static readonly string[] ContentFolders = { "Data", "Graphics", "Audio", "Fonts", "Scripts" };
        /// <summary>Files next to the Studio that are for making games, not playing them.</summary>
        static readonly HashSet<string> EditorFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "PokeCreatorStudio.exe", "PokeCreatorStudio.exe.config", "PokeCreatorImport.exe", "PokeCreatorImport.exe.config", "PokeCreator.Import.dll", "errorlog.txt" };
        static readonly HashSet<string> JunkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Thumbs.db", "desktop.ini", ".DS_Store" };

        public string GameFolder { get; private set; }
        public string ZipPath { get; private set; }
        public string ExeName { get; private set; }
        public int FileCount { get; private set; }
        public long Bytes { get; private set; }

        /// <summary>A file and folder name from the game title ("Pokémon Ruby: Remix" -> "Pokémon Ruby Remix").</summary>
        public static string SafeName(string title)
        {
            var bad = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|', '/', '\\' }));
            var sb = new StringBuilder();
            foreach (char ch in title ?? "") sb.Append(bad.Contains(ch) ? ' ' : ch);
            string s = string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
            return s.Length == 0 ? "Game" : s;
        }

        /// <summary>Returns a reason the build can't go ahead, or null.</summary>
        public string Validate(Project p)
        {
            if (string.IsNullOrWhiteSpace(OutputParent)) return "Choose a folder to put the game in.";
            string parent = Path.GetFullPath(OutputParent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string root = Path.GetFullPath(p.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (parent.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "The game can't go inside the project folder itself (it would copy itself). Pick a folder outside it.";
            string target = Path.Combine(OutputParent, SafeName(p.Settings.Title ?? p.Info.Name));
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() && !LooksLikeBuild(target))
                return "The folder " + target + " already exists and isn't a game made by PokeCreator, so it won't be replaced. Pick another folder.";
            return null;
        }

        static bool LooksLikeBuild(string dir)
        {
            return File.Exists(Path.Combine(dir, Project.ProjectFileName)) && Directory.GetFiles(dir, "*.exe").Length > 0 && File.Exists(Path.Combine(dir, "PokeCreator.Core.dll"));
        }

        /// <param name="runtimeDir">The folder holding Game.exe (the Studio's folder).</param>
        /// <param name="progress">Called with (percent, what's happening).</param>
        public void Build(Project p, string runtimeDir, Action<int, string> progress)
        {
            string name = SafeName(p.Settings.Title ?? p.Info.Name);
            GameFolder = Path.Combine(OutputParent, name);
            ExeName = NameExeAfterGame ? name + ".exe" : "Game.exe";
            FileCount = 0; Bytes = 0;

            progress(0, "Preparing " + GameFolder);
            if (Directory.Exists(GameFolder)) Directory.Delete(GameFolder, true); // checked by Validate: only an earlier build
            Directory.CreateDirectory(GameFolder);

            // 1. The game program and its libraries
            string game = Path.Combine(runtimeDir, "Game.exe");
            if (!File.Exists(game)) throw new FileNotFoundException("Game.exe was not found next to the Studio.", game);
            Copy(game, Path.Combine(GameFolder, ExeName));
            string config = Path.Combine(runtimeDir, "Game.exe.config");
            if (File.Exists(config)) Copy(config, Path.Combine(GameFolder, ExeName + ".config")); // the config must match the program's name
            foreach (var f in Directory.GetFiles(runtimeDir, "*.dll"))
                if (!EditorFiles.Contains(Path.GetFileName(f))) Copy(f, Path.Combine(GameFolder, Path.GetFileName(f)));
            foreach (var f in Directory.GetFiles(runtimeDir, "*.so*").Concat(Directory.GetFiles(runtimeDir, "*.dylib")))
                Copy(f, Path.Combine(GameFolder, Path.GetFileName(f)));
            string licenses = Path.Combine(runtimeDir, "licenses");
            if (Directory.Exists(licenses)) CopyTree(licenses, Path.Combine(GameFolder, "licenses"), null);

            // 2. The project
            Copy(Path.Combine(p.Root, Project.ProjectFileName), Path.Combine(GameFolder, Project.ProjectFileName));
            var files = new List<string>();
            foreach (var folder in ContentFolders)
            {
                string src = Path.Combine(p.Root, folder);
                if (Directory.Exists(src)) files.AddRange(Directory.GetFiles(src, "*", SearchOption.AllDirectories).Where(f => !Skip(f)));
            }
            for (int i = 0; i < files.Count; i++)
            {
                string rel = files[i].Substring(p.Root.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                Copy(files[i], Path.Combine(GameFolder, rel));
                if (i % 25 == 0) progress(5 + (int)(80L * i / Math.Max(1, files.Count)), "Copying " + rel);
            }
            // The built game has no BaseData folder, so give it the font it uses: when the font comes from the shared
            // defaults, copy them into the game's Fonts folder and point its settings straight at that file.
            string root = Path.GetFullPath(p.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string msgFont = p.MessageFontFile();
            if (msgFont != null && !Path.GetFullPath(msgFont).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var f in Project.SharedFonts) Copy(f, Path.Combine(GameFolder, "Fonts", Path.GetFileName(f)));
                var built = Json.Json.Deserialize<GameSettings>(Json.Json.Write(p.Settings));
                built.UseProjectFont = true; built.Font = Path.GetFileName(msgFont);
                Json.Json.ToFile(Path.Combine(GameFolder, "Data", "settings.json"), built);
            }

            // Folders the game expects, even if empty
            foreach (var folder in ContentFolders) Directory.CreateDirectory(Path.Combine(GameFolder, folder));

            File.WriteAllText(Path.Combine(GameFolder, "How to play.txt"),
                (p.Settings.Title ?? p.Info.Name) + "\r\n\r\nTo play, run " + ExeName + ".\r\n" +
                "It needs Windows with the .NET Framework 4.8 (built into Windows 10 and 11).\r\n\r\n" +
                "Saves are kept in %APPDATA%\\PokeCreator\\Saves, in a folder named after the game, so replacing the game folder with a newer version keeps them.\r\n\r\nMade with PokeCreator. Third-party library licenses are in the licenses folder.\r\n", new UTF8Encoding(false));

            // 3. A zip to share
            ZipPath = null;
            if (MakeZip)
            {
                progress(88, "Making " + name + ".zip");
                ZipPath = Path.Combine(OutputParent, name + ".zip");
                if (File.Exists(ZipPath)) File.Delete(ZipPath);
                ZipFile.CreateFromDirectory(GameFolder, ZipPath, CompressionLevel.Optimal, true);
            }
            progress(100, "Done");
        }

        static bool Skip(string f)
        {
            string n = Path.GetFileName(f);
            if (JunkNames.Contains(n) || n.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || n.EndsWith("~")) return true;
            string sep = Path.DirectorySeparatorChar.ToString();
            return f.Contains(sep + ".git" + sep) || f.Contains(sep + ".vs" + sep);
        }

        void CopyTree(string src, string dst, Func<string, bool> skip)
        {
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                if (skip == null || !skip(f)) Copy(f, Path.Combine(dst, f.Substring(src.TrimEnd(Path.DirectorySeparatorChar).Length + 1)));
        }

        void Copy(string src, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
            FileCount++; Bytes += new FileInfo(dst).Length;
        }
    }
}
