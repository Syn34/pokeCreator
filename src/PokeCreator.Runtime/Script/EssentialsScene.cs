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

            // Partner trainer
            Register("pbRegisterPartner", (c, a) => _game.RegisterPartner(Str(a, 0), Str(a, 1), a.Count > 2 ? ToInt(a[2]) : 0));
            Register("pbDeregisterPartner", (c, a) => { _game.State.Extras.Partner = null; return true; });
            Register("$PokemonGlobal.partner", (c, a) => _game.State.Extras.Partner != null);

            // Roaming Pokémon
            Register("pbResetAllRoamers", (c, a) => { Roaming.ResetAll(_game); return true; });
            Register("pbRoamPokemon", (c, a) => { Roaming.OnMapChanged(_game); return true; });

            // Safari Zone
            Register("pbInSafari?", (c, a) => Safari.InSafari(_game));
            foreach (var n in new[] { "pbSafariState.pbStart", "pbSafariState.start" })
                Register(n, (c, a) => { Safari.Start(_game, a.Count > 0 ? ToInt(a[0]) : 0); return true; });
            foreach (var n in new[] { "pbSafariState.pbEnd", "pbSafariState.end" }) Register(n, (c, a) => { Safari.End(_game); return true; });
            Register("pbSafariState.inProgress?", (c, a) => Safari.InProgress(_game));
            Register("pbSafariState.pbGoToStart", (c, a) => { Safari.GoToStart(_game); return true; });
            Register("pbSafariState.ballcount", (c, a) => Safari.State(_game).Balls);
            Register("pbSafariState.steps", (c, a) => Safari.State(_game).Steps);
            Register("pbSafariState.decision", (c, a) => Safari.State(_game).Decision);
            Register("pbSafariState.captures", (c, a) => Safari.State(_game).Captures);
            Register("pbSafariState.pbReceptionMap", (c, a) => Safari.State(_game).StartMap);

            // Bug-Catching Contest
            Register("pbInBugContest?", (c, a) => BugContest.InProgress(_game));
            Register("pbBugContestState.inProgress?", (c, a) => BugContest.InProgress(_game));
            Register("pbBugContestUndecided?", (c, a) => _game.State.Extras.BugContest != null && _game.State.Extras.BugContest.Undecided);
            Register("pbBugContestDecided?", (c, a) => _game.State.Extras.BugContest != null && !_game.State.Extras.BugContest.Undecided && !_game.State.Extras.BugContest.InProgress && _game.State.Extras.BugContest.Place >= -1 && _game.State.Extras.BugContest.EndsAt > 0);
            Register("pbBugContestState.pbSetPokemon", (c, a) => { BugContest.SetPokemon(_game, ToInt(Arg(a, 0))); return true; });
            Register("pbBugContestState.pbStart", (c, a) =>
            {
                // Without pbSetPokemon first, the first able Pokémon goes in
                if (BugContest.State(_game).LeftBehind.Count == 0 && _game.Party.Count > 1)
                    BugContest.SetPokemon(_game, Math.Max(0, _game.Party.FindIndex(PokeCreator.Battles.Battle.Able)));
                BugContest.Start(_game, a.Count > 0 ? ToInt(a[0]) : 0); return true;
            });
            Register("pbBugContestState.pbEnd", (c, a) => { BugContest.End(_game, false); return true; });
            Register("pbBugContestState.pbStartJudging", (c, a) => { BugContest.End(_game, false); return true; });
            Register("pbBugContestState.pbJudge", (c, a) => { BugContest.Judge(_game, null); return true; });
            Register("pbBugContestState.place", (c, a) => BugContest.State(_game).Place);
            Register("pbBugContestState.ballcount", (c, a) => BugContest.State(_game).Balls);
            Register("pbBugContestState.lastPokemon", (c, a) => BugContest.State(_game).Caught);
            Register("pbBugContestStartOver", (c, a) => { BugContest.End(_game, true); return true; });

            // Battle Tower: pbBattleTower(rounds = 7, double = false, level = 50)
            Register("pbBattleTower", (c, a) => { BattleTower.Run(_game, a.Count > 0 ? ToInt(a[0]) : 7, a.Count > 1 && Truthy(a[1]), a.Count > 2 ? ToInt(a[2]) : 50); return true; });
            Register("pbBattleChallenge.wins", (c, a) => BattleTower.State(_game).Wins);
            Register("pbBattleChallenge.decision", (c, a) => BattleTower.State(_game).Decision);
            Register("pbBattleChallenge.pbInProgress?", (c, a) => BattleTower.InProgress(_game));
            Register("pbBattleChallenge.record", (c, a) => { int r; BattleTower.State(_game).Records.TryGetValue(a.Count > 0 && Truthy(a[0]) ? "double" : "single", out r); return r; });
            Register("pbBattleChallenge.streak", (c, a) => { int r; BattleTower.State(_game).Streaks.TryGetValue(a.Count > 0 && Truthy(a[0]) ? "double" : "single", out r); return r; });

            // Shadow Pokémon: the Relic Stone purifies a Pokémon whose heart is fully open
            Register("pbRelicStone", (c, a) =>
            {
                if (!_game.State.Party.Any(p => p.ShadowPokemon)) { _game.Message.Show("There's no Pokémon to purify here."); return false; }
                _game.PushScene(new UI.PartyScene(_game, "Purify which Pokémon?", null, (i, close) =>
                {
                    var pk = _game.State.Party[i];
                    if (!pk.ShadowPokemon) { _game.Message.Show("That isn't a Shadow Pokémon."); return; }
                    close();
                    if (!PokeCreator.Pkmn.Shadow.Purifiable(pk)) { _game.Message.Show(pk.Name(_game.Project) + "'s heart is still closed..."); return; }
                    PokeCreator.Pkmn.Shadow.Purify(_game.Project, pk);
                    _game.Audio.PlayMe("Pkmn get");
                    _game.Message.Show(pk.Name(_game.Project) + " opened the door to its heart! " + pk.Name(_game.Project) + " was purified!");
                }, null));
                return true;
            });
            Register("pbPurify", (c, a) =>
            {
                var pk = Arg(a, 0) as PokeCreator.Pkmn.Pokemon;
                if (pk == null || !PokeCreator.Pkmn.Shadow.Purifiable(pk)) return false;
                PokeCreator.Pkmn.Shadow.Purify(_game.Project, pk); return true;
            });

            // Link battles and trades with another player (LAN / internet)
            Register("pbLinkBattle", (c, a) => { LinkPlay.Begin(_game, "battle", a.Count > 0 && Truthy(a[0])); return true; });
            Register("pbLinkTrade", (c, a) => { LinkPlay.Begin(_game, "trade", false); return true; });
            Register("pbLinkHost", (c, a) => { LinkPlay.Host(_game, a.Count > 0 ? Str(a, 0) : "battle", false); return true; });
            Register("pbLinkJoin", (c, a) => { LinkPlay.Join(_game, a.Count > 0 ? Str(a, 0) : "battle", a.Count > 1 ? Str(a, 1) : (_game.State.Extras.LastLinkAddress ?? "127.0.0.1")); return true; });

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

namespace PokeCreator.Runtime.Script
{
    /// <summary>GameData::Ribbon.get(:ID): a ribbon's name ("FOOTPRINT" → "Footprint Ribbon").</summary>
    public class RibbonInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public RibbonInfo(string id)
        {
            Id = id ?? "";
            string words = string.Join(" ", Id.Split('_').Select(w => w.Length == 0 ? w : w.Substring(0, 1) + w.Substring(1).ToLowerInvariant()));
            Name = words.EndsWith("Ribbon") ? words : words + " Ribbon";
            Description = "";
        }
    }
}
