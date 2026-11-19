using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Common events: events that aren't on a map. Called from other events (Call Common Event), or run
    /// on their own while a switch is ON (Autorun stops the player, Parallel runs alongside play).
    /// </summary>
    public class CommonEventsEditor : UserControl, IDocument
    {
        readonly Project _p;
        readonly MainForm _main;
        readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        readonly Panel _detail = new Panel { Dock = DockStyle.Fill };
        public bool Dirty { get; private set; }
        public string Title { get { return "Common Events"; } }
        public event Action DirtyChanged;

        public CommonEventsEditor(MainForm main, Project p)
        {
            _main = main; _p = p; Dock = DockStyle.Fill;
            var left = new Panel { Dock = DockStyle.Left, Width = 250, BackColor = Theme.Sidebar, Padding = new Padding(4) };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
            var add = new Button { Text = "New", Width = 56 }; add.Click += (s, e) => AddEvent();
            var dup = new Button { Text = "Duplicate", Width = 72 }; dup.Click += (s, e) => Duplicate();
            var del = new Button { Text = "Delete", Width = 58 }; del.Click += (s, e) => DeleteEvent();
            bar.Controls.AddRange(new Control[] { add, dup, del });
            left.Controls.Add(_list);
            left.Controls.Add(new Label { Text = "Called from events with Call Common Event, or run while a switch is ON.", Dock = DockStyle.Top, Height = 34, ForeColor = Color.DimGray });
            left.Controls.Add(bar);
            Controls.Add(_detail); Controls.Add(new Splitter { Dock = DockStyle.Left }); Controls.Add(left);
            _list.SelectedIndexChanged += (s, e) => ShowSelected();
            Fill(null);
        }

        void Mark() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }
        CommonEventData Current { get { return _list.SelectedItem as CommonEventData; } }

        void Fill(CommonEventData select)
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var c in _p.CommonEvents.OrderBy(x => x.Id)) _list.Items.Add(c);
            _list.EndUpdate();
            if (select != null) _list.SelectedItem = select;
            else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            else ShowSelected();
        }

        void ShowSelected()
        {
            foreach (Control c in _detail.Controls) c.Dispose();
            _detail.Controls.Clear();
            var ce = Current;
            if (ce == null)
            {
                _detail.Controls.Add(new Label { Text = "No common events yet. Click New to make one.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.DimGray });
                return;
            }
            var head = new Panel { Dock = DockStyle.Top, Height = 74, Padding = new Padding(8) };
            var name = new TextBox { Text = ce.Name, Left = 70, Top = 8, Width = 260 };
            var trigger = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 70, Top = 40, Width = 260 };
            trigger.Items.AddRange(CommonEventData.TriggerNames);
            trigger.SelectedIndex = Math.Max(0, Math.Min(2, ce.Trigger));
            var sw = new NumericUpDown { Left = 410, Top = 40, Width = 70, Minimum = 1, Maximum = 99999, Value = Math.Max(1, ce.SwitchId), Enabled = ce.Trigger != 0 };
            var swName = new Label { Left = 488, Top = 43, AutoSize = true, ForeColor = Color.DimGray, Text = _p.SwitchName((int)sw.Value) };
            head.Controls.Add(new Label { Text = "Name", Left = 8, Top = 11, AutoSize = true });
            head.Controls.Add(name);
            head.Controls.Add(new Label { Text = "ID " + ce.Id.ToString("000"), Left = 342, Top = 11, AutoSize = true, ForeColor = Color.DimGray });
            head.Controls.Add(new Label { Text = "Runs", Left = 8, Top = 43, AutoSize = true });
            head.Controls.Add(trigger);
            head.Controls.Add(new Label { Text = "Switch", Left = 350, Top = 43, AutoSize = true });
            head.Controls.Add(sw); head.Controls.Add(swName);
            name.TextChanged += (s, e) => { ce.Name = name.Text; Mark(); _list.Items[_list.SelectedIndex] = ce; };
            trigger.SelectedIndexChanged += (s, e) => { ce.Trigger = trigger.SelectedIndex; sw.Enabled = ce.Trigger != 0; Mark(); };
            sw.ValueChanged += (s, e) => { ce.SwitchId = (int)sw.Value; swName.Text = _p.SwitchName(ce.SwitchId); Mark(); };
            var cmds = new CommandListEditor(_p, ce.List, null);
            cmds.Changed += Mark;
            _detail.Controls.Add(cmds);
            _detail.Controls.Add(head);
        }

        void AddEvent()
        {
            int id = _p.CommonEvents.Count == 0 ? 1 : _p.CommonEvents.Max(c => c.Id) + 1;
            var ce = new CommonEventData { Id = id, Name = "New common event" };
            _p.CommonEvents.Add(ce);
            Mark(); Fill(ce);
        }

        void Duplicate()
        {
            var cur = Current; if (cur == null) return;
            var copy = (CommonEventData)Json.Json.ConvertTo(Json.Json.Parse(Json.Json.Write(cur)), typeof(CommonEventData));
            copy.Id = _p.CommonEvents.Max(c => c.Id) + 1; copy.Name = cur.Name + " (copy)";
            _p.CommonEvents.Add(copy);
            Mark(); Fill(copy);
        }

        void DeleteEvent()
        {
            var cur = Current; if (cur == null) return;
            if (MessageBox.Show(this, "Delete common event " + cur + "? Events that call it will do nothing.", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _p.CommonEvents.Remove(cur);
            Mark(); Fill(null);
        }

        public void Save()
        {
            Json.Json.ToFile(_p.DataFile("common_events.json"), _p.CommonEvents);
            Dirty = false; if (DirtyChanged != null) DirtyChanged();
            _main.RefreshTree();
        }
    }
}
