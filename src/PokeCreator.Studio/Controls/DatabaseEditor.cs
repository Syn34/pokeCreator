using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Json;
using PokeCreator.Projects;
using PokeCreator.Studio.Editors;

namespace PokeCreator.Studio.Controls
{
    /// <summary>Document tab contents that can be saved.</summary>
    public interface IDocument
    {
        string Title { get; }
        bool Dirty { get; }
        void Save();
    }

    /// <summary>Editor for one database table: searchable list + generated form + preview.</summary>
    public class DatabaseEditor : UserControl, IDocument
    {
        readonly Project _project;
        readonly DatabaseTable _table;
        readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Segoe UI", 9.5f) };
        readonly TextBox _search = new TextBox { Dock = DockStyle.Top };
        readonly Panel _formHost = new Panel { Dock = DockStyle.Fill };
        readonly PictureBox _preview = new PictureBox { Dock = DockStyle.Right, Width = 0, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
        readonly Label _heading = new Label { Dock = DockStyle.Top, Height = 34, Font = Theme.TitleFont, Padding = new Padding(8, 4, 0, 0) };
        List<GameRecord> _view = new List<GameRecord>();
        // Pokémon and trainer types: their images, highlighting of entries without one, and the image pane
        readonly RecordImages _images;
        readonly SpritePanel _sprites;
        readonly CheckBox _onlyMissing = new CheckBox { Text = "Only show entries without images", Dock = DockStyle.Top, Height = 22 };
        readonly Label _legend = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = Color.IndianRed };
        public bool Dirty { get; private set; }
        public string Title { get { return _table.Label; } }
        public event Action DirtyChanged;

        public DatabaseEditor(Project p, DatabaseTable table)
        {
            _project = p; _table = table;
            Dock = DockStyle.Fill;
            _images = RecordImages.For(p, table.RecordType);
            var left = new Panel { Dock = DockStyle.Left, Width = _images != null ? 290 : 250, BackColor = Theme.Sidebar, Padding = new Padding(4) };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = _images != null ? 64 : 34, Padding = new Padding(0, 2, 0, 0) };
            var add = new Button { Text = "New", Width = 56 };
            var dup = new Button { Text = "Duplicate", Width = 72 };
            var del = new Button { Text = "Delete", Width = 58 };
            var import = new Button { Text = "Import", Width = 58 };
            var up = new Button { Text = "▲", Width = 28 };
            var down = new Button { Text = "▼", Width = 28 };
            bar.Controls.AddRange(new Control[] { add, dup, del });
            if (_images != null) bar.Controls.Add(import);
            bar.Controls.AddRange(new Control[] { up, down });
            var searchLabel = new Label { Text = "Search (name or ID):", Dock = DockStyle.Top, Height = 18 };
            left.Controls.Add(_list);
            if (_images != null) { left.Controls.Add(_onlyMissing); left.Controls.Add(_legend); }
            left.Controls.Add(_search); left.Controls.Add(searchLabel); left.Controls.Add(bar);

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_formHost);
            if (_images != null)
            {
                _sprites = new SpritePanel(_images);
                _sprites.ImportRequested += k => ImportForSelected(k);
                right.Controls.Add(_sprites);
                // Highlight names of entries without an image
                _list.DrawMode = DrawMode.OwnerDrawFixed;
                _list.ItemHeight = Math.Max(18, _list.Font.Height + 4);
                _list.DrawItem += DrawItem;
                _onlyMissing.CheckedChanged += (s, e) => Refill(Selected);
                import.Click += (s, e) => ImportFiles();
                new ToolTip().SetToolTip(import, "Import images named after IDs (e.g. YOUNGSTER.png, CHARIZARD_1.png). Matching entries get the image; other names become new entries.");
            }
            else right.Controls.Add(_preview);
            right.Controls.Add(_heading);
            Controls.Add(right); Controls.Add(new Splitter { Dock = DockStyle.Left }); Controls.Add(left);

