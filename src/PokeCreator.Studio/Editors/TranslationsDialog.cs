using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Projects;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Game → Translations…: the project's languages (Languages/*.json). New Language makes a file with every message,
    /// choice and database name; Refresh Texts adds what's new; Edit opens a two-column grid to type translations.
    /// Players pick the language in the game's Options.
    /// </summary>
    public class TranslationsDialog : Form
    {
        readonly Project _p;
        readonly ListBox _list = new ListBox { Width = 360, Height = 220, IntegralHeight = false };

        public TranslationsDialog(Project p)
        {
            _p = p;
            Text = "Translations";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 300);
            var help = new Label { Text = "Each language is a file in the project's Languages folder. Players choose one in the game's Options.", AutoSize = false, Width = 490, Height = 34, Location = new Point(12, 10) };
            _list.Location = new Point(12, 48);
            _list.DoubleClick += (s, e) => EditSelected();
            Controls.Add(help); Controls.Add(_list);
            int y = 48;
            Func<string, EventHandler, Button> btn = (t, h) => { var b = new Button { Text = t, Width = 130, Height = 28, Location = new Point(380, y) }; b.Click += h; Controls.Add(b); y += 34; return b; };
            btn("New Language…", (s, e) => NewLanguage());
            btn("Edit…", (s, e) => EditSelected());
            btn("Refresh Texts", (s, e) => Refresh(true));
            btn("Open Folder", (s, e) => { Directory.CreateDirectory(Localization.Folder(_p)); MainForm.OpenInShell(Localization.Folder(_p)); });
            var close = new Button { Text = "Close", Width = 130, Height = 28, Location = new Point(380, 262), DialogResult = DialogResult.OK };
            Controls.Add(close); AcceptButton = close; CancelButton = close;
            Refresh(false);
        }

        string Selected { get { return _list.SelectedItem != null ? ((string)_list.SelectedItem).Split(new[] { "  (" }, StringSplitOptions.None)[0] : null; } }

        void Refresh(bool export)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                _list.Items.Clear();
                foreach (var name in Localization.Available(_p))
                {
                    if (export) Localization.Export(_p, name);
                    var lf = Localization.Load(_p, name);
                    int done = lf.Texts.Count(kv => !string.IsNullOrEmpty(kv.Value));
                    _list.Items.Add(name + "  (" + done + " of " + lf.Texts.Count + " translated)");
                }
                if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            }
            finally { Cursor = Cursors.Default; }
        }

        void NewLanguage()
        {
            string name = Prompt.Ask(this, "New Language", "Language name (e.g. Español):", "");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = string.Join("_", name.Trim().Split(Path.GetInvalidFileNameChars()));
            Cursor = Cursors.WaitCursor;
            try { Localization.Export(_p, name); } finally { Cursor = Cursors.Default; }
            Refresh(false);
        }

        void EditSelected()
        {
            var name = Selected;
            if (name == null) return;
            using (var ed = new TranslationGrid(_p, name)) ed.ShowDialog(this);
            Refresh(false);
        }
    }

    /// <summary>Original texts on the left, translations on the right; untranslated rows are highlighted.</summary>
    public class TranslationGrid : Form
    {
        readonly Project _p;
        readonly string _name;
        readonly LanguageFile _lf;
        readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
        readonly TextBox _filter = new TextBox { Width = 260 };
        readonly CheckBox _onlyMissing = new CheckBox { Text = "Only untranslated", AutoSize = true };

        public TranslationGrid(Project p, string name)
        {
            _p = p; _name = name; _lf = Localization.Load(p, name);
            Text = "Translation: " + name;
            ClientSize = new Size(900, 600); StartPosition = FormStartPosition.CenterParent;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6) };
            top.Controls.Add(new Label { Text = "Find:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            top.Controls.Add(_filter); top.Controls.Add(_onlyMissing);
            var save = new Button { Text = "Save", Width = 90 };
            save.Click += (s, e) => { Commit(); Localization.Save(_p, _name, _lf); DialogResult = DialogResult.OK; };
            top.Controls.Add(save);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Original", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True } });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Translation", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True } });
            _grid.CellValueChanged += (s, e) => { if (e.RowIndex >= 0) { var r = _grid.Rows[e.RowIndex]; _lf.Texts[(string)r.Tag] = Convert.ToString(r.Cells[1].Value) ?? ""; Shade(r); } };
            Controls.Add(_grid); Controls.Add(top);
            _filter.TextChanged += (s, e) => Fill();
            _onlyMissing.CheckedChanged += (s, e) => Fill();
            Fill();
        }

        void Shade(DataGridViewRow r) { r.Cells[1].Style.BackColor = string.IsNullOrEmpty(Convert.ToString(r.Cells[1].Value)) ? Color.FromArgb(255, 244, 214) : SystemColors.Window; }

        void Commit() { _grid.EndEdit(); }

        void Fill()
        {
            Commit();
            _grid.Rows.Clear();
            string f = _filter.Text.Trim();
            foreach (var kv in _lf.Texts)
            {
                if (_onlyMissing.Checked && !string.IsNullOrEmpty(kv.Value)) continue;
                if (f.Length > 0 && kv.Key.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 && (kv.Value ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int i = _grid.Rows.Add(kv.Key, kv.Value);
                _grid.Rows[i].Tag = kv.Key;
                Shade(_grid.Rows[i]);
                if (_grid.Rows.Count >= 3000) break;
            }
        }
    }
}
