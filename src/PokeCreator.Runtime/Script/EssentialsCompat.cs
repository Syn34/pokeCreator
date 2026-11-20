using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PokeCreator.Runtime.World;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.Script
{
    /// <summary>
    /// Translates the Ruby one-liners that Essentials events put in "Script" commands and
    /// "Conditional Branch: Script" into engine actions. Unknown calls are logged and ignored,
    /// so a game keeps running while support grows. Register new commands with <see cref="Register"/>.
    /// </summary>
    public class EssentialsCompat
    {
        public delegate object Handler(Interpreter ctx, List<object> args);

        readonly Game _game;
        readonly Dictionary<string, Handler> _commands = new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _warned = new HashSet<string>();

        public EssentialsCompat(Game g)
        {
            _game = g;
            RegisterBuiltins();
        }

        public void Register(string name, Handler h) { _commands[name] = h; }

        // ------------------------------------------------------------------ entry points
        public void RunScript(string script, Interpreter ctx)
        {
            foreach (var stmt in SplitStatements(script))
            {
                try { Eval(stmt, ctx); }
                catch (Exception ex) { WarnOnce("Script error in \"" + stmt + "\": " + ex.Message); }
            }
        }

        public bool Evaluate(string expr, Interpreter ctx)
        {
            try { return Truthy(Eval(expr.Trim(), ctx)); }
            catch (Exception ex) { WarnOnce("Condition error in \"" + expr + "\": " + ex.Message); return false; }
        }

        static bool Truthy(object o) { return o != null && !(o is bool && !(bool)o); }

        void WarnOnce(string msg) { if (_warned.Add(msg)) Console.WriteLine("[essentials] " + msg); }

        static IEnumerable<string> SplitStatements(string s)
        {
            // Join lines that end with an open parenthesis/comma (multi-line calls), then split on newlines/semicolons.
            var joined = Regex.Replace(s, @"([\(,])\s*\n\s*", "$1");
            foreach (var line in joined.Split('\n'))
                foreach (var part in SplitTopLevel(line, ';'))
                {
                    var t = part.Trim();
                    if (t.Length > 0 && !t.StartsWith("#")) yield return t;
                }
        }

        // ------------------------------------------------------------------ tiny expression evaluator
        object Eval(string e, Interpreter ctx)
        {
            e = e.Trim();
            if (e.Length == 0) return null;
            if (e.StartsWith("(") && MatchingParen(e, 0) == e.Length - 1) return Eval(e.Substring(1, e.Length - 2), ctx);

            // Boolean operators (lowest precedence)
            foreach (var op in new[] { "||", " or ", "&&", " and " })
            {
                int i = FindTopLevel(e, op);
                if (i > 0)
                {
                    bool isOr = op.Trim() == "||" || op.Trim() == "or";
                    bool l = Truthy(Eval(e.Substring(0, i), ctx));
                    if (isOr && l) return true;
                    if (!isOr && !l) return false;
                    return Truthy(Eval(e.Substring(i + op.Length), ctx));
                }
            }
            if (e.StartsWith("!") && !e.StartsWith("!=")) return !Truthy(Eval(e.Substring(1), ctx));
            if (e.StartsWith("not ")) return !Truthy(Eval(e.Substring(4), ctx));

            // Assignment: $game_switches[n] = x, $game_variables[n] = x, $game_self_switches[[map,ev,"A"]] = x
            int asg = FindTopLevel(e, "=");
            if (asg > 0 && e[asg - 1] != '=' && e[asg - 1] != '!' && e[asg - 1] != '<' && e[asg - 1] != '>' && (asg + 1 >= e.Length || e[asg + 1] != '='))
            {
                string lhs = e.Substring(0, asg).Trim();
                char opc = lhs.Length > 0 ? lhs[lhs.Length - 1] : ' ';
                bool compound = "+-*/".IndexOf(opc) >= 0;
                if (compound) lhs = lhs.Substring(0, lhs.Length - 1).Trim();
                object val = Eval(e.Substring(asg + 1), ctx);
                if (compound) val = Arith(Eval(lhs, ctx), val, opc);
                Assign(lhs, val, ctx);
                return val;
            }

            // Comparisons
            foreach (var op in new[] { "==", "!=", ">=", "<=", ">", "<" })
            {
                int i = FindTopLevel(e, op);
                if (i > 0)
                {
                    var a = Eval(e.Substring(0, i), ctx); var b = Eval(e.Substring(i + op.Length), ctx);
                    return CompareValues(a, b, op);
                }
            }
            foreach (var op in new[] { "+", "-", "*", "/" })
            {
                int i = FindTopLevel(e, op);
                if (i > 0) return Arith(Eval(e.Substring(0, i), ctx), Eval(e.Substring(i + 1), ctx), op[0]);
            }

            // Literals
            if (e == "true") return true;
            if (e == "false") return false;
            if (e == "nil") return null;
            int n; if (int.TryParse(e, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            if (e.StartsWith(":") && Regex.IsMatch(e, @"^:\w+$")) return e.Substring(1);
            if ((e.StartsWith("\"") && e.EndsWith("\"")) || (e.StartsWith("'") && e.EndsWith("'"))) return Unquote(e);
            var intl = Regex.Match(e, @"^_I(?:NTL|SPRINTF)\((.*)\)$", RegexOptions.Singleline);
            if (intl.Success)
            {
                var parts = SplitTopLevel(intl.Groups[1].Value, ',').Select(p => Eval(p, ctx)).ToList();
                string fmt = Convert.ToString(parts[0]);
                for (int k = 1; k < parts.Count; k++) fmt = fmt.Replace("{" + k + "}", Convert.ToString(parts[k]));
                return fmt;
            }

            // Global state
            var m = Regex.Match(e, @"^\$game_switches\[(.+)\]$");
            if (m.Success) return _game.State.GetSwitch(ToInt(Eval(m.Groups[1].Value, ctx)));
            m = Regex.Match(e, @"^\$game_variables\[(.+)\]$");
            if (m.Success) return _game.State.GetVar(ToInt(Eval(m.Groups[1].Value, ctx)));
            m = Regex.Match(e, @"^\$game_self_switches\[\[(.+)\]\]$");
            if (m.Success)
            {
                var p = SplitTopLevel(m.Groups[1].Value, ',').Select(x => Eval(x, ctx)).ToList();
                return _game.State.GetSelf(ToInt(p[0]), ToInt(p[1]), Convert.ToString(p[2]));
            }
            if (e == "$game_map.map_id") return _game.Map.Id;
            if (e == "@event_id") return ctx != null ? ctx.EventId : 0;
            if (e == "$player.money" || e == "$Trainer.money") return _game.State.Money;
            if (e == "$player.name" || e == "$Trainer.name") return _game.State.PlayerName;
            if (Regex.IsMatch(e, @"^\$(player|Trainer)\.(party\.length|pokemon_count|party_count)$")) return _game.Party.Count;
            m = Regex.Match(e, @"^\$(?:player|Trainer)\.(has_\w+|pokedex)\??$");
            if (m.Success) return _game.State.Flags.Contains(m.Groups[1].Value == "pokedex" ? "has_pokedex" : m.Groups[1].Value);
            m = Regex.Match(e, @"^\$(?:player|Trainer)\.(?:pokedex\.)?(seen_count|owned_count|pokedexSeen|pokedexOwned)$");
            if (m.Success) return m.Groups[1].Value.StartsWith("seen") || m.Groups[1].Value == "pokedexSeen" ? _game.State.Seen.Count : _game.State.Caught.Count;

            // Method call:  name(args)  or  Obj.name(args)  or bare name
            m = Regex.Match(e, @"^([\$@]?[\w:.]+[?!]?)\s*(?:\((.*)\))?\s*(\{.*\})?$", RegexOptions.Singleline);
            if (m.Success)
            {
                string name = m.Groups[1].Value;
                var args = m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0
                    ? SplitTopLevel(m.Groups[2].Value, ',').Select(a => Eval(a, ctx)).ToList()
                    : new List<object>();
                return Call(name, args, ctx);
            }
            WarnOnce("Can't understand script: " + e);
            return null;
        }

        object Call(string name, List<object> args, Interpreter ctx)
        {
            string key = name;
            Handler h;
            if (_commands.TryGetValue(key, out h)) return h(ctx, args);
            int dot = name.LastIndexOf('.');
            if (dot > 0 && _commands.TryGetValue(name.Substring(dot + 1), out h)) return h(ctx, args);
            WarnOnce("Unsupported Essentials call: " + name + " (ignored)");
            return null;
        }

        void Assign(string lhs, object val, Interpreter ctx)
        {
            var m = Regex.Match(lhs, @"^\$game_switches\[(.+)\]$");
            if (m.Success) { _game.State.SetSwitch(ToInt(Eval(m.Groups[1].Value, ctx)), Truthy(val)); _game.Map.RefreshEvents(); return; }
            m = Regex.Match(lhs, @"^\$game_variables\[(.+)\]$");
            if (m.Success) { _game.State.SetVar(ToInt(Eval(m.Groups[1].Value, ctx)), ToInt(val)); _game.Map.RefreshEvents(); return; }
            m = Regex.Match(lhs, @"^\$game_self_switches\[\[(.+)\]\]$");
            if (m.Success)
            {
                var p = SplitTopLevel(m.Groups[1].Value, ',').Select(x => Eval(x, ctx)).ToList();
                _game.State.SetSelf(ToInt(p[0]), ToInt(p[1]), Convert.ToString(p[2]), Truthy(val)); _game.Map.RefreshEvents(); return;
            }
            if (lhs == "$player.money" || lhs == "$Trainer.money") { _game.State.Money = ToInt(val); return; }
            if (lhs == "$player.name" || lhs == "$Trainer.name") { _game.State.PlayerName = Convert.ToString(val); return; }
            if (lhs == "$player.character_ID" || lhs == "$Trainer.character_ID") { _game.ChangePlayer(ToInt(val) - 1); return; }
            var fm = Regex.Match(lhs, @"^\$(?:player|Trainer)\.(has_\w+|pokedex)$");
            if (fm.Success)
            {
                string flag = fm.Groups[1].Value == "pokedex" ? "has_pokedex" : fm.Groups[1].Value;
                if (Truthy(val)) _game.State.Flags.Add(flag); else _game.State.Flags.Remove(flag);
                return;
            }
            WarnOnce("Can't assign to " + lhs);
        }

        // ------------------------------------------------------------------ built-in commands
        void RegisterBuiltins()
        {
            Handler noop = (c, a) => true;
            foreach (var n in new[] { "pbTrainerIntro", "pbTrainerEnd", "pbNoticePlayer", "pbCallBub", "pbFadeOutIn",
                                      "pbWait", "pbSEPlay", "pbBGMPlay", "pbMEPlay", "pbUpdateSceneMap", "pbExclaim", "pbTurnTowardEvent",
                                      "pbMoveRoute", "pbPhoneRegisterBattle", "pbPhoneRegister", "pbShowMap", "pbTownMap", "pbSetEscapePoint",
                                      "pbEraseEscapePoint", "pbDayCareDeposit", "pbDayCareWithdraw", "pbRegisterPartner", "pbDeregisterPartner",
                                      "pbBridgeOn", "pbBridgeOff", "pbStartOver", "pbUnlockWallpaper" })
                Register(n, noop);

            Register("pbCommonEvent", (c, a) => c != null && c.CallCommonEvent(ToInt(Arg(a, 0))));
            Register("pbMessage", (c, a) => { _game.Message.Show(Str(a, 0)); return 0; });
            Register("Kernel.pbMessage", (c, a) => { _game.Message.Show(Str(a, 0)); return 0; });
            Register("pbConfirmMessage", (c, a) =>
            {
                bool result = false;
                _game.Message.Show(Str(a, 0), new List<string> { "Yes", "No" }, 1, r => result = r == 0);
                return true; // answered asynchronously; conditions default to "yes"
            });

            Register("setTempSwitchOn", (c, a) => { SetSelf(c, Str(a, 0), true); return true; });
            Register("setTempSwitchOff", (c, a) => { SetSelf(c, Str(a, 0), false); return true; });
            Register("pbSetSelfSwitch", (c, a) =>
            {
                int ev = ToInt(Arg(a, 0)); string ch = Str(a, 1); bool v = a.Count < 3 || Truthy(a[2]);
                int map = a.Count > 3 ? ToInt(a[3]) : _game.Map.Id;
                _game.State.SetSelf(map, ev, ch, v); _game.Map.RefreshEvents(); return true;
            });
            Register("get_character", (c, a) => ToInt(Arg(a, 0)));
            Register("get_self", (c, a) => 0);
            Register("pbMapInterpreter", (c, a) => c);

            Register("pbReceiveItem", (c, a) => GiveItem(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1, "obtained"));
            Register("pbItemBall", (c, a) => GiveItem(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1, "found"));
            Register("pbGetItem", (c, a) => GiveItem(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1, "obtained"));
            Register("$bag.add", (c, a) => { _game.State.AddItem(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1); return true; });
            Register("$bag.has?", (c, a) => _game.State.Bag.ContainsKey(Str(a, 0)));
            Register("$PokemonBag.pbStoreItem", (c, a) => { _game.State.AddItem(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1); return true; });

            Register("pbAddPokemon", (c, a) => AddPokemon(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 5, true));
            Register("pbAddPokemonSilent", (c, a) => AddPokemon(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 5, false));
            Register("pbAddToParty", (c, a) => AddPokemon(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 5, true));
            Handler heal = (c, a) => { foreach (var pk in _game.State.Party) pk.Heal(_game.Project); return true; };
            Register("pbHealAll", heal);
            Register("$player.heal_party", heal);
            Register("$Trainer.heal_party", heal);
            Register("$player.able_pokemon_count", (c, a) => _game.State.Party.Count(pk => !pk.Fainted));
            Register("$player.party_full?", (c, a) => _game.State.Party.Count >= GameState.MaxParty);
            Register("pbBoxesFull?", (c, a) => false);
            Register("pbPokeCenterPC", (c, a) => { _game.Message.Show("The PC isn't available in this version of PokeCreator yet."); return true; });
            Register("pbTrainerPC", (c, a) => { _game.Message.Show("The PC isn't available in this version of PokeCreator yet."); return true; });
            Register("pbPokemonMart", (c, a) => { _game.Message.Show("Shops are coming in a future version of PokeCreator."); return true; });
            Register("pbSellItem", (c, a) => true);

            // Battles. The event waits for the battle; a Conditional Branch re-checks afterwards and gets the result.
            Register("pbSetPokemonCenter", (c, a) =>
            {
                if (_game.Map == null) return true;
                _game.State.HealMap = _game.Map.Id; _game.State.HealX = _game.Player.X; _game.State.HealY = _game.Player.Y; _game.State.HealDir = 2;
                return true;
            });
            Register("setBattleRule", (c, a) => { _battleRules[Str(a, 0)] = a.Count > 1 ? a[1] : true; return true; });
            Register("TrainerBattle.start", (c, a) => TrainerBattle(Str(a, 0), Str(a, 1), a.Count > 2 && !(a[2] is string) ? ToInt(a[2]) : 0, c));
            Register("pbTrainerBattle", (c, a) =>
            {
                if (a.Count > 5 && Truthy(a[5])) _battleRules["canLose"] = true;
                return TrainerBattle(Str(a, 0), Str(a, 1), a.Count > 4 ? ToInt(a[4]) : 0, c);
            });
            Register("pbDoubleTrainerBattle", (c, a) => TrainerBattle(Str(a, 0), Str(a, 1), a.Count > 2 && !(a[2] is string) ? ToInt(a[2]) : 0, c));
            Register("WildBattle.start", (c, a) => WildBattle(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 5, false, c));
            Register("pbWildBattle", (c, a) =>
            {
                if (a.Count > 4 && Truthy(a[4])) _battleRules["canLose"] = true;
                if (a.Count > 2 && a[2] != null) _battleRules["outcomeVar"] = ToInt(a[2]);
                return WildBattle(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 5, true, c);
            });

            // Variables, bag and screen helpers
            Register("pbSet", (c, a) => { _game.State.SetVar(ToInt(Arg(a, 0)), ToInt(Arg(a, 1))); _game.Map.RefreshEvents(); return Arg(a, 1); });
            Register("pbGet", (c, a) => _game.State.GetVar(ToInt(Arg(a, 0))));
            Register("$bag.remove", (c, a) => { bool had = _game.State.Bag.ContainsKey(Str(a, 0)); _game.State.AddItem(Str(a, 0), -(a.Count > 1 ? ToInt(a[1]) : 1)); return had; });
            Register("$bag.can_add?", (c, a) => true);
            Register("$bag.quantity", (c, a) => { int n; return _game.State.Bag.TryGetValue(Str(a, 0), out n) ? n : 0; });
            Register("$player.has_species?", (c, a) => _game.Party.Any(p => p.Species.Equals(Str(a, 0), StringComparison.OrdinalIgnoreCase)));
            Register("Tone.new", (c, a) => a.Select(x => (object)ToInt(x)).ToList());
            Register("pbToneChangeAll", (c, a) => { _game.SetScreenTone(Arg(a, 0) as List<object>, ToInt(Arg(a, 1)) * 3 / 2); return true; });
            foreach (var n in new[] { "hide_choice", "setBattleRule", "$game_screen.weather", "Phone.add", "pbPickBerry", "setPrice", "setVariable", "$speeches.push", "pbToneChangeAll" })
                if (!_commands.ContainsKey(n)) Register(n, noop);
            Register("Phone.can_add?", (c, a) => false);

            Register("pbGetPokemon", (c, a) => null);
            Register("pbPartyCount", (c, a) => _game.Party.Count);
            Register("pbChooseNonEggPokemon", (c, a) => true);
            Register("pbEventCanReachPlayer?", (c, a) => false);
            Register("pbFacingTerrainTag", (c, a) => 0);
            Register("pbGetTimeNow", (c, a) => DateTime.Now);
            Register("PBDayNight.isNight?", (c, a) => DateTime.Now.Hour < 6 || DateTime.Now.Hour >= 20);
            Register("PBDayNight.isDay?", (c, a) => DateTime.Now.Hour >= 6 && DateTime.Now.Hour < 20);
            Register("pbIsWeekday", (c, a) => true);
            // Pokédex
            Register("set_seen", (c, a) => { string sp = Convert.ToString(Arg(a, 0)); if (!string.IsNullOrEmpty(sp)) _game.State.Seen.Add(sp.ToUpperInvariant()); return true; });
            Register("set_owned", (c, a) => { string sp = Convert.ToString(Arg(a, 0)); if (!string.IsNullOrEmpty(sp)) { _game.State.Seen.Add(sp.ToUpperInvariant()); _game.State.Caught.Add(sp.ToUpperInvariant()); } return true; });
            Register("seen?", (c, a) => _game.State.Seen.Contains(Convert.ToString(Arg(a, 0)).ToUpperInvariant()));
            Register("owned?", (c, a) => _game.State.Caught.Contains(Convert.ToString(Arg(a, 0)).ToUpperInvariant()));
            Register("pbSeenForm", (c, a) => { string sp = Convert.ToString(Arg(a, 0)); if (!string.IsNullOrEmpty(sp)) _game.State.Seen.Add(sp.ToUpperInvariant()); return true; });
            // Name entry: pbEnterPlayerName / pbTrainerName open the name screen and wait for it
            Register("pbTrainerName", (c, a) => { _game.AskName("Your name?", _game.State.PlayerName, 12, n => _game.State.PlayerName = n, c); return true; });
            Register("pbEnterPlayerName", (c, a) => { _game.AskName(Arg(a, 0) != null ? Convert.ToString(Arg(a, 0)) : "Your name?", _game.State.PlayerName, Arg(a, 2) != null ? ToInt(Arg(a, 2)) : 12, n => _game.State.PlayerName = n, c); return true; });
            Register("pbChangePlayerName", (c, a) => true);
            // Player characters are numbered from 1 in Essentials (metadata [1], [2]...)
            Register("pbChangePlayer", (c, a) => { _game.ChangePlayer(ToInt(Arg(a, 0)) - 1); return true; });
            Register("rand", (c, a) => _game.Rng.Next(Math.Max(1, ToInt(Arg(a, 0)))));
        }

        void SetSelf(Interpreter c, string ch, bool v)
        {
            if (c == null || c.EventId <= 0) return;
            _game.State.SetSelf(c.MapId, c.EventId, ch, v); _game.Map.RefreshEvents();
        }

        object GiveItem(string id, int qty, string verb)
        {
            var item = _game.Project.Items.FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            string name = item == null ? id : (qty > 1 && !string.IsNullOrEmpty(item.NamePlural) ? item.NamePlural : item.Name);
            _game.State.AddItem(id, qty);
            _game.Audio.PlayMe(item != null && item.Pocket == 8 ? "Key item get" : "Item get");
            _game.Message.Show("\\PN " + verb + " " + (qty > 1 ? qty + " " : (StartsWithVowel(name) ? "an " : "a ")) + name + "!");
            return true;
        }

        readonly Dictionary<string, object> _battleRules = new Dictionary<string, object>();

        bool TakeRule(string name) { object v; bool on = _battleRules.TryGetValue(name, out v) && Truthy(v); return on; }

        /// <summary>Starts a trainer battle, or (when the event re-checks after it) returns whether the player won.</summary>
        object TrainerBattle(string type, string name, int version, Interpreter ctx)
        {
            if (_game.ResumeOutcome.HasValue && ctx != null && _game.ResumeOwner == ctx)
            {
                var o = _game.ResumeOutcome.Value; _game.ResumeOutcome = null;
                return o == PokeCreator.Battles.BattleOutcome.Win;
            }
            bool canLose = TakeRule("canLose");
            int outcomeVar = OutcomeVar();
            _battleRules.Clear();
            if (!_game.StartTrainerBattle(type, name, version, canLose, o => _game.State.SetVar(outcomeVar, (int)o)))
            {
                WarnOnce("Trainer battle skipped (no trainer " + type + " \"" + name + "\" in the database, or no Pokémon that can battle)");
                return true;
            }
            return false;
        }

        int OutcomeVar() { object v; return _battleRules.TryGetValue("outcomeVar", out v) ? ToInt(v) : 1; }

        /// <summary>Starts a wild battle, or returns its outcome (1 won, 2 lost, 3 ran, 4 caught) when re-checked.</summary>
        object WildBattle(string species, int level, bool asBool, Interpreter ctx)
        {
            if (_game.ResumeOutcome.HasValue && ctx != null && _game.ResumeOwner == ctx)
            {
                var o = _game.ResumeOutcome.Value; _game.ResumeOutcome = null;
                return asBool ? (object)(o != PokeCreator.Battles.BattleOutcome.Loss) : (int)o;
            }
            int outcomeVar = OutcomeVar();
            bool canLose = TakeRule("canLose");
            _battleRules.Clear();
            _game.StartWildBattle(species, level, o => _game.State.SetVar(outcomeVar, (int)o));
            return asBool ? (object)true : 1;
        }

        object AddPokemon(string species, int level, bool announce)
        {
            var pk = PokeCreator.Pkmn.Pokemon.Create(_game.Project, species, level, _game.Rng);
            if (pk.Data(_game.Project) == null) { WarnOnce("Unknown species " + species); return false; }
            pk.OTName = _game.State.PlayerName; pk.OTId = _game.State.TrainerId; pk.MetMap = _game.Map.Id;
            bool toParty = _game.State.Party.Count < GameState.MaxParty;
            if (toParty) _game.State.Party.Add(pk); else _game.State.Storage.Add(pk);
            _game.State.Register(pk);
            if (announce)
            {
                _game.Audio.PlayMe("Pkmn get");
                string name = pk.Name(_game.Project);
                _game.Message.Show("\\PN obtained " + name + "!" + (toParty ? "" : "\\n" + name + " was sent to the PC."), null, -1, _ =>
                    _game.Message.Show("Would you like to give a nickname to " + name + "?", new List<string> { "Yes", "No" }, 1, c =>
                    {
                        if (c == 0) _game.AskName(name + "'s nickname?", "", 12, n => pk.Nickname = n, null);
                    }));
            }
            return true;
        }

        string SpeciesName(string id)
        {
            var sp = _game.Project.Species.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            return sp != null ? sp.Name : id;
        }

        static bool StartsWithVowel(string s) { return s.Length > 0 && "AEIOUaeiou".IndexOf(s[0]) >= 0; }

        // ------------------------------------------------------------------ parsing helpers
        static object Arg(List<object> a, int i) { return i < a.Count ? a[i] : null; }
        static string Str(List<object> a, int i) { return i < a.Count && a[i] != null ? Convert.ToString(a[i], CultureInfo.InvariantCulture) : ""; }
        static int ToInt(object o)
        {
            if (o == null) return 0;
            if (o is bool) return (bool)o ? 1 : 0;
            int n; return int.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), out n) ? n : 0;
        }

        static object CompareValues(object a, object b, string op)
        {
            int ia, ib;
            bool numeric = int.TryParse(Convert.ToString(a), out ia) & int.TryParse(Convert.ToString(b), out ib);
            if (numeric)
                switch (op) { case "==": return ia == ib; case "!=": return ia != ib; case ">=": return ia >= ib; case "<=": return ia <= ib; case ">": return ia > ib; case "<": return ia < ib; }
            bool eq = Equals(a, b) || string.Equals(Convert.ToString(a), Convert.ToString(b));
            return op == "==" ? eq : op == "!=" ? !eq : (object)false;
        }

        static object Arith(object a, object b, char op)
        {
            if (op == '+' && (a is string || b is string)) return Convert.ToString(a) + Convert.ToString(b);
            int x = ToInt(a), y = ToInt(b);
            switch (op) { case '+': return x + y; case '-': return x - y; case '*': return x * y; case '/': return y == 0 ? 0 : x / y; }
            return 0;
        }

        static string Unquote(string s)
        {
            var sb = new StringBuilder();
            for (int i = 1; i < s.Length - 1; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length - 1 && (s[i + 1] == '"' || s[i + 1] == '\'' || s[i + 1] == '\\')) { sb.Append(s[i + 1]); i++; }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        static int MatchingParen(string s, int open)
        {
            int depth = 0; bool inStr = false; char q = '\0';
            for (int i = open; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == q) inStr = false; continue; }
                if (c == '"' || c == '\'') { inStr = true; q = c; continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        /// <summary>Index of op outside strings/brackets, or -1.</summary>
        static int FindTopLevel(string s, string op)
        {
            int depth = 0; bool inStr = false; char q = '\0';
            for (int i = 0; i <= s.Length - op.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == q) inStr = false; continue; }
                if (c == '"' || c == '\'') { inStr = true; q = c; continue; }
                if (c == '(' || c == '[' || c == '{') { depth++; continue; }
                if (c == ')' || c == ']' || c == '}') { depth--; continue; }
                if (depth == 0 && i > 0 && string.CompareOrdinal(s, i, op, 0, op.Length) == 0)
                {
                    // Don't treat "==" as "=" or "<=" as "<" etc.
                    if (op == "=" && (i + 1 < s.Length && s[i + 1] == '=')) { i++; continue; }
                    if ((op == ">" || op == "<") && i + 1 < s.Length && s[i + 1] == '=') continue;
                    if ((op == "-" || op == "+") && "=<>!(,+-*/".IndexOf(s[i - 1]) >= 0) continue;
                    return i;
                }
            }
            return -1;
        }

        static List<string> SplitTopLevel(string s, char sep)
        {
            var parts = new List<string>();
            int depth = 0, start = 0; bool inStr = false; char q = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == q) inStr = false; continue; }
                if (c == '"' || c == '\'') { inStr = true; q = c; continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == sep && depth == 0) { parts.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(s.Substring(start));
            return parts;
        }
    }
}
