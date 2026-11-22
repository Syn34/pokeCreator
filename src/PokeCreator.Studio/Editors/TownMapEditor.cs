using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Visual Town Map editor: the region image with a 16-pixel grid. Click a square to see or add a place there;
    /// set its name, point of interest and Fly destination; and put maps on the town map (their "Town map position").
    /// </summary>
    public class TownMapEditor : UserControl, IDocument
    {
        const int Sq = 16;
        readonly Project _p;
        readonly ComboBox _region = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        readonly TextBox _regionName = new TextBox { Dock = DockStyle.Top };
        readonly ComboBox _image = new ComboBox { Dock = DockStyle.Top };
        readonly MapView _view;
        readonly Panel _props = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
        readonly Label _where = new Label { Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        TownMapRegion _r;
        Image _img;
        int _selX = -1, _selY = -1;
        int _zoom = 2;
        bool _loading;

        public bool Dirty { get; private set; }
        public string Title { get { return "Town Maps"; } }
        public event Action DirtyChanged;

        public TownMapEditor(Project p)
        {
            _p = p;
            Dock = DockStyle.Fill;
            _view = new MapView(this) { Dock = DockStyle.Fill };

            var left = new Panel { Dock = DockStyle.Left, Width = 230, Padding = new Padding(8) };
            var addRegion = new Button { Text = "New region", Dock = DockStyle.Top, Height = 28 };
            addRegion.Click += (s, e) =>
            {
                int id = _p.TownMaps.Count == 0 ? 0 : _p.TownMaps.Select(r => { int n; return int.TryParse(r.Id, out n) ? n : 0; }).Max() + 1;
                _p.TownMaps.Add(new TownMapRegion { Id = id.ToString(), Name = "New region" });
                MarkDirty(); FillRegions(); _region.SelectedIndex = _region.Items.Count - 1;
            };
            var zoom = new CheckBox { Text = "Zoom ×2", Checked = true, Dock = DockStyle.Top, Height = 26 };
            zoom.CheckedChanged += (s, e) => { _zoom = zoom.Checked ? 2 : 1; _view.UpdateSize(); };
            var help = new Label
            {
                Dock = DockStyle.Top, Height = 150,
                Text = "Click a square on the map to select it.\n\nPlaces (red) are what the Town Map names, and where Fly can go (blue = has a Fly destination).\n\nUse \"Put a map here\" so the Town Map shows the player there when they are on that map."
            };
            // Top-docked controls stack in reverse order of adding, so add them bottom-up
            var order = new Control[] { Lbl("Region"), _region, addRegion, Lbl("Region name"), _regionName, Lbl("Map image (Graphics/UI/Town Map)"), _image, zoom, help };
            for (int i = order.Length - 1; i >= 0; i--) left.Controls.Add(order[i]);

            var right = new Panel { Dock = DockStyle.Right, Width = 300 };
            right.Controls.Add(_props);
            right.Controls.Add(_where);

            var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(40, 40, 48) };
            scroller.Controls.Add(_view);
            _view.Dock = DockStyle.None; _view.Location = new Point(8, 8);

            Controls.Add(scroller);
            Controls.Add(right);
            Controls.Add(left);

            _region.SelectedIndexChanged += (s, e) => SelectRegion();
            _regionName.TextChanged += (s, e) => { if (_loading || _r == null) return; _r.Name = _regionName.Text; MarkDirty(); FillRegionsKeep(); };
            _image.TextChanged += (s, e) => { if (_loading || _r == null) return; _r.Filename = _image.Text; LoadImage(); MarkDirty(); };
            foreach (var f in ImageNames()) _image.Items.Add(f);
            FillRegions();
            if (_region.Items.Count > 0) _region.SelectedIndex = 0;
        }

        static Label Lbl(string t) { return new Label { Text = t, Dock = DockStyle.Top, Height = 20, Padding = new Padding(0, 4, 0, 0) }; }

        IEnumerable<string> ImageNames()
        {
            var dir = System.IO.Path.Combine(_p.GraphicsDir, "UI", "Town Map");
            if (!System.IO.Directory.Exists(dir)) return new string[0];
            return System.IO.Directory.GetFiles(dir, "*.png").Select(System.IO.Path.GetFileName).Where(n => n.StartsWith("mapRegion", StringComparison.OrdinalIgnoreCase) || !n.StartsWith("icon") && !n.StartsWith("player") && n != "bg.png" && n != "cursor.png").OrderBy(n => n);
        }

        void FillRegions()
        {
            _loading = true;
            _region.Items.Clear();
            foreach (var r in _p.TownMaps) _region.Items.Add(r.Id + ": " + (r.Name ?? ""));
            _loading = false;
        }

        void FillRegionsKeep()
        {
            int i = _region.SelectedIndex;
            _loading = true;
            if (i >= 0 && i < _p.TownMaps.Count)
            {
                string t = _p.TownMaps[i].Id + ": " + (_p.TownMaps[i].Name ?? "");
                if (!Equals(_region.Items[i], t)) { _region.Items[i] = t; _region.SelectedIndex = i; }
            }
            _loading = false;
        }

        void SelectRegion()
        {
            if (_loading) return;
            int i = _region.SelectedIndex;
            _r = i >= 0 && i < _p.TownMaps.Count ? _p.TownMaps[i] : null;
            _loading = true;
            _regionName.Text = _r != null ? _r.Name ?? "" : "";
            _image.Text = _r != null ? _r.Filename ?? "" : "";
            _loading = false;
            _selX = _selY = -1;
            LoadImage();
            ShowSquare();
        }

        void LoadImage()
        {
            if (_img != null) { _img.Dispose(); _img = null; }
            if (_r != null && !string.IsNullOrEmpty(_r.Filename))
                _img = Theme.LoadImage(_p.FindAsset("Graphics/UI/Town Map", System.IO.Path.GetFileNameWithoutExtension(_r.Filename)));
            _view.UpdateSize();
        }

        void MarkDirty() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } _view.Invalidate(); }

        public void Save()
        {
            Json.Json.ToFile(_p.DataFile("town_maps.json"), _p.TownMaps);
            Dirty = false;
            if (DirtyChanged != null) DirtyChanged();
        }

        int RegionId { get { int n; return _r != null && int.TryParse(_r.Id, out n) ? n : 0; } }

        /// <summary>Town map position of every map (region, x, y), read once.</summary>
        Dictionary<int, List<int>> _mapPos;
        Dictionary<int, List<int>> MapPositions
        {
            get
            {
                if (_mapPos != null) return _mapPos;
                _mapPos = new Dictionary<int, List<int>>();
                foreach (var mi in _p.MapInfos)
                {
                    MapData md; try { md = _p.LoadMap(mi.Id); } catch { continue; }
                    var pos = md != null && md.Properties != null ? md.Properties.MapPosition : null;
                    if (pos != null && pos.Count >= 3) _mapPos[mi.Id] = pos;
                }
                return _mapPos;
            }
        }

        /// <summary>Maps whose Town map position is on (x, y) of this region.</summary>
        List<MapInfo> MapsAt(int x, int y)
        {
            return _p.MapInfos.Where(mi => { List<int> pos; return MapPositions.TryGetValue(mi.Id, out pos) && pos[0] == RegionId && pos[1] == x && pos[2] == y; }).ToList();
        }

        // ------------------------------------------------------------------ properties panel
        void ShowSquare()
        {
            _props.Controls.Clear();
            if (_r == null) { _where.Text = "No region. Click New region."; return; }
            if (_selX < 0) { _where.Text = "Click a square on the map."; return; }
            _where.Text = "Square " + _selX + ", " + _selY;
            var pt = _r.Points.FirstOrDefault(q => q.X == _selX && q.Y == _selY);
            var rows = new List<Control>();
            if (pt == null)
            {
                var add = new Button { Text = "Add a place here", Height = 30, Width = 260 };
                add.Click += (s, e) => { _r.Points.Add(new TownMapPoint { X = _selX, Y = _selY, Name = "New place" }); MarkDirty(); ShowSquare(); };
                rows.Add(add);
            }
            else
            {
                rows.Add(Lbl2("Place name"));
                var name = new TextBox { Text = pt.Name ?? "", Width = 260 };
                name.TextChanged += (s, e) => { pt.Name = name.Text; MarkDirty(); };
                rows.Add(name);
                rows.Add(Lbl2("Point of interest (optional)"));
                var poi = new TextBox { Text = pt.PointOfInterest ?? "", Width = 260 };
                poi.TextChanged += (s, e) => { pt.PointOfInterest = poi.Text; MarkDirty(); };
                rows.Add(poi);
                rows.Add(Lbl2("Fly to map (none = no Fly)"));
                var fly = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
                fly.Items.Add("(none)");
                foreach (var mi in _p.MapInfos.OrderBy(m => m.Id)) fly.Items.Add(mi.Id.ToString("000") + ": " + mi.Name);
                var cur = _p.MapInfos.OrderBy(m => m.Id).ToList().FindIndex(m => m.Id == pt.FlyMap);
                fly.SelectedIndex = pt.FlyMap > 0 && cur >= 0 ? cur + 1 : 0;
                var fx = new NumericUpDown { Minimum = 0, Maximum = 999, Value = Math.Max(0, pt.FlyX), Width = 80 };
                var fy = new NumericUpDown { Minimum = 0, Maximum = 999, Value = Math.Max(0, pt.FlyY), Width = 80 };
                fly.SelectedIndexChanged += (s, e) =>
                {
                    pt.FlyMap = fly.SelectedIndex <= 0 ? 0 : _p.MapInfos.OrderBy(m => m.Id).ElementAt(fly.SelectedIndex - 1).Id;
                    MarkDirty();
                };
                fx.ValueChanged += (s, e) => { pt.FlyX = (int)fx.Value; MarkDirty(); };
                fy.ValueChanged += (s, e) => { pt.FlyY = (int)fy.Value; MarkDirty(); };
                rows.Add(fly);
                rows.Add(Lbl2("Arrive at X, Y on that map"));
                var xy = new FlowLayoutPanel { Width = 270, Height = 30 };
                xy.Controls.Add(fx); xy.Controls.Add(fy);
                rows.Add(xy);
                rows.Add(Lbl2("Only when switch # is ON (optional)"));
                var sw = new TextBox { Text = pt.Switch ?? "", Width = 80 };
                sw.TextChanged += (s, e) => { pt.Switch = sw.Text.Trim().Length == 0 ? null : sw.Text.Trim(); MarkDirty(); };
                rows.Add(sw);
                var del = new Button { Text = "Delete this place", Height = 28, Width = 260 };
                del.Click += (s, e) => { _r.Points.Remove(pt); MarkDirty(); ShowSquare(); };
                rows.Add(del);
            }
            rows.Add(Lbl2(""));
            rows.Add(Lbl2("Maps shown at this square:"));
            var maps = MapsAt(_selX, _selY);
            rows.Add(new Label { Text = maps.Count == 0 ? "(none)" : string.Join("\n", maps.Select(m => m.Id.ToString("000") + ": " + m.Name)), AutoSize = true, MaximumSize = new Size(270, 0) });
            var put = new Button { Text = "Put a map here…", Height = 28, Width = 260 };
            put.Click += (s, e) => PutMapHere();
            rows.Add(put);
            int y = 0;
            foreach (var c in rows) { c.Location = new Point(4, y); _props.Controls.Add(c); y += c.Height + 4; }
        }

        static Label Lbl2(string t) { return new Label { Text = t, Width = 270, Height = 20 }; }

        void PutMapHere()
        {
            var ids = _p.MapInfos.OrderBy(m => m.Id).ToList();
            int k = ChoiceDialog.Ask(FindForm(), "Which map is at square " + _selX + ", " + _selY + "?", ids.Select(m => m.Id.ToString("000") + ": " + m.Name).ToArray(), true);
            if (k < 0) return;
            var md = _p.LoadMap(ids[k].Id);
            if (md.Properties == null) md.Properties = new MapProperties();
            md.Properties.MapPosition = new List<int> { RegionId, _selX, _selY };
            _p.SaveMap(md);
            MapPositions[ids[k].Id] = md.Properties.MapPosition;
            ShowSquare();
            _view.Invalidate();
        }

        // ------------------------------------------------------------------ canvas
        class MapView : Control
        {
            readonly TownMapEditor _ed;
            public MapView(TownMapEditor ed) { _ed = ed; DoubleBuffered = true; Size = new Size(480 * 2, 320 * 2); }

            public void UpdateSize()
            {
                int w = _ed._img != null ? _ed._img.Width : 480, h = _ed._img != null ? _ed._img.Height : 320;
                Size = new Size(w * _ed._zoom, h * _ed._zoom);
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                int z = _ed._zoom;
                _ed._selX = e.X / (Sq * z); _ed._selY = e.Y / (Sq * z);
                _ed.ShowSquare();
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                int z = _ed._zoom;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(Color.FromArgb(96, 168, 96));
                if (_ed._img != null) g.DrawImage(_ed._img, 0, 0, _ed._img.Width * z, _ed._img.Height * z);
                using (var grid = new Pen(Color.FromArgb(40, 0, 0, 0)))
                {
                    for (int x = 0; x <= Width; x += Sq * z) g.DrawLine(grid, x, 0, x, Height);
                    for (int y = 0; y <= Height; y += Sq * z) g.DrawLine(grid, 0, y, Width, y);
                }
                if (_ed._r == null) return;
                // Squares used by maps
                using (var mapBrush = new SolidBrush(Color.FromArgb(70, 255, 255, 0)))
                    foreach (var pos in _ed.MapPositions.Values)
                        if (pos[0] == _ed.RegionId) g.FillRectangle(mapBrush, pos[1] * Sq * z, pos[2] * Sq * z, Sq * z, Sq * z);
                foreach (var pt in _ed._r.Points)
                {
                    var c = pt.FlyMap > 0 ? Color.FromArgb(220, 40, 120, 240) : Color.FromArgb(220, 232, 64, 48);
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, pt.X * Sq * z + 3 * z, pt.Y * Sq * z + 3 * z, (Sq - 6) * z, (Sq - 6) * z);
                }
                if (_ed._selX >= 0)
                    using (var sel = new Pen(Color.White, 2))
                        g.DrawRectangle(sel, _ed._selX * Sq * z, _ed._selY * Sq * z, Sq * z - 1, Sq * z - 1);
            }
        }
    }
}
