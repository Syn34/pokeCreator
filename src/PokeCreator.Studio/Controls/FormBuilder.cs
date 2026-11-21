using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Json;
using PokeCreator.Projects;

namespace PokeCreator.Studio.Controls
{
    /// <summary>
    /// Builds an editing form for any object from its public properties and [Field] attributes.
    /// Every data class in PokeCreator.Core gets a friendly editor without writing UI code.
    /// </summary>
    public class FormBuilder
    {
        public static Project Project;
        public event Action Changed;

        object _target;
        readonly List<Action> _refreshers = new List<Action>();
        bool _loading;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static FieldAttribute Meta(PropertyInfo p)
        {
            return (FieldAttribute)p.GetCustomAttributes(typeof(FieldAttribute), true).FirstOrDefault()
                ?? new FieldAttribute(Humanize(p.Name));
        }

        public static string Humanize(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpper(name[i]) : name[i]);
            }
            return sb.ToString();
        }

        /// <summary>Creates a scrolling panel with one row per property, grouped under headings.</summary>
        public Control Build(object target, params string[] hide)
        {
            _target = target;
            _refreshers.Clear();
            var outer = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Panel };
            var table = new TableLayoutPanel
            {
                ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
                Padding = new Padding(8, 4, 16, 16), BackColor = Theme.Panel
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var props = Json.Json.GetSerializableProperties(target.GetType()).Where(p => Array.IndexOf(hide, p.Name) < 0).ToList();
            var groups = props.GroupBy(p => Meta(p).Group).ToList();
            foreach (var g in groups)
            {
                var head = new Label { Text = g.Key, Font = Theme.HeaderFont, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, 14, 0, 4) };
                table.Controls.Add(head); table.SetColumnSpan(head, 2);
                foreach (var p in g)
                {
                    var meta = Meta(p);
                    var label = new Label { Text = meta.Label, AutoSize = false, Width = 165, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 3, 4, 3) };
                    var editor = CreateEditor(target, p, meta);
                    if (meta.Help != null) { var tt = new ToolTip(); tt.SetToolTip(label, meta.Help); tt.SetToolTip(editor, meta.Help); label.Text += " ⓘ"; }
                    editor.Margin = new Padding(0, 3, 0, 3);
                    editor.Dock = DockStyle.Fill;
                    table.Controls.Add(label);
                    table.Controls.Add(editor);
                }
            }
            outer.Controls.Add(table);
            _loading = true;
            foreach (var r in _refreshers) r();
            _loading = false;
            return outer;
        }

        void Set(PropertyInfo p, object v)
        {
            if (_loading) return;
            p.SetValue(_target, v, null);
            if (Changed != null) Changed();
        }

        Control CreateEditor(object target, PropertyInfo p, FieldAttribute meta)
        {
            Type t = p.PropertyType;
            Type nt = Nullable.GetUnderlyingType(t) ?? t;

            if (nt == typeof(bool))
            {
                var cb = new CheckBox { Text = "", Height = 24 };
                _refreshers.Add(() => { var v = p.GetValue(target, null); cb.Checked = v is bool && (bool)v; });
                cb.CheckedChanged += (s, e) => Set(p, cb.Checked);
                return cb;
            }
            if (nt == typeof(int) || nt == typeof(double))
            {
                var nud = new NumericUpDown { Minimum = -999999, Maximum = 99999999, Width = 120, Dock = DockStyle.Left, DecimalPlaces = nt == typeof(double) ? 2 : 0, Increment = nt == typeof(double) ? 0.1M : 1 };
                var wrap = new Panel { Height = 26 };
                wrap.Controls.Add(nud);
                _refreshers.Add(() => { var v = p.GetValue(target, null); nud.Value = Math.Max(nud.Minimum, Math.Min(nud.Maximum, v == null ? 0 : Convert.ToDecimal(v, Inv))); });
                nud.ValueChanged += (s, e) => Set(p, nt == typeof(int) ? (object)(int)nud.Value : (double)nud.Value);
                return wrap;
            }
            if (t == typeof(string))
            {
                string[] choices = ResolveChoices(meta);
                if (choices != null)
                {
                    var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
                    combo.Items.AddRange(choices);
                    _refreshers.Add(() => combo.Text = (string)p.GetValue(target, null) ?? "");
                    combo.TextChanged += (s, e) => Set(p, combo.Text);
                    return combo;
                }
                if (meta.Asset != null) return AssetPicker(target, p, meta.Asset);
                var tb = new TextBox { Multiline = meta.Multiline, Height = meta.Multiline ? 72 : 24, ScrollBars = meta.Multiline ? ScrollBars.Vertical : ScrollBars.None };
                _refreshers.Add(() => tb.Text = (string)p.GetValue(target, null) ?? "");
                tb.TextChanged += (s, e) => Set(p, tb.Text);
                return tb;
            }
            if (t == typeof(List<string>))
            {
                string[] choices = ResolveChoices(meta);
                var panel = new Panel { Height = 26 };
                var tb = new TextBox { Dock = DockStyle.Fill };
                panel.Controls.Add(tb);
                if (choices != null)
                {
                    var btn = new Button { Text = "Pick…", Dock = DockStyle.Right, Width = 60 };
                    btn.Click += (s, e) =>
                    {
                        var cur = (List<string>)p.GetValue(target, null) ?? new List<string>();
                        var picked = MultiPickDialog.Show(panel.FindForm(), meta.Label, choices, cur);
                        if (picked != null) tb.Text = string.Join(", ", picked);
                    };
                    panel.Controls.Add(btn);
                }
                _refreshers.Add(() => { var v = (List<string>)p.GetValue(target, null); tb.Text = v == null ? "" : string.Join(", ", v); });
                tb.TextChanged += (s, e) => Set(p, tb.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList());
                return panel;
            }
            if (t == typeof(List<int>))
            {
                var tb = new TextBox();
                _refreshers.Add(() => { var v = (List<int>)p.GetValue(target, null); tb.Text = v == null ? "" : string.Join(", ", v); });
                tb.TextChanged += (s, e) =>
                {
                    var list = new List<int>();
                    foreach (var part in tb.Text.Split(',')) { int n; if (int.TryParse(part.Trim(), out n)) list.Add(n); }
                    Set(p, list);
                };
                return tb;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var et = t.GetGenericArguments()[0];
                var list = (IList)p.GetValue(target, null);
                if (list == null) { list = (IList)Activator.CreateInstance(t); p.SetValue(target, list, null); }
                Control ed = IsFlat(et) ? (Control)new ObjectGrid(list, et) : new ObjectListEditor(list, et);
                if (ed is ObjectGrid) ((ObjectGrid)ed).Changed += () => { if (Changed != null) Changed(); };
                else ((ObjectListEditor)ed).Changed += () => { if (Changed != null) Changed(); };
                return ed;
            }
            if (!t.IsValueType && t != typeof(string) && t.GetConstructor(Type.EmptyTypes) != null)
            {
                // Nested object (e.g. MapProperties): inline sub-form
                var v = p.GetValue(target, null);
                if (v == null) { v = Activator.CreateInstance(t); p.SetValue(target, v, null); }
                var sub = new FormBuilder();
                sub.Changed += () => { if (Changed != null) Changed(); };
                var c = sub.Build(v);
                var host = new Panel { Height = 260, BorderStyle = BorderStyle.FixedSingle };
                host.Controls.Add(c);
                return host;
            }
            var ro = new Label { Text = "(" + t.Name + ")", ForeColor = Color.Gray };
            return ro;
        }

        /// <summary>Lists of values for dropdowns: fixed Choices, "@Name" constants, or database table IDs.</summary>
        public static string[] ResolveChoices(FieldAttribute meta)
        {
            if (meta.Choices != null) return meta.Choices;
            if (meta.Ref == null) return null;
            if (meta.Ref.StartsWith("@"))
            {
                var f = typeof(Choices).GetField(meta.Ref.Substring(1));
                return f == null ? null : (string[])f.GetValue(null);
            }
            if (Project == null) return null;
            var table = Project.Table(meta.Ref);
            if (table == null) return null;
            return table.Items.Cast<GameRecord>().Select(r => r.Id).ToArray();
        }

        Control AssetPicker(object target, PropertyInfo p, string folder)
        {
            var panel = new Panel { Height = 26 };
            var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            if (Project != null)
            {
                string dir = Path.Combine(Project.Root, folder.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(dir))
                    combo.Items.AddRange(Directory.GetFiles(dir).Select(f => Path.GetFileNameWithoutExtension(f)).Distinct().OrderBy(x => x).Cast<object>().ToArray());
                // Fonts can also come from the shared defaults (BaseData/Fonts)
                if (folder == "Fonts")
                    foreach (var f in Project.SharedFonts.Select(Path.GetFileNameWithoutExtension))
                        if (!combo.Items.Contains(f)) combo.Items.Add(f);
            }
            panel.Controls.Add(combo);
            _refreshers.Add(() => combo.Text = (string)p.GetValue(target, null) ?? "");
            combo.TextChanged += (s, e) => Set(p, combo.Text);
            return panel;
        }

        public static bool IsFlat(Type t)
        {
            foreach (var p in Json.Json.GetSerializableProperties(t))
            {
                var pt = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                if (pt.IsPrimitive || pt == typeof(string) || pt == typeof(List<string>) || pt == typeof(List<int>)) continue;
                return false;
            }
            return true;
        }
    }

    /// <summary>Editable grid for lists of simple objects (level-up moves, evolutions, encounter slots...).</summary>
    public class ObjectGrid : Panel
    {
        readonly IList _list; readonly Type _type; readonly PropertyInfo[] _props;
        readonly DataGridView _grid;
        public event Action Changed;
        bool _filling;

        public ObjectGrid(IList list, Type type)
        {
            _list = list; _type = type; _props = Json.Json.GetSerializableProperties(type);
            Height = Math.Min(320, 60 + Math.Max(3, list.Count) * 23);
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersWidth = 24,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Theme.Panel, BorderStyle = BorderStyle.None
            };
            foreach (var p in _props)
            {
                var meta = FormBuilder.Meta(p);
                DataGridViewColumn col;
                string[] choices = FormBuilder.ResolveChoices(meta) ?? GuessChoices(p);
                if (choices != null && choices.Length < 2000 && p.PropertyType == typeof(string))
                {
                    var cc = new DataGridViewComboBoxColumn { FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing };
                    cc.Items.Add("");
                    cc.Items.AddRange(choices.Where(c => c != "").Cast<object>().ToArray());
                    col = cc;
                }
                else col = new DataGridViewTextBoxColumn();
                col.HeaderText = meta.Label == FormBuilder.Humanize(p.Name) ? p.Name : meta.Label;
                col.Name = p.Name;
                _grid.Columns.Add(col);
            }
            Fill();
            _grid.CellValueChanged += (s, e) => Pull();
            _grid.UserDeletedRow += (s, e) => Pull();
            _grid.DataError += (s, e) => { e.ThrowException = false; };
            _grid.CurrentCellDirtyStateChanged += (s, e) => { if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            Controls.Add(_grid);
        }

        // Species / move / item columns get dropdowns from the database by name convention
        static string[] GuessChoices(PropertyInfo p)
        {
            if (FormBuilder.Project == null) return null;
            string key = null;
            switch (p.Name) { case "Species": key = "species"; break; case "Move": key = "moves"; break; case "Item": case "Ball": key = "items"; break; case "Ability": key = "abilities"; break; }
            if (p.Name == "Stat") return Choices.Stats;
            if (p.Name == "Method" && p.DeclaringType == typeof(Evolution)) return Choices.EvolutionMethods;
            if (p.Name == "Type" && p.DeclaringType == typeof(EncounterMethod)) return Choices.EncounterTypes;
            if (p.Name == "Nature") return Choices.Natures;
            if (key == null) return null;
            var table = FormBuilder.Project.Table(key);
            return table == null ? null : table.Items.Cast<GameRecord>().Select(r => r.Id).ToArray();
        }

        void Fill()
        {
            _filling = true;
            _grid.Rows.Clear();
            foreach (var item in _list)
            {
                var row = new object[_props.Length];
                for (int i = 0; i < _props.Length; i++) row[i] = ToCell(_props[i].GetValue(item, null));
                int r = _grid.Rows.Add(row);
            }
            _filling = false;
        }

        static object ToCell(object v)
        {
            if (v is IList && !(v is string)) return string.Join(", ", ((IList)v).Cast<object>());
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        void Pull()
        {
            if (_filling) return;
            string before = Json.Json.Write(_list, false);
            _list.Clear();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                var obj = Activator.CreateInstance(_type);
                bool any = false;
                for (int i = 0; i < _props.Length; i++)
                {
                    string s = Convert.ToString(row.Cells[i].Value) ?? "";
                    if (s.Length > 0) any = true;
                    _props[i].SetValue(obj, FromCell(s, _props[i].PropertyType), null);
                }
                if (any) _list.Add(obj);
            }
            // (some grids report a row deleted when they're only rebuilt: nothing changed then)
            if (Json.Json.Write(_list, false) != before && Changed != null) Changed();
        }

        static object FromCell(string s, Type t)
        {
            var nt = Nullable.GetUnderlyingType(t);
            if (nt != null) { if (s.Trim().Length == 0) return null; t = nt; }
            if (t == typeof(string)) return s;
            if (t == typeof(int)) { int n; return int.TryParse(s, out n) ? n : 0; }
            if (t == typeof(double)) { double d; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0.0; }
            if (t == typeof(bool)) return s.Trim().ToLowerInvariant() == "true" || s.Trim() == "1";
            if (t == typeof(List<string>)) return s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (t == typeof(List<int>)) { var l = new List<int>(); foreach (var x in s.Split(',')) { int n; if (int.TryParse(x.Trim(), out n)) l.Add(n); } return l; }
            return null;
        }
    }

    /// <summary>List of complex objects (e.g. encounter methods with slots): list on the left, form on the right.</summary>
    public class ObjectListEditor : Panel
    {
        readonly IList _list; readonly Type _type;
        readonly ListBox _box = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        readonly Panel _form = new Panel { Dock = DockStyle.Fill };
        public event Action Changed;

        public ObjectListEditor(IList list, Type type)
        {
            _list = list; _type = type; Height = 340; BorderStyle = BorderStyle.FixedSingle;
            var left = new Panel { Dock = DockStyle.Left, Width = 190 };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30 };
            var add = new Button { Text = "Add", Width = 52 }; var del = new Button { Text = "Remove", Width = 64 }; var up = new Button { Text = "▲", Width = 30 }; var dn = new Button { Text = "▼", Width = 30 };
            bar.Controls.AddRange(new Control[] { add, del, up, dn });
            left.Controls.Add(_box); left.Controls.Add(bar);
            Controls.Add(_form); Controls.Add(new Splitter { Dock = DockStyle.Left }); Controls.Add(left);
            add.Click += (s, e) => { _list.Add(Activator.CreateInstance(_type)); Reload(_list.Count - 1); Fire(); };
            del.Click += (s, e) => { int i = _box.SelectedIndex; if (i >= 0) { _list.RemoveAt(i); Reload(Math.Min(i, _list.Count - 1)); Fire(); } };
            up.Click += (s, e) => Swap(-1); dn.Click += (s, e) => Swap(1);
            _box.SelectedIndexChanged += (s, e) => ShowItem();
            Reload(0);
        }

        void Fire() { if (Changed != null) Changed(); }

        void Swap(int d)
        {
            int i = _box.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= _list.Count) return;
            var t = _list[i]; _list[i] = _list[j]; _list[j] = t;
            Reload(j); Fire();
        }

        void Reload(int select)
        {
            _box.Items.Clear();
            foreach (var o in _list) _box.Items.Add(o.ToString());
            if (select >= 0 && select < _box.Items.Count) _box.SelectedIndex = select; else ShowItem();
        }

        void ShowItem()
        {
            _form.Controls.Clear();
            int i = _box.SelectedIndex;
            if (i < 0 || i >= _list.Count) return;
            var fb = new FormBuilder();
            fb.Changed += () => { _box.Items[i] = _list[i].ToString(); Fire(); };
            _form.Controls.Add(fb.Build(_list[i]));
        }
    }

    /// <summary>Checklist dialog with search, used for picking several types/moves/items at once.</summary>
    public static class MultiPickDialog
    {
        public static List<string> Show(IWin32Window owner, string title, string[] options, List<string> current)
        {
            using (var f = new Form { Text = "Choose " + title, Width = 360, Height = 520, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.SizableToolWindow })
            {
                var selected = new List<string>(current);
                var search = new TextBox { Dock = DockStyle.Top };
                var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
                var ok = new Button { Text = "OK", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK, Height = 30 };
                bool filling = false;
                Action fill = () =>
                {
                    filling = true;
                    list.Items.Clear();
                    string q = search.Text.Trim().ToLowerInvariant();
                    // Selected items first, in order, then the rest
                    foreach (var s in selected) if (q == "" || s.ToLowerInvariant().Contains(q)) list.Items.Add(s, true);
                    foreach (var o in options) if (!selected.Contains(o) && (q == "" || o.ToLowerInvariant().Contains(q))) list.Items.Add(o, false);
                    filling = false;
                };
                list.ItemCheck += (s, e) =>
                {
                    if (filling) return;
                    string item = (string)list.Items[e.Index];
                    if (e.NewValue == CheckState.Checked) { if (!selected.Contains(item)) selected.Add(item); }
                    else selected.Remove(item);
                };
                search.TextChanged += (s, e) => fill();
                f.Controls.Add(list); f.Controls.Add(search); f.Controls.Add(ok);
                f.AcceptButton = ok;
                fill();
                return f.ShowDialog(owner) == DialogResult.OK ? selected : null;
            }
        }
    }
}
