using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using PokeCreator.Import;
using PokeCreator.Json;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    // =====================================================================================
    /// <summary>Tilesets: images plus per-tile passability, priority and terrain tags.</summary>
    public class TilesetEditor : UserControl, IDocument
    {
        enum Mode { Passage, Priority, Terrain, Bush, Counter }
        readonly Project _p;
        readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        readonly Panel _formHost = new Panel { Dock = DockStyle.Top, Height = 230 };
        readonly Panel _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(60, 62, 70) };
        readonly TilesetCanvas _canvas;
        Mode _mode = Mode.Passage;
        public string Title { get { return "Tilesets"; } }
        public bool Dirty { get; private set; }
        public event Action DirtyChanged;

        public TilesetEditor(Project p)
        {
            _p = p; Dock = DockStyle.Fill;
            _canvas = new TilesetCanvas(this);
            _scroll.Controls.Add(_canvas);
            var left = new Panel { Dock = DockStyle.Left, Width = 220 };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 68 };
            var add = new Button { Text = "New Tileset" }; add.Click += (s, e) => { int id = _p.Tilesets.Count == 0 ? 1 : _p.Tilesets.Max(t => t.Id) + 1; _p.Tilesets.Add(new TilesetData { Id = id, Name = "New tileset" }); Fill(); _list.SelectedIndex = _list.Items.Count - 1; Mark(); };
            var fromImage = new Button { Text = "New From Image…", AutoSize = true };
            fromImage.Click += (s, e) =>
            {
                string name = TilesetImport.PickTileset(FindForm(), _p);
                if (name == null) return;
                int id = _p.Tilesets.Count == 0 ? 1 : _p.Tilesets.Max(t => t.Id) + 1;
                _p.Tilesets.Add(new TilesetData { Id = id, Name = name, TilesetName = name });
                Fill(); _list.SelectedIndex = _list.Items.Count - 1; Mark();
            };
            bar.Controls.Add(add);
            bar.Controls.Add(fromImage);
            left.Controls.Add(_list); left.Controls.Add(bar);

            var modes = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
            modes.Controls.Add(new Label { Text = "Click tiles to change:", AutoSize = true, Margin = new Padding(4, 8, 6, 0) });
            foreach (Mode m in Enum.GetValues(typeof(Mode)))
            {
                var mm = m;
                var rb = new RadioButton { Text = m == Mode.Passage ? "Passable (○/✕)" : m == Mode.Priority ? "Priority (above player)" : m == Mode.Terrain ? "Terrain tag" : m == Mode.Bush ? "Bush" : "Counter", AutoSize = true, Checked = m == Mode.Passage, Appearance = Appearance.Button };
                rb.CheckedChanged += (s, e) => { if (rb.Checked) { _mode = mm; _canvas.Invalidate(); } };
                modes.Controls.Add(rb);
            }
            var imageBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
            var changeImage = new Button { Text = "Replace Tileset Image…", AutoSize = true };
            changeImage.Click += (s, e) =>
            {
                var ts = Current; if (ts == null) return;
                string name = TilesetImport.PickTileset(FindForm(), _p);
                if (name == null) return;
                ts.TilesetName = name; Mark(); ShowSelected();
            };
            var autotile = new Button { Text = "Import Autotile…", AutoSize = true };
            autotile.Click += (s, e) =>
            {
                var ts = Current; if (ts == null) return;
                while (ts.AutotileNames.Count < 7) ts.AutotileNames.Add("");
                var slots = Enumerable.Range(0, 7).Select(i => "Slot " + (i + 1) + ": " + (ts.AutotileNames[i] == "" ? "(empty)" : ts.AutotileNames[i])).ToArray();
                int slot = ChoiceDialog.Ask(FindForm(), "Which autotile slot?", slots);
                if (slot < 0) return;
                string name = TilesetImport.PickAutotile(FindForm(), _p);
                if (name == null) return;
                ts.AutotileNames[slot] = name; Mark(); ShowSelected();
            };
            imageBar.Controls.Add(changeImage);
            imageBar.Controls.Add(autotile);
            imageBar.Controls.Add(new Label { Text = "16×16 sheets are doubled and wide sheets re-arranged to 8 tiles across automatically.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 8, 0, 0) });
            var help = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray, Text = "Left-click to raise/toggle, right-click to lower. Terrain: 1 ledge, 2 grass, 5-9 water, 10 tall grass, 12 ice, 15 bridge." };

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_scroll); right.Controls.Add(help); right.Controls.Add(modes); right.Controls.Add(imageBar); right.Controls.Add(_formHost);
            Controls.Add(right); Controls.Add(new Splitter { Dock = DockStyle.Left }); Controls.Add(left);
            _list.SelectedIndexChanged += (s, e) => ShowSelected();
            Fill();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        void Fill() { _list.Items.Clear(); foreach (var t in _p.Tilesets) _list.Items.Add(t); }
        void Mark() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }
        public TilesetData Current { get { return _list.SelectedItem as TilesetData; } }
        public TileGraphics Graphics;

        void ShowSelected()
        {
            _formHost.Controls.Clear();
            var ts = Current; if (ts == null) return;
            var fb = new FormBuilder();
            fb.Changed += () => { Mark(); ReloadGraphics(); };
            _formHost.Controls.Add(fb.Build(ts, "Passages", "Priorities", "TerrainTags", "Id"));
            ReloadGraphics();
        }

        void ReloadGraphics()
        {
            if (Graphics != null) Graphics.Dispose();
            Graphics = new TileGraphics(_p, Current);
            _canvas.Width = 256; _canvas.Height = (Graphics.TileRows + 1) * 32;
            _canvas.Invalidate();
        }

        public void Click(int id, bool left)
        {
            var ts = Current; if (ts == null) return;
            ts.EnsureSize(Graphics.TileRows);
            if (id >= ts.Passages.Count) return;
            switch (_mode)
            {
                case Mode.Passage: ts.Passages[id] = (ts.Passages[id] & 0x0f) == 0x0f ? ts.Passages[id] & ~0x0f : ts.Passages[id] | 0x0f; break;
                case Mode.Priority: ts.Priorities[id] = Math.Max(0, Math.Min(5, ts.Priorities[id] + (left ? 1 : -1))); break;
                case Mode.Terrain: ts.TerrainTags[id] = Math.Max(0, Math.Min(17, ts.TerrainTags[id] + (left ? 1 : -1))); break;
                case Mode.Bush: ts.Passages[id] ^= 0x40; break;
                case Mode.Counter: ts.Passages[id] ^= 0x80; break;
            }
            Mark(); _canvas.Invalidate();
        }

        public string Overlay(int id)
        {
            var ts = Current; if (ts == null) return "";
            switch (_mode)
            {
                case Mode.Passage: return (ts.Passage(id) & 0x0f) == 0x0f ? "✕" : (ts.Priority(id) > 0 ? "☆" : "○");
                case Mode.Priority: return ts.Priority(id).ToString();
                case Mode.Terrain: return ts.TerrainTag(id) == 0 ? "" : ts.TerrainTag(id).ToString();
                case Mode.Bush: return (ts.Passage(id) & 0x40) != 0 ? "B" : "";
                case Mode.Counter: return (ts.Passage(id) & 0x80) != 0 ? "C" : "";
            }
            return "";
        }

        public void Save() { Json.Json.ToFile(_p.DataFile("tilesets.json"), _p.Tilesets); Dirty = false; if (DirtyChanged != null) DirtyChanged(); }

        class TilesetCanvas : Control
        {
            readonly TilesetEditor _ed;
            public TilesetCanvas(TilesetEditor ed) { _ed = ed; DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; var tg = _ed.Graphics;
                g.Clear(Color.FromArgb(220, 220, 225));
                if (tg == null) return;
                g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                for (int i = 1; i < 8; i++) tg.Draw(g, i * 48 + 47, i * 32, 0);
                if (tg.TilesetImage != null) g.DrawImage(tg.TilesetImage, new Rectangle(0, 32, 256, tg.TilesetImage.Height), new Rectangle(0, 0, 256, tg.TilesetImage.Height), GraphicsUnit.Pixel);
                using (var f = new Font("Segoe UI", 11f, FontStyle.Bold))
                using (var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                    for (int row = 0; row <= tg.TileRows; row++)
                        for (int col = 0; col < 8; col++)
                        {
                            int id = Palette.IdAt(col, row);
                            string s = _ed.Overlay(id);
                            if (s == "") continue;
                            g.DrawString(s, f, shadow, col * 32 + 9, row * 32 + 7);
                            g.DrawString(s, f, s == "✕" ? Brushes.OrangeRed : Brushes.White, col * 32 + 8, row * 32 + 6);
                        }
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                int col = e.X / 32, row = e.Y / 32;
                if (col < 0 || col > 7 || row < 0) return;
                _ed.Click(Palette.IdAt(col, row), e.Button == MouseButtons.Left);
            }
        }
    }

    // =====================================================================================
    /// <summary>Thumbnails of images in a Graphics folder (or a file list for audio), with import / open.</summary>
    public class AssetBrowser : UserControl, IDocument
    {
        readonly Project _p;
        readonly string _dir;
        readonly ListView _view = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon };
        readonly ImageList _images = new ImageList { ImageSize = new Size(64, 64), ColorDepth = ColorDepth.Depth32Bit };
        readonly PictureBox _preview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(230, 230, 235) };
        readonly Label _details = new Label { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(6) };
        public string Title { get { return Path.GetFileName(_dir); } }
        public bool Dirty { get { return false; } }
        public void Save() { }

        public AssetBrowser(Project p, string dir)
        {
            _p = p; _dir = dir; Dock = DockStyle.Fill;
            _view.LargeImageList = _images;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(4) };
            var imp = new Button { Text = "Import Files…", AutoSize = true }; imp.Click += (s, e) => ImportFiles();
            var open = new Button { Text = "Open Folder", AutoSize = true }; open.Click += (s, e) => MainForm.OpenInShell(_dir);
            var refresh = new Button { Text = "Refresh", AutoSize = true }; refresh.Click += (s, e) => Fill();
            bar.Controls.AddRange(new Control[] { imp, open, refresh, new Label { Text = Relative(dir), AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 8, 0, 0) } });
            var right = new Panel { Dock = DockStyle.Right, Width = 320 };
            right.Controls.Add(_preview); right.Controls.Add(_details);
            Controls.Add(_view); Controls.Add(new Splitter { Dock = DockStyle.Right }); Controls.Add(right); Controls.Add(bar);
            _view.SelectedIndexChanged += (s, e) => ShowSelected();
            _view.DoubleClick += (s, e) => { if (_view.SelectedItems.Count > 0) MainForm.OpenInShell((string)_view.SelectedItems[0].Tag); };
            AllowDrop = true; _view.AllowDrop = true;
            _view.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            _view.DragDrop += (s, e) => CopyIn((string[])e.Data.GetData(DataFormats.FileDrop));
            Fill();
        }

        string Relative(string d) { return d.StartsWith(_p.Root) ? d.Substring(_p.Root.Length).TrimStart(Path.DirectorySeparatorChar) : d; }

        static bool IsImage(string f) { var e = Path.GetExtension(f).ToLowerInvariant(); return e == ".png" || e == ".gif" || e == ".bmp" || e == ".jpg"; }

        void Fill()
        {
            _view.BeginUpdate();
            _view.Items.Clear(); _images.Images.Clear();
            if (Directory.Exists(_dir))
            {
                var files = Directory.GetFiles(_dir).Where(f => !f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList();
                bool images = files.Take(400).Any(IsImage);
                _view.View = images ? View.LargeIcon : View.List;
                foreach (var f in files.Take(2000))
                {
                    var item = new ListViewItem(Path.GetFileNameWithoutExtension(f)) { Tag = f };
                    if (images && IsImage(f))
                    {
                        var img = Theme.LoadImage(f);
                        if (img != null) { _images.Images.Add(Thumb(img)); img.Dispose(); item.ImageIndex = _images.Images.Count - 1; }
                    }
                    _view.Items.Add(item);
                }
            }
            _view.EndUpdate();
        }

        static Image Thumb(Image img)
        {
            var b = new Bitmap(64, 64);
            using (var g = System.Drawing.Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                float s = Math.Min(64f / img.Width, 64f / img.Height);
                if (s > 2) s = (float)Math.Floor(s);
                int w = Math.Max(1, (int)(img.Width * s)), h = Math.Max(1, (int)(img.Height * s));
                g.DrawImage(img, (64 - w) / 2, (64 - h) / 2, w, h);
            }
            return b;
        }

        void ShowSelected()
        {
            if (_view.SelectedItems.Count == 0) return;
            string f = (string)_view.SelectedItems[0].Tag;
            var old = _preview.Image;
            _preview.Image = IsImage(f) ? Theme.LoadImage(f) : null;
            if (old != null) old.Dispose();
            var fi = new FileInfo(f);
            _details.Text = Path.GetFileName(f) + "\n" + (fi.Length / 1024.0).ToString("0.0") + " KB" + (_preview.Image != null ? "   " + _preview.Image.Width + "×" + _preview.Image.Height + " px" : "") + "\nUse this name (without extension) in events and data.";
        }

        void ImportFiles()
        {
            using (var d = new OpenFileDialog { Multiselect = true, Title = "Import into " + Relative(_dir) })
                if (d.ShowDialog(this) == DialogResult.OK) CopyIn(d.FileNames);
        }

        void CopyIn(string[] files)
        {
            Directory.CreateDirectory(_dir);
            foreach (var f in files) if (File.Exists(f)) File.Copy(f, Path.Combine(_dir, Path.GetFileName(f)), true);
            Fill();
        }
    }

    // =====================================================================================
    /// <summary>Plain text / C# / JSON editor with light syntax colouring, find and save.</summary>
    public class TextEditor : UserControl, IDocument
    {
        readonly string _path;
        readonly RichTextBox _box = new RichTextBox { Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 10f), AcceptsTab = true, WordWrap = false, DetectUrls = false, BorderStyle = BorderStyle.None };
        readonly TextBox _find = new TextBox { Width = 200 };
        bool _loading;
        public string Title { get { return Path.GetFileName(_path); } }
        public bool Dirty { get; private set; }
        public event Action DirtyChanged;

        static readonly string[] CsKeywords = { "using", "namespace", "class", "public", "private", "protected", "internal", "static", "void", "return", "if", "else", "for", "foreach", "while", "new", "var", "int", "string", "bool", "object", "true", "false", "null", "in", "switch", "case", "break", "default", "double", "List", "this", "override", "virtual" };

        public TextEditor(string path)
        {
            _path = path; Dock = DockStyle.Fill;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(4) };
            var save = new Button { Text = "Save", AutoSize = true }; save.Click += (s, e) => Save();
            var findBtn = new Button { Text = "Find next", AutoSize = true }; findBtn.Click += (s, e) => FindNext();
            var ext = new Button { Text = "Open in external editor", AutoSize = true }; ext.Click += (s, e) => MainForm.OpenInShell(_path);
            bar.Controls.AddRange(new Control[] { save, new Label { Text = "Find:", AutoSize = true, Margin = new Padding(12, 8, 2, 0) }, _find, findBtn, ext, new Label { Text = path, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 8, 0, 0) } });
            Controls.Add(_box); Controls.Add(bar);
            _loading = true;
            _box.Text = File.ReadAllText(path, Encoding.UTF8);
            Highlight();
            _loading = false;
            _box.TextChanged += (s, e) => { if (!_loading && !Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } };
            Action rehighlight = () => { if (!IsDisposed) Highlight(); };
            Theme.Changed += rehighlight;
            Disposed += (s, e) => Theme.Changed -= rehighlight;
            _find.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { FindNext(); e.SuppressKeyPress = true; } };
        }

        void Highlight()
        {
            if (_box.TextLength > 200000) return;
            bool wasLoading = _loading; _loading = true;
            string ext = Path.GetExtension(_path).ToLowerInvariant();
            string t = _box.Text;
            bool dark = Theme.Dark;
            _box.SelectAll(); _box.SelectionColor = Theme.Text;
            if (ext == ".cs")
            {
                Tint(@"\b(" + string.Join("|", CsKeywords) + @")\b", dark ? System.Drawing.Color.FromArgb(86, 156, 214) : System.Drawing.Color.Blue, t);
                Tint("\"(\\\\.|[^\"\\\\])*\"", dark ? System.Drawing.Color.FromArgb(206, 145, 120) : System.Drawing.Color.FromArgb(163, 21, 21), t);
                Tint(@"//[^\n]*", dark ? System.Drawing.Color.FromArgb(106, 153, 85) : System.Drawing.Color.Green, t);
                Tint(@"\[[A-Z]\w*\(", dark ? System.Drawing.Color.FromArgb(78, 201, 176) : System.Drawing.Color.Teal, t);
            }
            else if (ext == ".json")
            {
                Tint("\"(\\\\.|[^\"\\\\])*\"\\s*:", dark ? System.Drawing.Color.FromArgb(156, 220, 254) : System.Drawing.Color.FromArgb(4, 81, 165), t);
                Tint(@"\b(true|false|null)\b", dark ? System.Drawing.Color.FromArgb(86, 156, 214) : System.Drawing.Color.Blue, t);
            }
            _box.Select(0, 0);
            _loading = wasLoading;
        }

        void Tint(string pattern, Color c, string text)
        {
            foreach (Match m in Regex.Matches(text, pattern)) { _box.Select(m.Index, m.Length); _box.SelectionColor = c; }
        }

        void FindNext()
        {
            if (_find.Text.Length == 0) return;
            int start = _box.SelectionStart + _box.SelectionLength;
            int i = _box.Text.IndexOf(_find.Text, start, StringComparison.OrdinalIgnoreCase);
            if (i < 0) i = _box.Text.IndexOf(_find.Text, 0, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) { _box.Select(i, _find.Text.Length); _box.ScrollToCaret(); _box.Focus(); }
        }

        public void Save()
        {
            if (_path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try { Json.Json.Parse(_box.Text); }
                catch (Exception ex) { MessageBox.Show(this, "This JSON has a mistake and was not saved:\n" + ex.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            File.WriteAllText(_path, _box.Text.Replace("\r\n", "\n"), new UTF8Encoding(false));
            Dirty = false; if (DirtyChanged != null) DirtyChanged();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { _find.Focus(); _find.SelectAll(); return true; }
            if (keyData == Keys.F3) { FindNext(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // =====================================================================================
    /// <summary>Pick an Essentials folder and a destination, then convert in the background.</summary>
    public class ImportDialog : Form
    {
        readonly TextBox _src = new TextBox { Width = 440 }, _dst = new TextBox { Width = 440 };
        readonly TextBox _log = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Font = new Font(FontFamily.GenericMonospace, 9f), Dock = DockStyle.Fill };
        readonly Button _go = new Button { Text = "Import", Width = 100, Height = 30 };
        readonly CheckBox _assets = new CheckBox { Text = "Copy graphics, audio and fonts", Checked = true, AutoSize = true };
        public string ResultFolder { get; private set; }

        public ImportDialog()
        {
            Text = "Import a Pokémon Essentials project"; Width = 720; Height = 560; StartPosition = FormStartPosition.CenterParent;
            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 150, ColumnCount = 3, Padding = new Padding(8) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            var b1 = new Button { Text = "Browse…" }; b1.Click += (s, e) => Browse(_src, true);
            var b2 = new Button { Text = "Browse…" }; b2.Click += (s, e) => Browse(_dst, false);
            top.Controls.Add(new Label { Text = "Essentials folder\n(contains Game.exe, PBS)", AutoSize = true }); top.Controls.Add(_src); top.Controls.Add(b1);
            top.Controls.Add(new Label { Text = "New project folder", AutoSize = true }); top.Controls.Add(_dst); top.Controls.Add(b2);
            top.Controls.Add(new Label()); top.Controls.Add(_assets);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var close = new Button { Text = "Close", Width = 100, Height = 30, DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(close); bottom.Controls.Add(_go);
            Controls.Add(_log); Controls.Add(top); Controls.Add(bottom);
            _src.TextChanged += (s, e) => { if (_dst.Text.Length == 0 || _dst.Tag != null) { _dst.Text = Path.Combine(Path.GetDirectoryName(_src.Text.TrimEnd('\\', '/')) ?? "", Path.GetFileName(_src.Text.TrimEnd('\\', '/')) + " (PokeCreator)"); _dst.Tag = "auto"; } };
            _go.Click += (s, e) => { if (ResultFolder != null) { DialogResult = DialogResult.OK; Close(); } else Run(); };
            _log.Text = "PokeCreator converts:\r\n  • PBS files → Pokémon, moves, items, abilities, types, trainers, encounters, town map, dexes\r\n  • Data/*.rxdata → maps, events, tilesets, switch & variable names\r\n  • Graphics, Audio and Fonts are copied\r\nYour Essentials folder is not changed.\r\n";
        }

        void Browse(TextBox tb, bool source)
        {
            using (var d = new FolderBrowserDialog { Description = source ? "Select your Essentials project folder" : "Select (or create) the folder for the new project" })
                if (d.ShowDialog(this) == DialogResult.OK) { tb.Text = d.SelectedPath; if (!source) tb.Tag = null; }
        }

        void Run()
        {
            string src = _src.Text.Trim(), dst = _dst.Text.Trim();
            if (!Directory.Exists(src)) { MessageBox.Show(this, "Choose your Essentials folder first."); return; }
            if (dst.Length == 0) { MessageBox.Show(this, "Choose where to create the new project."); return; }
            if (Project.IsProjectFolder(dst) && MessageBox.Show(this, "A PokeCreator project already exists there. Overwrite its data?", "Import", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            _go.Enabled = false; _log.Clear();
            bool copy = _assets.Checked;
            var t = new Thread(() =>
            {
                var imp = new EssentialsImporter { CopyAssets = copy, Log = line => BeginInvoke(new Action(() => _log.AppendText(line + "\r\n"))) };
                try
                {
                    imp.Import(src, dst);
                    BeginInvoke(new Action(() =>
                    {
                        ResultFolder = dst;
                        _log.AppendText("\r\nDone! Click 'Open project' to start editing.\r\n");
                        _go.Text = "Open project"; _go.Enabled = true;
                    }));
                }
                catch (Exception ex) { BeginInvoke(new Action(() => { _log.AppendText("\r\nImport failed: " + ex.Message + "\r\n"); _go.Enabled = true; })); }
            }) { IsBackground = true };
            t.Start();
        }

    }
}
