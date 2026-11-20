using System;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.UI
{
    /// <summary>
    /// A window frame image drawn as a 9-slice: Essentials speech/choice skins (e.g. "speech hgss 1", "choice 1")
    /// or RMXP-format windowskins (192×128: background on the left, frame at the top right).
    /// For Essentials skins the stretched middle is found from the fill colour at the image's centre, so frames with
    /// thick or uneven borders keep their look at any size.
    /// </summary>
    public class WindowSkin
    {
        public Texture Tex;
        public bool Rmxp;
        // Centre slice (stretched); everything around it is the border
        public int CX, CY, CW, CH;
        /// <summary>Where the fill area starts inside the frame (for text padding).</summary>
        public int InnerLeft, InnerTop, InnerRight, InnerBottom;

        public static WindowSkin Load(Gfx g, string path)
        {
            var tex = g.Load(path);
            if (tex == null) return null;
            var s = new WindowSkin { Tex = tex };
            if (tex.Width == 192 && tex.Height == 128) { s.Rmxp = true; s.CX = 16; s.CY = 16; s.CW = 32; s.CH = 32; s.InnerLeft = s.InnerTop = s.InnerRight = s.InnerBottom = 16; return s; }
            var px = g.ReadPixels(path);
            int w = tex.Width, h = tex.Height;
            // Default: thirds
            s.CX = w / 3; s.CY = h / 3; s.CW = Math.Max(1, w - 2 * s.CX); s.CH = Math.Max(1, h - 2 * s.CY);
            s.InnerLeft = s.CX; s.InnerTop = s.CY; s.InnerRight = w - s.CX - s.CW; s.InnerBottom = h - s.CY - s.CH;
            if (px != null)
            {
                int mx = w / 2, my = h / 2;
                uint fill = px[my, mx];
                // The fill run through the centre, horizontally and vertically; then the middle of that run
                int x0 = mx, x1 = mx, y0 = my, y1 = my;
                while (x0 > 0 && px[my, x0 - 1] == fill) x0--;
                while (x1 < w - 1 && px[my, x1 + 1] == fill) x1++;
                while (y0 > 0 && px[y0 - 1, mx] == fill) y0--;
                while (y1 < h - 1 && px[y1 + 1, mx] == fill) y1++;
                // Prefer the fill area's own centre (some skins place it off-centre)
                int fx = (x0 + x1) / 2, fy = (y0 + y1) / 2;
                int ax0 = fx, ax1 = fx, ay0 = fy, ay1 = fy;
                while (ax0 > 0 && px[fy, ax0 - 1] == fill) ax0--;
                while (ax1 < w - 1 && px[fy, ax1 + 1] == fill) ax1++;
                while (ay0 > 0 && px[ay0 - 1, fx] == fill) ay0--;
                while (ay1 < h - 1 && px[ay1 + 1, fx] == fill) ay1++;
                if (ax1 - ax0 >= 2 && ay1 - ay0 >= 2)
                {
                    int cw = Math.Max(2, (ax1 - ax0 + 1) / 4), ch = Math.Max(2, (ay1 - ay0 + 1) / 4);
                    s.CX = (ax0 + ax1 + 1) / 2 - cw / 2; s.CW = cw;
                    s.CY = (ay0 + ay1 + 1) / 2 - ch / 2; s.CH = ch;
                    s.InnerLeft = ax0; s.InnerTop = ay0; s.InnerRight = w - 1 - ax1; s.InnerBottom = h - 1 - ay1;
                }
            }
            return s;
        }

        public void Draw(Gfx g, int x, int y, int w, int h)
        {
            if (Rmxp)
            {
                // Background stretched, then the 64×64 frame as a 9-slice with 16-pixel corners
                g.Draw(Tex, 0, 0, 128, 128, x + 2, y + 2, Math.Max(0, w - 4), Math.Max(0, h - 4));
                Slice(g, 128, 0, 64, 64, 16, 16, 32, 32, x, y, w, h);
                return;
            }
            Slice(g, 0, 0, Tex.Width, Tex.Height, CX, CY, CW, CH, x, y, w, h);
        }

        void Slice(Gfx g, int sx, int sy, int sw, int sh, int cx, int cy, int cw, int ch, int x, int y, int w, int h)
        {
            int l = cx, t = cy, r = sw - cx - cw, b = sh - cy - ch;
            // Small windows: shrink the borders proportionally
            if (l + r > w) { double k = w / (double)Math.Max(1, l + r); l = (int)(l * k); r = w - l; }
            if (t + b > h) { double k = h / (double)Math.Max(1, t + b); t = (int)(t * k); b = h - t; }
            int mw = Math.Max(0, w - l - r), mh = Math.Max(0, h - t - b);
            int srcR = sx + sw - (sw - cx - cw), srcB = sy + sh - (sh - cy - ch);
            int rw = sw - cx - cw, bh = sh - cy - ch;
            // corners
            g.Draw(Tex, sx, sy, cx, cy, x, y, l, t);
            g.Draw(Tex, srcR, sy, rw, cy, x + l + mw, y, r, t);
            g.Draw(Tex, sx, srcB, cx, bh, x, y + t + mh, l, b);
            g.Draw(Tex, srcR, srcB, rw, bh, x + l + mw, y + t + mh, r, b);
            // edges
            if (mw > 0) { g.Draw(Tex, sx + cx, sy, cw, cy, x + l, y, mw, t); g.Draw(Tex, sx + cx, srcB, cw, bh, x + l, y + t + mh, mw, b); }
            if (mh > 0) { g.Draw(Tex, sx, sy + cy, cx, ch, x, y + t, l, mh); g.Draw(Tex, srcR, sy + cy, rw, ch, x + l + mw, y + t, r, mh); }
            // middle
            if (mw > 0 && mh > 0) g.Draw(Tex, sx + cx, sy + cy, cw, ch, x + l, y + t, mw, mh);
        }
    }
}
