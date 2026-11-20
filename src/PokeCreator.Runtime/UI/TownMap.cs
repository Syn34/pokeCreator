using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.UI
{
    /// <summary>
    /// The region map (Essentials' UI_RegionMap): Graphics/UI/Town Map/bg.png, the region image, the player's head,
    /// a blinking cursor and, when flying, the places the player can fly to. Squares are 16×16 pixels.
    /// </summary>
    public class TownMapScene : Scene
    {
        const int Sq = 16;
        readonly bool _fly;
        readonly Action<TownMapPoint> _onFly;
        TownMapRegion _region;
        int _cx, _cy, _px = -1, _py = -1, _cols = 30, _rows = 20;

        /// <summary>View the map (fly = false), or choose a place to fly to; onFly gets the chosen point.</summary>
        public TownMapScene(Game g, bool fly, Action<TownMapPoint> onFly) : base(g)
        {
            Opaque = true; _fly = fly; _onFly = onFly;
            int region = 0;
            var pos = CurrentPosition(g);
            if (pos != null) { region = pos[0]; _px = pos[1]; _py = pos[2]; }
            _region = g.Project.TownMaps.FirstOrDefault(r => r.Id == region.ToString()) ?? g.Project.TownMaps.FirstOrDefault();
            if (_region == null || _region.Id != region.ToString()) { _px = _py = -1; }
            _cx = _px >= 0 ? _px : 0; _cy = _py >= 0 ? _py : 0;
            if (_px < 0 && _region != null && _region.Points.Count > 0) { _cx = _region.Points[0].X; _cy = _region.Points[0].Y; }
        }

        /// <summary>Region, x, y of the player on the town map (from the map's "Town map position", adjusted by the map size).</summary>
        public static int[] CurrentPosition(Game g)
        {
            if (g.Map == null || g.Map.Data.Properties == null) return null;
            var props = g.Map.Data.Properties;
            if (props.MapPosition == null || props.MapPosition.Count < 3) return null;
            int x = props.MapPosition[1], y = props.MapPosition[2];
            // "Map size" = squares wide (and flags): big maps cover several squares
            if (!string.IsNullOrEmpty(props.MapSize))
            {
                var parts = props.MapSize.Split(',');
                int w; string flags = parts.Length > 1 ? parts[1].Trim() : "";
                if (int.TryParse(parts[0].Trim(), out w) && w > 0)
                {
                    int h = Math.Max(1, (int)Math.Ceiling(flags.Length / (double)w));
                    if (w > 1) x += g.Player.X * w / Math.Max(1, g.Map.Width);
                    if (h > 1) y += g.Player.Y * h / Math.Max(1, g.Map.Height);
                }
            }
            return new[] { props.MapPosition[0], x, y };
        }

        bool PointVisible(TownMapPoint p)
        {
            if (string.IsNullOrEmpty(p.Switch)) return true;
            int sw; return !int.TryParse(p.Switch, out sw) || Game.State.GetSwitch(sw);
        }

        /// <summary>A point the player can fly to: it has a fly destination the player has visited.</summary>
        bool CanFlyTo(TownMapPoint p)
        {
            return p.FlyMap > 0 && PointVisible(p) && Game.State.Visited.Contains(p.FlyMap);
        }

        TownMapPoint PointAt(int x, int y) { return _region == null ? null : _region.Points.FirstOrDefault(p => p.X == x && p.Y == y && PointVisible(p)); }

        protected override void Update()
        {
            if (_region == null) { if (Input.Trigger(Button.Action) || Input.Trigger(Button.Cancel)) { Cancel(); Close(); } return; }
            int ox = _cx, oy = _cy;
            if (Input.Repeat(Button.Left) && _cx > 0) _cx--;
            if (Input.Repeat(Button.Right) && _cx < _cols - 1) _cx++;
            if (Input.Repeat(Button.Up) && _cy > 0) _cy--;
            if (Input.Repeat(Button.Down) && _cy < _rows - 1) _cy++;
            if (ox != _cx || oy != _cy) Cursor();
            if (Input.Trigger(Button.Cancel)) { Cancel(); Close(); return; }
            if (_fly && Input.Trigger(Button.Action))
            {
                var p = _region.Points.FirstOrDefault(q => q.X == _cx && q.Y == _cy && CanFlyTo(q));
                if (p == null) { Buzz(); return; }
                Decide();
                Game.Message.Show("Would you like to fly to " + p.Name + "?", new List<string> { "Yes", "No" }, 1, c =>
                {
                    if (c != 0) return;
                    Close();
                    if (_onFly != null) _onFly(p);
                });
            }
        }

        public override void Draw(Gfx g)
        {
            g.FillRect(0, 0, g.Width, g.Height, new Color(0, 0, 0));
            var bg = g.Load(Game.Project.FindAsset("Graphics/UI/Town Map", "bg"));
            if (bg != null) g.Draw(bg, 0, 0, bg.Width, bg.Height, 0, 0);
            else Ui.Gradient(g, 0, 0, g.Width, g.Height, new Color(72, 128, 200), new Color(32, 72, 136));
            if (_region == null)
            {
                Ui.LightText(g, 18, g.Height / 2 - 16, "There is no town map. Add one under Database > Town Maps.");
                return;
            }
            var img = string.IsNullOrEmpty(_region.Filename) ? null : g.Load(Game.Project.FindAsset("Graphics/UI/Town Map", System.IO.Path.GetFileNameWithoutExtension(_region.Filename)));
            int mw = img != null ? img.Width : 480, mh = img != null ? img.Height : 320;
            _cols = Math.Max(1, mw / Sq); _rows = Math.Max(1, mh / Sq);
            int mx = (g.Width - mw) / 2, my = (g.Height - mh) / 2;
            if (img != null) g.Draw(img, 0, 0, mw, mh, mx, my);
            else
            {
                g.FillRect(mx, my, mw, mh, new Color(96, 168, 96));
                foreach (var p in _region.Points) g.FillRect(mx + p.X * Sq + 2, my + p.Y * Sq + 2, Sq - 4, Sq - 4, new Color(232, 96, 64));
            }
            int frame = (Frame / 16) % 2;
            if (_fly)
            {
                var fly = g.Load(Game.Project.FindAsset("Graphics/UI/Town Map", "icon_fly"));
                foreach (var p in _region.Points.Where(CanFlyTo).GroupBy(p => p.X * 1000 + p.Y).Select(gr => gr.First()))
                {
                    if (fly != null) g.Draw(fly, frame * 32, 0, 32, 32, mx - 8 + p.X * Sq, my - 8 + p.Y * Sq);
                    else g.FillRect(mx + p.X * Sq + 4, my + p.Y * Sq + 4, 8, 8, new Color(248, 248, 120));
                }
            }
            if (_px >= 0)
            {
                string type = Game.Player.Look != null ? Game.Player.Look.TrainerType : "";
                var head = g.Load(Game.Project.FindAsset("Graphics/UI/Town Map", "player_" + type));
                if (head != null) g.Draw(head, 0, 0, Math.Min(32, head.Width), Math.Min(32, head.Height), mx - 8 + _px * Sq, my - 8 + _py * Sq);
                else g.FillRect(mx + _px * Sq + 3, my + _py * Sq + 3, 10, 10, new Color(248, 64, 48));
            }
            var cur = g.Load(Game.Project.FindAsset("Graphics/UI/Town Map", "cursor"));
            if (cur != null) g.Draw(cur, frame * 32, 0, 32, 32, mx - 8 + _cx * Sq, my - 8 + _cy * Sq);
            else
            {
                var c = new Color(248, 64, 48);
                g.FillRect(mx + _cx * Sq - 2, my + _cy * Sq - 2, Sq + 4, 2, c); g.FillRect(mx + _cx * Sq - 2, my + _cy * Sq + Sq, Sq + 4, 2, c);
                g.FillRect(mx + _cx * Sq - 2, my + _cy * Sq, 2, Sq, c); g.FillRect(mx + _cx * Sq + Sq, my + _cy * Sq, 2, Sq, c);
            }
            // Region name at the top, place and point of interest at the bottom (as Essentials)
            g.DrawText(18, 4, _fly ? "Where do you want to fly?" : (_region.Name ?? ""), Ui.Light, new Color(0, 0, 0));
            var at = PointAt(_cx, _cy);
            if (at != null)
            {
                g.DrawText(18, g.Height - 30, at.Name ?? "", Ui.Light, new Color(0, 0, 0));
                if (!string.IsNullOrEmpty(at.PointOfInterest)) { string s = at.PointOfInterest; g.DrawText(g.Width - 16 - g.TextWidth(s), g.Height - 30, s, Ui.Light, new Color(0, 0, 0)); }
            }
        }
    }
}
