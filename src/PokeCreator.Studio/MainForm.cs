using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Json;
using PokeCreator.Maps;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;
using PokeCreator.Studio.Editors;

namespace PokeCreator.Studio
{
    public class MainForm : Form
    {
        public Project Project { get; private set; }
        readonly TreeView _tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, Font = new Font("Segoe UI", 9.5f), BorderStyle = BorderStyle.None, BackColor = Theme.Sidebar, ItemHeight = 22 };
        readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
        readonly StatusStrip _status = new StatusStrip();
        readonly ToolStripStatusLabel _statusText = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly Panel _welcome = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly Settings _settings = Settings.Load();

        public MainForm(string openPath)
        {
            Text = "PokeCreator Studio";
            Width = 1400; Height = 900; StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            KeyPreview = true;

            MainMenuStrip = BuildMenu();
            var toolbar = BuildToolbar();
            _status.Items.Add(_statusText);

            var left = new Panel { Dock = DockStyle.Left, Width = 270, BackColor = Theme.Sidebar };
            var treeLabel = new Label { Text = "  PROJECT", Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
            left.Controls.Add(_tree); left.Controls.Add(treeLabel);

            var center = new Panel { Dock = DockStyle.Fill };
            center.Controls.Add(_tabs);
            center.Controls.Add(_welcome);

            Controls.Add(center);
            Controls.Add(new Splitter { Dock = DockStyle.Left, BackColor = Color.Gainsboro, Width = 3 });
            Controls.Add(left);
            Controls.Add(toolbar);
            Controls.Add(MainMenuStrip);
            Controls.Add(_status);

            _tree.NodeMouseDoubleClick += (s, e) => OpenNode(e.Node);
            _tree.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && _tree.SelectedNode != null) OpenNode(_tree.SelectedNode); };
            _tree.NodeMouseClick += (s, e) => { if (e.Button == MouseButtons.Right) { _tree.SelectedNode = e.Node; ShowTreeMenu(e.Node, e.Location); } };
            _tabs.MouseUp += (s, e) => { if (e.Button == MouseButtons.Middle) CloseTabAt(e.Location); };
            FormClosing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; else _settings.Save(); };

            BuildWelcome();
            _tabs.Visible = false;
            SetStatus("Welcome to PokeCreator Studio");

            if (openPath != null) OpenProject(openPath);
        }

        void SetStatus(string s) { _statusText.Text = s; }

        // ------------------------------------------------------------------ menus
        MenuStrip BuildMenu()
        {
            var m = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add("&New Project…", null, (s, e) => NewProject());
            file.DropDownItems.Add(new ToolStripMenuItem("&Open Project…", null, (s, e) => BrowseOpen()) { ShortcutKeys = Keys.Control | Keys.O });
            file.DropDownItems.Add("&Import Pokémon Essentials Project…", null, (s, e) => ImportEssentials());
            var recent = new ToolStripMenuItem("Recent Projects");
            file.DropDownOpening += (s, e) =>
            {
                recent.DropDownItems.Clear();
                foreach (var r in _settings.Recent.Where(Directory.Exists)) { string path = r; recent.DropDownItems.Add(r, null, (a, b) => OpenProject(path)); }
                recent.Enabled = recent.DropDownItems.Count > 0;
            };
            file.DropDownItems.Add(recent);
            file.DropDownItems.Add(new ToolStripSeparator());
            var save = new ToolStripMenuItem("&Save All", null, (s, e) => SaveAll()) { ShortcutKeys = Keys.Control | Keys.S };
            file.DropDownItems.Add(save);
            file.DropDownItems.Add(new ToolStripMenuItem("Close Tab", null, (s, e) => CloseTab(_tabs.SelectedTab)) { ShortcutKeys = Keys.Control | Keys.W });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("Open Project Folder in Explorer", null, (s, e) => { if (Project != null) OpenInShell(Project.Root); });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("E&xit", null, (s, e) => Close());

            var game = new ToolStripMenuItem("&Game");
            game.DropDownItems.Add(new ToolStripMenuItem("&Playtest", null, (s, e) => Playtest(null)) { ShortcutKeys = Keys.F5 });
            game.DropDownItems.Add(new ToolStripMenuItem("Playtest from Current Map", null, (s, e) => PlaytestHere()) { ShortcutKeys = Keys.Shift | Keys.F5 });
            game.DropDownItems.Add(new ToolStripSeparator());
            game.DropDownItems.Add("Game Settings", null, (s, e) => OpenSettings());
            game.DropDownItems.Add("Player Characters", null, (s, e) => OpenPlayers());
            game.DropDownItems.Add("Switches && Variables", null, (s, e) => OpenDocument("sys:switches", () => new SwitchesDocument(Project)));

            var db = new ToolStripMenuItem("&Database");
            db.DropDownOpening += (s, e) =>
            {
                db.DropDownItems.Clear();
                if (Project == null) return;
                foreach (var t in Project.Tables) { var table = t; db.DropDownItems.Add(t.Label, null, (a, b) => OpenTable(table)); }
            };
            db.DropDownItems.Add("(open a project)");

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add("Getting Started", null, (s, e) => ShowHelp());
            help.DropDownItems.Add("About PokeCreator", null, (s, e) => MessageBox.Show(this, "PokeCreator Studio " + Project.EngineVersion + "\nA standalone engine and editor for Pokémon fan games.\n\nhttps://github.com/Syn34/pokeCreator", "About"));

            m.Items.AddRange(new ToolStripItem[] { file, game, db, help });
            return m;
        }

        ToolStrip BuildToolbar()
        {
            var t = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2), ImageScalingSize = new Size(16, 16) };
            t.Items.Add(new ToolStripButton("New", null, (s, e) => NewProject()) { ToolTipText = "New project" });
            t.Items.Add(new ToolStripButton("Open", null, (s, e) => BrowseOpen()) { ToolTipText = "Open project (Ctrl+O)" });
            t.Items.Add(new ToolStripButton("Import Essentials", null, (s, e) => ImportEssentials()) { ToolTipText = "Convert a Pokémon Essentials project" });
            t.Items.Add(new ToolStripSeparator());
            t.Items.Add(new ToolStripButton("Save All", null, (s, e) => SaveAll()) { ToolTipText = "Save everything (Ctrl+S)" });
            t.Items.Add(new ToolStripSeparator());
            t.Items.Add(new ToolStripButton("▶ Playtest", null, (s, e) => Playtest(null)) { ToolTipText = "Run the game (F5)", Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.DarkGreen });
            t.Items.Add(new ToolStripButton("▶ From This Map", null, (s, e) => PlaytestHere()) { ToolTipText = "Start the game on the map you're editing (Shift+F5)" });
            t.Items.Add(new ToolStripSeparator());
            t.Items.Add(new ToolStripButton("Settings", null, (s, e) => OpenSettings()));
            t.Items.Add(new ToolStripButton("Project Folder", null, (s, e) => { if (Project != null) OpenInShell(Project.Root); }));
            return t;
        }

        void BuildWelcome()
        {
            var title = new Label { Text = "PokeCreator Studio", Font = new Font("Segoe UI", 26f, FontStyle.Bold), ForeColor = Theme.Accent, AutoSize = true, Location = new Point(60, 60) };
            var sub = new Label { Text = "Make your own Pokémon game. No RPG Maker required.", Font = new Font("Segoe UI", 12f), AutoSize = true, Location = new Point(64, 110), ForeColor = Color.DimGray };
            var b1 = BigButton("Import a Pokémon Essentials project", "Brings in your Pokémon, moves, items, trainers, maps and graphics.", 160, (s, e) => ImportEssentials());
            var b2 = BigButton("Open a PokeCreator project", "Open a folder that contains project.json.", 240, (s, e) => BrowseOpen());
            var b3 = BigButton("Create a blank project", "Start from scratch with an empty map.", 320, (s, e) => NewProject());
            _welcome.Controls.AddRange(new Control[] { title, sub, b1, b2, b3 });
            int y = 420;
            var recentLabel = new Label { Text = "Recent projects", Font = Theme.HeaderFont, AutoSize = true, Location = new Point(64, y) };
            _welcome.Controls.Add(recentLabel);
            foreach (var r in _settings.Recent.Where(Directory.Exists).Take(6))
            {
                y += 26; string path = r;
                var link = new LinkLabel { Text = r, AutoSize = true, Location = new Point(70, y) };
                link.LinkClicked += (s, e) => OpenProject(path);
                _welcome.Controls.Add(link);
            }
        }

        Control BigButton(string title, string desc, int y, EventHandler click)
        {
            var b = new Button { Text = title + "\n" + desc, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(64, y), Size = new Size(520, 64), Font = new Font("Segoe UI", 10f), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(246, 246, 250), Padding = new Padding(12, 0, 0, 0) };
            b.FlatAppearance.BorderColor = Color.Gainsboro;
            b.Click += click;
            return b;
        }

        // ------------------------------------------------------------------ project lifecycle
        void BrowseOpen()
        {
            using (var d = new OpenFileDialog { Filter = "PokeCreator project (project.json)|project.json", Title = "Open PokeCreator project" })
                if (d.ShowDialog(this) == DialogResult.OK) OpenProject(Path.GetDirectoryName(d.FileName));
        }

        public void OpenProject(string dir)
        {
            if (!ConfirmDiscard()) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                var p = Project.Load(dir);
                Project = p;
                FormBuilder.Project = p;
                foreach (TabPage tp in _tabs.TabPages.Cast<TabPage>().ToList()) { _tabs.TabPages.Remove(tp); tp.Dispose(); }
                RebuildTree();
                _welcome.Visible = false; _tabs.Visible = true;
                Text = "PokeCreator Studio - " + p.Info.Name;
                _settings.AddRecent(p.Root); _settings.Save();
                SetStatus("Opened " + p.Root + "  •  " + p.Species.Count + " Pokémon, " + p.Moves.Count + " moves, " + p.Items.Count + " items, " + p.MapInfos.Count + " maps");
                var first = p.MapInfos.FirstOrDefault(mi => mi.Id == p.Settings.StartMap) ?? p.MapInfos.FirstOrDefault();
                if (first != null) OpenMap(first.Id);
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not open the project:\n\n" + ex.Message, "Open project", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { Cursor = Cursors.Default; }
        }

        void NewProject()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose an empty folder for the new project" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string name = Path.GetFileName(d.SelectedPath.TrimEnd(Path.DirectorySeparatorChar));
                if (Directory.GetFileSystemEntries(d.SelectedPath).Length > 0 && MessageBox.Show(this, "That folder isn't empty. Create the project inside it anyway?", "New project", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                Project.Create(d.SelectedPath, name);
                OpenProject(d.SelectedPath);
            }
        }

        void ImportEssentials()
        {
            using (var dlg = new ImportDialog())
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultFolder != null) OpenProject(dlg.ResultFolder);
        }

        bool ConfirmDiscard()
        {
            var dirty = Documents().Where(d => d.Dirty).ToList();
            if (dirty.Count == 0) return true;
            var r = MessageBox.Show(this, "Save changes to:\n  " + string.Join("\n  ", dirty.Select(d => d.Title)) + " ?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) SaveAll();
            return true;
        }

        IEnumerable<IDocument> Documents()
        {
            foreach (TabPage tp in _tabs.TabPages) { var d = tp.Tag as IDocument; if (d != null) yield return d; }
        }

        public void SaveAll()
        {
            if (Project == null) return;
            try
            {
                foreach (var d in Documents().ToList()) if (d.Dirty) d.Save();
                Json.Json.ToFile(Project.DataFile("mapinfos.json"), Project.MapInfos);
                Json.Json.ToFile(Project.DataFile("tilesets.json"), Project.Tilesets);
                Json.Json.ToFile(Project.DataFile("map_connections.json"), Project.Connections);
                SetStatus("Saved at " + DateTime.Now.ToShortTimeString());
                RefreshTabTitles();
            }
            catch (Exception ex) { MessageBox.Show(this, "Save failed:\n" + ex.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ------------------------------------------------------------------ project tree
        void RebuildTree()
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            var p = Project;

            var settings = _tree.Nodes.Add("⚙  Game Settings");
            settings.Tag = "sys:settings";
            settings.Nodes.Add("Game Settings").Tag = "sys:settings";
            settings.Nodes.Add("Player Characters").Tag = "sys:players";
            settings.Nodes.Add("Switches & Variables").Tag = "sys:switches";
            settings.Nodes.Add("Tilesets").Tag = "sys:tilesets";
            settings.Nodes.Add("Map Connections").Tag = "sys:connections";

            var db = _tree.Nodes.Add("📖  Database");
            foreach (var t in p.Tables) db.Nodes.Add(t.Label + "  (" + t.Items.Count + ")").Tag = t;
            db.Expand();

            var maps = _tree.Nodes.Add("🗺  Maps");
            maps.Tag = "maps";
            AddMapNodes(maps.Nodes, 0, new HashSet<int>());
            maps.Expand();

            var scripts = _tree.Nodes.Add("📜  Scripts");
            scripts.Tag = "dir:" + p.ScriptsDir;
            if (Directory.Exists(p.ScriptsDir))
                foreach (var f in Directory.GetFiles(p.ScriptsDir, "*.cs").OrderBy(x => x)) scripts.Nodes.Add(Path.GetFileName(f)).Tag = "file:" + f;

            var assets = _tree.Nodes.Add("🖼  Graphics & Audio");
            foreach (var top in new[] { "Graphics", "Audio", "Fonts" })
            {
                string dir = Path.Combine(p.Root, top);
                if (!Directory.Exists(dir)) continue;
                var n = assets.Nodes.Add(top); n.Tag = "assets:" + dir;
                foreach (var sub in Directory.GetDirectories(dir).OrderBy(x => x)) AddAssetDir(n.Nodes, sub, 0);
            }

            var files = _tree.Nodes.Add("📁  All Project Files");
            files.Tag = "dir:" + p.Root;
            files.Nodes.Add("(loading…)");
            _tree.BeforeExpand -= LazyFiles; _tree.BeforeExpand += LazyFiles;
            _tree.EndUpdate();
        }

        void AddAssetDir(TreeNodeCollection parent, string dir, int depth)
        {
            var n = parent.Add(Path.GetFileName(dir)); n.Tag = "assets:" + dir;
            if (depth < 2) foreach (var sub in Directory.GetDirectories(dir).OrderBy(x => x)) AddAssetDir(n.Nodes, sub, depth + 1);
        }

        void LazyFiles(object s, TreeViewCancelEventArgs e)
        {
            var tag = e.Node.Tag as string;
            if (tag == null || !tag.StartsWith("dir:") || e.Node.Nodes.Count != 1 || e.Node.Nodes[0].Text != "(loading…)") return;
            e.Node.Nodes.Clear();
            string dir = tag.Substring(4);
            foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x))
            {
                var n = e.Node.Nodes.Add("📁 " + Path.GetFileName(d)); n.Tag = "dir:" + d; n.Nodes.Add("(loading…)");
            }
            foreach (var f in Directory.GetFiles(dir).OrderBy(x => x)) e.Node.Nodes.Add(Path.GetFileName(f)).Tag = "file:" + f;
        }

        void AddMapNodes(TreeNodeCollection parent, int parentId, HashSet<int> seen)
        {
            foreach (var mi in Project.MapInfos.Where(m => m.ParentId == parentId).OrderBy(m => m.Order))
            {
                if (!seen.Add(mi.Id)) continue;
                var n = parent.Add(mi.Id.ToString("000") + "  " + mi.Name);
                n.Tag = mi;
                AddMapNodes(n.Nodes, mi.Id, seen);
                if (mi.Expanded) n.Expand();
            }
        }

        void ShowTreeMenu(TreeNode node, Point at)
        {
            var menu = new ContextMenuStrip();
            if (node.Tag is MapInfo || (node.Tag as string) == "maps")
            {
                var mi = node.Tag as MapInfo;
                menu.Items.Add("New Map…", null, (s, e) => NewMap(mi == null ? 0 : mi.Id));
                if (mi != null)
                {
                    menu.Items.Add("Open", null, (s, e) => OpenMap(mi.Id));
                    menu.Items.Add("Rename…", null, (s, e) => RenameMap(mi));
                    menu.Items.Add("Map Properties…", null, (s, e) => { var ed = OpenMap(mi.Id); if (ed != null) ed.ShowProperties(); });
                    menu.Items.Add("Playtest from Here", null, (s, e) => { var m = Project.LoadMap(mi.Id); Playtest(new[] { "--map", mi.Id.ToString(), "--x", (m.Width / 2).ToString(), "--y", (m.Height / 2).ToString() }); });
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Delete Map", null, (s, e) => DeleteMap(mi));
                }
            }
            else if ((node.Tag as string ?? "").StartsWith("dir:" + (Project != null ? Project.ScriptsDir : "\0")) || (node.Parent != null && node.Parent.Text.Contains("Scripts")))
            {
                menu.Items.Add("New Script…", null, (s, e) => NewScript());
                menu.Items.Add("Open Scripts Folder", null, (s, e) => OpenInShell(Project.ScriptsDir));
            }
            else
            {
                var tag = node.Tag as string;
                if (tag != null && (tag.StartsWith("file:") || tag.StartsWith("dir:") || tag.StartsWith("assets:")))
                {
                    string path = tag.Substring(tag.IndexOf(':') + 1);
                    menu.Items.Add("Open", null, (s, e) => OpenNode(node));
                    menu.Items.Add("Show in Explorer", null, (s, e) => OpenInShell(File.Exists(path) ? Path.GetDirectoryName(path) : path));
                }
            }
            if (menu.Items.Count > 0) menu.Show(_tree, at);
        }

        void OpenNode(TreeNode n)
        {
            if (Project == null || n == null) return;
            var tag = n.Tag;
            if (tag is DatabaseTable) { OpenTable((DatabaseTable)tag); return; }
            if (tag is MapInfo) { OpenMap(((MapInfo)tag).Id); return; }
            var s = tag as string;
            if (s == null) return;
            if (s == "sys:settings") OpenSettings();
            else if (s == "sys:players") OpenPlayers();
            else if (s == "sys:switches") OpenDocument(s, () => new SwitchesDocument(Project));
            else if (s == "sys:tilesets") OpenDocument(s, () => new TilesetEditor(Project));
            else if (s == "sys:connections") OpenDocument(s, () => new ObjectDocument("Map Connections", Project.Connections, () => Json.Json.ToFile(Project.DataFile("map_connections.json"), Project.Connections), "Maps that join seamlessly at their edges. Edge is North/South/East/West; offsets line the maps up."));
            else if (s.StartsWith("assets:")) { string dir = s.Substring(7); OpenDocument(s, () => new AssetBrowser(Project, dir)); }
            else if (s.StartsWith("file:")) OpenFile(s.Substring(5));
        }

        public void OpenFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (new[] { ".png", ".gif", ".bmp", ".jpg" }.Contains(ext)) { OpenDocument("assets:" + Path.GetDirectoryName(path), () => new AssetBrowser(Project, Path.GetDirectoryName(path))); return; }
            if (new[] { ".cs", ".json", ".txt", ".md", ".ini", ".rb", ".xml", ".csv", ".config" }.Contains(ext) || new FileInfo(path).Length < 512 * 1024)
                OpenDocument("file:" + path, () => new TextEditor(path));
            else OpenInShell(path);
        }

        // ------------------------------------------------------------------ documents
        TabPage FindTab(string key) { return _tabs.TabPages.Cast<TabPage>().FirstOrDefault(t => (t.Name ?? "") == key); }

        public Control OpenDocument(string key, Func<Control> create)
        {
            var tab = FindTab(key);
            if (tab == null)
            {
                Control c;
                try { Cursor = Cursors.WaitCursor; c = create(); }
                catch (Exception ex) { MessageBox.Show(this, "Could not open: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return null; }
                finally { Cursor = Cursors.Default; }
                var doc = c as IDocument;
                tab = new TabPage(doc != null ? doc.Title : key) { Name = key, Tag = doc };
                c.Dock = DockStyle.Fill;
                tab.Controls.Add(c);
                _tabs.TabPages.Add(tab);
                var ev = c.GetType().GetEvent("DirtyChanged");
                if (ev != null) ev.AddEventHandler(c, new Action(RefreshTabTitles));
            }
            _tabs.SelectedTab = tab;
            return tab.Controls.Count > 0 ? tab.Controls[0] : null;
        }

        void RefreshTabTitles()
        {
            foreach (TabPage tp in _tabs.TabPages)
            {
                var d = tp.Tag as IDocument;
                if (d != null) tp.Text = d.Title + (d.Dirty ? " •" : "") + "   ";
            }
        }

        void CloseTab(TabPage tp)
        {
            if (tp == null) return;
            var d = tp.Tag as IDocument;
            if (d != null && d.Dirty)
            {
                var r = MessageBox.Show(this, "Save changes to " + d.Title + "?", "Close", MessageBoxButtons.YesNoCancel);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes) d.Save();
            }
            _tabs.TabPages.Remove(tp);
            tp.Dispose();
        }

        void CloseTabAt(Point p)
        {
            for (int i = 0; i < _tabs.TabCount; i++) if (_tabs.GetTabRect(i).Contains(p)) { CloseTab(_tabs.TabPages[i]); return; }
        }

        void OpenTable(DatabaseTable t) { OpenDocument("db:" + t.Key, () => new DatabaseEditor(Project, t)); }

        void OpenSettings()
        {
            if (Project == null) return;
            OpenDocument("sys:settings", () => new ObjectDocument("Game Settings", Project.Settings, () =>
            {
                Json.Json.ToFile(Project.DataFile("settings.json"), Project.Settings);
                Json.Json.ToFile(Path.Combine(Project.Root, Project.ProjectFileName), Project.Info);
            }, "Title, where a new game starts, screen size, fonts and music."));
        }

        void OpenPlayers()
        {
            if (Project == null) return;
            OpenDocument("sys:players", () => new ObjectDocument("Player Characters", Project.Players, () => Json.Json.ToFile(Project.DataFile("players.json"), Project.Players), "Each playable character: trainer type and overworld sprites."));
        }

        public MapEditor OpenMap(int id)
        {
            var info = Project.GetMapInfo(id);
            return OpenDocument("map:" + id, () => new MapEditor(this, Project, id)) as MapEditor;
        }

        // ------------------------------------------------------------------ maps
        void NewMap(int parentId)
        {
            string name = Prompt.Ask(this, "New map", "Map name:", "New Map");
            if (string.IsNullOrEmpty(name)) return;
            int ts = Project.Tilesets.Count > 0 ? Project.Tilesets[0].Id : 1;
            var parentMap = parentId > 0 ? Project.LoadMap(parentId) : null;
            if (parentMap != null) ts = parentMap.TilesetId;
            var m = Project.CreateMap(name, parentId, 20, 15, ts);
            Json.Json.ToFile(Project.DataFile("mapinfos.json"), Project.MapInfos);
            RebuildTree();
            OpenMap(m.Id);
        }

        void RenameMap(MapInfo mi)
        {
            string name = Prompt.Ask(this, "Rename map", "Map name:", mi.Name);
            if (string.IsNullOrEmpty(name)) return;
            mi.Name = name;
            Json.Json.ToFile(Project.DataFile("mapinfos.json"), Project.MapInfos);
            RebuildTree();
        }

        void DeleteMap(MapInfo mi)
        {
            if (MessageBox.Show(this, "Delete map " + mi.Id + " \"" + mi.Name + "\"? This can't be undone.", "Delete map", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var tab = FindTab("map:" + mi.Id); if (tab != null) { _tabs.TabPages.Remove(tab); tab.Dispose(); }
            Project.DeleteMap(mi.Id);
            Json.Json.ToFile(Project.DataFile("mapinfos.json"), Project.MapInfos);
            RebuildTree();
        }

        void NewScript()
        {
            string name = Prompt.Ask(this, "New script", "Script name (letters only):", "MyScript");
            if (string.IsNullOrEmpty(name)) return;
            name = new string(name.Where(char.IsLetterOrDigit).ToArray());
            if (name.Length == 0) return;
            Directory.CreateDirectory(Project.ScriptsDir);
            string path = Path.Combine(Project.ScriptsDir, name + ".cs");
            if (!File.Exists(path)) File.WriteAllText(path, ScriptTemplate.For(name));
            RebuildTree();
            OpenFile(path);
        }

        // ------------------------------------------------------------------ playtest
        void PlaytestHere()
        {
            var ed = _tabs.SelectedTab != null && _tabs.SelectedTab.Controls.Count > 0 ? _tabs.SelectedTab.Controls[0] as MapEditor : null;
            if (ed == null) { Playtest(null); return; }
            var pos = ed.PlaytestPosition();
            Playtest(new[] { "--map", ed.MapId.ToString(), "--x", pos.X.ToString(), "--y", pos.Y.ToString() });
        }

        public void Playtest(string[] extra)
        {
            if (Project == null) return;
            SaveAll();
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string game = Path.Combine(exeDir, "Game.exe");
            if (!File.Exists(game)) { MessageBox.Show(this, "Game.exe was not found next to the Studio:\n" + game, "Playtest"); return; }
            var args = new List<string> { Quote(Project.Root) };
            if (extra != null) args.AddRange(extra);
            try
            {
                var psi = new ProcessStartInfo(game, string.Join(" ", args)) { WorkingDirectory = exeDir, UseShellExecute = false };
                if (Type.GetType("Mono.Runtime") != null && Environment.OSVersion.Platform == PlatformID.Unix) { psi.FileName = "mono"; psi.Arguments = Quote(game) + " " + psi.Arguments; }
                Process.Start(psi);
                SetStatus("Playtest started");
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not start the game: " + ex.Message, "Playtest"); }
        }

        static string Quote(string s) { return "\"" + s + "\""; }

        public static void OpenInShell(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        void ShowHelp()
        {
            MessageBox.Show(this,
                "1. Import your Essentials project (File → Import) or open a PokeCreator project.\n" +
                "2. Edit data in Database: Pokémon, moves, items, trainers, encounters... Changes save with Ctrl+S.\n" +
                "3. Double-click a map to edit it: pick tiles on the left, paint with the left mouse button,\n" +
                "   right-click to pick a tile from the map. Switch to the Events layer to edit NPCs, doors and items.\n" +
                "4. Press F5 to playtest, or Shift+F5 to start on the map you're editing.\n" +
                "5. Everything is plain files in the project folder; 'All Project Files' shows them all.",
                "Getting started");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { SaveAll(); return true; }
            if (keyData == Keys.F5) { Playtest(null); return true; }
            if (keyData == (Keys.Shift | Keys.F5)) { PlaytestHere(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>Remembers recent projects between sessions.</summary>
    public class Settings
    {
        public List<string> Recent { get; set; }
        public Settings() { Recent = new List<string>(); }
        static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PokeCreator", "studio.json"); } }
        public static Settings Load() { try { return File.Exists(FilePath) ? Json.Json.FromFile<Settings>(FilePath) : new Settings(); } catch { return new Settings(); } }
        public void Save() { try { Json.Json.ToFile(FilePath, this); } catch { } }
        public void AddRecent(string p) { Recent.Remove(p); Recent.Insert(0, p); while (Recent.Count > 10) Recent.RemoveAt(Recent.Count - 1); }
    }

    public static class Prompt
    {
        public static string Ask(IWin32Window owner, string title, string label, string initial)
        {
            using (var f = new Form { Text = title, Width = 380, Height = 150, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false })
            {
                var l = new Label { Text = label, Left = 12, Top = 12, AutoSize = true };
                var tb = new TextBox { Text = initial, Left = 12, Top = 34, Width = 340 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 196, Top = 70, Width = 75 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 277, Top = 70, Width = 75 };
                f.Controls.AddRange(new Control[] { l, tb, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? tb.Text.Trim() : null;
            }
        }
    }

    public static class ScriptTemplate
    {
        public static string For(string name)
        {
            return
"using System.Collections.Generic;\n" +
"using PokeCreator.Runtime;\n" +
"using PokeCreator.Runtime.Script;\n\n" +
"// Scripts in this folder are compiled when the game starts.\n" +
"// Mark a static method with [ScriptCommand(\"name\")] and call it from an event's\n" +
"// Script command or Conditional Branch, e.g.:   " + char.ToLower(name[0]) + name.Substring(1) + "Hello(\"world\")\n" +
"public static class " + name + "\n" +
"{\n" +
"    [ScriptCommand(\"" + char.ToLower(name[0]) + name.Substring(1) + "Hello\")]\n" +
"    public static object Hello(Game game, Interpreter ctx, List<object> args)\n" +
"    {\n" +
"        string who = args.Count > 0 ? args[0].ToString() : \"trainer\";\n" +
"        game.Message.Show(\"Hello, \" + who + \"! Greetings from " + name + ".cs\");\n" +
"        return true;\n" +
"    }\n" +
"}\n";
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message + "\n\n" + e.Exception.StackTrace, "PokeCreator Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }
}
