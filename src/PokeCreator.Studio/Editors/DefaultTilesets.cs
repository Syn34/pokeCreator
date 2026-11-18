using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PokeCreator.Maps;
using PokeCreator.Projects;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Tileset folders every new project starts with. They live next to the Studio in
    /// BaseData/Tilesets/&lt;Folder&gt;/ (one image per area, optional Autotiles sub-folder, optional
    /// tileset.json with passability). BaseData/Tilesets/default.txt names the one new maps use.
    /// PokeCreator ships none (no Nintendo art); add your own, e.g. a FireRed folder.
    /// </summary>
    public static class DefaultTilesets
    {
        public static string Folder { get { return Path.Combine(BaseData.DefaultFolder, "Tilesets"); } }
        static string DefaultFile { get { return Path.Combine(Folder, "default.txt"); } }

        /// <summary>The default tileset folders, the one named in default.txt first.</summary>
        public static List<string> Available()
        {
            if (!Directory.Exists(Folder)) return new List<string>();
            string first = "";
            try { if (File.Exists(DefaultFile)) first = File.ReadAllText(DefaultFile).Trim(); } catch { }
            return Directory.GetDirectories(Folder)
                .Where(d => Directory.GetFiles(d).Any(f => new[] { ".png", ".bmp", ".gif", ".jpg" }.Contains(Path.GetExtension(f).ToLowerInvariant())))
                .OrderBy(d => string.Equals(Path.GetFileName(d), first, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Copies the default tileset folders into a project. The first one replaces the project's empty
        /// tileset 1, so new maps use it. Returns notes about converted images.
        /// </summary>
        public static List<string> InstallInto(Project p)
        {
            var notes = new List<string>();
            bool first = true;
            foreach (var dir in Available())
            {
                string name = Path.GetFileName(dir);
                var autotiles = TilesetImport.CopyFolder(p, dir, name, 0, notes);
                TilesetData ts = null;
                string json = Path.Combine(dir, "tileset.json");
                if (File.Exists(json))
                {
                    try { ts = Json.Json.FromFile<TilesetData>(json); } catch (Exception ex) { notes.Add(name + "/tileset.json: " + ex.Message); }
                }
                if (ts == null)
                {
                    ts = new TilesetData { Name = name };
                    for (int i = 0; i < Math.Min(7, autotiles.Count); i++) ts.AutotileNames[i] = autotiles[i];
                }
                ts.Folder = name;
                ts.SyncFolder(p.TilesetsDir);

                var empty = p.Tilesets.FirstOrDefault(t => t.Id == 1 && string.IsNullOrEmpty(t.TilesetName) && t.Sheets.Count == 0 && string.IsNullOrEmpty(t.Folder));
                if (first && empty != null) { ts.Id = 1; p.Tilesets[p.Tilesets.IndexOf(empty)] = ts; }
                else { ts.Id = p.Tilesets.Count == 0 ? 1 : p.Tilesets.Max(t => t.Id) + 1; p.Tilesets.Add(ts); }
                first = false;
            }
            if (!first) Json.Json.ToFile(p.DataFile("tilesets.json"), p.Tilesets);
            return notes;
        }

        /// <summary>Saves a project's tileset (images, autotiles and passability) as a default for new projects.</summary>
        public static string MakeDefault(Project p, TilesetData ts)
        {
            string name = MakeSafe(!string.IsNullOrEmpty(ts.Folder) ? ts.Folder : ts.Name);
            if (name.Length == 0) name = "Tileset " + ts.Id;
            string dest = Path.Combine(Folder, name);
            Directory.CreateDirectory(dest);
            var copy = (TilesetData)Json.Json.ConvertTo(Json.Json.Parse(Json.Json.Write(ts)), typeof(TilesetData));
            copy.Folder = name;
            // Sheets: copy each image into the default folder and point the copy at it
            foreach (var sh in ts.AllSheets())
            {
                string src = p.FindAsset("Graphics/Tilesets", sh.Value);
                if (src == null) continue;
                string file = Path.GetFileNameWithoutExtension(src);
                File.Copy(src, Path.Combine(dest, file + Path.GetExtension(src)), true);
                string newName = name + "/" + file;
                if (sh.Key == 0) copy.TilesetName = newName; else copy.Sheet(sh.Key).Name = newName;
            }
            // Autotiles
            string autoDest = Path.Combine(dest, "Autotiles");
            foreach (var a in ts.AutotileNames.Where(a => !string.IsNullOrEmpty(a)))
            {
                string src = p.FindAsset("Graphics/Autotiles", a);
                if (src == null) continue;
                Directory.CreateDirectory(autoDest);
                File.Copy(src, Path.Combine(autoDest, Path.GetFileName(src)), true);
            }
            copy.Id = 0;
            Json.Json.ToFile(Path.Combine(dest, "tileset.json"), copy);
            File.WriteAllText(DefaultFile, name);
            return dest;
        }

        static string MakeSafe(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            return new string((s ?? "").Where(c => !bad.Contains(c) && c != '/' && c != '\\').ToArray()).Trim();
        }
    }
}
