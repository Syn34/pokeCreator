using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using PokeCreator.Maps;

namespace PokeCreator.Studio.Controls
{
    /// <summary>Small vector icons for the project tree, drawn with GDI+ so they need no image files.</summary>
    public static class Icons
    {
        public const int Size = 18;
        static readonly Dictionary<string, Bitmap> _cache = new Dictionary<string, Bitmap>();

        /// <summary>Icons a user can pick for a map (key, label).</summary>
        public static readonly string[][] MapChoices =
        {
            new[] { "town", "Town" }, new[] { "city", "City" }, new[] { "route", "Route" }, new[] { "home", "House (inside)" },
            new[] { "building", "Building (inside)" }, new[] { "pokecenter", "Pokémon Center" }, new[] { "mart", "Poké Mart" },
            new[] { "gym", "Gym" }, new[] { "lab", "Lab" }, new[] { "cave", "Cave" }, new[] { "forest", "Forest / Park" },
            new[] { "water", "Sea / Lake" }, new[] { "island", "Island" }, new[] { "mountain", "Mountain" }, new[] { "tower", "Tower" },
            new[] { "gate", "Gate" }, new[] { "trophy", "League / Frontier" }, new[] { "star", "Special / Intro" }, new[] { "map", "Other" },
        };

        public static Bitmap Get(string key)
        {
            if (string.IsNullOrEmpty(key)) key = "file";
            Bitmap b;
            if (_cache.TryGetValue(key, out b)) return b;
            b = new Bitmap(Size, Size);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                Draw(g, key);
            }
            _cache[key] = b;
            return b;
        }

        // ------------------------------------------------------------------ map icon guessing
        /// <summary>Picks an icon for a map from its name (and its parent's), unless the user chose one.</summary>
        public static string ForMap(MapInfo mi, MapInfo parent)
        {
            if (!string.IsNullOrEmpty(mi.Icon)) return mi.Icon;
            string n = (mi.Name ?? "").ToLowerInvariant();
            Func<string[], bool> has = words => words.Any(w => n.Contains(w));
            if (has(new[] { "poké center", "poke center", "pokécenter", "pokecenter", "pokémon center", "pokemon center", "centre" })) return "pokecenter";
            if (has(new[] { "mart", "shop", "store", "dept" })) return "mart";
            if (has(new[] { "gym" })) return "gym";
            if (has(new[] { "lab" })) return "lab";
            if (has(new[] { "cave", "tunnel", "mine", "cavern", "grotto", "dungeon", "b1f", "b2f" })) return "cave";
            if (has(new[] { "gate" })) return "gate";
            if (has(new[] { "tower", "lighthouse" })) return "tower";
            if (has(new[] { "league", "frontier", "stadium", "hall of fame", "plateau", "arena", "palace", "factory", "champion", "elite" })) return "trophy";
            if (has(new[] { "house", "home", "'s ", "room", "bedroom", "apartment", "condo" })) return "home";
            if (has(new[] { "route", "road", "path", "cycling", "trail" })) return "route";
            if (has(new[] { "city" })) return "city";
            if (has(new[] { "town", "village", "hamlet" })) return "town";
            if (has(new[] { "forest", "wood", "park", "garden", "grove", "safari", "jungle" })) return "forest";
            if (has(new[] { "island", "isle" })) return "island";
            if (has(new[] { "sea", "lake", "beach", "harbor", "harbour", "bay", "ocean", "underwater", "river", "port", "berth" })) return "water";
            if (has(new[] { "mount", "mt.", "mountain", "peak", "summit", "volcano" })) return "mountain";
            if (has(new[] { "intro", "title", "test", "debug" })) return "star";
            if (has(new[] { "region", "world" })) return "map";
            // Maps inside a town/city are usually buildings
            if (parent != null) return "building";
            return "map";
        }

