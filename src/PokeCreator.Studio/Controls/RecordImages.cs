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
            /// <summary>Text before the ID in the file name, e.g. "trainer_" for trainer_YOUNGSTER.png.</summary>
            public string Prefix = "";
            /// <summary>A 4×4 character sheet (overworld sprite): the pane shows the four facing directions.</summary>
            public bool Charset;
            /// <summary>An animated sprite (Gen 5 style strip): only checked for when the game uses animated sprites.</summary>
            public bool Animated;
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
                    new Kind("Icon", "Graphics/Pokemon/Icons"),
                    new Kind("Animated front", "Graphics/Pokemon/Animated/Front") { Animated = true },
                    new Kind("Animated back", "Graphics/Pokemon/Animated/Back") { Animated = true },
                    new Kind("Animated front shiny", "Graphics/Pokemon/Animated/Front shiny") { Animated = true },
                    new Kind("Animated back shiny", "Graphics/Pokemon/Animated/Back shiny") { Animated = true } });
            if (recordType == typeof(TrainerTypeData))
                return new RecordImages(p, recordType, new[] {
                    new Kind("Trainer", "Graphics/Trainers"),
                    new Kind("Overworld sprite", "Graphics/Characters") { Prefix = "trainer_", Charset = true } });
            if (recordType == typeof(ItemData))
                return new RecordImages(p, recordType, new[] { new Kind("Icon", "Graphics/Items") });
            return null;
        }

        /// <summary>The kinds that count as missing: animated sprites only when the game uses them (Game Settings → Using animated sprites).</summary>
        public Kind[] Active { get { return Kinds.Where(k => !k.Animated || _p.Settings.UseAnimatedSprites).ToArray(); } }
        public bool HasAnimatedKinds { get { return Kinds.Any(k => k.Animated); } }
        public bool UsingAnimated { get { return _p.Settings.UseAnimatedSprites; } }
        public int AnimatedSpeed { get { return _p.Settings.AnimatedSpriteSpeed > 0 ? _p.Settings.AnimatedSpriteSpeed : 3; } }

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

        /// <summary>File name (no extension) of one of an entry's images, e.g. trainer_YOUNGSTER.</summary>
        public static string FileKey(GameRecord r, Kind k) { return k.Prefix + Key(r); }

        public bool Has(GameRecord r, Kind k) { HashSet<string> set; return r != null && _files.TryGetValue(k.Folder, out set) && set.Contains(FileKey(r, k)); }
        public bool HasMain(GameRecord r) { return Has(r, Main); }
        /// <summary>True if any of the entry's images (front, back, shiny, icon...) is missing.</summary>
        public bool MissingAny(GameRecord r) { return Active.Any(k => !Has(r, k)); }

        public string PathOf(GameRecord r, Kind k) { return Has(r, k) ? _p.FindAsset(k.Folder, FileKey(r, k)) : null; }

        /// <summary>Saves an image file as an entry's image (as PNG). Returns the path written.</summary>
        public string SaveImage(string sourceFile, GameRecord r, Kind k)
        {
            string dir = Path.Combine(_p.Root, k.Folder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);
            // Remove an old image with another extension so the new one is the one found
            string key = FileKey(r, k);
            foreach (var e in Exts) { string old = Path.Combine(dir, key + e); if (File.Exists(old) && !string.Equals(old, sourceFile, StringComparison.OrdinalIgnoreCase)) File.Delete(old); }
            string dest = Path.Combine(dir, key + ".png");
            if (Path.GetExtension(sourceFile).ToLowerInvariant() == ".png") File.Copy(sourceFile, dest, true);
            else using (var img = Theme.LoadImage(sourceFile)) { if (img == null) throw new InvalidDataException("Couldn't read " + Path.GetFileName(sourceFile) + "."); img.Save(dest, ImageFormat.Png); }
            _files[k.Folder].Add(key);
            return dest;
        }

        /// <summary>
        /// Saves an animated sprite: a GIF becomes a horizontal strip of square pictures (one per GIF frame); a PNG strip
        /// (or a single picture) is kept as it is. Gen 5 sized pictures (up to 128 pixels) are doubled, like Essentials' still sprites.
        /// Returns the path written and the number of pictures.
        /// </summary>
        public string SaveAnimated(string sourceFile, GameRecord r, Kind k, out int frames)
        {
            var pics = new List<Bitmap>();
            try
            {
                if (Path.GetExtension(sourceFile).Equals(".gif", StringComparison.OrdinalIgnoreCase))
                    pics.AddRange(GifDecoder.ReadFrames(sourceFile));   // every frame, with transparency, the same on every system
                else using (var src = Image.FromFile(sourceFile))
                {
                    {
                        // A PNG strip: square pictures side by side (as wide as it is tall each)
                        int fw = src.Width > src.Height * 3 / 2 ? src.Height : src.Width;
                        for (int x = 0; x + fw <= src.Width; x += fw)
                        {
                            var b = new Bitmap(fw, src.Height, PixelFormat.Format32bppArgb);
                            using (var g = Graphics.FromImage(b)) g.DrawImage(src, new Rectangle(0, 0, fw, src.Height), new Rectangle(x, 0, fw, src.Height), GraphicsUnit.Pixel);
                            pics.Add(b);
                        }
                    }
                }
                if (pics.Count == 0) throw new InvalidDataException("Couldn't read " + Path.GetFileName(sourceFile) + ".");
                int size = pics.Max(b => Math.Max(b.Width, b.Height));
                int scale = size <= 128 ? 2 : 1;
                int cell = size * scale;
                string dir = Path.Combine(_p.Root, k.Folder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(dir);
                string key = FileKey(r, k);
                foreach (var e in Exts) { string old = Path.Combine(dir, key + e); if (File.Exists(old)) File.Delete(old); }
                string dest = Path.Combine(dir, key + ".png");
                using (var strip = new Bitmap(size * pics.Count, size, PixelFormat.Format32bppArgb))
                {
                    // Each picture centred side to side, standing on the bottom edge like the still sprites (copied 1:1)
                    for (int i = 0; i < pics.Count; i++) CopyPixels(pics[i], strip, i * size + (size - pics[i].Width) / 2, size - pics[i].Height);
                    if (scale == 1) strip.Save(dest, ImageFormat.Png);
                    else using (var big = ScaleNearest(strip, scale)) big.Save(dest, ImageFormat.Png);
                }
                frames = pics.Count;
                _files[k.Folder].Add(key);
                return dest;
            }
            finally { foreach (var b in pics) b.Dispose(); }
        }

        static void CopyPixels(Bitmap src, Bitmap dst, int dx, int dy)
        {
            int w = src.Width, h = src.Height;
            var sd = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var dd = dst.LockBits(new Rectangle(0, 0, dst.Width, dst.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    int ty = dy + y;
                    if (ty < 0 || ty >= dst.Height) continue;
                    System.Runtime.InteropServices.Marshal.Copy(sd.Scan0 + y * sd.Stride, row, 0, w);
                    int x0 = Math.Max(0, -dx), x1 = Math.Min(w, dst.Width - dx);
                    if (x1 > x0) System.Runtime.InteropServices.Marshal.Copy(row, x0, dd.Scan0 + ty * dd.Stride + (dx + x0) * 4, x1 - x0);
                }
            }
            finally { src.UnlockBits(sd); dst.UnlockBits(dd); }
        }

        /// <summary>Pixel-doubling (crisp, no smoothing at the edges).</summary>
        static Bitmap ScaleNearest(Bitmap src, int scale)
        {
            int w = src.Width, h = src.Height;
            var dst = new Bitmap(w * scale, h * scale, PixelFormat.Format32bppArgb);
            var sd = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var dd = dst.LockBits(new Rectangle(0, 0, w * scale, h * scale), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w]; var outRow = new int[w * scale];
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(sd.Scan0 + y * sd.Stride, row, 0, w);
                    for (int x = 0; x < w; x++) for (int s = 0; s < scale; s++) outRow[x * scale + s] = row[x];
                    for (int s = 0; s < scale; s++) System.Runtime.InteropServices.Marshal.Copy(outRow, 0, dd.Scan0 + (y * scale + s) * dd.Stride, w * scale);
                }
            }
            finally { src.UnlockBits(sd); dst.UnlockBits(dd); }
            return dst;
        }

        /// <summary>Turns a file name into an ID the way Essentials writes them: CHARIZARD_1, YOUNGSTER.</summary>
        public static string IdFromFileName(string file, Kind k = null)
        {
            string n = Path.GetFileNameWithoutExtension(file).Trim();
            // trainer_YOUNGSTER.png is the overworld sprite of YOUNGSTER
            if (k != null && k.Prefix.Length > 0 && n.StartsWith(k.Prefix, StringComparison.OrdinalIgnoreCase)) n = n.Substring(k.Prefix.Length);
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
        readonly List<RecordImages.Kind> _smallKinds = new List<RecordImages.Kind>();
        // Animated sprites (Gen 5 style): their own box under the still images, playing their pictures
        readonly List<SpriteBox> _anim = new List<SpriteBox>();
        readonly List<RecordImages.Kind> _animKinds = new List<RecordImages.Kind>();
        readonly Label _animNote = new Label { Dock = DockStyle.Bottom, Height = 30, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f) };
        readonly Timer _animTimer = new Timer();
        GameRecord _record;
        public event Action<RecordImages.Kind> ImportRequested;

        public SpritePanel(RecordImages images)
        {
            _images = images;
            Dock = DockStyle.Right; Width = images.HasAnimatedKinds ? 262 : 236; Padding = new Padding(6);
            AutoScroll = true;
            var import = new Button { Text = "Import Image…", AutoSize = true };
            import.Click += (s, e) =>
            {
                if (ImportRequested == null) return;
                var still = _images.Kinds.Where(x => !x.Animated).ToArray();
                if (still.Length == 1) { ImportRequested(still[0]); return; }
                int k = ChoiceDialog.Ask(FindForm(), "Which image is this?", still.Select(x => x.Label + (_images.Has(_record, x) ? "  (replace)" : "")).ToArray());
                if (k >= 0) ImportRequested(still[k]);
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
            foreach (var k in _images.Kinds.Skip(1).Where(x => !x.Animated))
            {
                // Overworld sprites (four directions) get a full-width box; other sprites two per row
                var b = new SpriteBox { Width = k.Charset ? 212 : 104, Height = k.Charset ? 136 : 118, Small = true, Charset = k.Charset, Margin = new Padding(2) };
                _small.Add(b); _smallKinds.Add(k); _others.Controls.Add(b);
            }
            _others.Height = _small.Count == 0 ? 0 : _small.Sum(b => b.Charset ? 140 : 0) + ((_small.Count(b => !b.Charset) + 1) / 2) * 122 + 4;
            if (_images.HasAnimatedKinds)
            {
                var group = new GroupBox { Text = "Animated sprites (Gen 5)", Dock = DockStyle.Top, Padding = new Padding(4) };
                var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(2, 0, 0, 0) };
                foreach (var k in _images.Kinds.Where(x => x.Animated))
                {
                    var b = new SpriteBox { Width = 100, Height = 114, Small = true, Animate = true, Margin = new Padding(2) };
                    _anim.Add(b); _animKinds.Add(k); flow.Controls.Add(b);
                }
                var animImport = new Button { Text = "Import Animated…", AutoSize = true };
                animImport.Click += (s, e) =>
                {
                    if (ImportRequested == null || _record == null) return;
                    int k = ChoiceDialog.Ask(FindForm(), "Which animated sprite is this? (a GIF, or a PNG strip of square pictures)", _animKinds.Select(x => x.Label + (_images.Has(_record, x) ? "  (replace)" : "")).ToArray());
                    if (k >= 0) ImportRequested(_animKinds[k]);
                };
                var animOpen = new Button { Text = "Open Folder", AutoSize = true };
                animOpen.Click += (s, e) =>
                {
                    string dir = Path.Combine(ProjectRoot, "Graphics/Pokemon/Animated".Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(dir);
                    MainForm.OpenInShell(dir);
                };
                var animButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
                animButtons.Controls.Add(animImport); animButtons.Controls.Add(animOpen);
                group.Controls.Add(flow); group.Controls.Add(_animNote); group.Controls.Add(animButtons);
                group.Height = ((_anim.Count + 1) / 2) * 118 + 34 + 30 + 26;
                Controls.Add(group);
                // Play the strips at the game's speed
                _animTimer.Interval = Math.Max(16, _images.AnimatedSpeed * 1000 / 60);
                _animTimer.Tick += (s, e) => { foreach (var b in _anim) b.NextFrame(); };
                _animTimer.Start();
            }
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
            _caption.Text = r == null ? "" : has ? RecordImages.FileKey(r, _images.Main) + ".png" : "No " + _images.Main.Label.ToLowerInvariant() + " image";
            _caption.ForeColor = has ? (Theme.Dark ? Color.Gainsboro : Color.DimGray) : Color.IndianRed;
            for (int i = 0; i < _small.Count; i++)
            {
                var k = _smallKinds[i];
                _small[i].SetImage(r == null ? null : _images.PathOf(r, k), k.Charset && r != null ? k.Label + "\n" + RecordImages.FileKey(r, k) + ".png" : k.Label, r != null && !_images.Has(r, k));
            }
            // Animated sprites only count as missing when the game uses them
            for (int i = 0; i < _anim.Count; i++)
            {
                var k = _animKinds[i];
                _anim[i].SetImage(r == null ? null : _images.PathOf(r, k), k.Label.Replace("Animated ", ""), r != null && _images.UsingAnimated && !_images.Has(r, k));
            }
            _animNote.Text = _images.UsingAnimated ? "Graphics/Pokemon/Animated: GIFs become strips; Gen 5 sizes are doubled."
                                                   : "Not checked: tick \"Using animated sprites\" to use and check for them.";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _animTimer.Stop(); _animTimer.Dispose(); }
            base.Dispose(disposing);
        }

        /// <summary>Draws a sprite scaled by whole steps (crisp pixels) with its label; icons show their first frame.</summary>
        class SpriteBox : Control
        {
            Image _img; string _label = ""; bool _missing;
            public bool Small, Charset, Animate;
            int _frame;
            /// <summary>Animated boxes: shows the next picture of the strip.</summary>
            public void NextFrame()
            {
                if (_img == null || !Animate || _img.Width < _img.Height * 2) return;
                _frame++;
                Invalidate();
            }
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
                int labelH = Small ? (_label.Contains("\n") ? 34 : 19) : 0;
                var area = new Rectangle(2, 2, Width - 4, Height - (Small ? labelH + 3 : 6));
                // Checkerboard so transparent parts are visible
                using (var a = new SolidBrush(dark ? Color.FromArgb(46, 47, 52) : Color.FromArgb(246, 246, 248)))
                using (var b = new SolidBrush(dark ? Color.FromArgb(54, 55, 61) : Color.FromArgb(232, 232, 236)))
                {
                    g.FillRectangle(a, area);
                    for (int y = area.Top; y < area.Bottom; y += 12)
                        for (int x = area.Left + ((y - area.Top) / 12 % 2) * 12; x < area.Right; x += 24)
                            g.FillRectangle(b, x, y, Math.Min(12, area.Right - x), Math.Min(12, area.Bottom - y));
                }
                if (_img != null && Charset)
                {
                    // 4×4 character sheet: the standing frame of each direction (down, left, right, up) side by side
                    int fw = _img.Width / 4, fh = _img.Height / 4;
                    float scale = Math.Min((area.Width - 8f) / (fw * 4), (float)area.Height / fh);
                    if (scale >= 1) scale = (float)Math.Floor(scale);
                    int w = (int)(fw * scale), h = (int)(fh * scale);
                    int x0 = area.Left + (area.Width - w * 4) / 2;
                    g.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    for (int d = 0; d < 4; d++)
                        g.DrawImage(_img, new Rectangle(x0 + d * w, area.Top + (area.Height - h) / 2, w, h), new Rectangle(0, d * fh, fw, fh), GraphicsUnit.Pixel);
                }
                else if (_img != null)
                {
                    // Animated strips and 2-frame icons: show the first frame (animated boxes play the strip)
                    int fw = _img.Width, fh = _img.Height;
                    if (fw >= fh * 2) fw = _label == "Icon" ? fw / 2 : fh;
                    int sx = Animate && fw > 0 && _img.Width >= fw * 2 ? (_frame % (_img.Width / fw)) * fw : 0;
                    float scale = Math.Min((float)area.Width / fw, (float)area.Height / fh);
                    if (scale >= 1) scale = (float)Math.Floor(scale);
                    int w = (int)(fw * scale), h = (int)(fh * scale);
                    g.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(_img, new Rectangle(area.Left + (area.Width - w) / 2, area.Top + (area.Height - h) / 2, w, h), new Rectangle(sx, 0, fw, fh), GraphicsUnit.Pixel);
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
                        g.DrawString(_label, f, br, new RectangleF(0, Height - labelH, Width, labelH), sf);
            }

            protected override void Dispose(bool disposing) { if (disposing && _img != null) _img.Dispose(); base.Dispose(disposing); }
        }
    }
}
