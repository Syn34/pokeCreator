using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Editor for an event's Script command: the script on the left, the script calls the game understands on the
    /// right (search, double-click to insert), and a line telling which calls in the script the game won't understand.
    /// </summary>
    public static class ScriptDialog
    {
        public static string Ask(IWin32Window owner, string title, string initial)
        {
            ProblemsPanel.LoadKnownCalls();
            var calls = ProjectCheck.KnownScriptCalls != null ? ProjectCheck.KnownScriptCalls.Where(c => (c.StartsWith("pb") || c.StartsWith("$") || c.Contains(".")) && (!c.Contains("::") || c.StartsWith("GameData"))).OrderBy(c => c.TrimStart('$'), StringComparer.OrdinalIgnoreCase).ToList() : new List<string>();
            using (var f = new Form { Text = title, Width = 900, Height = 560, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, ShowInTaskbar = false })
            {
                var mono = new Font(FontFamily.GenericMonospace, 10f);
                var text = new TextBox { Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = mono, Text = (initial ?? "").Replace("\r", "").Replace("\n", "\r\n") };
                var status = new Label { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(6), AutoEllipsis = true };
                var search = new TextBox { Dock = DockStyle.Top };
                var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = mono };
                var hint = new Label { Dock = DockStyle.Top, Height = 34, Text = "Script calls the game understands. Double-click to insert.", Padding = new Padding(4) };
                var right = new Panel { Dock = DockStyle.Right, Width = 300 };
                right.Controls.Add(list); right.Controls.Add(search); right.Controls.Add(hint);
                Action fill = () =>
                {
                    string q = search.Text.Trim();
                    list.BeginUpdate(); list.Items.Clear();
                    foreach (var c in calls.Where(c => q.Length == 0 || c.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).Take(2000)) list.Items.Add(c);
                    list.EndUpdate();
                };
                Action check = () =>
                {
                    if (ProjectCheck.KnownScriptCalls == null) { status.Text = ""; return; }
                    var unknown = ProjectCheck.UnknownCalls(text.Text.Replace("\r", ""));
                    status.ForeColor = unknown.Count == 0 ? Color.SeaGreen : Color.DarkOrange;
                    status.Text = unknown.Count == 0 ? "Every script call here is one the game understands."
                        : "Not understood by the game (skipped unless one of your scripts or plugins adds it): " + string.Join(", ", unknown);
                };
                search.TextChanged += (s, e) => fill();
                text.TextChanged += (s, e) => check();
                list.DoubleClick += (s, e) =>
                {
                    if (list.SelectedItem == null) return;
                    string ins = (string)list.SelectedItem;
                    if (ins.StartsWith("pb") && !ins.EndsWith("?") && !ins.EndsWith("!")) ins += "()";
                    text.SelectedText = ins;
                    text.Focus();
                };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
                buttons.Controls.Add(cancel); buttons.Controls.Add(ok);
                var split = new Splitter { Dock = DockStyle.Right, Width = 4 };
                f.Controls.Add(text); f.Controls.Add(split); f.Controls.Add(right); f.Controls.Add(status); f.Controls.Add(buttons);
                f.CancelButton = cancel;
                fill(); check();
                f.Shown += (s, e) => { text.Focus(); text.SelectionLength = 0; };
                var r = f.ShowDialog(owner);
                return r == DialogResult.OK ? text.Text.Replace("\r", "") : null;
            }
        }
    }
}
