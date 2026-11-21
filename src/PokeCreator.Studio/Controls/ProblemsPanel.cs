using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.CSharp;
using PokeCreator.Projects;

namespace PokeCreator.Studio.Controls
{
    /// <summary>
    /// The Problems list along the bottom of the window: everything in the project that would break or
    /// look wrong in the game. Double-click a problem to go to it.
    /// </summary>
    public class ProblemsPanel : Panel
    {
        readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, BorderStyle = BorderStyle.None };
        readonly Label _summary = new Label { AutoSize = true, Margin = new Padding(10, 7, 6, 0) };
        readonly CheckBox _errors = new CheckBox { Text = "Errors", Checked = true, AutoSize = true, Margin = new Padding(8, 5, 0, 0) };
        readonly CheckBox _warnings = new CheckBox { Text = "Warnings", Checked = true, AutoSize = true, Margin = new Padding(4, 5, 0, 0) };
        readonly CheckBox _notes = new CheckBox { Text = "Notes", Checked = true, AutoSize = true, Margin = new Padding(4, 5, 0, 0) };
        readonly TextBox _search = new TextBox { Width = 200, Margin = new Padding(10, 4, 0, 0) };
        List<Problem> _all = new List<Problem>();
        List<Problem> _shown = new List<Problem>();
        Project _project;
        int _runId;

        /// <summary>A problem was double-clicked.</summary>
        public event Action<Problem> Navigate;
        /// <summary>The counts changed (after a check).</summary>
        public event Action<int, int> CountsChanged;
        public event Action CloseRequested;

        public ProblemsPanel()
        {
            Height = 230; Dock = DockStyle.Bottom;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, WrapContents = false, BackColor = Theme.Bar };
            bar.Controls.Add(new Label { Text = "PROBLEMS", Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Theme.SubText, AutoSize = true, Margin = new Padding(8, 8, 0, 0) });
            bar.Controls.Add(_summary);
            bar.Controls.Add(_errors); bar.Controls.Add(_warnings); bar.Controls.Add(_notes);
            bar.Controls.Add(new Label { Text = "Search:", AutoSize = true, Margin = new Padding(14, 7, 0, 0) });
            bar.Controls.Add(_search);
            var again = new Button { Text = "Check Again", AutoSize = true, Height = 24, Margin = new Padding(10, 3, 0, 0) };
            again.Click += (s, e) => Run(_project);
            bar.Controls.Add(again);
            var close = new Button { Text = "✕", Width = 28, Height = 24, Margin = new Padding(6, 3, 0, 0) };
            close.Click += (s, e) => { if (CloseRequested != null) CloseRequested(); };
            bar.Controls.Add(close);

