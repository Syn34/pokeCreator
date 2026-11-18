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
        public bool Dirty { get; private set; }
        public string Title { get { return _table.Label; } }
        public event Action DirtyChanged;

        public DatabaseEditor(Project p, DatabaseTable table)
        {
            _project = p; _table = table;
            Dock = DockStyle.Fill;
            var left = new Panel { Dock = DockStyle.Left, Width = 250, BackColor = Theme.Sidebar, Padding = new Padding(4) };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(0, 2, 0, 0) };
            var add = new Button { Text = "New", Width = 56 };
            var dup = new Button { Text = "Duplicate", Width = 72 };
            var del = new Button { Text = "Delete", Width = 58 };
            var up = new Button { Text = "▲", Width = 28 };
            var down = new Button { Text = "▼", Width = 28 };
            bar.Controls.AddRange(new Control[] { add, dup, del, up, down });
            var searchLabel = new Label { Text = "Search (name or ID):", Dock = DockStyle.Top, Height = 18 };
            left.Controls.Add(_list); left.Controls.Add(_search); left.Controls.Add(searchLabel); left.Controls.Add(bar);

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_formHost); right.Controls.Add(_preview); right.Controls.Add(_heading);
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
            _view = Items.Cast<GameRecord>().Where(r => q == "" || (r.Id ?? "").ToLowerInvariant().Contains(q) || r.DisplayName.ToLowerInvariant().Contains(q)).ToList();
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

        void ShowPreview(GameRecord r)
        {
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
