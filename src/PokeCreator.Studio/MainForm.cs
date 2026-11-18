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
        readonly ProjectTree _tree = new ProjectTree { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f), BackColor = Theme.Sidebar };
        readonly DocumentTabs _tabs = new DocumentTabs { Dock = DockStyle.Fill };
        readonly StatusStrip _status = new StatusStrip();
        readonly ToolStripStatusLabel _statusText = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly Panel _welcome = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly Settings _settings = Settings.Load();

        public MainForm(string openPath)
        {
            WheelScroll.Install();
            ApplyAppearance(_settings, false);
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

            // One click opens an item; on a folder-like item it expands/collapses it
            _tree.NodeClicked += n =>
            {
                if (OpenNode(n)) { BeginInvoke(new Action(() => _tree.Focus())); return; }
                if (n.Nodes.Count == 0) return;
                bool secondOfDouble = n == _lastToggled && (DateTime.Now - _lastToggleTime).TotalMilliseconds < SystemInformation.DoubleClickTime;
                if (secondOfDouble) { _lastToggled = null; return; }
                n.Toggle(); _lastToggled = n; _lastToggleTime = DateTime.Now;
            };
            _tree.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && _tree.SelectedNode != null) OpenNode(_tree.SelectedNode); };
            _tree.NodeMouseClick += (s, e) => { if (e.Button == MouseButtons.Right) { _tree.SelectedNode = e.Node; ShowTreeMenu(e.Node, e.Location); } };
            _tabs.CloseRequested += CloseTab;
            FormClosing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; else _settings.Save(); };

            BuildWelcome();
            _tabs.Visible = false;
            SetStatus("Welcome to PokeCreator Studio");

            ApplyAppearance(_settings, true);
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
            game.DropDownItems.Add(new ToolStripMenuItem("&World Map", null, (s, e) => OpenWorld()) { ShortcutKeys = Keys.Control | Keys.M });
            game.DropDownItems.Add("Tilesets", null, (s, e) => { if (Project != null) OpenDocument("sys:tilesets", () => new TilesetEditor(Project)); });
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
                db.DropDownItems.Add(new ToolStripSeparator());
                db.DropDownItems.Add("Add Missing Pokémon && Data…", null, (a, b) => MergeBaseData());
            };
            db.DropDownItems.Add("(open a project)");

            var view = new ToolStripMenuItem("&View");
            view.DropDownItems.Add(new ToolStripMenuItem("&Appearance…", null, (s, e) => OpenAppearance()));
            var darkItem = new ToolStripMenuItem("&Dark Mode", null, (s, e) => { _settings.DarkMode = !_settings.DarkMode; ApplyAppearance(_settings, true); }) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.D };
            view.DropDownOpening += (s, e) => darkItem.Checked = _settings.DarkMode;
            view.DropDownItems.Add(darkItem);

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add("Getting Started", null, (s, e) => ShowHelp());
            help.DropDownItems.Add("About PokeCreator", null, (s, e) => MessageBox.Show(this, "PokeCreator Studio " + Project.EngineVersion + "\nA standalone engine and editor for Pokémon fan games.\n\nhttps://github.com/Syn34/pokeCreator", "About"));

            m.Items.AddRange(new ToolStripItem[] { file, view, game, db, help });
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
                // New images dropped into tileset folders become new areas
                try { if (p.SyncTilesetFolders()) Json.Json.ToFile(p.DataFile("tilesets.json"), p.Tilesets); } catch { }
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
            using (var d = new NewProjectDialog(_settings.LastProjectLocation))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.ProjectFolder == null) return;
                _settings.LastProjectLocation = d.ProjectLocation;
                _settings.Save();
                OpenProject(d.ProjectFolder);
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

            var settings = Node(_tree.Nodes, "Game Settings", "settings", "sys:settings");
            Node(settings.Nodes, "Game Settings", "settings", "sys:settings");
            Node(settings.Nodes, "Player Characters", "players", "sys:players");
            Node(settings.Nodes, "Switches & Variables", "switches", "sys:switches");
            Node(settings.Nodes, "Tilesets", "tilesets", "sys:tilesets");
            Node(settings.Nodes, "Map Connections (list)", "connections", "sys:connections");
            Node(settings.Nodes, "Appearance (Studio)", "appearance", "sys:appearance");

            var db = Node(_tree.Nodes, "Database", "database", null);
            foreach (var t in p.Tables) Node(db.Nodes, t.Label + " (" + t.Items.Count + ")", t.Key, t);
            db.Expand();

            var maps = Node(_tree.Nodes, "Maps (" + p.MapInfos.Count + ")", "maps", "maps");
            Node(maps.Nodes, "World Map (connections)", "world", "sys:world");
            AddMapNodes(maps.Nodes, 0, new HashSet<int>());
            maps.Expand();

            var scripts = Node(_tree.Nodes, "Scripts", "scripts", "dir:" + p.ScriptsDir);
            if (Directory.Exists(p.ScriptsDir))
                foreach (var f in Directory.GetFiles(p.ScriptsDir, "*.cs").OrderBy(x => x)) Node(scripts.Nodes, Path.GetFileName(f), "cs", "file:" + f);

            var assets = Node(_tree.Nodes, "Graphics & Audio", "assets", null);
            foreach (var top in new[] { "Graphics", "Audio", "Fonts" })
            {
                string dir = Path.Combine(p.Root, top);
                if (!Directory.Exists(dir)) continue;
                var n = Node(assets.Nodes, top, top == "Audio" ? "audio" : top == "Fonts" ? "font" : "image", "assets:" + dir);
                foreach (var sub in Directory.GetDirectories(dir).OrderBy(x => x)) AddAssetDir(n.Nodes, sub, 0);
            }

            var files = Node(_tree.Nodes, "All Project Files", "files", "dir:" + p.Root);
            files.Nodes.Add("(loading…)");
            _tree.BeforeExpand -= LazyFiles; _tree.BeforeExpand += LazyFiles;
            _tree.EndUpdate();
        }

        static TreeNode Node(TreeNodeCollection parent, string text, string icon, object tag)
        {
            var n = parent.Add(text);
            n.ImageKey = icon; n.SelectedImageKey = icon; n.Tag = tag;
            return n;
        }

        void ChangeMapIcon(MapInfo mi)
        {
            var labels = new List<string> { "Automatic (from the map's name)" };
            labels.AddRange(Icons.MapChoices.Select(c => c[1]));
            int k = ChoiceDialog.Ask(this, "Icon for " + mi.Name, labels.ToArray());
            if (k < 0) return;
            mi.Icon = k == 0 ? null : Icons.MapChoices[k - 1][0];
            Json.Json.ToFile(Project.DataFile("mapinfos.json"), Project.MapInfos);
            RefreshMapIcons(_tree.Nodes);
        }

        void RefreshMapIcons(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                var mi = n.Tag as MapInfo;
                if (mi != null) n.ImageKey = n.SelectedImageKey = Icons.ForMap(mi, Project.GetMapInfo(mi.ParentId));
                RefreshMapIcons(n.Nodes);
            }
            _tree.Invalidate();
        }

        /// <summary>Adds Pokémon, moves, abilities and items from the built-in database that this project doesn't have.</summary>
        void MergeBaseData()
        {
            if (Project == null) return;
            string folder = BaseData.DefaultFolder;
            if (folder == null) { MessageBox.Show(this, "The BaseData folder wasn't found next to PokeCreatorStudio.exe.", "Add missing data"); return; }
            if (MessageBox.Show(this, "Add every Pokémon, form, move, ability and item from PokeCreator's built-in database that this project doesn't have yet?\n\n" +
                "Nothing you've already made or edited will be changed. New evolutions are added to existing Pokémon only when they lead to a newly added Pokémon.",
                "Add missing Pokémon & data", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            SaveAll();
            foreach (TabPage tp in _tabs.TabPages.Cast<TabPage>().Where(t => (t.Name ?? "").StartsWith("db:")).ToList()) { _tabs.TabPages.Remove(tp); tp.Dispose(); }
            var result = BaseData.MergeInto(Project, folder);
            foreach (var t in Project.Tables) Json.Json.ToFile(Project.DataFile(t.FileName), t.Items);
            RebuildTree();
            SetStatus(result.ToString());
            MessageBox.Show(this, result.ToString() + "\n\nYour project now has " + Project.Species.Count(sp => sp.Form == 0) + " Pokémon.", "Add missing Pokémon & data");
        }

        void OpenAppearance() { OpenDocument("sys:appearance", () => new AppearanceEditor(_settings, s => ApplyAppearance(s, true))); }

        /// <summary>Applies the Studio appearance settings (and saves them when 'live').</summary>
        void ApplyAppearance(Settings s, bool live)
        {
            GuideStyle guides;
            if (!Enum.TryParse(s.Guides ?? "", out guides)) guides = GuideStyle.Rainbow;
            var size = s.FontSize >= 8 && s.FontSize <= 14 ? s.FontSize : 9;
            Font = new Font("Segoe UI", size);
            _tree.Font = new Font("Segoe UI", size + 0.5f);
            _tree.ItemHeight = (int)Math.Round(24 * size / 9.0);
            if (live) { Theme.Set(s.DarkMode, AppearanceEditor.ParseColor(s.Accent), guides, s.ShowIcons); s.Save(); }
            else Theme.Set(s.DarkMode, AppearanceEditor.ParseColor(s.Accent), guides, s.ShowIcons);
        }

        void AddAssetDir(TreeNodeCollection parent, string dir, int depth)
        {
            var n = Node(parent, Path.GetFileName(dir), "folder", "assets:" + dir);
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
                var n = Node(e.Node.Nodes, Path.GetFileName(d), "folder", "dir:" + d); n.Nodes.Add("(loading…)");
            }
            foreach (var f in Directory.GetFiles(dir).OrderBy(x => x)) Node(e.Node.Nodes, Path.GetFileName(f), Icons.ForFile(f), "file:" + f);
        }

        void AddMapNodes(TreeNodeCollection parent, int parentId, HashSet<int> seen)
        {
            foreach (var mi in Project.MapInfos.Where(m => m.ParentId == parentId).OrderBy(m => m.Order))
            {
                if (!seen.Add(mi.Id)) continue;
                var n = Node(parent, mi.Id.ToString("000") + "  " + mi.Name, Icons.ForMap(mi, Project.GetMapInfo(mi.ParentId)), mi);
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
                    menu.Items.Add("Change Icon…", null, (s, e) => ChangeMapIcon(mi));
                    menu.Items.Add("Map Properties…", null, (s, e) => { var ed = OpenMap(mi.Id); if (ed != null) ed.ShowProperties(); });
                    menu.Items.Add("Playtest from Here", null, (s, e) => { var m = Project.LoadMap(mi.Id); Playtest(new[] { "--map", mi.Id.ToString(), "--x", (m.Width / 2).ToString(), "--y", (m.Height / 2).ToString() }); });
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add("Delete Map", null, (s, e) => DeleteMap(mi));
                }
            }
            else if ((node.Tag as string ?? "").StartsWith("dir:" + (Project != null ? Project.ScriptsDir : "\0")) || (node.Parent != null && node.Parent.ImageKey == "scripts"))
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

        TreeNode _lastToggled; DateTime _lastToggleTime;

        /// <summary>Opens what a tree item stands for. Returns false for items that only group others.</summary>
        bool OpenNode(TreeNode n)
        {
            if (Project == null || n == null) return false;
            var tag = n.Tag;
            if (tag is DatabaseTable) { OpenTable((DatabaseTable)tag); return true; }
            if (tag is MapInfo) { OpenMap(((MapInfo)tag).Id); return true; }
            var s = tag as string;
            if (s == null || !(s.StartsWith("sys:") || s.StartsWith("assets:") || s.StartsWith("file:"))) return false;
            if (s == "sys:settings") OpenSettings();
            else if (s == "sys:appearance") OpenAppearance();
            else if (s == "sys:players") OpenPlayers();
            else if (s == "sys:switches") OpenDocument(s, () => new SwitchesDocument(Project));
            else if (s == "sys:tilesets") OpenDocument(s, () => new TilesetEditor(Project));
            else if (s == "sys:world") OpenWorld();
            else if (s == "sys:connections") { var w = FindTab("sys:world"); if (w != null) CloseTab(w); if (FindTab("sys:world") == null) OpenDocument(s, () => new ObjectDocument("Map Connections", Project.Connections, () => Json.Json.ToFile(Project.DataFile("map_connections.json"), Project.Connections), "Maps that join seamlessly at their edges. Edge is North/South/East/West; offsets line the maps up.")); }
            else if (s.StartsWith("assets:")) { string dir = s.Substring(7); OpenDocument(s, () => new AssetBrowser(Project, dir)); }
            else if (s.StartsWith("file:")) OpenFile(s.Substring(5));
            else return false;
            return true;
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

        void OpenWorld()
        {
            if (Project == null) return;
            // The connection list document edits the same data; close it so the two can't disagree
            var list = FindTab("sys:connections");
            if (list != null) CloseTab(list);
            OpenDocument("sys:world", () => new WorldEditor(this, Project));
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

        /// <summary>Rebuilds the project tree after another document added or renamed things.</summary>
        public void RefreshTree() { RebuildTree(); }

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
        public bool DarkMode { get; set; }
        public string Accent { get; set; }
        public string Guides { get; set; }
        public bool ShowIcons { get; set; }
        public int FontSize { get; set; }
        /// <summary>Where the last new project was stored; the New Project dialog starts there.</summary>
        public string LastProjectLocation { get; set; }
        public Settings() { Recent = new List<string>(); Accent = "200,48,48"; Guides = "Rainbow"; ShowIcons = true; FontSize = 9; }
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
            Application.Idle += (s, e) => { foreach (Form f in Application.OpenForms) Theme.ApplyIfNeeded(f); };
            Application.ThreadException += (s, e) => MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message + "\n\n" + e.Exception.StackTrace, "PokeCreator Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }
}
