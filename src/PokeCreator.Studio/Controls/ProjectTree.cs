using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PokeCreator.Studio.Controls
{
    /// <summary>
    /// The project explorer: a TreeView drawn by hand with icons, coloured indent guides
    /// (like code editors), chevrons, item counts and hover highlighting.
    /// Set a node's ImageKey to an <see cref="Icons"/> key.
    /// </summary>
    public class ProjectTree : TreeView
    {
        TreeNode _hover;
        const int Pad = 6, Chevron = 12, IconGap = 4;

        public ProjectTree()
        {
            DrawMode = TreeViewDrawMode.OwnerDrawAll;
            ShowLines = false; ShowPlusMinus = false; ShowRootLines = false;
            FullRowSelect = true; HideSelection = false;
            Indent = 16; ItemHeight = 24;
            BorderStyle = BorderStyle.None;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Theme.Changed += () => { BackColor = Theme.Sidebar; ForeColor = Theme.Text; Invalidate(); };
        }

        int ChevronX(TreeNode n) { return Pad + n.Level * Indent; }

        protected override void OnDrawNode(DrawTreeNodeEventArgs e)
        {
            var n = e.Node;
            if (n == null || e.Bounds.Height <= 0) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var row = new Rectangle(0, e.Bounds.Y, Math.Max(ClientSize.Width, e.Bounds.Right), ItemHeight);

            Color bg = n == SelectedNode ? Theme.Selection : n == _hover ? Theme.Hover : Theme.Sidebar;
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, row);
            if (n == SelectedNode) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, row.Y, 3, row.Height);

            // Root section index (for "colour by section" guides)
            var root = n; while (root.Parent != null) root = root.Parent;
            int section = root.Index;

            // Indent guides: one vertical line per ancestor level
            g.SmoothingMode = SmoothingMode.None;
            for (int level = 0; level < n.Level; level++)
            {
                int x = Pad + level * Indent + Chevron / 2;
                using (var p = new Pen(Theme.GuideColor(level, section), Theme.Guides == GuideStyle.Plain ? 1f : 1.6f)) g.DrawLine(p, x, row.Y, x, row.Bottom);
            }
            // Short connector into this node
            if (n.Level > 0)
            {
                int x = Pad + (n.Level - 1) * Indent + Chevron / 2;
                using (var p = new Pen(Theme.GuideColor(n.Level - 1, section), Theme.Guides == GuideStyle.Plain ? 1f : 1.6f)) g.DrawLine(p, x, row.Y + row.Height / 2, ChevronX(n) - 1, row.Y + row.Height / 2);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int cx = ChevronX(n);
            if (n.Nodes.Count > 0)
            {
                int cy = row.Y + row.Height / 2;
                using (var p = new Pen(Theme.SubText, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    if (n.IsExpanded) g.DrawLines(p, new[] { new Point(cx + 2, cy - 2), new Point(cx + 6, cy + 2), new Point(cx + 10, cy - 2) });
                    else g.DrawLines(p, new[] { new Point(cx + 4, cy - 4), new Point(cx + 8, cy), new Point(cx + 4, cy + 4) });
                }
            }

            int x0 = cx + Chevron + 2;
            if (Theme.ShowIcons && !string.IsNullOrEmpty(n.ImageKey))
            {
                g.DrawImage(Icons.Get(n.ImageKey), x0, row.Y + (row.Height - Icons.Size) / 2, Icons.Size, Icons.Size);
                x0 += Icons.Size + IconGap;
            }

            // Text, with a trailing "(count)" drawn dimmer
            string text = n.Text, count = null;
            int paren = text.LastIndexOf(" (", StringComparison.Ordinal);
            if (paren > 0 && text.EndsWith(")")) { count = text.Substring(paren + 1); text = text.Substring(0, paren); }
            var font = n.Level == 0 ? new Font(Font, FontStyle.Bold) : Font;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            var textRect = new Rectangle(x0, row.Y, row.Right - x0 - 4, row.Height);
            TextRenderer.DrawText(g, text, font, textRect, n.ForeColor.IsEmpty ? Theme.Text : n.ForeColor, flags);
            if (count != null)
            {
                int w = TextRenderer.MeasureText(g, text, font, textRect.Size, flags).Width;
                TextRenderer.DrawText(g, count, Font, new Rectangle(x0 + w + 2, row.Y, Math.Max(0, row.Right - x0 - w - 6), row.Height), Theme.SubText, flags);
            }
            if (n.Level == 0) font.Dispose();
        }

        /// <summary>Raised when an item is clicked once with the left button (not on its expand arrow).</summary>
        public event Action<TreeNode> NodeClicked;

        bool OnChevron(TreeNode n, int x) { int cx = ChevronX(n); return n.Nodes.Count > 0 && x >= cx - 2 && x <= cx + Chevron + 2; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var n = GetNodeAt(e.Location);
            if (n != null && e.Button == MouseButtons.Left && OnChevron(n, e.X)) { n.Toggle(); return; }
            if (n != null) SelectedNode = n;
            base.OnMouseDown(e);
        }

        // The click notification is used rather than MouseUp: on Windows the tree control often swallows
        // the button-up of a plain click (it waits to see if a drag starts), so MouseUp isn't reliable.
        protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
        {
            base.OnNodeMouseClick(e);
            if (e.Button != MouseButtons.Left || e.Node == null || OnChevron(e.Node, e.X)) return;
            if (NodeClicked != null) NodeClicked(e.Node);
        }

        protected override void WndProc(ref Message m)
        {
            // Items open on one click, so a double-click is just two clicks (no built-in expand/collapse)
            const int WM_LBUTTONDBLCLK = 0x0203, WM_LBUTTONDOWN = 0x0201;
            if (m.Msg == WM_LBUTTONDBLCLK) m.Msg = WM_LBUTTONDOWN;
            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var n = GetNodeAt(e.Location);
            if (n != _hover)
            {
                var old = _hover; _hover = n;
                if (old != null) Invalidate(new Rectangle(0, old.Bounds.Y, ClientSize.Width, ItemHeight));
                if (n != null) Invalidate(new Rectangle(0, n.Bounds.Y, ClientSize.Width, ItemHeight));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null) { var old = _hover; _hover = null; Invalidate(new Rectangle(0, old.Bounds.Y, ClientSize.Width, ItemHeight)); }
        }

        protected override void OnAfterSelect(TreeViewEventArgs e) { base.OnAfterSelect(e); Invalidate(); }
        protected override void OnAfterExpand(TreeViewEventArgs e) { base.OnAfterExpand(e); Invalidate(); }
        protected override void OnAfterCollapse(TreeViewEventArgs e) { base.OnAfterCollapse(e); Invalidate(); }
    }
}
