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
        readonly ComboBox _show = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        readonly Panel _showRow = new Panel { Dock = DockStyle.Top, Height = 26, Padding = new Padding(0, 2, 0, 2) };
        readonly Label _legend = new Label { Dock = DockStyle.Top, Height = 20, ForeColor = Color.IndianRed, Padding = new Padding(0, 2, 0, 0) };
        // Pokémon: whether animated sprites count as missing (Game Settings → Using animated sprites)
        readonly CheckBox _useAnimated = new CheckBox { Text = "Using animated sprites", Dock = DockStyle.Top, Height = 22 };
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
            if (_images != null)
            {
                // Under the search bar: how many entries miss each image, and a filter
                _legend.Height = _images.Kinds.Length > 1 ? 54 : 20;
                _showRow.Controls.Add(_show);
                _showRow.Controls.Add(new Label { Text = "Show:", Dock = DockStyle.Left, Width = 44, TextAlign = ContentAlignment.MiddleLeft });
                FillShowChoices();
                left.Controls.Add(_showRow); left.Controls.Add(_legend);
                if (_images.HasAnimatedKinds)
                {
                    _useAnimated.Checked = p.Settings.UseAnimatedSprites;
                    new ToolTip().SetToolTip(_useAnimated, "Ticked: the game shows animated sprites (Graphics/Pokemon/Animated); missing details look only at the animated sprites (and icons), not the still ones.\nUnticked: animated sprites aren't used or checked for; missing details look at the still sprites.");
                    _useAnimated.CheckedChanged += (s, e) =>
                    {
                        p.Settings.UseAnimatedSprites = _useAnimated.Checked;
                        try { Json.Json.ToFile(p.DataFile("settings.json"), p.Settings); } catch { }
                        if (_useAnimated.Checked) p.EnsureAnimatedFolders();
                        FillShowChoices();
                        Refill(Selected);
                        if (_sprites != null) _sprites.Show(Selected);
                    };
                    left.Controls.Add(_useAnimated);
                }
            }
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
                _show.SelectedIndexChanged += (s, e) => Refill(Selected);
                import.Click += (s, e) => ImportFiles();
                new ToolTip().SetToolTip(import, "Import images named after IDs (e.g. YOUNGSTER.png, CHARIZARD_1.png). Matching entries get the image; other names become new entries.");
            }
            else right.Controls.Add(_preview);
            right.Controls.Add(_heading);
            Controls.Add(right); Controls.Add(new Splitter { Dock = DockStyle.Left }); Controls.Add(left);

            _search.TextChanged += (s, e) => Refill(null);
            _list.SelectedIndexChanged += (s, e) => { if (!_renaming) ShowSelected(); };
            add.Click += (s, e) => AddRecord(null);
            dup.Click += (s, e) => { var r = Selected; if (r != null) AddRecord(r); };
            del.Click += (s, e) => DeleteSelected();
            up.Click += (s, e) => MoveSelected(-1);
            down.Click += (s, e) => MoveSelected(1);
            Refill(null);
        }

        /// <summary>The Show filter: all, missing any image, then missing each kind the game checks for.</summary>
        void FillShowChoices()
        {
            _show.Items.Clear();
            _show.Items.Add("All entries");
            _show.Items.Add(_images.Kinds.Length > 1 ? "Entries missing any image" : "Entries missing an image");
            var active = _images.Active;
            if (_images.Kinds.Length > 1) foreach (var k in active) _show.Items.Add("Missing " + k.Label.ToLowerInvariant());
            _legend.Height = active.Length <= 1 ? 20 : active.Length <= 5 ? 54 : 54 + ((active.Length - 4) / 2) * 16;
            _show.SelectedIndex = 0;
        }

        bool _renaming;
        GameRecord _shown;

        IList Items { get { return _table.Items; } }
        GameRecord Selected { get { int i = _list.SelectedIndex; return i >= 0 && i < _view.Count ? _view[i] : null; } }

        void MarkDirty() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }

        void Refill(GameRecord select)
        {
            string q = _search.Text.Trim().ToLowerInvariant();
            Func<GameRecord, bool> shown = r => true;
            if (_images != null && _show.SelectedIndex == 1) shown = r => _images.MissingAny(r);
            else if (_images != null && _show.SelectedIndex >= 2 && _show.SelectedIndex - 2 < _images.Active.Length) { var k = _images.Active[_show.SelectedIndex - 2]; shown = r => !_images.Has(r, k); }
            _view = Items.Cast<GameRecord>().Where(r => (q == "" || (r.Id ?? "").ToLowerInvariant().Contains(q) || r.DisplayName.ToLowerInvariant().Contains(q)) && shown(r)).ToList();
            if (_images != null) UpdateLegend();
            int idx = select != null ? _view.IndexOf(select) : (_view.Count > 0 ? 0 : -1);
            // Refill quietly and only rebuild the form if a different entry ends up shown
            // (rebuilding on every letter typed in the search box made the screen flash)
            _renaming = true;
            try
            {
                _list.BeginUpdate();
                _list.Items.Clear();
                foreach (var r in _view) _list.Items.Add(r.DisplayName);
                if (idx >= 0) _list.SelectedIndex = idx;
                _list.EndUpdate();
            }
            finally { _renaming = false; }
            if (Selected != _shown || _shown == null) ShowSelected();
            MainForm.NotifyCounts();
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
            _shown = r;
            _heading.Text = r == null ? _table.Label + " (" + Items.Count + ")" : r.DisplayName;
            ShowPreview(r);
            if (r != null)
            {
                var fb = new FormBuilder();
                fb.Changed += () =>
                {
                    MarkDirty();
                    int i = _list.SelectedIndex;
                    // Renaming the list entry re-selects it, which must not rebuild the form you're typing in
                    if (i >= 0 && _list.Items[i].ToString() != r.DisplayName) { _renaming = true; try { ListUtil.Rename(_list, i, r.DisplayName); } finally { _renaming = false; } }
                    _heading.Text = r.DisplayName;
                };
                _formHost.Controls.Add(fb.Build(r));
            }
            _formHost.ResumeLayout();
        }

        /// <summary>"Missing: Front 1381 · Back 1387 · Icon 1383 ..." for the whole list.</summary>
        void UpdateLegend()
        {
            var all = Items.Cast<GameRecord>().ToList();
            var parts = _images.Active.Select(k => new { Label = k.Label.Replace("Animated ", "Anim. "), Count = all.Count(r => !_images.Has(r, k)) }).ToList();
            if (parts.All(p => p.Count == 0)) { _legend.Text = "Nothing missing: every entry has all its images."; _legend.ForeColor = Color.SeaGreen; return; }
            _legend.ForeColor = Color.IndianRed;
            if (parts.Count == 1) { _legend.Text = "Missing images: " + parts[0].Count + " of " + all.Count; return; }
            // "Missing images:" then the counts, three then two per line so it fits under the search bar
            var lines = new List<string> { "Missing images (" + all.Count(r => _images.MissingAny(r)) + " entries):" };
            lines.Add(string.Join("  ·  ", parts.Take(3).Select(p => p.Label + " " + p.Count)));
            if (parts.Count > 3) lines.Add(string.Join("  ·  ", parts.Skip(3).Take(2).Select(p => p.Label + " " + p.Count)));
            for (int i = 5; i < parts.Count; i += 2) lines.Add(string.Join("  ·  ", parts.Skip(i).Take(2).Select(p => p.Label + " " + p.Count)));
            _legend.Text = string.Join("\n", lines);
        }

        void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _view.Count) return;
            var r = _view[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool missing = _images.MissingAny(r);
            bool dark = Theme.Dark;
            Color back = selected ? SystemColors.Highlight : missing ? (dark ? Color.FromArgb(92, 38, 40) : Color.FromArgb(255, 222, 222)) : _list.BackColor;
            Color fore = selected ? SystemColors.HighlightText : missing ? (dark ? Color.FromArgb(255, 190, 190) : Color.FromArgb(150, 20, 20)) : _list.ForeColor;
            using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            var textRect = new Rectangle(e.Bounds.X + 3, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
            if (missing)
            {
                using (var small = new Font(_list.Font.FontFamily, _list.Font.Size - 1.5f))
                {
                    var tag = "missing details";
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
                int k = ChoiceDialog.Ask(FindForm(), "Which images are you importing?", _images.Kinds.Select(x => x.Label + "  (" + x.Folder + "/" + x.Prefix + "<ID>)" + (x.Animated ? "  — GIFs or PNG strips" : "")).ToArray());
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
                string id = RecordImages.IdFromFileName(f, kind);
                if (id.Length == 0) { skipped.Add(Path.GetFileName(f) + " (no usable name)"); continue; }
                GameRecord r;
                string raw = Path.GetFileNameWithoutExtension(f);
                if (kind.Prefix.Length > 0 && raw.StartsWith(kind.Prefix, StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(kind.Prefix.Length);
                if (byKey.TryGetValue(id, out r) || byKey.TryGetValue(raw, out r)) plan.Add(new KeyValuePair<string, GameRecord>(f, r));
                else if (create.Any(c => c.Value == id)) skipped.Add(Path.GetFileName(f) + " (same ID as another file)");
                else create.Add(new KeyValuePair<string, string>(f, id));
            }
            int replacing = plan.Count(x => _images.Has(x.Value, kind));
            bool replace = true;
            if (replacing > 0)
            {
                var ans = MessageBox.Show(this, replacing + " of these entries already have " + A(kind.Label) + " image. Replace them?\n\nYes = replace, No = keep the old images", "Import", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (ans == DialogResult.Cancel) return;
                replace = ans == DialogResult.Yes;
            }

            int set = 0; var made = new List<GameRecord>(); var errors = new List<string>();
            foreach (var x in plan)
            {
                if (!replace && _images.Has(x.Value, kind)) { skipped.Add(Path.GetFileName(x.Key) + " (kept old image)"); continue; }
                try { SaveImageOf(x.Key, x.Value, kind); set++; }
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
                    SaveImageOf(c.Key, r, kind);
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
            // Items also have a plural name ("Potions")
            var plural = r.GetType().GetProperty("NamePlural");
            if (plural != null && plural.PropertyType == typeof(string) && plural.CanWrite) plural.SetValue(r, RecordImages.NameFromId(id) + "s", null);
            return r;
        }

        /// <summary>"a front", "an icon".</summary>
        static string A(string label) { string l = label.ToLowerInvariant(); return ("aeiou".IndexOf(l[0]) >= 0 ? "an " : "a ") + l; }

        static void SetName(GameRecord r, string name)
        {
            var p = r.GetType().GetProperty("Name");
            if (p != null && p.PropertyType == typeof(string) && p.CanWrite) p.SetValue(r, name, null);
        }

        /// <summary>The image pane's Import Image button: any file becomes the selected entry's image.</summary>
        /// <summary>Animated sprites are converted to strips (GIF frames side by side); other images saved as PNG.</summary>
        void SaveImageOf(string file, GameRecord r, RecordImages.Kind kind)
        {
            if (kind.Animated) { int frames; _images.SaveAnimated(file, r, kind, out frames); }
            else _images.SaveImage(file, r, kind);
        }

        void ImportForSelected(RecordImages.Kind kind)
        {
            var r = Selected; if (r == null) return;
            using (var dlg = new OpenFileDialog { Title = "Import " + kind.Label + " Image for " + r.DisplayName, Filter = "Images|*.png;*.gif;*.bmp;*.jpg" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (_images.Has(r, kind) && MessageBox.Show(this, r.DisplayName + " already has " + A(kind.Label) + " image. Replace it?", "Import Image", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                try { SaveImageOf(dlg.FileName, r, kind); }
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

}
