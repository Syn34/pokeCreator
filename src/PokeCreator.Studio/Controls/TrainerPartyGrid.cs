using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Pkmn;
using PokeCreator.Projects;

namespace PokeCreator.Studio.Controls
{
    /// <summary>
    /// A trainer's party: one row per Pokémon, every setting but the level and nickname picked from a dropdown.
    /// The form, move, ability and gender lists come from that Pokémon's own entry in the database.
    /// </summary>
    public class TrainerPartyGrid : Panel
    {
        public const int MaxParty = 6;

        class Choice
        {
            public string Value { get; set; }
            public string Text { get; set; }
            public Choice(string v, string t) { Value = v; Text = t; }
            public override string ToString() { return Text; }
        }

        readonly List<TrainerPokemon> _list;
        readonly DataGridView _grid;
        readonly Button _add, _remove, _up, _down;
        bool _filling;
        public event Action Changed;

        Project P { get { return FormBuilder.Project; } }

        // shared lists (the same for every row)
        List<Choice> _species, _items, _balls, _natures, _ivs, _evs, _happiness, _yesNo;

        public TrainerPartyGrid(List<TrainerPokemon> list)
        {
            _list = list;
            BuildSharedLists();
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersWidth = 24, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Theme.Panel, BorderStyle = BorderStyle.None,
                EditMode = DataGridViewEditMode.EditProgrammatically, ScrollBars = ScrollBars.Both
            };
            AddCombo("Species", "Species", 120, _species);
            AddText("Level", "Level", 48);
            AddCombo("Form", "Form", 110, null);
            AddText("Name", "Nickname", 90);
            for (int i = 1; i <= 4; i++) AddCombo("Move" + i, "Move " + i, 130, null);
            AddCombo("Ability", "Ability", 120, null);
            AddCombo("AbilityIndex", "Ability slot", 130, null);
            AddCombo("Item", "Held item", 120, _items);
            AddCombo("Gender", "Gender", 80, null);
            AddCombo("Nature", "Nature", 85, _natures);
            AddCombo("IV", "IVs", 95, _ivs);
            AddCombo("EV", "EVs", 95, _evs);
            AddCombo("Happiness", "Happiness", 85, _happiness);
            AddCombo("Shiny", "Shiny", 55, _yesNo);
            AddCombo("SuperShiny", "Super shiny", 75, _yesNo);
            AddCombo("Shadow", "Shadow", 60, _yesNo);
            AddCombo("Ball", "Poké Ball", 110, _balls);
            _grid.Columns["Level"].ToolTipText = "1 to the max level in Game Settings.";
            _grid.Columns["Name"].ToolTipText = "Nickname (leave empty for the species name).";
            _grid.Columns["Move1"].ToolTipText = "Leave all four moves empty to give it the moves it would know by level.";
            _grid.Columns["IV"].ToolTipText = "One value is used for every stat.";
            _grid.Columns["EV"].ToolTipText = "One value is used for every stat.";

