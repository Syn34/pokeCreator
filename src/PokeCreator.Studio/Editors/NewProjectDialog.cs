using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Projects;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// New blank project: a name and a location. The project is created in a new folder named after
    /// the project inside the chosen location, e.g. Documents\PokeCreator Projects\My Game.
    /// </summary>
    public class NewProjectDialog : Form
    {
        readonly TextBox _name = new TextBox { Dock = DockStyle.Fill, Text = "My Pokémon Game" };
        readonly TextBox _location = new TextBox { Dock = DockStyle.Fill };
        readonly Label _preview = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
        readonly Label _error = new Label { Dock = DockStyle.Fill, ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleLeft };
        readonly Button _create = new Button { Text = "Create", Width = 100, Height = 30 };

        /// <summary>The project folder that was created (location + name).</summary>
        public string ProjectFolder { get; private set; }
        public string ProjectName { get { return _name.Text.Trim(); } }
        public string ProjectLocation { get { return CleanPath(_location.Text); } }

        public static string DefaultLocation
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PokeCreator Projects"); }
        }

        public NewProjectDialog(string lastLocation)
        {
            Text = "New Project";
            Width = 640; Height = 260;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            _location.Text = string.IsNullOrEmpty(lastLocation) ? DefaultLocation : lastLocation;

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5, Padding = new Padding(12, 14, 12, 4) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            for (int i = 0; i < 4; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var browse = new Button { Text = "Browse…", Dock = DockStyle.Fill };
            browse.Click += (s, e) => Browse();

            grid.Controls.Add(new Label { Text = "Project name", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            grid.Controls.Add(_name, 1, 0);
            grid.SetColumnSpan(_name, 2);
            grid.Controls.Add(new Label { Text = "Location", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            grid.Controls.Add(_location, 1, 1);
            grid.Controls.Add(browse, 2, 1);
            grid.Controls.Add(new Label(), 0, 2);
            grid.Controls.Add(_preview, 1, 2);
            grid.SetColumnSpan(_preview, 2);
            grid.Controls.Add(new Label(), 0, 3);
            grid.Controls.Add(_error, 1, 3);
            grid.SetColumnSpan(_error, 2);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var cancel = new Button { Text = "Cancel", Width = 100, Height = 30, DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(_create);

            Controls.Add(grid);
            Controls.Add(bottom);
            AcceptButton = _create; CancelButton = cancel;

            _name.TextChanged += (s, e) => CheckInput();
            _location.TextChanged += (s, e) => CheckInput();
            _create.Click += (s, e) => Create();
            Shown += (s, e) => { _name.Focus(); _name.SelectAll(); };
            CheckInput();
        }

        /// <summary>Accepts pasted paths with quotes (Windows "Copy as path"), %VARIABLES% and ~.</summary>
        public static string CleanPath(string text)
        {
            string s = (text ?? "").Trim().Trim('"', '\'').Trim();
            if (s.Length == 0) return "";
            s = Environment.ExpandEnvironmentVariables(s);
            if (s == "~" || s.StartsWith("~/") || s.StartsWith("~\\"))
                s = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + s.Substring(1);
            if (s.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) s = Uri.UnescapeDataString(new Uri(s).LocalPath);
            return s.Length > 3 ? s.TrimEnd('\\', '/') : s;
        }

        /// <summary>Returns a problem with the name or location, or null if it's fine.</summary>
        public static string Problem(string name, string location, out string folder)
        {
            folder = null;
            if (name.Length == 0) return "Enter a project name.";
            var bad = name.Where(c => Path.GetInvalidFileNameChars().Contains(c) || "<>:\"/\\|?*".IndexOf(c) >= 0).Distinct().ToArray();
            if (bad.Length > 0) return "A project name can't contain " + string.Join(" ", bad.Select(c => c.ToString())) + ".";
            if (name.EndsWith(".") || name.EndsWith(" ")) return "A project name can't end with a dot or a space.";
            if (location.Length == 0) return "Enter or browse to a location.";
            try
            {
                if (!Path.IsPathRooted(location)) return "Enter a full location, like C:\\Users\\You\\Documents.";
                folder = Path.GetFullPath(Path.Combine(location, name));
            }
            catch (Exception) { return "That location isn't a valid folder path."; }
            if (File.Exists(location)) return "The location is a file, not a folder.";
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
                return Project.IsProjectFolder(folder) ? "There's already a project called \"" + name + "\" there. Open it with File → Open Project, or pick another name." : "A folder called \"" + name + "\" already exists there and isn't empty. Pick another name or location.";
            return null;
        }

        bool CheckInput()
        {
            string folder;
            string problem = Problem(ProjectName, ProjectLocation, out folder);
            _preview.Text = folder != null ? "The project will be created in:  " + folder : "";
            _error.Text = problem ?? (ProjectLocation.Length > 0 && !Directory.Exists(ProjectLocation) ? "The location doesn't exist yet; it will be created." : "");
            _error.ForeColor = problem != null ? Color.Firebrick : Color.DimGray;
            _create.Enabled = problem == null;
            return problem == null;
        }

        void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose where to store the project. A folder named after the project will be created inside it.", ShowNewFolderButton = true })
            {
                string start = ProjectLocation;
                while (start.Length > 0 && !Directory.Exists(start)) start = Path.GetDirectoryName(start) ?? "";
                if (start.Length > 0) d.SelectedPath = start;
                if (d.ShowDialog(this) == DialogResult.OK) _location.Text = d.SelectedPath;
            }
        }

        void Create()
        {
            if (!CheckInput()) return;
            string folder;
            Problem(ProjectName, ProjectLocation, out folder);
            try
            {
                Cursor = Cursors.WaitCursor;
                Directory.CreateDirectory(folder);
                var project = Project.Create(folder, ProjectName);
                // Start with the default tileset folders (e.g. FireRed) if any are set up next to the Studio
                try { DefaultTilesets.InstallInto(project); }
                catch (Exception ex) { MessageBox.Show(this, "The project was created, but the default tilesets couldn't be copied:\n" + ex.Message, "New Project", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                ProjectFolder = folder;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't create the project:\n" + ex.Message, "New Project", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }
    }
}
