using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.UI
{
    /// <summary>What an animation does to a battler's sprite on the current frame.</summary>
    public class AnimSpriteState
    {
        public double X, Y, ZoomX = 1, ZoomY = 1, Angle;
        public bool Mirror, Visible = true;
        public int Opacity = 255, Blend;
        public Color Color;
    }

    /// <summary>A battler as the animation sees it: centre of its sprite and the sprite itself.</summary>
    public class AnimBattler
    {
        public int Key = -1;
        public double X, Y;
        public Texture Tex;
        public int FrameW, FrameH;
    }

    /// <summary>
    /// Plays an Essentials battle animation (20 frames a second): moves the user's and target's sprites, draws the cels
    /// from the animation sheet, plays its sounds and flashes. Positions in the animation are relative to where the
    /// editor puts the user (128,224) and the target (384,96), and are mapped onto where they really stand.
    /// </summary>
    public class BattleAnimPlayer
    {
        public const int TicksPerFrame = 3;
        readonly Game _game;
        readonly BattleAnimation _a;
        readonly Texture _sheet;
        public readonly AnimBattler User, Target;
        int _tick, _fired = -1;
        // Flashes: scope 1 target, 2 user, 3 screen
        Color _flashColor; int _flashScope, _flashTicks, _flashTotal;

        public BattleAnimPlayer(Game game, BattleAnimation a, AnimBattler user, AnimBattler target)
        {
            _game = game; _a = a; User = user; Target = target ?? user;
            if (!string.IsNullOrEmpty(a.Graphic)) _sheet = game.Gfx.LoadHued(game.Project.FindAsset("Graphics/Animations", a.Graphic), a.Hue);
        }

        /// <summary>Moves the cels drawn from the sheet (not the Pokémon's own sprites); used to set send-out effects down where the Pokémon stands.</summary>
        public double EffectShiftX, EffectShiftY;

        public string Name { get { return _a.Id; } }
        public int Frame { get { return _tick / TicksPerFrame; } }
        public bool Done { get { return Frame >= _a.Frames.Count && _flashTicks <= 0; } }

        public void Update()
        {
            int f = Math.Min(Frame, _a.Frames.Count);
            for (int i = _fired + 1; i <= f && i < _a.Frames.Count; i++)
                foreach (var t in _a.Timings.Where(x => x.Frame == i)) Fire(t);
            _fired = Math.Max(_fired, f);
            _tick++;
            if (_flashTicks > 0) _flashTicks--;
        }

        void Fire(AnimationTiming t)
        {
            if (t.Type != 0) return;
            if (!string.IsNullOrEmpty(t.Sound))
            {
                string name = _game.Project.FindAsset("Audio/SE", "Anim/" + t.Sound) != null ? "Anim/" + t.Sound : t.Sound;
                _game.Audio.PlaySe(name, t.Volume > 0 ? t.Volume : 100);
            }
            if (t.FlashScope > 0 && t.FlashDuration > 0)
            {
                var c = t.FlashColor ?? new[] { 255, 255, 255, 255 };
                _flashColor = new Color((byte)Clamp(c[0]), (byte)Clamp(c[1]), (byte)Clamp(c[2]), (byte)Clamp(c.Length > 3 ? c[3] : 255));
                _flashScope = t.FlashScope; _flashTotal = _flashTicks = t.FlashDuration * TicksPerFrame;
            }
        }

        static int Clamp(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        List<int[]> Cels { get { int f = Frame; return f >= 0 && f < _a.Frames.Count ? _a.Frames[f] : null; } }

        /// <summary>Where a cel goes on screen, and whether it should be mirrored (an animation running right to left).</summary>
        void Place(int[] cel, out double x, out double y, out bool flip)
        {
            x = cel[BattleAnimation.X]; y = cel[BattleAnimation.Y]; flip = false;
            switch (cel[BattleAnimation.Focus])
            {
                case BattleAnimation.FocusTarget:
                    x += Target.X - BattleAnimation.TargetX; y += Target.Y - BattleAnimation.TargetY; break;
                case BattleAnimation.FocusUser:
                    x += User.X - BattleAnimation.UserX; y += User.Y - BattleAnimation.UserY; break;
                case BattleAnimation.FocusUserAndTarget:
                    {
                        // Map the editor's user→target line onto the real one (scale + rotation)
                        double sx = BattleAnimation.TargetX - BattleAnimation.UserX, sy = BattleAnimation.TargetY - BattleAnimation.UserY;
                        double dx = Target.X - User.X, dy = Target.Y - User.Y;
                        if (User == Target || (dx == 0 && dy == 0)) { x += User.X - BattleAnimation.UserX; y += User.Y - BattleAnimation.UserY; break; }
                        double den = sx * sx + sy * sy;
                        double rr = (dx * sx + dy * sy) / den, ri = (dy * sx - dx * sy) / den;
                        double px = x - BattleAnimation.UserX, py = y - BattleAnimation.UserY;
                        x = User.X + px * rr - py * ri; y = User.Y + px * ri + py * rr;
                        flip = (sx > 0) != (dx > 0);
                        break;
                    }
            }
        }

        /// <summary>The user's (cel 0) or target's (cel 1) sprite on this frame; null = draw it as usual.</summary>
        public AnimSpriteState BattlerState(int key)
        {
            var cels = Cels;
            if (cels == null || key < 0) return null;
            int idx = key == User.Key ? 0 : key == Target.Key ? 1 : -1;
            if (idx < 0 || idx >= cels.Count || cels[idx] == null) return FlashOnly(key);
            var cel = cels[idx];
            double x, y; bool flip; Place(cel, out x, out y, out flip);
            var st = new AnimSpriteState
            {
                X = x, Y = y, ZoomX = cel[BattleAnimation.ZoomX] / 100.0, ZoomY = cel[BattleAnimation.ZoomY] / 100.0,
                Angle = cel[BattleAnimation.Angle], Mirror = cel[BattleAnimation.Mirror] != 0, Visible = cel[BattleAnimation.Visible] != 0,
                Opacity = cel[BattleAnimation.Opacity], Blend = cel[BattleAnimation.Blend],
                Color = CelColor(cel)
            };
            ApplyFlash(key, st);
            return st;
        }

        AnimSpriteState FlashOnly(int key)
        {
            if (_flashTicks <= 0 || !FlashHits(key)) return null;
            var b = key == User.Key ? User : Target;
            var st = new AnimSpriteState { X = b.X, Y = b.Y };
            ApplyFlash(key, st);
            return st;
        }

        bool FlashHits(int key) { return (_flashScope == 1 && key == Target.Key) || (_flashScope == 2 && key == User.Key); }

        void ApplyFlash(int key, AnimSpriteState st)
        {
            if (_flashTicks <= 0 || !FlashHits(key)) return;
            int a = _flashColor.A * _flashTicks / Math.Max(1, _flashTotal);
            if (a > st.Color.A) st.Color = new Color(_flashColor.R, _flashColor.G, _flashColor.B, (byte)a);
        }

        static Color CelColor(int[] cel)
        {
            int a = cel[BattleAnimation.ColorA];
            return a <= 0 ? default(Color) : new Color((byte)Clamp(cel[BattleAnimation.ColorR]), (byte)Clamp(cel[BattleAnimation.ColorG]), (byte)Clamp(cel[BattleAnimation.ColorB]), (byte)Clamp(a));
        }

        /// <summary>Draws the effect cels (2 and up): behind = those set to go behind the Pokémon.</summary>
        public void Draw(Gfx g, bool behind)
        {
            var cels = Cels;
            if (cels == null) return;
            for (int i = 2; i < cels.Count; i++)
            {
                var cel = cels[i];
                if (cel == null || cel[BattleAnimation.Visible] == 0) continue;
                int pr = cel[BattleAnimation.Priority];
                bool isBehind = pr == 0 || pr == 2;
                if (isBehind != behind) continue;
                double x, y; bool flip; Place(cel, out x, out y, out flip);
                int pattern = cel[BattleAnimation.Pattern];
                Texture tex; int sx, sy, sw, sh;
                if (pattern < 0)
                {
                    var b = pattern == -1 ? User : Target;
                    tex = b.Tex; sx = 0; sy = 0; sw = b.FrameW; sh = b.FrameH;
                    flip = false;
                }
                else
                {
                    tex = _sheet; sw = sh = BattleAnimation.CelSize;
                    x += EffectShiftX; y += EffectShiftY;
                    sx = pattern % BattleAnimation.SheetColumns * sw; sy = pattern / BattleAnimation.SheetColumns * sh;
                    if (tex == null || sy >= tex.Height) continue;
                }
                if (tex == null) continue;
                g.DrawEx(tex, sx, sy, sw, sh, x, y, cel[BattleAnimation.ZoomX] / 100.0, cel[BattleAnimation.ZoomY] / 100.0,
                         cel[BattleAnimation.Angle], (cel[BattleAnimation.Mirror] != 0) ^ flip, cel[BattleAnimation.Opacity], cel[BattleAnimation.Blend], CelColor(cel));
            }
        }

        /// <summary>A screen flash from the animation's timings.</summary>
        public void DrawScreenFlash(Gfx g)
        {
            if (_flashTicks <= 0 || _flashScope != 3) return;
            int a = _flashColor.A * _flashTicks / Math.Max(1, _flashTotal);
            g.FillRect(0, 0, g.Width, g.Height, new Color(_flashColor.R, _flashColor.G, _flashColor.B, (byte)a));
        }
    }
}
