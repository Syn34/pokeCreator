using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Tools
{
    /// <summary>Game > Build Game: makes a folder (and a .zip) that players can run without the Studio.</summary>
    public class BuildGameDialog : Form
    {
        readonly Project _p;
        readonly TextBox _folder = new TextBox { Left = 16, Top = 74, Width = 470 };
        readonly Label _target = new Label { Left = 16, Top = 102, Width = 560, Height = 18, ForeColor = Color.DimGray, AutoEllipsis = true };
        readonly CheckBox _exe = new CheckBox { Left = 16, Top = 130, Width = 560, Checked = true };
        readonly CheckBox _zip = new CheckBox { Left = 16, Top = 156, Width = 560, Checked = true, Text = "Also make a .zip to send to players" };
        readonly Label _problems = new Label { Left = 16, Top = 190, Width = 440, Height = 34 };
        readonly LinkLabel _showProblems = new LinkLabel { Left = 460, Top = 190, Width = 120, Text = "Show problems", Visible = false };
        readonly ProgressBar _bar = new ProgressBar { Left = 16, Top = 232, Width = 564, Height = 18 };
        readonly Label _status = new Label { Left = 16, Top = 256, Width = 564, Height = 36, AutoEllipsis = true };
        readonly Button _build = new Button { Text = "Build", Width = 100, Height = 30, Left = 266, Top = 300 };
        readonly Button _open = new Button { Text = "Open Folder", Width = 110, Height = 30, Left = 372, Top = 300, Enabled = false };
        readonly Button _close = new Button { Text = "Close", Width = 90, Height = 30, Left = 490, Top = 300, DialogResult = DialogResult.Cancel };
        string _builtFolder;
        bool _busy;

        /// <summary>The folder chosen, to remember for next time.</summary>
        public string OutputFolder { get { return _folder.Text.Trim(); } }
        /// <summary>The person clicked "Show problems".</summary>
        public bool WantsProblems { get; private set; }

        public BuildGameDialog(Project p, string lastFolder, int errors, int warnings)
        {
            _p = p;
            Text = "Build Game"; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; ClientSize = new Size(596, 344);
            Controls.Add(new Label { Text = "Makes a copy of your game that players can run without PokeCreator Studio: the game program, your maps, data, graphics, audio, fonts and scripts.", Left = 16, Top = 12, Width = 564, Height = 36 });
            Controls.Add(new Label { Text = "Put the game in this folder:", Left = 16, Top = 54, AutoSize = true });
            var browse = new Button { Text = "Browse…", Left = 492, Top = 72, Width = 88, Height = 25 };
            browse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = "Where should the game folder go?", SelectedPath = Directory.Exists(OutputFolder) ? OutputFolder : "" })
                    if (d.ShowDialog(this) == DialogResult.OK) _folder.Text = d.SelectedPath;
            };
            _folder.Text = !string.IsNullOrEmpty(lastFolder) ? lastFolder : Path.Combine(Path.GetDirectoryName(p.Root.TrimEnd(Path.DirectorySeparatorChar)) ?? p.Root, "Builds");
            string name = GameBuilder.SafeName(p.Settings.Title ?? p.Info.Name);
            _exe.Text = "Name the program after the game (" + name + ".exe instead of Game.exe)";
            _folder.TextChanged += (s, e) => UpdateTarget();
            _exe.CheckedChanged += (s, e) => UpdateTarget();
            UpdateTarget();

            if (errors > 0)
            {
                _problems.Text = "⛔ This project has " + errors + " error(s). The game may crash or show the wrong thing where they are.";
                _problems.ForeColor = Color.FromArgb(220, 70, 70);
                _showProblems.Visible = true;
            }
            else
            {
                _problems.Text = warnings > 0 ? "No errors. " + warnings + " warning(s), mostly missing graphics or sounds; the game still runs." : "✓ No problems found.";
                _problems.ForeColor = warnings > 0 ? Theme.Text : Theme.Go;
                _showProblems.Visible = warnings > 0;
            }
            _showProblems.LinkClicked += (s, e) => { WantsProblems = true; DialogResult = DialogResult.Cancel; Close(); };

            _build.Click += (s, e) => Build();
            _open.Click += (s, e) => { if (_builtFolder != null) OpenInShell(_builtFolder); };
            Controls.AddRange(new Control[] { _folder, browse, _target, _exe, _zip, _problems, _showProblems, _bar, _status, _build, _open, _close });
            AcceptButton = _build; CancelButton = _close;
            FormClosing += (s, e) => { if (_busy) e.Cancel = true; };
        }

        void UpdateTarget()
        {
            string name = GameBuilder.SafeName(_p.Settings.Title ?? _p.Info.Name);
            _target.Text = OutputFolder.Length == 0 ? "" : "→ " + Path.Combine(OutputFolder, name) + Path.DirectorySeparatorChar + (_exe.Checked ? name + ".exe" : "Game.exe");
        }

        void Build()
        {
            var b = new GameBuilder { OutputParent = OutputFolder, NameExeAfterGame = _exe.Checked, MakeZip = _zip.Checked };
            string why = b.Validate(_p);
            if (why != null) { MessageBox.Show(this, why, "Build Game", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { Directory.CreateDirectory(OutputFolder); }
            catch (Exception ex) { MessageBox.Show(this, "Can't use that folder: " + ex.Message, "Build Game"); return; }
            _busy = true; _build.Enabled = false; _open.Enabled = false; _close.Enabled = false; _status.ForeColor = Theme.Text;
            string runtime = Application.StartupPath;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Exception error = null;
                try { b.Build(_p, runtime, (pct, msg) => BeginInvoke(new Action(() => { _bar.Value = Math.Max(0, Math.Min(100, pct)); _status.Text = msg; }))); }
                catch (Exception ex) { error = ex; }
                BeginInvoke(new Action(() =>
                {
                    _busy = false; _build.Enabled = true; _close.Enabled = true;
                    if (error != null)
                    {
                        _bar.Value = 0;
                        _status.ForeColor = Color.FromArgb(220, 70, 70);
                        _status.Text = "The build stopped: " + error.Message;
                        return;
                    }
                    _builtFolder = b.GameFolder; _open.Enabled = true; _build.Text = "Build Again";
                    _status.Text = "Built " + b.FileCount + " files (" + (b.Bytes / 1048576.0).ToString("0.0") + " MB) in " + b.GameFolder + (b.ZipPath != null ? "\nTo share: send " + Path.GetFileName(b.ZipPath) + " (next to that folder)." : "");
                }));
            });
        }

        static void OpenInShell(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }
    }
}
