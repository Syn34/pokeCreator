using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PokeCreator.Maps;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime.Script
{
    /// <summary>Script calls that act on the scene: waits, sounds, "!" bubbles, turning, move routes, bridges, start over.</summary>
    public partial class EssentialsCompat
    {
        static readonly string[] MoveRouteNames =
        {
            "", "Down", "Left", "Right", "Up", "LowerLeft", "LowerRight", "UpperLeft", "UpperRight", "Random", "TowardPlayer", "AwayFromPlayer",
            "Forward", "Backward", "Jump", "Wait", "TurnDown", "TurnLeft", "TurnRight", "TurnUp", "TurnRight90", "TurnLeft90", "Turn180",
            "TurnRightOrLeft90", "TurnRandom", "TurnTowardPlayer", "TurnAwayFromPlayer", "SwitchOn", "SwitchOff", "ChangeSpeed", "ChangeFreq",
            "WalkAnimeOn", "WalkAnimeOff", "StepAnimeOn", "StepAnimeOff", "DirectionFixOn", "DirectionFixOff", "ThroughOn", "ThroughOff",
            "AlwaysOnTopOn", "AlwaysOnTopOff", "Graphic", "Opacity", "Blending", "PlaySE", "Script"
        };

        /// <summary>How many values follow each move-route command in a pbMoveRoute list.</summary>
        static int MoveRouteParams(int code)
        {
            switch (code)
            {
                case 14: return 2;                     // Jump x, y
                case 41: return 4;                     // Graphic name, hue, direction, pattern
                case 15: case 27: case 28: case 29: case 30: case 42: case 43: case 44: case 45: return 1;
                default: return 0;
            }
        }

        static double ToDouble(object o)
        {
            if (o == null) return 0;
            double d; return double.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }

        /// <summary>A character from a script argument: an event object, an event number (0 = this event, -1 = the player) or the player.</summary>
        Character CharArg(Interpreter c, object o)
        {
            var ch = o as Character;
            if (ch != null) return ch;
            if (o is string && ((string)o == "$game_player" || (string)o == "player")) return _game.Player;
            if (c != null) return c.GetCharacter(ToInt(o));
            if (ToInt(o) < 0) return _game.Player;
            return _game.Map != null ? _game.Map.Events.FirstOrDefault(e => e.Id == ToInt(o)) : null;
        }

        static string AudioName(object o)
        {
            if (o == null) return "";
            var s = o as string;
            if (s != null) return s;
            var list = o as List<object>;
            if (list != null && list.Count > 0) return Convert.ToString(list[0]);
            return Convert.ToString(o);
        }

        void RegisterSceneCalls()
        {
            for (int i = 1; i < MoveRouteNames.Length; i++) { int code = i; Register("PBMoveRoute::" + MoveRouteNames[i], (c, a) => code); }
            Register("$game_player", (c, a) => _game.Player);

            // pbWait: seconds in Essentials v21 (a decimal, or a small whole number), frames in older versions
            Register("pbWait", (c, a) =>
            {
                if (c == null || a.Count == 0) return true;
                object v = a[0];
                double d = ToDouble(v);
                int frames = v is double || d < 10 ? (int)Math.Round(d * 60) : (int)(d * 3 / 2);
                c.Wait(Math.Max(1, frames));
                return true;
            });
            Register("pbSEPlay", (c, a) => { _game.Audio.PlaySe(AudioName(Arg(a, 0)), a.Count > 1 ? ToInt(a[1]) : 100); return true; });
            Register("pbMEPlay", (c, a) => { _game.Audio.PlayMe(AudioName(Arg(a, 0)), a.Count > 1 ? ToInt(a[1]) : 100); return true; });
            Register("pbBGMPlay", (c, a) => { _game.Audio.PlayBgm(AudioName(Arg(a, 0)), a.Count > 1 ? ToInt(a[1]) : 100); return true; });
            Register("pbBGSPlay", (c, a) => { _game.Audio.PlayBgs(AudioName(Arg(a, 0)), a.Count > 1 ? ToInt(a[1]) : 100); return true; });
            Register("pbBGMFade", (c, a) => { _game.Audio.StopBgm(); return true; });
            Register("pbBGMStop", (c, a) => { _game.Audio.StopBgm(); return true; });
            Register("pbPlayCry", (c, a) => { var pk = Arg(a, 0) as PokeCreator.Pkmn.Pokemon; if (pk != null) _game.Audio.PlayCry(pk); else _game.Audio.PlaySe("Cries/" + Str(a, 0)); return true; });

            // pbExclaim(event): "!" over a character (or each of a list)
            Register("pbExclaim", (c, a) =>
            {
                var list = Arg(a, 0) as List<object>;
                var targets = list != null ? list : new List<object> { Arg(a, 0) };
                foreach (var t in targets)
                {
                    var ch = CharArg(c, t);
                    if (ch != null) _game.Exclaim(ch, 30);
                }
                if (c != null) c.Wait(30);
                return true;
            });
            Register("pbTurnTowardEvent", (c, a) =>
            {
                var ch = CharArg(c, Arg(a, 0)); var other = CharArg(c, Arg(a, 1));
                if (ch != null && other != null) ch.TurnToward(other);
                return true;
            });

            // pbMoveRoute(event, [PBMoveRoute::Up, PBMoveRoute::Wait, 4, ...], wait_complete = false)
            Register("pbMoveRoute", (c, a) =>
            {
                var ch = CharArg(c, Arg(a, 0));
                var cmds = Arg(a, 1) as List<object>;
                if (ch == null || cmds == null) return null;
                var route = new MoveRoute { Repeat = false, Skippable = true };
                for (int i = 0; i < cmds.Count; i++)
                {
                    int code = cmds[i] is string ? Array.IndexOf(MoveRouteNames, ((string)cmds[i]).Replace("PBMoveRoute::", "")) : ToInt(cmds[i]);
                    if (code <= 0) continue;
                    var ec = new EventCommand(code, 0);
                    int np = MoveRouteParams(code);
                    for (int k = 0; k < np && i + 1 < cmds.Count; k++) ec.Params.Add(cmds[++i]);
                    route.List.Add(ec);
                }
                route.List.Add(new EventCommand(0, 0));
                ch.ForceMoveRoute(route);
                if (a.Count > 2 && Truthy(a[2]) && c != null) c.WaitForMoves();
                return route;
            });

            Register("pbBridgeOn", (c, a) => { GameMap.BridgeHeight = a.Count > 0 ? Math.Max(1, ToInt(a[0])) : 2; return true; });
            Register("pbBridgeOff", (c, a) => { GameMap.BridgeHeight = 0; return true; });
            Register("$PokemonGlobal.bridge", (c, a) => GameMap.BridgeHeight);

            // pbStartOver: back to the last Pokémon Center (or home) with the party healed
            Register("pbStartOver", (c, a) =>
            {
                _game.Message.Show(_game.State.HealMap > 0 ? "You scurried back to a Pokémon Center, protecting your exhausted Pokémon from any further harm..." : "You scurried back home, protecting your exhausted Pokémon from any further harm...", null, -1, _ => _game.Whiteout());
                return true;
            });
        }
    }
}
