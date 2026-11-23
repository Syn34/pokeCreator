using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Projects;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>
    /// Game Settings → Appearance (Game UI): the custom battle and menu pictures (Data/ui.json), with a live preview
    /// of a battle drawn the way the game lays the pieces out.
    /// </summary>
    public class GameUiEditor : UserControl, IDocument
    {
        readonly Project _p;
        readonly PictureBox _preview = new PictureBox { Width = 512, Height = 384, SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Black };
        readonly Dictionary<string, Image> _images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        readonly ComboBox _scene = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        public string Title { get { return "Appearance (Game UI)"; } }
        public bool Dirty { get; private set; }
        public event Action DirtyChanged;

        Control _form;
        readonly Panel _formHost = new Panel { Dock = DockStyle.Fill };

        void BuildForm()
        {
            var fb = new FormBuilder();
            fb.Changed += () => { MarkDirty(); _images.Clear(); Redraw(); };
            if (_form != null) { _formHost.Controls.Remove(_form); _form.Dispose(); }
            _form = fb.Build(_p.Ui);
            _formHost.Controls.Add(_form);
        }

        void MarkDirty() { if (!Dirty) { Dirty = true; if (DirtyChanged != null) DirtyChanged(); } }

        public GameUiEditor(Project p)
        {
            _p = p; Dock = DockStyle.Fill;
            if (_p.Ui == null) _p.Ui = new UiSkin();
            BuildForm();
            var form = _formHost;

            var right = new Panel { Dock = DockStyle.Right, Width = 540, Padding = new Padding(10) };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34 };
            _scene.Items.AddRange(new object[] { "Single battle: choosing a command", "Single battle: choosing a move", "Single battle: message", "Double battle", "Trainer battle start (party balls)" });
            _scene.SelectedIndex = 0;
            _scene.SelectedIndexChanged += (s, e) => Redraw();
            var reload = new Button { Text = "Reload pictures", AutoSize = true };
            reload.Click += (s, e) => { _images.Clear(); Redraw(); };
            var folder = new Button { Text = "Open UI Folder", AutoSize = true };
            folder.Click += (s, e) => { string d = Path.Combine(_p.Root, "Graphics", "UI"); Directory.CreateDirectory(d); MainForm.OpenInShell(d); };
            var kitIn = new Button { Text = "Import UI Kit…", AutoSize = true };
            kitIn.Click += (s, e) => ImportKit();
            var kitOut = new Button { Text = "Save Layout to Kit…", AutoSize = true };
            kitOut.Click += (s, e) => ExportKit();
            new ToolTip().SetToolTip(kitIn, "Copy a folder of UI pictures into Graphics/UI. If it has a ui-layout.json, its pictures and positions are filled in too.");
            new ToolTip().SetToolTip(kitOut, "Write these settings as ui-layout.json into a kit folder in Graphics/UI, so the folder can be shared and imported in another game.");
            bar.Height = 64;
            bar.Controls.AddRange(new Control[] { new Label { Text = "Preview:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) }, _scene, reload, folder, kitIn, kitOut });
            var note = new Label
            {
                Dock = DockStyle.Top, Height = 92, ForeColor = Theme.SubText, Padding = new Padding(0, 6, 0, 0),
                Text = "Put your pictures in Graphics/UI (sub-folders are fine) and pick them on the left. Anything left empty is drawn the built-in way.\n" +
                       "Positions inside a data box are in the picture's own pixels (before Picture scale) and can be negative (e.g. a name above the box).\n" +
                       "The preview uses sample Pokémon; the game uses the same layout."
            };
            var holder = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            holder.Controls.Add(_preview);
            right.Controls.Add(holder); right.Controls.Add(note); right.Controls.Add(bar);

            var head = new Panel { Dock = DockStyle.Top, Height = 64 };
            head.Controls.Add(new Label { Text = "Custom pictures for battles and menus: data boxes, HP / EXP bars, status icons, numbers, party balls, menu backgrounds and the selection arrow.", Dock = DockStyle.Top, Height = 26, Padding = new Padding(8, 4, 8, 0), ForeColor = Color.DimGray });
            head.Controls.Add(new Label { Text = Title, Dock = DockStyle.Top, Height = 34, Font = Theme.TitleFont, Padding = new Padding(8, 4, 0, 0) });
            Controls.Add(form); Controls.Add(new Splitter { Dock = DockStyle.Right }); Controls.Add(right); Controls.Add(head);
            Redraw();
        }

        public void Save()
        {
            Json.Json.ToFile(_p.DataFile("ui.json"), _p.Ui);
            Dirty = false; if (DirtyChanged != null) DirtyChanged();
        }

        // ------------------------------------------------------------------ UI kits

        const string LayoutFile = "ui-layout.json";

        static IEnumerable<System.Reflection.PropertyInfo> PictureFields()
        {
            return typeof(UiSkin).GetProperties().Where(pr => pr.PropertyType == typeof(string) && pr.CanWrite &&
                pr.GetCustomAttributes(typeof(FieldAttribute), true).Cast<FieldAttribute>().Any(f => f.Asset != null));
        }

        /// <summary>Copies a kit folder into Graphics/UI and applies its ui-layout.json (picture names relative to the kit).</summary>
        void ImportKit()
        {
            string src;
            using (var d = new FolderBrowserDialog { Description = "Choose the folder with the UI pictures (and ui-layout.json, if it has one)" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                src = d.SelectedPath;
            }
            string name = Path.GetFileName(src.TrimEnd('/', '\\'));
            string uiDir = Path.Combine(_p.Root, "Graphics", "UI"), dst = Path.Combine(uiDir, name);
            try
            {
                if (!string.Equals(Path.GetFullPath(src).TrimEnd('/', '\\'), Path.GetFullPath(dst).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(dst) && MessageBox.Show(FindForm(), "Graphics/UI/" + name + " already exists. Replace its files?", "Import UI Kit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    {
                        string rel = f.Substring(src.TrimEnd('/', '\\').Length).TrimStart('/', '\\');
                        string to = Path.Combine(dst, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        File.Copy(f, to, true);
                    }
                }
                string layout = Path.Combine(dst, LayoutFile);
                if (File.Exists(layout))
                {
                    var kit = Json.Json.FromFile<UiSkin>(layout);
                    foreach (var pr in typeof(UiSkin).GetProperties().Where(x => x.CanWrite && x.CanRead))
                    {
                        object v = pr.GetValue(kit, null);
                        if (PictureFields().Contains(pr) && v is string && ((string)v).Length > 0) v = name + "/" + ((string)v).Replace('\\', '/');
                        pr.SetValue(_p.Ui, v, null);
                    }
                    MessageBox.Show(FindForm(), "Imported " + name + " into Graphics/UI and applied its layout. Check the preview, then Save.", "Import UI Kit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else MessageBox.Show(FindForm(), "Copied " + name + " into Graphics/UI. It has no " + LayoutFile + ", so pick its pictures on the left.", "Import UI Kit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                MarkDirty(); _images.Clear(); BuildForm(); Redraw();
            }
            catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Import UI Kit", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        /// <summary>Writes the current settings as the kit folder's ui-layout.json (picture names relative to that folder).</summary>
        void ExportKit()
        {
            string uiDir = Path.Combine(_p.Root, "Graphics", "UI");
            // The kit is the first folder the pictures are in
            var used = PictureFields().Select(pr => pr.GetValue(_p.Ui, null) as string).Where(v => !string.IsNullOrEmpty(v) && v.Contains("/")).Select(v => v.Split('/')[0]).Distinct().ToList();
            if (used.Count == 0) { MessageBox.Show(FindForm(), "Pick pictures from a folder in Graphics/UI first: that folder is the kit.", "Save Layout to Kit"); return; }
            string kitName = used[0];
            if (used.Count > 1 && MessageBox.Show(FindForm(), "The pictures come from several folders (" + string.Join(", ", used) + "). Save the layout into " + kitName + "? Pictures from other folders are left out.", "Save Layout to Kit", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            var kit = new UiSkin();
            foreach (var pr in typeof(UiSkin).GetProperties().Where(x => x.CanWrite && x.CanRead))
            {
                object v = pr.GetValue(_p.Ui, null);
                if (PictureFields().Contains(pr))
                {
                    var sv = v as string;
                    v = !string.IsNullOrEmpty(sv) && sv.StartsWith(kitName + "/") ? sv.Substring(kitName.Length + 1) : null;
                }
                pr.SetValue(kit, v, null);
            }
            string file = Path.Combine(uiDir, kitName, LayoutFile);
            Json.Json.ToFile(file, kit);
            MessageBox.Show(FindForm(), "Saved Graphics/UI/" + kitName + "/" + LayoutFile + ". Share the " + kitName + " folder; Import UI Kit… sets it all up in another game.", "Save Layout to Kit", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------------ preview

        Image Img(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Image i;
            if (_images.TryGetValue(name, out i)) return i;
            i = Theme.LoadImage(_p.FindAsset("Graphics/UI", name));
            _images[name] = i;
            return i;
        }

        Image InFolder(string folder, string name) { return string.IsNullOrEmpty(folder) ? null : Img(folder.TrimEnd('/', '\\') + "/" + name); }

        UiSkin U { get { return _p.Ui; } }
        int S { get { return U.S; } }

        void Pic(Graphics g, Image i, int x, int y) { if (i != null) g.DrawImage(i, new Rectangle(x, y, i.Width * S, i.Height * S), 0, 0, i.Width, i.Height, GraphicsUnit.Pixel); }

        Color TextCol(string hex, Color def) { var c = UiSkin.Rgb(hex); return c == null ? def : Color.FromArgb(c[0], c[1], c[2]); }

        void Text(Graphics g, string s, int x, int y, bool right = false)
        {
            using (var f = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var sz = g.MeasureString(s, f);
                float tx = right ? x - sz.Width : x;
                using (var sh = new SolidBrush(TextCol(U.TextShadow, Color.FromArgb(208, 208, 200)))) g.DrawString(s, f, sh, tx + 1, y + 1);
                using (var b = new SolidBrush(TextCol(U.TextColor, Color.FromArgb(64, 64, 64)))) g.DrawString(s, f, b, tx, y);
            }
        }

        int TextWidth(Graphics g, string s) { using (var f = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel)) return (int)g.MeasureString(s, f).Width; }

        int Number(Graphics g, int x, int y, string s, bool right)
        {
            var parts = new List<Image>();
            bool pics = !string.IsNullOrEmpty(U.DigitsFolder);
            if (pics) foreach (char ch in s) { var t = ch == '/' ? Img(U.HpSlash) : char.IsDigit(ch) ? InFolder(U.DigitsFolder, "digit_" + ch) : null; if (t == null) { pics = false; break; } parts.Add(t); }
            if (!pics) { Text(g, s, x, y - 4, right); return TextWidth(g, s); }
            int w = parts.Sum(t => t.Width * S), cx = right ? x - w : x;
            foreach (var t in parts) { Pic(g, t, cx, y); cx += t.Width * S; }
            return w;
        }

        void Bar(Graphics g, int x, int y, double f, Image frame, Image empty, Image fill, int defW, Color defColor)
        {
            var shape = frame ?? fill;
            if (shape == null)
            {
                g.FillRectangle(new SolidBrush(Color.FromArgb(72, 72, 80)), x - 2, y - 2, defW + 4, 10);
                g.FillRectangle(new SolidBrush(Color.FromArgb(40, 40, 48)), x, y, defW, 6);
                g.FillRectangle(new SolidBrush(defColor), x, y, (int)(defW * f), 6);
                return;
            }
            if (frame != null) Pic(g, frame, x, y);
            var inner = fill ?? empty;
            if (inner == null) return;
            int ox = frame != null ? (frame.Width - inner.Width) / 2 : 0, oy = frame != null ? (frame.Height - inner.Height) / 2 : 0;
            if (empty != null) Pic(g, empty, x + ox * S, y + oy * S);
            if (fill != null && f > 0)
            {
                int src = Math.Max(1, (int)Math.Ceiling(fill.Width * f));
                g.DrawImage(fill, new Rectangle(x + ox * S, y + oy * S, src * S, fill.Height * S), 0, 0, src, fill.Height, GraphicsUnit.Pixel);
            }
        }

        void StatusIcon(Graphics g, int x, int y, string code)
        {
            var t = InFolder(U.StatusFolder, "status_" + code);
            if (t != null) Pic(g, t, x, y);
            else using (var f = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel)) g.DrawString(code, f, Brushes.OrangeRed, x, y - 6);
        }

        /// <summary>Same layout as the game's BattleScene.DrawCustomBox.</summary>
        void DrawBox(Graphics g, bool foe, bool multi, int slot, int count, string name, string gender, int level, int hp, int max, string status, double exp)
        {
            int s = S;
            var tex = (multi ? Img(foe ? U.FoeBoxSmall : U.PlayerBoxSmall) : null) ?? Img(foe ? U.FoeBox : U.PlayerBox);
            if (tex == null)
            {
                // Built-in box (approximate)
                int bx = foe ? 16 : 262, by = foe ? (multi ? 8 + slot * 54 : 30) : (multi ? 196 + slot * 46 : 192), bw = foe ? 236 : 240, bh = foe ? (multi ? 50 : 62) : (multi ? 44 : 88);
                using (var b = new SolidBrush(Color.FromArgb(248, 248, 232))) g.FillRectangle(b, bx, by, bw, bh);
                using (var pen = new Pen(Color.FromArgb(64, 80, 72), 2)) g.DrawRectangle(pen, bx, by, bw, bh);
                Text(g, name, bx + 12, by + 2); Text(g, "Lv" + level, bx + bw - 12, by + 2, true);
                if (Img(U.HpFrame) != null || Img(U.HpGreen) != null) Bar(g, bx + 104, by + (multi ? 34 : 40), (double)hp / max, Img(U.HpFrame), Img(U.HpEmpty), Img((double)hp / max > 0.5 ? U.HpGreen : (double)hp / max > 0.2 ? U.HpYellow : U.HpRed), 100, Color.LimeGreen);
                else Bar(g, bx + 104, by + (multi ? 34 : 40), (double)hp / max, null, null, null, 100, Color.LimeGreen);
                return;
            }
            int above = Math.Max(0, -UiSkin.Pos(foe ? U.FoeName : U.PlayerName, 4, 0)[1]) * s;
            int boxW = tex.Width * s, boxH = tex.Height * s + above;
            int[] p = foe ? UiSkin.Pos(U.FoeBoxPos, 16, 30) : UiSkin.Pos(U.PlayerBoxPos, 512 - boxW - 8, 384 - 104 - count * (boxH + 4));
            int x = p[0], y = p[1] + slot * (boxH + 4);
            Pic(g, tex, x, y);
            Func<List<int>, int, int, int[]> at = (v, dx, dy) => { var q = UiSkin.Pos(v, dx, dy); return new[] { x + q[0] * s, y + q[1] * s }; };
            var hpFrame = Img(U.HpFrame) ?? Img(U.HpGreen);
            int hfw = hpFrame != null ? hpFrame.Width : 48, hfh = hpFrame != null ? hpFrame.Height : 4;
            var np = at(foe ? U.FoeName : U.PlayerName, 4, 0);
            Text(g, name, np[0], np[1]);
            var gi = gender == "M" ? Img(U.Male) : Img(U.Female);
            int gx = np[0] + TextWidth(g, name) + 4;
            if (gi != null) Pic(g, gi, gx, np[1] + 8); else Text(g, gender == "M" ? "♂" : "♀", gx, np[1]);
            var lp = at(foe ? U.FoeLevel : U.PlayerLevel, tex.Width - 26, 0);
            int lx = lp[0]; var lv = Img(U.LvIcon);
            if (lv != null) { Pic(g, lv, lx, lp[1]); lx += lv.Width * s + s; }
            Number(g, lx, lp[1], (lv == null && string.IsNullOrEmpty(U.DigitsFolder) ? "Lv" : "") + level, false);
            var hpp = at(foe ? U.FoeHpBar : U.PlayerHpBar, tex.Width - hfw - 4, tex.Height / 2 - hfh / 2);
            double f = (double)hp / max;
            Bar(g, hpp[0], hpp[1], f, Img(U.HpFrame), Img(U.HpEmpty), Img(f > 0.5 ? U.HpGreen : f > 0.2 ? U.HpYellow : U.HpRed) ?? Img(U.HpGreen), 96, f > 0.5 ? Color.LimeGreen : f > 0.2 ? Color.Gold : Color.Red);
            if (!string.IsNullOrEmpty(status)) { var sp = at(foe ? U.FoeStatus : U.PlayerStatus, 4, tex.Height / 2); StatusIcon(g, sp[0], sp[1], status); }
            if (foe)
            {
                var cp = at(U.FoeCaught, tex.Width - 12, tex.Height / 2);
                var ci = Img(U.Caught);
                if (ci != null) Pic(g, ci, cp[0], cp[1]); else g.FillEllipse(Brushes.Red, cp[0], cp[1], 10, 10);
                return;
            }
            if (!multi || U.PlayerBoxSmall == null) { var hn = at(U.PlayerHpNumbers, tex.Width - 4, (hpp[1] - y) / s + hfh + 1); Number(g, hn[0], hn[1], hp + "/" + max, true); }
            if (multi && U.PlayerBoxSmall != null) return;
            var expShape = Img(U.ExpFrame) ?? Img(U.ExpFill);
            int efw = expShape != null ? expShape.Width : 64, efh = expShape != null ? expShape.Height : 2;
            var ep = at(U.PlayerExpBar, tex.Width - efw - 4, tex.Height - efh - 2);
            Bar(g, ep[0], ep[1], exp, Img(U.ExpFrame), Img(U.ExpEmpty), Img(U.ExpFill), efw * s, Color.DeepSkyBlue);
        }

        void Bottom(Graphics g, string pic, Action builtIn)
        {
            var t = Img(pic);
            if (t == null) { builtIn(); return; }
            int h = t.Height * S, w = Math.Max(512, t.Width * S);
            g.DrawImage(t, new Rectangle((512 - w) / 2, 384 - h, w, h), 0, 0, t.Width, t.Height, GraphicsUnit.Pixel);
        }

        void Arrow(Graphics g, int x, int y)
        {
            var a = Img(U.Arrow);
            if (a != null) { Pic(g, a, x, y + 8 - a.Height * S / 2); return; }
            g.FillPolygon(Brushes.DimGray, new[] { new Point(x, y), new Point(x + 8, y + 8), new Point(x, y + 16) });
        }

        void Redraw()
        {
            var bmp = new Bitmap(512, 384);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var sky = new LinearGradientBrush(new Rectangle(0, 0, 512, 288), Color.FromArgb(160, 216, 248), Color.FromArgb(232, 248, 200), 90f)) g.FillRectangle(sky, 0, 0, 512, 288);
                using (var grass = new SolidBrush(Color.FromArgb(150, 200, 110))) { g.FillEllipse(grass, 270, 150, 220, 50); g.FillEllipse(grass, -40, 250, 300, 60); }
                g.FillRectangle(new SolidBrush(Color.FromArgb(40, 40, 48)), 0, 284, 512, 100);
                int mode = _scene.SelectedIndex;
                bool dbl = mode == 3;
                if (mode == 4)
                {
                    DrawBalls(g, 96, true); DrawBalls(g, 200, false);
                }
                else if (dbl)
                {
                    DrawBox(g, true, true, 0, 2, "Pidgey", "M", 8, 20, 26, null, 0); DrawBox(g, true, true, 1, 2, "Rattata", "F", 7, 4, 22, "PSN", 0);
                    DrawBox(g, false, true, 0, 2, "Pikachu", "M", 25, 30, 52, "PAR", 0.6); DrawBox(g, false, true, 1, 2, "Bulbasaur", "F", 24, 60, 61, null, 0.3);
                }
                else
                {
                    DrawBox(g, true, false, 0, 1, "Bulbasaur", "M", 12, 25, 34, "SLP", 0);
                    DrawBox(g, false, false, 0, 1, "Pikachu", "F", 25, 18, 52, "BRN", 0.6);
                }
                // Bottom of the screen
                if (mode == 1)
                    Bottom(g, U.FightMenu, () => { g.FillRectangle(Brushes.WhiteSmoke, 4, 288, 340, 92); g.FillRectangle(Brushes.WhiteSmoke, 348, 288, 160, 92); });
                else if (mode == 0 || mode == 3)
                    Bottom(g, U.CommandMenu, () => { g.FillRectangle(Brushes.WhiteSmoke, 4, 288, 260, 92); g.FillRectangle(Brushes.WhiteSmoke, 268, 288, 240, 92); });
                else
                    Bottom(g, U.BattleMessage, () => g.FillRectangle(Brushes.WhiteSmoke, 4, 288, 504, 92));
                using (var f = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    var dark = Brushes.DimGray;
                    if (mode == 0 || mode == 3)
                    {
                        g.DrawString("What will", f, dark, 24, 300); g.DrawString((dbl ? "Pikachu" : "Pikachu") + " do?", f, dark, 24, 332);
                        string[] cmds = { "FIGHT", "BAG", "POKéMON", "RUN" };
                        for (int i = 0; i < 4; i++) { int cx = 296 + (i % 2) * 110, cy = 298 + (i / 2) * 36; if (i == 0) Arrow(g, cx - 18, cy + 4); g.DrawString(cmds[i], f, dark, cx, cy); }
                    }
                    else if (mode == 1)
                    {
                        string[] mv = { "Thunderbolt", "Quick Attack", "Iron Tail", "Thunder Wave" };
                        for (int i = 0; i < 4; i++) { int cx = 30 + (i % 2) * 160, cy = 298 + (i / 2) * 36; if (i == 0) Arrow(g, cx - 18, cy + 4); g.DrawString(mv[i], f, dark, cx, cy); }
                        g.DrawString("PP 15/15", f, dark, 366, 298); g.DrawString("TYPE/Electric", f, dark, 366, 332);
                    }
                    else g.DrawString(mode == 4 ? "Youngster Joey would like to battle!" : "Pikachu used Thunderbolt!", f, dark, 26, 300);
                }
            }
            var old = _preview.Image; _preview.Image = bmp; if (old != null) old.Dispose();
        }

        void DrawBalls(Graphics g, int y, bool foe)
        {
            var ok = Img(U.BallOk);
            int s = S, bw = ok != null ? Math.Max(22, ok.Width * s + 4) : 22, w = 6 * bw + 24, ph = ok != null ? Math.Max(28, ok.Height * s + 8) : 28;
            int x = foe ? 0 : 512 - w;
            using (var b = new SolidBrush(Color.FromArgb(200, 48, 48, 56))) g.FillRectangle(b, x, y, w, ph);
            string[] states = { "ok", "ok", "status", "fainted", "empty", "empty" };
            for (int i = 0; i < 6; i++)
            {
                int bx = x + 12 + i * bw + bw / 2, by = y + ph / 2;
                var t = Img(states[i] == "ok" ? U.BallOk : states[i] == "status" ? U.BallStatus : states[i] == "fainted" ? U.BallFainted : U.BallEmpty) ?? (states[i] != "empty" ? ok : null);
                if (t != null) Pic(g, t, bx - t.Width * s / 2, by - t.Height * s / 2);
                else g.FillEllipse(states[i] == "empty" ? Brushes.Gray : states[i] == "fainted" ? Brushes.DarkGray : states[i] == "status" ? Brushes.Orange : Brushes.Red, bx - 6, by - 6, 12, 12);
            }
        }
    }
}
