using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Json;
using PokeCreator.Maps;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.World
{
    /// <summary>
    /// Anything that walks on the map (player or event). Positions use RMXP conventions:
    /// X/Y in tiles, RealX/RealY in 1/128 tile units, directions 2/4/6/8.
    /// </summary>
    public abstract class Character
    {
        protected readonly Game Game;
        public int X, Y;
        public double RealX, RealY;
        public int Direction = 2;
        public int Pattern, OriginalPattern;
        public int MoveSpeed = 4, MoveFrequency = 6;
        /// <summary>Extra speed multiplier on top of MoveSpeed (the bicycle).</summary>
        public double SpeedScale = 1;
        public bool WalkAnime = true, StepAnime, DirectionFix, Through, AlwaysOnTop;
        public string CharacterName = "";
        public int TileId;
        public int Opacity = 255;
        public bool Transparent;

        protected double AnimeCount;
        protected int StopCount, WaitCount;
        protected int JumpCount, JumpPeak;

        // Move routes (custom movement and forced "Set Move Route")
        protected MoveRoute Route;
        protected int RouteIndex;
        protected bool RouteForced;
        public bool MoveRouteForcing { get { return RouteForced; } }
        MoveRoute _originalRoute; int _originalRouteIndex;

        const double FpsScale = 40.0 / 60.0; // RMXP ran at 40 FPS; we run at 60

        protected Character(Game game) { Game = game; }

        public bool Moving { get { return Math.Abs(RealX - X * 128) > 0.01 || Math.Abs(RealY - Y * 128) > 0.01; } }
        public bool Jumping { get { return JumpCount > 0; } }

        public void MoveTo(int x, int y)
        {
            X = x; Y = y; RealX = x * 128; RealY = y * 128;
        }

        public virtual GameMap Map { get { return Game.Map; } }

        // ------------------------------------------------------------------ update
        public virtual void Update()
        {
            if (Jumping) UpdateJump();
            else if (Moving) UpdateMove();
            else UpdateStop();

            if (AnimeCount > 18 - MoveSpeed * 2)
            {
                if (!StepAnime && StopCount > 0) Pattern = OriginalPattern;
                else Pattern = (Pattern + 1) % 4;
                AnimeCount = 0;
            }
            if (WaitCount > 0) { WaitCount--; return; }
            if (RouteForced) { UpdateRoute(); return; }
            if (CanMoveAutonomously()) UpdateSelfMovement();
        }

        protected virtual bool CanMoveAutonomously() { return false; }
        protected virtual void UpdateSelfMovement() { }

        void UpdateJump()
        {
            JumpCount--;
            RealX = (RealX * JumpCount + X * 128) / (JumpCount + 1);
            RealY = (RealY * JumpCount + Y * 128) / (JumpCount + 1);
        }

        void UpdateMove()
        {
            double dist = Math.Pow(2, MoveSpeed) * FpsScale * SpeedScale;
            if (Y * 128 > RealY) RealY = Math.Min(RealY + dist, Y * 128);
            if (X * 128 < RealX) RealX = Math.Max(RealX - dist, X * 128);
            if (X * 128 > RealX) RealX = Math.Min(RealX + dist, X * 128);
            if (Y * 128 < RealY) RealY = Math.Max(RealY - dist, Y * 128);
            if (WalkAnime) AnimeCount += 1.5 * FpsScale;
            else if (StepAnime) AnimeCount += FpsScale;
        }

        void UpdateStop()
        {
            if (StepAnime) AnimeCount += FpsScale;
            else if (Pattern != OriginalPattern) AnimeCount += 1.5 * FpsScale;
            StopCount++;
        }

        /// <summary>Pixel offset of the jump arc.</summary>
        public int JumpHeight
        {
            get
            {
                if (!Jumping) return 0;
                int n = JumpPeak * 2 - JumpCount;
                return (JumpPeak * JumpPeak - (n - JumpPeak) * (n - JumpPeak)) / 2;
            }
        }

        // ------------------------------------------------------------------ movement
        public static int Dx(int d) { return d == 6 ? 1 : d == 4 ? -1 : 0; }
        public static int Dy(int d) { return d == 2 ? 1 : d == 8 ? -1 : 0; }

        public virtual bool Passable(int x, int y, int d)
        {
            int nx = x + Dx(d), ny = y + Dy(d);
            var map = Map;
            if (!map.Valid(nx, ny)) return false;
            if (Through) return true;
            if (!map.TilePassable(x, y, d, this as GameEvent)) return false;
            if (!map.TilePassable(nx, ny, 10 - d, this as GameEvent)) return false;
            foreach (var e in map.EventsAt(nx, ny))
            {
                if (e == this || e.Through) continue;
                if (!(this is Player)) return false;
                if (e.CharacterName.Length > 0) return false;
            }
            var pl = Game.Player;
            if (!(this is Player) && pl.X == nx && pl.Y == ny && !pl.Through && CharacterName.Length > 0) return false;
            return true;
        }

        public void Turn(int d)
        {
            if (DirectionFix || d == 0) return;
            Direction = d;
            StopCount = 0;
        }
        public void TurnToward(Character c)
        {
            int sx = X - c.X, sy = Y - c.Y;
            if (sx == 0 && sy == 0) return;
            if (Math.Abs(sx) > Math.Abs(sy)) Turn(sx > 0 ? 4 : 6); else Turn(sy > 0 ? 8 : 2);
        }

        public virtual bool Move(int d, bool turn = true)
        {
            if (turn) Turn(d);
            if (Passable(X, Y, d))
            {
                Turn(d);
                X += Dx(d); Y += Dy(d);
                OnMoved();
                return true;
            }
            OnBump(X + Dx(d), Y + Dy(d));
            return false;
        }

        protected virtual void OnMoved() { }
        protected virtual void OnBump(int x, int y) { }

        public void MoveRandom() { Move(new[] { 2, 4, 6, 8 }[Game.Rng.Next(4)], true); }

        public void MoveToward(Character c)
        {
            int sx = X - c.X, sy = Y - c.Y;
            if (sx == 0 && sy == 0) return;
            int d1 = Math.Abs(sx) > Math.Abs(sy) ? (sx > 0 ? 4 : 6) : (sy > 0 ? 8 : 2);
            if (!Move(d1))
            {
                int d2 = Math.Abs(sx) > Math.Abs(sy) ? (sy > 0 ? 8 : sy < 0 ? 2 : 0) : (sx > 0 ? 4 : sx < 0 ? 6 : 0);
                if (d2 != 0) Move(d2);
            }
        }

        public void Jump(int dx, int dy)
        {
            if (dx != 0 || dy != 0)
            {
                if (Math.Abs(dx) > Math.Abs(dy)) Turn(dx < 0 ? 4 : 6); else Turn(dy < 0 ? 8 : 2);
            }
            X += dx; Y += dy;
            int dist = (int)Math.Round(Math.Sqrt(dx * dx + dy * dy));
            JumpPeak = 10 + dist - MoveSpeed;
            JumpCount = JumpPeak * 2;
            StopCount = 0;
        }

        // ------------------------------------------------------------------ move routes
        public void ForceMoveRoute(MoveRoute route)
        {
            if (!RouteForced) { _originalRoute = Route; _originalRouteIndex = RouteIndex; }
            Route = route; RouteIndex = 0; RouteForced = true;
            WaitCount = 0;
        }

        protected void SetCustomRoute(MoveRoute r) { Route = r; RouteIndex = 0; }

        protected void UpdateRoute()
        {
            if (Route == null || Moving || Jumping) return;
            while (true)
            {
                if (RouteIndex >= Route.List.Count) { EndRoute(); return; }
                var cmd = Route.List[RouteIndex];
                if (cmd.Code == 0)
                {
                    if (Route.Repeat) { RouteIndex = 0; if (Route.List.Count <= 1) return; continue; }
                    EndRoute(); return;
                }
                bool advance = ExecuteMoveCommand(cmd);
                if (!advance) return; // blocked and not skippable: retry next frame
                RouteIndex++;
                // Commands that take time end this frame's processing
                if (cmd.Code <= 26) return; // moves, waits and turns each take a frame (RMXP behaviour)
            }
        }

        void EndRoute()
        {
            if (RouteForced)
            {
                RouteForced = false;
                Route = _originalRoute; RouteIndex = _originalRouteIndex;
            }
            else RouteIndex = 0;
        }

        /// <summary>Executes one RMXP move command. Returns false if it should be retried.</summary>
        bool ExecuteMoveCommand(EventCommand c)
        {
            int code = c.Code;
            bool ok = true;
            switch (code)
            {
                case 1: ok = Move(2); break;
                case 2: ok = Move(4); break;
                case 3: ok = Move(6); break;
                case 4: ok = Move(8); break;
                case 5: case 6: case 7: case 8: // diagonals: approximate with two steps
                    ok = Move(code == 5 || code == 7 ? 2 : 8) | Move(code == 5 || code == 6 ? 4 : 6); break;
                case 9: MoveRandom(); break;
                case 10: MoveToward(Game.Player); break;
                case 11: { int sx = X - Game.Player.X, sy = Y - Game.Player.Y; ok = Move(Math.Abs(sx) > Math.Abs(sy) ? (sx > 0 ? 6 : 4) : (sy > 0 ? 2 : 8)); break; }
                case 12: ok = Move(Direction, false); break;
                case 13: { bool fix = DirectionFix; DirectionFix = true; ok = Move(10 - Direction, false); DirectionFix = fix; break; }
                case 14: Jump(c.IntParam(0), c.IntParam(1)); break;
                case 15: WaitCount = c.IntParam(0) * 3 / 2 - 1; break;
                case 16: Turn(2); break;
                case 17: Turn(4); break;
                case 18: Turn(6); break;
                case 19: Turn(8); break;
                case 20: Turn(Direction == 2 ? 6 : Direction == 4 ? 2 : Direction == 6 ? 8 : 4); break; // turn 90 right
                case 21: Turn(Direction == 2 ? 4 : Direction == 4 ? 8 : Direction == 6 ? 2 : 6); break; // turn 90 left
                case 22: Turn(10 - Direction); break;
                case 23: Turn(Game.Rng.Next(2) == 0 ? (Direction == 2 || Direction == 8 ? 4 : 2) : (Direction == 2 || Direction == 8 ? 6 : 8)); break;
                case 24: Turn(new[] { 2, 4, 6, 8 }[Game.Rng.Next(4)]); break;
                case 25: TurnToward(Game.Player); break;
                case 26: TurnToward(Game.Player); Turn(10 - Direction); break;
                case 27: Game.State.SetSwitch(c.IntParam(0), true); Game.Map.RefreshEvents(); break;
                case 28: Game.State.SetSwitch(c.IntParam(0), false); Game.Map.RefreshEvents(); break;
                case 29: MoveSpeed = c.IntParam(0); break;
                case 30: MoveFrequency = c.IntParam(0); break;
                case 31: WalkAnime = true; break;
                case 32: WalkAnime = false; break;
                case 33: StepAnime = true; break;
                case 34: StepAnime = false; break;
                case 35: DirectionFix = true; break;
                case 36: DirectionFix = false; break;
                case 37: Through = true; break;
                case 38: Through = false; break;
                case 39: AlwaysOnTop = true; break;
                case 40: AlwaysOnTop = false; break;
                case 41:
                    TileId = 0; CharacterName = c.StrParam(0);
                    if (c.Params.Count > 2) { int d = c.IntParam(2); if (d != 0) Direction = d; }
                    if (c.Params.Count > 3) { Pattern = OriginalPattern = c.IntParam(3); }
                    break;
                case 42: Opacity = c.IntParam(0); break;
                case 43: break; // blend type
                case 44:
                    {
                        var a = c.Params.Count > 0 ? c.Params[0] as JsonObject : null;
                        if (a != null) Game.Audio.PlaySe(a.GetString("Name"), a.GetInt("Volume", 100));
                        break;
                    }
                case 45: Game.Compat.RunScript(c.StrParam(0), null); break;
            }
            if (!ok && Route != null && Route.Skippable) ok = true;
            return ok || code > 14;
        }

        // ------------------------------------------------------------------ drawing info
        public int ScreenX(int displayX) { return (int)Math.Round(RealX / 4.0) - displayX + 16; }
        public int ScreenY(int displayY) { return (int)Math.Round(RealY / 4.0) - displayY + 32 - JumpHeight; }
    }
}
