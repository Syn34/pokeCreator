using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Studio.Controls;

namespace PokeCreator.Studio.Editors
{
    /// <summary>Studio look and feel: light/dark mode, accent colour, tree guides, icons, font size.</summary>
    public class AppearanceEditor : UserControl, IDocument
    {
        public static readonly string[][] Accents =
        {
            new[] { "Poké Ball Red", "200,48,48" }, new[] { "Ocean Blue", "40,110,210" }, new[] { "Leaf Green", "46,150,70" },
            new[] { "Psychic Purple", "140,70,200" }, new[] { "Ember Orange", "225,110,30" }, new[] { "Electric Yellow", "200,160,0" },
            new[] { "Fairy Pink", "215,80,150" }, new[] { "Steel Gray", "100,110,125" },
        };

        readonly Settings _settings;
        readonly Action<Settings> _apply;
        public string Title { get { return "Appearance"; } }
        public bool Dirty { get { return false; } }
        public void Save() { }

        public AppearanceEditor(Settings settings, Action<Settings> apply)
        {
            _settings = settings; _apply = apply;
            Dock = DockStyle.Fill; AutoScroll = true;
            var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(24, 16, 24, 40), Location = new Point(0, 0) };
            Controls.Add(body);

            body.Controls.Add(new Label { Text = "Appearance", Font = Theme.TitleFont, AutoSize = true });
            body.Controls.Add(new Label { Text = "Changes apply right away and are remembered next time you open the Studio.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 0, 3, 12) });

            // Theme
            body.Controls.Add(Heading("Theme"));
            var themes = new FlowLayoutPanel { AutoSize = true };
            themes.Controls.Add(ThemeCard("Light", false));
            themes.Controls.Add(ThemeCard("Dark", true));
            body.Controls.Add(themes);

            // Accent
            body.Controls.Add(Heading("Accent colour"));
            body.Controls.Add(new Label { Text = "Used for headings, the selected tab and the selected item in the project tree.", AutoSize = true, ForeColor = Color.DimGray });
            var accents = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(760, 0) };
            foreach (var a in Accents) accents.Controls.Add(AccentSwatch(a[0], ParseColor(a[1])));
            body.Controls.Add(accents);

            // Tree
            body.Controls.Add(Heading("Project tree"));
            var guides = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
            guides.Controls.Add(GuideOption("Rainbow guides: each nesting level gets its own colour (like code editors)", GuideStyle.Rainbow));
            guides.Controls.Add(GuideOption("Section colours: each part of the project (Database, Maps, Scripts...) has its own colour", GuideStyle.Section));
            guides.Controls.Add(GuideOption("Plain guides", GuideStyle.Plain));
            body.Controls.Add(guides);
            var icons = new CheckBox { Text = "Show icons (houses for towns, signposts for routes, Poké Balls for Pokémon...)", AutoSize = true, Checked = _settings.ShowIcons, Margin = new Padding(3, 8, 3, 3) };
            icons.CheckedChanged += (s, e) => { _settings.ShowIcons = icons.Checked; _apply(_settings); };
            body.Controls.Add(icons);
            body.Controls.Add(new Label { Text = "Tip: right-click a map in the tree and choose \"Change Icon…\" to pick its icon yourself.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 4, 3, 3) });

            // Font size
            body.Controls.Add(Heading("Text size"));
            var size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = new Padding(3, 3, 3, 24) };
            int[] sizes = { 8, 9, 10, 11, 12 };
            foreach (var z in sizes) size.Items.Add(z == 9 ? "9 (default)" : z.ToString());
            size.SelectedIndex = Math.Max(0, Array.IndexOf(sizes, _settings.FontSize));
            size.SelectedIndexChanged += (s, e) => { _settings.FontSize = sizes[size.SelectedIndex]; _apply(_settings); };
            body.Controls.Add(size);
        }

        static Label Heading(string t) { return new Label { Text = t, Font = Theme.HeaderFont, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(3, 18, 3, 6) }; }

        public static Color ParseColor(string s)
        {
            var p = (s ?? "").Split(',').Select(x => { int n; return int.TryParse(x.Trim(), out n) ? n : 0; }).ToArray();
            return p.Length == 3 ? Color.FromArgb(p[0], p[1], p[2]) : Color.FromArgb(200, 48, 48);
        }

        Control ThemeCard(string name, bool dark)
        {
            var card = new Panel { Width = 230, Height = 150, Margin = new Padding(3, 3, 16, 3), Cursor = Cursors.Hand };
            Theme.Keep(card);
            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                Color win = dark ? Color.FromArgb(30, 31, 34) : Color.White, side = dark ? Color.FromArgb(27, 28, 31) : Color.FromArgb(240, 242, 246),
                      bar = dark ? Color.FromArgb(34, 35, 39) : Color.FromArgb(236, 238, 242), txt = dark ? Color.FromArgb(200, 200, 210) : Color.FromArgb(60, 60, 70);
                g.Clear(win);
                using (var b = new SolidBrush(bar)) g.FillRectangle(b, 0, 0, card.Width, 20);
                using (var b = new SolidBrush(side)) g.FillRectangle(b, 0, 20, 70, card.Height - 50);
                var pal = new[] { Color.FromArgb(214, 160, 0), Color.FromArgb(46, 160, 67), Color.FromArgb(150, 80, 210) };
                for (int i = 0; i < 5; i++)
                {
                    int y = 28 + i * 14, lvl = i == 0 ? 0 : i < 3 ? 1 : 2;
                    for (int l = 0; l < lvl; l++) using (var p = new Pen(pal[l], 1.5f)) g.DrawLine(p, 8 + l * 10, y - 3, 8 + l * 10, y + 11);
                    g.DrawImage(Icons.Get(new[] { "database", "species", "moves", "maps", "town" }[i]), 12 + lvl * 10, y, 12, 12);
                    using (var b = new SolidBrush(txt)) g.FillRectangle(b, 27 + lvl * 10, y + 4, 30 - lvl * 10, 4);
                }
                using (var b = new SolidBrush(Theme.AccentColor)) g.FillRectangle(b, 80, 28, 60, 6);
                using (var b = new SolidBrush(txt)) { g.FillRectangle(b, 80, 44, 120, 4); g.FillRectangle(b, 80, 56, 90, 4); g.FillRectangle(b, 80, 68, 110, 4); }
                bool selected = _settings.DarkMode == dark;
                using (var b = new SolidBrush(selected ? Theme.AccentColor : Color.FromArgb(150, 150, 160)))
                    g.FillRectangle(b, 0, card.Height - 30, card.Width, 30);
                TextRenderer.DrawText(g, name + (selected ? "  ✓" : ""), new Font("Segoe UI", 10f, FontStyle.Bold), new Rectangle(0, card.Height - 30, card.Width, 30), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using (var p = new Pen(selected ? Theme.AccentColor : Color.FromArgb(120, 120, 130), selected ? 3 : 1)) g.DrawRectangle(p, 1, 1, card.Width - 3, card.Height - 3);
            };
            card.Click += (s, e) => { _settings.DarkMode = dark; _apply(_settings); foreach (Control c in card.Parent.Controls) c.Invalidate(); };
            return card;
        }

        Control AccentSwatch(string name, Color c)
        {
            var b = new Button { Text = name, Width = 175, Height = 34, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(30, 0, 0, 0), Margin = new Padding(3, 3, 8, 3) };
            b.Paint += (s, e) =>
            {
                using (var br = new SolidBrush(c)) e.Graphics.FillEllipse(br, 8, 9, 16, 16);
                if (AppearanceEditor.ParseColor(_settings.Accent).ToArgb() == c.ToArgb())
                    using (var p = new Pen(c, 3)) e.Graphics.DrawRectangle(p, 1, 1, b.Width - 3, b.Height - 3);
            };
            b.Click += (s, e) => { _settings.Accent = c.R + "," + c.G + "," + c.B; _apply(_settings); Invalidate(true); };
            return b;
        }

        Control GuideOption(string text, GuideStyle style)
        {
            var rb = new RadioButton { Text = text, AutoSize = true, Checked = _settings.Guides == style.ToString() };
            rb.CheckedChanged += (s, e) => { if (rb.Checked) { _settings.Guides = style.ToString(); _apply(_settings); } };
            return rb;
        }
    }
}