            _list.Columns.Add("", 26);
            _list.Columns.Add("Where", 380);
            _list.Columns.Add("Problem", 800);
            _list.SmallImageList = MakeIcons();
            _list.VirtualMode = true;
            _list.RetrieveVirtualItem += (s, e) =>
            {
                var p = _shown[e.ItemIndex];
                var it = new ListViewItem("", (int)p.Severity);
                it.SubItems.Add(p.Where); it.SubItems.Add(p.Message);
                it.ToolTipText = p.Message;
                e.Item = it;
            };
            _list.ShowItemToolTips = true;
            _list.DoubleClick += (s, e) => Go();
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Go(); e.Handled = true; } };
            _list.Resize += (s, e) => _list.Columns[2].Width = Math.Max(200, _list.ClientSize.Width - _list.Columns[0].Width - _list.Columns[1].Width - 4);
            _errors.CheckedChanged += (s, e) => Filter();
            _warnings.CheckedChanged += (s, e) => Filter();
            _notes.CheckedChanged += (s, e) => Filter();
            _search.TextChanged += (s, e) => Filter();
            Controls.Add(_list);
            Controls.Add(bar);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
        }

        public int ErrorCount { get { return _all.Count(p => p.Severity == ProblemSeverity.Error); } }

        void Go()
        {
            if (_list.SelectedIndices.Count == 0) return;
            int i = _list.SelectedIndices[0];
            if (i >= 0 && i < _shown.Count && Navigate != null) Navigate(_shown[i]);
        }

        /// <summary>Checks the project's data now, and compiles its scripts in the background.</summary>
        public void Run(Project p)
        {
            _project = p;
            if (p == null) { _all = new List<Problem>(); Filter(); return; }
            Cursor = Cursors.WaitCursor;
            LoadKnownCalls();
            try { _all = ProjectCheck.Run(p); }
            catch (Exception ex) { _all = new List<Problem> { new Problem { Severity = ProblemSeverity.Error, Where = "Checker", Message = "The check stopped early: " + ex.Message } }; }
            finally { Cursor = Cursors.Default; }
            Filter();
            int run = ++_runId;
            string scripts = p.ScriptsDir;
            PluginProject = p;
            foreach (var msg in PokeCreator.Projects.Plugins.Problems(p)) _all.Add(new Problem { Severity = ProblemSeverity.Error, Where = "Plugins", Message = msg, Target = "sys:plugins" });
            _form = FindForm();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var found = CompileScripts(scripts);
                if (found.Count == 0 || IsDisposed) return;
                try
                {
                    // The panel may never have been shown (no handle yet), so post through the window
                    Control host = IsHandleCreated ? this : (Control)FindFormSafe();
                    if (host == null || !host.IsHandleCreated) return;
                    host.BeginInvoke(new Action(() =>
                    {
                        if (run != _runId) return;
                        _all.InsertRange(0, found);
                        Filter();
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        /// <summary>Asks Game.exe which script calls it understands, once, so event scripts can be checked.</summary>
        static void LoadKnownCalls()
        {
            if (ProjectCheck.KnownScriptCalls != null) return;
            try
            {
                string game = Path.Combine(Application.StartupPath, "Game.exe");
                if (!File.Exists(game)) return;
                var asm = System.Reflection.Assembly.LoadFrom(game);
                var t = asm.GetType("PokeCreator.Runtime.Script.EssentialsCompat");
                var m = t != null ? t.GetMethod("KnownCalls") : null;
                var list = m != null ? m.Invoke(null, null) as List<string> : null;
                if (list != null) ProjectCheck.KnownScriptCalls = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
            catch { }
        }

        Form _form;
        Form FindFormSafe() { return _form; }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); _form = FindForm(); }

        /// <summary>Compiles Scripts/*.cs the same way the game does, so script errors show up before you playtest.</summary>
        /// <summary>The project whose switched-on plugins' scripts are checked with its own.</summary>
        public static PokeCreator.Projects.Project PluginProject;

        public static List<Problem> CompileScripts(string dir)
        {
            var result = new List<Problem>();
            try
            {
                var fileList = new List<string>();
                if (Directory.Exists(dir)) fileList.AddRange(Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories));
                if (PluginProject != null) fileList.InsertRange(0, PokeCreator.Projects.Plugins.ScriptFiles(PluginProject));
                var files = fileList.ToArray();
                if (files.Length == 0) return result;
                string game = Path.Combine(Application.StartupPath, "Game.exe");
                if (!File.Exists(game)) return result;
                var opts = new CompilerParameters { GenerateInMemory = false, GenerateExecutable = false, OutputAssembly = Path.Combine(Path.GetTempPath(), "pokecreator_check_" + Guid.NewGuid().ToString("N") + ".dll") };
                opts.ReferencedAssemblies.Add("System.dll");
                opts.ReferencedAssemblies.Add("System.Core.dll");
                opts.ReferencedAssemblies.Add(game);
                opts.ReferencedAssemblies.Add(typeof(Project).Assembly.Location);
                CompilerResults cr;
                using (var provider = new CSharpCodeProvider()) cr = provider.CompileAssemblyFromFile(opts, files);
                foreach (CompilerError e in cr.Errors)
                {
                    if (e.IsWarning) continue;
                    string rel = string.IsNullOrEmpty(e.FileName) ? "Scripts" : "Scripts/" + Path.GetFileName(e.FileName);
                    result.Add(new Problem { Severity = ProblemSeverity.Error, Where = rel + ", line " + e.Line, Message = e.ErrorText + " (the game won't start until this is fixed)", Target = "file:" + e.FileName + "|" + e.Line });
                }
                try { File.Delete(opts.OutputAssembly); } catch { }
            }
            catch (Exception ex) { result.Add(new Problem { Severity = ProblemSeverity.Warning, Where = "Scripts", Message = "Could not check the scripts: " + ex.Message }); }
            return result;
        }

        void Filter()
        {
            string q = _search.Text.Trim();
            _shown = _all.Where(p =>
                (p.Severity == ProblemSeverity.Error ? _errors.Checked : p.Severity == ProblemSeverity.Warning ? _warnings.Checked : _notes.Checked) &&
                (q.Length == 0 || p.Where.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || p.Message.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            _list.VirtualListSize = _shown.Count;
            _list.Invalidate();
            int errors = ErrorCount, warnings = _all.Count(p => p.Severity == ProblemSeverity.Warning), notes = _all.Count - errors - warnings;
            _summary.Text = _all.Count == 0 ? "No problems found ✓" : errors + " error(s), " + warnings + " warning(s), " + notes + " note(s)";
            _summary.ForeColor = errors > 0 ? Color.FromArgb(220, 70, 70) : Theme.Text;
            if (CountsChanged != null) CountsChanged(errors, warnings);
        }

        static ImageList MakeIcons()
        {
            var il = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            for (int k = 0; k < 3; k++)
            {
                var b = new Bitmap(16, 16);
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var f = new Font("Segoe UI", 8f, FontStyle.Bold))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        if (k == 0) { using (var br = new SolidBrush(Color.FromArgb(220, 60, 60))) g.FillEllipse(br, 1, 1, 14, 14); g.DrawString("!", f, Brushes.White, new RectangleF(0, 1, 16, 15), sf); }
                        else if (k == 1) { using (var br = new SolidBrush(Color.FromArgb(235, 175, 40))) g.FillPolygon(br, new[] { new PointF(8, 1), new PointF(15.5f, 15), new PointF(0.5f, 15) }); g.DrawString("!", f, Brushes.Black, new RectangleF(0, 3, 16, 13), sf); }
                        else { using (var br = new SolidBrush(Color.FromArgb(70, 140, 230))) g.FillEllipse(br, 1, 1, 14, 14); g.DrawString("i", f, Brushes.White, new RectangleF(0, 1, 16, 15), sf); }
                    }
                }
                il.Images.Add(b);
            }
            return il;
        }
    }
}
