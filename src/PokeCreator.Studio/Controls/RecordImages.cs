using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Projects;
using PokeCreator.Studio.Editors;

namespace PokeCreator.Studio.Controls
{
    /// <summary>
    /// Which images belong to database entries (Pokémon sprites, trainer pictures), where they live,
    /// and which entries are missing them. File names are the entry's ID, like the game expects:
    /// Graphics/Pokemon/Front/CHARIZARD_1.png, Graphics/Trainers/YOUNGSTER.png.
    /// </summary>
    public class RecordImages
    {
        public class Kind
        {
            public string Label, Folder;
            public Kind(string label, string folder) { Label = label; Folder = folder; }
            public override string ToString() { return Label; }
        }

        readonly Project _p;
        public readonly Type RecordType;
        public readonly Kind[] Kinds;
        readonly Dictionary<string, HashSet<string>> _files = new Dictionary<string, HashSet<string>>();
        static readonly string[] Exts = { ".png", ".gif", ".bmp", ".jpg" };

        RecordImages(Project p, Type t, Kind[] kinds) { _p = p; RecordType = t; Kinds = kinds; Refresh(); }

        /// <summary>Image info for a table, or null if its entries have no images of their own.</summary>
        public static RecordImages For(Project p, Type recordType)
        {
            if (recordType == typeof(SpeciesData))
                return new RecordImages(p, recordType, new[] {
                    new Kind("Front", "Graphics/Pokemon/Front"), new Kind("Back", "Graphics/Pokemon/Back"),
                    new Kind("Front shiny", "Graphics/Pokemon/Front shiny"), new Kind("Back shiny", "Graphics/Pokemon/Back shiny"),
                    new Kind("Icon", "Graphics/Pokemon/Icons") });
            if (recordType == typeof(TrainerTypeData))
                return new RecordImages(p, recordType, new[] { new Kind("Trainer", "Graphics/Trainers") });
            return null;
        }

        /// <summary>The main image (Front sprite, trainer picture); entries without it are highlighted.</summary>
        public Kind Main { get { return Kinds[0]; } }

        /// <summary>File name (no extension) of an entry's images.</summary>
        public static string Key(GameRecord r)
        {
            var s = r as SpeciesData;
            if (s != null)
            {
                string sp = string.IsNullOrEmpty(s.Species) ? s.Id : s.Species;
                return s.Form > 0 ? sp + "_" + s.Form : sp;
            }
            return r.Id;
        }

