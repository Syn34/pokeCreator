using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PokeCreator.Studio.Controls
{
    /// <summary>
    /// Document tab strip with a close (×) button on every tab, hover feedback,
    /// middle-click to close and a right-click menu (Close / Close Others / Close All).
    /// </summary>
    public class DocumentTabs : TabControl
    {
        public event Action<TabPage> CloseRequested;
        int _hoverClose = -1;
        const int CloseSize = 14;

        public DocumentTabs()
        {
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Normal;
            Padding = new Point(22, 5);   // room for the × on the right
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            Theme.Changed += Invalidate;
        }

        Rectangle CloseRect(int index)
        {
            var r = GetTabRect(index);
            return new Rectangle(r.Right - CloseSize - 6, r.Top + (r.Height - CloseSize) / 2, CloseSize, CloseSize);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= TabCount) return;
            var g = e.Graphics;
            var r = GetTabRect(e.Index);
            bool sel = e.Index == SelectedIndex;

            if (e.Index == TabCount - 1)
                using (var b = new SolidBrush(Theme.Back))
                {
                    g.FillRectangle(b, r.Right, 0, Math.Max(0, Width - r.Right), r.Bottom + 2);
                    g.FillRectangle(b, 0, 0, Math.Max(0, GetTabRect(0).Left), r.Bottom + 2);
                }

            using (var b = new SolidBrush(sel ? Theme.Panel : Theme.Bar)) g.FillRectangle(b, r);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X, r.Bottom - 3, r.Width, 3);

            var textRect = new Rectangle(r.X + 8, r.Y, r.Width - CloseSize - 16, r.Height);
            TextRenderer.DrawText(g, TabPages[e.Index].Text.Trim(), Font, textRect, sel ? Theme.Text : Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var cr = CloseRect(e.Index);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (e.Index == _hoverClose)
                using (var b = new SolidBrush(Theme.Dark ? Color.FromArgb(200, 70, 70) : Color.FromArgb(232, 80, 80))) g.FillEllipse(b, cr);
            var xColor = e.Index == _hoverClose ? Color.White : (sel ? Theme.Text : Theme.SubText);
            using (var p = new Pen(xColor, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                int m = 4;
                g.DrawLine(p, cr.Left + m, cr.Top + m, cr.Right - m, cr.Bottom - m);
                g.DrawLine(p, cr.Right - m, cr.Top + m, cr.Left + m, cr.Bottom - m);
            }
        }

        int TabAt(Point p)
        {
            for (int i = 0; i < TabCount; i++) if (GetTabRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = TabAt(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left && CloseRect(i).Contains(e.Location)) { Request(TabPages[i]); return; }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = TabAt(e.Location);
            if (i < 0) return;
            if (e.Button == MouseButtons.Middle) { Request(TabPages[i]); return; }
            if (e.Button == MouseButtons.Right)
            {
                var tab = TabPages[i];
                var menu = new ContextMenuStrip();
                menu.Items.Add("Close", null, (s, a) => Request(tab));
                menu.Items.Add("Close Others", null, (s, a) => { foreach (var t in TabPages.Cast<TabPage>().Where(t => t != tab).ToList()) Request(t); });
                menu.Items.Add("Close All", null, (s, a) => { foreach (var t in TabPages.Cast<TabPage>().ToList()) Request(t); });
                Theme.Apply(menu);
                menu.Show(this, e.Location);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = TabAt(e.Location);
            int hover = i >= 0 && CloseRect(i).Contains(e.Location) ? i : -1;
            if (hover != _hoverClose) { _hoverClose = hover; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_hoverClose != -1) { _hoverClose = -1; Invalidate(); } }

        void Request(TabPage t) { if (CloseRequested != null) CloseRequested(t); }
    }
}
