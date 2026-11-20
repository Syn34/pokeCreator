using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>Helpers for the project's switch and variable names (kept in Data/system.json).</summary>
    public static class SwitchNames
    {
        public static List<string> ListOf(Project p, bool isVar) { return isVar ? p.System.Variables : p.System.Switches; }
        /// <summary>The highest ID (index 0 is unused, as in RPG Maker).</summary>
        public static int Max(Project p, bool isVar) { return Math.Max(0, ListOf(p, isVar).Count - 1); }
        public static string Name(Project p, bool isVar, int id) { return isVar ? p.VariableName(id) : p.SwitchName(id); }
        public static string Label(Project p, bool isVar, int id) { string n = Name(p, isVar, id); return id.ToString("0000") + ": " + (string.IsNullOrEmpty(n) ? "(no name)" : n); }

        public static void SetName(Project p, bool isVar, int id, string name)
        {
            var l = ListOf(p, isVar);
            EnsureMax(p, isVar, id);
            l[id] = name ?? "";
        }

        public static void EnsureMax(Project p, bool isVar, int max)
        {
            var l = ListOf(p, isVar);
            if (l.Count == 0) l.Add(null);
            while (l.Count <= max) l.Add("");
        }

        public static void Save(Project p) { Json.Json.ToFile(p.DataFile("system.json"), p.System); }
    }

    /// <summary>Pick a switch or variable by name; rename it or add more on the spot.</summary>
    public class IdPickerDialog : Form
    {
        readonly Project _p;
        readonly bool _var;
        readonly TextBox _search = new TextBox { Dock = DockStyle.Top };
        readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font(FontFamily.GenericMonospace, 9.5f) };
        readonly TextBox _name = new TextBox { Left = 54, Top = 8, Width = 250 };
        bool _changed, _filling;
        public int Chosen { get; private set; }

        public static int Ask(IWin32Window owner, Project p, bool isVariable, int current, string title = null)
        {
            using (var d = new IdPickerDialog(p, isVariable, current, title))
                return d.ShowDialog(owner) == DialogResult.OK ? d.Chosen : -1;
        }

        IdPickerDialog(Project p, bool isVar, int current, string title)
        {
            _p = p; _var = isVar; Chosen = Math.Max(1, current);
            string kind = isVar ? "Variable" : "Switch";
            Text = title ?? "Choose a " + kind.ToLowerInvariant();
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(420, 520);
            var hint = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = Color.DimGray, Text = "Type to search names or an ID. Give the " + kind.ToLowerInvariant() + " a name below so you can find it later." };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 78 };
            bottom.Controls.Add(new Label { Text = "Name", Left = 8, Top = 11, AutoSize = true });
            bottom.Controls.Add(_name);
            var more = new Button { Text = "Add 10 more", Left = 310, Top = 6, Width = 100, Height = 26 };
            more.Click += (s, e) => { SwitchNames.EnsureMax(_p, _var, SwitchNames.Max(_p, _var) + 10); _changed = true; Fill(SwitchNames.Max(_p, _var) - 9); };
            bottom.Controls.Add(more);
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 228, Top = 42, Width = 88, Height = 28 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 322, Top = 42, Width = 88, Height = 28 };
            bottom.Controls.Add(ok); bottom.Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            Controls.Add(_list); Controls.Add(_search); Controls.Add(hint); Controls.Add(bottom);

            if (SwitchNames.Max(p, isVar) < Chosen) { SwitchNames.EnsureMax(p, isVar, Math.Max(Chosen, 50)); _changed = true; }
            _search.TextChanged += (s, e) => Fill(Chosen);
            _list.SelectedIndexChanged += (s, e) =>
            {
                if (_filling || !(_list.SelectedItem is Item)) return;
                Chosen = ((Item)_list.SelectedItem).Id;
                _filling = true; _name.Text = SwitchNames.Name(_p, _var, Chosen); _filling = false;
            };
            _name.TextChanged += (s, e) =>
            {
                if (_filling) return;
                SwitchNames.SetName(_p, _var, Chosen, _name.Text); _changed = true;
                int i = _list.SelectedIndex;
                if (i >= 0) { _filling = true; _list.Items[i] = new Item { Id = Chosen, Text = SwitchNames.Label(_p, _var, Chosen) }; _list.SelectedIndex = i; _filling = false; }
            };
            _list.DoubleClick += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            _search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && _list.Items.Count > 0) { _list.Focus(); e.Handled = true; }
            };
            FormClosed += (s, e) => { if (_changed) SwitchNames.Save(_p); };
            Fill(Chosen);
            Shown += (s, e) => _list.Focus();
        }

        class Item { public int Id; public string Text; public override string ToString() { return Text; } }

        void Fill(int select)
        {
            _filling = true;
            string q = _search.Text.Trim();
            int qn; bool isNum = int.TryParse(q, out qn);
            _list.BeginUpdate();
            _list.Items.Clear();
            int max = SwitchNames.Max(_p, _var), selIndex = -1;
            for (int id = 1; id <= max; id++)
            {
                string label = SwitchNames.Label(_p, _var, id);
                if (q.Length > 0 && !(isNum ? id == qn || id.ToString().StartsWith(q) : label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                if (id == select) selIndex = _list.Items.Count;
                _list.Items.Add(new Item { Id = id, Text = label });
            }
            _list.EndUpdate();
            _filling = false;
            if (_list.Items.Count > 0) _list.SelectedIndex = selIndex >= 0 ? selIndex : 0;
            if (_list.Items.Count > 0 && _list.TopIndex >= _list.Items.Count) _list.TopIndex = 0;
        }
    }

    /// <summary>A button showing "0005: Got starter" that opens the picker; used wherever a switch or variable is chosen.</summary>
    public class IdButton : Button
    {
        readonly Project _p;
        readonly bool _var;
        int _id;
        public event Action<int> IdChanged;
        public int Id { get { return _id; } set { _id = Math.Max(1, value); UpdateText(); } }

        public IdButton(Project p, bool isVariable, int id)
        {
            _p = p; _var = isVariable; _id = Math.Max(1, id);
            TextAlign = ContentAlignment.MiddleLeft; AutoEllipsis = true; Height = 24;
            UpdateText();
            Click += (s, e) =>
            {
                int k = IdPickerDialog.Ask(FindForm(), _p, _var, _id);
                if (k < 1) { UpdateText(); return; } // names may have changed even if cancelled
                _id = k; UpdateText();
                if (IdChanged != null) IdChanged(k);
            };
        }

        void UpdateText() { Text = SwitchNames.Label(_p, _var, _id); }
    }

    /// <summary>Database > Switches & Variables: name them, set how many there are, and see where each is used.</summary>
    public class SwitchesEditor : UserControl, IDocument
    {
        readonly Project _p;
        readonly MainForm _main;
        readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };
        readonly ListBox _uses = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        readonly Label _usesHead = new Label { Dock = DockStyle.Top, Height = 24, Font = Theme.HeaderFont, ForeColor = Theme.Accent, Text = "Used in" };
        readonly Side[] _sides = new Side[2];
        List<Usage> _all = new List<Usage>();
        Dictionary<string, int> _counts = new Dictionary<string, int>();
        public bool Dirty { get; private set; }
        public string Title { get { return "Switches & Variables"; } }
        public event Action DirtyChanged;

        class Side
        {
            public bool IsVar;
            public ListBox List;
            public TextBox Name;
            public NumericUpDown Max;
            public TextBox Search;
        }

        class Row { public int Id; public string Text; public override string ToString() { return Text; } }

        class UseRow { public Usage U; public override string ToString() { return U.Where + "  —  " + U.What; } }

        public SwitchesEditor(MainForm main, Project p)
        {
            _main = main; _p = p; Dock = DockStyle.Fill;
            SwitchNames.EnsureMax(p, false, 1); SwitchNames.EnsureMax(p, true, 1);
            _tabs.TabPages.Add(BuildSide(0, false));
            _tabs.TabPages.Add(BuildSide(1, true));
            _tabs.SelectedIndexChanged += (s, e) => ShowUses();

            var right = new Panel { Dock = DockStyle.Right, Width = 520, Padding = new Padding(8) };
            right.Controls.Add(_uses);
            right.Controls.Add(new Label { Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray, Text = "Double-click a line to open that event or common event." });
            right.Controls.Add(_usesHead);
            _uses.DoubleClick += (s, e) => { var r = _uses.SelectedItem as UseRow; if (r != null) _main.NavigateTo(r.U.Target); };
            var refresh = new Button { Text = "Find Uses Again", Dock = DockStyle.Bottom, Height = 28 };
            refresh.Click += (s, e) => Rescan();
            right.Controls.Add(refresh);
            Controls.Add(_tabs); Controls.Add(new Splitter { Dock = DockStyle.Right }); Controls.Add(right);
            Rescan();
        }

        TabPage BuildSide(int index, bool isVar)
        {
            var side = new Side { IsVar = isVar };
            _sides[index] = side;
            var tp = new TabPage(isVar ? "Variables" : "Switches") { Padding = new Padding(6) };
            side.Search = new TextBox { Dock = DockStyle.Top };
            side.List = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font(FontFamily.GenericMonospace, 9.5f) };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 70 };
            bottom.Controls.Add(new Label { Text = "Name", Left = 4, Top = 9, AutoSize = true });
            side.Name = new TextBox { Left = 60, Top = 6, Width = 300 };
            bottom.Controls.Add(side.Name);
            bottom.Controls.Add(new Label { Text = "How many", Left = 4, Top = 42, AutoSize = true });
            side.Max = new NumericUpDown { Left = 80, Top = 39, Width = 80, Minimum = 1, Maximum = 5000, Value = Math.Max(1, SwitchNames.Max(_p, isVar)) };
            bottom.Controls.Add(side.Max);
            bottom.Controls.Add(new Label { Text = isVar ? "(variables 1 to this number)" : "(switches 1 to this number)", Left = 168, Top = 42, AutoSize = true, ForeColor = Color.DimGray });
            tp.Controls.Add(side.List);
            tp.Controls.Add(new Label { Dock = DockStyle.Top, Height = 22, ForeColor = Color.DimGray, Text = isVar ? "Variables hold numbers (a counter, a choice, steps taken...)." : "Switches are ON/OFF flags (got the starter, beat the gym...)." });
            tp.Controls.Add(side.Search);
            tp.Controls.Add(bottom);

            bool filling = false;
            side.Search.TextChanged += (s, e) => FillSide(side, CurrentId(side));
            side.List.SelectedIndexChanged += (s, e) =>
            {
                if (filling) return;
                var r = side.List.SelectedItem as Row; if (r == null) return;
                filling = true; side.Name.Text = SwitchNames.Name(_p, isVar, r.Id); filling = false;
                ShowUses();
            };
            side.Name.TextChanged += (s, e) =>
            {
                if (filling) return;
                var r = side.List.SelectedItem as Row; if (r == null) return;
                SwitchNames.SetName(_p, isVar, r.Id, side.Name.Text); Mark();
                int i = side.List.SelectedIndex;
                filling = true; side.List.Items[i] = new Row { Id = r.Id, Text = RowText(isVar, r.Id) }; side.List.SelectedIndex = i; filling = false;
            };
            side.Max.ValueChanged += (s, e) =>
            {
                int max = (int)side.Max.Value, cur = SwitchNames.Max(_p, isVar);
                if (max == cur) return;
                var l = SwitchNames.ListOf(_p, isVar);
                if (max > cur) SwitchNames.EnsureMax(_p, isVar, max);
                else
                {
                    // Only drop unnamed, unused ones from the end
                    int keep = max;
                    for (int id = cur; id > max; id--)
                        if (!string.IsNullOrEmpty(SwitchNames.Name(_p, isVar, id)) || Count(isVar, id) > 0) { keep = id; break; }
                    l.RemoveRange(keep + 1, l.Count - keep - 1);
                    if (keep != max) { side.Max.Value = keep; MessageBox.Show(this, (isVar ? "Variable " : "Switch ") + keep + " has a name or is used, so the list stops there.", Title); }
                }
                Mark(); FillSide(side, CurrentId(side));
            };
            side.List.KeyDown += (s, e) => { if (e.KeyCode == Keys.F2 || e.KeyCode == Keys.Enter) { side.Name.Focus(); side.Name.SelectAll(); e.Handled = true; } };
            return tp;
        }

        int Count(bool isVar, int id) { int n; return _counts.TryGetValue((isVar ? "v" : "s") + id, out n) ? n : 0; }

        string RowText(bool isVar, int id)
        {
            int n = Count(isVar, id);
            return SwitchNames.Label(_p, isVar, id) + (n > 0 ? "   (" + n + " use" + (n == 1 ? "" : "s") + ")" : "");
        }

        int CurrentId(Side side) { var r = side.List.SelectedItem as Row; return r == null ? 1 : r.Id; }

        void FillSide(Side side, int select)
        {
            string q = side.Search.Text.Trim();
            int qn; bool isNum = int.TryParse(q, out qn);
            side.List.BeginUpdate();
            side.List.Items.Clear();
            int selIndex = -1;
            for (int id = 1; id <= SwitchNames.Max(_p, side.IsVar); id++)
            {
                string t = RowText(side.IsVar, id);
                if (q.Length > 0 && !(isNum ? id.ToString().StartsWith(q) : t.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                if (id == select) selIndex = side.List.Items.Count;
                side.List.Items.Add(new Row { Id = id, Text = t });
            }
            side.List.EndUpdate();
            if (side.List.Items.Count > 0) side.List.SelectedIndex = selIndex >= 0 ? selIndex : 0;
            if (side.List.Items.Count > 0 && side.List.TopIndex >= side.List.Items.Count) side.List.TopIndex = 0;
        }

        void Rescan()
        {
            Cursor = Cursors.WaitCursor;
            try { _all = ProjectSearch.AllUses(_p); }
            finally { Cursor = Cursors.Default; }
            _counts = _all.GroupBy(u => (u.IsVariable ? "v" : "s") + u.Id).ToDictionary(g => g.Key, g => g.Count());
            foreach (var side in _sides) FillSide(side, CurrentId(side));
            ShowUses();
        }

        void ShowUses()
        {
            var side = _sides[Math.Max(0, _tabs.SelectedIndex)];
            int id = CurrentId(side);
            var uses = _all.Where(u => u.IsVariable == side.IsVar && u.Id == id).ToList();
            _usesHead.Text = (side.IsVar ? "Variable " : "Switch ") + id.ToString("0000") + " is used in " + uses.Count + " place" + (uses.Count == 1 ? "" : "s");
            _uses.BeginUpdate();
            _uses.Items.Clear();
            foreach (var u in uses) _uses.Items.Add(new UseRow { U = u });
            if (uses.Count == 0) _uses.Items.Add("Not used by any event or common event.");
            _uses.EndUpdate();
        }

        /// <summary>Selects a switch or variable (used when opening from elsewhere).</summary>
        public void SelectId(bool isVar, int id) { _tabs.SelectedIndex = isVar ? 1 : 0; FillSide(_sides[isVar ? 1 : 0], id); }

        void Mark() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }

        public void Save()
        {
            SwitchNames.Save(_p);
            Dirty = false; if (DirtyChanged != null) DirtyChanged();
        }
    }
}
