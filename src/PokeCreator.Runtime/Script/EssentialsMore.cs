using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PokeCreator.Pkmn;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime.Script
{
    /// <summary>More Essentials calls: seasons and months, time events, party checks, levels, Pokédexes, screen effects, music.</summary>
    public partial class EssentialsCompat
    {
        static readonly string[] MonthNames = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

        void RegisterMoreCalls()
        {
            // ---- Seasons (Essentials: the season changes every month: Jan spring, Feb summer, Mar autumn, Apr winter, May spring…)
            Func<int> season = () => (DateTime.Now.Month - 1) % 4;
            Register("pbGetSeason", (c, a) => season());
            Register("pbIsSpring?", (c, a) => season() == 0);
            Register("pbIsSummer?", (c, a) => season() == 1);
            Register("pbIsAutumn?", (c, a) => season() == 2);
            Register("pbIsFall?", (c, a) => season() == 2);
            Register("pbIsWinter?", (c, a) => season() == 3);
            Register("pbIsSeason", (c, a) =>
            {
                int s = season();
                if (a.Count > 0 && ToInt(a[0]) > 0) _game.State.SetVar(ToInt(a[0]), s);
                return a.Skip(1).Any(x => ToInt(x) == s);
            });
            Register("pbGetSeasonName", (c, a) => new[] { "Spring", "Summer", "Autumn", "Winter" }[a.Count > 0 ? ((ToInt(a[0]) % 4) + 4) % 4 : season()]);
            Register("pbIsMonth", (c, a) =>
            {
                int m = DateTime.Now.Month;
                if (a.Count > 0 && ToInt(a[0]) > 0) _game.State.SetVar(ToInt(a[0]), m);
                return a.Skip(1).Any(x => ToInt(x) == m);
            });
            Register("pbGetMonthName", (c, a) => MonthNames[Math.Max(1, Math.Min(12, a.Count > 0 ? ToInt(a[0]) : DateTime.Now.Month)) - 1]);
            Register("pbGetAbbrevMonthName", (c, a) => MonthNames[Math.Max(1, Math.Min(12, a.Count > 0 ? ToInt(a[0]) : DateTime.Now.Month)) - 1].Substring(0, 3));
            Register("pbGetWeekdayName", (c, a) => CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(a.Count > 0 ? (DayOfWeek)(((ToInt(a[0]) % 7) + 7) % 7) : DateTime.Now.DayOfWeek));
            Register("pbGetTimeNow.mon", (c, a) => DateTime.Now.Month);
            Register("pbGetTimeNow.month", (c, a) => DateTime.Now.Month);
            Register("pbGetTimeNow.day", (c, a) => DateTime.Now.Day);
            Register("pbGetTimeNow.mday", (c, a) => DateTime.Now.Day);
            Register("pbGetTimeNow.year", (c, a) => DateTime.Now.Year);
            Register("pbGetTimeNow.min", (c, a) => DateTime.Now.Minute);
            Register("pbGetTimeNow.sec", (c, a) => DateTime.Now.Second);
            Register("pbGetTimeNow.yday", (c, a) => DateTime.Now.DayOfYear);

            // ---- Time events: pbTimeEvent(var, secs) starts a timer stored with a variable; pbTimeEventValid(var) says if it ran out
            Func<int, Dictionary<string, long>> te = v => { if (_game.State.Extras.TimeEvents == null) _game.State.Extras.TimeEvents = new Dictionary<string, long>(); return _game.State.Extras.TimeEvents; };
            Register("pbTimeEvent", (c, a) =>
            {
                int v = ToInt(Arg(a, 0)); var d = te(v);
                d[v + ":t"] = DateTime.Now.Ticks; d[v + ":w"] = (a.Count > 1 ? ToInt(a[1]) : 0) * TimeSpan.TicksPerSecond; d.Remove(v + ":days");
                _game.State.SetVar(v, 1); return true;
            });
            Register("pbTimeEventDays", (c, a) =>
            {
                int v = ToInt(Arg(a, 0)); var d = te(v);
                d[v + ":t"] = DateTime.Now.Date.Ticks; d[v + ":days"] = a.Count > 1 ? ToInt(a[1]) : 0; d.Remove(v + ":w");
                _game.State.SetVar(v, 1); return true;
            });
            Register("pbTimeEventValid", (c, a) =>
            {
                int v = ToInt(Arg(a, 0)); var d = te(v);
                long t;
                if (!d.TryGetValue(v + ":t", out t)) return false;
                long days, wait;
                bool done = d.TryGetValue(v + ":days", out days) ? (DateTime.Now.Date - new DateTime(t)).TotalDays >= days
                                                                : DateTime.Now.Ticks - t >= (d.TryGetValue(v + ":w", out wait) ? wait : 0);
                if (done) { d.Remove(v + ":t"); d.Remove(v + ":w"); d.Remove(v + ":days"); _game.State.SetVar(v, 0); }
                return done;
            });

            // ---- Party checks
            Register("pbHasSpecies?", (c, a) => _game.State.Party.Any(p => !p.IsEgg && p.Species == Str(a, 0) && (a.Count < 2 || ToInt(a[1]) < 0 || p.Form == ToInt(a[1]))));
            Register("pbHasFatefulSpecies?", (c, a) => _game.State.Party.Any(p => !p.IsEgg && p.Species == Str(a, 0)));
            Register("pbHasType?", (c, a) => _game.State.Party.Any(p => !p.IsEgg && p.Types(_game.Project).Contains(Str(a, 0))));
            Register("pbCheckMove", (c, a) => _game.State.Party.FirstOrDefault(p => !p.IsEgg && p.Moves.Any(m => m.Id == Str(a, 0))));
            Register("pbSeenForm?", (c, a) => _game.State.Seen.Contains(Str(a, 0)));
            Register("pbCheckAble", (c, a) => { int skip = ToInt(Arg(a, 0)); return _game.State.Party.Where((p, i) => i != skip).Any(p => !p.IsEgg && !p.Fainted); });
            Register("pbAllFainted", (c, a) => !_game.State.Party.Any(p => !p.IsEgg && !p.Fainted));
            Register("pbFirstAble", (c, a) => _game.State.Party.FirstOrDefault(p => !p.IsEgg && !p.Fainted));
            Register("$player.first_able_pokemon", (c, a) => _game.State.Party.FirstOrDefault(p => !p.IsEgg && !p.Fainted));
            Register("$player.first_pokemon", (c, a) => _game.State.Party.FirstOrDefault(p => !p.IsEgg));
            Register("pbBalancedLevel", (c, a) =>
            {
                var party = (Arg(a, 0) as System.Collections.IList ?? _game.State.Party).Cast<object>().OfType<Pokemon>().Where(p => !p.IsEgg).ToList();
                if (party.Count == 0) return 1;
                // Essentials' formula leans toward the stronger Pokémon: mean plus a share of the spread
                double mean = party.Average(p => p.Level), sd = Math.Sqrt(party.Average(p => (p.Level - mean) * (p.Level - mean)));
                return Math.Max(1, Math.Min(GrowthRates.MaxLevel, (int)Math.Round(mean + sd / 2)));
            });
            Register("pbGetPlayerCharset", (c, a) => _game.Player.CharacterName);

            // ---- Levels: pbChangeLevel(pkmn, level, scene) shows the change and learns moves on the way up
            Handler changeLevel = (c, a) =>
            {
                var pk = Arg(a, 0) as Pokemon; if (pk == null || pk.IsEgg) return false;
                int to = Math.Max(1, Math.Min(GrowthRates.MaxLevel, ToInt(Arg(a, 1)))), from = pk.Level;
                var d = pk.Data(_game.Project);
                pk.Level = to; pk.Exp = GrowthRates.ExpForLevel(d != null ? d.GrowthRate : "Medium", to);
                if (to > from)
                {
                    _game.Message.Show(pk.Name(_game.Project) + " grew to Lv. " + to + "!");
                    for (int lv = from + 1; lv <= to; lv++)
                        foreach (var mv in Growth.MovesAtLevel(_game.Project, pk, lv))
                            if (Growth.TryLearn(_game.Project, pk, mv)) { var md = _game.Project.Moves.FirstOrDefault(x => x.Id == mv); _game.Message.Show(pk.Name(_game.Project) + " learned " + (md != null ? md.Name : mv) + "!"); }
                }
                else if (to < from) _game.Message.Show(pk.Name(_game.Project) + " was downgraded to Lv. " + to + ".");
                else _game.Message.Show(pk.Name(_game.Project) + "'s level remained unchanged.");
                pk.HP = Math.Min(pk.HP, pk.TotalHP(_game.Project));
                return true;
            };
            Register("pbChangeLevel", changeLevel);
            Register("pbSetLevel", changeLevel);

            // ---- Pokédexes
            Register("pbUnlockDex", (c, a) => { DexState.Unlock(_game, a.Count > 0 ? ToInt(a[0]) : -1); return true; });
            Register("pbLockDex", (c, a) => { DexState.Lock(_game, a.Count > 0 ? ToInt(a[0]) : -1); return true; });
            Register("$player.pokedex.unlock", (c, a) => { DexState.Unlock(_game, a.Count > 0 ? ToInt(a[0]) : -1); return true; });
            Register("$player.pokedex.lock", (c, a) => { DexState.Lock(_game, a.Count > 0 ? ToInt(a[0]) : -1); return true; });
            Register("$Trainer.pokedex.unlock", (c, a) => { DexState.Unlock(_game, a.Count > 0 ? ToInt(a[0]) : -1); return true; });
            Register("$Trainer.pokedex.lock", (c, a) => { DexState.Lock(_game, a.Count > 0 ? ToInt(a[0]) : -1); return true; });
            Register("$player.pokedex.unlocked?", (c, a) => DexState.IsUnlocked(_game, a.Count > 0 ? ToInt(a[0]) : -1));
            Register("pbSetViableDexes", (c, a) => true);
            Register("pbGetRegionalNumber", (c, a) => DexState.RegionalNumber(_game, ToInt(Arg(a, 0)), Str(a, 1)));
            Register("pbGetLanguage", (c, a) => 2);
            Register("pbPokedexHasForm", (c, a) => _game.State.Seen.Contains(Str(a, 0)));

            // ---- Screen and music
            Register("Color.new", (c, a) => a.Select(x => (object)ToInt(x)).ToList());
            Register("pbShake", (c, a) => { _game.Shake(a.Count > 0 ? ToInt(a[0]) : 5, a.Count > 1 ? ToInt(a[1]) : 5, a.Count > 2 ? ToInt(a[2]) : 10); return true; });
            Register("pbFlash", (c, a) => { _game.Flash(a.Count > 1 ? Math.Max(1, ToInt(a[1]) * 3 / 2) : 15); return true; });
            Register("pbSEStop", (c, a) => true);
            Register("pbBGSFade", (c, a) => true);
            Register("pbMEFade", (c, a) => true);
            Register("pbCueBGM", (c, a) => { _game.Audio.FadeOutBgm(1); _game.Audio.PlayBgm(Str(a, 0)); return true; });
            Register("pbGetWildBattleBGM", (c, a) => _game.Project.Settings.WildBattleBGM ?? "Battle wild");
            Register("pbGetTrainerBattleBGM", (c, a) => _game.Project.Settings.TrainerBattleBGM ?? "Battle trainer");
            Register("pbCanFly?", (c, a) => FieldMoves.WithMove(_game, "FLY") != null && _game.Map != null && _game.Map.Data.Properties != null && _game.Map.Data.Properties.Outdoor);
            Register("pbRoamingAreas", (c, a) =>
            {
                var defs = _game.Project.Settings.Roamers; int i = ToInt(Arg(a, 0));
                if (defs == null || i < 0 || i >= defs.Count || string.IsNullOrEmpty(defs[i].Maps)) return new List<object>();
                return defs[i].Maps.Split(',').Select(x => { int m; return int.TryParse(x.Trim(), out m) ? (object)m : null; }).Where(x => x != null).ToList();
            });
        }
    }
}
namespace PokeCreator.Runtime.Script
{
    public partial class EssentialsCompat
    {
        void RegisterMoreCalls2()
        {
            Register("pbMoveTutorAnnotations", (c, a) =>
            {
                string mv = Str(a, 0);
                var list = Arg(a, 1) as System.Collections.IList;
                return _game.State.Party.Select(p =>
                {
                    if (p.IsEgg) return (object)"NOT ABLE";
                    if (p.Moves.Any(m => m.Id == mv)) return "LEARNED";
                    var d = p.Data(_game.Project);
                    bool ok = list != null ? list.Cast<object>().Any(x => Convert.ToString(x, CultureInfo.InvariantCulture) == p.Species) : Growth.CanLearnFromItem(_game.Project, p, mv);
                    return ok ? "ABLE" : "NOT ABLE";
                }).ToList();
            });
            Register("pbRemovePokemonAt", (c, a) =>
            {
                int i = ToInt(Arg(a, 0));
                if (i < 0 || i >= _game.State.Party.Count || _game.State.Party.Count(p => !p.IsEgg) <= 1 && !_game.State.Party[i].IsEgg) return false;
                _game.State.Party.RemoveAt(i); return true;
            });
            Register("pbStorePokemon", (c, a) => AddPokemonArg(a, true));
            Register("pbNicknameAndStore", (c, a) => AddPokemonArg(a, true));
            Register("pbAutoplayOnTransition", (c, a) => { _game.PlayMapMusic(); return true; });
            Register("pbPlayTrainerIntroME", (c, a) =>
            {
                var tt = _game.Project.TrainerTypes.FirstOrDefault(t => t.Id == Str(a, 0));
                if (tt != null && !string.IsNullOrEmpty(tt.IntroBGM)) _game.Audio.PlayMe(tt.IntroBGM);
                return true;
            });
            Register("pbJumpToward", (c, a) =>
            {
                int dist = a.Count > 0 ? Math.Max(1, ToInt(a[0])) : 1, d = _game.Player.Direction;
                int dx = d == 4 ? -dist : d == 6 ? dist : 0, dy = d == 8 ? -dist : d == 2 ? dist : 0;
                if (!_game.Map.Data.Valid(_game.Player.X + dx, _game.Player.Y + dy)) return false;
                if (a.Count > 1 && Truthy(a[1])) _game.Audio.PlaySe("Player jump");
                _game.Player.Jump(dx, dy);
                if (c != null) c.WaitForMoves();
                return true;
            });
            Register("pbLedge", (c, a) => false); // ledges are jumped by walking into them
            Register("pbSmashEvent", (c, a) =>
            {
                var ev = (Arg(a, 0) as GameEvent) ?? (c != null ? c.GetCharacter(a.Count > 0 && !(a[0] is GameEvent) ? ToInt(a[0]) : 0) as GameEvent : null);
                if (ev == null) return false;
                _game.Audio.PlaySe("Rock Smash");
                ev.Erase();
                if (c != null && ev.Id == c.EventId) _game.State.SetSelf(c.MapId, c.EventId, "A", true);
                return true;
            });
            Register("pbPushThisEvent", (c, a) => { FieldMoves.PushThisBoulder(_game, c); return true; });
            Register("pbHatchAnimation", (c, a) => { var pk = Arg(a, 0) as Pokemon; if (pk != null && pk.IsEgg) DayCare.Hatch(_game, pk); return true; });
            Register("pbHatch", (c, a) => { var pk = Arg(a, 0) as Pokemon; if (pk != null && pk.IsEgg) DayCare.Hatch(_game, pk); return true; });
            Register("pbFlyToNewLocation", (c, a) => false);
            Register("pbEncounter", (c, a) => FieldMoves.EncounterFrom(_game, new[] { Str(a, 0).TrimStart(':') }));
            Register("pbFadeOutInWithMusic", (c, a) => true);
            Register("pbScrollMap", (c, a) => { _game.ScrollMap(ToInt(Arg(a, 0)), ToInt(Arg(a, 1)), a.Count > 2 ? ToInt(a[2]) : 4); if (c != null) c.WaitForScroll(); return true; });
            Register("pbEnterPokemonName", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                _game.PromptPending = true;
                int max = a.Count > 2 ? Math.Max(1, ToInt(a[2])) : 12;
                _game.AskName(Str(a, 0), a.Count > 3 ? Str(a, 3) : "", max, n => _game.SetPrompt(c, n ?? ""), null);
                return "";
            });
            Register("pbEnterBoxName", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                _game.PromptPending = true;
                _game.AskName(Str(a, 0), a.Count > 3 ? Str(a, 3) : "", 12, n => _game.SetPrompt(c, n ?? ""), null);
                return "";
            });
            Register("pbPCItemStorage", (c, a) => { UI.PcMenu.PlayerPc(_game, null); return true; });
            Register("pbPCMailbox", (c, a) => { UI.PcMenu.Mailbox(_game, () => { }); return true; });
            Register("pbDive", (c, a) => FieldMoves.TryDive(_game));
            Register("pbSurfacing", (c, a) => FieldMoves.TryDive(_game));
            Register("pbAscendWaterfall", (c, a) => { FieldMoves.OfferWaterfall(_game); return true; });
            Register("pbDescendWaterfall", (c, a) => { FieldMoves.OfferWaterfall(_game); return true; });
            Register("pbBikeCheck", (c, a) => FieldMoves.CanBike(_game));
            Register("pbSweetScent", (c, a) => { FieldMoves.SweetScent(_game, _game.State.Party.FirstOrDefault(p => !p.IsEgg)); return true; });
            Register("pbGetPlayerTrainerType", (c, a) => _game.Player.Look != null ? _game.Player.Look.TrainerType : "");
            Register("pbLoadTrainer", (c, a) => _game.Project.Trainers.FirstOrDefault(t => t.TrainerType == Str(a, 0) && t.Name == Str(a, 1) && t.Version == (a.Count > 2 ? ToInt(a[2]) : 0)));
            Register("pbUseItemOnPokemon", (c, a) =>
            {
                var pk = Arg(a, 1) as Pokemon; string item = Str(a, 0);
                if (pk == null || string.IsNullOrEmpty(item)) return false;
                var it = _game.Project.Items.FirstOrDefault(x => x.Id == item); if (it == null) return false;
                UI.ItemFlow.UseOn(_game, pk, it, ItemEffects.Info(_game.Project, it), ok => { });
                return true;
            });
            Register("pbStoreMail", (c, a) =>
            {
                var pk = Arg(a, 0) as Pokemon; if (pk == null) return false;
                pk.Item = Str(a, 1); pk.Mail = new PokemonMail { Item = Str(a, 1), Message = Str(a, 2), Sender = _game.State.PlayerName };
                return true;
            });
            Register("pbMoveToMailbox", (c, a) =>
            {
                var pk = Arg(a, 0) as Pokemon; if (pk == null || pk.Mail == null) return false;
                if (_game.State.Extras.Mailbox == null) _game.State.Extras.Mailbox = new List<PokemonMail>();
                if (_game.State.Extras.Mailbox.Count >= 10) return false;
                _game.State.Extras.Mailbox.Add(pk.Mail); pk.Mail = null; pk.Item = null; return true;
            });
            Register("pbDisplayMail", (c, a) =>
            {
                var m = Arg(a, 0) as PokemonMail ?? (Arg(a, 0) is Pokemon ? ((Pokemon)Arg(a, 0)).Mail : null);
                if (m != null) _game.PushScene(new UI.MailScene(_game, m));
                return true;
            });
            // $PokemonStorage: boxes as lists ($PokemonStorage[box][slot]) plus the usual queries
            Register("$PokemonStorage", (c, a) => _game.State.Storage.Boxes.Select(b => (object)b.Slots).ToList());
            Register("$PokemonStorage.full?", (c, a) => _game.State.Storage.Full);
            Register("$PokemonStorage.maxBoxes", (c, a) => _game.State.Storage.Boxes.Count);
            Register("$PokemonStorage.maxPokemon", (c, a) => PokemonStorage.BoxSize);
            Register("$PokemonStorage.currentBox", (c, a) => _game.State.Storage.Current);
            Register("$PokemonStorage.pbStoreCaught", (c, a) => { var pk = Arg(a, 0) as Pokemon; return pk == null ? -1 : _game.State.Storage.TryAdd(pk); });
            Register("$PokemonStorage.pbFirstFreePos", (c, a) => { int b = ToInt(Arg(a, 0)); return b >= 0 && b < _game.State.Storage.Boxes.Count ? _game.State.Storage.Boxes[b].Slots.IndexOf(null) : -1; });
            Register("$PokemonStorage.boxName", (c, a) => { int b = ToInt(Arg(a, 0)); return b >= 0 && b < _game.State.Storage.Boxes.Count ? _game.State.Storage.Boxes[b].Name : ""; });
            Register("$game_system.save_disabled", (c, a) => _game.State.Flags.Contains("save_disabled"));
            Register("$game_system.menu_disabled", (c, a) => _game.State.Flags.Contains("menu_disabled"));
            Register("$game_system.encounter_disabled", (c, a) => _game.State.Flags.Contains("encounter_disabled"));
            Register("pbStringToAudioFile", (c, a) => new Dictionary<string, object> { { "name", Str(a, 0) }, { "volume", 100 }, { "pitch", 100 } });
            Register("RPG::AudioFile.new", (c, a) => new Dictionary<string, object> { { "name", Str(a, 0) }, { "volume", a.Count > 1 ? ToInt(a[1]) : 100 }, { "pitch", a.Count > 2 ? ToInt(a[2]) : 100 } });
            Register("Game.save", (c, a) => { try { SaveSystem.Save(_game); return true; } catch { return false; } });
            Register("pbSaveGame", (c, a) => { try { SaveSystem.Save(_game); return true; } catch { return false; } });
            Handler showMap = (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                _game.PromptPending = true;
                var scene = new UI.TownMapScene(_game, false, null);
                scene.OnClosed = () => _game.SetPrompt(c, true);
                _game.PushScene(scene);
                return false;
            };
            Register("pbShowMap", showMap);
            Register("pbTownMap", showMap);
        }
    }
}
