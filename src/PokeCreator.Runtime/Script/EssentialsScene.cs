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
            var d = o as Dictionary<string, object>; object n;
            if (d != null && d.TryGetValue("name", out n)) return Convert.ToString(n);
            return Convert.ToString(o);
        }

        void RegisterSceneCalls()
        {
            for (int i = 1; i < MoveRouteNames.Length; i++) { int code = i; Register("PBMoveRoute::" + MoveRouteNames[i], (c, a) => code); }
            RegisterMiscCalls();
            Register("$game_player", (c, a) => _game.Player);
            Register("$game_player.x", (c, a) => _game.Player.X);
            Register("$game_player.y", (c, a) => _game.Player.Y);
            Register("$game_player.direction", (c, a) => _game.Player.Direction);
            foreach (var t in new[] { "Symbol", "String", "Integer", "Numeric", "Array", "Hash", "Pokemon", "Fixnum" }) { string tn = t; Register(tn, (c, a) => tn); }

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
            Register("pbBugContestState.pbEnd", (c, a) =>
            {
                var bs = BugContest.State(_game);
                if (bs.Results != null) { BugContest.FinishJudged(_game); bs.Results = null; } else BugContest.End(_game, false);
                return true;
            });
            Register("pbBugContestState.pbStartJudging", (c, a) => { BugContest.ComputeResults(_game); return true; });
            Register("pbBugContestState.pbGetPlaceInfo", (c, a) =>
            {
                var bs = BugContest.State(_game);
                int i = ToInt(Arg(a, 0));
                if (bs.Results == null || i < 0 || i >= bs.Results.Count) return null;
                var r = bs.Results[i];
                _game.State.SetStr(1, r.Name); _game.State.SetStr(2, r.Pokemon); _game.State.SetVar(3, r.Score);
                return new List<object> { r.Name, r.Pokemon, r.Score };
            });
            Register("pbBugContestState.pbContestHeld?", (c, a) =>
            {
                var bs = _game.State.Extras.BugContest;
                return bs != null && bs.LastContest > 0 && DateTime.Now.Ticks - bs.LastContest < TimeSpan.TicksPerDay;
            });
            Register("pbBugContestState.decision", (c, a) => BugContest.State(_game).Place);
            Register("pbBugContestState.pbJudge", (c, a) => { BugContest.Judge(_game, null); return true; });
            Register("pbBugContestState.place", (c, a) => BugContest.State(_game).Place);
            Register("pbBugContestState.ballcount", (c, a) => BugContest.State(_game).Balls);
            Register("pbBugContestState.lastPokemon", (c, a) => BugContest.State(_game).Caught);
            Register("pbBugContestStartOver", (c, a) => { BugContest.End(_game, true); return true; });

            // Battle Tower: pbBattleTower(rounds = 7, double = false, level = 50)
            Register("pbBattleFacility", (c, a) =>
            {
                string kind = Str(a, 0).TrimStart(':').ToLowerInvariant();
                BattleTower.Run(_game, a.Count > 1 ? ToInt(a[1]) : 7, a.Count > 2 && Truthy(a[2]), a.Count > 3 ? ToInt(a[3]) : 50, kind);
                return true;
            });
            Register("pbBattleTower", (c, a) => { BattleTower.Run(_game, a.Count > 0 ? ToInt(a[0]) : 7, a.Count > 1 && Truthy(a[1]), a.Count > 2 ? ToInt(a[2]) : 50); return true; });
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

            // NPCs following the player
            Register("Followers.add", (c, a) => { Followers.Add(_game, CharArg(c, Arg(a, 0)) as GameEvent, Str(a, 1), a.Count > 2 ? ToInt(a[2]) : 0); return true; });
            Register("pbAddDependency2", (c, a) => { Followers.Add(_game, CharArg(c, Arg(a, 0)) as GameEvent, Str(a, 1), a.Count > 2 ? ToInt(a[2]) : 0); return true; });
            Register("Followers.remove", (c, a) => { Followers.Remove(_game, Str(a, 0)); return true; });
            Register("pbRemoveDependency2", (c, a) => { Followers.Remove(_game, Str(a, 0)); return true; });
            Register("Followers.clear", (c, a) => { Followers.Clear(_game); return true; });
            Register("pbRemoveDependencies", (c, a) => { Followers.Clear(_game); return true; });
            Register("Followers.get", (c, a) => Followers.Characters.Any(f => f.Info.Name == Str(a, 0)));
            Register("Followers.follow_into_door", (c, a) => { Followers.IntoDoor(_game); if (c != null && Followers.Characters.Count > 0) c.Wait(8); return true; });
            Register("Followers.hide_followers", (c, a) => { Followers.Hide(); return true; });
            Register("Followers.put_followers_on_player", (c, a) => { Followers.PutOnPlayer(_game); return true; });

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

namespace PokeCreator.Runtime.Script
{
    public partial class EssentialsCompat
    {
        /// <summary>A bag screen showing only some items; the chosen item ID (or "NONE") goes into a variable.</summary>
        void ChooseItemInto(int variable, Func<PokeCreator.Data.ItemData, bool> filter)
        {
            if (!_game.Project.Items.Any(i => filter(i) && _game.State.ItemCount(i.Id) > 0)) { _game.State.SetStr(variable, "NONE"); return; }
            _game.PushScene(new UI.BagScene(_game, id => { _game.State.SetStr(variable, id); _game.CloseAllScenes(); }, filter, () => _game.State.SetStr(variable, "NONE")));
        }

        static bool HasFlag(PokeCreator.Data.ItemData i, string flag) { return i.Flags != null && i.Flags.Any(f => string.Equals(f, flag, StringComparison.OrdinalIgnoreCase)); }

        void RegisterMiscCalls()
        {
            Register("pbChooseFossil", (c, a) => { ChooseItemInto(a.Count > 0 ? ToInt(a[0]) : 1, i => HasFlag(i, "Fossil") || i.Id.EndsWith("FOSSIL") || i.Id == "OLDAMBER"); return true; });
            Register("pbChooseApricorn", (c, a) => { ChooseItemInto(a.Count > 0 ? ToInt(a[0]) : 1, i => HasFlag(i, "Apricorn") || i.Id.EndsWith("APRICORN")); return true; });
            Register("pbChooseItem", (c, a) => { ChooseItemInto(a.Count > 0 ? ToInt(a[0]) : 1, i => true); return true; });
            Register("pbChooseBallList", (c, a) => { ChooseItemInto(a.Count > 0 ? ToInt(a[0]) : 1, i => i.Pocket == 3); return true; });

            // pbGenerateEgg(species, owner): an Egg straight into the party
            Register("pbGenerateEgg", (c, a) =>
            {
                if (_game.State.Party.Count >= GameState.MaxParty) return false;
                var egg = PokeCreator.Pkmn.Pokemon.Create(_game.Project, Str(a, 0), 1, _game.Rng);
                var data = egg.Data(_game.Project);
                if (data == null) return false;
                egg.IsEgg = true;
                egg.EggSteps = data.HatchSteps > 1 ? data.HatchSteps : 5120;
                egg.OTName = a.Count > 1 && Str(a, 1).Length > 0 ? Str(a, 1) : _game.State.PlayerName; egg.OTId = _game.State.TrainerId;
                egg.MetMap = _game.Map != null ? _game.Map.Id : 0; egg.MetLevel = 1;
                _game.State.Party.Add(egg);
                return true;
            });

            // Choosing a Pokémon for an in-game trade
            Register("pbChoosePokemonForTrade", (c, a) =>
            {
                string sp = Str(a, 2);
                ChoosePokemon(a, pk => !pk.IsEgg && !pk.ShadowPokemon && (sp.Length == 0 || pk.Species == sp));
                return true;
            });
            Register("pbChooseTradablePokemon", (c, a) => { ChoosePokemon(a, pk => !pk.IsEgg && !pk.ShadowPokemon); return true; });

            // pbEnterNPCName(prompt, min, max, default, charset): asks, then the script runs again with the name
            Register("pbEnterNPCName", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                string def = Str(a, 3);
                int max = a.Count > 2 ? Math.Max(1, ToInt(a[2])) : 10;
                _game.PromptPending = true;
                _game.PushScene(new UI.NameEntryScene(_game, Str(a, 0), def, max, n => _game.SetPrompt(c, string.IsNullOrEmpty(n) ? def : n)));
                return def;
            });
            Register("pbEnterText", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                string def = Str(a, 3);
                _game.PromptPending = true;
                _game.PushScene(new UI.NameEntryScene(_game, Str(a, 0), def, a.Count > 2 ? Math.Max(1, ToInt(a[2])) : 12, n => _game.SetPrompt(c, n ?? "")));
                return def;
            });

            // Game Corner
            Register("pbSlotMachine", (c, a) => { _game.PushScene(new UI.SlotMachineScene(_game, a.Count > 0 ? ToInt(a[0]) : 1)); return true; });
            Register("pbVoltorbFlip", (c, a) => { _game.PushScene(new UI.VoltorbFlipScene(_game)); return true; });

            // Battle Frontier (Essentials' pbBattleChallenge)
            Register("pbBattleChallenge", (c, a) => new BattleChallengeApi(_game));
            Func<string, bool, bool, ChallengeRules> rules = (kind, dbl, open) => new ChallengeRules { Kind = kind, Double = dbl, OpenLevel = open, Size = dbl ? 4 : 3 };
            Register("pbBattleTowerRules", (c, a) => rules("tower", a.Count > 0 && Truthy(a[0]), a.Count > 1 && Truthy(a[1])));
            Register("pbBattleFactoryRules", (c, a) => rules("factory", a.Count > 0 && Truthy(a[0]), a.Count > 1 && Truthy(a[1])));
            Register("pbBattlePalaceRules", (c, a) => rules("palace", a.Count > 0 && Truthy(a[0]), a.Count > 1 && Truthy(a[1])));
            Register("pbBattleArenaRules", (c, a) => rules("arena", false, a.Count > 0 && Truthy(a[0])));
            foreach (var cup in new[] { "pbPikaCupRules", "pbPokeCupRules", "pbPrimeCupRules", "pbFancyCupRules", "pbLittleCupRules", "pbNiceCupRules", "pbChallengeCupRules" })
            {
                string cn = cup;
                Register(cn, (c, a) => new ChallengeRules { Kind = "cup", Double = a.Count > 0 && Truthy(a[0]), Size = 3, OpenLevel = cn == "pbPrimeCupRules" || cn == "pbChallengeCupRules", MaxLevel = cn == "pbLittleCupRules" ? 5 : cn == "pbPikaCupRules" ? 20 : 50 });
            }
            Register("pbHasEligible?", (c, a) =>
            {
                int need = a.Count > 0 ? ToInt(a[0]) : 3;
                return _game.State.Party.Where(p => !p.IsEgg && !p.ShadowPokemon).Select(p => p.Species).Distinct().Count() >= need;
            });
            Register("pbEntryScreen", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                int need = a.Count > 0 ? ToInt(a[0]) : 3;
                var fs = new BattleChallengeApi(_game);
                var entries = new List<int>();
                _game.PromptPending = true;
                Action pick = null;
                pick = () =>
                {
                    if (entries.Count >= need)
                    {
                        if (_game.State.Extras.Frontier == null) _game.State.Extras.Frontier = new FrontierState();
                        _game.State.Extras.Frontier.Entries = entries.ToList();
                        _game.SetPrompt(c, true); return;
                    }
                    _game.PushScene(new UI.PartyScene(_game, "Choose Pokémon " + (entries.Count + 1) + " of " + need + ".", null, (i, close) =>
                    {
                        var pk = _game.State.Party[i];
                        if (pk.IsEgg || entries.Contains(i) || entries.Any(j => _game.State.Party[j].Species == pk.Species)) { _game.Message.Show("That Pokémon can't be entered."); return; }
                        close(); entries.Add(i); pick();
                    }, () => _game.SetPrompt(c, false)));
                };
                pick();
                return false;
            });
            Register("pbBattleChallengeBattle", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                _game.PromptPending = true;
                new BattleChallengeApi(_game).Battle(won => _game.SetPrompt(c, won));
                return false;
            });
            Register("pbBattleChallenge.extra.pbChooseRentals", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                _game.PromptPending = true;
                new BattleChallengeApi(_game).ChooseRentals(ok => _game.SetPrompt(c, ok));
                return false;
            });
            Register("pbBattleChallenge.extra.pbChooseSwaps", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                _game.PromptPending = true;
                new BattleChallengeApi(_game).ChooseSwap(ok => _game.SetPrompt(c, ok));
                return false;
            });
            Register("pbBattleChallengeBeginSpeech", (c, a) => new BattleChallengeApi(_game).BeginSpeech());
            Register("pbBattleChallengeGraphic", (c, a) =>
            {
                var ev = CharArg(c, Arg(a, 0));
                string gfx = new BattleChallengeApi(_game).NextGraphic();
                if (ev != null && _game.Project.FindAsset("Graphics/Characters", gfx) != null) ev.CharacterName = gfx;
                return true;
            });
            Register("$game_temp.last_battle_record", (c, a) => World.BattleRecorder.Last);
            Register("$PokemonGlobal.lastbattle", (c, a) => _game.State.Extras.SavedBattle);
            Register("pbRecordLastBattle", (c, a) => { if (World.BattleRecorder.Last != null) _game.State.Extras.SavedBattle = World.BattleRecorder.Last; return true; });
            Register("pbPlayLastBattle", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                var rec = _game.State.Extras.SavedBattle;
                if (rec == null) { _game.Message.Show("There's no recorded battle."); return false; }
                _game.PromptPending = true;
                World.BattleRecorder.Play(_game, rec, () => _game.SetPrompt(c, true));
                return false;
            });
            Register("pbPlayBattle", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                var rec = (a.Count > 0 ? a[0] : null) as World.BattleRecord ?? World.BattleRecorder.Last;
                if (rec == null) return false;
                _game.PromptPending = true;
                World.BattleRecorder.Play(_game, rec, () => _game.SetPrompt(c, true));
                return false;
            });

            // Triple Triad
            Register("pbCanTriadDuel?", (c, a) => UI.TriadCards.Count(_game) >= 5);
            Register("pbGiveTriadCard", (c, a) => { UI.TriadCards.Give(_game, Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1); return true; });
            Register("pbTriadDuel", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                if (UI.TriadCards.Count(_game) < 5) { _game.Message.Show("You need at least 5 cards."); return 0; }
                _game.PromptPending = true;
                _game.PushScene(new UI.TriadDuelScene(_game, a.Count > 0 ? Str(a, 0) : "Opponent", a.Count > 1 ? ToInt(a[1]) : 0, a.Count > 2 ? ToInt(a[2]) : 100, r => _game.SetPrompt(c, r)));
                return 0;
            });
            Register("pbBuyTriads", (c, a) => { _game.PushScene(new UI.TriadShopScene(_game, false)); return true; });
            Register("pbSellTriads", (c, a) => { _game.PushScene(new UI.TriadShopScene(_game, true)); return true; });
            Register("pbTriadList", (c, a) => { _game.PushScene(new UI.TriadShopScene(_game, true, true)); return true; });

            Register("pbMiningGame", (c, a) => { _game.PushScene(new UI.MiningScene(_game)); return true; });
            Register("pbTilePuzzle", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                _game.PromptPending = true;
                _game.PushScene(new UI.TilePuzzleScene(_game, a.Count > 0 ? ToInt(a[0]) : 3, Str(a, 1), a.Count > 2 ? ToInt(a[2]) : 0, a.Count > 3 ? ToInt(a[3]) : 0, ok => _game.SetPrompt(c, ok)));
                return false;
            });
            Register("pbDuel", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                var tt = _game.Project.TrainerTypes.FirstOrDefault(t => t.Id == Str(a, 0));
                string foe = ((tt != null ? tt.Name + " " : "") + Str(a, 1)).Trim();
                _game.PromptPending = true;
                _game.PushScene(new UI.DuelScene(_game, foe.Length > 0 ? foe : "Opponent", ok => _game.SetPrompt(c, ok)));
                return false;
            });

            // Battle Point shop, Game Corner prizes, lottery
            Register("pbBattlePointShop", (c, a) =>
            {
                var stock = Arg(a, 0) as List<object>;
                _game.PushScene(new UI.MartScene(_game, stock == null ? new List<string>() : stock.Select(x => Convert.ToString(x)).ToList(), true) { BattlePoints = true });
                return true;
            });
            Register("pbBuyPrize", (c, a) =>
            {
                string id = Str(a, 0); int qty = a.Count > 1 ? Math.Max(1, ToInt(a[1])) : 1;
                if (_game.Project.Species.Any(s => s.Id == id)) return Call("pbAddPokemon", new List<object> { id, a.Count > 1 ? ToInt(a[1]) : 5 }, c);
                if (!_game.Project.Items.Any(i => i.Id == id)) return false;
                _game.State.AddItem(id, qty); return true;
            });
            Register("pbSetLotteryNumber", (c, a) =>
            {
                // The day's lucky number: the same all day
                var now = DateTime.Now;
                int n = new Random(now.Year * 1000 + now.DayOfYear + _game.State.TrainerId).Next(65536);
                _game.State.SetVar(a.Count > 0 ? ToInt(a[0]) : 1, n);
                return n;
            });
            Register("pbLottery", (c, a) =>
            {
                // pbLottery(number, nameVar, whereVar, matchedVar): the Pokémon whose ID No. matches the most last digits
                string win = (ToInt(Arg(a, 0)) % 100000).ToString("00000");
                int nameVar = a.Count > 1 ? ToInt(a[1]) : 2, whereVar = a.Count > 2 ? ToInt(a[2]) : 3, matchVar = a.Count > 3 ? ToInt(a[3]) : 4;
                int best = 0; string bestName = ""; int where = 0;
                Action<IEnumerable<PokeCreator.Pkmn.Pokemon>, int> check = (list, w) =>
                {
                    foreach (var pk in list.Where(p => p != null && !p.IsEgg))
                    {
                        string id = (pk.OTId % 100000).ToString("00000");
                        int m = 0; while (m < 5 && id[4 - m] == win[4 - m]) m++;
                        if (m > best) { best = m; bestName = pk.Name(_game.Project); where = w; }
                    }
                };
                check(_game.State.Party, 1); check(_game.State.Storage, 2);
                _game.State.SetStr(nameVar, bestName); _game.State.SetVar(whereVar, where); _game.State.SetVar(matchVar, best);
                return best;
            });

            Register("pbCanDoubleBattle?", (c, a) => _game.State.Party.Count(PokeCreator.Battles.Battle.Able) >= 2 || _game.State.Extras.Partner != null);
            Register("pbCanTripleBattle?", (c, a) => _game.State.Party.Count(PokeCreator.Battles.Battle.Able) >= 3);

            // pbHiddenPower(pkmn) → [type, power]
            Register("pbHiddenPower", (c, a) =>
            {
                var pk = Arg(a, 0) as PokeCreator.Pkmn.Pokemon;
                if (pk == null) return new List<object> { "NORMAL", 60 };
                string[] types = { "FIGHTING", "FLYING", "POISON", "GROUND", "ROCK", "BUG", "GHOST", "STEEL", "FIRE", "WATER", "GRASS", "ELECTRIC", "PSYCHIC", "ICE", "DRAGON", "DARK" };
                int[] order = { 0, 1, 2, 3, 4, 5 }; // HP, Atk, Def, Spe, SpA, SpD as stored
                int t = 0, pw = 0;
                int[] bit = { 0, 1, 2, 5, 3, 4 }; // game order: HP, Atk, Def, Spe, SpA, SpD -> weights 1,2,4,8,16,32
                for (int i = 0; i < 6; i++) { t |= (pk.IV[order[i]] & 1) << bit[i]; pw |= ((pk.IV[order[i]] >> 1) & 1) << bit[i]; }
                return new List<object> { types[t * 15 / 63], 30 + pw * 40 / 63 };
            });
            Register("GameData::Type.get", (c, a) => _game.Project.Types.FirstOrDefault(t => t.Id == Str(a, 0)));
            Register("GameData::Ability.get", (c, a) => _game.Project.Abilities.FirstOrDefault(t => t.Id == Str(a, 0)));
            Register("GameData::TrainerType.get", (c, a) => _game.Project.TrainerTypes.FirstOrDefault(t => t.Id == Str(a, 0)));
            Register("Pokemon::EV_LIMIT", (c, a) => PokeCreator.Pkmn.ItemEffects.MaxTotalEV);
            Register("Pokemon::EV_STAT_LIMIT", (c, a) => PokeCreator.Pkmn.ItemEffects.MaxEV);
            Register("Pokemon::IV_STAT_LIMIT", (c, a) => 31);
            Register("Phone.reset_after_win", (c, a) => true);
            Register("$player.pokedex.owned_shadow_pokemon?", (c, a) => _game.State.Party.Concat(_game.State.Storage).Any(p => p.ShadowPokemon && p.Species == Str(a, 0)));

            // Bug-Catching Contest extras used by Essentials' example events
            Register("pbBugContestState", (c, a) => new BugContestApi(_game));
            Register("pbBugContestState.pbContestHeld?", (c, a) => _game.State.Extras.BugContest != null && (_game.State.Extras.BugContest.InProgress || _game.State.Extras.BugContest.Undecided));
            Register("pbBugContestState.pbGetPlaceInfo", (c, a) => new List<object> { "", "", 0 });

            // pbEvolutionEvent(n): the n-th party Pokémon evolves if it can (by any method that's met now)
            Register("pbEvolutionEvent", (c, a) =>
            {
                // Evolutions with the Event / EventAfterDamageTaken method and this number
                string n = a.Count > 0 ? Convert.ToString(a[0]) : "";
                foreach (var pk in _game.State.Party.Where(p => !p.IsEgg))
                {
                    var d = pk.Data(_game.Project);
                    var evo = d == null || d.Evolutions == null ? null : d.Evolutions.FirstOrDefault(e => (e.Method ?? "").StartsWith("Event") && (string.IsNullOrEmpty(e.Parameter) || e.Parameter == n));
                    if (evo != null) { _game.PushScene(new UI.EvolutionScene(_game, pk, evo.Species, false, null)); return true; }
                }
                return false;
            });
        }
    }

    /// <summary>The object pbBugContestState returns in scripts (contest = pbBugContestState; contest.pbStart(20)).</summary>
    public class BugContestApi
    {
        readonly Game _g;
        public BugContestApi(Game g) { _g = g; }
        public void pbSetJudgingPoint(int map, int x, int y) { }
        public void pbSetReception(string name) { }
        public void pbSetContestMap(string name) { }
        public void pbSetPokemon(int index) { World.BugContest.SetPokemon(_g, index); }
        public void pbStart(int balls) { World.BugContest.Start(_g, balls); }
        public void pbEnd() { World.BugContest.End(_g, false); }
        public bool inProgress() { return World.BugContest.InProgress(_g); }
    }
}
