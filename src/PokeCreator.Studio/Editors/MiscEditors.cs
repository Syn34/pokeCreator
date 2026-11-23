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
    /// <summary>
    /// Tilesets: a folder of sheets (one image per area) plus autotiles, with per-tile passability,
    /// priority and terrain tags. Maps using a tileset can paint from every sheet in its folder.
    /// </summary>
    public class TilesetEditor : UserControl, IDocument
    {
        enum Mode { Passage, Priority, Terrain, Bush, Counter }
        readonly Project _p;
        readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        readonly ListBox _sheets = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        readonly Label _sheetInfo = new Label { Dock = DockStyle.Bottom, Height = 60, ForeColor = Color.DimGray, Padding = new Padding(2) };
        readonly List<int> _sheetSlots = new List<int>();
        readonly Panel _formHost = new Panel { Dock = DockStyle.Top, Height = 200 };
        readonly Panel _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(60, 62, 70) };
        readonly TilesetCanvas _canvas;
        Mode _mode = Mode.Passage;
        public int Slot { get; private set; }
        public string Title { get { return "Tilesets"; } }
        public bool Dirty { get; private set; }
        public event Action DirtyChanged;

        public TilesetEditor(Project p)
        {
            _p = p; Dock = DockStyle.Fill;
            _canvas = new TilesetCanvas(this);
            _scroll.Controls.Add(_canvas);

            // Left: tilesets
            var left = new Panel { Dock = DockStyle.Left, Width = 220 };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 140, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(2) };
            var fromFolder = new Button { Text = "New Tileset Folder…", AutoSize = true };
            fromFolder.Click += (s, e) => { var ts = TilesetImport.PickFolder(FindForm(), _p); if (ts == null) return; Fill(); _list.SelectedItem = ts; Mark(); };
            var fromImage = new Button { Text = "New From Image…", AutoSize = true };
            fromImage.Click += (s, e) =>
            {
                string name = TilesetImport.PickTileset(FindForm(), _p);
                if (name == null) return;
                int id = _p.Tilesets.Count == 0 ? 1 : _p.Tilesets.Max(t => t.Id) + 1;
                _p.Tilesets.Add(new TilesetData { Id = id, Name = name, TilesetName = name });
                Fill(); _list.SelectedIndex = _list.Items.Count - 1; Mark();
            };
            var add = new Button { Text = "New Empty", AutoSize = true };
            add.Click += (s, e) => { int id = _p.Tilesets.Count == 0 ? 1 : _p.Tilesets.Max(t => t.Id) + 1; _p.Tilesets.Add(new TilesetData { Id = id, Name = "New tileset" }); Fill(); _list.SelectedIndex = _list.Items.Count - 1; Mark(); };
            var makeDefault = new Button { Text = "Make Default for New Projects", AutoSize = true };
            makeDefault.Click += (s, e) => MakeDefault();
            var planes = new Button { Text = "Panorama && Fog…", AutoSize = true };
            planes.Click += (s, e) => EditPlanes();
            bar.Controls.AddRange(new Control[] { fromFolder, fromImage, add, makeDefault, planes });
            left.Controls.Add(_list); left.Controls.Add(bar);
            left.Controls.Add(new Label { Text = "Tilesets", Dock = DockStyle.Top, Height = 22, Font = Theme.HeaderFont });

            // Middle: sheets (areas) in the selected tileset's folder
            var mid = new Panel { Dock = DockStyle.Left, Width = 190 };
            var sheetBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 140, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(2) };
            var addSheet = new Button { Text = "Add Area Image…", AutoSize = true }; addSheet.Click += (s, e) => AddSheet();
            var replace = new Button { Text = "Replace Image…", AutoSize = true }; replace.Click += (s, e) => ReplaceSheet();
            var refresh = new Button { Text = "Refresh Folder", AutoSize = true }; refresh.Click += (s, e) => RefreshFolder(true);
            var open = new Button { Text = "Open Folder", AutoSize = true }; open.Click += (s, e) => { var d = FolderPath(); if (d != null) { Directory.CreateDirectory(d); MainForm.OpenInShell(d); } };
            sheetBar.Controls.AddRange(new Control[] { addSheet, replace, refresh, open });
            mid.Controls.Add(_sheets); mid.Controls.Add(_sheetInfo); mid.Controls.Add(sheetBar);
            mid.Controls.Add(new Label { Text = "Areas (sheets)", Dock = DockStyle.Top, Height = 22, Font = Theme.HeaderFont });
            _sheets.SelectedIndexChanged += (s, e) => { if (_sheets.SelectedIndex >= 0) { Slot = _sheetSlots[_sheets.SelectedIndex]; SizeCanvas(); } };

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
            imageBar.Controls.Add(autotile);
            imageBar.Controls.Add(new Label { Text = "Maps using this tileset can paint from every area. 16×16 images are doubled and wide images re-arranged to 8 tiles across.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 8, 0, 0) });
            var help = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray, Text = "Left-click to raise/toggle, right-click to lower. Terrain: 1 ledge, 2 grass, 5-9 water, 10 tall grass, 12 ice, 15 bridge." };

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_scroll); right.Controls.Add(help); right.Controls.Add(modes); right.Controls.Add(imageBar); right.Controls.Add(_formHost);
            Controls.Add(right); Controls.Add(new Splitter { Dock = DockStyle.Left }); Controls.Add(mid); Controls.Add(new Splitter { Dock = DockStyle.Left }); Controls.Add(left);
            _list.SelectedIndexChanged += (s, e) => ShowSelected();
            Fill();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        void Fill() { var sel = Current; _list.Items.Clear(); foreach (var t in _p.Tilesets) _list.Items.Add(t); if (sel != null && _list.Items.Contains(sel)) _list.SelectedItem = sel; }
        void Mark() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }
        public TilesetData Current { get { return _list.SelectedItem as TilesetData; } }
        public TileGraphics Graphics;

        string FolderPath()
        {
            var ts = Current; if (ts == null) return null;
            if (string.IsNullOrEmpty(ts.Folder)) return null;
            return Path.Combine(_p.TilesetsDir, ts.Folder);
        }

        void ShowSelected()
        {
            _formHost.Controls.Clear();
            var ts = Current; if (ts == null) return;
            RefreshFolder(false);
            var fb = new FormBuilder();
            fb.Changed += () => { Mark(); RefreshFolder(false); };
            _formHost.Controls.Add(fb.Build(ts, "Passages", "Priorities", "TerrainTags", "Id", "Sheets"));
        }

        /// <summary>Picks up new images in the folder (converting any that need it) and reloads the graphics.</summary>
        void RefreshFolder(bool tell)
        {
            var ts = Current; if (ts == null) return;
            var notes = new List<string>();
            string dir = FolderPath();
            if (dir != null && Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".png" && ext != ".bmp" && ext != ".gif" && ext != ".jpg") continue;
                    try { var n = TilesetImport.ConvertInPlace(f); if (n != null) notes.Add(n); }
                    catch (Exception ex) { notes.Add(ex.Message); }
                }
                var added = ts.SyncFolder(_p.TilesetsDir);
                if (added.Count > 0) { Mark(); notes.Insert(0, "New areas: " + string.Join(", ", added.Select(TilesetData.LabelOf))); }
            }
            int keep = Slot;
            if (Graphics != null) Graphics.Dispose();
            Graphics = new TileGraphics(_p, ts);
            _sheets.BeginUpdate();
            _sheets.Items.Clear(); _sheetSlots.Clear();
            foreach (var sh in Graphics.Sheets) { _sheetSlots.Add(sh.Key); _sheets.Items.Add(sh.Value); }
            _sheets.EndUpdate();
            int k = _sheetSlots.IndexOf(keep);
            if (k < 0) k = _sheetSlots.Count > 0 ? 0 : -1;
            Slot = k >= 0 ? _sheetSlots[k] : 0;
            if (k >= 0) _sheets.SelectedIndex = k;
            _sheetInfo.Text = string.IsNullOrEmpty(ts.Folder)
                ? "This tileset is a single image. Set Folder (above) to a folder in Graphics/Tilesets to use one image per area."
                : "Folder: Graphics/Tilesets/" + ts.Folder + "\nAdd images to it for more areas.";
            SizeCanvas();
            if (tell) MessageBox.Show(this, notes.Count == 0 ? "The folder is up to date." : string.Join("\n", notes.Take(15)), "Refresh Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void SizeCanvas()
        {
            _canvas.Width = 256; _canvas.Height = (Graphics == null ? 1 : Graphics.Rows(Slot) + 1) * 32;
            _scroll.AutoScrollPosition = Point.Empty;
            _canvas.Invalidate();
        }

        /// <summary>Adds an image to the tileset's folder as a new area (creating the folder if the tileset has none).</summary>
        void AddSheet()
        {
            var ts = Current; if (ts == null) return;
            using (var dlg = new OpenFileDialog { Title = "Add Area Image", Filter = TilesetImport.Filter, Multiselect = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (string.IsNullOrEmpty(ts.Folder))
                {
                    string folder = new string((ts.Name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
                    if (folder.Length == 0) folder = "Tileset " + ts.Id;
                    ts.Folder = folder;
                }
                string dir = FolderPath();
                Directory.CreateDirectory(dir);
                var notes = new List<string>();
                foreach (var f in dlg.FileNames)
                {
                    string target = Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + ".png");
                    if (File.Exists(target) && MessageBox.Show(this, Path.GetFileName(target) + " is already in the folder. Replace it?", "Add Area Image", MessageBoxButtons.YesNo) != DialogResult.Yes) continue;
                    try
                    {
                        using (var img = (Bitmap)Theme.LoadImage(f))
                        {
                            if (img == null) { notes.Add(Path.GetFileName(f) + ": couldn't be read."); continue; }
                            if (TilesetImport.IsEngineSheet(img.Width, img.Height)) img.Save(target, System.Drawing.Imaging.ImageFormat.Png);
                            else { string n; using (var c = TilesetImport.ToEngineTileset(img, TilesetImport.GuessTileSize(img.Width), out n)) c.Save(target, System.Drawing.Imaging.ImageFormat.Png); notes.Add(Path.GetFileName(f) + ": " + n.Trim()); }
                        }
                    }
                    catch (Exception ex) { notes.Add(Path.GetFileName(f) + ": " + ex.Message); }
                }
                Mark();
                ShowSelected();
                if (notes.Count > 0) MessageBox.Show(this, string.Join("\n", notes), "Add Area Image", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>Replaces the selected area's image, keeping its tiles' settings and the maps that use it.</summary>
        void ReplaceSheet()
        {
            var ts = Current; if (ts == null) return;
            string current = _p.FindAsset("Graphics/Tilesets", ts.SheetImage(Slot));
            using (var dlg = new OpenFileDialog { Title = "Replace Image", Filter = TilesetImport.Filter })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (var img = (Bitmap)Theme.LoadImage(dlg.FileName))
                    {
                        if (img == null) throw new InvalidDataException("Couldn't read the image.");
                        string n = "";
                        Bitmap conv = TilesetImport.IsEngineSheet(img.Width, img.Height) ? new Bitmap(img) : TilesetImport.ToEngineTileset(img, TilesetImport.GuessTileSize(img.Width), out n);
                        using (conv)
                        {
                            if (current == null)
                            {
                                string name = TilesetImport.SaveInto(_p, "Tilesets", Path.GetFileNameWithoutExtension(dlg.FileName), conv);
                                if (Slot == 0) ts.TilesetName = name; else ts.Sheet(Slot).Name = name;
                            }
                            else
                            {
                                if (Graphics != null) { Graphics.Dispose(); Graphics = null; }
                                conv.Save(Path.ChangeExtension(current, ".png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                        if (n != "") MessageBox.Show(this, n, "Replace Image", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    Mark(); ShowSelected();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Replace Image", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        /// <summary>The tileset's panorama (behind the map) and fog (over it), as in RPG Maker XP.</summary>
        void EditPlanes()
        {
            var ts = _list.SelectedItem as TilesetData;
            if (ts == null) return;
            string[] keep = { "PanoramaName", "PanoramaHue", "FogName", "FogHue", "FogOpacity", "FogBlend", "FogZoom", "FogSx", "FogSy" };
            var hide = typeof(TilesetData).GetProperties().Select(pr => pr.Name).Where(n => !keep.Contains(n)).ToArray();
            using (var f = new Form { Text = "Panorama and fog — " + ts.Name, Width = 520, Height = 420, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false })
            {
                var fb = new FormBuilder();
                fb.Changed += Mark;
                var form = fb.Build(ts, hide);
                var ok = new Button { Text = "Close", Dock = DockStyle.Bottom, Height = 30, DialogResult = DialogResult.OK };
                f.Controls.Add(form); f.Controls.Add(ok);
                f.AcceptButton = ok;
                f.ShowDialog(FindForm());
            }
        }

        void MakeDefault()
        {
            var ts = Current; if (ts == null) return;
            try
            {
                string dest = DefaultTilesets.MakeDefault(_p, ts);
                MessageBox.Show(this, "New projects will now start with \"" + ts.Name + "\" (images, autotiles and passability), and their first map will use it.\n\nSaved in:\n" + dest, "Make Default", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't save the default tileset:\n" + ex.Message, "Make Default", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        public void Click(int id, bool left)
        {
            var ts = Current; if (ts == null) return;
            if (Graphics != null) ts.EnsureSize(Slot, Graphics.Rows(Slot));
            int pass = ts.Passage(id);
            switch (_mode)
            {
                case Mode.Passage: ts.SetPassage(id, (pass & 0x0f) == 0x0f ? pass & ~0x0f : pass | 0x0f); break;
                case Mode.Priority: ts.SetPriority(id, Math.Max(0, Math.Min(5, ts.Priority(id) + (left ? 1 : -1)))); break;
                case Mode.Terrain: ts.SetTerrainTag(id, Math.Max(0, Math.Min(17, ts.TerrainTag(id) + (left ? 1 : -1)))); break;
                case Mode.Bush: ts.SetPassage(id, pass ^ 0x40); break;
                case Mode.Counter: ts.SetPassage(id, pass ^ 0x80); break;
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

        protected override void Dispose(bool disposing) { if (disposing && Graphics != null) Graphics.Dispose(); base.Dispose(disposing); }

        class TilesetCanvas : Control, IWheelTarget
        {
            public void Wheel(int delta) { WheelScroll.ScrollParent(this, delta); }
            readonly TilesetEditor _ed;
            public TilesetCanvas(TilesetEditor ed) { _ed = ed; DoubleBuffered = true; SetStyle(ControlStyles.Selectable, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; var tg = _ed.Graphics;
                g.Clear(Color.FromArgb(220, 220, 225));
                if (tg == null) return;
                g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                for (int i = 1; i < 8; i++) tg.Draw(g, i * 48 + 47, i * 32, 0);
                var img = tg.Image(_ed.Slot);
                if (img != null) g.DrawImage(img, new Rectangle(0, 32, 256, img.Height), new Rectangle(0, 0, 256, img.Height), GraphicsUnit.Pixel);
                int rows = tg.Rows(_ed.Slot);
                int firstRow = Math.Max(0, e.ClipRectangle.Top / 32), lastRow = Math.Min(rows, e.ClipRectangle.Bottom / 32 + 1);
                using (var f = new Font("Segoe UI", 11f, FontStyle.Bold))
                using (var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                    for (int row = firstRow; row <= lastRow; row++)
                        for (int col = 0; col < 8; col++)
                        {
                            int id = Palette.IdAt(col, row, _ed.Slot);
                            string s = _ed.Overlay(id);
                            if (s == "") continue;
                            g.DrawString(s, f, shadow, col * 32 + 9, row * 32 + 7);
                            g.DrawString(s, f, s == "✕" ? Brushes.OrangeRed : Brushes.White, col * 32 + 8, row * 32 + 6);
                        }
            }
            protected override void OnMouseWheel(MouseEventArgs e) { WheelScroll.ToParent(this, e); }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                Focus();
                int col = e.X / 32, row = e.Y / 32;
                if (col < 0 || col > 7 || row < 0) return;
                _ed.Click(Palette.IdAt(col, row, _ed.Slot), e.Button == MouseButtons.Left);
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
            if (IsAudioFolder)
            {
                // Which files the game can play, above the Import Files bar
                var formats = new Label
                {
                    Dock = DockStyle.Top, AutoSize = false, Height = 40, Padding = new Padding(8, 4, 8, 2), ForeColor = Theme.SubText,
                    Text = "Supported audio: OGG (best: small, loops cleanly), WAV, MP3, MP4 / M4A, MIDI, FLAC, Opus.\n" +
                           "MP4 / M4A are converted the first time they play (a short pause once). MIDI sounds different on every PC, so OGG is safer for music."
                };
                Controls.Add(formats);
            }
            _view.SelectedIndexChanged += (s, e) => ShowSelected();
            _view.DoubleClick += (s, e) => { if (_view.SelectedItems.Count > 0) MainForm.OpenInShell((string)_view.SelectedItems[0].Tag); };
            AllowDrop = true; _view.AllowDrop = true;
            _view.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            _view.DragDrop += (s, e) => CopyIn((string[])e.Data.GetData(DataFormats.FileDrop));
            Fill();
        }

        string Relative(string d) { return d.StartsWith(_p.Root) ? d.Substring(_p.Root.Length).TrimStart(Path.DirectorySeparatorChar) : d; }

        bool IsAudioFolder
        {
            get
            {
                string rel = Relative(_dir).Replace('\\', '/');
                return rel.Equals("Audio", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("Audio/", StringComparison.OrdinalIgnoreCase);
            }
        }

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
            {
                if (IsAudioFolder) d.Filter = "Audio files (OGG, WAV, MP3, MP4, M4A, MIDI, FLAC, Opus)|*.ogg;*.wav;*.mp3;*.mp4;*.m4a;*.mid;*.midi;*.flac;*.opus|All files|*.*";
                if (d.ShowDialog(this) == DialogResult.OK) CopyIn(d.FileNames);
            }
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
        readonly CodeEditor _code = new CodeEditor { Dock = DockStyle.Fill };
        readonly TextBox _find = new TextBox { Width = 200 };
        readonly Label _pos = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 8, 0, 0) };
        public string Title { get { return Path.GetFileName(_path); } }
        public bool Dirty { get; private set; }
        public event Action DirtyChanged;

        /// <summary>Moves the caret to a line (1-based) and scrolls it into view.</summary>
        public void GoToLine(int line)
        {
            int start = _code.GetFirstCharIndexFromLine(Math.Max(0, line - 1));
            if (start < 0) return;
            _code.Select(start, 0);
            _code.ScrollToCaret();
            _code.Focus();
        }

        public TextEditor(string path)
        {
            _path = path; Dock = DockStyle.Fill;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(4) };
            var save = new Button { Text = "Save", AutoSize = true }; save.Click += (s, e) => Save();
            var findBtn = new Button { Text = "Find next", AutoSize = true }; findBtn.Click += (s, e) => FindNext();
            var ext = new Button { Text = "Open in external editor", AutoSize = true }; ext.Click += (s, e) => MainForm.OpenInShell(_path);
            bar.Controls.AddRange(new Control[] { save, new Label { Text = "Find:", AutoSize = true, Margin = new Padding(12, 8, 2, 0) }, _find, findBtn, ext, _pos, new Label { Text = path, AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 8, 0, 0) } });
            Controls.Add(_code); Controls.Add(bar);
            string ext2 = Path.GetExtension(path).ToLowerInvariant();
            _code.Language = ext2 == ".cs" ? "cs" : ext2 == ".json" ? "json" : "";
            _code.LoadText(File.ReadAllText(path, Encoding.UTF8));
            _code.TextEdited += (s, e) => { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } };
            _code.SelectionChanged += (s, e) => ShowPosition();
            _find.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { FindNext(); e.SuppressKeyPress = true; } };
            ShowPosition();
        }

        void ShowPosition()
        {
            var b = _code;
            int line = b.GetLineFromCharIndex(b.SelectionStart);
            int col = b.SelectionStart - b.GetFirstCharIndexFromLine(line);
            _pos.Text = "Ln " + (line + 1) + "   Ch " + (col + 1);
        }

        void FindNext()
        {
            var box = _code;
            if (_find.Text.Length == 0) return;
            int start = box.SelectionStart + box.SelectionLength;
            string all = box.Text;
            int i = all.IndexOf(_find.Text, Math.Min(start, all.Length), StringComparison.OrdinalIgnoreCase);
            if (i < 0) i = all.IndexOf(_find.Text, 0, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) { box.Select(i, _find.Text.Length); box.ScrollToCaret(); box.Focus(); }
        }

        public void Save()
        {
            string text = _code.Text;
            if (_path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                try { Json.Json.Parse(text); }
                catch (Exception ex) { MessageBox.Show(this, "This JSON has a mistake and was not saved:\n" + ex.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            File.WriteAllText(_path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
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
