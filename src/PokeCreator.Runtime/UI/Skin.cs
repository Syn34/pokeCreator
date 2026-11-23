using System;
using System.Collections.Generic;
using PokeCreator.Data;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.UI
{
    /// <summary>
    /// The project's custom UI pictures (Project.Ui, Studio: Game Settings → Appearance (Game UI)).
    /// Every helper returns false when no picture is set, so the caller draws the built-in look.
    /// </summary>
    public static class Skin
    {
        static Game _g;
        static readonly Dictionary<string, Texture> _cache = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);

        public static void Init(Game g) { _g = g; _cache.Clear(); }

        public static UiSkin U { get { return _g != null && _g.Project != null ? _g.Project.Ui ?? Empty : Empty; } }
        static readonly UiSkin Empty = new UiSkin();
        public static int S { get { return U.S; } }

        /// <summary>A picture from Graphics/UI ("Folder/name" allowed), or null.</summary>
        public static Texture Tex(string name)
        {
            if (_g == null || string.IsNullOrEmpty(name)) return null;
            Texture t;
            if (_cache.TryGetValue(name, out t)) return t;
            string path = _g.Project.FindAsset("Graphics/UI", name);
            t = path == null ? null : _g.Gfx.Load(path);
            _cache[name] = t;
            return t;
        }

        public static Texture InFolder(string folder, string name) { return string.IsNullOrEmpty(folder) ? null : Tex(folder.TrimEnd('/', '\\') + "/" + name); }

        /// <summary>Draws a picture at Picture scale.</summary>
        public static void Pic(Gfx g, Texture t, int x, int y) { if (t != null) g.Draw(t, 0, 0, t.Width, t.Height, x, y, t.Width * S, t.Height * S); }

        public static bool Pic(Gfx g, string name, int x, int y) { var t = Tex(name); if (t == null) return false; Pic(g, t, x, y); return true; }

        /// <summary>A full-screen background picture (stretched).</summary>
        public static bool Background(Gfx g, string name)
        {
            var t = Tex(name);
            if (t == null) return false;
            g.Draw(t, 0, 0, t.Width, t.Height, 0, 0, g.Width, g.Height);
            return true;
        }

        /// <summary>A picture along the bottom of the screen (battle menus): Picture scale, stretched to the screen's width if narrower.</summary>
        public static bool Bottom(Gfx g, string name)
        {
            var t = Tex(name);
            if (t == null) return false;
            int h = t.Height * S, w = Math.Max(g.Width, t.Width * S);
            g.Draw(t, 0, 0, t.Width, t.Height, (g.Width - w) / 2, g.Height - h, w, h);
            return true;
        }

        public static Color TextColor { get { var c = UiSkin.Rgb(U.TextColor); return c == null ? Ui.Dark : new Color((byte)c[0], (byte)c[1], (byte)c[2]); } }
        public static Color TextShadow { get { var c = UiSkin.Rgb(U.TextShadow); return c == null ? Ui.DarkShadow : new Color((byte)c[0], (byte)c[1], (byte)c[2]); } }

        /// <summary>Numbers with the digit pictures (or the font). Returns the width drawn.</summary>
        public static int Number(Gfx g, int x, int y, string text, bool alignRight)
        {
            var digits = new List<Texture>();
            bool pictures = !string.IsNullOrEmpty(U.DigitsFolder);
            if (pictures)
                foreach (char ch in text)
                {
                    Texture t = ch == '/' ? Tex(U.HpSlash) : ch >= '0' && ch <= '9' ? InFolder(U.DigitsFolder, "digit_" + ch) : null;
                    if (t == null) { pictures = false; break; }
                    digits.Add(t);
                }
            if (!pictures)
            {
                int tw = g.TextWidth(text);
                g.DrawText(alignRight ? x - tw : x, y, text, TextColor, TextShadow);
                return tw;
            }
            int w = 0;
            foreach (var t in digits) w += t.Width * S;
            int cx = alignRight ? x - w : x;
            foreach (var t in digits) { Pic(g, t, cx, y); cx += t.Width * S; }
            return w;
        }

        public static bool HasHpBar { get { return Tex(U.HpFrame) != null || Tex(U.HpGreen) != null; } }

        /// <summary>The custom HP bar at (x, y), w wide (0 = the frame's own width at Picture scale).</summary>
        public static bool HpBar(Gfx g, int x, int y, int w, int hp, int max)
        {
            double f = max <= 0 ? 0 : Math.Max(0, Math.Min(1, (double)hp / max));
            var fill = Tex(f > 0.5 ? U.HpGreen : f > 0.2 ? U.HpYellow : U.HpRed) ?? Tex(U.HpGreen);
            return Bar(g, x, y, w, f, Tex(U.HpFrame), Tex(U.HpEmpty), fill);
        }

        public static bool ExpBar(Gfx g, int x, int y, int w, double f)
        {
            return Bar(g, x, y, w, Math.Max(0, Math.Min(1, f)), Tex(U.ExpFrame), Tex(U.ExpEmpty), Tex(U.ExpFill));
        }

        static bool Bar(Gfx g, int x, int y, int w, double f, Texture frame, Texture empty, Texture fill)
        {
            if (frame == null && fill == null) return false;
            var shape = frame ?? fill;
            int s = S;
            if (w <= 0) w = shape.Width * s;
            double sx = (double)w / (shape.Width * s);   // stretch sideways to the width asked for
            int fh = shape.Height * s;
            if (frame != null) g.Draw(frame, 0, 0, frame.Width, frame.Height, x, y, w, fh);
            var inner = fill ?? empty;
            if (inner == null) return true;
            int ox = frame != null ? (frame.Width - inner.Width) / 2 : 0, oy = frame != null ? (frame.Height - inner.Height) / 2 : 0;
            int ix = x + (int)Math.Round(ox * s * sx), iy = y + oy * s, iw = (int)Math.Round(inner.Width * s * sx), ih = inner.Height * s;
            if (empty != null) g.Draw(empty, 0, 0, empty.Width, empty.Height, ix, iy, iw, ih);
            if (fill != null && f > 0)
            {
                int src = Math.Max(1, (int)Math.Ceiling(fill.Width * f));
                g.Draw(fill, 0, 0, src, fill.Height, ix, iy, (int)Math.Round(src * s * sx), ih);
            }
            return true;
        }

        /// <summary>The status code shown for a Pokémon: SLP, PSN, TOX, BRN, PAR, FRZ, FNT (or "").</summary>
        public static string StatusCode(string status, bool fainted)
        {
            if (fainted) return "FNT";
            switch ((status ?? "").ToUpperInvariant()) { case "BURN": return "BRN"; case "POISON": return "PSN"; case "TOXIC": return "TOX"; case "PARALYSIS": return "PAR"; case "SLEEP": return "SLP"; case "FROZEN": return "FRZ"; }
            return "";
        }

        public static bool Status(Gfx g, int x, int y, string code)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(U.StatusFolder)) return false;
            var t = InFolder(U.StatusFolder, "status_" + code) ?? (code == "TOX" ? InFolder(U.StatusFolder, "status_PSN") : null);
            if (t == null) return false;
            Pic(g, t, x, y);
            return true;
        }

        public static bool Gender(Gfx g, int x, int y, string gender)
        {
            var t = gender == "M" ? Tex(U.Male) : gender == "F" ? Tex(U.Female) : null;
            if (t == null) return false;
            Pic(g, t, x, y);
            return true;
        }

        public static bool Arrow(Gfx g, int x, int y)
        {
            var t = Tex(U.Arrow);
            if (t == null) return false;
            Pic(g, t, x, y + 8 - t.Height * S / 2);
            return true;
        }
    }
}