            _grid.DataError += (s, e) => { e.ThrowException = false; };
            _grid.CurrentCellDirtyStateChanged += (s, e) => { if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellValueChanged += (s, e) => { if (!_filling && e.RowIndex >= 0) Pull(e.RowIndex, _grid.Columns[e.ColumnIndex].Name); };
            _grid.CellValidating += ValidateLevel;
            _grid.SelectionChanged += (s, e) => UpdateButtons();
            _grid.EditingControlShowing += (s, e) =>
            {
                var cb = e.Control as ComboBox;
                if (cb == null) return;
                cb.SelectionChangeCommitted -= OnPicked;
                cb.SelectionChangeCommitted += OnPicked;
            };
            // one click opens a dropdown / starts typing (edit only starts on a click, never while the grid is being filled)
            _grid.CellMouseClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Button != MouseButtons.Left) return;
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (!_grid.BeginEdit(true)) return;
                var cb = _grid.EditingControl as ComboBox;
                if (cb != null) BeginInvoke(new Action(() => { if (_grid.EditingControl == cb) cb.DroppedDown = true; }));
            };
            _grid.KeyPress += (s, e) =>
            {
                if (_grid.IsCurrentCellInEditMode || _grid.CurrentCell == null || char.IsControl(e.KeyChar) || !_grid.BeginEdit(true)) return;
                var tb = _grid.EditingControl as TextBox;   // typing into a level/nickname cell starts with that letter
                if (tb != null) { tb.Text = e.KeyChar.ToString(); tb.SelectionStart = 1; e.Handled = true; }
            };
            _grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.F2 || (e.KeyCode == Keys.Enter && !_grid.IsCurrentCellInEditMode)) { if (_grid.CurrentCell != null) _grid.BeginEdit(true); e.Handled = true; } };
            _grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && !_grid.IsCurrentCellInEditMode) { RemoveSelected(); e.Handled = true; } };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(0, 3, 0, 0) };
            _add = new Button { Text = "Add Pokémon", Width = 100, Height = 25 };
            _remove = new Button { Text = "Remove", Width = 70, Height = 25 };
            _up = new Button { Text = "▲", Width = 30, Height = 25 };
            _down = new Button { Text = "▼", Width = 30, Height = 25 };
            var note = new Label { AutoSize = true, ForeColor = Theme.SubText, Padding = new Padding(8, 6, 0, 0), Text = "Moves, abilities, forms and genders are the ones this Pokémon has in the database." };
            _add.Click += (s, e) => AddPokemon();
            _remove.Click += (s, e) => RemoveSelected();
            _up.Click += (s, e) => MoveSelected(-1);
            _down.Click += (s, e) => MoveSelected(1);
            bar.Controls.AddRange(new Control[] { _add, _remove, _up, _down, note });

            Controls.Add(_grid);
            Controls.Add(bar);
            Fill();
        }

        // ------------------------------------------------------------------ columns

        void AddText(string name, string header, int width)
        {
            var c = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, MinimumWidth = width, FillWeight = width, SortMode = DataGridViewColumnSortMode.NotSortable };
            _grid.Columns.Add(c);
        }

        void AddCombo(string name, string header, int width, List<Choice> shared)
        {
            var c = new DataGridViewComboBoxColumn
            {
                Name = name, HeaderText = header, MinimumWidth = width, FillWeight = width, SortMode = DataGridViewColumnSortMode.NotSortable,
                DisplayMember = "Text", ValueMember = "Value", ValueType = typeof(string),
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton, FlatStyle = FlatStyle.Flat, MaxDropDownItems = 16, DropDownWidth = Math.Max(width, 210)
            };
            if (shared != null) foreach (var ch in shared) c.Items.Add(ch);
            _grid.Columns.Add(c);
        }

        void BuildSharedLists()
        {
            var p = P;
            _species = new List<Choice>();
            _items = new List<Choice> { new Choice("", "(none)") };
            _balls = new List<Choice> { new Choice("", "(Poké Ball)") };
            if (p != null)
            {
                foreach (var s in p.Species.Where(s => s.Form == 0)) _species.Add(new Choice(s.Id, s.Id));
                foreach (var it in p.Items) _items.Add(new Choice(it.Id, ItemName(it)));
                foreach (var it in p.Items.Where(IsBall)) _balls.Add(new Choice(it.Id, ItemName(it)));
            }
            _natures = new List<Choice> { new Choice("", "(random)") };
            foreach (var n in Natures.All) _natures.Add(new Choice(n, Cap(n)));
            _ivs = new List<Choice> { new Choice("", "(random)") };
            for (int i = 31; i >= 0; i--) _ivs.Add(new Choice(i.ToString(CultureInfo.InvariantCulture), i + (i == 31 ? " (perfect)" : "")));
            _evs = new List<Choice> { new Choice("", "(none)") };
            for (int i = 0; i <= 252; i += 4) _evs.Add(new Choice(i.ToString(CultureInfo.InvariantCulture), i.ToString(CultureInfo.InvariantCulture)));
            _evs.Add(new Choice("252", "252"));
            _evs = _evs.GroupBy(c => c.Value).Select(g => g.First()).ToList();
            _happiness = new List<Choice> { new Choice("", "(species default)") };
            for (int i = 255; i >= 0; i--) _happiness.Add(new Choice(i.ToString(CultureInfo.InvariantCulture), i + (i == 255 ? " (max)" : "")));
            _yesNo = new List<Choice> { new Choice("False", "No"), new Choice("True", "Yes") };
        }

        static bool IsBall(ItemData it) { return it.Pocket == 3 || (it.Flags != null && (it.Flags.Contains("PokeBall") || it.Flags.Contains("SnagBall"))); }
        static string ItemName(ItemData it) { return string.IsNullOrEmpty(it.Name) ? it.Id : it.Name; }
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : s.Substring(0, 1).ToUpperInvariant() + s.Substring(1).ToLowerInvariant(); }

        // ------------------------------------------------------------------ per-Pokémon lists

        SpeciesData Data(string species, int form)
        {
            var p = P;
            if (p == null || string.IsNullOrEmpty(species)) return null;
            string id = form > 0 ? species + "_" + form : species;
            return p.Species.FirstOrDefault(s => s.Id == id) ?? p.Species.FirstOrDefault(s => s.Id == species);
        }

        string MoveName(string id)
        {
            var m = P == null ? null : P.Moves.FirstOrDefault(x => x.Id == id);
            return m != null && !string.IsNullOrEmpty(m.Name) ? m.Name : id;
        }

        string AbilityName(string id)
        {
            var a = P == null ? null : P.Abilities.FirstOrDefault(x => x.Id == id);
            return a != null && !string.IsNullOrEmpty(a.Name) ? a.Name : id;
        }

        List<Choice> FormChoices(string species)
        {
            var list = new List<Choice>();
            var p = P;
            if (p != null && !string.IsNullOrEmpty(species))
                foreach (var s in p.Species.Where(s => s.Id == species || s.Species == species).OrderBy(s => s.Form))
                {
                    string label = s.Form + (string.IsNullOrEmpty(s.FormName) ? (s.Form == 0 ? " (normal)" : "") : " (" + s.FormName + ")");
                    if (!list.Any(c => c.Value == s.Form.ToString(CultureInfo.InvariantCulture))) list.Add(new Choice(s.Form.ToString(CultureInfo.InvariantCulture), label));
                }
            if (list.Count == 0) list.Add(new Choice("0", "0 (normal)"));
            return list;
        }

        List<Choice> MoveChoices(SpeciesData d, bool first)
        {
            var list = new List<Choice> { new Choice("", first ? "(by level)" : "(none)") };
            if (d == null) return list;
            var seen = new HashSet<string>();
            foreach (var lm in d.Moves.OrderBy(m => m.Level))
                if (seen.Add(lm.Move)) list.Add(new Choice(lm.Move, MoveName(lm.Move) + "  · Lv " + lm.Level));
            foreach (var m in d.TutorMoves.OrderBy(MoveName))
                if (seen.Add(m)) list.Add(new Choice(m, MoveName(m) + "  · TM/tutor"));
            foreach (var m in d.EggMoves.OrderBy(MoveName))
                if (seen.Add(m)) list.Add(new Choice(m, MoveName(m) + "  · egg"));
            return list;
        }

        List<Choice> AbilityChoices(SpeciesData d)
        {
            var list = new List<Choice> { new Choice("", "(from ability slot)") };
            if (d == null) return list;
            foreach (var a in d.Abilities.Distinct()) list.Add(new Choice(a, AbilityName(a)));
            foreach (var a in d.HiddenAbilities.Distinct()) if (!list.Any(c => c.Value == a)) list.Add(new Choice(a, AbilityName(a) + " (hidden)"));
            return list;
        }

        List<Choice> AbilitySlotChoices(SpeciesData d)
        {
            var list = new List<Choice> { new Choice("", "(random)") };
            if (d == null) return list;
            for (int i = 0; i < Math.Min(2, d.Abilities.Count); i++) list.Add(new Choice(i.ToString(CultureInfo.InvariantCulture), i + ": " + AbilityName(d.Abilities[i])));
            for (int i = 0; i < d.HiddenAbilities.Count && i < 4; i++) list.Add(new Choice((i + 2).ToString(CultureInfo.InvariantCulture), (i + 2) + ": " + AbilityName(d.HiddenAbilities[i]) + " (hidden)"));
            return list;
        }

        static List<Choice> GenderChoices(SpeciesData d)
        {
            string ratio = d != null ? d.GenderRatio : null;
            if (ratio == "Genderless") return new List<Choice> { new Choice("", "(genderless)") };
            if (ratio == "AlwaysMale") return new List<Choice> { new Choice("", "(random)"), new Choice("male", "Male") };
            if (ratio == "AlwaysFemale") return new List<Choice> { new Choice("", "(random)"), new Choice("female", "Female") };
            return new List<Choice> { new Choice("", "(random)"), new Choice("male", "Male"), new Choice("female", "Female") };
        }

        /// <summary>Puts the lists for this row's Pokémon into its cells.</summary>
        void SetRowChoices(DataGridViewRow row, TrainerPokemon tp)
        {
            var d = Data(tp.Species, tp.Form);
            SetItems(row, "Form", FormChoices(tp.Species));
            for (int i = 1; i <= 4; i++) SetItems(row, "Move" + i, MoveChoices(d, i == 1));
            SetItems(row, "Ability", AbilityChoices(d));
            SetItems(row, "AbilityIndex", AbilitySlotChoices(d));
            SetItems(row, "Gender", GenderChoices(d));
        }

        void SetItems(DataGridViewRow row, string col, List<Choice> choices)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells[col];
            cell.Items.Clear();
            foreach (var c in choices) cell.Items.Add(c);
        }

        /// <summary>Sets a dropdown cell; a value that isn't in its list (e.g. from an import) is kept and marked.</summary>
        void SetValue(DataGridViewRow row, string col, string value, string unknownNote)
        {
            value = value ?? "";
            var cell = row.Cells[col] as DataGridViewComboBoxCell;
            if (cell != null && !cell.Items.Cast<Choice>().Any(c => c.Value == value))
            {
                // the shared lists live on the column; a cell only gets its own copy when it needs an extra entry
                if (cell.Items.Count == 0) foreach (var c in ((DataGridViewComboBoxColumn)_grid.Columns[col]).Items) cell.Items.Add(c);
                if (!cell.Items.Cast<Choice>().Any(c => c.Value == value)) cell.Items.Add(new Choice(value, value + unknownNote));
            }
            row.Cells[col].Value = value;
        }

        // ------------------------------------------------------------------ fill / pull

        void Fill()
        {
            _filling = true;
            try
            {
                _grid.Rows.Clear();
                foreach (var tp in _list)
                {
                    int r = _grid.Rows.Add();
                    ShowRow(_grid.Rows[r], tp);
                }
            }
            finally { _filling = false; }
            _grid.CurrentCell = null;
            Height = 32 + _grid.ColumnHeadersHeight + Math.Max(3, _list.Count) * (_grid.RowTemplate.Height + 1) + 22;
            UpdateButtons();
        }

        void ShowRow(DataGridViewRow row, TrainerPokemon tp)
        {
            bool was = _filling; _filling = true;
            try
            {
                SetRowChoices(row, tp);
                SetValue(row, "Species", tp.Species, " (not in database)");
                row.Cells["Level"].Value = tp.Level.ToString(CultureInfo.InvariantCulture);
                SetValue(row, "Form", tp.Form.ToString(CultureInfo.InvariantCulture), " (no such form)");
                row.Cells["Name"].Value = tp.Name ?? "";
                for (int i = 0; i < 4; i++) SetValue(row, "Move" + (i + 1), i < tp.Moves.Count ? tp.Moves[i] : "", " (can't learn)");
                SetValue(row, "Ability", tp.Ability, " (not its ability)");
                SetValue(row, "AbilityIndex", tp.AbilityIndex.HasValue ? tp.AbilityIndex.Value.ToString(CultureInfo.InvariantCulture) : "", "");
                SetValue(row, "Item", tp.Item, " (not in database)");
                string g = string.IsNullOrEmpty(tp.Gender) ? "" : tp.Gender.StartsWith("f", StringComparison.OrdinalIgnoreCase) ? "female" : "male";
                SetValue(row, "Gender", g, " (not possible)");
                SetValue(row, "Nature", string.IsNullOrEmpty(tp.Nature) ? "" : tp.Nature.ToUpperInvariant(), "");
                SetValue(row, "IV", StatsText(tp.IV), " (per stat)");
                SetValue(row, "EV", StatsText(tp.EV), " (per stat)");
                SetValue(row, "Happiness", tp.Happiness.HasValue ? tp.Happiness.Value.ToString(CultureInfo.InvariantCulture) : "", "");
                SetValue(row, "Shiny", tp.Shiny ? "True" : "False", "");
                SetValue(row, "SuperShiny", tp.SuperShiny ? "True" : "False", "");
                SetValue(row, "Shadow", tp.Shadow ? "True" : "False", "");
                SetValue(row, "Ball", tp.Ball, " (not in database)");
            }
            finally { _filling = was; }
        }

        static string StatsText(List<int> v)
        {
            if (v == null || v.Count == 0) return "";
            if (v.All(x => x == v[0])) return v[0].ToString(CultureInfo.InvariantCulture);
            return string.Join(",", v.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        static List<int> ParseStats(string s)
        {
            var l = new List<int>();
            foreach (var part in (s ?? "").Split(',')) { int n; if (int.TryParse(part.Trim(), out n)) l.Add(n); }
            return l;
        }

        string Cell(DataGridViewRow row, string col)
        {
            string raw = Convert.ToString(row.Cells[col].Value) ?? "";
            // some versions of the grid hand back the shown text instead of the value
            var cb = row.Cells[col] as DataGridViewComboBoxCell;
            if (cb != null)
            {
                var items = cb.Items.Count > 0 ? cb.Items.Cast<Choice>() : ((DataGridViewComboBoxColumn)_grid.Columns[col]).Items.Cast<Choice>();
                if (!items.Any(c => c.Value == raw)) { var m = items.FirstOrDefault(c => c.Text == raw); if (m != null) return m.Value; }
            }
            return raw;
        }

        void Pull(int r, string col)
        {
            if (r < 0 || r >= _list.Count) return;
            var row = _grid.Rows[r]; var tp = _list[r];
            string v = Cell(row, col);
            switch (col)
            {
                case "Species":
                    if (v.Length == 0) { BeginInvoke(new Action(() => ShowRow(row, tp))); return; }   // a Pokémon always has a species
                    if (v == tp.Species) return;
                    tp.Species = v; tp.Form = 0;
                    KeepValid(tp);
                    BeginInvoke(new Action(() => { _grid.EndEdit(); ShowRow(row, tp); }));   // not while the dropdown is still closing
                    break;
                case "Form":
                    { int f; int.TryParse(v, out f); if (f == tp.Form) return; tp.Form = f; KeepValid(tp); BeginInvoke(new Action(() => { _grid.EndEdit(); ShowRow(row, tp); })); }
                    break;
                case "Level": { int n; if (int.TryParse(v, out n)) tp.Level = Math.Max(1, n); } break;
                case "Name": tp.Name = v.Trim().Length == 0 ? null : v; break;
                case "Move1": case "Move2": case "Move3": case "Move4":
                    tp.Moves = Enumerable.Range(1, 4).Select(i => Cell(row, "Move" + i)).Where(m => m.Length > 0).ToList();
                    break;
                case "Ability": tp.Ability = v.Length == 0 ? null : v; break;
                case "AbilityIndex": { int n; tp.AbilityIndex = int.TryParse(v, out n) ? (int?)n : null; } break;
                case "Item": tp.Item = v.Length == 0 ? null : v; break;
                case "Gender": tp.Gender = v.Length == 0 ? null : v; break;
                case "Nature": tp.Nature = v.Length == 0 ? null : v; break;
                case "IV": tp.IV = ParseStats(v); break;
                case "EV": tp.EV = ParseStats(v); break;
                case "Happiness": { int n; tp.Happiness = int.TryParse(v, out n) ? (int?)n : null; } break;
                case "Shiny": tp.Shiny = v == "True"; break;
                case "SuperShiny": tp.SuperShiny = v == "True"; break;
                case "Shadow": tp.Shadow = v == "True"; break;
                case "Ball": tp.Ball = v.Length == 0 ? null : v; break;
            }
            Fire();
        }

        /// <summary>After the species or form changes, drops the settings the new one can't have.</summary>
        void KeepValid(TrainerPokemon tp)
        {
            var d = Data(tp.Species, tp.Form);
            if (d == null) return;
            var learnable = new HashSet<string>(d.Moves.Select(m => m.Move).Concat(d.TutorMoves).Concat(d.EggMoves));
            tp.Moves = tp.Moves.Where(learnable.Contains).ToList();
            if (!string.IsNullOrEmpty(tp.Ability) && !d.Abilities.Contains(tp.Ability) && !d.HiddenAbilities.Contains(tp.Ability)) tp.Ability = null;
            if (tp.AbilityIndex.HasValue && !AbilitySlotChoices(d).Any(c => c.Value == tp.AbilityIndex.Value.ToString(CultureInfo.InvariantCulture))) tp.AbilityIndex = null;
            string g = string.IsNullOrEmpty(tp.Gender) ? "" : tp.Gender.StartsWith("f", StringComparison.OrdinalIgnoreCase) ? "female" : "male";
            if (g.Length > 0 && !GenderChoices(d).Any(c => c.Value == g)) tp.Gender = null;
        }

        void ValidateLevel(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (_grid.Columns[e.ColumnIndex].Name != "Level") return;
            int n; int max = P != null && P.Settings.MaxLevel > 0 ? P.Settings.MaxLevel : 100;
            string s = Convert.ToString(e.FormattedValue).Trim();
            if (!int.TryParse(s, out n) || n < 1 || n > max)
            {
                _grid.Rows[e.RowIndex].ErrorText = "Level must be 1 to " + max + ".";
                e.Cancel = true;
            }
            else _grid.Rows[e.RowIndex].ErrorText = "";
        }

        void Fire() { if (Changed != null) Changed(); }

        void OnPicked(object sender, EventArgs e)
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        // ------------------------------------------------------------------ add / remove / order

        int SelectedRow { get { return _grid.CurrentCell != null ? _grid.CurrentCell.RowIndex : -1; } }

        void UpdateButtons()
        {
            if (_add == null) return;
            int r = SelectedRow;
            _add.Enabled = _list.Count < MaxParty;
            _remove.Enabled = r >= 0 && r < _list.Count;
            _up.Enabled = r > 0;
            _down.Enabled = r >= 0 && r < _list.Count - 1;
        }

        void AddPokemon()
        {
            if (_list.Count >= MaxParty) return;
            var last = _list.LastOrDefault();
            var tp = new TrainerPokemon
            {
                Species = last != null ? last.Species : (_species.Count > 0 ? _species[0].Value : ""),
                Level = last != null ? last.Level : 5
            };
            _list.Add(tp);
            Fill();
            SelectCell(_list.Count - 1, "Species");
            Fire();
        }

        void RemoveSelected()
        {
            int r = SelectedRow;
            if (r < 0 || r >= _list.Count) return;
            _grid.EndEdit();
            _list.RemoveAt(r);
            Fill();
            if (_list.Count > 0) SelectCell(Math.Min(r, _list.Count - 1), "Species");
            Fire();
        }

        void MoveSelected(int d)
        {
            int r = SelectedRow, j = r + d;
            if (r < 0 || j < 0 || j >= _list.Count) return;
            _grid.EndEdit();
            var tp = _list[r]; _list.RemoveAt(r); _list.Insert(j, tp);
            string col = _grid.CurrentCell.OwningColumn.Name;
            Fill();
            SelectCell(j, col);
            Fire();
        }

        void SelectCell(int r, string col)
        {
            if (r < 0 || r >= _grid.Rows.Count) return;
            try { _grid.CurrentCell = _grid.Rows[r].Cells[col]; } catch (InvalidOperationException) { }
            UpdateButtons();
        }
    }
}
