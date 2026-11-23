using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Json;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Battle animations found in something the user picked to import, with the sheets and sounds they use:
    /// a package folder (animation.json and/or Data/PkmnAnimations.rxdata, Graphics/Animations, Audio/SE/Anim),
    /// the same zipped, a single PokeCreator animation .json, an Essentials PkmnAnimations.rxdata or exported .anm,
    /// a whole Essentials project, another PokeCreator project, or a folder holding several packages.
    /// </summary>
    public class AnimationPackage : IDisposable
    {
        public static readonly string[] ImageExts = { ".png", ".gif", ".bmp", ".jpg", ".jpeg" };
        public static readonly string[] SoundExts = { ".wav", ".ogg", ".mp3", ".mid", ".midi", ".m4a", ".mp4", ".flac", ".opus" };
        public const string FileFilter = "Animation packages and files (*.zip;*.json;*.rxdata;*.anm)|*.zip;*.json;*.rxdata;*.anm|All files (*.*)|*.*";
        public const string SupportedText = "a package folder or .zip (animation.json and/or Data/PkmnAnimations.rxdata, with Graphics/Animations and Audio/SE/Anim), "
                                          + "a PokeCreator animation .json, an Essentials PkmnAnimations.rxdata or exported .anm, a whole Essentials or PokeCreator project, "
                                          + "or a folder with several packages in it";

        public string Source;
        public readonly List<BattleAnimation> Animations = new List<BattleAnimation>();
        public readonly List<string> Notes = new List<string>();
        public string Readme;
        readonly List<string> _files = new List<string>();
        string _root, _temp;

        // ------------------------------------------------------------------ reading
        public static AnimationPackage Load(string path)
        {
            var pk = new AnimationPackage { Source = path };
            try { pk.Read(path); }
            catch { pk.Dispose(); throw; }
            return pk;
        }

        void Read(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (Directory.Exists(path)) { _root = path; Scan(path); return; }
            if (!File.Exists(path)) throw new FileNotFoundException("Not found: " + path);
            if (ext == ".zip")
            {
                _temp = Path.Combine(Path.GetTempPath(), "PokeCreatorImport", Guid.NewGuid().ToString("N"));
                Extract(path, _temp);
                _root = _temp;
                Scan(_temp);
                return;
            }
            // A single file: its media is looked for beside it (or around its Data folder)
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            _root = string.Equals(Path.GetFileName(dir), "Data", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(dir) : dir;
            ListFiles(_root, 0);
            if (ext == ".json") ReadJson(path);
            else if (ext == ".rxdata" || ext == ".anm") ReadMarshal(path);
            else throw new InvalidDataException("PokeCreator can't import " + ext + " files as battle animations.\nUse " + SupportedText + ".");
            FindReadme();
        }

        static void Extract(string zip, string to)
        {
            Directory.CreateDirectory(to);
            string full = Path.GetFullPath(to) + Path.DirectorySeparatorChar;
            using (var z = ZipFile.OpenRead(zip))
                foreach (var e in z.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue; // a folder entry
                    string dest = Path.GetFullPath(Path.Combine(to, e.FullName.Replace('\\', '/').TrimStart('/')));
                    if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue; // never write outside the temp folder
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
        }

        void ListFiles(string dir, int depth)
        {
            if (depth > 6 || _files.Count > 50000) return;
            string name = Path.GetFileName(dir);
            if (depth > 0 && (name.StartsWith(".") || name == "bin" || name == "obj")) return;
            try { _files.AddRange(Directory.GetFiles(dir)); foreach (var d in Directory.GetDirectories(dir)) ListFiles(d, depth + 1); }
            catch (UnauthorizedAccessException) { }
        }

        void Scan(string folder)
        {
            ListFiles(folder, 0);
            // PokeCreator JSON first: it holds everything, so it wins over an Essentials copy of the same animation
            foreach (var f in _files.Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).OrderBy(Depth))
                ReadJson(f);
            foreach (var f in _files.Where(IsEssentialsFile).OrderBy(Depth))
                ReadMarshal(f);
            FindReadme();
        }

        static int Depth(string f) { return f.Count(c => c == '/' || c == '\\'); }

        static bool IsEssentialsFile(string f)
        {
            string n = Path.GetFileName(f).ToLowerInvariant();
            return n.EndsWith(".anm") || (n.EndsWith(".rxdata") && n.Contains("animation") && n != "animations.rxdata"); // Animations.rxdata = RMXP map animations
        }

        void ReadJson(string file)
        {
            try
            {
                if (new FileInfo(file).Length > 64L * 1024 * 1024) return;
                string text = File.ReadAllText(file, Encoding.UTF8);
                if (text.IndexOf("\"Frames\"", StringComparison.Ordinal) < 0) return; // cheap check: not an animation file
                var root = Json.Json.Parse(text);
                var items = new List<object>();
                var obj = root as JsonObject;
                if (obj != null && obj.GetArray("Animations") != null) items.AddRange(obj.GetArray("Animations"));
                else if (obj != null) items.Add(obj);
                else if (root is List<object>) items.AddRange((List<object>)root);
                int n = 0;
                foreach (var it in items)
                {
                    var o = it as JsonObject;
                    if (o == null || !o.ContainsKey("Frames")) continue;
                    var a = (BattleAnimation)Json.Json.ConvertTo(o, typeof(BattleAnimation));
                    if (a == null) continue;
                    if (string.IsNullOrEmpty(a.Id)) a.Id = Path.GetFileNameWithoutExtension(file);
                    Fix(a);
                    if (Add(a)) n++;
                }
                if (n > 0) Notes.Add(Rel(file) + ": " + n + " animation" + (n == 1 ? "" : "s"));
            }
            catch (Exception ex) { Notes.Add(Rel(file) + ": not read (" + ex.Message + ")"); }
        }

        void ReadMarshal(string file)
        {
            try
            {
                int n = 0, dup = 0;
                foreach (var a in PokeCreator.Import.EssentialsImporter.ReadAnimations(file)) { Fix(a); if (Add(a)) n++; else dup++; }
                Notes.Add(Rel(file) + ": " + n + " animation" + (n == 1 ? "" : "s") + " (Essentials)" + (dup > 0 ? ", " + dup + " already read from another file" : ""));
            }
            catch (Exception ex) { Notes.Add(Rel(file) + ": not read (" + ex.Message + ")"); }
        }

        static void Fix(BattleAnimation a)
        {
            if (a.Frames == null) a.Frames = new List<List<int[]>>();
            if (a.Timings == null) a.Timings = new List<AnimationTiming>();
            for (int i = 0; i < a.Frames.Count; i++)
            {
                if (a.Frames[i] == null) a.Frames[i] = new List<int[]>();
                for (int c = 0; c < a.Frames[i].Count; c++)
                {
                    var cel = a.Frames[i][c];
                    if (cel != null && cel.Length < BattleAnimation.CelLength) { var full = new int[BattleAnimation.CelLength]; Array.Copy(cel, full, cel.Length); a.Frames[i][c] = full; }
                }
            }
            foreach (var t in a.Timings) if (t.FlashColor == null || t.FlashColor.Length < 4) t.FlashColor = new[] { 255, 255, 255, 255 };
        }

        bool Add(BattleAnimation a)
        {
            if (Animations.Any(x => x.Id == a.Id)) return false;
            Animations.Add(a);
            return true;
        }

        void FindReadme()
        {
            var f = _files.Where(x => { string n = Path.GetFileName(x).ToLowerInvariant(); return n == "readme.txt" || n == "readme.md" || n == "readme"; })
                          .OrderBy(Depth).FirstOrDefault();
            if (f == null) return;
            try { Readme = File.ReadAllText(f, Encoding.UTF8).Replace("\r\n", "\n").Replace("\n", "\r\n"); } catch { }
        }

        string Rel(string f)
        {
            if (_root != null && f.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) f = f.Substring(_root.Length).TrimStart('/', '\\');
            return f.Replace('\\', '/');
        }

        // ------------------------------------------------------------------ media
        /// <summary>The package's file for an animation sheet, or null.</summary>
        public string FindSheet(string name) { return FindMedia(name, ImageExts, new[] { "graphics/animations/", "animations/" }); }
        /// <summary>The package's file for an animation sound, or null.</summary>
        public string FindSound(string name) { return FindMedia(name, SoundExts, new[] { "audio/se/anim/", "audio/se/", "se/anim/", "anim/", "audio/" }); }

        string FindMedia(string name, string[] exts, string[] preferred)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string want = name.Replace('\\', '/').ToLowerInvariant();
            string wantFile = want.Contains("/") ? want.Substring(want.LastIndexOf('/') + 1) : want;
            string best = null; int bestScore = int.MaxValue;
            foreach (var f in _files)
            {
                string e = Path.GetExtension(f).ToLowerInvariant();
                if (!exts.Contains(e)) continue;
                string rel = Rel(f).ToLowerInvariant();
                string noExt = rel.Substring(0, rel.Length - e.Length);
                string fileNoExt = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                int score;
                if (noExt == want || rel == want || noExt.EndsWith("/" + want) || rel.EndsWith("/" + want)) score = 0;
                else if (fileNoExt == wantFile || Path.GetFileName(f).ToLowerInvariant() == wantFile) score = 10;
                else continue;
                int pi = Array.FindIndex(preferred, p => rel.Contains(p));
                score += pi < 0 ? preferred.Length : pi;
                if (score < bestScore) { bestScore = score; best = f; }
            }
            return best;
        }

        // ------------------------------------------------------------------ installing
        public class Result { public int Added, Replaced, Skipped, Copied, Same, Renamed; public List<string> Missing = new List<string>(); public BattleAnimation First; }

        public enum Clash { Replace, KeepBoth, Skip }

        /// <summary>Adds the chosen animations to the project and copies the sheets and sounds they use into it.</summary>
        public Result Install(Project p, IEnumerable<BattleAnimation> chosen, Clash clash)
        {
            var r = new Result();
            var list = chosen.ToList();
            // Sheets and sounds first, so a renamed file can be renamed in the animations too
            var sheetMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var soundMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in list)
            {
                if (!string.IsNullOrEmpty(a.Graphic) && !sheetMap.ContainsKey(a.Graphic))
                    sheetMap[a.Graphic] = CopyMedia(p, FindSheet(a.Graphic), a.Graphic, "Graphics/Animations", Path.Combine(p.GraphicsDir, "Animations"), r);
                foreach (var t in a.Timings)
                    if (!string.IsNullOrEmpty(t.Sound) && !soundMap.ContainsKey(t.Sound))
                        soundMap[t.Sound] = CopyMedia(p, FindSound(t.Sound), t.Sound, "Audio/SE", Path.Combine(p.AudioDir, "SE", "Anim"), r);
            }
            foreach (var src in list)
            {
                // Each import gets its own copy, so importing the same package twice never shares objects
                var a = Json.Json.Deserialize<BattleAnimation>(Json.Json.Write(src, false));
                Fix(a);
                string g; if (a.Graphic != null && sheetMap.TryGetValue(a.Graphic, out g)) a.Graphic = g;
                foreach (var t in a.Timings) { string s; if (t.Sound != null && soundMap.TryGetValue(t.Sound, out s)) t.Sound = s; }
                var old = p.Animations.FirstOrDefault(x => x.Id == a.Id);
                if (old != null)
                {
                    if (clash == Clash.Skip) { r.Skipped++; continue; }
                    if (clash == Clash.Replace) { int i = p.Animations.IndexOf(old); p.Animations[i] = a; r.Replaced++; if (r.First == null) r.First = a; continue; }
                    a.Id = UniqueId(p, a.Id);
                }
                p.Animations.Add(a); r.Added++;
                if (r.First == null) r.First = a;
            }
            return r;
        }

        static string UniqueId(Project p, string id)
        {
            for (int n = 2; ; n++) { string c = id + " (" + n + ")"; if (!p.Animations.Any(x => x.Id == c)) return c; }
        }

        /// <summary>Copies one sheet or sound into the project and returns the name the animation should use for it.</summary>
        static string CopyMedia(Project p, string from, string name, string assetFolder, string destDir, Result r)
        {
            if (from == null)
            {
                bool have = p.FindAsset(assetFolder, name) != null || (assetFolder == "Audio/SE" && p.FindAsset("Audio/SE", "Anim/" + name) != null);
                if (!have) r.Missing.Add((assetFolder == "Audio/SE" ? "sound " : "sheet ") + name);
                return name;
            }
            string baseName = Path.GetFileName(name.Replace('\\', '/'));
            string ext = Path.GetExtension(from);
            if (ImageExts.Contains(Path.GetExtension(baseName).ToLowerInvariant()) || SoundExts.Contains(Path.GetExtension(baseName).ToLowerInvariant()))
                baseName = Path.GetFileNameWithoutExtension(baseName);
            Directory.CreateDirectory(destDir);
            for (int n = 1; ; n++)
            {
                string candidate = n == 1 ? baseName : baseName + " (" + n + ")";
                // Any file of that name, whatever its extension, counts as taken
                var existing = Directory.GetFiles(destDir).FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), candidate, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (SameBytes(existing, from)) { r.Same++; return candidate; }
                    continue; // a different file already has that name: use the next free one
                }
                File.Copy(from, Path.Combine(destDir, candidate + ext));
                r.Copied++;
                if (n > 1) r.Renamed++;
                return candidate;
            }
        }

        static bool SameBytes(string a, string b)
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            return File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b));
        }

        // ------------------------------------------------------------------ exporting
        /// <summary>
        /// Writes animations as a package zip that any PokeCreator project can import: animation.json (or animations.json
        /// for several), the sheets in Graphics/Animations, the sounds in Audio/SE/Anim and a README.txt.
        /// </summary>
        public static int Export(Project p, IList<BattleAnimation> anims, string zipPath, out List<string> missing)
        {
            missing = new List<string>();
            if (File.Exists(zipPath)) File.Delete(zipPath);
            int files = 0;
            using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                object json = anims.Count == 1 ? (object)anims[0] : anims;
                AddText(z, anims.Count == 1 ? "animation.json" : "animations.json", Json.Json.Write(json, true));
                var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in anims)
                {
                    if (!string.IsNullOrEmpty(a.Graphic) && done.Add("g:" + a.Graphic))
                    {
                        string f = p.FindAsset("Graphics/Animations", a.Graphic);
                        if (f == null) missing.Add("sheet " + a.Graphic);
                        else { z.CreateEntryFromFile(f, "Graphics/Animations/" + a.Graphic + Path.GetExtension(f)); files++; }
                    }
                    foreach (var t in a.Timings)
                        if (!string.IsNullOrEmpty(t.Sound) && done.Add("s:" + t.Sound))
                        {
                            string f = p.FindAsset("Audio/SE", "Anim/" + t.Sound) ?? p.FindAsset("Audio/SE", t.Sound);
                            if (f == null) missing.Add("sound " + t.Sound);
                            else { z.CreateEntryFromFile(f, "Audio/SE/Anim/" + t.Sound.Replace('\\', '/') + Path.GetExtension(f)); files++; }
                        }
                }
                var sb = new StringBuilder();
                sb.AppendLine("PokeCreator battle animation package");
                sb.AppendLine("====================================");
                sb.AppendLine();
                foreach (var a in anims) sb.AppendLine("  " + a.Id + "  (" + a.Frames.Count + " frames, sheet: " + (string.IsNullOrEmpty(a.Graphic) ? "none" : a.Graphic) + ")");
                sb.AppendLine();
                sb.AppendLine("INSTALL");
                sb.AppendLine("  In PokeCreator Studio: Database > Battle Animations > Import... and choose this zip");
                sb.AppendLine("  (or the folder you unzipped it to). Studio adds the animations and copies the sheets");
                sb.AppendLine("  and sounds into your project. Save.");
                AddText(z, "README.txt", sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"));
            }
            return files;
        }

        static void AddText(ZipArchive z, string name, string text)
        {
            var e = z.CreateEntry(name);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(text);
        }

        public void Dispose()
        {
            if (_temp == null) return;
            try { Directory.Delete(_temp, true); } catch { }
            _temp = null;
        }
    }

    /// <summary>Shows what a package holds and lets the user choose which animations to import and what to do about names they already have.</summary>
    public class AnimationImportDialog : Form
    {
        readonly AnimationPackage _pk;
        readonly Project _p;
        readonly CheckedListBox _list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        readonly ComboBox _clash = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        readonly Label _summary = new Label { Dock = DockStyle.Top, Height = 54, AutoEllipsis = true };
        List<BattleAnimation> _shown;

        public AnimationPackage.Clash Clash { get { return (AnimationPackage.Clash)_clash.SelectedIndex; } }
        public IEnumerable<BattleAnimation> Chosen { get { return _list.CheckedIndices.Cast<int>().Select(i => _shown[i]); } }

        public AnimationImportDialog(Project p, AnimationPackage pk)
        {
            _p = p; _pk = pk;
            Text = "Import Battle Animations — " + Path.GetFileName(pk.Source.TrimEnd('\\', '/'));
            Size = new Size(820, 620); MinimumSize = new Size(620, 420);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            Padding = new Padding(8);

            _shown = pk.Animations.OrderBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToList();
            int clashes = 0, missing = 0;
            foreach (var a in _shown)
            {
                bool have = p.Animations.Any(x => x.Id == a.Id);
                if (have) clashes++;
                var lacks = new List<string>();
                if (!string.IsNullOrEmpty(a.Graphic) && pk.FindSheet(a.Graphic) == null && p.FindAsset("Graphics/Animations", a.Graphic) == null) lacks.Add("sheet");
                if (a.Timings.Any(t => !string.IsNullOrEmpty(t.Sound) && pk.FindSound(t.Sound) == null && p.FindAsset("Audio/SE", "Anim/" + t.Sound) == null && p.FindAsset("Audio/SE", t.Sound) == null)) lacks.Add("sound");
                if (lacks.Count > 0) missing++;
                _list.Items.Add(a.Id + "   —   " + a.Frames.Count + " frames" + (have ? "   •  you already have one with this name" : "   •  new")
                                + (lacks.Count > 0 ? "   •  " + string.Join(" and ", lacks) + " not included" : ""), true);
            }
            _summary.Text = "Found " + _shown.Count + " animation" + (_shown.Count == 1 ? "" : "s") + " in " + pk.Source + "."
                          + (clashes > 0 ? "  " + clashes + " ha" + (clashes == 1 ? "s" : "ve") + " the same name as one you already have." : "")
                          + (missing > 0 ? "  " + missing + " use a sheet or sound that isn't in the package or your project." : "")
                          + "\r\nTheir sheets and sounds are copied into Graphics/Animations and Audio/SE/Anim (a different file with the same name is copied as \"name (2)\").";

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 0, 0) };
            var ok = new Button { Text = "Import", Width = 100, Height = 28, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Width = 100, Height = 28, DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(cancel); bottom.Controls.Add(ok);
            AcceptButton = ok; CancelButton = cancel;

            var options = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(0, 4, 0, 0) };
            options.Controls.Add(new Label { Text = "Same name as yours:", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
            _clash.Items.AddRange(new object[] { "Replace yours with the imported one", "Keep both (the imported one gets \" (2)\")", "Skip it (keep yours)" });
            _clash.SelectedIndex = 0;
            options.Controls.Add(_clash);
            var all = new Button { Text = "Select all", Width = 90, Height = 26, Margin = new Padding(20, 2, 3, 0) };
            var none = new Button { Text = "Select none", Width = 90, Height = 26, Margin = new Padding(3, 2, 3, 0) };
            all.Click += (s, e) => { for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, true); };
            none.Click += (s, e) => { for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, false); };
            options.Controls.Add(all); options.Controls.Add(none);

            Control centre = _list;
            if (!string.IsNullOrEmpty(pk.Readme) || pk.Notes.Count > 0)
            {
                var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
                split.Panel1.Controls.Add(_list);
                var info = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(pk.Readme)) sb.Append(pk.Readme.TrimEnd()).Append("\r\n\r\n");
                if (pk.Notes.Count > 0) { sb.Append("Read from:\r\n"); foreach (var n in pk.Notes) sb.Append("  ").Append(n).Append("\r\n"); }
                info.Text = sb.ToString();
                split.Panel2.Controls.Add(info);
                split.Panel2.Controls.Add(new Label { Text = string.IsNullOrEmpty(pk.Readme) ? "Package contents" : "The package's README", Dock = DockStyle.Top, Height = 20 });
                centre = split;
                info.TabStop = false;
                Load += (s, e) => { try { split.SplitterDistance = Math.Max(120, split.Height / 2); } catch { } };
                Shown += (s, e) => { info.Select(0, 0); ActiveControl = _list; };
            }
            Controls.Add(centre);
            Controls.Add(new Label { Text = "Tick the animations to import:", Dock = DockStyle.Top, Height = 20 });
            Controls.Add(_summary);
            Controls.Add(options);
            Controls.Add(bottom);
            centre.BringToFront();
            Theme.Apply(this);
        }
    }
}
