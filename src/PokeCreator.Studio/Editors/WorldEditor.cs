using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Json;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// World map: every map placed by its connections. Drag maps so their edges touch to connect
    /// them (that's how the game walks seamlessly between maps); pull them apart to disconnect.
    /// Layout of maps without connections is kept in Data/world.json.
    /// </summary>
    public class WorldEditor : UserControl, IDocument
    {
        public class Placed
        {
            public int Id;
            public MapData Map;
            public int X, Y; // tile position in the world
            public Rectangle Rect { get { return new Rectangle(X, Y, Map.Width, Map.Height); } }
        }

        readonly MainForm _main;
        readonly Project _p;
        readonly Dictionary<int, Placed> _placed = new Dictionary<int, Placed>();
        readonly WorldCanvas _canvas;
        readonly ListBox _unplaced = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        readonly CheckBox _outdoorOnly = new CheckBox { Text = "Outdoor maps only", Checked = true, Dock = DockStyle.Top, Height = 24 };
        readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(236, 238, 242), Padding = new Padding(6, 0, 0, 0) };
        public bool Dirty { get; private set; }
        public string Title { get { return "World Map"; } }
        public event Action DirtyChanged;

        public WorldEditor(MainForm main, Project p)
        {
            _main = main; _p = p; Dock = DockStyle.Fill;
            _canvas = new WorldCanvas(this, p) { Dock = DockStyle.Fill };
            Theme.Keep(_canvas);

            var left = new Panel { Dock = DockStyle.Left, Width = 230, Padding = new Padding(4) };
            var place = new Button { Text = "Place on World →", Dock = DockStyle.Bottom, Height = 30 };
            place.Click += (s, e) => PlaceSelected();
            _unplaced.DoubleClick += (s, e) => PlaceSelected();
            _unplaced.MouseDown += (s, e) => { if (_unplaced.SelectedItem != null && e.Button == MouseButtons.Left && e.Clicks == 1) _unplaced.DoDragDrop(_unplaced.SelectedItem, DragDropEffects.Move); };
            _outdoorOnly.CheckedChanged += (s, e) => FillUnplaced();
            left.Controls.Add(_unplaced);
            left.Controls.Add(_outdoorOnly);
            left.Controls.Add(new Label { Text = "Maps not on the world yet. Drag one onto the world, or double-click it.", Dock = DockStyle.Top, Height = 48, ForeColor = Color.DimGray });
            left.Controls.Add(new Label { Text = "Unplaced maps", Dock = DockStyle.Top, Height = 24, Font = Theme.HeaderFont });
            left.Controls.Add(place);

            var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 2, 4, 2) };
            bar.Items.Add(new ToolStripButton("Zoom In", null, (s, e) => _canvas.ZoomBy(1.25f)));
            bar.Items.Add(new ToolStripButton("Zoom Out", null, (s, e) => _canvas.ZoomBy(0.8f)));
            bar.Items.Add(new ToolStripButton("Fit All", null, (s, e) => _canvas.FitAll()));
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(new ToolStripButton("Show Names", null, (s, e) => { _canvas.ShowNames = !_canvas.ShowNames; _canvas.Invalidate(); }) { CheckOnClick = true, Checked = true });
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(new ToolStripLabel("Drag maps edge to edge to connect • Arrow keys nudge (Shift = 10) • Double-click opens • Del removes • Right-click empty space for a new map • Wheel zooms, right-drag pans"));

            Controls.Add(_canvas);
            Controls.Add(new Splitter { Dock = DockStyle.Left });
            Controls.Add(left);
            Controls.Add(_status);
            Controls.Add(bar);

            BuildLayout();
            FillUnplaced();
            Load += (s, e) => _canvas.FitAll();
        }

        public IEnumerable<Placed> PlacedMaps { get { return _placed.Values; } }
        public void SetStatus(string s) { _status.Text = s; }
        void MarkDirty() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }

        string WorldFile { get { return _p.DataFile("world.json"); } }

        // ------------------------------------------------------------------ layout from connections
        static Point EdgePoint(string edge, int offset, MapData m)
        {
            switch (edge)
            {
                case "North": return new Point(offset, 0);
                case "South": return new Point(offset, m.Height);
                case "East": return new Point(m.Width, offset);
                default: return new Point(0, offset);
            }
        }

        void BuildLayout()
        {
            _placed.Clear();
            var saved = new Dictionary<int, Point>();
            try
            {
                if (File.Exists(WorldFile))
                    foreach (var kv in Json.Json.FromFile<Dictionary<string, List<int>>>(WorldFile))
                    {
                        int id; if (int.TryParse(kv.Key, out id) && kv.Value.Count >= 2) saved[id] = new Point(kv.Value[0], kv.Value[1]);
                    }
            }
            catch { }

            var connected = new HashSet<int>();
            foreach (var c in _p.Connections) { connected.Add(c.Map1); connected.Add(c.Map2); }
            var todo = new HashSet<int>(connected.Concat(saved.Keys).Where(id => _p.GetMapInfo(id) != null));
            int nextX = 0;
            while (todo.Count > 0)
            {
                // Each group of connected maps is laid out from one root by walking its connections
                int root = todo.FirstOrDefault(saved.ContainsKey);
                if (root == 0) root = todo.OrderBy(i => i).First();
                var comp = new Dictionary<int, Point> { { root, Point.Empty } };
                var queue = new Queue<int>(); queue.Enqueue(root);
                while (queue.Count > 0)
                {
                    int id = queue.Dequeue();
                    var m = _p.LoadMap(id); if (m == null) continue;
                    foreach (var c in _p.Connections)
                    {
                        int other; Point d;
                        var m1 = _p.LoadMap(c.Map1); var m2 = _p.LoadMap(c.Map2);
                        if (m1 == null || m2 == null) continue;
                        var e1 = EdgePoint(c.Edge1, c.Offset1, m1); var e2 = EdgePoint(c.Edge2, c.Offset2, m2);
                        if (c.Map1 == id) { other = c.Map2; d = new Point(e1.X - e2.X, e1.Y - e2.Y); }
                        else if (c.Map2 == id) { other = c.Map1; d = new Point(e2.X - e1.X, e2.Y - e1.Y); }
                        else continue;
                        if (comp.ContainsKey(other)) continue;
                        comp[other] = new Point(comp[id].X + d.X, comp[id].Y + d.Y);
                        queue.Enqueue(other);
                    }
                }
                // Anchor the group where it was saved, otherwise put it to the right of everything else
                Point anchor;
                int anchorId = comp.Keys.FirstOrDefault(saved.ContainsKey);
                if (anchorId != 0) anchor = new Point(saved[anchorId].X - comp[anchorId].X, saved[anchorId].Y - comp[anchorId].Y);
                else
                {
                    int minX = comp.Values.Min(pt => pt.X), minY = comp.Values.Min(pt => pt.Y);
                    anchor = new Point(nextX - minX, -minY);
                }
                foreach (var kv in comp)
                {
                    var m = _p.LoadMap(kv.Key); if (m == null) continue;
                    var pl = new Placed { Id = kv.Key, Map = m, X = kv.Value.X + anchor.X, Y = kv.Value.Y + anchor.Y };
                    _placed[kv.Key] = pl;
                    nextX = Math.Max(nextX, pl.X + m.Width + 8);
                    todo.Remove(kv.Key);
                }
                todo.Remove(root);
            }
        }

        void FillUnplaced()
        {
            _unplaced.BeginUpdate();
            _unplaced.Items.Clear();
            foreach (var mi in _p.MapInfos.OrderBy(m => m.Id))
            {
                if (_placed.ContainsKey(mi.Id)) continue;
                if (_outdoorOnly.Checked)
                {
                    var m = _p.LoadMap(mi.Id);
                    if (m == null || m.Properties == null || !m.Properties.Outdoor) continue;
                }
                _unplaced.Items.Add(mi);
            }
            _unplaced.EndUpdate();
        }

        void PlaceSelected()
        {
            var mi = _unplaced.SelectedItem as MapInfo;
            if (mi == null) return;
            var b = _placed.Count == 0 ? Rectangle.Empty : WorldBounds();
            PlaceAt(mi.Id, new Point(b.Right + 4, b.Top));
        }

        public void PlaceAt(int id, Point tile)
        {
            var m = _p.LoadMap(id);
            if (m == null || _placed.ContainsKey(id)) return;
            var pl = new Placed { Id = id, Map = m, X = tile.X, Y = tile.Y };
            _placed[id] = pl;
            Snap(pl);
            Reconnect(pl);
            FillUnplaced();
            _canvas.Invalidate();
            MarkDirty();
        }

        public Rectangle WorldBounds()
        {
            if (_placed.Count == 0) return new Rectangle(0, 0, 40, 30);
            var r = _placed.Values.First().Rect;
            foreach (var p in _placed.Values) r = Rectangle.Union(r, p.Rect);
            return r;
        }

        // ------------------------------------------------------------------ snapping and connections
        /// <summary>Moves a map up to 3 tiles so it lines up with a neighbour's edge.</summary>
        public void Snap(Placed m)
        {
            const int range = 3;
            int bestDx = int.MaxValue, bestDy = int.MaxValue;
            foreach (var o in _placed.Values)
            {
                if (o == m) continue;
                var a = m.Rect; var b = o.Rect;
                bool vOverlap = a.Top < b.Bottom + range && a.Bottom > b.Top - range;
                bool hOverlap = a.Left < b.Right + range && a.Right > b.Left - range;
                if (vOverlap)
                {
                    foreach (int dx in new[] { b.Right - a.Left, b.Left - a.Right }) if (Math.Abs(dx) <= range && Math.Abs(dx) < Math.Abs(bestDx)) bestDx = dx;
                    if (Math.Abs(b.Top - a.Top) <= range && Math.Abs(b.Top - a.Top) < Math.Abs(bestDy)) bestDy = b.Top - a.Top;
                }
                if (hOverlap)
                {
                    foreach (int dy in new[] { b.Bottom - a.Top, b.Top - a.Bottom }) if (Math.Abs(dy) <= range && Math.Abs(dy) < Math.Abs(bestDy)) bestDy = dy;
                    if (Math.Abs(b.Left - a.Left) <= range && Math.Abs(b.Left - a.Left) < Math.Abs(bestDx)) bestDx = b.Left - a.Left;
                }
            }
            if (bestDx != int.MaxValue) m.X += bestDx;
            if (bestDy != int.MaxValue) m.Y += bestDy;
        }

        /// <summary>Replaces a map's connections with one for every neighbour whose edge it touches.</summary>
        public void Reconnect(Placed m)
        {
            _p.Connections.RemoveAll(c => c.Map1 == m.Id || c.Map2 == m.Id);
            foreach (var o in _placed.Values)
            {
                if (o == m) continue;
                var c = Touching(m, o);
                if (c != null) _p.Connections.Add(c);
            }
            MarkDirty();
            UpdateStatus(m);
        }

        public static MapConnection Touching(Placed a, Placed b)
        {
            var ra = a.Rect; var rb = b.Rect;
            bool vOverlap = ra.Top < rb.Bottom && ra.Bottom > rb.Top;
            bool hOverlap = ra.Left < rb.Right && ra.Right > rb.Left;
            if (vOverlap && rb.Left == ra.Right) return new MapConnection { Map1 = a.Id, Edge1 = "East", Offset1 = Math.Max(0, rb.Top - ra.Top), Map2 = b.Id, Edge2 = "West", Offset2 = Math.Max(0, ra.Top - rb.Top) };
            if (vOverlap && ra.Left == rb.Right) return new MapConnection { Map1 = a.Id, Edge1 = "West", Offset1 = Math.Max(0, rb.Top - ra.Top), Map2 = b.Id, Edge2 = "East", Offset2 = Math.Max(0, ra.Top - rb.Top) };
            if (hOverlap && rb.Top == ra.Bottom) return new MapConnection { Map1 = a.Id, Edge1 = "South", Offset1 = Math.Max(0, rb.Left - ra.Left), Map2 = b.Id, Edge2 = "North", Offset2 = Math.Max(0, ra.Left - rb.Left) };
            if (hOverlap && ra.Top == rb.Bottom) return new MapConnection { Map1 = a.Id, Edge1 = "North", Offset1 = Math.Max(0, rb.Left - ra.Left), Map2 = b.Id, Edge2 = "South", Offset2 = Math.Max(0, ra.Left - rb.Left) };
            return null;
        }

        public bool Overlaps(Placed m) { return _placed.Values.Any(o => o != m && o.Rect.IntersectsWith(m.Rect)); }

        public void UpdateStatus(Placed m)
        {
            if (m == null) { SetStatus(_placed.Count + " maps on the world, " + _p.Connections.Count + " connections"); return; }
            var mi = _p.GetMapInfo(m.Id);
            var links = _p.Connections.Where(c => c.Map1 == m.Id || c.Map2 == m.Id)
                .Select(c => { int o = c.Map1 == m.Id ? c.Map2 : c.Map1; var omi = _p.GetMapInfo(o); int myOff = c.Map1 == m.Id ? c.Offset1 : c.Offset2, otherOff = c.Map1 == m.Id ? c.Offset2 : c.Offset1;
                    string edge = c.Map1 == m.Id ? c.Edge1 : c.Edge2; bool side = edge == "East" || edge == "West";
                    string shift = myOff != 0 ? " (" + myOff + (side ? " tiles higher)" : " tiles further left)") : otherOff != 0 ? " (" + otherOff + (side ? " tiles lower)" : " tiles further right)") : " (aligned)";
                    return (c.Map1 == m.Id ? c.Edge1 : c.Edge2) + ": " + (omi != null ? omi.Name : o.ToString()) + shift; });
            SetStatus(m.Id.ToString("000") + " " + (mi != null ? mi.Name : "") + "  (" + m.Map.Width + "×" + m.Map.Height + ")   " +
                (Overlaps(m) ? "⚠ overlaps another map   " : "") + "Connected — " + (links.Any() ? string.Join(", ", links) : "none"));
        }

        public void Remove(Placed m)
        {
            _placed.Remove(m.Id);
            _p.Connections.RemoveAll(c => c.Map1 == m.Id || c.Map2 == m.Id);
            FillUnplaced(); _canvas.Invalidate(); MarkDirty(); UpdateStatus(null);
        }

        public void OpenMap(int id) { _main.OpenMap(id); }

        /// <summary>Creates a new outdoor map with its top-left corner at a world tile, connected to whatever it touches.</summary>
        public void NewMapAt(Point tile)
        {
            string name = Prompt.Ask(FindForm(), "New map", "Map name:", "Route " + (_p.MapInfos.Count + 1));
            if (string.IsNullOrEmpty(name)) return;
            string size = Prompt.Ask(FindForm(), "New map", "Size in tiles (width x height):", "30x30");
            if (string.IsNullOrEmpty(size)) return;
            var parts = size.ToLowerInvariant().Replace('×', 'x').Split('x', ',', ' ').Where(x => x != "").ToArray();
            int w, h;
            if (parts.Length != 2 || !int.TryParse(parts[0], out w) || !int.TryParse(parts[1], out h) || w < 1 || h < 1 || w > 500 || h > 500)
            { MessageBox.Show(FindForm(), "Enter a size like 30x30.", "New map"); return; }
            // Use the tileset of the nearest map on the world, so outdoor maps match their neighbours
            int ts = _p.Tilesets.Count > 0 ? _p.Tilesets[0].Id : 1;
            var near = _placed.Values.OrderBy(o => Math.Abs(o.X - tile.X) + Math.Abs(o.Y - tile.Y)).FirstOrDefault();
            if (near != null) ts = near.Map.TilesetId;
            var m = _p.CreateMap(name, 0, w, h, ts);
            m.Properties = m.Properties ?? new MapProperties();
            m.Properties.Name = name; m.Properties.Outdoor = true; m.Properties.ShowArea = true;
            if (near != null) { m.Bgm = near.Map.Bgm; m.AutoplayBgm = near.Map.AutoplayBgm; }
            _p.SaveMap(m);
            Json.Json.ToFile(_p.DataFile("mapinfos.json"), _p.MapInfos);
            PlaceAt(m.Id, tile);
            _main.RefreshTree();
        }

        public void Save()
        {
            Json.Json.ToFile(_p.DataFile("map_connections.json"), _p.Connections);
            var positions = new Dictionary<string, List<int>>();
            foreach (var pl in _placed.Values.OrderBy(x => x.Id)) positions[pl.Id.ToString()] = new List<int> { pl.X, pl.Y };
            Json.Json.ToFile(WorldFile, positions);
            Dirty = false; if (DirtyChanged != null) DirtyChanged();
        }
    }

    /// <summary>Zoomable, pannable canvas that draws map thumbnails and lets you drag them.</summary>
    public class WorldCanvas : Control
    {
        const int TilePx = 8; // thumbnail resolution
        readonly WorldEditor _ed;
        readonly Project _p;
        readonly Dictionary<int, Bitmap> _thumbs = new Dictionary<int, Bitmap>();
        readonly Dictionary<int, TileGraphics> _tilesets = new Dictionary<int, TileGraphics>();
        float _zoom = 0.5f;
        PointF _pan = new PointF(20, 20);
        WorldEditor.Placed _drag, _selected;
        Point _dragOffset, _panStart; PointF _panOrigin; bool _panning;
        public bool ShowNames = true;

        public WorldCanvas(WorldEditor ed, Project p)
        {
            _ed = ed; _p = p;
            DoubleBuffered = true; AllowDrop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        float Px { get { return TilePx * _zoom; } }
        RectangleF Screen(Rectangle tiles) { return new RectangleF(_pan.X + tiles.X * Px, _pan.Y + tiles.Y * Px, tiles.Width * Px, tiles.Height * Px); }
        Point TileAt(Point screen) { return new Point((int)Math.Floor((screen.X - _pan.X) / Px), (int)Math.Floor((screen.Y - _pan.Y) / Px)); }

        public void ZoomBy(float f, Point? around = null)
        {
            var c = around ?? new Point(Width / 2, Height / 2);
            float old = _zoom;
            _zoom = Math.Max(0.05f, Math.Min(4f, _zoom * f));
            _pan = new PointF(c.X - (c.X - _pan.X) * _zoom / old, c.Y - (c.Y - _pan.Y) * _zoom / old);
            Invalidate();
        }

        public void FitAll()
        {
            var b = _ed.WorldBounds();
            if (Width < 10 || Height < 10) return;
            _zoom = Math.Max(0.05f, Math.Min(2f, Math.Min((Width - 40f) / (b.Width * TilePx), (Height - 40f) / (b.Height * TilePx))));
            _pan = new PointF(20 - b.X * Px + (Width - 40 - b.Width * Px) / 2, 20 - b.Y * Px + (Height - 40 - b.Height * Px) / 2);
            _ed.UpdateStatus(null);
            Invalidate();
        }

        Bitmap Thumb(WorldEditor.Placed pl)
        {
            Bitmap b;
            if (_thumbs.TryGetValue(pl.Id, out b)) return b;
            var m = pl.Map;
            TileGraphics tg;
            if (!_tilesets.TryGetValue(m.TilesetId, out tg)) { tg = new TileGraphics(_p, _p.GetTileset(m.TilesetId)); _tilesets[m.TilesetId] = tg; }
            b = new Bitmap(Math.Max(1, m.Width * TilePx), Math.Max(1, m.Height * TilePx), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.FromArgb(24, 24, 28));
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                for (int z = 0; z < 3; z++)
                    for (int y = 0; y < m.Height; y++)
                        for (int x = 0; x < m.Width; x++)
                        {
                            int id = m.GetTile(x, y, z);
                            if (id > 0) tg.Draw(g, id, x * TilePx, y * TilePx, TilePx);
                        }
            }
            _thumbs[pl.Id] = b;
            return b;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Dark ? Color.FromArgb(22, 23, 26) : Color.FromArgb(70, 120, 170));
            // Grid every 10 tiles
            using (var grid = new Pen(Color.FromArgb(30, 255, 255, 255)))
            {
                float step = 10 * Px;
                if (step >= 6)
                {
                    for (float x = _pan.X % step; x < Width; x += step) g.DrawLine(grid, x, 0, x, Height);
                    for (float y = _pan.Y % step; y < Height; y += step) g.DrawLine(grid, 0, y, Width, y);
                }
            }
            g.InterpolationMode = _zoom >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            foreach (var pl in _ed.PlacedMaps.OrderBy(x => x == _drag ? 1 : 0))
            {
                var r = Screen(pl.Rect);
                if (!r.IntersectsWith(e.ClipRectangle)) continue;
                g.DrawImage(Thumb(pl), r);
                bool overlap = _ed.Overlaps(pl);
                var border = overlap ? Color.OrangeRed : pl == _selected ? Color.Gold : Color.FromArgb(160, 255, 255, 255);
                using (var pen = new Pen(border, pl == _selected || overlap ? 3 : 1)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
            // Connected edges in green
            using (var pen = new Pen(Color.LimeGreen, 4))
                foreach (var c in _p.Connections)
                {
                    var a = _ed.PlacedMaps.FirstOrDefault(x => x.Id == c.Map1); var b = _ed.PlacedMaps.FirstOrDefault(x => x.Id == c.Map2);
                    if (a == null || b == null) continue;
                    var ra = a.Rect; var rb = b.Rect;
                    Rectangle seg;
                    if (c.Edge1 == "East" || c.Edge1 == "West")
                    {
                        int x = c.Edge1 == "East" ? ra.Right : ra.Left;
                        seg = new Rectangle(x, Math.Max(ra.Top, rb.Top), 0, Math.Min(ra.Bottom, rb.Bottom) - Math.Max(ra.Top, rb.Top));
                    }
                    else
                    {
                        int y = c.Edge1 == "South" ? ra.Bottom : ra.Top;
                        seg = new Rectangle(Math.Max(ra.Left, rb.Left), y, Math.Min(ra.Right, rb.Right) - Math.Max(ra.Left, rb.Left), 0);
                    }
                    var s = Screen(seg);
                    g.DrawLine(pen, s.Left, s.Top, s.Right, s.Bottom);
                }
            if (ShowNames)
                using (var font = new Font("Segoe UI", Math.Max(7f, Math.Min(12f, 9f * _zoom * 2)), FontStyle.Bold))
                using (var bg = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                    foreach (var pl in _ed.PlacedMaps)
                    {
                        var r = Screen(pl.Rect);
                        var mi = _p.GetMapInfo(pl.Id);
                        string name = mi != null ? mi.Name : pl.Id.ToString();
                        var sz = g.MeasureString(name, font);
                        if (sz.Width > r.Width + 40) continue;
                        g.FillRectangle(bg, r.X + 3, r.Y + 3, sz.Width + 4, sz.Height);
                        g.DrawString(name, font, Brushes.White, r.X + 5, r.Y + 3);
                    }
        }

        WorldEditor.Placed At(Point screen)
        {
            var t = TileAt(screen);
            return _ed.PlacedMaps.LastOrDefault(pl => pl.Rect.Contains(t));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                var hit = At(e.Location);
                if (e.Button == MouseButtons.Right && hit != null) { ShowMenu(hit, e.Location); return; }
                _panning = true; _panStart = e.Location; _panOrigin = _pan; return;
            }
            _selected = _drag = At(e.Location);
            if (_drag != null)
            {
                var t = TileAt(e.Location);
                _dragOffset = new Point(t.X - _drag.X, t.Y - _drag.Y);
                _ed.UpdateStatus(_drag);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_panning) { _pan = new PointF(_panOrigin.X + e.X - _panStart.X, _panOrigin.Y + e.Y - _panStart.Y); Invalidate(); return; }
            if (_drag != null && e.Button == MouseButtons.Left)
            {
                var t = TileAt(e.Location);
                int nx = t.X - _dragOffset.X, ny = t.Y - _dragOffset.Y;
                if (nx != _drag.X || ny != _drag.Y) { _drag.X = nx; _drag.Y = ny; _ed.Snap(_drag); _ed.UpdateStatus(_drag); Invalidate(); }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_panning)
            {
                _panning = false;
                if (e.Button == MouseButtons.Right && Math.Abs(e.X - _panStart.X) + Math.Abs(e.Y - _panStart.Y) < 4)
                {
                    var tile = TileAt(e.Location);
                    var menu = new ContextMenuStrip();
                    menu.Items.Add("New Map Here…", null, (s, a) => _ed.NewMapAt(tile));
                    menu.Items.Add("Fit All", null, (s, a) => FitAll());
                    Theme.Apply(menu);
                    menu.Show(this, e.Location);
                }
                return;
            }
            if (_drag != null) { _ed.Reconnect(_drag); _drag = null; Invalidate(); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            var hit = At(e.Location);
            if (hit != null && e.Button == MouseButtons.Left) _ed.OpenMap(hit.Id);
        }

        protected override bool IsInputKey(Keys k)
        {
            var b = k & Keys.KeyCode;
            return b == Keys.Left || b == Keys.Right || b == Keys.Up || b == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_selected == null) return;
            int dx = 0, dy = 0;
            switch (e.KeyCode)
            {
                case Keys.Left: dx = -1; break;
                case Keys.Right: dx = 1; break;
                case Keys.Up: dy = -1; break;
                case Keys.Down: dy = 1; break;
                case Keys.Delete: _ed.Remove(_selected); _selected = null; Invalidate(); return;
                case Keys.Enter: _ed.OpenMap(_selected.Id); return;
                default: return;
            }
            int step = e.Shift ? 10 : 1;
            _selected.X += dx * step; _selected.Y += dy * step;
            _ed.Reconnect(_selected);
            Invalidate();
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseEventArgs e) { ZoomBy(e.Delta > 0 ? 1.2f : 1 / 1.2f, e.Location); }

        void ShowMenu(WorldEditor.Placed pl, Point at)
        {
            _selected = pl; Invalidate(); _ed.UpdateStatus(pl);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Map", null, (s, a) => _ed.OpenMap(pl.Id));
            menu.Items.Add("Disconnect From Neighbours", null, (s, a) => { _p.Connections.RemoveAll(c => c.Map1 == pl.Id || c.Map2 == pl.Id); pl.X += 2; pl.Y += 2; _ed.Reconnect(pl); Invalidate(); });
            menu.Items.Add("Remove From World", null, (s, a) => _ed.Remove(pl));
            Theme.Apply(menu);
            menu.Show(this, at);
        }

        protected override void OnDragEnter(DragEventArgs e) { if (e.Data.GetDataPresent(typeof(MapInfo))) e.Effect = DragDropEffects.Move; }

        protected override void OnDragDrop(DragEventArgs e)
        {
            var mi = e.Data.GetData(typeof(MapInfo)) as MapInfo;
            if (mi == null) return;
            var pt = PointToClient(new Point(e.X, e.Y));
            _ed.PlaceAt(mi.Id, TileAt(pt));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (var b in _thumbs.Values) b.Dispose(); foreach (var t in _tilesets.Values) t.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
