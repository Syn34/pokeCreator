using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PokeCreator.Studio.Controls
{
    /// <summary>A control that reacts to the mouse wheel itself (scrolls its panel, zooms...).</summary>
    public interface IWheelTarget { void Wheel(int delta); }

    /// <summary>
    /// The mouse wheel scrolls whatever is under the mouse, not just the control that has focus
    /// (Windows normally sends it to the focused control, so panels didn't scroll until clicked).
    /// </summary>
    public sealed class WheelScroll : IMessageFilter
    {
        const int WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;
        static bool _installed;
        static readonly bool Native = !(Type.GetType("Mono.Runtime") != null && Environment.OSVersion.Platform == PlatformID.Unix);

        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            Application.AddMessageFilter(new WheelScroll());
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL && m.Msg != WM_MOUSEHWHEEL) return false;
            try
            {
                var pos = Cursor.Position;
                int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                var target = ControlAt(pos);
                if (target == null) return false;

                // Our own canvases and scrolling panels: handle directly
                for (var c = target; c != null; c = c.Parent)
                {
                    var wt = c as IWheelTarget;
                    if (wt != null) { wt.Wheel(delta); return true; }
                    // Single-line text boxes and unfocused drop-downs let the page scroll, like in Visual Studio
                    var tb = c as TextBox;
                    if ((tb != null && !tb.Multiline) || ((c is ComboBox || c is NumericUpDown) && !c.ContainsFocus)) continue;
                    if (c is ListBox || c is TreeView || c is TextBoxBase || c is ListView || c is DataGridView || c is ComboBox || c is NumericUpDown) break;
                    var sc = c as ScrollableControl;
                    if (sc != null && sc.AutoScroll && sc.VerticalScroll.Visible && !(c is Form)) { Scroll(sc, delta, m.Msg == WM_MOUSEHWHEEL); return true; }
                }

                // Everything else (lists, text boxes...): send the wheel to the control under the mouse
                if (Native)
                {
                    IntPtr under = WindowFromPoint(pos);
                    if (under == IntPtr.Zero || under == m.HWnd || Control.FromChildHandle(under) == null) return false;
                    SendMessage(under, m.Msg, m.WParam, m.LParam);
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>The deepest visible control of this program under a screen point.</summary>
        public static Control ControlAt(Point screen)
        {
            if (Native)
            {
                try
                {
                    var c = Control.FromChildHandle(WindowFromPoint(screen));
                    if (c != null) return c;
                }
                catch { }
            }
            Form form = null;
            var active = Form.ActiveForm;
            if (active != null && active.Visible && active.Bounds.Contains(screen)) form = active;
            else form = Application.OpenForms.Cast<Form>().LastOrDefault(f => f.Visible && f.Bounds.Contains(screen));
            if (form == null) return null;
            Control cur = form;
            while (true)
            {
                var child = cur.GetChildAtPoint(cur.PointToClient(screen), GetChildAtPointSkip.Invisible);
                if (child == null) return cur;
                cur = child;
            }
        }

        static void Scroll(ScrollableControl sc, int delta, bool sideways)
        {
            sideways |= (Control.ModifierKeys & Keys.Shift) != 0;
            int step = delta * 96 / 120; // 3 tiles per notch
            var pos = new Point(-sc.AutoScrollPosition.X, -sc.AutoScrollPosition.Y);
            if (sideways) pos.X = Math.Max(0, pos.X - step); else pos.Y = Math.Max(0, pos.Y - step);
            sc.AutoScrollPosition = pos;
        }

        /// <summary>Scrolls the nearest scrollable panel around a control. Returns false if there is none.</summary>
        public static bool ScrollParent(Control c, int delta)
        {
            bool sideways = (Control.ModifierKeys & Keys.Shift) != 0;
            for (var p = c.Parent; p != null; p = p.Parent)
            {
                var sc = p as ScrollableControl;
                if (sc == null || !sc.AutoScroll || p is Form) continue;
                if (sideways ? !sc.HorizontalScroll.Visible : !sc.VerticalScroll.Visible) continue;
                Scroll(sc, delta, false);
                return true;
            }
            return false;
        }

        /// <summary>For OnMouseWheel overrides: scroll the parent panel and mark the event handled.</summary>
        public static void ToParent(Control c, MouseEventArgs e)
        {
            var h = e as HandledMouseEventArgs;
            if (h != null && h.Handled) return;
            if (ScrollParent(c, e.Delta) && h != null) h.Handled = true;
        }
    }
}