        // ------------------------------------------------------------------ drawing
        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }
        static readonly Color Outline = C(40, 40, 48);

        static void Fill(Graphics g, Color c, params PointF[] pts) { using (var br = new SolidBrush(c)) g.FillPolygon(br, pts); using (var p = new Pen(Outline, 1f)) g.DrawPolygon(p, pts); }
        static void Box(Graphics g, Color c, float x, float y, float w, float h, bool outline = true) { using (var br = new SolidBrush(c)) g.FillRectangle(br, x, y, w, h); if (outline) using (var p = new Pen(Outline, 1f)) g.DrawRectangle(p, x, y, w, h); }
        static void Round(Graphics g, Color c, float x, float y, float w, float h, float r, bool outline = true)
        {
            using (var path = RoundRect(x, y, w, h, r))
            {
                using (var br = new SolidBrush(c)) g.FillPath(br, path);
                if (outline) using (var p = new Pen(Outline, 1f)) g.DrawPath(p, path);
            }
        }
        static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath(); float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90); p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        static void Circle(Graphics g, Color c, float x, float y, float d, bool outline = true) { using (var br = new SolidBrush(c)) g.FillEllipse(br, x, y, d, d); if (outline) using (var p = new Pen(Outline, 1f)) g.DrawEllipse(p, x, y, d, d); }
        static void Line(Graphics g, Color c, float w, float x1, float y1, float x2, float y2) { using (var p = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(p, x1, y1, x2, y2); }
        static void Txt(Graphics g, string s, Color c, float size, float x, float y)
        {
            using (var f = new Font("Arial", size, FontStyle.Bold, GraphicsUnit.Pixel)) using (var br = new SolidBrush(c))
            {
                var sz = g.MeasureString(s, f);
                g.DrawString(s, f, br, x - sz.Width / 2 + 0.5f, y - sz.Height / 2 + 0.5f);
            }
        }

        static void House(Graphics g, Color roof, Color wall)
        {
            Box(g, wall, 3.5f, 8.5f, 11, 7.5f);
            Fill(g, roof, new PointF(1.5f, 9.5f), new PointF(9, 2.5f), new PointF(16.5f, 9.5f));
            Box(g, C(140, 90, 50), 7.5f, 11.5f, 3, 4.5f);
        }

        static void Person(Graphics g, Color shirt)
        {
            Circle(g, C(250, 214, 180), 6, 2, 6);
            Fill(g, shirt, new PointF(3, 16.5f), new PointF(4, 10), new PointF(14, 10), new PointF(15, 16.5f));
        }

        static void Ball(Graphics g, float x, float y, float d)
        {
            using (var red = new SolidBrush(C(230, 50, 50))) g.FillPie(red, x, y, d, d, 180, 180);
            using (var white = new SolidBrush(Color.White)) g.FillPie(white, x, y, d, d, 0, 180);
            using (var p = new Pen(Outline, 1.2f)) { g.DrawEllipse(p, x, y, d, d); g.DrawLine(p, x, y + d / 2, x + d, y + d / 2); }
            Circle(g, Color.White, x + d / 2 - d * 0.17f, y + d / 2 - d * 0.17f, d * 0.34f);
        }

        /// <summary>Three stacked map layers (diamonds); the given one (0 ground, 1 middle, 2 top) is filled in and drawn on top.</summary>
        static void Layers(Graphics g, int active)
        {
            Func<int, PointF[]> diamond = i => { float y = 12.5f - i * 4.5f; return new[] { new PointF(9, y - 3.6f), new PointF(16.5f, y), new PointF(9, y + 3.6f), new PointF(1.5f, y) }; };
            using (var pen = new Pen(C(120, 126, 140), 1.1f))
            using (var faint = new SolidBrush(Color.FromArgb(70, 200, 205, 215)))
                for (int i = 0; i < 3; i++) if (i != active) { g.FillPolygon(faint, diamond(i)); g.DrawPolygon(pen, diamond(i)); }
            Fill(g, active == 0 ? C(96, 176, 80) : active == 1 ? C(232, 170, 60) : C(80, 150, 230), diamond(active));
        }

        static void Draw(Graphics g, string key)
        {
            switch (key)
            {
                // ---- map editor toolbar
                case "layer-ground": Layers(g, 0); break;
                case "layer-middle": Layers(g, 1); break;
                case "layer-top": Layers(g, 2); break;
                case "layer-events":
                    // Little NPC with a speech bubble
                    Circle(g, C(250, 214, 170), 3.5f, 5, 5.5f);
                    Fill(g, C(220, 70, 60), new PointF(2, 16.5f), new PointF(3, 11), new PointF(9, 11), new PointF(10, 16.5f));
                    Round(g, Color.White, 9.5f, 1.5f, 7.5f, 6, 2);
                    Fill(g, Color.White, new PointF(11, 7), new PointF(10, 9.5f), new PointF(13, 7));
                    Line(g, C(220, 60, 60), 1.4f, 13.25f, 2.8f, 13.25f, 4.6f);
                    Circle(g, C(220, 60, 60), 12.6f, 5.4f, 1.3f, false);
                    break;
                case "tool-pencil":
                    // Diagonal pencil: pink eraser, yellow body, wooden tip, graphite point
                    using (var m = new Matrix()) { m.RotateAt(45, new PointF(9, 9)); g.Transform = m; }
                    Box(g, C(240, 150, 170), 7, -1.5f, 4, 3);
                    Box(g, C(190, 190, 200), 7, 1.5f, 4, 1.5f);
                    Box(g, C(250, 200, 60), 7, 3, 4, 9);
                    Fill(g, C(236, 200, 150), new PointF(7, 12), new PointF(11, 12), new PointF(9, 17));
                    Fill(g, C(60, 60, 70), new PointF(8.3f, 15.2f), new PointF(9.7f, 15.2f), new PointF(9, 17));
                    g.ResetTransform();
                    break;
                case "tool-select":
                    using (var p = new Pen(C(60, 60, 60), 1.4f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawRectangle(p, 2.5f, 3.5f, 13, 11);
                    using (var p = new Pen(C(40, 120, 220), 1.6f)) { g.DrawLine(p, 10, 10, 15.5f, 15.5f); g.DrawLine(p, 10, 10, 10, 15); g.DrawLine(p, 10, 10, 15, 10); }
                    break;
                case "tool-rect":
                    Box(g, C(150, 200, 255), 2.5f, 4.5f, 13, 9, false);
                    using (var p = new Pen(C(40, 100, 200), 1.6f)) g.DrawRectangle(p, 2.5f, 4.5f, 13, 9);
                    foreach (var pt in new[] { new PointF(2.5f, 4.5f), new PointF(15.5f, 4.5f), new PointF(2.5f, 13.5f), new PointF(15.5f, 13.5f) })
                        Box(g, Color.White, pt.X - 1.5f, pt.Y - 1.5f, 3, 3);
                    break;
                case "tool-fill":
                    // Paint bucket tipping, with a drop
                    using (var m = new Matrix()) { m.RotateAt(-30, new PointF(8, 9)); g.Transform = m; }
                    Fill(g, C(170, 180, 196), new PointF(3, 6), new PointF(13, 6), new PointF(12, 15), new PointF(4, 15));
                    Box(g, C(70, 150, 240), 3, 4.5f, 10, 2.5f);
                    using (var p = new Pen(Outline, 1.2f)) g.DrawArc(p, 4, 0.5f, 8, 8, 180, 180);
                    g.ResetTransform();
                    Fill(g, C(70, 150, 240), new PointF(15, 9), new PointF(17, 13), new PointF(15, 15.5f), new PointF(13, 13));
                    break;
                case "tool-eraser":
                    using (var m = new Matrix()) { m.RotateAt(-35, new PointF(9, 9)); g.Transform = m; }
                    Box(g, C(245, 140, 160), 2, 6, 7, 6.5f);
                    Box(g, C(240, 240, 246), 9, 6, 7, 6.5f);
                    g.ResetTransform();
                    Line(g, C(150, 150, 160), 1.2f, 3, 16.5f, 15, 16.5f);
                    break;
                // ---- maps
                case "town": House(g, C(220, 70, 60), C(250, 236, 200)); break;
                case "home": House(g, C(70, 120, 210), C(240, 240, 248)); break;
                case "city":
                    Box(g, C(120, 140, 170), 2.5f, 6.5f, 6, 10); Box(g, C(150, 170, 200), 8.5f, 2.5f, 7, 14);
                    for (int y = 4; y < 15; y += 3) { Box(g, C(255, 230, 120), 10.5f, y + 0.5f, 1.5f, 1.5f, false); Box(g, C(255, 230, 120), 12.8f, y + 0.5f, 1.5f, 1.5f, false); }
                    for (int y = 8; y < 15; y += 3) Box(g, C(255, 230, 120), 4.5f, y + 0.5f, 2, 1.5f, false);
                    break;
                case "route":
                    Line(g, C(120, 80, 40), 2, 9, 7, 9, 17);
                    Fill(g, C(110, 180, 90), new PointF(2, 2.5f), new PointF(13, 2.5f), new PointF(16.5f, 5.5f), new PointF(13, 8.5f), new PointF(2, 8.5f));
                    break;
                case "building":
                    Round(g, C(160, 110, 70), 4.5f, 2.5f, 9, 14, 3); Circle(g, C(250, 210, 80), 10.5f, 9, 2, false); break;
                case "pokecenter":
                    Round(g, Color.White, 1.5f, 1.5f, 15, 15, 4); using (var p = new Pen(C(225, 50, 60), 1.5f)) using (var path = RoundRect(1.5f, 1.5f, 15, 15, 4)) g.DrawPath(p, path);
                    Box(g, C(225, 50, 60), 7.5f, 4, 3, 10, false); Box(g, C(225, 50, 60), 4, 7.5f, 10, 3, false); break;
                case "mart": Round(g, C(50, 110, 220), 1.5f, 1.5f, 15, 15, 4); Txt(g, "M", Color.White, 11, 9, 9); break;
                case "gym":
                    Fill(g, C(230, 60, 60), new PointF(5, 1), new PointF(8, 1), new PointF(8, 8), new PointF(5, 8));
                    Fill(g, C(60, 110, 220), new PointF(10, 1), new PointF(13, 1), new PointF(13, 8), new PointF(10, 8));
                    Circle(g, C(250, 200, 50), 4, 6, 10); Circle(g, C(255, 235, 130), 6.5f, 8.5f, 5, false); break;
                case "lab":
                    Fill(g, C(225, 235, 245), new PointF(7, 1.5f), new PointF(11, 1.5f), new PointF(11, 6), new PointF(16, 16), new PointF(2, 16), new PointF(7, 6));
                    Fill(g, C(80, 200, 160), new PointF(4.5f, 11), new PointF(13.5f, 11), new PointF(16, 16), new PointF(2, 16)); break;
                case "cave":
                    Fill(g, C(130, 120, 110), new PointF(1, 16), new PointF(4, 6), new PointF(9, 2), new PointF(14, 6), new PointF(17, 16));
                    using (var br = new SolidBrush(C(30, 25, 25))) g.FillPie(br, 5, 8, 8, 16, 180, 180); break;
                case "forest":
                    Box(g, C(120, 80, 40), 8, 11, 2, 6, false);
                    Fill(g, C(60, 160, 70), new PointF(9, 1), new PointF(15, 9), new PointF(11.5f, 9), new PointF(16, 13.5f), new PointF(2, 13.5f), new PointF(6.5f, 9), new PointF(3, 9)); break;
                case "water":
                    Circle(g, C(120, 190, 240), 1.5f, 1.5f, 15);
                    using (var p = new Pen(Color.White, 1.6f)) { g.DrawBezier(p, 3, 7, 5.5f, 5, 7, 9, 9, 7); g.DrawBezier(p, 9, 7, 11, 5, 12.5f, 9, 15, 7); g.DrawBezier(p, 3, 11.5f, 5.5f, 9.5f, 7, 13.5f, 9, 11.5f); g.DrawBezier(p, 9, 11.5f, 11, 9.5f, 12.5f, 13.5f, 15, 11.5f); }
                    break;
                case "island":
                    Box(g, C(110, 180, 240), 0.5f, 12.5f, 17, 4.5f, false);
                    using (var br = new SolidBrush(C(240, 210, 140))) g.FillEllipse(br, 3, 10, 12, 5);
                    Line(g, C(140, 95, 50), 1.6f, 9, 11, 10, 4);
                    Fill(g, C(70, 170, 80), new PointF(10, 4), new PointF(4, 6), new PointF(9, 3), new PointF(16, 5)); break;
                case "mountain":
                    Fill(g, C(120, 130, 140), new PointF(0.5f, 16), new PointF(7, 3), new PointF(10, 8), new PointF(12, 5), new PointF(17.5f, 16));
                    Fill(g, Color.White, new PointF(5, 7), new PointF(7, 3), new PointF(9, 7)); break;
                case "tower":
                    Fill(g, C(180, 180, 195), new PointF(6, 16.5f), new PointF(7, 4), new PointF(11, 4), new PointF(12, 16.5f));
                    Line(g, Outline, 1, 9, 4, 9, 0.5f); Fill(g, C(230, 60, 60), new PointF(9, 0.5f), new PointF(14, 2), new PointF(9, 3.5f));
                    Box(g, C(255, 230, 120), 8, 7, 2, 2, false); Box(g, C(255, 230, 120), 8, 11, 2, 2, false); break;
                case "gate":
                    Box(g, C(170, 150, 130), 2.5f, 5.5f, 4, 11); Box(g, C(170, 150, 130), 11.5f, 5.5f, 4, 11);
                    Box(g, C(200, 80, 70), 1.5f, 2.5f, 15, 3); break;
                case "trophy":
                    using (var br = new SolidBrush(C(245, 195, 50))) { g.FillPie(br, 3.5f, -3, 11, 14, 0, 180); }
                    using (var p = new Pen(Outline, 1)) g.DrawPie(p, 3.5f, -3, 11, 14, 0, 180);
                    Box(g, C(245, 195, 50), 8, 11, 2, 3); Box(g, C(200, 150, 40), 5, 14, 8, 2.5f); break;
                case "star":
                    {
                        var pts = new PointF[10];
                        for (int i = 0; i < 10; i++) { double a = -Math.PI / 2 + i * Math.PI / 5; float r = i % 2 == 0 ? 8 : 3.6f; pts[i] = new PointF(9 + (float)(r * Math.Cos(a)), 9.5f + (float)(r * Math.Sin(a))); }
                        Fill(g, C(250, 205, 60), pts); break;
                    }
                case "map":
                case "townmaps":
                    Fill(g, C(240, 225, 180), new PointF(1, 3), new PointF(6, 1.5f), new PointF(12, 3), new PointF(17, 1.5f), new PointF(17, 15), new PointF(12, 16.5f), new PointF(6, 15), new PointF(1, 16.5f));
                    Line(g, C(80, 170, 90), 1.6f, 3, 12, 8, 7); Line(g, C(80, 140, 220), 1.6f, 9, 13, 15, 6); break;

                // ---- sections and database tables
                case "settings":
                    using (var br = new SolidBrush(C(130, 140, 160)))
                    {
                        for (int i = 0; i < 8; i++) { var st = g.Save(); g.TranslateTransform(9, 9); g.RotateTransform(i * 45); g.FillRectangle(br, -1.8f, -8, 3.6f, 4); g.Restore(st); }
                        g.FillEllipse(br, 3, 3, 12, 12);
                    }
                    Circle(g, Color.White, 6.5f, 6.5f, 5, false); break;
                case "database":
                    for (int i = 2; i >= 0; i--)
                    {
                        float y = 2.5f + i * 4.5f;
                        using (var br = new SolidBrush(i == 0 ? C(250, 175, 70) : C(235, 140, 50))) { g.FillRectangle(br, 2.5f, y + 2, 13, 4); g.FillEllipse(br, 2.5f, y + 4, 13, 4); }
                        using (var p = new Pen(Outline, 1f)) { g.DrawArc(p, 2.5f, y + 4, 13, 4, 0, 180); g.DrawLine(p, 2.5f, y + 2, 2.5f, y + 6); g.DrawLine(p, 15.5f, y + 2, 15.5f, y + 6); }
                    }
                    Circle(g, C(255, 205, 120), 2.5f, 0.5f, 13); using (var p = new Pen(Outline, 1f)) g.DrawEllipse(p, 2.5f, 0.5f, 13, 4);
                    break;
                case "maps": Circle(g, C(90, 170, 230), 1.5f, 1.5f, 15); Fill(g, C(90, 190, 90), new PointF(4, 6), new PointF(8, 4), new PointF(10, 8), new PointF(7, 12), new PointF(4, 10)); Fill(g, C(90, 190, 90), new PointF(11, 10), new PointF(14, 9), new PointF(14, 13), new PointF(11, 14)); break;
                case "scripts": Round(g, C(140, 90, 220), 1.5f, 1.5f, 15, 15, 3); Txt(g, "{ }", Color.White, 9, 9, 9); break;
                case "cs": Round(g, C(104, 33, 122), 1.5f, 1.5f, 15, 15, 3); Txt(g, "C#", Color.White, 8.5f, 9, 9); break;
                case "assets": case "image":
                    Round(g, C(235, 240, 250), 1.5f, 3, 15, 12, 2);
                    Fill(g, C(80, 170, 90), new PointF(2.5f, 14), new PointF(7, 8), new PointF(10, 11.5f), new PointF(12, 9.5f), new PointF(15.5f, 14));
                    Circle(g, C(250, 200, 60), 11.5f, 4.5f, 3, false); break;
                case "audio":
                    Circle(g, C(60, 160, 220), 2, 11, 5.5f); Circle(g, C(60, 160, 220), 10, 9.5f, 5.5f);
                    Line(g, C(60, 160, 220), 1.6f, 7, 13.5f, 7, 3); Line(g, C(60, 160, 220), 1.6f, 15, 12, 15, 1.5f); Line(g, C(60, 160, 220), 2.2f, 7, 3, 15, 1.5f); break;
                case "font": Round(g, C(235, 235, 240), 1.5f, 1.5f, 15, 15, 3); Txt(g, "Aa", C(60, 60, 70), 9, 9, 9); break;
                case "folder":
                    Fill(g, C(240, 190, 70), new PointF(1, 4), new PointF(6.5f, 4), new PointF(8, 6), new PointF(17, 6), new PointF(17, 15.5f), new PointF(1, 15.5f)); break;
                case "files": Fill(g, C(240, 190, 70), new PointF(1, 4), new PointF(6.5f, 4), new PointF(8, 6), new PointF(17, 6), new PointF(17, 15.5f), new PointF(1, 15.5f)); Box(g, Color.White, 5, 8, 8, 5.5f); break;
                case "json": Round(g, C(240, 160, 40), 1.5f, 1.5f, 15, 15, 3); Txt(g, "{}", Color.White, 9.5f, 9, 9); break;
                case "text": case "file":
                    Fill(g, Color.White, new PointF(3.5f, 1.5f), new PointF(11, 1.5f), new PointF(14.5f, 5), new PointF(14.5f, 16.5f), new PointF(3.5f, 16.5f));
                    for (int y = 7; y <= 13; y += 3) Line(g, C(160, 160, 170), 1, 6, y, 12, y); break;
                case "types":
                    Fill(g, C(240, 80, 60), new PointF(9, 1), new PointF(13, 5), new PointF(9, 9), new PointF(5, 5));
                    Fill(g, C(60, 140, 240), new PointF(5, 5), new PointF(9, 9), new PointF(5, 13), new PointF(1, 9));
                    Fill(g, C(90, 200, 90), new PointF(13, 5), new PointF(17, 9), new PointF(13, 13), new PointF(9, 9));
                    Fill(g, C(250, 210, 60), new PointF(9, 9), new PointF(13, 13), new PointF(9, 17), new PointF(5, 13)); break;
                case "abilities": Fill(g, C(250, 210, 50), new PointF(10, 0.5f), new PointF(3, 10), new PointF(8, 10), new PointF(6.5f, 17.5f), new PointF(15, 7), new PointF(10, 7)); break;
                case "moves":
                    Line(g, C(120, 120, 130), 4.2f, 3.5f, 14.5f, 14, 4); Line(g, C(230, 235, 245), 2.2f, 3.5f, 14.5f, 14, 4);
                    Line(g, C(150, 100, 50), 2.5f, 2, 12, 6, 16); Line(g, C(150, 100, 50), 2.5f, 1.5f, 16.5f, 3.5f, 14.5f); break;
                case "items":
                    Box(g, C(210, 210, 220), 7, 1.5f, 4, 3); Round(g, C(170, 90, 220), 3.5f, 4.5f, 11, 12, 3); Box(g, Color.White, 6, 8, 6, 4.5f, false); break;
                case "species": case "pokemon": Ball(g, 1.5f, 1.5f, 15); break;
                case "trainer_types": Person(g, C(70, 130, 220)); break;
                case "trainers": Person(g, C(220, 80, 70)); Ball(g, 10.5f, 10.5f, 7); break;
                case "players": Person(g, C(80, 180, 100)); break;
                case "encounters":
                    using (var br = new SolidBrush(C(70, 175, 80)))
                        foreach (var x in new[] { 3f, 7.5f, 12f }) g.FillPolygon(br, new[] { new PointF(x - 3, 16.5f), new PointF(x, 3 + (x == 7.5f ? 0 : 3)), new PointF(x + 3, 16.5f) });
                    using (var p = new Pen(C(40, 110, 50), 1)) g.DrawLine(p, 1, 16.5f, 17, 16.5f); break;
                case "town_maps": goto case "map";
                case "regional_dexes": Round(g, C(220, 50, 50), 3, 1.5f, 12, 15, 2); Circle(g, C(120, 200, 250), 6, 4, 4.5f); Box(g, C(250, 250, 250), 5.5f, 11, 7, 2.5f, false); break;
                case "commonevents":
                    // A list of commands with a shared/link mark
                    Round(g, C(120, 100, 200), 2, 2, 14, 14, 3);
                    for (int i = 0; i < 3; i++) Line(g, Color.White, 1.4f, 5, 6 + i * 3, 13, 6 + i * 3);
                    Circle(g, C(250, 200, 70), 11f, 11f, 6.5f);
                    break;
                case "switches": Round(g, C(80, 190, 100), 1, 5, 16, 8, 4); Circle(g, Color.White, 9, 5.5f, 7); break;
                case "tilesets":
                    Box(g, C(90, 180, 90), 1.5f, 1.5f, 7, 7); Box(g, C(200, 170, 120), 9.5f, 1.5f, 7, 7);
                    Box(g, C(100, 160, 230), 1.5f, 9.5f, 7, 7); Box(g, C(150, 150, 160), 9.5f, 9.5f, 7, 7); break;
                case "world":
                    Round(g, C(60, 130, 200), 1.5f, 1.5f, 15, 15, 2);
                    Box(g, C(110, 190, 90), 3, 3, 5, 5); Box(g, C(110, 190, 90), 8, 3, 6, 5); Box(g, C(230, 200, 110), 3, 8, 11, 6);
                    break;
                case "connections":
                    using (var p = new Pen(C(120, 130, 150), 2.4f)) { g.DrawArc(p, 1.5f, 5.5f, 9, 7, 90, 180); g.DrawArc(p, 7.5f, 5.5f, 9, 7, 270, 180); g.DrawLine(p, 6, 5.5f, 9, 5.5f); g.DrawLine(p, 9, 12.5f, 12, 12.5f); g.DrawLine(p, 6, 9, 12, 9); }
                    break;
                case "appearance":
                    Circle(g, C(240, 225, 200), 1.5f, 2, 15);
                    Circle(g, C(230, 60, 60), 4, 5, 3.2f, false); Circle(g, C(60, 140, 240), 8, 3.8f, 3.2f, false); Circle(g, C(70, 190, 90), 11.8f, 6, 3.2f, false); Circle(g, C(250, 200, 50), 4.5f, 9.5f, 3.2f, false);
                    Circle(g, Color.White, 9.5f, 10.5f, 4); break;
                default: goto case "file";
            }
        }

        /// <summary>Icon for a file in "All Project Files", by extension.</summary>
        public static string ForFile(string path)
        {
            switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
            {
                case ".cs": return "cs";
                case ".json": return "json";
                case ".png": case ".gif": case ".bmp": case ".jpg": return "image";
                case ".ogg": case ".wav": case ".mp3": case ".mid": case ".midi": return "audio";
                case ".ttf": case ".otf": return "font";
                default: return "text";
            }
        }
    }
}
