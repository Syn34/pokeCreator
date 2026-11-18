using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Json;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    public enum MapTool { Pencil, Rectangle, Fill, Eraser }

    /// <summary>Map document: palette on the left, canvas in the middle, toolbar on top.</summary>
    public class MapEditor : UserControl, IDocument
    {
        readonly MainForm _main;
        readonly Project _p;
        public readonly int MapId;
        MapData _map;
        TileGraphics _tiles;
        readonly MapCanvas _canvas;
        readonly Palette _palette;
        readonly Label _info = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(236, 238, 242), Padding = new Padding(6, 0, 0, 0) };
        readonly List<ToolStripButton> _layerButtons = new List<ToolStripButton>();
        readonly List<ToolStripButton> _toolButtons = new List<ToolStripButton>();
        readonly Stack<string> _undo = new Stack<string>(), _redo = new Stack<string>();

        public bool Dirty { get; private set; }
        public string Title { get { var mi = _p.GetMapInfo(MapId); return "Map " + MapId.ToString("000") + (mi != null ? ": " + mi.Name : ""); } }
        public event Action DirtyChanged;

        public MapEditor(MainForm main, Project p, int mapId)
        {
            _main = main; _p = p; MapId = mapId;
            _map = p.LoadMap(mapId);
            if (_map == null) throw new Exception("Map " + mapId + " could not be loaded.");
            _tiles = new TileGraphics(p, p.GetTileset(_map.TilesetId));
            Dock = DockStyle.Fill;

            _canvas = new MapCanvas(this, p, _map, _tiles);
            var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(60, 62, 70) };
            scroller.Controls.Add(_canvas);
            _palette = new Palette(_tiles);
            var palScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(60, 62, 70) };
            palScroll.Controls.Add(_palette);
            var left = new Panel { Dock = DockStyle.Left, Width = 8 * 32 + 26 };
            left.Controls.Add(palScroll);
            left.Controls.Add(new Label { Text = "Tiles: click or drag to select", Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) });

            Controls.Add(scroller);
            Controls.Add(new Splitter { Dock = DockStyle.Left });
            Controls.Add(left);
            Controls.Add(_info);
            Controls.Add(BuildToolbar());
            SetLayer(0); SetTool(MapTool.Pencil);
            _canvas.Hover += (x, y) => ShowInfo(x, y);
        }

        ToolStrip BuildToolbar()
        {
            var t = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 2, 4, 2) };
            t.Items.Add(new ToolStripLabel("Layer:"));
            string[] names = { "1 Ground", "2 Middle", "3 Top", "Events" };
            for (int i = 0; i < 4; i++)
            {
                int layer = i;
                var b = new ToolStripButton(names[i]) { CheckOnClick = false, ToolTipText = i < 3 ? "Edit tile layer " + (i + 1) : "Place and edit events (NPCs, doors, items)" };
                b.Click += (s, e) => SetLayer(layer);
                _layerButtons.Add(b); t.Items.Add(b);
            }
            t.Items.Add(new ToolStripSeparator());
            foreach (MapTool tool in Enum.GetValues(typeof(MapTool)))
            {
                var tl = tool;
                var b = new ToolStripButton(tool.ToString()) { ToolTipText = tool == MapTool.Fill ? "Flood fill" : tool.ToString() };
                b.Click += (s, e) => SetTool(tl);
                _toolButtons.Add(b); t.Items.Add(b);
            }
            t.Items.Add(new ToolStripSeparator());
            var undo = new ToolStripButton("Undo") { ToolTipText = "Ctrl+Z" }; undo.Click += (s, e) => Undo(); t.Items.Add(undo);
            var redo = new ToolStripButton("Redo") { ToolTipText = "Ctrl+Y" }; redo.Click += (s, e) => Redo(); t.Items.Add(redo);
            t.Items.Add(new ToolStripSeparator());
            var zoom = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
            zoom.Items.AddRange(new object[] { "25%", "50%", "75%", "100%", "150%", "200%" });
            zoom.SelectedIndex = 3;
            zoom.SelectedIndexChanged += (s, e) => _canvas.SetZoom(int.Parse(((string)zoom.SelectedItem).TrimEnd('%')));
            t.Items.Add(new ToolStripLabel("Zoom")); t.Items.Add(zoom);
            var dim = new ToolStripButton("Dim other layers") { CheckOnClick = true, Checked = true };
            dim.CheckedChanged += (s, e) => { _canvas.DimOthers = dim.Checked; _canvas.Invalidate(); };
            t.Items.Add(dim);
            var grid = new ToolStripButton("Grid") { CheckOnClick = true };
            grid.CheckedChanged += (s, e) => { _canvas.ShowGrid = grid.Checked; _canvas.Invalidate(); };
            t.Items.Add(grid);
            var pass = new ToolStripButton("Passability") { CheckOnClick = true, ToolTipText = "Show which tiles block the player" };
            pass.CheckedChanged += (s, e) => { _canvas.ShowPassability = pass.Checked; _canvas.Invalidate(); };
            t.Items.Add(pass);
            t.Items.Add(new ToolStripSeparator());
            t.Items.Add(new ToolStripButton("Map Properties…", null, (s, e) => ShowProperties()));
            t.Items.Add(new ToolStripButton("▶ Playtest here", null, (s, e) => { var pos = PlaytestPosition(); _main.Playtest(new[] { "--map", MapId.ToString(), "--x", pos.X.ToString(), "--y", pos.Y.ToString() }); }) { ForeColor = Color.DarkGreen });
            return t;
        }

        public MapTool Tool { get; private set; }
        public int Layer { get; private set; }
        public Palette PaletteControl { get { return _palette; } }

        void SetLayer(int l)
        {
            Layer = l;
            for (int i = 0; i < _layerButtons.Count; i++) _layerButtons[i].Checked = i == l;
            _canvas.Invalidate();
        }

        void SetTool(MapTool t)
        {
            Tool = t;
            for (int i = 0; i < _toolButtons.Count; i++) _toolButtons[i].Checked = (MapTool)i == t;
        }

        void ShowInfo(int x, int y)
        {
            if (!_map.Valid(x, y)) { _info.Text = ""; return; }
            string s = "(" + x + ", " + y + ")";
            if (Layer < 3)
            {
                int id = _map.GetTile(x, y, Layer);
                var ts = _tiles.Tileset;
                s += "   tile " + id + (id >= 48 && id < 384 ? " (autotile " + (id / 48) + ")" : "") + "   priority " + ts.Priority(id) + "   terrain " + TerrainName(ts.TerrainTag(id)) + "   " + ((ts.Passage(id) & 0x0f) == 0x0f ? "blocked" : "passable");
            }
            var ev = _map.Events.FirstOrDefault(e => e.X == x && e.Y == y);
            if (ev != null) s += "   event " + ev.Id + ": " + ev.Name;
            s += "     Left: paint   Right: pick tile   Events layer: double-click to edit, right-click for menu";
            _info.Text = s;
        }

        static string TerrainName(int t) { return t >= 0 && t < TerrainTags.Names.Length ? TerrainTags.Names[t] : t.ToString(); }

        public void MarkDirty() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }

        // ------------------------------------------------------------------ undo
        public void Snapshot()
        {
            _undo.Push(Json.Json.Write(new UndoState { Layers = _map.Layers, Events = _map.Events }, false));
            if (_undo.Count > 60) { var keep = _undo.Take(60).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); }
            _redo.Clear();
        }

        class UndoState { public List<List<List<int>>> Layers { get; set; } public List<MapEvent> Events { get; set; } }

        void Restore(string snap)
        {
            var st = Json.Json.Deserialize<UndoState>(snap);
            _map.Layers = st.Layers; _map.Events = st.Events;
            _canvas.InvalidateAll(); MarkDirty();
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Push(Json.Json.Write(new UndoState { Layers = _map.Layers, Events = _map.Events }, false));
            Restore(_undo.Pop());
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Push(Json.Json.Write(new UndoState { Layers = _map.Layers, Events = _map.Events }, false));
            Restore(_redo.Pop());
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
            if (keyData == (Keys.Control | Keys.Y)) { Redo(); return true; }
            if (keyData == Keys.Delete && Layer == 3) { _canvas.DeleteSelectedEvent(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ------------------------------------------------------------------ properties / playtest
        public Point PlaytestPosition()
        {
            var sel = _canvas.SelectedCell;
            if (_map.Valid(sel.X, sel.Y)) return sel;
            return new Point(_map.Width / 2, _map.Height / 2);
        }

        public class MapSettings
        {
            [Field("Width (tiles)")] public int Width { get; set; }
            [Field("Height (tiles)")] public int Height { get; set; }
            [Field("Tileset ID", Help = "See Game Settings → Tilesets")] public int TilesetId { get; set; }
            [Field("Play music automatically", Group = "Audio")] public bool AutoplayBgm { get; set; }
            [Field("Music", Asset = "Audio/BGM", Group = "Audio")] public string Bgm { get; set; }
            [Field("Encounter step average", Group = "Encounters")] public int EncounterStep { get; set; }
            public MapProperties Properties { get; set; }
        }

        public void ShowProperties()
        {
            var s = new MapSettings { Width = _map.Width, Height = _map.Height, TilesetId = _map.TilesetId, AutoplayBgm = _map.AutoplayBgm, Bgm = _map.Bgm != null ? _map.Bgm.Name : "", EncounterStep = _map.EncounterStep };
            s.Properties = Json.Json.Deserialize<MapProperties>(Json.Json.Write(_map.Properties ?? new MapProperties()));
            using (var f = new Form { Text = Title + " — Properties", Width = 640, Height = 720, StartPosition = FormStartPosition.CenterParent })
            {
                var fb = new FormBuilder();
                var body = fb.Build(s, "Properties");
                var props = new FormBuilder().Build(s.Properties);
                var tabs = new TabControl { Dock = DockStyle.Fill };
                var t1 = new TabPage("Map"); t1.Controls.Add(body);
                var t2 = new TabPage("Pokémon settings"); t2.Controls.Add(props);
                tabs.TabPages.Add(t1); tabs.TabPages.Add(t2);
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
                f.Controls.Add(tabs); f.Controls.Add(ok); f.AcceptButton = ok;
                if (f.ShowDialog(this) != DialogResult.OK) return;
            }
            Snapshot();
            if (s.Width != _map.Width || s.Height != _map.Height) _map.Resize(Math.Max(1, Math.Min(500, s.Width)), Math.Max(1, Math.Min(500, s.Height)));
            if (s.TilesetId != _map.TilesetId)
            {
                _map.TilesetId = s.TilesetId;
                _tiles.Load(_p.GetTileset(s.TilesetId));
                _palette.Reload();
            }
            _map.AutoplayBgm = s.AutoplayBgm;
            if (_map.Bgm == null) _map.Bgm = new AudioFile();
            _map.Bgm.Name = s.Bgm ?? "";
            _map.EncounterStep = s.EncounterStep;
            _map.Properties = s.Properties;
            _canvas.Resized(); _canvas.InvalidateAll();
            MarkDirty();
        }

        public void Save()
        {
            _p.SaveMap(_map);
            Dirty = false;
            if (DirtyChanged != null) DirtyChanged();
        }

        public void SetPlayerStart(int x, int y)
        {
            _p.Settings.StartMap = MapId; _p.Settings.StartX = x; _p.Settings.StartY = y;
            Json.Json.ToFile(_p.DataFile("settings.json"), _p.Settings);
            _canvas.Invalidate();
        }

        public void EditEvent(MapEvent ev)
        {
            Snapshot();
            using (var dlg = new EventEditor(_p, _map, ev))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) { MarkDirty(); _canvas.Invalidate(); }
                else _undo.Pop();
            }
        }
    }

    /// <summary>Tileset palette: blank + 7 autotiles on the first row, then the tileset image.</summary>
    public class Palette : Control
    {
        readonly TileGraphics _tiles;
        Point _selStart, _selEnd;
        bool _dragging;
        public event Action SelectionChanged;

        public Palette(TileGraphics tiles)
        {
            _tiles = tiles;
            DoubleBuffered = true;
            Reload();
        }

        public void Reload()
        {
            Width = 8 * 32; Height = (1 + _tiles.TileRows) * 32;
            _selStart = _selEnd = new Point(0, 1);
            Invalidate();
        }

        public static int IdAt(int col, int row) { return row == 0 ? (col == 0 ? 0 : col * 48) : 384 + (row - 1) * 8 + col; }

        public Rectangle Selection
        {
            get
            {
                int x0 = Math.Min(_selStart.X, _selEnd.X), y0 = Math.Min(_selStart.Y, _selEnd.Y);
                int x1 = Math.Max(_selStart.X, _selEnd.X), y1 = Math.Max(_selStart.Y, _selEnd.Y);
                if (y0 == 0) y1 = 0; // autotile row selects one row at a time
                return new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            }
        }

        /// <summary>The selected brush: tile IDs [y][x].</summary>
        public int[,] Brush
        {
            get
            {
                var r = Selection;
                var b = new int[r.Height, r.Width];
                for (int y = 0; y < r.Height; y++) for (int x = 0; x < r.Width; x++) b[y, x] = IdAt(r.X + x, r.Y + y);
                return b;
            }
        }

        public void SelectId(int id)
        {
            Point p;
            if (id == 0) p = new Point(0, 0);
            else if (id < 384) p = new Point(id / 48, 0);
            else { int i = id - 384; p = new Point(i % 8, i / 8 + 1); }
            _selStart = _selEnd = p;
            Invalidate();
            var parent = Parent as ScrollableControl;
            if (parent != null) parent.ScrollControlIntoView(this);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _selStart = _selEnd = Cell(e.Location); _dragging = true; Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) { _selEnd = Cell(e.Location); Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; if (SelectionChanged != null) SelectionChanged(); }

        Point Cell(Point p) { return new Point(Math.Max(0, Math.Min(7, p.X / 32)), Math.Max(0, Math.Min(_tiles.TileRows, p.Y / 32))); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var checker = new HatchBrush(HatchStyle.LargeCheckerBoard, Color.FromArgb(205, 205, 210), Color.FromArgb(230, 230, 235)))
                g.FillRectangle(checker, ClientRectangle);
            for (int i = 1; i < 8; i++) _tiles.Draw(g, i * 48 + 47, i * 32, 0);
            g.DrawString("∅", Font, Brushes.Gray, 10, 9);
            if (_tiles.TilesetImage != null) g.DrawImage(_tiles.TilesetImage, new Rectangle(0, 32, 256, _tiles.TilesetImage.Height), new Rectangle(0, 0, 256, _tiles.TilesetImage.Height), GraphicsUnit.Pixel);
            g.DrawLine(Pens.DimGray, 0, 32, Width, 32);
            var r = Selection;
            var rect = new Rectangle(r.X * 32, r.Y * 32, r.Width * 32 - 1, r.Height * 32 - 1);
            using (var pen = new Pen(Color.White, 3)) g.DrawRectangle(pen, rect);
            using (var pen = new Pen(Color.Black, 1)) g.DrawRectangle(pen, rect);
        }
    }

    /// <summary>The map drawing surface. Tiles are cached in 16x16-tile chunks per layer.</summary>
    public class MapCanvas : Control
    {
        const int Chunk = 16;
        readonly MapEditor _ed;
        readonly Project _p;
        readonly MapData _map;
        readonly TileGraphics _tiles;
        readonly Dictionary<long, Bitmap> _chunks = new Dictionary<long, Bitmap>();
        readonly Dictionary<string, Bitmap> _sprites = new Dictionary<string, Bitmap>();
        int _zoom = 100;
        public bool DimOthers = true, ShowGrid, ShowPassability;
        Point _hover = new Point(-1, -1), _dragStart, _dragEnd;
        bool _painting, _rectDragging, _movingEvent;
        MapEvent _selectedEvent, _clipboard;
        public Point SelectedCell = new Point(-1, -1);
        public event Action<int, int> Hover;

        public MapCanvas(MapEditor ed, Project p, MapData map, TileGraphics tiles)
        {
            _ed = ed; _p = p; _map = map; _tiles = tiles;
            DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable, true);
            Resized();
        }

        float Scale { get { return _zoom / 100f; } }
        int CellPx { get { return (int)Math.Round(32 * Scale); } }

        public void SetZoom(int z) { _zoom = z; Resized(); Invalidate(); }
        public void Resized() { Width = Math.Max(1, (int)(_map.Width * 32 * Scale)); Height = Math.Max(1, (int)(_map.Height * 32 * Scale)); }

        public void InvalidateAll()
        {
            foreach (var b in _chunks.Values) b.Dispose();
            _chunks.Clear();
            Invalidate();
        }

        void InvalidateTile(int x, int y)
        {
            for (int l = 0; l < 3; l++)
            {
                long k = Key(l, x / Chunk, y / Chunk);
                Bitmap b; if (_chunks.TryGetValue(k, out b)) { b.Dispose(); _chunks.Remove(k); }
            }
        }

        static long Key(int layer, int cx, int cy) { return ((long)layer << 40) | ((long)cx << 20) | (long)cy; }

        Bitmap GetChunk(int layer, int cx, int cy)
        {
            long k = Key(layer, cx, cy);
            Bitmap b;
            if (_chunks.TryGetValue(k, out b)) return b;
            b = new Bitmap(Chunk * 32, Chunk * 32, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                for (int y = 0; y < Chunk; y++)
                    for (int x = 0; x < Chunk; x++)
                    {
                        int mx = cx * Chunk + x, my = cy * Chunk + y;
                        if (mx >= _map.Width || my >= _map.Height) continue;
                        int id = _map.GetTile(mx, my, layer);
                        if (id > 0) _tiles.Draw(g, id, x * 32, y * 32);
                    }
            }
            _chunks[k] = b;
            return b;
        }

        Bitmap Sprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Bitmap b;
            if (!_sprites.TryGetValue(name, out b)) { b = Theme.LoadImage(_p.FindAsset("Graphics/Characters", name)) as Bitmap; _sprites[name] = b; }
            return b;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.Black);
            float s = Scale;
            var clip = e.ClipRectangle;
            int cx0 = Math.Max(0, (int)(clip.Left / s) / (Chunk * 32)), cy0 = Math.Max(0, (int)(clip.Top / s) / (Chunk * 32));
            int cx1 = Math.Min((_map.Width - 1) / Chunk, (int)(clip.Right / s) / (Chunk * 32));
            int cy1 = Math.Min((_map.Height - 1) / Chunk, (int)(clip.Bottom / s) / (Chunk * 32));
            using (var dim = new ImageAttributes())
            {
                var cm = new ColorMatrix { Matrix33 = 0.45f };
                dim.SetColorMatrix(cm);
                for (int layer = 0; layer < 3; layer++)
                {
                    bool faded = DimOthers && _ed.Layer < 3 && layer != _ed.Layer;
                    for (int cy = cy0; cy <= cy1; cy++)
                        for (int cx = cx0; cx <= cx1; cx++)
                        {
                            var b = GetChunk(layer, cx, cy);
                            var dest = new Rectangle((int)(cx * Chunk * 32 * s), (int)(cy * Chunk * 32 * s), (int)Math.Ceiling(Chunk * 32 * s), (int)Math.Ceiling(Chunk * 32 * s));
                            if (faded) g.DrawImage(b, dest, 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, dim);
                            else g.DrawImage(b, dest);
                        }
                }
            }
            int c = CellPx;
            if (ShowPassability) DrawPassability(g, c);
            DrawEvents(g, c);
            // Player start marker
            if (_p.Settings.StartMap == _map.Id)
            {
                var r = new Rectangle(_p.Settings.StartX * c, _p.Settings.StartY * c, c - 1, c - 1);
                using (var pen = new Pen(Color.Lime, 2)) g.DrawRectangle(pen, r);
                g.DrawString("S", Font, Brushes.Lime, r.X + 2, r.Y + 1);
            }
            if (ShowGrid)
                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255)))
                {
                    for (int x = 0; x <= _map.Width; x++) g.DrawLine(pen, x * c, 0, x * c, _map.Height * c);
                    for (int y = 0; y <= _map.Height; y++) g.DrawLine(pen, 0, y * c, _map.Width * c, y * c);
                }
            // Hover / brush outline
            if (_map.Valid(_hover.X, _hover.Y))
            {
                Rectangle r;
                if (_rectDragging) r = CellRect(_dragStart, _dragEnd);
                else if (_ed.Layer < 3) { var br = _ed.PaletteControl.Selection; r = new Rectangle(_hover.X, _hover.Y, _ed.Tool == MapTool.Pencil ? br.Width : 1, _ed.Tool == MapTool.Pencil ? br.Height : 1); }
                else r = new Rectangle(_hover.X, _hover.Y, 1, 1);
                var pr = new Rectangle(r.X * c, r.Y * c, r.Width * c - 1, r.Height * c - 1);
                using (var pen = new Pen(Color.White, 2)) g.DrawRectangle(pen, pr);
                g.DrawRectangle(Pens.Black, pr);
            }
        }

        void DrawPassability(Graphics g, int c)
        {
            using (var block = new SolidBrush(Color.FromArgb(90, 255, 0, 0)))
                for (int y = 0; y < _map.Height; y++)
                    for (int x = 0; x < _map.Width; x++)
                    {
                        bool blocked = false;
                        for (int z = 2; z >= 0; z--)
                        {
                            int id = _map.GetTile(x, y, z);
                            if (id <= 0) continue;
                            if ((_tiles.Tileset.Passage(id) & 0x0f) == 0x0f) { blocked = true; break; }
                            if (_tiles.Tileset.Priority(id) == 0) break;
                        }
                        if (blocked) g.FillRectangle(block, x * c, y * c, c, c);
                    }
        }

        void DrawEvents(Graphics g, int c)
        {
            bool active = _ed.Layer == 3;
            using (var fill = new SolidBrush(Color.FromArgb(active ? 70 : 40, 255, 255, 255)))
            using (var font = new Font("Segoe UI", Math.Max(6f, 7f * Scale)))
                foreach (var ev in _map.Events)
                {
                    var r = new Rectangle(ev.X * c, ev.Y * c, c - 1, c - 1);
                    var pg = ev.Pages.Count > 0 ? ev.Pages[0] : null;
                    if (active) g.FillRectangle(fill, r);
                    if (pg != null && pg.Graphic.TileId > 0) _tiles.Draw(g, pg.Graphic.TileId, r.X, r.Y, c);
                    else if (pg != null) TileGraphics.DrawCharacter(g, Sprite(pg.Graphic.CharacterName), pg.Graphic.Direction, pg.Graphic.Pattern, r.X, r.Y, c);
                    if (active || pg == null || string.IsNullOrEmpty(pg.Graphic.CharacterName))
                    {
                        using (var pen = new Pen(ev == _selectedEvent ? Color.Yellow : Color.White, ev == _selectedEvent ? 2 : 1)) g.DrawRectangle(pen, r);
                        if (active && c >= 24) g.DrawString(ev.Id.ToString(), font, Brushes.White, r.X + 1, r.Y + 1);
                    }
                }
        }

        static Rectangle CellRect(Point a, Point b)
        {
            int x0 = Math.Min(a.X, b.X), y0 = Math.Min(a.Y, b.Y);
            return new Rectangle(x0, y0, Math.Abs(a.X - b.X) + 1, Math.Abs(a.Y - b.Y) + 1);
        }

        Point CellAt(Point p) { int c = CellPx; return new Point(p.X / c, p.Y / c); }

        // ------------------------------------------------------------------ editing
        void Put(int x, int y, int id)
        {
            if (!_map.Valid(x, y)) return;
            int layer = _ed.Layer;
            _map.SetTile(x, y, layer, id);
            TileGraphics.FixAutotiles(_map, layer, x, y); // neighbouring autotiles reshape around any change
            for (int yy = y - 1; yy <= y + 1; yy++) for (int xx = x - 1; xx <= x + 1; xx++) if (_map.Valid(xx, yy)) InvalidateTile(xx, yy);
        }

        void Stamp(int x, int y)
        {
            var b = _ed.Tool == MapTool.Eraser ? new int[1, 1] : _ed.PaletteControl.Brush;
            for (int by = 0; by < b.GetLength(0); by++) for (int bx = 0; bx < b.GetLength(1); bx++) Put(x + bx, y + by, b[by, bx]);
            int c = CellPx;
            Invalidate(new Rectangle((x - 2) * c, (y - 2) * c, (b.GetLength(1) + 4) * c, (b.GetLength(0) + 4) * c));
            _ed.MarkDirty();
        }

        void FillRect(Rectangle r)
        {
            var b = _ed.Tool == MapTool.Eraser ? new int[1, 1] : _ed.PaletteControl.Brush;
            int bh = b.GetLength(0), bw = b.GetLength(1);
            for (int y = r.Top; y < r.Bottom; y++) for (int x = r.Left; x < r.Right; x++) Put(x, y, b[(y - r.Top) % bh, (x - r.Left) % bw]);
            Invalidate(); _ed.MarkDirty();
        }

        void Flood(int sx, int sy)
        {
            int layer = _ed.Layer;
            int target = _map.GetTile(sx, sy, layer);
            int kindTarget = target >= 48 && target < 384 ? target / 48 : -1;
            var b = _ed.PaletteControl.Brush;
            int bh = b.GetLength(0), bw = b.GetLength(1);
            Func<int, bool> same = id => kindTarget >= 0 ? (id >= 48 && id < 384 && id / 48 == kindTarget) : id == target;
            var seen = new bool[_map.Width, _map.Height];
            var q = new Queue<Point>(); q.Enqueue(new Point(sx, sy)); seen[sx, sy] = true;
            var cells = new List<Point>();
            while (q.Count > 0)
            {
                var p = q.Dequeue(); cells.Add(p);
                foreach (var d in new[] { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1) })
                {
                    int nx = p.X + d.X, ny = p.Y + d.Y;
                    if (!_map.Valid(nx, ny) || seen[nx, ny] || !same(_map.GetTile(nx, ny, layer))) continue;
                    seen[nx, ny] = true; q.Enqueue(new Point(nx, ny));
                }
            }
            foreach (var p in cells) _map.SetTile(p.X, p.Y, layer, b[((p.Y - sy) % bh + bh) % bh, ((p.X - sx) % bw + bw) % bw]);
            foreach (var p in cells) TileGraphics.FixAutotiles(_map, layer, p.X, p.Y);
            InvalidateAll(); _ed.MarkDirty();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var cell = CellAt(e.Location);
            if (!_map.Valid(cell.X, cell.Y)) return;
            SelectedCell = cell;
            if (_ed.Layer == 3) { EventMouseDown(e, cell); return; }
            if (e.Button == MouseButtons.Right)
            {
                // Eyedropper: pick the top-most tile on the current layer
                int id = _map.GetTile(cell.X, cell.Y, _ed.Layer);
                _ed.PaletteControl.SelectId(id >= 48 && id < 384 ? (id / 48) * 48 : id);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            _ed.Snapshot();
            switch (_ed.Tool)
            {
                case MapTool.Pencil:
                case MapTool.Eraser:
                    _painting = true; Stamp(cell.X, cell.Y); break;
                case MapTool.Rectangle:
                    _rectDragging = true; _dragStart = _dragEnd = cell; Invalidate(); break;
                case MapTool.Fill:
                    Flood(cell.X, cell.Y); break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var cell = CellAt(e.Location);
            if (cell != _hover)
            {
                var old = _hover; _hover = cell;
                if (Hover != null) Hover(cell.X, cell.Y);
                if (_painting && _map.Valid(cell.X, cell.Y)) Stamp(cell.X, cell.Y);
                if (_rectDragging) { _dragEnd = new Point(Math.Max(0, Math.Min(_map.Width - 1, cell.X)), Math.Max(0, Math.Min(_map.Height - 1, cell.Y))); }
                if (_movingEvent && _selectedEvent != null && _map.Valid(cell.X, cell.Y) && !_map.Events.Any(ev => ev != _selectedEvent && ev.X == cell.X && ev.Y == cell.Y))
                { _selectedEvent.X = cell.X; _selectedEvent.Y = cell.Y; _ed.MarkDirty(); }
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_rectDragging) { _rectDragging = false; FillRect(CellRect(_dragStart, _dragEnd)); }
            _painting = false; _movingEvent = false;
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = new Point(-1, -1); Invalidate(); }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (_ed.Layer != 3 || e.Button != MouseButtons.Left) return;
            var cell = CellAt(e.Location);
            var ev = _map.Events.FirstOrDefault(x => x.X == cell.X && x.Y == cell.Y);
            if (ev == null) ev = NewEvent(cell);
            _movingEvent = false;
            _ed.EditEvent(ev);
        }

        void EventMouseDown(MouseEventArgs e, Point cell)
        {
            _selectedEvent = _map.Events.FirstOrDefault(x => x.X == cell.X && x.Y == cell.Y);
            Invalidate();
            if (e.Button == MouseButtons.Left && _selectedEvent != null) { _ed.Snapshot(); _movingEvent = true; }
            if (e.Button != MouseButtons.Right) return;
            var menu = new ContextMenuStrip();
            if (_selectedEvent == null)
            {
                menu.Items.Add("New Event Here…", null, (s, a) => { var ev = NewEvent(cell); _ed.EditEvent(ev); });
                menu.Items.Add("New NPC (talks when spoken to)…", null, (s, a) => QuickNpc(cell));
                menu.Items.Add("New Door / Warp…", null, (s, a) => QuickWarp(cell));
                menu.Items.Add("New Item Ball…", null, (s, a) => QuickItem(cell));
                if (_clipboard != null) menu.Items.Add("Paste Event", null, (s, a) => PasteEvent(cell));
            }
            else
            {
                var ev = _selectedEvent;
                menu.Items.Add("Edit Event…", null, (s, a) => _ed.EditEvent(ev));
                menu.Items.Add("Copy Event", null, (s, a) => _clipboard = Clone(ev));
                menu.Items.Add("Delete Event", null, (s, a) => DeleteSelectedEvent());
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Set Player Start Here", null, (s, a) => _ed.SetPlayerStart(cell.X, cell.Y));
            menu.Show(this, e.Location);
        }

        static MapEvent Clone(MapEvent ev) { return Json.Json.Deserialize<MapEvent>(Json.Json.Write(ev)); }

        MapEvent NewEvent(Point cell)
        {
            _ed.Snapshot();
            var ev = new MapEvent { Id = _map.NextEventId(), X = cell.X, Y = cell.Y };
            ev.Name = "EV" + ev.Id.ToString("000");
            _map.Events.Add(ev);
            _selectedEvent = ev;
            _ed.MarkDirty(); Invalidate();
            return ev;
        }

        void PasteEvent(Point cell)
        {
            _ed.Snapshot();
            var ev = Clone(_clipboard); ev.Id = _map.NextEventId(); ev.X = cell.X; ev.Y = cell.Y;
            _map.Events.Add(ev); _selectedEvent = ev; _ed.MarkDirty(); Invalidate();
        }

        public void DeleteSelectedEvent()
        {
            if (_selectedEvent == null) return;
            _ed.Snapshot();
            _map.Events.Remove(_selectedEvent); _selectedEvent = null; _ed.MarkDirty(); Invalidate();
        }

        // Quick event templates: the most common Pokémon event types in one step
        void QuickNpc(Point cell)
        {
            string text = Prompt.Ask(FindForm(), "New NPC", "What does this person say?", "Hello there!");
            if (text == null) return;
            var ev = NewEvent(cell); ev.Name = "NPC";
            var pg = ev.Pages[0];
            pg.Graphic.CharacterName = "NPC 01"; pg.MoveType = 1; pg.Trigger = Triggers.Action;
            pg.List = new List<EventCommand> { new EventCommand(101, 0, text), new EventCommand(0, 0) };
            _ed.EditEvent(ev);
        }

        void QuickWarp(Point cell)
        {
            var ev = NewEvent(cell); ev.Name = "Door";
            var pg = ev.Pages[0];
            pg.Trigger = Triggers.PlayerTouch;
            pg.List = new List<EventCommand>
            {
                new EventCommand(250, 0, AudioObj("Door enter")),
                new EventCommand(201, 0, 0, _map.Id, cell.X, cell.Y, 0, 0),
                new EventCommand(0, 0)
            };
            _ed.EditEvent(ev);
        }

        void QuickItem(Point cell)
        {
            string item = Prompt.Ask(FindForm(), "New item ball", "Item ID (e.g. POTION):", "POTION");
            if (string.IsNullOrEmpty(item)) return;
            var ev = NewEvent(cell); ev.Name = "Item";
            var p1 = ev.Pages[0];
            p1.Graphic.CharacterName = "Object ball"; p1.Trigger = Triggers.Action;
            p1.List = new List<EventCommand>
            {
                new EventCommand(111, 0, 12, "pbItemBall(:" + item.ToUpperInvariant() + ")"),
                new EventCommand(123, 1, "A", 0),
                new EventCommand(0, 1),
                new EventCommand(412, 0),
                new EventCommand(0, 0)
            };
            var p2 = new EventPage();
            p2.Condition.SelfSwitchValid = true; p2.Condition.SelfSwitchCh = "A";
            ev.Pages.Add(p2);
            _ed.EditEvent(ev);
        }

        static JsonObject AudioObj(string name) { var a = new JsonObject(); a["Name"] = name; a["Volume"] = 100; a["Pitch"] = 100; return a; }
    }
}
