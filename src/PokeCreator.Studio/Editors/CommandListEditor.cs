using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Json;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// The command list of an event page or common event: shows the commands, adds them from a palette
    /// (grouped like RPG Maker's command window), edits, deletes and moves them.
    /// </summary>
    public class CommandListEditor : UserControl
    {
        readonly Project _p;
        readonly MapData _map;          // null for common events
        readonly List<EventCommand> _list;
        readonly ListBox _cmds = new ListBox { Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9.5f), IntegralHeight = false, HorizontalScrollbar = true };
        public event Action Changed;

        public CommandListEditor(Project p, List<EventCommand> list, MapData map)
        {
            _p = p; _list = list; _map = map; Dock = DockStyle.Fill; Padding = new Padding(6);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36 };
            var add = new Button { Text = "＋ Add Command…", AutoSize = true, Height = 30, Font = new Font(Font, FontStyle.Bold) };
            add.Click += (s, e) => ShowPalette();
            bar.Controls.Add(add);
            AddCmdButton(bar, "Text", AddText);
            AddCmdButton(bar, "Choices", AddChoices);
            AddCmdButton(bar, "If", AddConditional);
            AddCmdButton(bar, "Switch", AddSwitch);
            AddCmdButton(bar, "Variable", AddVariable);
            AddCmdButton(bar, "Move Route", AddMoveRoute);
            AddCmdButton(bar, "Script", AddScript);
            var edits = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
            AddCmdButton(edits, "Edit…", EditSelected);
            AddCmdButton(edits, "Delete", DeleteSelected);
            AddCmdButton(edits, "▲", () => MoveSelected(-1));
            AddCmdButton(edits, "▼", () => MoveSelected(1));
            AddCmdButton(edits, "Edit as JSON…", EditJson);
            Controls.Add(_cmds);
            Controls.Add(new Label { Text = "Commands run top to bottom. Select a line, then add: new commands go above it. Double-click to edit; Insert or Enter on the last line to add.", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray, AutoEllipsis = true });
            Controls.Add(bar);
            Controls.Add(edits);
            _cmds.DoubleClick += (s, e) => { if (L[Sel].Code == 0) ShowPalette(); else EditSelected(); };
            _cmds.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) DeleteSelected();
                else if (e.KeyCode == Keys.Insert || (e.KeyCode == Keys.Enter && L[Sel].Code == 0)) ShowPalette();
                else if (e.KeyCode == Keys.Enter) EditSelected();
                else return;
                e.Handled = true;
            };
            _cmds.Resize += (s, e) => ClampTop();
            RefreshList(0);
        }

        // Mono's ListBox can scroll past the end when a row is selected before it has been laid out (it's 1×1
        // then), and its next paint throws for short lists such as a brand-new event's. Keep the scroll in range.
        void ClampTop() { if (_cmds.Items.Count > 0 && _cmds.TopIndex >= _cmds.Items.Count) _cmds.TopIndex = 0; }

        static void AddCmdButton(FlowLayoutPanel bar, string text, Action a) { var b = new Button { Text = text, AutoSize = true, Height = 28 }; b.Click += (s, e) => a(); bar.Controls.Add(b); }

        void Mark() { if (Changed != null) Changed(); }

        // ------------------------------------------------------------------ list
        List<EventCommand> L { get { return _list; } }

        void RefreshList(int select)
        {
            if (L.Count == 0 || L[L.Count - 1].Code != 0 || L[L.Count - 1].Indent != 0) L.Add(new EventCommand(0, 0));
            // AddRange instead of BeginUpdate/EndUpdate: Mono's ListBox can paint a stale item count after EndUpdate
            var lines = L.Select(c => (object)(new string(' ', c.Indent * 3) + CommandText.Describe(c, _p))).ToArray();
            _cmds.Items.Clear();
            _cmds.Items.AddRange(lines);
            _cmds.SelectedIndex = Math.Max(0, Math.Min(select, L.Count - 1));
            ClampTop();
        }

        int Sel { get { return Math.Max(0, Math.Min(_cmds.SelectedIndex, L.Count - 1)); } }

        void Insert(params EventCommand[] cmds)
        {
            int at = Sel, indent = L[at].Indent;
            foreach (var c in cmds) c.Indent += indent;
            L.InsertRange(at, cmds);
            RefreshList(at + cmds.Length);
            Mark();
        }

        static readonly HashSet<int> BlockStarts = new HashSet<int> { 111, 102, 112 };
        static readonly HashSet<int> Structural = new HashSet<int> { 0, 401, 402, 403, 404, 411, 412, 413, 408, 655, 509 };

        static int ContinuationOf(int code) { return code == 101 ? 401 : code == 108 ? 408 : code == 355 ? 655 : code == 209 ? 509 : -1; }

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
                int cont = ContinuationOf(c.Code);
                while (cont > 0 && end + 1 < L.Count && L[end + 1].Code == cont) end++;
            }
            L.RemoveRange(i, Math.Min(L.Count - 1, end) - i + 1);
            RefreshList(i);
            Mark();
        }

        void MoveSelected(int d)
        {
            int i = Sel, j = i + d;
            if (j < 0 || j >= L.Count - 1 || Structural.Contains(L[i].Code) || BlockStarts.Contains(L[i].Code) || Structural.Contains(L[j].Code) || BlockStarts.Contains(L[j].Code) || L[i].Indent != L[j].Indent) return;
            if (ContinuationOf(L[i].Code) > 0 && j + 1 < L.Count && L[i + 1].Code == ContinuationOf(L[i].Code)) return; // multi-line commands move with Edit as JSON
            var t = L[i]; L[i] = L[j]; L[j] = t;
            RefreshList(j);
            Mark();
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
                        RefreshList(i); Mark(); return;
                    }
                case 108: case 355:
                    {
                        int cont = ContinuationOf(c.Code);
                        string full = c.StrParam(0); int k = i + 1;
                        while (k < L.Count && L[k].Code == cont) { full += "\n" + L[k].StrParam(0); k++; }
                        string t = TextDialog.Ask(FindForm(), c.Code == 108 ? "Edit comment" : "Edit script", full, true);
                        if (t == null) return;
                        L.RemoveRange(i + 1, k - i - 1);
                        var lines = t.Replace("\r", "").Split('\n');
                        c.Params = new List<object> { lines[0] };
                        for (int n = lines.Length - 1; n >= 1; n--) L.Insert(i + 1, new EventCommand(cont, c.Indent, lines[n]));
                        RefreshList(i); Mark(); return;
                    }
                case 201: { var r = TransferDialog.Ask(FindForm(), _p, c); if (r != null) { c.Params = r.Params; RefreshList(i); Mark(); } return; }
                case 209: EditMoveRoute(i); return;
                case 117:
                    {
                        int id = PickCommonEvent(c.IntParam(0)); if (id <= 0) return;
                        c.Params = new List<object> { id }; RefreshList(i); Mark(); return;
                    }
                case 0: ShowPalette(); return;
                default: EditJson(); return;
            }
        }

        void EditJson()
        {
            int i = Sel; var c = L[i];
            string json = Json.Json.Write(c);
            string t = TextDialog.Ask(FindForm(), "Edit command " + c.Code + " (JSON)", json, true);
            if (t == null) return;
            try { var nc = Json.Json.Deserialize<EventCommand>(t); L[i] = nc; RefreshList(i); Mark(); }
            catch (Exception ex) { MessageBox.Show(this, "Invalid JSON: " + ex.Message); }
        }

        // ------------------------------------------------------------------ palette
        void ShowPalette()
        {
            var groups = new List<KeyValuePair<string, List<KeyValuePair<string, Action>>>>();
            Func<string, List<KeyValuePair<string, Action>>> group = name => { var l = new List<KeyValuePair<string, Action>>(); groups.Add(new KeyValuePair<string, List<KeyValuePair<string, Action>>>(name, l)); return l; };
            Action<List<KeyValuePair<string, Action>>, string, Action> cmd = (l, n, a) => l.Add(new KeyValuePair<string, Action>(n, a));

            var msg = group("Messages");
            cmd(msg, "Show Text", AddText);
            cmd(msg, "Show Choices", AddChoices);
            cmd(msg, "Input Number", AddInputNumber);
            cmd(msg, "Comment", AddComment);

            var flow = group("Flow");
            cmd(flow, "Conditional Branch", AddConditional);
            cmd(flow, "Loop", () => Insert(new EventCommand(112, 0), new EventCommand(0, 1), new EventCommand(413, 0)));
            cmd(flow, "Break Loop", () => Insert(new EventCommand(113, 0)));
            cmd(flow, "Exit Event Processing", () => Insert(new EventCommand(115, 0)));
            cmd(flow, "Call Common Event", () => { int id = PickCommonEvent(0); if (id > 0) Insert(new EventCommand(117, 0, id)); });
            cmd(flow, "Label", () => { string n = TextDialog.Ask(FindForm(), "Label name", "", false); if (!string.IsNullOrEmpty(n)) Insert(new EventCommand(118, 0, n)); });
            cmd(flow, "Jump to Label", AddJumpToLabel);
            cmd(flow, "Wait", AddWait);
            cmd(flow, "Erase Event", () => Insert(new EventCommand(116, 0)));

            var state = group("Switches && Variables");
            cmd(state, "Control Switches", AddSwitch);
            cmd(state, "Control Variables", AddVariable);
            cmd(state, "Control Self Switch", AddSelfSwitch);

            var party = group("Player && Party");
            cmd(party, "Give Pokémon", AddGivePokemon);
            cmd(party, "Give Item", AddGiveItem);
            cmd(party, "Change Money", AddMoney);
            cmd(party, "Heal Party (Recover All)", () => Insert(new EventCommand(314, 0, 0)));

            var map = group("Map && Movement");
            cmd(map, "Transfer Player (Warp)", AddTransfer);
            cmd(map, "Set Event Location", AddEventLocation);
            cmd(map, "Set Move Route", AddMoveRoute);
            cmd(map, "Wait for Move's Completion", () => Insert(new EventCommand(210, 0)));
            cmd(map, "Player Transparent / Visible", () => { int k = ChoiceDialog.Ask(FindForm(), "Player is", new[] { "Transparent (hidden)", "Normal (visible)" }); if (k >= 0) Insert(new EventCommand(208, 0, k)); });

            var screen = group("Screen");
            cmd(screen, "Change Screen Tone", AddTone);
            cmd(screen, "Screen Flash", () => { int f = NumberDialog.Ask(FindForm(), "Flash length (1/20 second units)", 10); if (f > 0) Insert(new EventCommand(224, 0, ColorObj(255, 255, 255, 255), f)); });

            var audio = group("Audio");
            cmd(audio, "Play BGM", AddMusic);
            cmd(audio, "Fade Out BGM", () => { int sec = NumberDialog.Ask(FindForm(), "Fade out over (seconds)", 1); if (sec >= 0) Insert(new EventCommand(242, 0, sec)); });
            cmd(audio, "Play ME (jingle)", () => { string n = AssetPick("Audio/ME", "Music effect"); if (n != null) Insert(new EventCommand(249, 0, AudioObj(n))); });
            cmd(audio, "Play SE", AddSound);

            var adv = group("Advanced");
            cmd(adv, "Script", AddScript);

            using (var dlg = new CommandPalette(groups))
            {
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK && dlg.Chosen != null) dlg.Chosen();
            }
        }

        /// <summary>A window of command buttons grouped by kind; clicking one closes it and runs that command's dialog.</summary>
        class CommandPalette : Form
        {
            public Action Chosen;
            public CommandPalette(List<KeyValuePair<string, List<KeyValuePair<string, Action>>>> groups)
            {
                Text = "Add Command"; StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
                var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8), AutoSize = true };
                for (int i = 0; i < 3; i++) columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
                var stacks = new FlowLayoutPanel[3];
                for (int i = 0; i < 3; i++) { stacks[i] = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill }; columns.Controls.Add(stacks[i], i, 0); }
                // Spread the groups over three columns, balancing their height
                var heights = new int[3];
                foreach (var g in groups)
                {
                    int col = Array.IndexOf(heights, heights.Min());
                    stacks[col].Controls.Add(new Label { Text = g.Key, Font = Theme.HeaderFont, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(2, heights[col] == 0 ? 0 : 10, 2, 2) });
                    foreach (var c in g.Value)
                    {
                        var act = c.Value;
                        var b = new Button { Text = c.Key, Width = 210, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(2) };
                        b.Click += (s, e) => { Chosen = act; DialogResult = DialogResult.OK; Close(); };
                        stacks[col].Controls.Add(b);
                    }
                    heights[col] += 34 + g.Value.Count * 32;
                }
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 28 };
                var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 4, 8, 4) };
                bottom.Controls.Add(cancel);
                CancelButton = cancel;
                Controls.Add(columns); Controls.Add(bottom);
                ClientSize = new Size(3 * 230 + 24, heights.Max() + 70);
            }
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

        void AddInputNumber()
        {
            int id = IdPickerDialog.Ask(FindForm(), _p, true, 1, "Store the number in which variable?"); if (id < 1) return;
            int digits = NumberDialog.Ask(FindForm(), "How many digits (1-8)", 3); if (digits < 1) return;
            Insert(new EventCommand(103, 0, id, Math.Min(8, digits)));
        }

        void AddComment()
        {
            string t = TextDialog.Ask(FindForm(), "Comment (a note for yourself; the game ignores it)", "", true);
            if (string.IsNullOrEmpty(t)) return;
            var lines = t.Replace("\r", "").Split('\n');
            var cmds = new List<EventCommand> { new EventCommand(108, 0, lines[0]) };
            for (int i = 1; i < lines.Length; i++) cmds.Add(new EventCommand(408, 0, lines[i]));
            Insert(cmds.ToArray());
        }

        void AddConditional()
        {
            var kinds = new[] { "Switch is ON", "Switch is OFF", "Self switch is ON", "Variable compared to a number", "Money is at least", "Script condition" };
            int k = ChoiceDialog.Ask(FindForm(), "Conditional branch", kinds);
            if (k < 0) return;
            EventCommand cond;
            switch (k)
            {
                case 0: case 1: { int id = IdPickerDialog.Ask(FindForm(), _p, false, 1, "Which switch?"); if (id < 1) return; cond = new EventCommand(111, 0, 0, id, k == 0 ? 0 : 1); break; }
                case 2: { int ch = ChoiceDialog.Ask(FindForm(), "Self switch", new[] { "A", "B", "C", "D" }); if (ch < 0) return; cond = new EventCommand(111, 0, 2, "ABCD"[ch].ToString(), 0); break; }
                case 3:
                    {
                        int id = IdPickerDialog.Ask(FindForm(), _p, true, 1, "Which variable?"); if (id < 1) return;
                        string[] ops = { "== equal to", ">= at least", "<= at most", "> more than", "< less than", "!= not equal to" };
                        int op = ChoiceDialog.Ask(FindForm(), "Variable " + id + " is", ops); if (op < 0) return;
                        int v = NumberDialog.Ask(FindForm(), "Number", 1, -99999);
                        cond = new EventCommand(111, 0, 1, id, 0, v, op); break;
                    }
                case 4: { int m = NumberDialog.Ask(FindForm(), "Money at least", 100); if (m < 0) return; cond = new EventCommand(111, 0, 7, m, 0); break; }
                default: { string s = TextDialog.Ask(FindForm(), "Script condition", "$game_switches[1]", false); if (s == null) return; cond = new EventCommand(111, 0, 12, s); break; }
            }
            Insert(cond, new EventCommand(0, 1), new EventCommand(411, 0), new EventCommand(0, 1), new EventCommand(412, 0));
        }

        void AddJumpToLabel()
        {
            var labels = L.Where(c => c.Code == 118).Select(c => c.StrParam(0)).Distinct().ToArray();
            if (labels.Length == 0) { MessageBox.Show(FindForm(), "Add a Label first; Jump to Label goes to it.", "Jump to Label"); return; }
            int k = ChoiceDialog.Ask(FindForm(), "Jump to label", labels); if (k < 0) return;
            Insert(new EventCommand(119, 0, labels[k]));
        }

        void AddSwitch()
        {
            int id = IdPickerDialog.Ask(FindForm(), _p, false, 1, "Turn which switch ON or OFF?"); if (id < 1) return;
            int on = ChoiceDialog.Ask(FindForm(), "Set switch " + id + " (" + _p.SwitchName(id) + ") to", new[] { "ON", "OFF" }); if (on < 0) return;
            Insert(new EventCommand(121, 0, id, id, on));
        }

        void AddSelfSwitch()
        {
            int ch = ChoiceDialog.Ask(FindForm(), "Self switch", new[] { "A = ON", "A = OFF", "B = ON", "B = OFF", "C = ON", "C = OFF", "D = ON", "D = OFF" }); if (ch < 0) return;
            Insert(new EventCommand(123, 0, "ABCD"[ch / 2].ToString(), ch % 2));
        }

        void AddVariable()
        {
            int id = IdPickerDialog.Ask(FindForm(), _p, true, 1, "Change which variable?"); if (id < 1) return;
            int op = ChoiceDialog.Ask(FindForm(), "Variable " + id + " (" + _p.VariableName(id) + ")", new[] { "Set to", "Add", "Subtract", "Multiply by", "Divide by", "Remainder after dividing by" }); if (op < 0) return;
            int src = ChoiceDialog.Ask(FindForm(), "Using", new[] { "A number", "Another variable", "A random number" }); if (src < 0) return;
            if (src == 0) { int v = NumberDialog.Ask(FindForm(), "Number", 0, -99999); Insert(new EventCommand(122, 0, id, id, op, 0, v)); }
            else if (src == 1) { int v = IdPickerDialog.Ask(FindForm(), _p, true, 1, "Read the value from which variable?"); if (v < 1) return; Insert(new EventCommand(122, 0, id, id, op, 1, v)); }
            else { int lo = NumberDialog.Ask(FindForm(), "Random from", 1, -99999); int hi = NumberDialog.Ask(FindForm(), "Random to", 10, -99999); Insert(new EventCommand(122, 0, id, id, op, 2, lo, hi)); }
        }

        void AddGivePokemon()
        {
            var ids = _p.Species.Where(s => s.Form == 0).Select(s => s.Id).ToArray();
            int k = ChoiceDialog.Ask(FindForm(), "Give Pokémon", ids, true); if (k < 0) return;
            int lv = NumberDialog.Ask(FindForm(), "Level", 5); if (lv < 1) return;
            Insert(new EventCommand(355, 0, "pbAddPokemon(:" + ids[k] + ", " + Math.Min(100, lv) + ")"));
        }

        void AddGiveItem()
        {
            var ids = _p.Items.Select(it => it.Id).ToArray();
            int k = ChoiceDialog.Ask(FindForm(), "Give item", ids, true); if (k < 0) return;
            Insert(new EventCommand(355, 0, "pbReceiveItem(:" + ids[k] + ")"));
        }

        void AddMoney()
        {
            int op = ChoiceDialog.Ask(FindForm(), "Money", new[] { "Give money", "Take money" }); if (op < 0) return;
            int v = NumberDialog.Ask(FindForm(), "Amount", 100); if (v < 0) return;
            Insert(new EventCommand(125, 0, op, 0, v));
        }

        void AddTransfer() { var c = TransferDialog.Ask(FindForm(), _p, null); if (c != null) Insert(c); }

        void AddEventLocation()
        {
            int who = PickCharacter("Move which event?", false); if (who < -1) return;
            int x = NumberDialog.Ask(FindForm(), "New X", 0); if (x < 0) return;
            int y = NumberDialog.Ask(FindForm(), "New Y", 0); if (y < 0) return;
            Insert(new EventCommand(202, 0, who, 0, x, y, 0));
        }

        void AddMoveRoute()
        {
            int who = 0; bool wait; MoveRoute route = new MoveRoute { Repeat = false, Skippable = false };
            if (!MoveRouteDialog.Ask(FindForm(), _p, _map, true, ref who, route, out wait)) return;
            var cmds = new List<EventCommand> { RouteCommand(who, route) };
            cmds.AddRange(RouteLines(route, 0));
            if (wait) cmds.Add(new EventCommand(210, 0));
            Insert(cmds.ToArray());
        }

        void EditMoveRoute(int i)
        {
            var c = L[i];
            int who = c.IntParam(0);
            var obj = c.Params.Count > 1 ? c.Params[1] as JsonObject : null;
            var route = obj != null ? (MoveRoute)Json.Json.ConvertTo(obj, typeof(MoveRoute)) : new MoveRoute { Repeat = false };
            if (route.List == null) route.List = new List<EventCommand>();
            bool wait;
            if (!MoveRouteDialog.Ask(FindForm(), _p, _map, true, ref who, route, out wait)) return;
            int k = i + 1; while (k < L.Count && L[k].Code == 509) k++;
            L.RemoveRange(i + 1, k - i - 1);
            L[i] = RouteCommand(who, route); L[i].Indent = c.Indent;
            L.InsertRange(i + 1, RouteLines(route, c.Indent));
            bool hasWait = i + 1 + route.List.Count(x => x.Code != 0) < L.Count && L[i + 1 + route.List.Count(x => x.Code != 0)].Code == 210;
            if (wait && !hasWait) L.Insert(i + 1 + route.List.Count(x => x.Code != 0), new EventCommand(210, c.Indent));
            RefreshList(i); Mark();
        }

        static EventCommand RouteCommand(int who, MoveRoute route)
        {
            if (route.List.Count == 0 || route.List[route.List.Count - 1].Code != 0) route.List.Add(new EventCommand(0, 0));
            return new EventCommand(209, 0, who, (JsonObject)Json.Json.Parse(Json.Json.Write(route))); // plain JSON tree, same as a loaded file
        }

        /// <summary>The "$> Move Down" lines shown under Set Move Route (RPG Maker keeps them for display).</summary>
        static List<EventCommand> RouteLines(MoveRoute route, int indent)
        {
            var l = new List<EventCommand>();
            foreach (var m in route.List.Where(x => x.Code != 0))
            {
                var mc = new JsonObject(); mc["Code"] = m.Code; mc["Params"] = m.Params;
                l.Add(new EventCommand(509, indent, mc));
            }
            return l;
        }

        void AddTone()
        {
            var presets = new[] { "Normal", "Night (dark blue)", "Evening (orange)", "Sepia (old photo)", "Grey (black and white)", "Black (fade out)" };
            double[][] vals = { new[] { 0.0, 0, 0, 0 }, new[] { -68.0, -68, 0, 68 }, new[] { 17.0, -34, -68, 17 }, new[] { 34.0, 0, -34, 170 }, new[] { 0.0, 0, 0, 255 }, new[] { -255.0, -255, -255, 0 } };
            int k = ChoiceDialog.Ask(FindForm(), "Change screen tone to", presets); if (k < 0) return;
            int f = NumberDialog.Ask(FindForm(), "Change over (1/20 second units, 0 = instantly)", 20); if (f < 0) return;
            var tone = new JsonObject(); tone["_class"] = "Tone"; tone["Values"] = vals[k].Select(v => (object)v).ToList();
            Insert(new EventCommand(223, 0, tone, f));
        }

        static JsonObject ColorObj(int r, int g, int b, int a) { var c = new JsonObject(); c["_class"] = "Color"; c["Values"] = new List<object> { (double)r, (double)g, (double)b, (double)a }; return c; }

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

        void AddScript()
        {
            string s = TextDialog.Ask(FindForm(), "Script", "", true, "Calls a script command, e.g. pbReceiveItem(:POTION) or one defined in your Scripts folder.");
            if (string.IsNullOrEmpty(s)) return;
            var lines = s.Replace("\r", "").Split('\n');
            var cmds = new List<EventCommand> { new EventCommand(355, 0, lines[0]) };
            for (int i = 1; i < lines.Length; i++) cmds.Add(new EventCommand(655, 0, lines[i]));
            Insert(cmds.ToArray());
        }

        // ------------------------------------------------------------------ pickers
        int PickCommonEvent(int current)
        {
            var list = _p.CommonEvents.OrderBy(c => c.Id).ToList();
            if (list.Count == 0) { MessageBox.Show(FindForm(), "There are no common events yet. Make one under Database > Common Events.", "Call Common Event"); return -1; }
            int k = ChoiceDialog.Ask(FindForm(), "Call which common event?", list.Select(c => c.ToString()).ToArray());
            return k < 0 ? -1 : list[k].Id;
        }

        /// <summary>-1 player, 0 this event, n event n; returns -2 if cancelled.</summary>
        int PickCharacter(string title, bool allowPlayer)
        {
            var ids = new List<int>(); var names = new List<string>();
            if (allowPlayer) { ids.Add(-1); names.Add("Player"); }
            ids.Add(0); names.Add("This event");
            if (_map != null) foreach (var e in _map.Events.OrderBy(x => x.Id)) { ids.Add(e.Id); names.Add("Event " + e.Id.ToString("000") + ": " + e.Name); }
            int k = ChoiceDialog.Ask(FindForm(), title, names.ToArray());
            return k < 0 ? -2 : ids[k];
        }

        string AssetPick(string folder, string title)
        {
            string dir = Path.Combine(_p.Root, folder.Replace('/', Path.DirectorySeparatorChar));
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileNameWithoutExtension).Distinct().OrderBy(x => x).ToArray() : new string[0];
            if (files.Length == 0) { MessageBox.Show(FindForm(), "There are no files in " + folder + " yet.", title); return null; }
            int k = ChoiceDialog.Ask(FindForm(), title, files, true);
            return k < 0 ? null : files[k];
        }

        static JsonObject AudioObj(string name) { var a = new JsonObject(); a["Name"] = name; a["Volume"] = 100; a["Pitch"] = 100; return a; }
    }

    /// <summary>
    /// Builds a move route: who moves, and a list of steps (move, turn, jump, wait, change speed...).
    /// Used by Set Move Route and by an event page's custom movement.
    /// </summary>
    public class MoveRouteDialog : Form
    {
        readonly MoveRoute _route;
        readonly ListBox _steps = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font(FontFamily.GenericMonospace, 9.5f) };
        readonly ComboBox _who = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly CheckBox _repeat = new CheckBox { Text = "Repeat the route", AutoSize = true };
        readonly CheckBox _skip = new CheckBox { Text = "Skip steps that are blocked", AutoSize = true };
        readonly CheckBox _wait = new CheckBox { Text = "Then wait until it finishes", AutoSize = true, Checked = true };
        readonly List<int> _whoIds = new List<int>();
        public int Who { get { return _who.SelectedIndex >= 0 ? _whoIds[_who.SelectedIndex] : 0; } }

        /// <summary>Edits a route in place. withTarget: show "who moves" and "wait" (Set Move Route); false for a page's custom movement.</summary>
        public static bool Ask(IWin32Window owner, Project p, MapData map, bool withTarget, ref int who, MoveRoute route, out bool wait)
        {
            using (var d = new MoveRouteDialog(p, map, withTarget, who, route))
            {
                bool ok = d.ShowDialog(owner) == DialogResult.OK;
                wait = ok && withTarget && d._wait.Checked;
                if (ok) { who = d.Who; route.Repeat = d._repeat.Checked; route.Skippable = d._skip.Checked; }
                return ok;
            }
        }

        MoveRouteDialog(Project p, MapData map, bool withTarget, int who, MoveRoute route)
        {
            _route = route;
            if (_route.List == null) _route.List = new List<EventCommand>();
            _route.List.RemoveAll(x => x.Code == 0);
            Text = withTarget ? "Set Move Route" : "Custom Movement Route";
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            Size = new Size(930, 620);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(6, 4, 6, 0) };
            if (withTarget)
            {
                top.Controls.Add(new Label { Text = "Who moves:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
                _whoIds.Add(-1); _who.Items.Add("Player");
                _whoIds.Add(0); _who.Items.Add("This event");
                if (map != null) foreach (var e in map.Events.OrderBy(x => x.Id)) { _whoIds.Add(e.Id); _who.Items.Add("Event " + e.Id.ToString("000") + ": " + e.Name); }
                int k = _whoIds.IndexOf(who); _who.SelectedIndex = k >= 0 ? k : 1;
                top.Controls.Add(_who);
            }
            _repeat.Checked = route.Repeat; _skip.Checked = route.Skippable;
            top.Controls.Add(_repeat); top.Controls.Add(_skip);
            if (withTarget) top.Controls.Add(_wait);

            // Left: the steps; right: buttons that add steps
            var left = new Panel { Dock = DockStyle.Left, Width = 360, Padding = new Padding(6) };
            var stepBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
            Action<string, Action> sb = (t, a) => { var b = new Button { Text = t, AutoSize = true }; b.Click += (s, e) => a(); stepBar.Controls.Add(b); };
            sb("Delete", () => { int i = _steps.SelectedIndex; if (i >= 0 && i < _route.List.Count) { _route.List.RemoveAt(i); Fill(i); } });
            sb("▲", () => Swap(-1)); sb("▼", () => Swap(1));
            sb("Clear", () => { _route.List.Clear(); Fill(0); });
            left.Controls.Add(_steps);
            left.Controls.Add(new Label { Text = "Steps (new steps go above the selected one)", Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray });
            left.Controls.Add(stepBar);
            _steps.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { int i = _steps.SelectedIndex; if (i >= 0 && i < _route.List.Count) { _route.List.RemoveAt(i); Fill(i); } } };

            var grid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
            Action<string> head = t => grid.Controls.Add(new Label { Text = t, Font = Theme.HeaderFont, ForeColor = Theme.Accent, AutoSize = false, Width = 520, Height = 22, Margin = new Padding(2, 8, 2, 2) });
            Action<int, string> btn = (code, label) =>
            {
                var b = new Button { Text = label, Width = 124, Height = 28, Margin = new Padding(2) };
                b.Click += (s, e) => AddStep(code);
                grid.Controls.Add(b);
            };
            head("Move");
            btn(1, "↓ Down"); btn(2, "← Left"); btn(3, "→ Right"); btn(4, "↑ Up");
            btn(5, "↙ Lower left"); btn(6, "↘ Lower right"); btn(7, "↖ Upper left"); btn(8, "↗ Upper right");
            btn(9, "At random"); btn(10, "Toward player"); btn(11, "Away from player"); btn(12, "1 step forward");
            btn(13, "1 step backward"); btn(14, "Jump…"); btn(15, "Wait…");
            head("Turn");
            btn(16, "Face down"); btn(17, "Face left"); btn(18, "Face right"); btn(19, "Face up");
            btn(20, "Turn 90° right"); btn(21, "Turn 90° left"); btn(22, "Turn 180°"); btn(23, "90° right or left");
            btn(24, "Turn at random"); btn(25, "Face player"); btn(26, "Face away");
            head("Settings");
            btn(29, "Speed…"); btn(30, "Frequency…"); btn(27, "Switch ON…"); btn(28, "Switch OFF…");
            btn(31, "Walk anim ON"); btn(32, "Walk anim OFF"); btn(33, "Step anim ON"); btn(34, "Step anim OFF");
            btn(35, "Dir. fix ON"); btn(36, "Dir. fix OFF"); btn(37, "Through ON"); btn(38, "Through OFF");
            btn(39, "On top ON"); btn(40, "On top OFF"); btn(42, "Opacity…"); btn(41, "Graphic…");
            btn(44, "Play SE…"); btn(45, "Script…");

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 6) };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
            bottom.Controls.Add(cancel); bottom.Controls.Add(ok);
            AcceptButton = ok; CancelButton = cancel;
            Controls.Add(grid); Controls.Add(left); Controls.Add(top); Controls.Add(bottom);
            _project = p;
            Fill(0);
        }

        readonly Project _project;

        void Fill(int select)
        {
            _steps.BeginUpdate();
            _steps.Items.Clear();
            foreach (var m in _route.List) _steps.Items.Add(Describe(m));
            _steps.Items.Add("◆");
            _steps.EndUpdate();
            _steps.SelectedIndex = Math.Max(0, Math.Min(select, _steps.Items.Count - 1));
            if (_steps.TopIndex >= _steps.Items.Count) _steps.TopIndex = 0; // Mono: see CommandListEditor.ClampTop
        }

        static string Describe(EventCommand m)
        {
            string name = CommandText.MoveName(m.Code);
            switch (m.Code)
            {
                case 14: return name + " (" + m.IntParam(0) + ", " + m.IntParam(1) + ")";
                case 15: return name + " " + m.IntParam(0) + " frame(s)";
                case 27: case 28: return name + ": " + m.IntParam(0);
                case 29: case 30: case 42: return name + ": " + m.IntParam(0);
                case 41: return name + ": " + m.StrParam(0);
                case 44: { var a = m.Params.Count > 0 ? m.Params[0] as JsonObject : null; return name + ": " + (a == null ? "" : a.GetString("Name")); }
                case 45: return name + ": " + m.StrParam(0);
                default: return name;
            }
        }

        void Swap(int d)
        {
            int i = _steps.SelectedIndex, j = i + d;
            if (i < 0 || i >= _route.List.Count || j < 0 || j >= _route.List.Count) return;
            var t = _route.List[i]; _route.List[i] = _route.List[j]; _route.List[j] = t;
            Fill(j);
        }

        void AddStep(int code)
        {
            EventCommand m;
            switch (code)
            {
                case 14:
                    {
                        int x = NumberDialog.Ask(this, "Jump: tiles right (negative = left)", 0, -20);
                        int y = NumberDialog.Ask(this, "Jump: tiles down (negative = up)", 0, -20);
                        m = new EventCommand(14, 0, x, y); break;
                    }
                case 15: { int f = NumberDialog.Ask(this, "Wait (1/20 second units)", 4); if (f < 1) return; m = new EventCommand(15, 0, f); break; }
                case 27: case 28: { int id = IdPickerDialog.Ask(this, _project, false, 1, code == 27 ? "Turn which switch ON?" : "Turn which switch OFF?"); if (id < 1) return; m = new EventCommand(code, 0, id); break; }
                case 29: { int k = ChoiceDialog.Ask(this, "Movement speed", new[] { "1: Slowest", "2: Slower", "3: Slow", "4: Normal", "5: Fast", "6: Fastest" }); if (k < 0) return; m = new EventCommand(29, 0, k + 1); break; }
                case 30: { int k = ChoiceDialog.Ask(this, "Movement frequency", new[] { "1: Lowest", "2: Lower", "3: Low", "4: Normal", "5: High", "6: Highest" }); if (k < 0) return; m = new EventCommand(30, 0, k + 1); break; }
                case 42: { int o = NumberDialog.Ask(this, "Opacity (0 = invisible, 255 = solid)", 255); if (o < 0) return; m = new EventCommand(42, 0, Math.Min(255, o)); break; }
                case 41:
                    {
                        string dir = Path.Combine(_project.GraphicsDir, "Characters");
                        var files = Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileNameWithoutExtension).Distinct().OrderBy(x => x).ToArray() : new string[0];
                        if (files.Length == 0) return;
                        int k = ChoiceDialog.Ask(this, "Change graphic to", files, true); if (k < 0) return;
                        m = new EventCommand(41, 0, files[k], 0, 2, 0); break;
                    }
                case 44:
                    {
                        string dir = Path.Combine(_project.Root, "Audio", "SE");
                        var files = Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileNameWithoutExtension).Distinct().OrderBy(x => x).ToArray() : new string[0];
                        if (files.Length == 0) return;
                        int k = ChoiceDialog.Ask(this, "Play sound effect", files, true); if (k < 0) return;
                        var a = new JsonObject(); a["Name"] = files[k]; a["Volume"] = 100; a["Pitch"] = 100;
                        m = new EventCommand(44, 0, a); break;
                    }
                case 45: { string s = TextDialog.Ask(this, "Script", "", false); if (string.IsNullOrEmpty(s)) return; m = new EventCommand(45, 0, s); break; }
                default: m = new EventCommand(code, 0); break;
            }
            int at = Math.Max(0, Math.Min(_steps.SelectedIndex < 0 ? _route.List.Count : _steps.SelectedIndex, _route.List.Count));
            _route.List.Insert(at, m);
            Fill(at + 1);
        }
    }
}
