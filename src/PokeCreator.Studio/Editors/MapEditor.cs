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
    public enum MapTool { Pencil, Rectangle, Fill, Eraser, Select }

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
            left.Controls.Add(_sheetList);
            left.Controls.Add(_sheetHeader);
            _sheetList.SelectedIndexChanged += (s, e) =>
            {
                if (_fillingSheets || _sheetList.SelectedIndex < 0) return;
                _palette.Slot = _sheetSlots[_sheetList.SelectedIndex];
                palScroll.AutoScrollPosition = Point.Empty;
            };
            _palette.SlotChanged += () => { int k = _sheetSlots.IndexOf(_palette.Slot); if (k >= 0 && k != _sheetList.SelectedIndex) { _fillingSheets = true; _sheetList.SelectedIndex = k; _fillingSheets = false; } };
            FillSheets();

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
            var t = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 2, 4, 2), ImageScalingSize = new Size(Icons.Size, Icons.Size), ShowItemToolTips = true };
            t.Items.Add(new ToolStripLabel("Layer:"));
            // Layer and tool buttons are icons; hovering shows the name
            string[] names = { "Ground", "Middle", "Top", "Events" };
            string[] layerIcons = { "layer-ground", "layer-middle", "layer-top", "layer-events" };
            for (int i = 0; i < 4; i++)
            {
                int layer = i;
                var b = new ToolStripButton(names[i], Icons.Get(layerIcons[i])) { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = names[i], AutoToolTip = false, Padding = new Padding(3) };
                b.Click += (s, e) => SetLayer(layer);
                _layerButtons.Add(b); t.Items.Add(b);
            }
            t.Items.Add(new ToolStripSeparator());
            foreach (MapTool tool in Enum.GetValues(typeof(MapTool)))
            {
                var tl = tool;
                string icon = tool == MapTool.Pencil ? "tool-pencil" : tool == MapTool.Rectangle ? "tool-rect" : tool == MapTool.Fill ? "tool-fill" : tool == MapTool.Eraser ? "tool-eraser" : "tool-select";
                string tip = tool == MapTool.Select ? "Select (drag an area, then Ctrl+C / Ctrl+X / Delete; Ctrl+V pastes)" : tool.ToString();
                var b = new ToolStripButton(tool.ToString(), Icons.Get(icon)) { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = tip, AutoToolTip = false, Padding = new Padding(3) };
                b.Click += (s, e) => SetTool(tl);
                _toolButtons.Add(b); t.Items.Add(b);
            }
            t.Items.Add(new ToolStripSeparator());
            _undoButton = new ToolStripButton("Undo") { ToolTipText = "Ctrl+Z", Enabled = false }; _undoButton.Click += (s, e) => Undo(); t.Items.Add(_undoButton);
            _redoButton = new ToolStripButton("Redo") { ToolTipText = "Ctrl+Y", Enabled = false }; _redoButton.Click += (s, e) => Redo(); t.Items.Add(_redoButton);
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

        // Sheets (areas) of the map's tileset folder; any of them can be painted on this map
        readonly ListBox _sheetList = new ListBox { Dock = DockStyle.Top, IntegralHeight = false, Height = 60 };
        readonly Label _sheetHeader = new Label { Dock = DockStyle.Top, Height = 20, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
        readonly List<int> _sheetSlots = new List<int>();
        bool _fillingSheets;

        void FillSheets()
        {
            _fillingSheets = true;
            _sheetList.BeginUpdate();
            _sheetList.Items.Clear(); _sheetSlots.Clear();
            foreach (var sh in _tiles.Sheets) { _sheetSlots.Add(sh.Key); _sheetList.Items.Add(sh.Value); }
            _sheetList.EndUpdate();
            int rows = Math.Max(1, Math.Min(8, _sheetSlots.Count));
            _sheetList.Height = rows * Math.Max(_sheetList.ItemHeight, _sheetList.Font.Height + 3) + 8;
            var ts = _tiles.Tileset;
            _sheetHeader.Text = (string.IsNullOrEmpty(ts.Folder) ? "Tileset: " + ts.Name : "Tileset folder: " + ts.Folder) + (_sheetSlots.Count > 1 ? "  (" + _sheetSlots.Count + " areas)" : "");
            int k = _sheetSlots.IndexOf(_palette.Slot);
            if (k < 0 && _sheetSlots.Count > 0) { k = 0; _palette.Slot = _sheetSlots[0]; }
            if (k >= 0) _sheetList.SelectedIndex = k;
            _fillingSheets = false;
        }

        void SetLayer(int l)
        {
            Layer = l;
            for (int i = 0; i < _layerButtons.Count; i++) _layerButtons[i].Checked = i == l;
            _canvas.Invalidate();
        }

        void SetTool(MapTool t)
        {
            Tool = t;
            if (_canvas != null) { _canvas.CancelPaste(); if (t != MapTool.Select) _canvas.ClearSelection(); }
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
                s += "   tile " + id + (id >= 48 && id < 384 ? " (autotile " + (id / 48) + ")" : id >= 384 && ts.AllSheets().Count > 1 ? " (" + TilesetData.LabelOf(ts.SheetImage(TilesetData.SlotOf(id))) + ")" : "") + "   priority " + ts.Priority(id) + "   terrain " + TerrainName(ts.TerrainTag(id)) + "   " + ((ts.Passage(id) & 0x0f) == 0x0f ? "blocked" : "passable");
            }
            var ev = _map.Events == null ? null : _map.Events.FirstOrDefault(e => e != null && e.X == x && e.Y == y);
            if (ev != null) s += "   event " + ev.Id + ": " + ev.Name;
            if (_canvas.Pasting) s += "     Click to paste (all layers)   Esc or right-click: stop pasting";
            else if (Layer == 3) s += "     Double-click: edit   Right-click: menu   Ctrl+C / Ctrl+V: copy / paste event";
            else if (Tool == MapTool.Select) s += "     Drag to select   Ctrl+C copy   Ctrl+X cut   Delete clear   Ctrl+V paste";
            else s += "     Left: paint   Right: pick tile (drag to pick an area)   Ctrl+V: paste";
            _info.Text = s;
        }

        static string TerrainName(int t) { return t >= 0 && t < TerrainTags.Names.Length ? TerrainTags.Names[t] : t.ToString(); }

        public void MarkDirty() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }

        // ------------------------------------------------------------------ undo
        ToolStripButton _undoButton, _redoButton;

        /// <summary>Undo/Redo are only clickable when there is something to undo or redo.</summary>
        void UpdateUndoButtons()
        {
            if (_undoButton != null) _undoButton.Enabled = _undo.Count > 0;
            if (_redoButton != null) _redoButton.Enabled = _redo.Count > 0;
        }

        string CurrentState() { return Json.Json.Write(new UndoState { Layers = _map.Layers, Events = _map.Events }, false); }

        /// <summary>Remembers the map for Undo. Returns false if it was the same as the last remembered state.</summary>
        public bool Snapshot()
        {
            string now = CurrentState();
            // Don't stack identical states (e.g. clicking without changing anything)
            bool pushed = _undo.Count == 0 || _undo.Peek() != now;
            if (pushed) _undo.Push(now);
            if (_undo.Count > 60) { var keep = _undo.Take(60).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); }
            _redo.Clear();
            UpdateUndoButtons();
            return pushed;
        }

        class UndoState
        {
            public List<List<List<int>>> Layers { get; set; }
            public List<MapEvent> Events { get; set; }
            // Empty lists aren't written to the snapshot, so they must exist after reading it back
            public UndoState() { Layers = new List<List<List<int>>>(); Events = new List<MapEvent>(); }
        }

        void Restore(string snap)
        {
            var st = Json.Json.Deserialize<UndoState>(snap);
            // Only take layers that fit this map (a damaged snapshot must not break the map)
            if (st.Layers != null && st.Layers.Count == _map.Layers.Count && st.Layers.All(l => l != null && l.Count == _map.Height && l.All(r => r != null && r.Count == _map.Width)))
                _map.Layers = st.Layers;
            _map.Events = st.Events ?? new List<MapEvent>();
            _canvas.InvalidateAll(); MarkDirty();
        }

        public void Undo()
        {
            if (_undo.Count == 0) { UpdateUndoButtons(); return; }
            _redo.Push(CurrentState());
            Restore(_undo.Pop());
            UpdateUndoButtons();
        }

        public void Redo()
        {
            if (_redo.Count == 0) { UpdateUndoButtons(); return; }
            _undo.Push(CurrentState());
            Restore(_redo.Pop());
            UpdateUndoButtons();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
            if (keyData == (Keys.Control | Keys.Y)) { Redo(); return true; }
            if (keyData == Keys.Delete && Layer == 3) { _canvas.DeleteSelectedEvent(); return true; }
            if (!(ActiveControl is TextBoxBase) && !(ActiveControl is ComboBox))
            {
                if (keyData == (Keys.Control | Keys.C)) { if (Layer == 3) _canvas.CopySelectedEvent(); else _canvas.CopySelection(false); return true; }
                if (keyData == (Keys.Control | Keys.X)) { if (Layer == 3) { _canvas.CopySelectedEvent(); _canvas.DeleteSelectedEvent(); } else _canvas.CopySelection(true); return true; }
                if (keyData == (Keys.Control | Keys.V)) { if (Layer == 3) _canvas.PasteEventAtHover(); else { _canvas.BeginPaste(); ShowInfo(_canvas.HoverCell.X, _canvas.HoverCell.Y); } return true; }
                if (keyData == Keys.Delete && _canvas.HasSelection) { _canvas.ClearSelectedTiles(); return true; }
                if (keyData == (Keys.Control | Keys.A) && Layer < 3) { SetTool(MapTool.Select); _canvas.SelectAll(); return true; }
                if (keyData == Keys.Escape) { _canvas.CancelPaste(); _canvas.ClearSelection(); return true; }
            }
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
                FillSheets();
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

        /// <summary>Selects the event layer and opens an event's editor (used by the Problems list).</summary>
        public void EditEventById(int id, int page = 0)
        {
            var ev = _map.Events.FirstOrDefault(e => e.Id == id);
            if (ev == null) return;
            EditEvent(ev, page);
        }

        public void EditEvent(MapEvent ev) { EditEvent(ev, 0); }

        public void EditEvent(MapEvent ev, int page)
        {
            bool pushed = Snapshot();
            using (var dlg = new EventEditor(_p, _map, ev, page))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) { MarkDirty(); _canvas.Invalidate(); }
                else if (pushed && _undo.Count > 0) { _undo.Pop(); UpdateUndoButtons(); }
            }
        }
    }

    /// <summary>Tileset palette: blank + 7 autotiles on the first row, then the selected sheet's tiles.</summary>
    public class Palette : Control, IWheelTarget
    {
        public void Wheel(int delta) { WheelScroll.ScrollParent(this, delta); }
        readonly TileGraphics _tiles;
        Point _selStart, _selEnd;
        bool _dragging;
        int _slot;
        public event Action SelectionChanged;
        public event Action SlotChanged;

        /// <summary>Which sheet (area) of the tileset folder is shown.</summary>
        public int Slot
        {
            get { return _slot; }
            set { if (_slot == value) return; _slot = value; Reload(); if (SlotChanged != null) SlotChanged(); }
        }

        public Palette(TileGraphics tiles)
        {
            _tiles = tiles;
            DoubleBuffered = true;
            Reload();
        }

        public void Reload()
        {
            Width = 8 * 32; Height = (1 + _tiles.Rows(_slot)) * 32;
            _selStart = _selEnd = new Point(0, 1);
            Invalidate();
        }

        public static int IdAt(int col, int row) { return IdAt(col, row, 0); }
        public static int IdAt(int col, int row, int slot) { return row == 0 ? (col == 0 ? 0 : col * 48) : TilesetData.TileId(slot, (row - 1) * 8 + col); }

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
        int[,] _custom; // a block picked from the map with right-drag

        /// <summary>Uses a block of tiles picked from the map as the brush, until a palette tile is chosen.</summary>
        public void SetCustomBrush(int[,] b) { _custom = b; Invalidate(); if (SelectionChanged != null) SelectionChanged(); }
        public Size BrushSize { get { if (_custom != null) return new Size(_custom.GetLength(1), _custom.GetLength(0)); var r = Selection; return r.Size; } }

        public int[,] Brush
        {
            get
            {
                if (_custom != null) return (int[,])_custom.Clone();
                var r = Selection;
                var b = new int[r.Height, r.Width];
                for (int y = 0; y < r.Height; y++) for (int x = 0; x < r.Width; x++) b[y, x] = IdAt(r.X + x, r.Y + y, _slot);
                return b;
            }
        }

        public void SelectId(int id)
        {
            Point p;
            if (id == 0) p = new Point(0, 0);
            else if (id < TilesetData.FirstTile) p = new Point(id / 48, 0);
            else { Slot = TilesetData.SlotOf(id); int i = TilesetData.IndexOf(id); p = new Point(i % 8, i / 8 + 1); }
            _selStart = _selEnd = p; _custom = null;
            Invalidate();
            var parent = Parent as ScrollableControl;
            if (parent != null) parent.ScrollControlIntoView(this);
        }

        protected override void OnMouseWheel(MouseEventArgs e) { WheelScroll.ToParent(this, e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _selStart = _selEnd = Cell(e.Location); _dragging = true; _custom = null; Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) { _selEnd = Cell(e.Location); Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; if (SelectionChanged != null) SelectionChanged(); }

        Point Cell(Point p) { return new Point(Math.Max(0, Math.Min(7, p.X / 32)), Math.Max(0, Math.Min(_tiles.Rows(_slot), p.Y / 32))); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var checker = new HatchBrush(HatchStyle.LargeCheckerBoard, Color.FromArgb(205, 205, 210), Color.FromArgb(230, 230, 235)))
                g.FillRectangle(checker, ClientRectangle);
            for (int i = 1; i < 8; i++) _tiles.Draw(g, i * 48 + 47, i * 32, 0);
            g.DrawString("∅", Font, Brushes.Gray, 10, 9);
            var sheet = _tiles.Image(_slot);
            if (sheet != null) g.DrawImage(sheet, new Rectangle(0, 32, 256, sheet.Height), new Rectangle(0, 0, 256, sheet.Height), GraphicsUnit.Pixel);
            g.DrawLine(Pens.DimGray, 0, 32, Width, 32);
            var r = Selection;
            var rect = new Rectangle(r.X * 32, r.Y * 32, r.Width * 32 - 1, r.Height * 32 - 1);
            if (_custom != null) { g.DrawString("Brush picked from the map (" + _custom.GetLength(1) + "×" + _custom.GetLength(0) + ")", Font, Brushes.Gray, 4, Height - 18); return; }
            using (var pen = new Pen(Color.White, 3)) g.DrawRectangle(pen, rect);
            using (var pen = new Pen(Color.Black, 1)) g.DrawRectangle(pen, rect);
        }
    }

    /// <summary>The map drawing surface. Tiles are cached in 16x16-tile chunks per layer.</summary>
    public partial class MapCanvas : Control, IWheelTarget
    {
        public void Wheel(int delta) { WheelScroll.ScrollParent(this, delta); }
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
        bool _painting, _rectDragging, _movingEvent, _selecting, _picking;
        MapEvent _selectedEvent;
        static MapEvent _clipboard;         // copied event (shared by all maps)
        static TileClip _tileClip;          // copied tiles (shared by all maps)
        Rectangle _sel = Rectangle.Empty;   // selected area (Select tool)
        bool _pasting;

        /// <summary>Tiles copied from a map: all three layers.</summary>
        class TileClip { public int W, H; public int[][,] Layers = new int[3][,]; }

        public bool Pasting { get { return _pasting; } }
        public bool HasSelection { get { return _sel.Width > 0 && _sel.Height > 0; } }
        public Point HoverCell { get { return _hover; } }
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
                else if (_pasting && _tileClip != null) r = new Rectangle(_hover.X, _hover.Y, _tileClip.W, _tileClip.H);
                else if (_picking || _selecting) r = CellRect(_dragStart, _dragEnd);
                else if (_ed.Layer < 3) { var br = _ed.PaletteControl.BrushSize; r = new Rectangle(_hover.X, _hover.Y, _ed.Tool == MapTool.Pencil ? br.Width : 1, _ed.Tool == MapTool.Pencil ? br.Height : 1); }
                else r = new Rectangle(_hover.X, _hover.Y, 1, 1);
                var pr = new Rectangle(r.X * c, r.Y * c, r.Width * c - 1, r.Height * c - 1);
                if (_pasting && _tileClip != null) DrawClip(g, _tileClip, _hover.X, _hover.Y, c);
                using (var pen = new Pen(_pasting ? Color.Cyan : _picking ? Color.Orange : Color.White, 2)) g.DrawRectangle(pen, pr);
                g.DrawRectangle(Pens.Black, pr);
            }
            if (HasSelection && !_selecting)
            {
                var sr = new Rectangle(_sel.X * c, _sel.Y * c, _sel.Width * c - 1, _sel.Height * c - 1);
                using (var fill = new SolidBrush(Color.FromArgb(40, 80, 160, 255))) g.FillRectangle(fill, sr);
                using (var pen = new Pen(Color.Yellow, 2) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, sr);
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

        protected override void OnMouseWheel(MouseEventArgs e) { WheelScroll.ToParent(this, e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var cell = CellAt(e.Location);
            if (!_map.Valid(cell.X, cell.Y)) return;
            SelectedCell = cell;
            if (_ed.Layer == 3) { EventMouseDown(e, cell); return; }
            if (_pasting)
            {
                if (e.Button == MouseButtons.Right) { CancelPaste(); return; }
                if (e.Button == MouseButtons.Left) PasteAt(cell.X, cell.Y);
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                // Eyedropper: click picks one tile; drag picks an area of the current layer as the brush
                _picking = true; _dragStart = _dragEnd = cell; Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (_ed.Tool == MapTool.Select) { _selecting = true; _sel = Rectangle.Empty; _dragStart = _dragEnd = cell; Invalidate(); return; }
            ClearSelection();
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
                if (_rectDragging || _selecting || _picking) { _dragEnd = new Point(Math.Max(0, Math.Min(_map.Width - 1, cell.X)), Math.Max(0, Math.Min(_map.Height - 1, cell.Y))); }
                if (_movingEvent && _selectedEvent != null && _map.Valid(cell.X, cell.Y) && !_map.Events.Any(ev => ev != _selectedEvent && ev.X == cell.X && ev.Y == cell.Y))
                { _selectedEvent.X = cell.X; _selectedEvent.Y = cell.Y; _ed.MarkDirty(); }
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_rectDragging) { _rectDragging = false; FillRect(CellRect(_dragStart, _dragEnd)); }
            if (_selecting) { _selecting = false; _sel = CellRect(_dragStart, _dragEnd); Invalidate(); }
            if (_picking)
            {
                _picking = false;
                var r = CellRect(_dragStart, _dragEnd);
                int layer = _ed.Layer;
                if (r.Width == 1 && r.Height == 1)
                {
                    int id = _map.GetTile(r.X, r.Y, layer);
                    _ed.PaletteControl.SelectId(id >= 48 && id < 384 ? (id / 48) * 48 : id);
                }
                else
                {
                    var b = new int[r.Height, r.Width];
                    for (int y = 0; y < r.Height; y++) for (int x = 0; x < r.Width; x++) b[y, x] = _map.GetTile(r.X + x, r.Y + y, layer);
                    _ed.PaletteControl.SetCustomBrush(b);
                }
                Invalidate();
            }
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
                menu.Items.Add("New Trainer (battles when they see you)…", null, (s, a) => QuickTrainer(cell));
                menu.Items.Add("New Nurse (heals the party)…", null, (s, a) => QuickNurse(cell));
                menu.Items.Add("New Poké Mart Clerk…", null, (s, a) => QuickMart(cell));
                menu.Items.Add("New PC (Pokémon Storage)", null, (s, a) => QuickSimple(cell, "PC", "", new List<EventCommand> { new EventCommand(355, 0, "pbPokeCenterPC"), new EventCommand(0, 0) }));
                menu.Items.Add("New Sign…", null, (s, a) =>
                {
                    string text = TextDialog.Ask(FindForm(), "New Sign: what it says", "Route 1", true);
                    if (text == null) return;
                    QuickSimple(cell, "Sign", "", new List<EventCommand> { new EventCommand(101, 0, text), new EventCommand(0, 0) });
                });
                var fieldMenu = new ToolStripMenuItem("New Field Object");
                fieldMenu.DropDownItems.Add("Cut Tree", null, (s, a) => QuickFieldObject(cell, "CutTree"));
                fieldMenu.DropDownItems.Add("Rock Smash Rock", null, (s, a) => QuickFieldObject(cell, "SmashRock"));
                fieldMenu.DropDownItems.Add("Strength Boulder", null, (s, a) => QuickFieldObject(cell, "StrengthBoulder"));
                fieldMenu.DropDownItems.Add("Headbutt Tree", null, (s, a) => QuickFieldObject(cell, "HeadbuttTree"));
                fieldMenu.DropDownItems.Add("Berry Plant…", null, (s, a) => QuickFieldObject(cell, "BerryPlant"));
                fieldMenu.DropDownItems.Add("Empty Berry Soil (plant your own)", null, (s, a) => QuickFieldObject(cell, "BerrySoil"));
                fieldMenu.DropDownItems.Add("Hidden Item (found with the Itemfinder)…", null, (s, a) => QuickFieldObject(cell, "HiddenItem"));
                menu.Items.Add(fieldMenu);
                var dcMenu = new ToolStripMenuItem("New Day Care");
                dcMenu.DropDownItems.Add("Day Care Lady (takes and returns Pokémon)", null, (s, a) => QuickDayCare(cell, true));
                dcMenu.DropDownItems.Add("Day Care Man (gives Eggs)", null, (s, a) => QuickDayCare(cell, false));
                menu.Items.Add(dcMenu);
                menu.Items.Add(NpcMenu(cell));
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

        // ------------------------------------------------------------------ copy / paste
        public void CopySelectedEvent() { if (_selectedEvent != null) _clipboard = Clone(_selectedEvent); }

        public void PasteEventAtHover()
        {
            if (_clipboard == null) return;
            var cell = _map.Valid(_hover.X, _hover.Y) ? _hover : SelectedCell;
            if (!_map.Valid(cell.X, cell.Y) || _map.Events.Any(e => e.X == cell.X && e.Y == cell.Y)) return;
            PasteEvent(cell);
        }

        public void ClearSelection() { if (HasSelection || _selecting) { _sel = Rectangle.Empty; _selecting = false; Invalidate(); } }
        public void SelectAll() { _sel = new Rectangle(0, 0, _map.Width, _map.Height); Invalidate(); }

        /// <summary>Copies the selected area (all three layers); with cut, also clears it.</summary>
        public void CopySelection(bool cut)
        {
            if (!HasSelection) return;
            var clip = new TileClip { W = _sel.Width, H = _sel.Height };
            for (int l = 0; l < 3; l++)
            {
                clip.Layers[l] = new int[clip.H, clip.W];
                for (int y = 0; y < clip.H; y++) for (int x = 0; x < clip.W; x++) clip.Layers[l][y, x] = _map.GetTile(_sel.X + x, _sel.Y + y, l);
            }
            _tileClip = clip;
            if (cut) ClearSelectedTiles();
        }

        public void ClearSelectedTiles()
        {
            if (!HasSelection) return;
            _ed.Snapshot();
            for (int l = 0; l < 3; l++)
                for (int y = _sel.Top; y < _sel.Bottom; y++) for (int x = _sel.Left; x < _sel.Right; x++) _map.SetTile(x, y, l, 0);
            FixAutotilesAround(_sel);
            InvalidateAll(); _ed.MarkDirty();
        }

        public void BeginPaste()
        {
            if (_tileClip == null) return;
            _pasting = true; ClearSelection(); Focus(); Invalidate();
        }

        public void CancelPaste() { if (_pasting) { _pasting = false; Invalidate(); } }

        void PasteAt(int px, int py)
        {
            var clip = _tileClip; if (clip == null) return;
            _ed.Snapshot();
            for (int l = 0; l < 3; l++)
                for (int y = 0; y < clip.H; y++) for (int x = 0; x < clip.W; x++)
                        if (_map.Valid(px + x, py + y)) _map.SetTile(px + x, py + y, l, clip.Layers[l][y, x]);
            FixAutotilesAround(new Rectangle(px, py, clip.W, clip.H));
            InvalidateAll(); _ed.MarkDirty();
        }

        /// <summary>Autotiles just outside a changed area reshape to join (or stop joining) it.</summary>
        void FixAutotilesAround(Rectangle r)
        {
            for (int l = 0; l < 3; l++)
                for (int y = r.Top - 1; y <= r.Bottom; y++)
                    for (int x = r.Left - 1; x <= r.Right; x++)
                        if (_map.Valid(x, y) && (y == r.Top - 1 || y == r.Bottom || x == r.Left - 1 || x == r.Right)) TileGraphics.FixAutotiles(_map, l, x, y);
        }

        void DrawClip(Graphics g, TileClip clip, int cx, int cy, int c)
        {
            for (int l = 0; l < 3; l++)
                for (int y = 0; y < clip.H; y++) for (int x = 0; x < clip.W; x++)
                    {
                        int id = clip.Layers[l][y, x];
                        if (id > 0 && _map.Valid(cx + x, cy + y)) _tiles.Draw(g, id, (cx + x) * c, (cy + y) * c, c);
                    }
        }

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
            pg.Graphic.CharacterName = TemplateSprite("NPC 01"); pg.MoveType = 1; pg.Trigger = Triggers.Action;
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
            var itemIds = _p.Items.Where(i => i.Pocket != 8).Select(i => i.Id).ToArray();
            int pick = ChoiceDialog.Ask(FindForm(), "New item ball: which item?", itemIds, true);
            if (pick < 0) return;
            string item = itemIds[pick];
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

        /// <summary>
        /// An Essentials trainer: "Trainer(n)" event that walks up when it sees the player, battles, and afterwards
        /// says its after-battle line (self switch A).
        /// </summary>
        void QuickTrainer(Point cell)
        {
            var list = _p.Trainers.OrderBy(t => t.TrainerType).ThenBy(t => t.Name).ToList();
            if (list.Count == 0) { MessageBox.Show(FindForm(), "There are no trainers yet. Add them under Database > Trainers first.", "New Trainer"); return; }
            int k = ChoiceDialog.Ask(FindForm(), "New trainer: which trainer from the database?", list.Select(t => t.TrainerType + " " + t.Name + (t.Version > 0 ? " (version " + t.Version + ")" : "") + "  -  " + t.Pokemon.Count + " Pokémon").ToArray(), true);
            if (k < 0) return;
            var tr = list[k];
            int range = NumberDialog.Ask(FindForm(), "How many tiles can they see? (0 = only when talked to)", 3);
            if (range < 0) return;
            string before = Prompt.Ask(FindForm(), "New trainer", "What do they say before battling?", "Hey! You there! Let's battle!");
            if (before == null) return;
            string after = Prompt.Ask(FindForm(), "New trainer", "What do they say after you beat them?", "You're strong!");
            if (after == null) return;
            string battle = "TrainerBattle.start(:" + tr.TrainerType + ", \"" + tr.Name.Replace("\"", "\\\"") + "\"" + (tr.Version > 0 ? ", " + tr.Version : "") + ")";
            var ev = NewEvent(cell); ev.Name = range > 0 ? "Trainer(" + range + ")" : "Trainer";
            var p1 = ev.Pages[0];
            // Overworld sprites follow the trainer_<TYPE> naming
            string sprite = "trainer_" + tr.TrainerType;
            p1.Graphic.CharacterName = TemplateSprite(sprite);
            p1.Trigger = range > 0 ? Triggers.EventTouch : Triggers.Action;
            p1.List = new List<EventCommand>
            {
                new EventCommand(355, 0, "pbTrainerIntro(:" + tr.TrainerType + ")"),
                new EventCommand(355, 0, "pbNoticePlayer(get_self)"),
                new EventCommand(101, 0, before),
                new EventCommand(111, 0, 12, battle),
                new EventCommand(123, 1, "A", 0),
                new EventCommand(0, 1),
                new EventCommand(412, 0),
                new EventCommand(355, 0, "pbTrainerEnd"),
                new EventCommand(0, 0)
            };
            var p2 = new EventPage();
            p2.Condition.SelfSwitchValid = true; p2.Condition.SelfSwitchCh = "A";
            p2.Graphic.CharacterName = p1.Graphic.CharacterName; p2.Graphic.Direction = p1.Graphic.Direction;
            p2.Trigger = Triggers.Action;
            p2.List = new List<EventCommand> { new EventCommand(101, 0, after), new EventCommand(0, 0) };
            // Rematches: when the database has later versions of this trainer, offer to register them in the Pokégear
            int maxVersion = _p.Trainers.Where(t => t.TrainerType == tr.TrainerType && t.Name == tr.Name).Select(t => t.Version).DefaultIfEmpty(0).Max();
            if (maxVersion > tr.Version && MessageBox.Show(FindForm(), "This trainer has " + maxVersion + " rematch version(s) in the database.\n\nLet the player register them in the Pokégear phone for rematches?", "Rematches", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                string t = ":" + tr.TrainerType + ", \"" + tr.Name.Replace("\"", "\\\"") + "\"";
                // After the first win, ask to register (inserted before pbTrainerEnd)
                p1.List.Insert(p1.List.Count - 2, new EventCommand(355, 0, "pbPhoneRegisterBattle(\"Let's trade numbers so we can battle again!\", get_self, " + t + ", " + maxVersion + ")"));
                p2.List = new List<EventCommand>
                {
                    new EventCommand(111, 0, 12, "pbPhoneReadyToBattle?(" + t + ")"),
                    new EventCommand(101, 1, before),
                    new EventCommand(111, 1, 12, "TrainerBattle.start(" + t + ", pbPhoneBattleCount(" + t + "))"),
                    new EventCommand(355, 2, "pbPhoneIncrement(" + t + ")"),
                    new EventCommand(0, 2),
                    new EventCommand(412, 1),
                    new EventCommand(0, 1),
                    new EventCommand(411, 0),
                    new EventCommand(101, 1, after),
                    new EventCommand(0, 1),
                    new EventCommand(412, 0),
                    new EventCommand(0, 0)
                };
            }
            ev.Pages.Add(p2);
            _ed.EditEvent(ev);
        }

        /// <summary>A Pokémon Center nurse: heals the party and makes this the place to return to after blacking out.</summary>
        /// <summary>An event with no picture that runs its commands when the player presses Action facing it.</summary>
        void QuickSimple(Point cell, string name, string sprite, List<EventCommand> list)
        {
            var ev = NewEvent(cell); ev.Name = name;
            var pg = ev.Pages[0];
            pg.Graphic.CharacterName = sprite;
            pg.Trigger = Triggers.Action;
            pg.List = list;
            _ed.EditEvent(ev);
        }

        void QuickNurse(Point cell)
        {
            var ev = NewEvent(cell); ev.Name = "Nurse";
            var pg = ev.Pages[0];
            pg.Graphic.CharacterName = TemplateSprite("NPC 16");
            pg.Trigger = Triggers.Action;
            pg.List = new List<EventCommand>
            {
                new EventCommand(102, 0, new List<object> { "Yes", "No" }, 2),
                new EventCommand(402, 0, 0, "Yes"),
                new EventCommand(101, 1, "OK, I'll take your Pokémon for a few seconds."),
                new EventCommand(355, 1, "pbSetPokemonCenter"),
                new EventCommand(314, 1, 0),
                new EventCommand(249, 1, AudioObj("Pkmn healing")),
                new EventCommand(106, 1, 40),
                new EventCommand(101, 1, "Thank you for waiting. We've restored your Pokémon to full health. We hope to see you again!"),
                new EventCommand(0, 1),
                new EventCommand(402, 0, 1, "No"),
                new EventCommand(101, 1, "We hope to see you again!"),
                new EventCommand(0, 1),
                new EventCommand(404, 0),
                new EventCommand(0, 0)
            };
            pg.List.Insert(0, new EventCommand(101, 0, "Welcome to our Pokémon Center! Would you like me to heal your Pokémon back to perfect health?"));
            _ed.EditEvent(ev);
        }

        /// <summary>A shop clerk selling the items you tick.</summary>
        void QuickMart(Point cell)
        {
            var items = _p.Items.Where(i => i.Price > 0 && i.Pocket != 8).OrderBy(i => i.Pocket).ThenBy(i => i.Price).ToList();
            using (var f = new Form { Text = "New Poké Mart Clerk: tick the items to sell", Width = 460, Height = 560, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, ShowInTaskbar = false })
            {
                var search = new TextBox { Dock = DockStyle.Top };
                var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
                var ok = new Button { Text = "Create", Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.OK };
                var chosen = new HashSet<string> { "POKEBALL", "POTION", "ANTIDOTE", "PARALYZEHEAL", "ESCAPEROPE", "REPEL" };
                Action fill = () =>
                {
                    list.Items.Clear();
                    foreach (var it in items)
                        if (search.Text.Length == 0 || (it.Name ?? it.Id).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0)
                            list.Items.Add(new KeyValuePair<string, string>(it.Id, (it.Name ?? it.Id) + "  ($" + it.Price + ")"), chosen.Contains(it.Id));
                };
                list.Format += (s, e) => e.Value = ((KeyValuePair<string, string>)e.ListItem).Value;
                list.ItemCheck += (s, e) => { var id = ((KeyValuePair<string, string>)list.Items[e.Index]).Key; if (e.NewValue == CheckState.Checked) chosen.Add(id); else chosen.Remove(id); };
                search.TextChanged += (s, e) => fill();
                f.Controls.Add(list); f.Controls.Add(search); f.Controls.Add(ok); f.AcceptButton = ok;
                fill();
                if (f.ShowDialog(FindForm()) != DialogResult.OK || chosen.Count == 0) return;
                var stock = items.Where(i => chosen.Contains(i.Id)).Select(i => ":" + i.Id).ToList();
                var ev = NewEvent(cell); ev.Name = "Mart";
                var pg = ev.Pages[0];
                pg.Graphic.CharacterName = TemplateSprite("NPC 19");
                pg.Trigger = Triggers.Action;
                pg.List = new List<EventCommand> { new EventCommand(355, 0, "pbPokemonMart([" + string.Join(", ", stock) + "])"), new EventCommand(0, 0) };
                _ed.EditEvent(ev);
            }
        }

        /// <summary>
        /// A character sprite for a template event: the preferred one if the project has it, else any character
        /// sprite, so the event is visible and can be talked to from the front.
        /// </summary>
        /// <summary>Cut trees, smashable rocks, Strength boulders, Headbutt trees and berry plants, set up as in Essentials.</summary>
        void QuickFieldObject(Point cell, string kind)
        {
            string berry = null, hidden = null; int count = 2;
            if (kind == "HiddenItem")
            {
                var ids = _p.Items.Where(it => it.Pocket != 8).Select(it => it.Id).ToArray();
                int k = ChoiceDialog.Ask(FindForm(), "Which item is hidden here?", ids, true); if (k < 0) return;
                hidden = ids[k];
            }
            if (kind == "BerryPlant")
            {
                var ids = _p.Items.Where(it => it.Pocket == 5).Select(it => it.Id).ToArray();
                if (ids.Length == 0) ids = _p.Items.Select(it => it.Id).ToArray();
                int k = ChoiceDialog.Ask(FindForm(), "Which berry grows here?", ids, true); if (k < 0) return;
                berry = ids[k];
                count = NumberDialog.Ask(FindForm(), "How many berries?", 2); if (count < 1) return;
            }
            var ev = NewEvent(cell); ev.Name = kind == "BerrySoil" ? "BerryPlant" : kind;
            var pg = ev.Pages[0];
            pg.Trigger = Triggers.Action;
            switch (kind)
            {
                case "CutTree":
                    pg.Graphic.CharacterName = FieldSprite("Object tree 1");
                    pg.List = new List<EventCommand> { new EventCommand(111, 0, 12, "pbCut"), new EventCommand(355, 1, "pbSmashThisEvent"), new EventCommand(0, 1), new EventCommand(412, 0), new EventCommand(0, 0) };
                    break;
                case "SmashRock":
                    pg.Graphic.CharacterName = FieldSprite("Object rock");
                    pg.List = new List<EventCommand> { new EventCommand(111, 0, 12, "pbRockSmash"), new EventCommand(355, 1, "pbSmashThisEvent"),
                        new EventCommand(355, 1, "pbRockSmashRandomEncounter"), new EventCommand(0, 1), new EventCommand(412, 0), new EventCommand(0, 0) };
                    break;
                case "StrengthBoulder":
                    pg.Graphic.CharacterName = FieldSprite("Object boulder");
                    pg.Trigger = Triggers.PlayerTouch; // pushed by walking into it; pressing A offers Strength
                    pg.List = new List<EventCommand> { new EventCommand(355, 0, "pbPushThisBoulder"), new EventCommand(0, 0) };
                    break;
                case "HeadbuttTree":
                    pg.Graphic.CharacterName = FieldSprite("Object tree 2");
                    pg.List = new List<EventCommand> { new EventCommand(355, 0, "pbHeadbutt"), new EventCommand(0, 0) };
                    break;
                case "BerryPlant":
                    pg.Graphic.CharacterName = FieldSprite("berrytree_" + berry);
                    pg.Graphic.Direction = 8; pg.DirectionFix = true; pg.StepAnime = true; pg.WalkAnime = false;
                    pg.List = new List<EventCommand> { new EventCommand(355, 0, "pbPickBerry(:" + berry + ", " + count + ")"), new EventCommand(0, 0) };
                    var soil = new EventPage();
                    soil.Condition.SelfSwitchValid = true; soil.Condition.SelfSwitchCh = "A";
                    soil.Trigger = Triggers.Action;
                    soil.List = new List<EventCommand> { new EventCommand(355, 0, "pbBerryPlant"), new EventCommand(0, 0) };
                    ev.Pages.Add(soil);
                    break;
                case "BerrySoil":
                    pg.List = new List<EventCommand> { new EventCommand(355, 0, "pbBerryPlant"), new EventCommand(0, 0) };
                    break;
                case "HiddenItem":
                    // Invisible; picked up once (self switch A), and the Itemfinder points at it while it's there
                    pg.List = new List<EventCommand> { new EventCommand(111, 0, 12, "pbItemBall(:" + hidden + ")"), new EventCommand(123, 1, "A", 0), new EventCommand(0, 1), new EventCommand(412, 0), new EventCommand(0, 0) };
                    var found = new EventPage();
                    found.Condition.SelfSwitchValid = true; found.Condition.SelfSwitchCh = "A";
                    found.List = new List<EventCommand> { new EventCommand(0, 0) };
                    ev.Pages.Add(found);
                    break;
            }
            _ed.EditEvent(ev);
        }

        /// <summary>Day Care Lady / Man using Essentials' DayCare calls (variables 1, 3 and 4 are used as scratch).</summary>
        void QuickDayCare(Point cell, bool lady)
        {
            var ev = NewEvent(cell); ev.Name = lady ? "Day Care Lady" : "Day Care Man";
            var pg = ev.Pages[0];
            pg.Graphic.CharacterName = TemplateSprite(lady ? "NPC 13" : "NPC 12");
            pg.Trigger = Triggers.Action;
            pg.List = DayCareCommands(lady);
            _ed.EditEvent(ev);
        }

        public static List<EventCommand> DayCareCommands(bool lady)
        {
            var L = new List<EventCommand>();
            Action<int, string> text = (i, s) => L.Add(new EventCommand(101, i, s));
            Action<int, string> script = (i, s) => L.Add(new EventCommand(355, i, s));
            Action<int, string> ifScript = (i, s) => L.Add(new EventCommand(111, i, 12, s));
            Action<int> end = i => { L.Add(new EventCommand(0, i + 1)); L.Add(new EventCommand(412, i)); };
            Action<int> exit = i => L.Add(new EventCommand(115, i));
            Action<int> yesNo = i => { L.Add(new EventCommand(102, i, new List<object> { "Yes", "No" }, 2)); L.Add(new EventCommand(402, i, 0, "Yes")); };
            Action<int> no = i => { L.Add(new EventCommand(0, i + 1)); L.Add(new EventCommand(402, i, 1, "No")); };
            Action<int> endChoice = i => { L.Add(new EventCommand(0, i + 1)); L.Add(new EventCommand(404, i)); };
            if (lady)
            {
                ifScript(0, "DayCare.egg_generated?"); text(1, "Ah, there you are! My husband was looking for you."); exit(1); end(0);
                // Take Pokémon back
                ifScript(0, "DayCare.count > 0");
                text(1, "Ah, it's you! Good to see you. Now, about your Pokémon...");
                ifScript(1, "DayCare.get_level_gain(0, 3, 4) && pbGet(4) > 0"); text(2, "By level, your \\v[3] has grown by about \\v[4]."); end(1);
                ifScript(1, "DayCare.get_level_gain(1, 3, 4) && pbGet(4) > 0"); text(2, "By level, your \\v[3] has grown by about \\v[4]."); end(1);
                text(1, "Do you want your Pokémon back?");
                yesNo(1);
                ifScript(2, "$player.party_full?"); text(3, "Your Pokémon team is full. Make room, then come see me."); exit(3); end(2);
                script(2, "DayCare.choose(_I(\"Which one do you want back?\"), 1)");
                L.Add(new EventCommand(111, 2, 1, 1, 0, 0, 4)); text(3, "Very good. Come again."); exit(3); end(2);
                script(2, "DayCare.get_details(pbGet(1), 3, 4)");
                text(2, "\\GIf you want your \\v[3] back, it will cost $\\v[4].");
                yesNo(2);
                ifScript(3, "$player.money < pbGet(4)"); text(4, "\\GYou don't have enough money..."); exit(4); end(3);
                L.Add(new EventCommand(125, 3, 1, 1, 4));
                L.Add(new EventCommand(250, 3, AudioObj("Mart buy item")));
                script(3, "DayCare.withdraw(pbGet(1))");
                text(3, "\\PN took \\v[3] back from the Day-Care Lady.");
                exit(3);
                no(2); endChoice(2);
                no(1); endChoice(1);
                end(0);
                // Leave a Pokémon
                ifScript(0, "DayCare.count < 2");
                text(1, "I'm the Day-Care Lady. We can raise Pokémon for you. Would you like us to raise one?");
                yesNo(1);
                ifScript(2, "$player.able_pokemon_count <= 1"); text(3, "Oh? But you'd have no Pokémon left to battle with. Come back another time."); exit(3); end(2);
                text(2, "Which Pokémon should we raise for you?");
                script(2, "pbChooseNonEggPokemon(1, 3)");
                L.Add(new EventCommand(111, 2, 1, 1, 0, 0, 4)); text(3, "Oh, fine, then. Come again."); exit(3); end(2);
                ifScript(2, "!$player.has_other_able_pokemon?(pbGet(1))"); text(3, "If you leave me that Pokémon, what are you gonna battle with?"); exit(3); end(2);
                script(2, "DayCare.deposit(pbGet(1))");
                text(2, "Fine, we'll raise your \\v[3] for a while. Come back for it later.");
                no(1); text(2, "Oh, fine, then. Come again."); endChoice(1);
                L.Add(new EventCommand(0, 1)); L.Add(new EventCommand(411, 0));
                text(1, "We're raising two Pokémon for you already. Come back for them later.");
                end(0);
            }
            else
            {
                ifScript(0, "DayCare.egg_generated?");
                text(1, "Ah, it's you! We were raising your Pokémon, and my word, were we surprised! Your Pokémon had an Egg! Do you want it?");
                yesNo(1);
                ifScript(2, "$player.party_full?"); text(3, "You have no room for it... Come back when you've made room."); exit(3); end(2);
                script(2, "DayCare.collect_egg");
                L.Add(new EventCommand(249, 2, AudioObj("Egg get")));
                text(2, "\\PN received the Egg from the Day-Care Man.");
                text(2, "Take good care of it.");
                no(1); script(2, "DayCare.reset_egg_counters"); text(2, "Well then, I'll keep it. Thanks!"); endChoice(1);
                exit(1);
                end(0);
                ifScript(0, "DayCare.count == 2");
                script(1, "DayCare.get_compatibility(1)");
                string[] lines = { "The two prefer to play with other Pokémon more than with each other.", "The two don't really seem to like each other very much.", "The two seem to get along.", "The two seem to get along very well." };
                for (int c = 0; c < 4; c++) { L.Add(new EventCommand(111, 1, 1, 1, 0, c, 0)); text(2, lines[c]); end(1); }
                L.Add(new EventCommand(0, 1)); L.Add(new EventCommand(411, 0));
                text(1, "I'm the Day-Care Man. My wife and I raise Pokémon for Trainers. Talk to her if you'd like us to raise one.");
                end(0);
            }
            L.Add(new EventCommand(0, 0));
            return L;
        }

        /// <summary>The Essentials object graphic if the project has it; otherwise left for the user to pick.</summary>
        string FieldSprite(string name) { return _p.FindAsset("Graphics/Characters", name) != null ? name : TemplateSprite(name); }

        string TemplateSprite(string preferred)
        {
            if (_p.FindAsset("Graphics/Characters", preferred) != null) return preferred;
            string dir = System.IO.Path.Combine(_p.GraphicsDir, "Characters");
            if (!System.IO.Directory.Exists(dir)) return "";
            var any = System.IO.Directory.GetFiles(dir, "*.png").Select(System.IO.Path.GetFileNameWithoutExtension).OrderBy(n => n.StartsWith("trainer_") ? 1 : 0).ThenBy(n => n).FirstOrDefault();
            return any ?? "";
        }

        static JsonObject AudioObj(string name) { var a = new JsonObject(); a["Name"] = name; a["Volume"] = 100; a["Pitch"] = 100; return a; }
    }
}
