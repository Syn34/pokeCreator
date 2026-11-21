using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Maps;
using PokeCreator.Projects;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Edit → Find in Events (Ctrl+Shift+F): searches every map event and common event — names, messages, scripts and
    /// every other command as the event editor shows it — and opens the one picked.
    /// </summary>
    public class FindDialog : Form
    {
        readonly Project _p;
        readonly Action<string> _go;
        readonly TextBox _text = new TextBox { Dock = DockStyle.Fill };
        readonly CheckBox _case = new CheckBox { Text = "Match case", AutoSize = true, Dock = DockStyle.Right };
        readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
        readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };

        public FindDialog(Project p, Action<string> go)
        {
            _p = p; _go = go;
            Text = "Find in Events";
            Size = new Size(820, 520);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            var top = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 6, 8, 4) };
            var find = new Button { Text = "Find", Dock = DockStyle.Right, Width = 80 };
            top.Controls.Add(_text); top.Controls.Add(_case); top.Controls.Add(find);
            _list.Columns.Add("Where", 260); _list.Columns.Add("Page", 50); _list.Columns.Add("Line", 480);
            Controls.Add(_list); Controls.Add(top); Controls.Add(_status);
            AcceptButton = find;
            find.Click += (s, e) => Search();
            _list.DoubleClick += (s, e) => Open();
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) Open(); };
            Shown += (s, e) => { ActiveControl = _text; _text.Focus(); };
            _status.Text = "Type text to look for (a message, a switch name, a script call, an event name...) and press Enter.";
        }

        void Open()
        {
            if (_list.SelectedItems.Count == 0) return;
            var target = _list.SelectedItems[0].Tag as string;
            if (target != null) _go(target);
        }

        void Search()
        {
            string q = _text.Text;
            _list.Items.Clear();
            if (string.IsNullOrEmpty(q)) return;
            var cmp = _case.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            Func<string, bool> hit = s => s != null && s.IndexOf(q, cmp) >= 0;
            int found = 0;
            _list.BeginUpdate();
            try
            {
                foreach (var mi in _p.MapInfos.OrderBy(m => m.Id))
                {
                    MapData map;
                    try { map = _p.LoadMap(mi.Id); } catch { continue; }
                    if (map == null) continue;
                    foreach (var ev in map.Events)
                    {
                        string where = "Map " + mi.Id.ToString("000") + " " + mi.Name + " › " + ev.Id.ToString("000") + ": " + ev.Name;
                        if (hit(ev.Name)) { Add(where, "", "(event name) " + ev.Name, "map:" + mi.Id + ":event:" + ev.Id); found++; }
                        for (int pg = 0; pg < ev.Pages.Count; pg++)
                        {
                            var page = ev.Pages[pg];
                            if (page.Graphic != null && hit(page.Graphic.CharacterName)) { Add(where, (pg + 1).ToString(), "(graphic) " + page.Graphic.CharacterName, "map:" + mi.Id + ":event:" + ev.Id + ":page:" + (pg + 1)); found++; }
                            foreach (var c in page.List)
                            {
                                string line = Line(c);
                                if (!hit(line)) continue;
                                Add(where, (pg + 1).ToString(), line, "map:" + mi.Id + ":event:" + ev.Id + ":page:" + (pg + 1));
                                if (++found >= 2000) break;
                            }
                        }
                    }
                    if (found >= 2000) break;
                }
                foreach (var ce in _p.CommonEvents)
                {
                    string where = "Common event " + ce.Id.ToString("000") + ": " + ce.Name;
                    if (hit(ce.Name)) { Add(where, "", "(name) " + ce.Name, "commonevent:" + ce.Id); found++; }
                    foreach (var c in ce.List)
                    {
                        string line = Line(c);
                        if (hit(line)) { Add(where, "", line, "commonevent:" + ce.Id); found++; }
                    }
                }
            }
            finally { _list.EndUpdate(); }
            _status.Text = found == 0 ? "Nothing found." : found + " found" + (found >= 2000 ? " (showing the first 2000)" : "") + ". Double-click one to open it.";
        }

        string Line(EventCommand c)
        {
            string s;
            try { s = CommandText.Describe(c, _p); } catch { s = null; }
            if (string.IsNullOrEmpty(s)) s = string.Join(" ", c.Params.Select(x => x == null ? "" : x.ToString()));
            return s.Trim();
        }

        void Add(string where, string page, string line, string target)
        {
            var it = new ListViewItem(new[] { where, page, line }) { Tag = target };
            _list.Items.Add(it);
        }
    }
}
