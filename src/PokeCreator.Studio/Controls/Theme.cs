using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PokeCreator.Studio.Controls
{
    public enum GuideStyle { Rainbow, Section, Plain }

    /// <summary>
    /// Studio colours (light / dark) and a theming pass that recolours any control tree.
    /// Controls remember their "role" (panel, sidebar, hint text, accent heading...) the first
    /// time they're themed, so switching modes later recolours them correctly.
    /// </summary>
    public static class Theme
    {
        public static bool Dark { get; private set; }
        public static Color AccentColor = Color.FromArgb(200, 48, 48);
        public static GuideStyle Guides = GuideStyle.Rainbow;
        public static bool ShowIcons = true;
        public static int Version { get; private set; }
        public static event Action Changed;

        // ------------------------------------------------------------------ palette
        public static Color Back { get { return Dark ? Color.FromArgb(30, 31, 34) : Color.White; } }
        public static Color Panel { get { return Dark ? Color.FromArgb(37, 38, 42) : Color.FromArgb(250, 250, 252); } }
        public static Color Sidebar { get { return Dark ? Color.FromArgb(27, 28, 31) : Color.FromArgb(240, 242, 246); } }
        public static Color Input { get { return Dark ? Color.FromArgb(48, 50, 56) : Color.White; } }
        public static Color ButtonBack { get { return Dark ? Color.FromArgb(56, 58, 66) : Color.FromArgb(246, 246, 250); } }
        public static Color Border { get { return Dark ? Color.FromArgb(70, 72, 80) : Color.Gainsboro; } }
        public static Color Text { get { return Dark ? Color.FromArgb(222, 224, 230) : Color.FromArgb(30, 30, 34); } }
        public static Color SubText { get { return Dark ? Color.FromArgb(140, 144, 154) : Color.DimGray; } }
        public static Color Selection { get { return Dark ? Color.FromArgb(55, 70, 105) : Color.FromArgb(204, 224, 255); } }
        public static Color Hover { get { return Dark ? Color.FromArgb(44, 46, 52) : Color.FromArgb(232, 236, 244); } }
        public static Color Bar { get { return Dark ? Color.FromArgb(34, 35, 39) : Color.FromArgb(236, 238, 242); } }
        public static Color Preview { get { return Dark ? Color.FromArgb(44, 46, 52) : Color.White; } }
        public static Color Go { get { return Dark ? Color.FromArgb(120, 220, 130) : Color.DarkGreen; } }
        public static Color Accent { get { return Dark ? ControlPaint.Light(AccentColor, 0.35f) : AccentColor; } }

        public static readonly Font HeaderFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        public static readonly Font TitleFont = new Font("Segoe UI", 14f, FontStyle.Bold);
        public static readonly Font Mono = new Font(FontFamily.GenericMonospace, 10f);

        static readonly Color[] RainbowLight = { Color.FromArgb(214, 160, 0), Color.FromArgb(46, 160, 67), Color.FromArgb(150, 80, 210), Color.FromArgb(0, 150, 170), Color.FromArgb(220, 80, 60), Color.FromArgb(50, 110, 220) };
        static readonly Color[] RainbowDark = { Color.FromArgb(255, 215, 90), Color.FromArgb(120, 220, 130), Color.FromArgb(200, 150, 255), Color.FromArgb(90, 210, 230), Color.FromArgb(255, 140, 120), Color.FromArgb(120, 170, 255) };

        /// <summary>Colour of an indent guide at a given depth, inside a given top-level section.</summary>
        public static Color GuideColor(int depth, int section)
        {
            var pal = Dark ? RainbowDark : RainbowLight;
            switch (Guides)
            {
                case GuideStyle.Rainbow: return pal[depth % pal.Length];
                case GuideStyle.Section:
                    {
                        var c = pal[section % pal.Length];
                        return depth % 2 == 0 ? c : Color.FromArgb(Dark ? 150 : 170, c);
                    }
                default: return Border;
            }
        }

        public static void Set(bool dark, Color accent, GuideStyle guides, bool icons)
        {
            Dark = dark; AccentColor = accent; Guides = guides; ShowIcons = icons;
            Version++;
            foreach (Form f in Application.OpenForms) Apply(f);
            if (Changed != null) Changed();
        }

        // ------------------------------------------------------------------ roles
        enum Role { Normal, Sidebar, Panel, Keep, Preview, Bar, Hint, Accent, Go, Big }
        class Info { public Role Back = Role.Normal, Fore = Role.Normal; public int Version = -1; public bool Hooked; }
        static readonly ConditionalWeakTable<object, Info> _info = new ConditionalWeakTable<object, Info>();
        static readonly HashSet<Color> KeepColors = new HashSet<Color> { Color.FromArgb(60, 62, 70) };

        /// <summary>Exclude a control (e.g. the map canvas) from theming.</summary>
        public static void Keep(Control c) { _info.GetOrCreateValue(c).Back = Role.Keep; _info.GetOrCreateValue(c).Fore = Role.Keep; }

        static bool Same(Color a, Color b) { return a.ToArgb() == b.ToArgb(); }

        static Info Classify(Control c)
        {
            Info i;
            if (_info.TryGetValue(c, out i) && i.Version >= 0) return i;
            if (i == null) i = _info.GetOrCreateValue(c);
            if (i.Back != Role.Keep)
            {
                var b = c.BackColor;
                if (KeepColors.Contains(b) || b.ToArgb() == Color.Black.ToArgb() && !(c is Label)) i.Back = Role.Keep;
                else if (Same(b, Color.FromArgb(240, 242, 246)) || Same(b, Color.FromArgb(27, 28, 31))) i.Back = Role.Sidebar;
                else if (Same(b, Color.FromArgb(236, 238, 242)) || Same(b, Color.FromArgb(34, 35, 39))) i.Back = Role.Bar;
                else if (c is PictureBox && (Same(b, Color.White) || Same(b, Color.FromArgb(230, 230, 235)))) i.Back = Role.Preview;
                else if (c is Button && Same(b, Color.FromArgb(246, 246, 250))) i.Back = Role.Big;
                else if (Same(b, Color.FromArgb(250, 250, 252)) || Same(b, Color.FromArgb(37, 38, 42))) i.Back = Role.Panel;
                var f = c.ForeColor;
                if (Same(f, AccentColor) || Same(f, Color.FromArgb(200, 48, 48)) || Same(f, Accent)) i.Fore = Role.Accent;
                else if (Same(f, Color.DimGray) || Same(f, Color.Gray)) i.Fore = Role.Hint;
                else if (Same(f, Color.DarkGreen)) i.Fore = Role.Go;
            }
            return i;
        }

        static Color ForeFor(Role r)
        {
            switch (r) { case Role.Hint: return SubText; case Role.Accent: return Accent; case Role.Go: return Go; }
            return Text;
        }

        static Color BackFor(Role r, Control c)
        {
            switch (r)
            {
                case Role.Sidebar: return Sidebar;
                case Role.Panel: return Panel;
                case Role.Preview: return Preview;
                case Role.Bar: return Bar;
                case Role.Big: return ButtonBack;
            }
            if (c is TextBoxBase || c is ComboBox || c is ListBox || c is NumericUpDown || c is TreeView || c is ListView || c is UpDownBase) return Input;
            if (c is Button) return ButtonBack;
            return Back;
        }

        /// <summary>Themes a window that hasn't been themed for the current settings yet (used for dialogs).</summary>
        public static void ApplyIfNeeded(Control c)
        {
            Info i;
            if (_info.TryGetValue(c, out i) && i.Version == Version) return;
            Apply(c);
        }

        /// <summary>Recolours a control and all of its children; new children are themed when added.</summary>
        public static void Apply(Control c)
        {
            if (c == null) return;
            var info = Classify(c);
            if (info.Back != Role.Keep)
            {
                if (!(c is Label) || c.BackColor != Color.Transparent) c.BackColor = c is Label && info.Back == Role.Normal ? Color.Transparent : BackFor(info.Back, c);
                c.ForeColor = ForeFor(info.Fore);
                StyleSpecial(c);
            }
            info.Version = Version;
            if (!info.Hooked)
            {
                info.Hooked = true;
                c.ControlAdded += (s, e) => Apply(e.Control);
                if (c is Form) ((Form)c).HandleCreated += (s, e) => NativeDark((Form)c);
            }
            foreach (Control child in c.Controls) Apply(child);
        }

        static void StyleSpecial(Control c)
        {
            if (c is Form) NativeDark((Form)c);
            var ts = c as ToolStrip;
            if (ts != null)
            {
                ts.Renderer = new ToolStripProfessionalRenderer(new Colors()) { RoundedEdges = false };
                foreach (ToolStripItem it in ts.Items) StyleItem(it);
            }
            var btn = c as Button;
            if (btn != null) { btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderColor = Border; btn.UseVisualStyleBackColor = false; }
            var combo = c as ComboBox;
            if (combo != null) combo.FlatStyle = Dark ? FlatStyle.Flat : FlatStyle.Standard;
            var grid = c as DataGridView;
            if (grid != null)
            {
                grid.EnableHeadersVisualStyles = false;
                grid.BackgroundColor = Panel; grid.GridColor = Border;
                grid.DefaultCellStyle.BackColor = Input; grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = Selection; grid.DefaultCellStyle.SelectionForeColor = Text;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Bar; grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                grid.RowHeadersDefaultCellStyle.BackColor = Bar; grid.RowHeadersDefaultCellStyle.ForeColor = Text;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            }
            var tabs = c as TabControl;
            if (tabs != null && tabs.DrawMode != TabDrawMode.OwnerDrawFixed)
            {
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.SizeMode = TabSizeMode.Normal;
                tabs.DrawItem += DrawTab;
            }
            if (c is TabPage) ((TabPage)c).UseVisualStyleBackColor = false;
            var link = c as LinkLabel;
            if (link != null) { link.LinkColor = Dark ? Color.FromArgb(120, 170, 255) : Color.FromArgb(0, 102, 204); link.ActiveLinkColor = link.LinkColor; }
            if (c is TreeView || c is ListBox || c is TextBoxBase || c is ListView || c is ScrollableControl) NativeScrollbars(c);
        }

        static void StyleItem(ToolStripItem it)
        {
            Info i = _info.GetOrCreateValue(it);
            if (i.Version < 0)
            {
                if (Same(it.ForeColor, Color.DarkGreen)) i.Fore = Role.Go;
                i.Version = 0;
            }
            it.ForeColor = ForeFor(i.Fore);
            var menu = it as ToolStripMenuItem;
            if (menu != null) foreach (ToolStripItem sub in menu.DropDownItems) StyleItem(sub);
            var dd = it as ToolStripDropDownItem;
            if (dd != null && dd.DropDown != null) { dd.DropDown.BackColor = Panel; dd.DropDown.ForeColor = Text; dd.DropDown.Renderer = new ToolStripProfessionalRenderer(new Colors()) { RoundedEdges = false }; }
            var cb = it as ToolStripComboBox;
            if (cb != null) { cb.BackColor = Input; cb.ForeColor = Text; cb.FlatStyle = Dark ? FlatStyle.Flat : FlatStyle.Standard; }
        }

        static void DrawTab(object sender, DrawItemEventArgs e)
        {
            var tabs = (TabControl)sender;
            if (e.Index < 0 || e.Index >= tabs.TabCount) return;
            bool sel = e.Index == tabs.SelectedIndex;
            var r = e.Bounds;
            if (e.Index == tabs.TabCount - 1)
            {
                // Fill the empty strip to the right of the tabs (WinForms has no property for it)
                var last = tabs.GetTabRect(e.Index);
                using (var b = new SolidBrush(Back))
                {
                    e.Graphics.FillRectangle(b, last.Right, 0, Math.Max(0, tabs.Width - last.Right), last.Bottom + 2);
                    e.Graphics.FillRectangle(b, 0, 0, Math.Max(0, tabs.GetTabRect(0).Left), last.Bottom + 2);
                }
            }
            using (var b = new SolidBrush(sel ? Panel : Bar)) e.Graphics.FillRectangle(b, r);
            if (sel) using (var p = new SolidBrush(Accent)) e.Graphics.FillRectangle(p, r.X, r.Bottom - 3, r.Width, 3);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, r, sel ? Text : SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        // ------------------------------------------------------------------ Windows 10/11 dark title bars and scrollbars
        static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        static void NativeDark(Form f)
        {
            if (!IsWindows || !f.IsHandleCreated) return;
            try
            {
                int on = Dark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            }
            catch { }
        }

        static void NativeScrollbars(Control c)
        {
            if (!IsWindows) return;
            try
            {
                if (c.IsHandleCreated) SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null);
                else c.HandleCreated += (s, e) => { try { SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { } };
            }
            catch { }
        }

        /// <summary>Menu and toolbar colours.</summary>
        class Colors : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin { get { return Bar; } }
            public override Color ToolStripGradientMiddle { get { return Bar; } }
            public override Color ToolStripGradientEnd { get { return Bar; } }
            public override Color ToolStripBorder { get { return Bar; } }
            public override Color MenuStripGradientBegin { get { return Bar; } }
            public override Color MenuStripGradientEnd { get { return Bar; } }
            public override Color StatusStripGradientBegin { get { return Bar; } }
            public override Color StatusStripGradientEnd { get { return Bar; } }
            public override Color ToolStripDropDownBackground { get { return Panel; } }
            public override Color ImageMarginGradientBegin { get { return Panel; } }
            public override Color ImageMarginGradientMiddle { get { return Panel; } }
            public override Color ImageMarginGradientEnd { get { return Panel; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Selection; } }
            public override Color MenuItemSelected { get { return Selection; } }
            public override Color MenuItemSelectedGradientBegin { get { return Selection; } }
            public override Color MenuItemSelectedGradientEnd { get { return Selection; } }
            public override Color MenuItemPressedGradientBegin { get { return Selection; } }
            public override Color MenuItemPressedGradientEnd { get { return Selection; } }
            public override Color ButtonSelectedHighlight { get { return Hover; } }
            public override Color ButtonSelectedGradientBegin { get { return Hover; } }
            public override Color ButtonSelectedGradientMiddle { get { return Hover; } }
            public override Color ButtonSelectedGradientEnd { get { return Hover; } }
            public override Color ButtonSelectedBorder { get { return Border; } }
            public override Color ButtonCheckedGradientBegin { get { return Selection; } }
            public override Color ButtonCheckedGradientMiddle { get { return Selection; } }
            public override Color ButtonCheckedGradientEnd { get { return Selection; } }
            public override Color ButtonPressedGradientBegin { get { return Selection; } }
            public override Color ButtonPressedGradientMiddle { get { return Selection; } }
            public override Color ButtonPressedGradientEnd { get { return Selection; } }
            public override Color CheckBackground { get { return Selection; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Bar; } }
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Loads an image without locking the file (so it can be replaced while the Studio runs).</summary>
        public static Image LoadImage(string path)
        {
            if (path == null || !File.Exists(path)) return null;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var img = Image.FromStream(fs))
                    return new Bitmap(img);
            }
            catch { return null; }
        }
    }
}
