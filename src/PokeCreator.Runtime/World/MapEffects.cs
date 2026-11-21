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

        public void Clear() { _list.Clear(); _anims.Clear(); }

        public void Update()
        {
            foreach (var e in _list) e.Frame++;
            _list.RemoveAll(e => e.Frame >= Length(e.Kind));
            foreach (var r in _anims)
            {
                // RPG Maker animations run at 20 frames a second: each frame shows for 3 of ours
                if (r.Ticks % 3 == 0)
                {
                    int f = r.Ticks / 3;
                    foreach (var tm in r.A.Timings.Where(t => t.Frame == f))
                    {
                        if (!string.IsNullOrEmpty(tm.Se)) _g.Audio.PlaySe(tm.Se, tm.Volume > 0 ? tm.Volume : 80);
                        if (tm.FlashScope == 2 && tm.FlashDuration > 0) _g.Flash(tm.FlashDuration * 3 / 2);
                    }
                }
                r.Ticks++;
            }
            _anims.RemoveAll(r => r.Ticks / 3 >= Math.Max(1, r.A.Frames.Count));
        }

        class RunningAnim { public PokeCreator.Data.MapAnimation A; public Character Target; public int Ticks; }
        readonly List<RunningAnim> _anims = new List<RunningAnim>();

        /// <summary>Show Animation with an imported RPG Maker animation; false if the project has no animation with that ID.</summary>
        public bool PlayAnimation(int id, Character target)
        {
            var a = _g.Project.MapAnimations.FirstOrDefault(x => x.Id == id);
            if (a == null || a.Frames.Count == 0) return false;
            _anims.Add(new RunningAnim { A = a, Target = target });
            return true;
        }

        public bool AnimationRunning(Character target) { return _anims.Any(r => r.Target == target); }

        /// <summary>Draws one frame of an imported map animation centred at (x, y), looping; false if there's no such animation.</summary>
        public bool DrawLooping(Gfx g, int id, double x, double y, int tick)
        {
            var a = _g.Project.MapAnimations.FirstOrDefault(m => m.Id == id);
            if (a == null || a.Frames.Count == 0) return false;
            var sheet = g.Load(_g.Project.FindAsset("Graphics/Animations", a.Graphic));
            if (sheet == null) return false;
            foreach (var cell in a.Frames[(tick / 3) % a.Frames.Count])
            {
                int pat = cell[0];
                int sx = (pat % 5) * 192, sy = (pat / 5) * 192;
                if (pat < 0 || sx + 192 > sheet.Width || sy + 192 > sheet.Height) continue;
                g.DrawEx(sheet, sx, sy, 192, 192, x + cell[1], y + cell[2], cell[3] / 100.0, cell[3] / 100.0, cell[4], cell[5] != 0, cell[6], cell[7]);
            }
            return true;
        }

        void DrawAnimations(Gfx g, int displayX, int displayY)
        {
            foreach (var r in _anims)
            {
                int f = Math.Min(r.A.Frames.Count - 1, r.Ticks / 3);
                var sheet = r.A.Hue != 0 ? g.LoadHued(_g.Project.FindAsset("Graphics/Animations", r.A.Graphic), r.A.Hue) : g.Load(_g.Project.FindAsset("Graphics/Animations", r.A.Graphic));
                if (sheet == null) continue;
                double bx, by;
                if (r.A.Position == 3 || r.Target == null) { bx = g.Width / 2.0; by = g.Height / 2.0; }
                else
                {
                    bx = r.Target.ScreenX(displayX); by = r.Target.ScreenY(displayY);
                    if (r.A.Position == 0) by -= 64; else if (r.A.Position == 1) by -= 32;
                }
                foreach (var cell in r.A.Frames[f])
                {
                    int pat = cell[0];
                    if (pat < 0) continue;
                    int sx = (pat % 5) * 192, sy = (pat / 5) * 192;
                    if (sx + 192 > sheet.Width || sy + 192 > sheet.Height) continue;
                    g.DrawEx(sheet, sx, sy, 192, 192, bx + cell[1], by + cell[2], cell[3] / 100.0, cell[3] / 100.0, cell[4], cell[5] != 0, cell[6], cell[7]);
                }
            }
        }

        /// <summary>The Show Animation event command (RMXP animation numbers as Essentials: 1 grass, 2 dust, 3 "!").</summary>
        public static string KindForAnimation(int id) { return id == 1 ? "grass" : id == 2 ? "dust" : id == 3 ? "exclaim" : null; }

        public void Draw(Gfx g, int displayX, int displayY)
        {
            if (_anims.Count > 0) DrawAnimations(g, displayX, displayY);
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
