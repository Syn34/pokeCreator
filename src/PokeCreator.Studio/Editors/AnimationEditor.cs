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
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Battle animation editor (Essentials' format): pick an animation, watch it play over two Pokémon, step through
    /// its frames, drag cels around, pick their picture from the sheet and change their zoom, angle, opacity and focus.
    /// Cel 0 is the user's sprite and cel 1 the target's; effects are cel 2 and up.
    /// </summary>
    public class AnimationEditor : UserControl, IDocument
    {
        readonly Project _p;
        readonly TextBox _filter = new TextBox { Dock = DockStyle.Top };
        readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        readonly Canvas _canvas;
        readonly SheetView _sheetView;
        readonly TrackBar _scrub = new TrackBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 0, TickStyle = TickStyle.None };
        readonly Label _frameLabel = new Label { Dock = DockStyle.Right, Width = 110, TextAlign = ContentAlignment.MiddleCenter };
        readonly Button _play = new Button { Text = "▶ Play", Dock = DockStyle.Left, Width = 90 };
        readonly Timer _timer = new Timer { Interval = 50 }; // 20 frames a second, as in Essentials
        readonly TextBox _name = new TextBox { Dock = DockStyle.Top };
        readonly ComboBox _graphic = new ComboBox { Dock = DockStyle.Top };
        readonly NumericUpDown _hue = new NumericUpDown { Dock = DockStyle.Top, Minimum = 0, Maximum = 359 };
        readonly ListBox _cels = new ListBox { Dock = DockStyle.Top, Height = 110, IntegralHeight = false };
        readonly Panel _celProps = new Panel { Dock = DockStyle.Top, Height = 330 };
        readonly ListBox _timings = new ListBox { Dock = DockStyle.Top, Height = 80, IntegralHeight = false };
        readonly ComboBox _userSpecies = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox _targetSpecies = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly Dictionary<string, NumericUpDown> _num = new Dictionary<string, NumericUpDown>();
        readonly ComboBox _focus = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        readonly ComboBox _blend = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        readonly ComboBox _priority = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        readonly CheckBox _mirror = new CheckBox { Text = "Mirror", Width = 70 };
        readonly CheckBox _visible = new CheckBox { Text = "Visible", Width = 70 };

        BattleAnimation _a;
        int _frame, _cel = -1;
        bool _loading;
        readonly Dictionary<string, Image> _images = new Dictionary<string, Image>();

        public bool Dirty { get; private set; }
        public string Title { get { return "Battle Animations"; } }
        public event Action DirtyChanged;

        public AnimationEditor(Project p)
        {
            _p = p;
            Dock = DockStyle.Fill;
            _canvas = new Canvas(this) { Width = 512, Height = 288 };
            _sheetView = new SheetView(this) { Dock = DockStyle.Fill };

            // ---- left: list of animations
            var left = new Panel { Dock = DockStyle.Left, Width = 250, Padding = new Padding(6) };
            var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66 };
            leftButtons.Controls.Add(Btn("New", NewAnimation));
            leftButtons.Controls.Add(Btn("Duplicate", Duplicate));
            leftButtons.Controls.Add(Btn("Delete", DeleteAnimation));
            leftButtons.Controls.Add(Btn("Import from Essentials…", ImportFromEssentials, 230));
            left.Controls.Add(_list);
            left.Controls.Add(Lbl("Search (Move:, OppMove:, Common:)"));
            left.Controls.Add(_filter);
            left.Controls.Add(leftButtons);
            // Top-docked filter goes above its label
            left.Controls.SetChildIndex(_filter, 0);

            // ---- centre: preview, transport, sheet
            var centre = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            var canvasHost = new Panel { Dock = DockStyle.Top, Height = 300, BackColor = Color.FromArgb(40, 40, 48) };
            canvasHost.Controls.Add(_canvas); _canvas.Location = new Point(6, 6);
            var transport = new Panel { Dock = DockStyle.Top, Height = 34 };
            transport.Controls.Add(_scrub);
            transport.Controls.Add(_frameLabel);
            var frameButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 290 };
            frameButtons.Controls.Add(Btn("+ Frame", AddFrame, 66));
            frameButtons.Controls.Add(Btn("Copy frame", CopyFrame, 80));
            frameButtons.Controls.Add(Btn("Delete frame", DeleteFrame, 90));
            transport.Controls.Add(frameButtons);
            transport.Controls.Add(_play);
            var sheetHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(40, 40, 48) };
            sheetHost.Controls.Add(_sheetView); _sheetView.Dock = DockStyle.None; _sheetView.Location = new Point(0, 0);
            centre.Controls.Add(sheetHost);
            centre.Controls.Add(Lbl("Animation sheet — click a picture to use it for the selected cel"));
            centre.Controls.Add(transport);
            centre.Controls.Add(canvasHost);

            // ---- right: animation and cel properties
            var right = new Panel { Dock = DockStyle.Right, Width = 300, AutoScroll = true, Padding = new Padding(6) };
            BuildCelProps();
            var celButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32 };
            celButtons.Controls.Add(Btn("+ Cel", AddCel, 70));
            celButtons.Controls.Add(Btn("Remove cel", RemoveCel, 90));
            var timingButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32 };
            timingButtons.Controls.Add(Btn("+ Sound here", AddTiming, 100));
            timingButtons.Controls.Add(Btn("Remove", RemoveTiming, 70));
            var order = new Control[] { Lbl("Name"), _name, Lbl("Sheet (Graphics/Animations)"), _graphic, Lbl("Hue"), _hue,
                                        Lbl("Preview user"), _userSpecies, Lbl("Preview target"), _targetSpecies,
                                        Lbl("Cels on this frame"), _cels, celButtons, _celProps,
                                        Lbl("Sounds and flashes (double-click to edit)"), _timings, timingButtons };
            for (int i = order.Length - 1; i >= 0; i--) right.Controls.Add(order[i]);

            Controls.Add(centre);
            Controls.Add(right);
            Controls.Add(left);

            // ---- events
            _filter.TextChanged += (s, e) => FillList();
            _list.SelectedIndexChanged += (s, e) => { if (_loading) return; var a = _list.SelectedItem as BattleAnimation; if (a != null) Select(a); };
            _scrub.ValueChanged += (s, e) => { if (_loading) return; _frame = _scrub.Value; ShowFrame(); };
            _play.Click += (s, e) => TogglePlay();
            _timer.Tick += (s, e) =>
            {
                if (_a == null || _a.Frames.Count == 0) { TogglePlay(); return; }
                _frame++;
                if (_frame >= _a.Frames.Count) { _frame = _a.Frames.Count - 1; TogglePlay(); }
                ShowFrame();
            };
            _name.TextChanged += (s, e) => { if (_loading || _a == null) return; _a.Id = _name.Text.Trim(); MarkDirty(); RefreshListItem(); };
            _graphic.TextChanged += (s, e) => { if (_loading || _a == null) return; _a.Graphic = _graphic.Text; MarkDirty(); _sheetView.UpdateSize(); _canvas.Invalidate(); };
            _hue.ValueChanged += (s, e) => { if (_loading || _a == null) return; _a.Hue = (int)_hue.Value; MarkDirty(); _sheetView.Invalidate(); _canvas.Invalidate(); };
            _cels.SelectedIndexChanged += (s, e) => { if (_loading) return; _cel = _cels.SelectedIndex; ShowCel(); _canvas.Invalidate(); _sheetView.Invalidate(); };
            _timings.DoubleClick += (s, e) => EditTiming();
            _userSpecies.SelectedIndexChanged += (s, e) => _canvas.Invalidate();
            _targetSpecies.SelectedIndexChanged += (s, e) => _canvas.Invalidate();

            foreach (var f in SheetNames()) _graphic.Items.Add(f);
            var withSprites = _p.Species.Where(sp => sp.Form == 0 && _p.FindAsset("Graphics/Pokemon/Front", sp.Id) != null).Select(sp => sp.Id).ToList();
            foreach (var id in withSprites) { _userSpecies.Items.Add(id); _targetSpecies.Items.Add(id); }
            if (withSprites.Count > 0)
            {
                _userSpecies.SelectedIndex = Math.Max(0, withSprites.IndexOf("CHARMANDER"));
                _targetSpecies.SelectedIndex = Math.Max(0, withSprites.IndexOf("PIDGEY") >= 0 ? withSprites.IndexOf("PIDGEY") : Math.Min(1, withSprites.Count - 1));
            }
            FillList();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            else ShowFrame();
        }

        static Label Lbl(string t) { return new Label { Text = t, Dock = DockStyle.Top, Height = 20, Padding = new Padding(0, 4, 0, 0) }; }
        static Button Btn(string t, Action a, int w = 72) { var b = new Button { Text = t, Width = w, Height = 26 }; b.Click += (s, e) => a(); return b; }

        void MarkDirty() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }

        public void Save()
        {
            Json.Json.ToFile(_p.DataFile("animations.json"), _p.Animations);
            Dirty = false;
            if (DirtyChanged != null) DirtyChanged();
        }

        IEnumerable<string> SheetNames()
        {
            string dir = Path.Combine(_p.GraphicsDir, "Animations");
            if (!Directory.Exists(dir)) return new string[0];
            return Directory.GetFiles(dir).Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileNameWithoutExtension).OrderBy(x => x);
        }

        // ------------------------------------------------------------------ list
        void FillList()
        {
            string f = _filter.Text.Trim();
            var keep = _list.SelectedItem;
            bool was = _loading; _loading = true;   // the shown animation stays as it is while filtering
            try
            {
                _list.BeginUpdate();
                _list.Items.Clear();
                foreach (var a in _p.Animations.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
                    if (f.Length == 0 || (a.Id ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) _list.Items.Add(a);
                if (keep != null && _list.Items.Contains(keep)) _list.SelectedItem = keep;
                _list.EndUpdate();
            }
            finally { _loading = was; }
            if (keep == null && _list.SelectedItem == null && _a != null && _list.Items.Contains(_a)) { _loading = true; _list.SelectedItem = _a; _loading = was; }
        }

        void RefreshListItem()
        {
            int i = _list.SelectedIndex;
            if (i >= 0) { _loading = true; try { ListUtil.Rename(_list, i, _list.Items[i]); } finally { _loading = false; } }
        }

        void Select(BattleAnimation a)
        {
            if (_timer.Enabled) TogglePlay();
            _a = a; _frame = 0; _cel = -1;
            _loading = true;
            _name.Text = a.Id; _graphic.Text = a.Graphic ?? ""; _hue.Value = Math.Max(0, Math.Min(359, a.Hue));
            _loading = false;
            _sheetView.UpdateSize();
            FillTimings();
            ShowFrame();
        }

        void NewAnimation()
        {
            var a = new BattleAnimation { Id = UniqueName("Move:NEWMOVE"), Graphic = _graphic.Items.Count > 0 ? (string)_graphic.Items[0] : "" };
            for (int i = 0; i < 10; i++) a.Frames.Add(DefaultFrame());
            _p.Animations.Add(a); MarkDirty(); _filter.Text = ""; FillList(); _list.SelectedItem = a;
        }

        string UniqueName(string baseName)
        {
            string n = baseName; int k = 2;
            while (_p.Animations.Any(x => x.Id == n)) n = baseName + k++;
            return n;
        }

        static List<int[]> DefaultFrame()
        {
            return new List<int[]> { NewCel(BattleAnimation.UserX, BattleAnimation.UserY, -1, BattleAnimation.FocusUser), NewCel(BattleAnimation.TargetX, BattleAnimation.TargetY, -2, BattleAnimation.FocusTarget) };
        }

        static int[] NewCel(int x, int y, int pattern, int focus)
        {
            var c = new int[BattleAnimation.CelLength];
            c[BattleAnimation.X] = x; c[BattleAnimation.Y] = y; c[BattleAnimation.ZoomX] = 100; c[BattleAnimation.ZoomY] = 100;
            c[BattleAnimation.Visible] = 1; c[BattleAnimation.Pattern] = pattern; c[BattleAnimation.Opacity] = 255;
            c[BattleAnimation.Priority] = 1; c[BattleAnimation.Focus] = focus;
            if (pattern < 0) c[BattleAnimation.Locked] = 1;
            return c;
        }

        void Duplicate()
        {
            if (_a == null) return;
            var copy = Json.Json.Deserialize<BattleAnimation>(Json.Json.Write(_a, false));
            copy.Id = UniqueName(_a.Id + "_copy");
            _p.Animations.Add(copy); MarkDirty(); FillList(); _list.SelectedItem = copy;
        }

        void DeleteAnimation()
        {
            if (_a == null) return;
            if (MessageBox.Show(this, "Delete the animation " + _a.Id + "?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _p.Animations.Remove(_a); _a = null; MarkDirty(); FillList();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0; else ShowFrame();
        }

        void ImportFromEssentials()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose your Pokémon Essentials project folder (the one with Data and Graphics)." })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string root = dlg.SelectedPath;
                string file = Path.Combine(root, "Data", "PkmnAnimations.rxdata");
                if (!File.Exists(file)) { MessageBox.Show(this, "That folder has no Data/PkmnAnimations.rxdata.", "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                List<BattleAnimation> found;
                try { found = PokeCreator.Import.EssentialsImporter.ReadAnimations(file); }
                catch (Exception ex) { MessageBox.Show(this, "Could not read the animations:\n" + ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                bool replace = true;
                if (_p.Animations.Count > 0)
                {
                    var r = MessageBox.Show(this, "Found " + found.Count + " animations.\n\nYes = replace animations with the same name\nNo = only add the ones you don't have", "Import", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) return;
                    replace = r == DialogResult.Yes;
                }
                int added = 0;
                foreach (var a in found)
                {
                    var old = _p.Animations.FirstOrDefault(x => x.Id == a.Id);
                    if (old != null) { if (!replace) continue; _p.Animations.Remove(old); }
                    _p.Animations.Add(a); added++;
                }
                int copied = CopyFolder(Path.Combine(root, "Graphics", "Animations"), Path.Combine(_p.GraphicsDir, "Animations"))
                           + CopyFolder(Path.Combine(root, "Audio", "SE", "Anim"), Path.Combine(_p.AudioDir, "SE", "Anim"));
                MarkDirty();
                _graphic.Items.Clear(); foreach (var f in SheetNames()) _graphic.Items.Add(f);
                FillList();
                if (_list.Items.Count > 0 && _a == null) _list.SelectedIndex = 0;
                MessageBox.Show(this, added + " animations imported, " + copied + " sheet and sound files copied.\nSave to keep them.", "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        static int CopyFolder(string from, string to)
        {
            if (!Directory.Exists(from)) return 0;
            Directory.CreateDirectory(to);
            int n = 0;
            foreach (var f in Directory.GetFiles(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(f));
                if (File.Exists(dest)) continue;
                File.Copy(f, dest); n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ frames
        List<int[]> CurrentCels { get { return _a != null && _frame >= 0 && _frame < _a.Frames.Count ? _a.Frames[_frame] : null; } }

        void ShowFrame()
        {
            _loading = true;
            int count = _a != null ? _a.Frames.Count : 0;
            _scrub.Maximum = Math.Max(0, count - 1);
            _frame = Math.Max(0, Math.Min(_frame, Math.Max(0, count - 1)));
            _scrub.Value = Math.Min(_scrub.Maximum, _frame);
            _frameLabel.Text = count == 0 ? "no frames" : "Frame " + (_frame + 1) + " / " + count;
            _cels.Items.Clear();
            var cels = CurrentCels;
            if (cels != null)
                for (int i = 0; i < cels.Count; i++)
                    _cels.Items.Add(i == 0 ? "0: User" : i == 1 ? "1: Target" : cels[i] == null ? i + ": (empty)" : i + ": picture " + cels[i][BattleAnimation.Pattern]);
            if (_cel >= _cels.Items.Count) _cel = _cels.Items.Count - 1;
            _cels.SelectedIndex = _cel;
            _loading = false;
            ShowCel();
            _canvas.Invalidate();
            _sheetView.Invalidate();
        }

        void TogglePlay()
        {
            if (_timer.Enabled) { _timer.Stop(); _play.Text = "▶ Play"; return; }
            if (_a == null || _a.Frames.Count == 0) return;
            if (_frame >= _a.Frames.Count - 1) _frame = 0;
            _timer.Start(); _play.Text = "■ Stop";
        }

        void AddFrame()
        {
            if (_a == null) return;
            _a.Frames.Insert(Math.Min(_a.Frames.Count, _frame + 1), DefaultFrame());
            _frame++; MarkDirty(); ShowFrame();
        }

        void CopyFrame()
        {
            var cels = CurrentCels;
            if (cels == null) return;
            _a.Frames.Insert(_frame + 1, cels.Select(c => c == null ? null : (int[])c.Clone()).ToList());
            _frame++; MarkDirty(); ShowFrame();
        }

        void DeleteFrame()
        {
            if (_a == null || _a.Frames.Count == 0) return;
            _a.Frames.RemoveAt(_frame);
            foreach (var t in _a.Timings.Where(t => t.Frame > _frame)) t.Frame--;
            MarkDirty(); FillTimings(); ShowFrame();
        }

        // ------------------------------------------------------------------ cels
        void BuildCelProps()
        {
            string[] fields = { "X", "Y", "Zoom X %", "Zoom Y %", "Angle", "Opacity", "Picture" };
            int y = 4;
            foreach (var f in fields)
            {
                _celProps.Controls.Add(new Label { Text = f, Left = 0, Top = y + 3, Width = 80 });
                var n = new NumericUpDown { Left = 84, Top = y, Width = 100, Minimum = f == "Picture" ? -2 : -9999, Maximum = 9999 };
                string key = f;
                n.ValueChanged += (s, e) => CelValueChanged(key);
                _num[f] = n;
                _celProps.Controls.Add(n);
                y += 28;
            }
            _focus.Items.AddRange(new object[] { "(none)", "Target", "User", "User and target", "Screen" });
            _blend.Items.AddRange(new object[] { "Normal", "Add", "Subtract" });
            _priority.Items.AddRange(new object[] { "Behind Pokémon", "In front", "Behind user", "In front of user" });
            foreach (var pair in new[] { Tuple.Create("Focus", (Control)_focus), Tuple.Create("Blend", (Control)_blend), Tuple.Create("Layer", (Control)_priority) })
            {
                _celProps.Controls.Add(new Label { Text = pair.Item1, Left = 0, Top = y + 3, Width = 80 });
                pair.Item2.Left = 84; pair.Item2.Top = y;
                _celProps.Controls.Add(pair.Item2);
                y += 28;
            }
            _mirror.Left = 84; _mirror.Top = y; _visible.Left = 160; _visible.Top = y;
            _celProps.Controls.Add(_mirror); _celProps.Controls.Add(_visible);
            y += 30;
            _celProps.Controls.Add(new Label { Text = "Drag cels on the preview to move them. Focus decides what a cel follows when the Pokémon stand elsewhere in a battle.", Left = 0, Top = y, Width = 280, Height = 48 });
            _focus.SelectedIndexChanged += (s, e) => CelValueChanged("Focus");
            _blend.SelectedIndexChanged += (s, e) => CelValueChanged("Blend");
            _priority.SelectedIndexChanged += (s, e) => CelValueChanged("Layer");
            _mirror.CheckedChanged += (s, e) => CelValueChanged("Mirror");
            _visible.CheckedChanged += (s, e) => CelValueChanged("Visible");
        }

        int[] SelectedCel { get { var cels = CurrentCels; return cels != null && _cel >= 0 && _cel < cels.Count ? cels[_cel] : null; } }

        void ShowCel()
        {
            var c = SelectedCel;
            _loading = true;
            _celProps.Enabled = c != null;
            if (c != null)
            {
                SetNum("X", c[BattleAnimation.X]); SetNum("Y", c[BattleAnimation.Y]);
                SetNum("Zoom X %", c[BattleAnimation.ZoomX]); SetNum("Zoom Y %", c[BattleAnimation.ZoomY]);
                SetNum("Angle", c[BattleAnimation.Angle]); SetNum("Opacity", c[BattleAnimation.Opacity]); SetNum("Picture", c[BattleAnimation.Pattern]);
                _focus.SelectedIndex = Math.Max(0, Math.Min(4, c[BattleAnimation.Focus]));
                _blend.SelectedIndex = Math.Max(0, Math.Min(2, c[BattleAnimation.Blend]));
                _priority.SelectedIndex = Math.Max(0, Math.Min(3, c[BattleAnimation.Priority]));
                _mirror.Checked = c[BattleAnimation.Mirror] != 0; _visible.Checked = c[BattleAnimation.Visible] != 0;
            }
            _loading = false;
        }

        void SetNum(string k, int v) { var n = _num[k]; n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v)); }

        void CelValueChanged(string key)
        {
            var c = SelectedCel;
            if (_loading || c == null) return;
            switch (key)
            {
                case "X": c[BattleAnimation.X] = (int)_num[key].Value; break;
                case "Y": c[BattleAnimation.Y] = (int)_num[key].Value; break;
                case "Zoom X %": c[BattleAnimation.ZoomX] = (int)_num[key].Value; break;
                case "Zoom Y %": c[BattleAnimation.ZoomY] = (int)_num[key].Value; break;
                case "Angle": c[BattleAnimation.Angle] = (int)_num[key].Value; break;
                case "Opacity": c[BattleAnimation.Opacity] = Math.Max(0, Math.Min(255, (int)_num[key].Value)); break;
                case "Picture": if (_cel >= 2) c[BattleAnimation.Pattern] = (int)_num[key].Value; break;
                case "Focus": c[BattleAnimation.Focus] = _focus.SelectedIndex; break;
                case "Blend": c[BattleAnimation.Blend] = _blend.SelectedIndex; break;
                case "Layer": c[BattleAnimation.Priority] = _priority.SelectedIndex; break;
                case "Mirror": c[BattleAnimation.Mirror] = _mirror.Checked ? 1 : 0; break;
                case "Visible": c[BattleAnimation.Visible] = _visible.Checked ? 1 : 0; break;
            }
            MarkDirty();
            if (key == "Picture") { int keep = _cel; ShowFrame(); _cel = keep; }
            _canvas.Invalidate(); _sheetView.Invalidate();
        }

        void AddCel()
        {
            var cels = CurrentCels;
            if (cels == null) return;
            int idx = cels.FindIndex(2, c => c == null);
            var cel = NewCel(256, 144, 0, BattleAnimation.FocusScreen);
            if (idx < 0) { cels.Add(cel); idx = cels.Count - 1; } else cels[idx] = cel;
            _cel = idx; MarkDirty(); ShowFrame();
        }

        void RemoveCel()
        {
            var cels = CurrentCels;
            if (cels == null || _cel < 2 || _cel >= cels.Count) return;
            cels[_cel] = null;
            while (cels.Count > 2 && cels[cels.Count - 1] == null) cels.RemoveAt(cels.Count - 1);
            _cel = -1; MarkDirty(); ShowFrame();
        }

        // ------------------------------------------------------------------ timings
        void FillTimings()
        {
            _timings.Items.Clear();
            if (_a == null) return;
            foreach (var t in _a.Timings.OrderBy(x => x.Frame))
                _timings.Items.Add(new TimingItem(t));
        }

        class TimingItem
        {
            public readonly AnimationTiming T;
            public TimingItem(AnimationTiming t) { T = t; }
            public override string ToString()
            {
                string flash = T.FlashScope == 0 ? "" : " flash " + (T.FlashScope == 1 ? "target" : T.FlashScope == 2 ? "user" : "screen");
                return "Frame " + (T.Frame + 1) + ": " + (string.IsNullOrEmpty(T.Sound) ? "(no sound)" : T.Sound + " " + T.Volume + "%") + flash;
            }
        }

        void AddTiming()
        {
            if (_a == null) return;
            var t = new AnimationTiming { Frame = _frame, Sound = "", Volume = 80 };
            if (!EditTimingDialog(t)) return;
            _a.Timings.Add(t); MarkDirty(); FillTimings();
        }

        void RemoveTiming()
        {
            var it = _timings.SelectedItem as TimingItem;
            if (_a == null || it == null) return;
            _a.Timings.Remove(it.T); MarkDirty(); FillTimings();
        }

        void EditTiming()
        {
            var it = _timings.SelectedItem as TimingItem;
            if (it == null) return;
            if (EditTimingDialog(it.T)) { MarkDirty(); FillTimings(); }
        }

        bool EditTimingDialog(AnimationTiming t)
        {
            using (var f = new Form { Text = "Sound / flash", Width = 340, Height = 270, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false })
            {
                var frame = new NumericUpDown { Left = 120, Top = 10, Width = 80, Minimum = 1, Maximum = 999, Value = Math.Max(1, t.Frame + 1) };
                var sound = new ComboBox { Left = 120, Top = 40, Width = 190, Text = t.Sound ?? "" };
                string seDir = Path.Combine(_p.AudioDir, "SE", "Anim");
                if (Directory.Exists(seDir)) foreach (var s in Directory.GetFiles(seDir).Select(Path.GetFileNameWithoutExtension).Distinct().OrderBy(x => x)) sound.Items.Add(s);
                var vol = new NumericUpDown { Left = 120, Top = 70, Width = 80, Minimum = 0, Maximum = 100, Value = Math.Max(0, Math.Min(100, t.Volume)) };
                var scope = new ComboBox { Left = 120, Top = 100, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
                scope.Items.AddRange(new object[] { "No flash", "Target", "User", "Screen" }); scope.SelectedIndex = Math.Max(0, Math.Min(3, t.FlashScope));
                var dur = new NumericUpDown { Left = 120, Top = 130, Width = 80, Minimum = 1, Maximum = 100, Value = Math.Max(1, t.FlashDuration) };
                var ok = new Button { Text = "OK", Left = 140, Top = 190, Width = 80, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 230, Top = 190, Width = 80, DialogResult = DialogResult.Cancel };
                foreach (var pair in new[] { Tuple.Create("Frame", 12), Tuple.Create("Sound (SE/Anim)", 42), Tuple.Create("Volume %", 72), Tuple.Create("Flash", 102), Tuple.Create("Flash frames", 132) })
                    f.Controls.Add(new Label { Text = pair.Item1, Left = 10, Top = pair.Item2, Width = 108 });
                f.Controls.AddRange(new Control[] { frame, sound, vol, scope, dur, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(this) != DialogResult.OK) return false;
                t.Frame = (int)frame.Value - 1; t.Sound = sound.Text.Trim(); t.Volume = (int)vol.Value; t.FlashScope = scope.SelectedIndex; t.FlashDuration = (int)dur.Value;
                return true;
            }
        }

        // ------------------------------------------------------------------ images
        Image LoadImage(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Image img;
            if (_images.TryGetValue(path, out img)) return img;
            try { img = File.Exists(path) ? new Bitmap(new MemoryStream(File.ReadAllBytes(path))) : null; } catch { img = null; }
            _images[path] = img;
            return img;
        }

        Image Sheet { get { return _a == null ? null : LoadImage(_p.FindAsset("Graphics/Animations", _a.Graphic)); } }

        Image Sprite(ComboBox species, bool back)
        {
            var id = species.SelectedItem as string;
            if (id == null) return null;
            return LoadImage(_p.FindAsset(back ? "Graphics/Pokemon/Back" : "Graphics/Pokemon/Front", id) ?? _p.FindAsset("Graphics/Pokemon/Front", id));
        }

        /// <summary>RMXP-style hue rotation as a colour matrix.</summary>
        static ColorMatrix HueMatrix(int hue, float opacity)
        {
            double rad = hue * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad), k = (1 - cos) / 3, s3 = Math.Sqrt(1 / 3.0) * sin;
            float a = (float)(cos + k), b = (float)(k - s3), c = (float)(k + s3);
            return new ColorMatrix(new[]
            {
                new[] { a, c, b, 0f, 0f },
                new[] { b, a, c, 0f, 0f },
                new[] { c, b, a, 0f, 0f },
                new[] { 0f, 0f, 0f, opacity, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f }
            });
        }

        // ------------------------------------------------------------------ preview canvas
        /// <summary>The battle area (512×288, the editor's coordinates): user at 128,224, target at 384,96.</summary>
        class Canvas : Control
        {
            readonly AnimationEditor _ed;
            int _drag = -1; Point _dragFrom; int _celX0, _celY0;
            public Canvas(AnimationEditor ed) { _ed = ed; DoubleBuffered = true; }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var bg = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Color.FromArgb(160, 216, 248), Color.FromArgb(232, 248, 200), 90f)) g.FillRectangle(bg, 0, 0, Width, Height);
                using (var b = new SolidBrush(Color.FromArgb(150, 200, 110))) { g.FillEllipse(b, 384 - 120, 112, 240, 44); g.FillEllipse(b, 128 - 140, 252, 280, 44); }
                var cels = _ed.CurrentCels;
                var user = _ed.Sprite(_ed._userSpecies, true); var target = _ed.Sprite(_ed._targetSpecies, false);
                // Pokémon: from cels 0 and 1 if the frame has them
                DrawBattler(g, target, cels != null && cels.Count > 1 ? cels[1] : null, BattleAnimation.TargetX, BattleAnimation.TargetY);
                DrawBattler(g, user, cels != null && cels.Count > 0 ? cels[0] : null, BattleAnimation.UserX, BattleAnimation.UserY);
                if (cels != null)
                    for (int i = 2; i < cels.Count; i++) if (cels[i] != null) DrawCel(g, cels[i], user, target, i == _ed._cel);
                if (cels == null) TextRenderer.DrawText(g, "No animation selected", Font, new Point(8, 8), Color.Black);
            }

            void DrawBattler(Graphics g, Image img, int[] cel, int x, int y)
            {
                if (cel == null) { if (img != null) DrawImage(g, img, new Rectangle(0, 0, Frame(img), img.Height), x, y, 1, 1, 0, false, 255, 0); return; }
                if (cel[BattleAnimation.Visible] == 0) return;
                if (img != null) DrawImage(g, img, new Rectangle(0, 0, Frame(img), img.Height), cel[BattleAnimation.X], cel[BattleAnimation.Y], cel[BattleAnimation.ZoomX] / 100f, cel[BattleAnimation.ZoomY] / 100f, cel[BattleAnimation.Angle], cel[BattleAnimation.Mirror] != 0, cel[BattleAnimation.Opacity], 0);
                if (_ed._cel >= 0 && _ed.CurrentCels != null && _ed._cel < 2 && _ed.CurrentCels[_ed._cel] == cel) Outline(g, cel[BattleAnimation.X], cel[BattleAnimation.Y], img != null ? Frame(img) : 96, img != null ? img.Height : 96);
            }

            static int Frame(Image img) { return img.Width > img.Height * 3 / 2 ? img.Height : img.Width; }

            void DrawCel(Graphics g, int[] cel, Image user, Image target, bool selected)
            {
                int pat = cel[BattleAnimation.Pattern];
                Image img; Rectangle src; int hue = 0;
                if (pat == -1) { img = user; src = img != null ? new Rectangle(0, 0, Frame(img), img.Height) : Rectangle.Empty; }
                else if (pat == -2) { img = target; src = img != null ? new Rectangle(0, 0, Frame(img), img.Height) : Rectangle.Empty; }
                else
                {
                    img = _ed.Sheet; hue = _ed._a != null ? _ed._a.Hue : 0;
                    src = new Rectangle(pat % BattleAnimation.SheetColumns * BattleAnimation.CelSize, pat / BattleAnimation.SheetColumns * BattleAnimation.CelSize, BattleAnimation.CelSize, BattleAnimation.CelSize);
                }
                if (cel[BattleAnimation.Visible] != 0 && img != null)
                    DrawImage(g, img, src, cel[BattleAnimation.X], cel[BattleAnimation.Y], cel[BattleAnimation.ZoomX] / 100f, cel[BattleAnimation.ZoomY] / 100f, cel[BattleAnimation.Angle], cel[BattleAnimation.Mirror] != 0, cel[BattleAnimation.Opacity], hue);
                if (selected) Outline(g, cel[BattleAnimation.X], cel[BattleAnimation.Y], (int)(BattleAnimation.CelSize * Math.Abs(cel[BattleAnimation.ZoomX]) / 100f), (int)(BattleAnimation.CelSize * Math.Abs(cel[BattleAnimation.ZoomY]) / 100f));
            }

            static void Outline(Graphics g, int cx, int cy, int w, int h)
            {
                using (var pen = new Pen(Color.FromArgb(230, 40, 40), 2) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, cx - w / 2, cy - h / 2, w, h);
            }

            void DrawImage(Graphics g, Image img, Rectangle src, float cx, float cy, float zx, float zy, float angle, bool mirror, int opacity, int hue)
            {
                if (src.Width <= 0 || src.Height <= 0 || src.Y >= img.Height) return;
                var state = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(-angle);
                g.ScaleTransform(mirror ? -zx : zx, zy);
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(HueMatrix(hue, Math.Max(0, Math.Min(255, opacity)) / 255f));
                    g.DrawImage(img, new Rectangle(-src.Width / 2, -src.Height / 2, src.Width, src.Height), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
                g.Restore(state);
            }

            /// <summary>Topmost cel under the mouse (effects first, then the Pokémon).</summary>
            int HitCel(Point p)
            {
                var cels = _ed.CurrentCels;
                if (cels == null) return -1;
                for (int i = cels.Count - 1; i >= 0; i--)
                {
                    var c = cels[i];
                    if (c == null) continue;
                    int w = i < 2 ? 96 : (int)(BattleAnimation.CelSize * Math.Abs(c[BattleAnimation.ZoomX]) / 100f), h = i < 2 ? 96 : (int)(BattleAnimation.CelSize * Math.Abs(c[BattleAnimation.ZoomY]) / 100f);
                    if (i >= 2) { w = Math.Max(24, w / 2); h = Math.Max(24, h / 2); } // most sheet pictures are smaller than their cell
                    if (Math.Abs(p.X - c[BattleAnimation.X]) <= w / 2 && Math.Abs(p.Y - c[BattleAnimation.Y]) <= h / 2) return i;
                }
                return -1;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                Focus();
                int hit = HitCel(e.Location);
                _ed._cel = hit;
                _ed.ShowFrame();
                if (hit < 0) return;
                var c = _ed.CurrentCels[hit];
                _drag = hit; _dragFrom = e.Location; _celX0 = c[BattleAnimation.X]; _celY0 = c[BattleAnimation.Y];
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (_drag < 0 || e.Button != MouseButtons.Left) return;
                var cels = _ed.CurrentCels;
                if (cels == null || _drag >= cels.Count || cels[_drag] == null) return;
                cels[_drag][BattleAnimation.X] = _celX0 + e.X - _dragFrom.X;
                cels[_drag][BattleAnimation.Y] = _celY0 + e.Y - _dragFrom.Y;
                _ed.MarkDirty(); _ed.ShowCel(); Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e) { _drag = -1; }
        }

        // ------------------------------------------------------------------ sheet palette
        /// <summary>The animation sheet; click a picture to use it for the selected effect cel.</summary>
        class SheetView : Control
        {
            readonly AnimationEditor _ed;
            const int Cell = 96; // shown at half size
            public SheetView(AnimationEditor ed) { _ed = ed; DoubleBuffered = true; Size = new Size(Cell * 5, Cell); }

            public void UpdateSize()
            {
                var img = _ed.Sheet;
                int rows = img == null ? 1 : Math.Max(1, (img.Height + BattleAnimation.CelSize - 1) / BattleAnimation.CelSize);
                Size = new Size(Cell * BattleAnimation.SheetColumns, Cell * rows);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Color.FromArgb(56, 56, 64));
                var img = _ed.Sheet;
                if (img == null) { TextRenderer.DrawText(g, "No sheet", Font, new Point(6, 6), Color.White); return; }
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(HueMatrix(_ed._a != null ? _ed._a.Hue : 0, 1f));
                    g.DrawImage(img, new Rectangle(0, 0, img.Width / 2, img.Height / 2), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                }
                using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255)))
                {
                    for (int x = 0; x <= Width; x += Cell) g.DrawLine(pen, x, 0, x, Height);
                    for (int y = 0; y <= Height; y += Cell) g.DrawLine(pen, 0, y, Width, y);
                }
                var c = _ed.SelectedCel;
                if (c != null && _ed._cel >= 2 && c[BattleAnimation.Pattern] >= 0)
                {
                    int p = c[BattleAnimation.Pattern];
                    using (var pen = new Pen(Color.FromArgb(240, 60, 60), 3)) g.DrawRectangle(pen, p % BattleAnimation.SheetColumns * Cell + 1, p / BattleAnimation.SheetColumns * Cell + 1, Cell - 3, Cell - 3);
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                var c = _ed.SelectedCel;
                if (c == null || _ed._cel < 2) return;
                int col = e.X / Cell, row = e.Y / Cell;
                if (col >= BattleAnimation.SheetColumns) return;
                _ed._num["Picture"].Value = row * BattleAnimation.SheetColumns + col;
            }
        }
    }
}
