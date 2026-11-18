using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Json;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>Turns event commands into readable lines, like RPG Maker's event list.</summary>
    public static class CommandText
    {
        public static string Describe(EventCommand c, Project p)
        {
            string s;
            switch (c.Code)
            {
                case 0: return "◆";
                case 101: return "◆ Text: " + c.StrParam(0);
                case 401: return "    " + c.StrParam(0);
                case 102: { var l = c.Params.Count > 0 ? c.Params[0] as List<object> : null; return "◆ Show Choices: " + (l == null ? "" : string.Join(", ", l)); }
                case 402: return ": When [" + Choice(c, p) + "]";
                case 403: return ": When Cancel";
                case 404: return ": Branch End";
                case 106: return "◆ Wait: " + c.IntParam(0) + " frame(s)";
                case 108: return "◆ Comment: " + c.StrParam(0);
                case 408: return "    " + c.StrParam(0);
                case 111: return "◆ If: " + Condition(c, p);
                case 411: return ": Else";
                case 412: return ": Branch End";
                case 112: return "◆ Loop";
                case 413: return ": Repeat Above";
                case 113: return "◆ Break Loop";
                case 115: return "◆ Exit Event Processing";
                case 116: return "◆ Erase Event";
                case 117: return "◆ Call Common Event: " + c.IntParam(0);
                case 118: return "◆ Label: " + c.StrParam(0);
                case 119: return "◆ Jump to Label: " + c.StrParam(0);
                case 121:
                    s = c.IntParam(0) == c.IntParam(1) ? "[" + c.IntParam(0).ToString("0000") + ": " + p.SwitchName(c.IntParam(0)) + "]" : "[" + c.IntParam(0) + ".." + c.IntParam(1) + "]";
                    return "◆ Control Switches: " + s + " = " + (c.IntParam(2) == 0 ? "ON" : "OFF");
                case 122:
                    {
                        string[] ops = { "=", "+=", "-=", "*=", "/=", "%=" };
                        string target = "[" + c.IntParam(0).ToString("0000") + ": " + p.VariableName(c.IntParam(0)) + "]";
                        string val = c.IntParam(3) == 0 ? c.IntParam(4).ToString() : c.IntParam(3) == 1 ? "Variable [" + c.IntParam(4) + "]" : c.IntParam(3) == 2 ? "Random " + c.IntParam(4) + ".." + c.IntParam(5) : "(other)";
                        return "◆ Control Variables: " + target + " " + ops[Math.Min(5, c.IntParam(2))] + " " + val;
                    }
                case 123: return "◆ Control Self Switch: " + c.StrParam(0) + " = " + (c.IntParam(1) == 0 ? "ON" : "OFF");
                case 125: return "◆ Change Money: " + (c.IntParam(0) == 0 ? "+" : "-") + c.IntParam(2);
                case 201:
                    {
                        if (c.IntParam(0) != 0) return "◆ Transfer Player: (from variables)";
                        var mi = p.GetMapInfo(c.IntParam(1));
                        return "◆ Transfer Player: [" + c.IntParam(1).ToString("000") + ": " + (mi != null ? mi.Name : "?") + "] (" + c.IntParam(2) + ", " + c.IntParam(3) + ")" + DirName(c.IntParam(4)) + (c.IntParam(5) == 0 ? "" : ", no fade");
                    }
                case 202: return "◆ Set Event Location: " + Who(c.IntParam(0)) + " (" + c.IntParam(2) + ", " + c.IntParam(3) + ")";
                case 208: return "◆ Change Transparent Flag: " + (c.IntParam(0) == 0 ? "Transparent" : "Normal");
                case 209: return "◆ Set Move Route: " + Who(c.IntParam(0));
                case 509: { var mc = c.Params.Count > 0 ? c.Params[0] as JsonObject : null; return "    $> " + (mc == null ? "" : MoveName(mc.GetInt("Code"))); }
                case 210: return "◆ Wait for Move's Completion";
                case 223: return "◆ Change Screen Color Tone";
                case 224: return "◆ Screen Flash";
                case 225: return "◆ Screen Shake";
                case 241: return "◆ Play BGM: " + Audio(c);
                case 242: return "◆ Fade Out BGM: " + c.IntParam(0) + " sec";
                case 245: return "◆ Play BGS: " + Audio(c);
                case 249: return "◆ Play ME: " + Audio(c);
                case 250: return "◆ Play SE: " + Audio(c);
                case 301: return "◆ Battle Processing";
                case 314: return "◆ Recover All";
                case 355: return "◆ Script: " + c.StrParam(0);
                case 655: return "    " + c.StrParam(0);
                default: return "◆ [" + c.Code + "] " + string.Join(", ", c.Params.Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)));
            }
        }

        static string Choice(EventCommand c, Project p) { return c.StrParam(1).Length > 0 ? c.StrParam(1) : "choice " + (c.IntParam(0) + 1); }
        static string Who(int id) { return id < 0 ? "Player" : id == 0 ? "This event" : "Event " + id; }
        static string DirName(int d) { return d == 2 ? ", facing down" : d == 4 ? ", facing left" : d == 6 ? ", facing right" : d == 8 ? ", facing up" : ""; }
        static string Audio(EventCommand c) { var a = c.Params.Count > 0 ? c.Params[0] as JsonObject : null; return a == null ? "" : a.GetString("Name") + " (" + a.GetInt("Volume", 100) + ")"; }

        static readonly string[] Moves = { "", "Move Down", "Move Left", "Move Right", "Move Up", "Move Lower Left", "Move Lower Right", "Move Upper Left", "Move Upper Right", "Move at Random", "Move toward Player", "Move away from Player", "1 Step Forward", "1 Step Backward", "Jump", "Wait", "Turn Down", "Turn Left", "Turn Right", "Turn Up", "Turn 90° Right", "Turn 90° Left", "Turn 180°", "Turn 90° Right or Left", "Turn at Random", "Turn toward Player", "Turn away from Player", "Switch ON", "Switch OFF", "Change Speed", "Change Frequency", "Move Animation ON", "Move Animation OFF", "Stop Animation ON", "Stop Animation OFF", "Direction Fix ON", "Direction Fix OFF", "Through ON", "Through OFF", "Always on Top ON", "Always on Top OFF", "Change Graphic", "Change Opacity", "Change Blending", "Play SE", "Script" };
        public static string MoveName(int code) { return code >= 0 && code < Moves.Length ? Moves[code] : "Move " + code; }

        static string Condition(EventCommand c, Project p)
        {
            switch (c.IntParam(0))
            {
                case 0: return "Switch [" + c.IntParam(1).ToString("0000") + ": " + p.SwitchName(c.IntParam(1)) + "] is " + (c.IntParam(2) == 0 ? "ON" : "OFF");
                case 1:
                    {
                        string[] ops = { "==", ">=", "<=", ">", "<", "!=" };
                        return "Variable [" + c.IntParam(1).ToString("0000") + ": " + p.VariableName(c.IntParam(1)) + "] " + ops[Math.Min(5, c.IntParam(4))] + " " + (c.IntParam(2) == 0 ? c.IntParam(3).ToString() : "Variable [" + c.IntParam(3) + "]");
                    }
                case 2: return "Self Switch " + c.StrParam(1) + " is " + (c.IntParam(2) == 0 ? "ON" : "OFF");
                case 6: return Who(c.IntParam(1)) + " is facing " + c.IntParam(2);
                case 7: return "Money " + (c.IntParam(2) == 0 ? ">=" : "<=") + " " + c.IntParam(1);
                case 12: return "Script: " + c.StrParam(1);
            }
            return "(condition type " + c.IntParam(0) + ")";
        }
    }

    /// <summary>Edits one map event: pages, conditions, graphic, behaviour and the command list.</summary>
    public class EventEditor : Form
    {
        readonly Project _p;
        readonly MapData _map;
        readonly MapEvent _ev;
        readonly MapEvent _work;
        readonly TabControl _pages = new TabControl { Dock = DockStyle.Fill };
        readonly TextBox _name = new TextBox { Width = 220 };

        public EventEditor(Project p, MapData map, MapEvent ev)
        {
            _p = p; _map = map; _ev = ev;
            _work = Json.Json.Deserialize<MapEvent>(Json.Json.Write(ev));
            Text = "Event " + ev.Id.ToString("000") + " at (" + ev.X + ", " + ev.Y + ")";
            Width = 1000; Height = 720; StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6) };
            top.Controls.Add(new Label { Text = "Name:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            _name.Text = _work.Name;
            top.Controls.Add(_name);
            var newPage = new Button { Text = "New Page", Width = 90 }; newPage.Click += (s, e) => { _work.Pages.Add(new EventPage()); BuildPages(_work.Pages.Count - 1); };
            var copyPage = new Button { Text = "Copy Page", Width = 90 }; copyPage.Click += (s, e) => { int i = _pages.SelectedIndex; if (i >= 0) { _work.Pages.Insert(i + 1, Json.Json.Deserialize<EventPage>(Json.Json.Write(_work.Pages[i]))); BuildPages(i + 1); } };
            var delPage = new Button { Text = "Delete Page", Width = 90 }; delPage.Click += (s, e) => { int i = _pages.SelectedIndex; if (i >= 0 && _work.Pages.Count > 1) { _work.Pages.RemoveAt(i); BuildPages(Math.Max(0, i - 1)); } };
            top.Controls.AddRange(new Control[] { newPage, copyPage, delPage });
            top.Controls.Add(new Label { Text = "   The LAST page whose conditions are met is the one that runs.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 8, 0, 0) });

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
            var ok = new Button { Text = "OK", Width = 90 };
            ok.Click += (s, e) => { Apply(); DialogResult = DialogResult.OK; Close(); };
            bottom.Controls.AddRange(new Control[] { cancel, ok });
            CancelButton = cancel;

            Controls.Add(_pages); Controls.Add(top); Controls.Add(bottom);
            BuildPages(0);
        }

        void Apply()
        {
            _ev.Name = _name.Text;
            _ev.Pages = _work.Pages;
        }

        void BuildPages(int select)
        {
            _pages.TabPages.Clear();
            for (int i = 0; i < _work.Pages.Count; i++)
            {
                var tp = new TabPage("Page " + (i + 1));
                tp.Controls.Add(new PageEditor(_p, _map, _work.Pages[i]));
                _pages.TabPages.Add(tp);
            }
            _pages.SelectedIndex = Math.Min(select, _pages.TabCount - 1);
        }
    }

    public class PageEditor : UserControl
    {
        readonly Project _p;
        readonly MapData _map;
        readonly EventPage _pg;
        readonly ListBox _cmds = new ListBox { Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9.5f), IntegralHeight = false, HorizontalScrollbar = true };
        readonly PictureBox _sprite = new PictureBox { Width = 96, Height = 96, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };

        public PageEditor(Project p, MapData map, EventPage pg)
        {
            _p = p; _map = map; _pg = pg; Dock = DockStyle.Fill;
            var left = new Panel { Dock = DockStyle.Left, Width = 330, AutoScroll = true, Padding = new Padding(6) };
            int y = 4;
            left.Controls.Add(Head("Conditions", ref y));
            var c = pg.Condition;
            AddCheckNum(left, "Switch", c.Switch1Valid, c.Switch1Id, (v, n) => { c.Switch1Valid = v; c.Switch1Id = n; }, ref y);
            AddCheckNum(left, "Switch", c.Switch2Valid, c.Switch2Id, (v, n) => { c.Switch2Valid = v; c.Switch2Id = n; }, ref y);
            AddCheckNum(left, "Variable", c.VariableValid, c.VariableId, (v, n) => { c.VariableValid = v; c.VariableId = n; }, ref y);
            var varVal = new NumericUpDown { Left = 200, Top = y, Width = 80, Minimum = -99999, Maximum = 99999, Value = c.VariableValue };
            varVal.ValueChanged += (s, e) => c.VariableValue = (int)varVal.Value;
            left.Controls.Add(new Label { Text = "is at least", Left = 120, Top = y + 3, AutoSize = true }); left.Controls.Add(varVal); y += 28;
            var ssCheck = new CheckBox { Text = "Self switch", Left = 8, Top = y, Checked = c.SelfSwitchValid, Width = 100 };
            var ssCombo = new ComboBox { Left = 120, Top = y, Width = 50, DropDownStyle = ComboBoxStyle.DropDownList };
            ssCombo.Items.AddRange(new object[] { "A", "B", "C", "D" }); ssCombo.SelectedItem = c.SelfSwitchCh ?? "A";
            if (ssCombo.SelectedIndex < 0) ssCombo.SelectedIndex = 0;
            ssCheck.CheckedChanged += (s, e) => c.SelfSwitchValid = ssCheck.Checked;
            ssCombo.SelectedIndexChanged += (s, e) => c.SelfSwitchCh = (string)ssCombo.SelectedItem;
            left.Controls.Add(ssCheck); left.Controls.Add(ssCombo); y += 32;

            left.Controls.Add(Head("Graphic", ref y));
            var g = pg.Graphic;
            var charCombo = new ComboBox { Left = 8, Top = y, Width = 200, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            charCombo.Items.Add("(none)");
            string chDir = Path.Combine(p.GraphicsDir, "Characters");
            if (Directory.Exists(chDir)) charCombo.Items.AddRange(Directory.GetFiles(chDir).Select(Path.GetFileNameWithoutExtension).Distinct().OrderBy(x => x).Cast<object>().ToArray());
            charCombo.Text = string.IsNullOrEmpty(g.CharacterName) ? "(none)" : g.CharacterName;
            _sprite.Left = 220; _sprite.Top = y;
            left.Controls.Add(charCombo); left.Controls.Add(_sprite);
            var dirCombo = new ComboBox { Left = 8, Top = y + 30, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            dirCombo.Items.AddRange(new object[] { "Facing down", "Facing left", "Facing right", "Facing up" });
            dirCombo.SelectedIndex = g.Direction == 4 ? 1 : g.Direction == 6 ? 2 : g.Direction == 8 ? 3 : 0;
            left.Controls.Add(dirCombo);
            charCombo.TextChanged += (s, e) => { g.CharacterName = charCombo.Text == "(none)" ? "" : charCombo.Text; g.TileId = 0; UpdateSprite(); };
            dirCombo.SelectedIndexChanged += (s, e) => { g.Direction = new[] { 2, 4, 6, 8 }[dirCombo.SelectedIndex]; UpdateSprite(); };
            y += 104;

            left.Controls.Add(Head("Behaviour", ref y));
            var trig = Combo(left, "Starts when", Triggers.Names, pg.Trigger, i => pg.Trigger = i, ref y);
            Combo(left, "Movement", new[] { "Stays still", "Walks randomly", "Approaches player", "Custom route" }, pg.MoveType, i => pg.MoveType = i, ref y);
            Combo(left, "Speed", new[] { "1: Slowest", "2: Slower", "3: Slow", "4: Normal", "5: Fast", "6: Fastest" }, pg.MoveSpeed - 1, i => pg.MoveSpeed = i + 1, ref y);
            Combo(left, "Frequency", new[] { "1: Lowest", "2: Lower", "3: Low", "4: Normal", "5: High", "6: Highest" }, pg.MoveFrequency - 1, i => pg.MoveFrequency = i + 1, ref y);
            Check(left, "Walking animation", pg.WalkAnime, v => pg.WalkAnime = v, ref y);
            Check(left, "Stepping animation (always)", pg.StepAnime, v => pg.StepAnime = v, ref y);
            Check(left, "Direction fix (never turns)", pg.DirectionFix, v => pg.DirectionFix = v, ref y);
            Check(left, "Through (walk through it)", pg.Through, v => pg.Through = v, ref y);
            Check(left, "Always on top", pg.AlwaysOnTop, v => pg.AlwaysOnTop = v, ref y);

            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 66 };
            AddCmdButton(bar, "Text", AddText);
            AddCmdButton(bar, "Choices", AddChoices);
            AddCmdButton(bar, "If (branch)", AddConditional);
            AddCmdButton(bar, "Switch", AddSwitch);
            AddCmdButton(bar, "Self Switch", AddSelfSwitch);
            AddCmdButton(bar, "Variable", AddVariable);
            AddCmdButton(bar, "Warp", AddTransfer);
            AddCmdButton(bar, "Sound", AddSound);
            AddCmdButton(bar, "Music", AddMusic);
            AddCmdButton(bar, "Wait", AddWait);
            AddCmdButton(bar, "Give Item", AddGiveItem);
            AddCmdButton(bar, "Script", AddScript);
            AddCmdButton(bar, "Erase Event", () => Insert(new EventCommand(116, 0)));
            var edits = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
            AddCmdButton(edits, "Edit…", EditSelected);
            AddCmdButton(edits, "Delete", DeleteSelected);
            AddCmdButton(edits, "▲", () => MoveSelected(-1));
            AddCmdButton(edits, "▼", () => MoveSelected(1));
            AddCmdButton(edits, "Edit as JSON…", EditJson);
            right.Controls.Add(_cmds);
            right.Controls.Add(new Label { Text = "Commands (run top to bottom). Select a line, then add: new commands go above it.", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray });
            right.Controls.Add(bar);
            right.Controls.Add(edits);
            _cmds.DoubleClick += (s, e) => EditSelected();
            _cmds.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) DeleteSelected(); };

            Controls.Add(right); Controls.Add(left);
            RefreshList(0);
            UpdateSprite();
        }

        static Label Head(string t, ref int y) { var l = new Label { Text = t, Font = Theme.HeaderFont, ForeColor = Theme.Accent, Left = 4, Top = y, AutoSize = true }; y += 24; return l; }

        static void AddCheckNum(Panel host, string label, bool on, int num, Action<bool, int> set, ref int y)
        {
            var cb = new CheckBox { Text = label, Left = 8, Top = y, Checked = on, Width = 100 };
            var nud = new NumericUpDown { Left = 120, Top = y, Width = 70, Minimum = 1, Maximum = 9999, Value = Math.Max(1, num) };
            cb.CheckedChanged += (s, e) => set(cb.Checked, (int)nud.Value);
            nud.ValueChanged += (s, e) => set(cb.Checked, (int)nud.Value);
            host.Controls.Add(cb); host.Controls.Add(nud); y += 28;
        }

        static ComboBox Combo(Panel host, string label, string[] items, int sel, Action<int> set, ref int y)
        {
            host.Controls.Add(new Label { Text = label, Left = 8, Top = y + 3, Width = 90 });
            var cb = new ComboBox { Left = 100, Top = y, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            cb.Items.AddRange(items);
            cb.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, sel));
            cb.SelectedIndexChanged += (s, e) => set(cb.SelectedIndex);
            host.Controls.Add(cb); y += 28;
            return cb;
        }

        static void Check(Panel host, string label, bool v, Action<bool> set, ref int y)
        {
            var cb = new CheckBox { Text = label, Left = 8, Top = y, Width = 280, Checked = v };
            cb.CheckedChanged += (s, e) => set(cb.Checked);
            host.Controls.Add(cb); y += 24;
        }

        static void AddCmdButton(FlowLayoutPanel bar, string text, Action a) { var b = new Button { Text = text, AutoSize = true, Height = 28 }; b.Click += (s, e) => a(); bar.Controls.Add(b); }

        void UpdateSprite()
        {
            var old = _sprite.Image;
            var sheet = Theme.LoadImage(_p.FindAsset("Graphics/Characters", _pg.Graphic.CharacterName)) as Bitmap;
            if (sheet == null) { _sprite.Image = null; }
            else
            {
                var bmp = new Bitmap(sheet.Width / 4, sheet.Height / 4);
                using (var g = Graphics.FromImage(bmp))
                {
                    int row = _pg.Graphic.Direction == 4 ? 1 : _pg.Graphic.Direction == 6 ? 2 : _pg.Graphic.Direction == 8 ? 3 : 0;
                    g.DrawImage(sheet, new Rectangle(0, 0, bmp.Width, bmp.Height), new Rectangle((_pg.Graphic.Pattern % 4) * bmp.Width, row * bmp.Height, bmp.Width, bmp.Height), GraphicsUnit.Pixel);
                }
                sheet.Dispose();
                _sprite.Image = bmp;
            }
            if (old != null) old.Dispose();
        }

        // ------------------------------------------------------------------ list
        List<EventCommand> L { get { return _pg.List; } }

        void RefreshList(int select)
        {
            if (L.Count == 0 || L[L.Count - 1].Code != 0) L.Add(new EventCommand(0, 0));
            _cmds.BeginUpdate();
            _cmds.Items.Clear();
            foreach (var c in L) _cmds.Items.Add(new string(' ', c.Indent * 3) + CommandText.Describe(c, _p));
            _cmds.EndUpdate();
            _cmds.SelectedIndex = Math.Max(0, Math.Min(select, L.Count - 1));
        }

        int Sel { get { return Math.Max(0, _cmds.SelectedIndex); } }

        void Insert(params EventCommand[] cmds)
        {
            int at = Sel, indent = L[at].Indent;
            foreach (var c in cmds) c.Indent += indent;
            L.InsertRange(at, cmds);
            RefreshList(at + cmds.Length);
        }

        static readonly HashSet<int> BlockStarts = new HashSet<int> { 111, 102, 112 };
        static readonly HashSet<int> Structural = new HashSet<int> { 0, 401, 402, 403, 404, 411, 412, 413, 408, 655, 509 };

        void DeleteSelected()
        {
            int i = Sel;
            var c = L[i];
            if (Structural.Contains(c.Code)) return;
            int end = i;
            if (BlockStarts.Contains(c.Code))
            {
                int endCode = c.Code == 111 ? 412 : c.Code == 102 ? 404 : 413;
                while (end + 1 < L.Count && !(L[end + 1].Indent == c.Indent && L[end + 1].Code == endCode)) end++;
                end++;
            }
            else
            {
                int cont = c.Code == 101 ? 401 : c.Code == 108 ? 408 : c.Code == 355 ? 655 : c.Code == 209 ? 509 : -1;
                while (cont > 0 && end + 1 < L.Count && L[end + 1].Code == cont) end++;
            }
            L.RemoveRange(i, Math.Min(L.Count - 1, end) - i + 1);
            RefreshList(i);
        }

        void MoveSelected(int d)
        {
            int i = Sel, j = i + d;
            if (j < 0 || j >= L.Count - 1 || Structural.Contains(L[i].Code) || BlockStarts.Contains(L[i].Code) || Structural.Contains(L[j].Code) || BlockStarts.Contains(L[j].Code) || L[i].Indent != L[j].Indent) return;
            var t = L[i]; L[i] = L[j]; L[j] = t;
            RefreshList(j);
        }

        void EditSelected()
        {
            int i = Sel; var c = L[i];
            switch (c.Code)
            {
                case 101:
                    {
                        string full = c.StrParam(0); int k = i + 1;
                        while (k < L.Count && L[k].Code == 401) { full += " " + L[k].StrParam(0); k++; }
                        string t = TextDialog.Ask(FindForm(), "Edit text", full, true);
                        if (t == null) return;
                        L.RemoveRange(i + 1, k - i - 1);
                        c.Params = new List<object> { t };
                        RefreshList(i); return;
                    }
                case 355:
                    {
                        string full = c.StrParam(0); int k = i + 1;
                        while (k < L.Count && L[k].Code == 655) { full += "\n" + L[k].StrParam(0); k++; }
                        string t = TextDialog.Ask(FindForm(), "Edit script", full, true);
                        if (t == null) return;
                        L.RemoveRange(i + 1, k - i - 1);
                        var lines = t.Replace("\r", "").Split('\n');
                        c.Params = new List<object> { lines[0] };
                        for (int n = lines.Length - 1; n >= 1; n--) L.Insert(i + 1, new EventCommand(655, c.Indent, lines[n]));
                        RefreshList(i); return;
                    }
                case 201: { var r = TransferDialog.Ask(FindForm(), _p, c); if (r != null) { c.Params = r.Params; RefreshList(i); } return; }
                case 0: return;
                default: EditJson(); return;
            }
        }

        void EditJson()
        {
            int i = Sel; var c = L[i];
            string json = Json.Json.Write(c);
            string t = TextDialog.Ask(FindForm(), "Edit command " + c.Code + " (JSON)", json, true);
            if (t == null) return;
            try { var nc = Json.Json.Deserialize<EventCommand>(t); L[i] = nc; RefreshList(i); }
            catch (Exception ex) { MessageBox.Show(this, "Invalid JSON: " + ex.Message); }
        }

        // ------------------------------------------------------------------ add commands
        void AddText()
        {
            string t = TextDialog.Ask(FindForm(), "Show text", "", true, "Tip: \\PN inserts the player's name; \\n starts a new line.");
            if (!string.IsNullOrEmpty(t)) Insert(new EventCommand(101, 0, t));
        }

        void AddChoices()
        {
            string t = TextDialog.Ask(FindForm(), "Show choices (one per line, up to 4)", "Yes\nNo", true);
            if (t == null) return;
            var choices = t.Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Take(4).ToList();
            if (choices.Count == 0) return;
            var cmds = new List<EventCommand> { new EventCommand(102, 0, choices.Cast<object>().ToList(), choices.Count) };
            for (int k = 0; k < choices.Count; k++) { cmds.Add(new EventCommand(402, 0, k, choices[k])); cmds.Add(new EventCommand(0, 1)); }
            cmds.Add(new EventCommand(404, 0));
            Insert(cmds.ToArray());
        }

        void AddConditional()
        {
            var kinds = new[] { "Switch is ON", "Switch is OFF", "Self switch A is ON", "Variable >= value", "Script condition" };
            int k = ChoiceDialog.Ask(FindForm(), "Conditional branch", kinds);
            if (k < 0) return;
            EventCommand cond;
            switch (k)
            {
                case 0: case 1: { int id = NumberDialog.Ask(FindForm(), "Switch ID", 1); if (id < 1) return; cond = new EventCommand(111, 0, 0, id, k == 0 ? 0 : 1); break; }
                case 2: cond = new EventCommand(111, 0, 2, "A", 0); break;
                case 3: { int id = NumberDialog.Ask(FindForm(), "Variable ID", 1); if (id < 1) return; int v = NumberDialog.Ask(FindForm(), "At least", 1); cond = new EventCommand(111, 0, 1, id, 0, v, 1); break; }
                default: { string s = TextDialog.Ask(FindForm(), "Script condition", "$game_switches[1]", false); if (s == null) return; cond = new EventCommand(111, 0, 12, s); break; }
            }
            Insert(cond, new EventCommand(0, 1), new EventCommand(411, 0), new EventCommand(0, 1), new EventCommand(412, 0));
        }

        void AddSwitch()
        {
            int id = NumberDialog.Ask(FindForm(), "Switch ID", 1); if (id < 1) return;
            int on = ChoiceDialog.Ask(FindForm(), "Set switch " + id + " to", new[] { "ON", "OFF" }); if (on < 0) return;
            Insert(new EventCommand(121, 0, id, id, on));
        }

        void AddSelfSwitch()
        {
            int ch = ChoiceDialog.Ask(FindForm(), "Self switch", new[] { "A = ON", "A = OFF", "B = ON", "B = OFF", "C = ON", "C = OFF", "D = ON", "D = OFF" }); if (ch < 0) return;
            Insert(new EventCommand(123, 0, "ABCD"[ch / 2].ToString(), ch % 2));
        }

        void AddVariable()
        {
            int id = NumberDialog.Ask(FindForm(), "Variable ID", 1); if (id < 1) return;
            int op = ChoiceDialog.Ask(FindForm(), "Operation", new[] { "Set to", "Add", "Subtract" }); if (op < 0) return;
            int v = NumberDialog.Ask(FindForm(), "Value", 0, -99999);
            Insert(new EventCommand(122, 0, id, id, op, 0, v));
        }

        void AddTransfer() { var c = TransferDialog.Ask(FindForm(), _p, null); if (c != null) Insert(c); }

        void AddSound()
        {
            string n = AssetPick("Audio/SE", "Sound effect"); if (n == null) return;
            Insert(new EventCommand(250, 0, AudioObj(n)));
        }

        void AddMusic()
        {
            string n = AssetPick("Audio/BGM", "Background music"); if (n == null) return;
            Insert(new EventCommand(241, 0, AudioObj(n)));
        }

        void AddWait() { int f = NumberDialog.Ask(FindForm(), "Wait (1/20 second units, 20 = 1 second)", 20); if (f > 0) Insert(new EventCommand(106, 0, f)); }

        void AddGiveItem()
        {
            var ids = _p.Items.Select(it => it.Id).ToArray();
            int k = ChoiceDialog.Ask(FindForm(), "Give item", ids, true); if (k < 0) return;
            Insert(new EventCommand(355, 0, "pbReceiveItem(:" + ids[k] + ")"));
        }

        void AddScript()
        {
            string s = TextDialog.Ask(FindForm(), "Script", "", true, "Calls a script command, e.g. pbReceiveItem(:POTION) or one defined in your Scripts folder.");
            if (string.IsNullOrEmpty(s)) return;
            var lines = s.Replace("\r", "").Split('\n');
            var cmds = new List<EventCommand> { new EventCommand(355, 0, lines[0]) };
            for (int i = 1; i < lines.Length; i++) cmds.Add(new EventCommand(655, 0, lines[i]));
            Insert(cmds.ToArray());
        }

        string AssetPick(string folder, string title)
        {
            string dir = Path.Combine(_p.Root, folder.Replace('/', Path.DirectorySeparatorChar));
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileNameWithoutExtension).Distinct().OrderBy(x => x).ToArray() : new string[0];
            int k = ChoiceDialog.Ask(FindForm(), title, files, true);
            return k < 0 ? null : files[k];
        }

        static JsonObject AudioObj(string name) { var a = new JsonObject(); a["Name"] = name; a["Volume"] = 100; a["Pitch"] = 100; return a; }
    }

    // ------------------------------------------------------------------ small dialogs
    public static class TextDialog
    {
        public static string Ask(IWin32Window owner, string title, string initial, bool multiline, string hint = null)
        {
            using (var f = new Form { Text = title, Width = 560, Height = multiline ? 340 : 160, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false })
            {
                var tb = new TextBox { Dock = DockStyle.Fill, Multiline = multiline, Text = (initial ?? "").Replace("\n", "\r\n"), ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, Font = new Font("Segoe UI", 10f), AcceptsReturn = multiline };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 30 };
                f.Controls.Add(tb);
                if (hint != null) f.Controls.Add(new Label { Text = hint, Dock = DockStyle.Top, Height = 22, ForeColor = Color.DimGray });
                f.Controls.Add(ok);
                if (!multiline) f.AcceptButton = ok;
                return f.ShowDialog(owner) == DialogResult.OK ? tb.Text.Replace("\r\n", "\n") : null;
            }
        }
    }

    public static class NumberDialog
    {
        public static int Ask(IWin32Window owner, string title, int initial, int min = 0)
        {
            using (var f = new Form { Text = title, Width = 300, Height = 130, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var nud = new NumericUpDown { Left = 12, Top = 12, Width = 260, Minimum = min, Maximum = 999999, Value = Math.Max(min, initial) };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 196, Top = 48, Width = 76 };
                f.Controls.Add(nud); f.Controls.Add(ok); f.AcceptButton = ok;
                return f.ShowDialog(owner) == DialogResult.OK ? (int)nud.Value : -1;
            }
        }
    }

    public static class ChoiceDialog
    {
        public static int Ask(IWin32Window owner, string title, string[] items, bool searchable = false)
        {
            using (var f = new Form { Text = title, Width = 360, Height = searchable ? 480 : Math.Min(480, 110 + items.Length * 20), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false })
            {
                var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
                var search = new TextBox { Dock = DockStyle.Top, Visible = searchable };
                Action fill = () => { list.Items.Clear(); string q = search.Text.ToLowerInvariant(); foreach (var it in items) if (q == "" || it.ToLowerInvariant().Contains(q)) list.Items.Add(it); if (list.Items.Count > 0) list.SelectedIndex = 0; };
                search.TextChanged += (s, e) => fill();
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 30 };
                list.DoubleClick += (s, e) => { f.DialogResult = DialogResult.OK; f.Close(); };
                f.Controls.Add(list); f.Controls.Add(search); f.Controls.Add(ok); f.AcceptButton = ok;
                fill();
                if (f.ShowDialog(owner) != DialogResult.OK || list.SelectedItem == null) return -1;
                return Array.IndexOf(items, (string)list.SelectedItem);
            }
        }
    }

    public static class TransferDialog
    {
        public static EventCommand Ask(IWin32Window owner, Project p, EventCommand existing)
        {
            using (var f = new Form { Text = "Warp player (Transfer)", Width = 420, Height = 260, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var maps = new ComboBox { Left = 100, Top = 12, Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var mi in p.MapInfos.OrderBy(m => m.Id)) maps.Items.Add(mi);
                var x = new NumericUpDown { Left = 100, Top = 44, Width = 80, Maximum = 999 };
                var y = new NumericUpDown { Left = 230, Top = 44, Width = 80, Maximum = 999 };
                var dir = new ComboBox { Left = 100, Top = 76, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
                dir.Items.AddRange(new object[] { "Keep direction", "Down", "Left", "Right", "Up" }); dir.SelectedIndex = 0;
                var fade = new CheckBox { Text = "Fade out and in", Left = 100, Top = 108, Checked = true, Width = 200 };
                if (existing != null)
                {
                    maps.SelectedItem = p.MapInfos.FirstOrDefault(m => m.Id == existing.IntParam(1));
                    x.Value = existing.IntParam(2); y.Value = existing.IntParam(3);
                    int di = Array.IndexOf(new[] { 0, 2, 4, 6, 8 }, existing.IntParam(4)); dir.SelectedIndex = di >= 0 ? di : 0;
                    fade.Checked = existing.IntParam(5) == 0;
                }
                if (maps.SelectedIndex < 0 && maps.Items.Count > 0) maps.SelectedIndex = 0;
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 300, Top = 170, Width = 90 };
                f.Controls.AddRange(new Control[] { new Label { Text = "Map", Left = 12, Top = 15 }, maps, new Label { Text = "X", Left = 12, Top = 47 }, x, new Label { Text = "Y", Left = 200, Top = 47, Width = 20 }, y,
                    new Label { Text = "Direction", Left = 12, Top = 79 }, dir, fade, ok,
                    new Label { Text = "Tip: open the target map and hover a tile to see its X, Y.", Left = 12, Top = 140, Width = 380, ForeColor = Color.DimGray } });
                f.AcceptButton = ok;
                if (f.ShowDialog(owner) != DialogResult.OK || maps.SelectedItem == null) return null;
                return new EventCommand(201, 0, 0, ((MapInfo)maps.SelectedItem).Id, (int)x.Value, (int)y.Value, new[] { 0, 2, 4, 6, 8 }[dir.SelectedIndex], fade.Checked ? 0 : 1);
            }
        }
    }
}
