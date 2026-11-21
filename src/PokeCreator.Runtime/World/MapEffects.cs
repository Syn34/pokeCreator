using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// Small animations on the map (as Essentials' overworld animations): grass rustling when someone walks into tall
    /// grass, dust where a jump lands, and the "!" over a character. They use Graphics/Animations/Overworld dust and
    /// grass and Overworld exclaim (192×192 cells) when the project has them, simple shapes otherwise.
    /// </summary>
    public class MapEffects
    {
        class Effect { public string Kind; public int MapX, MapY; public int Frame; public Character Follow; }
        readonly List<Effect> _list = new List<Effect>();
        readonly Game _g;
        public MapEffects(Game g) { _g = g; }

        const int FramesPerCell = 4;
        static int Length(string kind) { return (kind == "grass" ? 5 : kind == "dust" || kind == "splash" ? 3 : 8) * FramesPerCell; }

        /// <summary>kind: "grass", "dust" or "exclaim"; at a tile, or over a character.</summary>
        public void Play(string kind, int x, int y, Character follow = null)
        {
            _list.Add(new Effect { Kind = kind, MapX = x, MapY = y, Follow = follow });
        }

        public void Clear() { _list.Clear(); }

        public void Update()
        {
            foreach (var e in _list) e.Frame++;
            _list.RemoveAll(e => e.Frame >= Length(e.Kind));
        }

        /// <summary>The Show Animation event command (RMXP animation numbers as Essentials: 1 grass, 2 dust, 3 "!").</summary>
        public static string KindForAnimation(int id) { return id == 1 ? "grass" : id == 2 ? "dust" : id == 3 ? "exclaim" : null; }

        public void Draw(Gfx g, int displayX, int displayY)
        {
            if (_list.Count == 0) return;
            var sheet = g.Load(_g.Project.FindAsset("Graphics/Animations", "Overworld dust and grass"));
            var exSheet = g.Load(_g.Project.FindAsset("Graphics/Animations", "Overworld exclaim"));
            foreach (var e in _list)
            {
                int cx, cy;
                if (e.Follow != null) { cx = e.Follow.ScreenX(displayX); cy = e.Follow.ScreenY(displayY) - 16; }
                else { cx = e.MapX * 32 + 16 - displayX; cy = e.MapY * 32 + 16 - displayY; }
                int step = e.Frame / FramesPerCell;
                if (e.Kind == "exclaim")
                {
                    if (exSheet != null) { int cell = step % 4; g.Draw(exSheet, (cell % 5) * 192, (cell / 5) * 192, 192, 192, cx - 96, cy - 96 - 40); }
                    else { g.FillRect(cx - 4, cy - 70, 8, 18, new Color(232, 48, 48)); g.FillRect(cx - 4, cy - 48, 8, 8, new Color(232, 48, 48)); }
                    continue;
                }
                if (e.Kind == "splash")
                {
                    // Water splashing out of a puddle: two drops either side and a widening ring
                    var water = new Color(160, 200, 248, 220);
                    int r = 6 + step * 5;
                    g.FillRect(cx - r, cy + 10, r * 2, 2, water);
                    g.FillRect(cx - r + 2, cy + 4 - step * 3, 4, 4, water); g.FillRect(cx + r - 6, cy + 4 - step * 3, 4, 4, water);
                    continue;
                }
                int first = e.Kind == "grass" ? 3 : 0;
                int c = first + step;
                if (sheet != null) g.Draw(sheet, (c % 4) * 192, (c / 4) * 192, 192, 192, cx - 96, cy - 96);
                else if (e.Kind == "grass")
                {
                    var leaf = new Color(72, 168, 72);
                    int spread = 4 + step * 3;
                    g.FillRect(cx - spread, cy - step * 2, 4, 4, leaf); g.FillRect(cx + spread - 4, cy - step * 2, 4, 4, leaf); g.FillRect(cx - 2, cy - 6 - step * 3, 4, 4, leaf);
                }
                else
                {
                    var dust = new Color(216, 200, 168, 200);
                    int r = 4 + step * 4;
                    g.FillRect(cx - r, cy + 8, 6, 4, dust); g.FillRect(cx + r - 6, cy + 8, 6, 4, dust);
                }
            }
        }
    }
}
