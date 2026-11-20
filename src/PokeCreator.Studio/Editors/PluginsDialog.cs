using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Projects;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Game → Plugins…: plugins installed in the project (Plugins folder). Tick to switch on or off, install from a .zip or
    /// folder, export one as a .zip to share, or make a new plugin from some of the project's scripts.
    /// </summary>
    public class PluginsDialog : Form
    {
        readonly Project _p;
        readonly CheckedListBox _list = new CheckedListBox { Width = 380, Height = 250, IntegralHeight = false, CheckOnClick = true };
        readonly Label _info = new Label { Width = 380, Height = 70, AutoSize = false };
        List<PluginInfo> _plugins = new List<PluginInfo>();
        bool _filling;

        public PluginsDialog(Project p)
        {
            _p = p;
            Text = "Plugins";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 380);
            Controls.Add(new Label { Text = "Ticked plugins are loaded by the game: their scripts, graphics, sounds and database records.", AutoSize = false, Width = 530, Height = 34, Location = new Point(12, 10) });
            _list.Location = new Point(12, 48);
            _info.Location = new Point(12, 304);
            Controls.Add(_list); Controls.Add(_info);
            _list.SelectedIndexChanged += (s, e) => ShowInfo();
            _list.ItemCheck += (s, e) =>
            {
                if (_filling || e.Index < 0 || e.Index >= _plugins.Count) return;
                _plugins[e.Index].Disabled = e.NewValue != CheckState.Checked;
                Plugins.Save(_plugins[e.Index]);
            };
            int y = 48;
            Func<string, EventHandler, Button> btn = (t, h) => { var b = new Button { Text = t, Width = 145, Height = 28, Location = new Point(404, y) }; b.Click += h; Controls.Add(b); y += 34; return b; };
            btn("Install from .zip…", (s, e) => InstallZip());
            btn("Install from folder…", (s, e) => InstallFolder());
            btn("Export as .zip…", (s, e) => ExportZip());
            btn("New from scripts…", (s, e) => NewFromScripts());
            btn("Remove", (s, e) => Remove());
            btn("Open Folder", (s, e) => { Directory.CreateDirectory(Plugins.Folder(_p)); MainForm.OpenInShell(Plugins.Folder(_p)); });
            var close = new Button { Text = "Close", Width = 145, Height = 28, Location = new Point(404, 340), DialogResult = DialogResult.OK };
            Controls.Add(close); AcceptButton = close; CancelButton = close;
            Fill();
        }

        PluginInfo Selected { get { int i = _list.SelectedIndex; return i >= 0 && i < _plugins.Count ? _plugins[i] : null; } }

        void Fill()
        {
            _filling = true;
            _plugins = Plugins.All(_p);
            _list.Items.Clear();
            foreach (var x in _plugins) _list.Items.Add(x.Name + " " + x.Version, !x.Disabled);
            _filling = false;
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            ShowInfo();
        }

        void ShowInfo()
        {
            var x = Selected;
            if (x == null) { _info.Text = _plugins.Count == 0 ? "No plugins yet. Install one, or make one from your scripts to share it." : ""; return; }
            int scripts = Directory.Exists(Path.Combine(x.Folder, "Scripts")) ? Directory.GetFiles(Path.Combine(x.Folder, "Scripts"), "*.cs", SearchOption.AllDirectories).Length : 0;
            _info.Text = (string.IsNullOrEmpty(x.Author) ? "" : "By " + x.Author + ". ") + (x.Description ?? "") + "\n" + scripts + " script file(s)"
                + (x.Requires != null && x.Requires.Count > 0 ? "; needs " + string.Join(", ", x.Requires) : "");
        }

        string Target(string name)
        {
            string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
            return Path.Combine(Plugins.Folder(_p), safe.Length > 0 ? safe : "Plugin");
        }

        static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        void InstallZip()
        {
            using (var dlg = new OpenFileDialog { Filter = "Plugin (*.zip)|*.zip", Title = "Install plugin" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "pokecreator_plugin_" + Guid.NewGuid().ToString("N"));
                    ZipFile.ExtractToDirectory(dlg.FileName, tmp);
                    // The zip may hold the plugin folder itself or its contents
                    string root = File.Exists(Path.Combine(tmp, "plugin.json")) ? tmp : Directory.GetDirectories(tmp).FirstOrDefault(d => File.Exists(Path.Combine(d, "plugin.json"))) ?? tmp;
                    InstallFrom(root, Path.GetFileNameWithoutExtension(dlg.FileName));
                    try { Directory.Delete(tmp, true); } catch { }
                }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't install the plugin:\n" + ex.Message, "Plugins", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void InstallFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose the plugin's folder (with plugin.json)" })
                if (dlg.ShowDialog(this) == DialogResult.OK) InstallFrom(dlg.SelectedPath, Path.GetFileName(dlg.SelectedPath));
        }

        void InstallFrom(string root, string fallbackName)
        {
            PluginInfo info = null;
            try { var meta = Path.Combine(root, "plugin.json"); if (File.Exists(meta)) info = PokeCreator.Json.Json.FromFile<PluginInfo>(meta); } catch { }
            string name = info != null && !string.IsNullOrEmpty(info.Name) ? info.Name : fallbackName;
            string to = Target(name);
            if (Directory.Exists(to) && MessageBox.Show(this, "\"" + name + "\" is already installed. Replace it?", "Plugins", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            CopyDir(root, to);
            if (!File.Exists(Path.Combine(to, "plugin.json"))) Plugins.Save(new PluginInfo { Name = name, Folder = to });
            Fill();
        }

        void ExportZip()
        {
            var x = Selected;
            if (x == null) return;
            using (var dlg = new SaveFileDialog { Filter = "Plugin (*.zip)|*.zip", FileName = x.Name + " " + x.Version + ".zip" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                    ZipFile.CreateFromDirectory(x.Folder, dlg.FileName, CompressionLevel.Optimal, true);
                    MessageBox.Show(this, "Saved " + Path.GetFileName(dlg.FileName) + ". Other PokeCreator projects can install it with Install from .zip.", "Plugins");
                }
                catch (Exception ex) { MessageBox.Show(this, "Couldn't export: " + ex.Message, "Plugins"); }
            }
        }

        void NewFromScripts()
        {
            var scripts = Directory.Exists(_p.ScriptsDir) ? Directory.GetFiles(_p.ScriptsDir, "*.cs", SearchOption.AllDirectories) : new string[0];
            if (scripts.Length == 0) { MessageBox.Show(this, "The project's Scripts folder has no .cs files yet.", "Plugins"); return; }
            using (var f = new Form { Text = "New Plugin", ClientSize = new Size(420, 400), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false })
            {
                var name = new TextBox { Location = new Point(110, 12), Width = 290 };
                var author = new TextBox { Location = new Point(110, 42), Width = 290 };
                var desc = new TextBox { Location = new Point(110, 72), Width = 290, Height = 50, Multiline = true };
                var files = new CheckedListBox { Location = new Point(12, 152), Width = 390, Height = 190, CheckOnClick = true };
                var move = new CheckBox { Text = "Move the files (not copy), so the plugin's version is the one the game uses", Location = new Point(12, 346), Width = 400 };
                foreach (var s in scripts) files.Items.Add(s.Substring(_p.ScriptsDir.Length).TrimStart('\\', '/'), true);
                f.Controls.Add(new Label { Text = "Name:", Location = new Point(12, 15), AutoSize = true }); f.Controls.Add(name);
                f.Controls.Add(new Label { Text = "Author:", Location = new Point(12, 45), AutoSize = true }); f.Controls.Add(author);
                f.Controls.Add(new Label { Text = "Description:", Location = new Point(12, 75), AutoSize = true }); f.Controls.Add(desc);
                f.Controls.Add(new Label { Text = "Scripts to include:", Location = new Point(12, 132), AutoSize = true }); f.Controls.Add(files); f.Controls.Add(move);
                var ok = new Button { Text = "Create", DialogResult = DialogResult.OK, Location = new Point(240, 368), Width = 80 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(325, 368), Width = 80 };
                f.Controls.Add(ok); f.Controls.Add(cancel); f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(name.Text)) return;
                string to = Target(name.Text.Trim());
                Directory.CreateDirectory(Path.Combine(to, "Scripts"));
                foreach (int i in files.CheckedIndices)
                {
                    string rel = (string)files.Items[i];
                    string src = Path.Combine(_p.ScriptsDir, rel), dst = Path.Combine(to, "Scripts", rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst, true);
                    if (move.Checked) File.Delete(src);
                }
                Plugins.Save(new PluginInfo { Name = name.Text.Trim(), Author = author.Text.Trim(), Description = desc.Text.Trim(), Folder = to, Disabled = !move.Checked });
                if (!move.Checked) MessageBox.Show(this, "The plugin was made switched off, because the same scripts are still in your Scripts folder (both would be loaded). Export it as a .zip to share it.", "Plugins");
                Fill();
            }
        }

        void Remove()
        {
            var x = Selected;
            if (x == null) return;
            if (MessageBox.Show(this, "Remove the plugin \"" + x.Name + "\" from this project? Its folder is deleted.", "Plugins", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { Directory.Delete(x.Folder, true); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Plugins"); }
            Fill();
        }
    }
}