        public void Refresh()
        {
            _files.Clear();
            foreach (var k in Kinds)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string dir = Path.Combine(_p.Root, k.Folder.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir))
                        if (Exts.Contains(Path.GetExtension(f).ToLowerInvariant())) set.Add(Path.GetFileNameWithoutExtension(f));
                _files[k.Folder] = set;
            }
        }

        public bool Has(GameRecord r, Kind k) { HashSet<string> set; return r != null && _files.TryGetValue(k.Folder, out set) && set.Contains(Key(r)); }
        public bool HasMain(GameRecord r) { return Has(r, Main); }
        /// <summary>True if any of the entry's images (front, back, shiny, icon...) is missing.</summary>
        public bool MissingAny(GameRecord r) { return Kinds.Any(k => !Has(r, k)); }

        public string PathOf(GameRecord r, Kind k) { return Has(r, k) ? _p.FindAsset(k.Folder, Key(r)) : null; }

        /// <summary>Saves an image file as an entry's image (as PNG). Returns the path written.</summary>
        public string SaveImage(string sourceFile, GameRecord r, Kind k)
        {
            string dir = Path.Combine(_p.Root, k.Folder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);
            // Remove an old image with another extension so the new one is the one found
            foreach (var e in Exts) { string old = Path.Combine(dir, Key(r) + e); if (File.Exists(old) && !string.Equals(old, sourceFile, StringComparison.OrdinalIgnoreCase)) File.Delete(old); }
            string dest = Path.Combine(dir, Key(r) + ".png");
            if (Path.GetExtension(sourceFile).ToLowerInvariant() == ".png") File.Copy(sourceFile, dest, true);
            else using (var img = Theme.LoadImage(sourceFile)) { if (img == null) throw new InvalidDataException("Couldn't read " + Path.GetFileName(sourceFile) + "."); img.Save(dest, ImageFormat.Png); }
            _files[k.Folder].Add(Key(r));
            return dest;
        }

        /// <summary>Turns a file name into an ID the way Essentials writes them: CHARIZARD_1, YOUNGSTER.</summary>
        public static string IdFromFileName(string file)
        {
            string n = Path.GetFileNameWithoutExtension(file).Trim();
            var chars = n.Select(c => char.IsLetterOrDigit(c) && c < 128 ? char.ToUpperInvariant(c) : c == '_' || c == ' ' || c == '-' ? '_' : '\0').Where(c => c != '\0').ToArray();
            string id = new string(chars).Trim('_');
            while (id.Contains("__")) id = id.Replace("__", "_");
            return id;
        }

        /// <summary>"YOUNG_COUPLE" → "Young Couple".</summary>
        public static string NameFromId(string id)
        {
            return string.Join(" ", id.Split('_').Where(w => w.Length > 0).Select(w => w.Length == 1 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant()));
        }
    }

    /// <summary>The image pane beside an entry's details: its sprites, or a note that it has none.</summary>
    public class SpritePanel : Panel
    {
        readonly RecordImages _images;
        readonly SpriteBox _main = new SpriteBox { Dock = DockStyle.Top, Height = 210 };
        readonly Label _caption = new Label { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        readonly FlowLayoutPanel _others = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 0, Padding = new Padding(4, 0, 0, 0) };
        readonly List<SpriteBox> _small = new List<SpriteBox>();
        GameRecord _record;
        public event Action<RecordImages.Kind> ImportRequested;

        public SpritePanel(RecordImages images)
        {
            _images = images;
            Dock = DockStyle.Right; Width = 236; Padding = new Padding(6);
            AutoScroll = true;
            var import = new Button { Text = "Import Image…", AutoSize = true };
            import.Click += (s, e) =>
            {
                if (ImportRequested == null) return;
                if (_images.Kinds.Length == 1) { ImportRequested(_images.Main); return; }
                int k = ChoiceDialog.Ask(FindForm(), "Which image is this?", _images.Kinds.Select(x => x.Label + (_images.Has(_record, x) ? "  (replace)" : "")).ToArray());
                if (k >= 0) ImportRequested(_images.Kinds[k]);
            };
            var open = new Button { Text = "Open Folder", AutoSize = true };
            open.Click += (s, e) =>
            {
                string dir = Path.Combine(ProjectRoot, _images.Main.Folder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(dir);
                MainForm.OpenInShell(dir);
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36 };
            buttons.Controls.Add(import); buttons.Controls.Add(open);
            foreach (var k in _images.Kinds.Skip(1))
            {
                var b = new SpriteBox { Width = 104, Height = 118, Small = true, Margin = new Padding(2) };
                _small.Add(b); _others.Controls.Add(b);
            }
            _others.Height = _small.Count == 0 ? 0 : ((_small.Count + 1) / 2) * 122 + 4;
            Controls.Add(_others);
            Controls.Add(buttons);
            Controls.Add(_caption);
            Controls.Add(_main);
        }

        string ProjectRoot { get { var mf = FindForm() as MainForm; return mf != null && mf.Project != null ? mf.Project.Root : ""; } }

        public void Show(GameRecord r)
        {
            _record = r;
            _main.SetImage(r == null ? null : _images.PathOf(r, _images.Main), _images.Main.Label, r != null && !_images.HasMain(r));
            bool has = r != null && _images.HasMain(r);
            _caption.Text = r == null ? "" : has ? RecordImages.Key(r) + ".png" : "No " + _images.Main.Label.ToLowerInvariant() + " image";
            _caption.ForeColor = has ? (Theme.Dark ? Color.Gainsboro : Color.DimGray) : Color.IndianRed;
            for (int i = 0; i < _small.Count; i++)
            {
                var k = _images.Kinds[i + 1];
                _small[i].SetImage(r == null ? null : _images.PathOf(r, k), k.Label, r != null && !_images.Has(r, k));
            }
        }

        /// <summary>Draws a sprite scaled by whole steps (crisp pixels) with its label; icons show their first frame.</summary>
        class SpriteBox : Control
        {
            Image _img; string _label = ""; bool _missing;
            public bool Small;
            public SpriteBox() { DoubleBuffered = true; }

            public void SetImage(string path, string label, bool missing)
            {
                if (_img != null) { _img.Dispose(); _img = null; }
                _img = Theme.LoadImage(path);
                _label = label; _missing = missing;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                bool dark = Theme.Dark;
                var area = new Rectangle(2, 2, Width - 4, Height - (Small ? 22 : 6));
                // Checkerboard so transparent parts are visible
                using (var a = new SolidBrush(dark ? Color.FromArgb(46, 47, 52) : Color.FromArgb(246, 246, 248)))
                using (var b = new SolidBrush(dark ? Color.FromArgb(54, 55, 61) : Color.FromArgb(232, 232, 236)))
                {
                    g.FillRectangle(a, area);
                    for (int y = area.Top; y < area.Bottom; y += 12)
                        for (int x = area.Left + ((y - area.Top) / 12 % 2) * 12; x < area.Right; x += 24)
                            g.FillRectangle(b, x, y, Math.Min(12, area.Right - x), Math.Min(12, area.Bottom - y));
                }
                if (_img != null)
                {
                    // Animated strips and 2-frame icons: show the first frame
                    int fw = _img.Width, fh = _img.Height;
                    if (fw >= fh * 2) fw = _label == "Icon" ? fw / 2 : fh;
                    float scale = Math.Min((float)area.Width / fw, (float)area.Height / fh);
                    if (scale >= 1) scale = (float)Math.Floor(scale);
                    int w = (int)(fw * scale), h = (int)(fh * scale);
                    g.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(_img, new Rectangle(area.Left + (area.Width - w) / 2, area.Top + (area.Height - h) / 2, w, h), new Rectangle(0, 0, fw, fh), GraphicsUnit.Pixel);
                }
                else
                {
                    using (var f = new Font("Segoe UI", Small ? 8f : 10f))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(_missing ? (Small ? "Missing" : "No image yet\n\nUse Import Image…\nor the Import button") : "—", f, _missing ? Brushes.IndianRed : Brushes.Gray, area, sf);
                }
                using (var pen = new Pen(_missing ? Color.IndianRed : dark ? Color.FromArgb(70, 72, 80) : Color.FromArgb(210, 210, 216))) g.DrawRectangle(pen, area);
                if (Small)
                    using (var f = new Font("Segoe UI", 8.5f))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                    using (var br = new SolidBrush(_missing ? Color.IndianRed : dark ? Color.Gainsboro : Color.DimGray))
                        g.DrawString(_label, f, br, new RectangleF(0, Height - 19, Width, 18), sf);
            }

            protected override void Dispose(bool disposing) { if (disposing && _img != null) _img.Dispose(); base.Dispose(disposing); }
        }
    }
}