            _search.TextChanged += (s, e) => Refill(null);
            _list.SelectedIndexChanged += (s, e) => ShowSelected();
            add.Click += (s, e) => AddRecord(null);
            dup.Click += (s, e) => { var r = Selected; if (r != null) AddRecord(r); };
            del.Click += (s, e) => DeleteSelected();
            up.Click += (s, e) => MoveSelected(-1);
            down.Click += (s, e) => MoveSelected(1);
            Refill(null);
        }

        IList Items { get { return _table.Items; } }
        GameRecord Selected { get { int i = _list.SelectedIndex; return i >= 0 && i < _view.Count ? _view[i] : null; } }

        void MarkDirty() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }

        void Refill(GameRecord select)
        {
            string q = _search.Text.Trim().ToLowerInvariant();
            bool onlyMissing = _images != null && _onlyMissing.Checked;
            _view = Items.Cast<GameRecord>().Where(r => (q == "" || (r.Id ?? "").ToLowerInvariant().Contains(q) || r.DisplayName.ToLowerInvariant().Contains(q)) && (!onlyMissing || !_images.HasMain(r))).ToList();
            if (_images != null)
            {
                int missing = Items.Cast<GameRecord>().Count(r => !_images.HasMain(r));
                _legend.Text = missing == 0 ? "Every entry has an image." : "Highlighted: no " + _images.Main.Label.ToLowerInvariant() + " image (" + missing + ")";
                _legend.ForeColor = missing == 0 ? Color.SeaGreen : Color.IndianRed;
            }
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var r in _view) _list.Items.Add(r.DisplayName);
            _list.EndUpdate();
            int idx = select != null ? _view.IndexOf(select) : (_view.Count > 0 ? 0 : -1);
            if (idx >= 0) _list.SelectedIndex = idx; else ShowSelected();
        }

        public void SelectById(string id)
        {
            _search.Text = "";
            var r = Items.Cast<GameRecord>().FirstOrDefault(x => x.Id == id);
            if (r != null) Refill(r);
        }

        void ShowSelected()
        {
            _formHost.SuspendLayout();
            foreach (Control c in _formHost.Controls) c.Dispose();
            _formHost.Controls.Clear();
            var r = Selected;
            _heading.Text = r == null ? _table.Label + " (" + Items.Count + ")" : r.DisplayName;
            ShowPreview(r);
            if (r != null)
            {
                var fb = new FormBuilder();
                fb.Changed += () =>
                {
                    MarkDirty();
                    int i = _list.SelectedIndex;
                    if (i >= 0 && _list.Items[i].ToString() != r.DisplayName) _list.Items[i] = r.DisplayName;
                    _heading.Text = r.DisplayName;
                };
                _formHost.Controls.Add(fb.Build(r));
            }
            _formHost.ResumeLayout();
        }

        void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _view.Count) return;
            var r = _view[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool missing = !_images.HasMain(r);
            bool dark = Theme.Dark;
            Color back = selected ? SystemColors.Highlight : missing ? (dark ? Color.FromArgb(92, 38, 40) : Color.FromArgb(255, 222, 222)) : _list.BackColor;
            Color fore = selected ? SystemColors.HighlightText : missing ? (dark ? Color.FromArgb(255, 190, 190) : Color.FromArgb(150, 20, 20)) : _list.ForeColor;
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            var textRect = new Rectangle(e.Bounds.X + 3, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
            if (missing)
            {
                using (var small = new Font(_list.Font.FontFamily, _list.Font.Size - 1.5f))
                {
                    var tag = "no image";
                    var sz = TextRenderer.MeasureText(tag, small);
                    TextRenderer.DrawText(e.Graphics, tag, small, new Rectangle(e.Bounds.Right - sz.Width - 4, e.Bounds.Y, sz.Width + 2, e.Bounds.Height), selected ? fore : Color.IndianRed, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                    textRect.Width -= sz.Width + 4;
                }
            }
            TextRenderer.DrawText(e.Graphics, r.DisplayName, _list.Font, textRect, fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        // ------------------------------------------------------------------ importing images
        /// <summary>
        /// Imports images named after IDs. A name that matches an entry becomes its image; any other name
        /// becomes a new entry (Pokémon forms like CHARIZARD_3 are copied from their base species).
        /// </summary>
        void ImportFiles()
        {
            RecordImages.Kind kind = _images.Main;
            if (_images.Kinds.Length > 1)
            {
                int k = ChoiceDialog.Ask(FindForm(), "Which sprites are you importing?", _images.Kinds.Select(x => x.Label + "  (" + x.Folder + ")").ToArray());
                if (k < 0) return;
                kind = _images.Kinds[k];
            }
            string[] files;
            using (var dlg = new OpenFileDialog { Title = "Import " + kind.Label + " Images — name each file after an ID", Filter = "Images|*.png;*.gif;*.bmp;*.jpg", Multiselect = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                files = dlg.FileNames;
            }
            var byKey = new Dictionary<string, GameRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (GameRecord r in Items) { if (!byKey.ContainsKey(RecordImages.Key(r))) byKey[RecordImages.Key(r)] = r; if (r.Id != null && !byKey.ContainsKey(r.Id)) byKey[r.Id] = r; }

            // Work out what each file does before changing anything
            var plan = new List<KeyValuePair<string, GameRecord>>();   // file -> existing entry
            var create = new List<KeyValuePair<string, string>>();     // file -> new ID
            var skipped = new List<string>();
            foreach (var f in files)
            {
                string id = RecordImages.IdFromFileName(f);
                if (id.Length == 0) { skipped.Add(Path.GetFileName(f) + " (no usable name)"); continue; }
                GameRecord r;
                if (byKey.TryGetValue(id, out r) || byKey.TryGetValue(Path.GetFileNameWithoutExtension(f), out r)) plan.Add(new KeyValuePair<string, GameRecord>(f, r));
                else if (create.Any(c => c.Value == id)) skipped.Add(Path.GetFileName(f) + " (same ID as another file)");
                else create.Add(new KeyValuePair<string, string>(f, id));
            }
            int replacing = plan.Count(x => _images.Has(x.Value, kind));
            bool replace = true;
            if (replacing > 0)
            {
                var ans = MessageBox.Show(this, replacing + " of these entries already have a " + kind.Label.ToLowerInvariant() + " image. Replace them?\n\nYes = replace, No = keep the old images", "Import", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (ans == DialogResult.Cancel) return;
                replace = ans == DialogResult.Yes;
            }

            int set = 0; var made = new List<GameRecord>(); var errors = new List<string>();
            foreach (var x in plan)
            {
                if (!replace && _images.Has(x.Value, kind)) { skipped.Add(Path.GetFileName(x.Key) + " (kept old image)"); continue; }
                try { _images.SaveImage(x.Key, x.Value, kind); set++; }
                catch (Exception ex) { errors.Add(Path.GetFileName(x.Key) + ": " + ex.Message); }
            }
            foreach (var c in create)
            {
                try
                {
                    var r = NewRecordFor(c.Value);
                    // New forms go right after their species' other entries, like the built-in forms
                    var sd = r as SpeciesData;
                    int at = -1;
                    if (sd != null && sd.Form > 0)
                    {
                        Func<int, bool> same = i => { var o = Items[i] as SpeciesData; return o != null && (o.Id == sd.Species || o.Species == sd.Species); };
                        int b = Enumerable.Range(0, Items.Count).FirstOrDefault(i => same(i));
                        if (Items.Count > 0 && same(b)) { at = b + 1; while (at < Items.Count && same(at)) at++; }
                    }
                    if (at >= 0) Items.Insert(at, r); else Items.Add(r);
                    _images.SaveImage(c.Key, r, kind);
                    made.Add(r);
                }
                catch (Exception ex) { errors.Add(Path.GetFileName(c.Key) + ": " + ex.Message); }
            }
            if (made.Count > 0) MarkDirty();
            _search.Text = "";
            Refill(made.Count > 0 ? made[0] : plan.Count > 0 ? plan[0].Value : Selected);

            var msg = new List<string>();
            msg.Add("Set the " + kind.Label.ToLowerInvariant() + " image of " + set + " existing " + (set == 1 ? "entry." : "entries."));
            if (made.Count > 0) msg.Add("Created " + made.Count + " new " + (made.Count == 1 ? "entry" : "entries") + ": " + string.Join(", ", made.Take(10).Select(r => r.Id)) + (made.Count > 10 ? "…" : "") + "\n(Fill in their details, then save.)");
            if (skipped.Count > 0) msg.Add("Skipped: " + string.Join(", ", skipped.Take(10)) + (skipped.Count > 10 ? "…" : ""));
            if (errors.Count > 0) msg.Add("Problems:\n" + string.Join("\n", errors.Take(10)));
            MessageBox.Show(this, string.Join("\n\n", msg), "Import", MessageBoxButtons.OK, errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        /// <summary>A new entry for an imported image whose name matched no ID.</summary>
        GameRecord NewRecordFor(string id)
        {
            if (_table.RecordType == typeof(SpeciesData))
            {
                // CHARIZARD_3 with CHARIZARD in the list: a new form, copied from the base species
                int us = id.LastIndexOf('_'); int form;
                if (us > 0 && int.TryParse(id.Substring(us + 1), out form) && form > 0)
                {
                    string baseId = id.Substring(0, us);
                    var baseRec = Items.Cast<SpeciesData>().FirstOrDefault(x => string.Equals(x.Id, baseId, StringComparison.OrdinalIgnoreCase) && x.Form == 0);
                    if (baseRec != null)
                    {
                        var f = (SpeciesData)Json.Json.ConvertTo(Json.Json.Parse(Json.Json.Write(baseRec)), typeof(SpeciesData));
                        f.Id = baseRec.Id + "_" + form; f.Species = baseRec.Id; f.Form = form;
                        var fn = typeof(SpeciesData).GetProperty("FormName");
                        if (fn != null) fn.SetValue(f, "Form " + form, null);
                        return f;
                    }
                }
                var sp = new SpeciesData { Id = id, Species = id };
                SetName(sp, RecordImages.NameFromId(id));
                return sp;
            }
            var r = (GameRecord)Activator.CreateInstance(_table.RecordType);
            r.Id = id;
            SetName(r, RecordImages.NameFromId(id));
            return r;
        }

        static void SetName(GameRecord r, string name)
        {
            var p = r.GetType().GetProperty("Name");
            if (p != null && p.PropertyType == typeof(string) && p.CanWrite) p.SetValue(r, name, null);
        }

        /// <summary>The image pane's Import Image button: any file becomes the selected entry's image.</summary>
        void ImportForSelected(RecordImages.Kind kind)
        {
            var r = Selected; if (r == null) return;
            using (var dlg = new OpenFileDialog { Title = "Import " + kind.Label + " Image for " + r.DisplayName, Filter = "Images|*.png;*.gif;*.bmp;*.jpg" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (_images.Has(r, kind) && MessageBox.Show(this, r.DisplayName + " already has a " + kind.Label.ToLowerInvariant() + " image. Replace it?", "Import Image", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                try { _images.SaveImage(dlg.FileName, r, kind); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import Image", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            Refill(r);
        }

        void ShowPreview(GameRecord r)
        {
            if (_sprites != null) { _sprites.Show(r); _list.Invalidate(); return; }
            string path = null;
            if (r is SpeciesData)
            {
                var s = (SpeciesData)r;
                path = _project.FindAsset("Graphics/Pokemon/Front", s.Form > 0 ? s.Species + "_" + s.Form : s.Species) ?? _project.FindAsset("Graphics/Pokemon/Front", s.Species);
            }
            else if (r is ItemData) path = _project.FindAsset("Graphics/Items", r.Id);
            else if (r is TrainerTypeData) path = _project.FindAsset("Graphics/Trainers", r.Id);
            else if (r is TrainerData) path = _project.FindAsset("Graphics/Trainers", ((TrainerData)r).TrainerType);
            var old = _preview.Image;
            _preview.Image = Theme.LoadImage(path);
            if (old != null) old.Dispose();
            _preview.Width = _preview.Image == null ? 0 : 200;
        }

        void AddRecord(GameRecord copyFrom)
        {
            GameRecord r = copyFrom != null
                ? (GameRecord)Json.Json.ConvertTo(Json.Json.Parse(Json.Json.Write(copyFrom)), _table.RecordType)
                : (GameRecord)Activator.CreateInstance(_table.RecordType);
            string baseId = copyFrom != null ? copyFrom.Id + "_COPY" : "NEW" + _table.Key.TrimEnd('s').ToUpperInvariant();
            string id = baseId; int n = 2;
            while (Items.Cast<GameRecord>().Any(x => x.Id == id)) id = baseId + n++;
            r.Id = id;
            var nameProp = r.GetType().GetProperty("Name");
            if (copyFrom == null && nameProp != null && nameProp.PropertyType == typeof(string)) nameProp.SetValue(r, "New " + _table.Label.TrimEnd('s'), null);
            int at = Selected != null ? Items.IndexOf(Selected) + 1 : Items.Count;
            Items.Insert(at, r);
            _search.Text = "";
            Refill(r);
            MarkDirty();
        }

        void DeleteSelected()
        {
            var r = Selected;
            if (r == null) return;
            if (MessageBox.Show(this, "Delete \"" + r.DisplayName + "\"?\n\nAnything that refers to " + r.Id + " will need updating.", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            int i = _list.SelectedIndex;
            Items.Remove(r);
            Refill(null);
            if (_view.Count > 0) _list.SelectedIndex = Math.Min(i, _view.Count - 1);
            MarkDirty();
        }

        void MoveSelected(int d)
        {
            var r = Selected; if (r == null) return;
            int i = Items.IndexOf(r), j = i + d;
            if (j < 0 || j >= Items.Count) return;
            Items.RemoveAt(i); Items.Insert(j, r);
            Refill(r); MarkDirty();
        }

        public void Save()
        {
            Json.Json.ToFile(_project.DataFile(_table.FileName), Items);
            Dirty = false;
            if (DirtyChanged != null) DirtyChanged();
        }
    }

    /// <summary>Editor for a single settings object (Game Settings) or a plain list (Players).</summary>
    public class ObjectDocument : UserControl, IDocument
    {
        readonly Action _save;
        public string Title { get; private set; }
        public bool Dirty { get; private set; }
        public event Action DirtyChanged;

        public ObjectDocument(string title, object target, Action save, string description = null)
        {
            Title = title; _save = save; Dock = DockStyle.Fill;
            Control body;
            if (target is IList)
            {
                var et = target.GetType().GetGenericArguments()[0];
                var ed = new ObjectListEditor((IList)target, et) { Dock = DockStyle.Fill };
                ed.Changed += Mark;
                body = ed;
            }
            else
            {
                var fb = new FormBuilder();
                fb.Changed += Mark;
                body = fb.Build(target);
            }
            Controls.Add(body);
            if (description != null) Controls.Add(new Label { Text = description, Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 0), ForeColor = Color.DimGray });
            Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 34, Font = Theme.TitleFont, Padding = new Padding(8, 4, 0, 0) });
        }

        void Mark() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }
        public void Save() { _save(); Dirty = false; if (DirtyChanged != null) DirtyChanged(); }
    }

    /// <summary>Switch and variable names.</summary>
    public class SwitchesDocument : UserControl, IDocument
    {
        readonly Project _p;
        readonly DataGridView _sw, _var;
        public string Title { get { return "Switches & Variables"; } }
        public bool Dirty { get; private set; }
        public event Action DirtyChanged;

        public SwitchesDocument(Project p)
        {
            _p = p; Dock = DockStyle.Fill;
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 400 };
            _sw = MakeGrid("Switch", p.System.Switches);
            _var = MakeGrid("Variable", p.System.Variables);
            split.Panel1.Controls.Add(_sw); split.Panel1.Controls.Add(new Label { Text = "Switches (on/off flags)", Dock = DockStyle.Top, Font = Theme.HeaderFont, Height = 26 });
            split.Panel2.Controls.Add(_var); split.Panel2.Controls.Add(new Label { Text = "Variables (numbers)", Dock = DockStyle.Top, Font = Theme.HeaderFont, Height = 26 });
            Controls.Add(split);
        }

        DataGridView MakeGrid(string what, List<string> names)
        {
            var g = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Theme.Panel };
            g.Columns.Add("id", "ID"); g.Columns.Add("name", what + " name");
            g.Columns[0].ReadOnly = true; g.Columns[0].FillWeight = 20;
            while (names.Count < 101) names.Add(null);
            for (int i = 1; i < names.Count; i++) g.Rows.Add(i.ToString("000"), names[i] ?? "");
            g.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                names[e.RowIndex + 1] = Convert.ToString(g.Rows[e.RowIndex].Cells[1].Value);
                if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); }
            };
            return g;
        }

        public void Save()
        {
            Json.Json.ToFile(_p.DataFile("system.json"), _p.System);
            Dirty = false; if (DirtyChanged != null) DirtyChanged();
        }
    }
}
