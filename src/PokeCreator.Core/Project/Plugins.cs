using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PokeCreator.Projects
{
    /// <summary>plugin.json: what a plugin is and whether it's switched on in this project.</summary>
    public class PluginInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        /// <summary>Other plugins this one needs (they're loaded first).</summary>
        public List<string> Requires { get; set; }
        public bool Disabled { get; set; }
        [Json.JsonIgnore] public string Folder { get; set; }
        public PluginInfo() { Requires = new List<string>(); Version = "1.0"; }
        public override string ToString() { return Name + " " + Version + (Disabled ? " (off)" : ""); }
    }

    /// <summary>
    /// Plugins: folders in the project's Plugins folder that can be shared between projects. Each has a plugin.json and
    /// any of: Scripts/*.cs (compiled with the project's scripts: [ScriptCommand] methods and [ScriptInit] setup),
    /// Graphics / Audio / Fonts (used when the project doesn't have a file of that name), and Data/&lt;table&gt;.json
    /// records (items, moves, species... added to the game's database, or replacing entries with the same ID).
    /// </summary>
    public static class Plugins
    {
        public static string Folder(Project p) { return Path.Combine(p.Root, "Plugins"); }

        public static List<PluginInfo> All(Project p)
        {
            var list = new List<PluginInfo>();
            var root = Folder(p);
            if (!Directory.Exists(root)) return list;
            foreach (var dir in Directory.GetDirectories(root).OrderBy(d => d))
            {
                PluginInfo info = null;
                var meta = Path.Combine(dir, "plugin.json");
                try { if (File.Exists(meta)) info = Json.Json.FromFile<PluginInfo>(meta); } catch { }
                if (info == null) info = new PluginInfo();
                if (string.IsNullOrEmpty(info.Name)) info.Name = Path.GetFileName(dir);
                info.Folder = dir;
                list.Add(info);
            }
            return list;
        }

        public static void Save(PluginInfo info)
        {
            if (string.IsNullOrEmpty(info.Folder)) return;
            Directory.CreateDirectory(info.Folder);
            Json.Json.ToFile(Path.Combine(info.Folder, "plugin.json"), info);
        }

        /// <summary>The switched-on plugins whose requirements are met, each after the plugins it needs.</summary>
        public static List<PluginInfo> Enabled(Project p)
        {
            var on = All(p).Where(x => !x.Disabled).ToList();
            var byName = on.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
            var ordered = new List<PluginInfo>();
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Func<PluginInfo, bool> visit = null;
            visit = x =>
            {
                if (ordered.Contains(x)) return true;
                if (!visiting.Add(x.Name)) return false; // a loop in the requirements
                foreach (var r in x.Requires ?? new List<string>())
                {
                    PluginInfo dep;
                    if (!byName.TryGetValue(r, out dep) || !visit(dep)) { Console.WriteLine("[plugins] " + x.Name + " needs " + r + ", which isn't installed or is switched off"); return false; }
                }
                ordered.Add(x);
                return true;
            };
            foreach (var x in on) visit(x);
            return ordered;
        }

        /// <summary>Missing requirements, for the Studio ("Name needs X").</summary>
        public static List<string> Problems(Project p)
        {
            var all = All(p);
            var result = new List<string>();
            foreach (var x in all.Where(a => !a.Disabled))
                foreach (var r in x.Requires ?? new List<string>())
                    if (!all.Any(a => !a.Disabled && string.Equals(a.Name, r, StringComparison.OrdinalIgnoreCase))) result.Add(x.Name + " needs the plugin \"" + r + "\"");
            return result;
        }

        /// <summary>Script files of the switched-on plugins.</summary>
        public static List<string> ScriptFiles(Project p)
        {
            var files = new List<string>();
            foreach (var x in Enabled(p))
            {
                var dir = Path.Combine(x.Folder, "Scripts");
                if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f));
            }
            return files;
        }

        /// <summary>Makes the plugins' graphics and sounds findable (after the project's own).</summary>
        public static void UseAssets(Project p)
        {
            p.ExtraAssetRoots.Clear();
            foreach (var x in Enabled(p)) p.ExtraAssetRoots.Add(x.Folder);
        }

        /// <summary>In the game: adds the plugins' database records (Data/items.json ...). Returns how many.</summary>
        public static int MergeData(Project p)
        {
            int count = 0;
            foreach (var x in Enabled(p))
            {
                var dir = Path.Combine(x.Folder, "Data");
                if (!Directory.Exists(dir)) continue;
                foreach (var table in p.Tables)
                {
                    var file = Path.Combine(dir, table.FileName);
                    if (!File.Exists(file)) continue;
                    try
                    {
                        var listType = typeof(List<>).MakeGenericType(table.RecordType);
                        var records = (IList)Json.Json.ConvertTo(Json.Json.Parse(File.ReadAllText(file)), listType);
                        var target = table.Items;
                        foreach (var rec in records)
                        {
                            var gr = rec as PokeCreator.Data.GameRecord;
                            int existing = -1;
                            if (gr != null)
                                for (int i = 0; i < target.Count; i++) { var o = target[i] as PokeCreator.Data.GameRecord; if (o != null && o.Id == gr.Id) { existing = i; break; } }
                            if (existing >= 0) target[existing] = rec; else target.Add(rec);
                            count++;
                        }
                    }
                    catch (Exception e) { Console.WriteLine("[plugins] " + x.Name + ": couldn't read " + table.FileName + ": " + e.Message); }
                }
            }
            return count;
        }
    }
}
