using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PokeCreator.Runtime.UI;
using PokeCreator.Runtime.World;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.Script
{
    /// <summary>
    /// Translates the Ruby one-liners that Essentials events put in "Script" commands and
    /// "Conditional Branch: Script" into engine actions. Unknown calls are logged and ignored,
    /// so a game keeps running while support grows. Register new commands with <see cref="Register"/>.
    /// </summary>
    public partial class EssentialsCompat
    {
        public delegate object Handler(Interpreter ctx, List<object> args);

        readonly Game _game;
        readonly Dictionary<string, Handler> _commands = new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary>Every script call name the game understands (for the Studio's checks).</summary>
        public static List<string> KnownCalls()
        {
            var c = new EssentialsCompat(null);
            return c._commands.Keys.ToList();
        }

        public EssentialsCompat(Game g)
        {
            _game = g;
            RegisterBuiltins();
            RegisterMoreCalls();
            RegisterMoreCalls2();
        }

        public void Register(string name, Handler h) { _commands[name] = h; }

        // ------------------------------------------------------------------ entry points
        /// <summary>Ruby local variables (pkmn = pbGetPokemon(1)) for the script being run.</summary>
        Dictionary<string, object> _locals = new Dictionary<string, object>();
        /// <summary>Ruby global variables set by scripts.</summary>
        readonly Dictionary<string, object> _globals = new Dictionary<string, object>();

        public void RunScript(string script, Interpreter ctx)
        {
            _locals = ctx != null ? ctx.Locals : new Dictionary<string, object>();
            var stmts = SplitStatements(script).ToList();
            if (true)
            {
                int pos = 0;
                var tree = ParseBlock(stmts, ref pos);
                _steps = 0;
                ExecBlock(tree, ctx);
                return;
            }
            for (int i = 0; i < stmts.Count; i++)
            {
                string stmt = stmts[i];
                // "<party>.each do |pkmn| ... end": run simple per-Pokémon statements
                var each = Regex.Match(stmt, @"^\$(?:player|Trainer)\.(?:pokemon_party|party|able_party)\.each\s+do\s*\|(\w+)\|$");
                if (each.Success)
                {
                    var body = new List<string>();
                    int depth = 1;
                    for (i++; i < stmts.Count; i++)
                    {
                        if (Regex.IsMatch(stmts[i], @"\bdo\b(\s*\|[^|]*\|)?$")) depth++;
                        if (stmts[i] == "end" && --depth == 0) break;
                        body.Add(stmts[i]);
                    }
                    EachPokemon(each.Groups[1].Value, body);
                    continue;
                }
                try { Eval(stmt, ctx); }
                catch (Exception ex) { WarnOnce("Script error in \"" + stmt + "\": " + ex.Message); }
                if (_game.PromptPending) break; // waiting for the player; the script runs again afterwards
            }
        }

        /// <summary>Statements inside a party .each block: heal, ribbons (not tracked), status cures.</summary>
        void EachPokemon(string v, List<string> body)
        {
            foreach (var line in body)
            {
                string l = line.Trim();
                if (l == v + ".heal") { foreach (var pk in _game.State.Party) pk.Heal(_game.Project); continue; }
                if (l == v + ".heal_status") { foreach (var pk in _game.State.Party) pk.Status = ""; continue; }
                if (l.StartsWith(v + ".giveRibbon") || l.StartsWith(v + ".record_first_moves")) continue; // ribbons aren't tracked
                WarnOnce("Unsupported statement in a party .each block: " + l);
            }
        }

        public bool Evaluate(string expr, Interpreter ctx)
        {
            _locals = new Dictionary<string, object>();
            try { return Truthy(Eval(expr.Trim(), ctx)); }
            catch (Exception ex) { WarnOnce("Condition error in \"" + expr + "\": " + ex.Message); return false; }
        }

        static bool Truthy(object o) { return o != null && !(o is bool && !(bool)o); }

        void WarnOnce(string msg) { if (_warned.Add(msg)) Console.WriteLine("[essentials] " + msg); }

        /// <summary>Inside a { |x| … } block spread over lines, a line break ends a statement (becomes ';').</summary>
        static string BlockLineBreaks(string s)
        {
            if (s.IndexOf('{') < 0) return s;
            var buf = s.ToCharArray(); var stack = new Stack<char>(); bool inStr = false; char q = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == q) inStr = false; continue; }
                if (c == '"' || c == '\'') { inStr = true; q = c; continue; }
                if (c == '#' && (i + 1 >= s.Length || s[i + 1] != '{')) { while (i + 1 < s.Length && s[i + 1] != '\n') i++; continue; }
                if (c == '(' || c == '[' || c == '{') stack.Push(c);
                else if ((c == ')' || c == ']' || c == '}') && stack.Count > 0) stack.Pop();
                else if (c == '\n' && stack.Count > 0 && stack.Peek() == '{')
                {
                    int p = i - 1; while (p >= 0 && (s[p] == ' ' || s[p] == '\t' || s[p] == '\r')) p--;
                    int nx = i + 1; while (nx < s.Length && (s[nx] == ' ' || s[nx] == '\t' || s[nx] == '\r')) nx++;
                    if (p >= 0 && ",{|([+-*/=&<>\\".IndexOf(s[p]) < 0 && nx < s.Length && s[nx] != '}') buf[i] = ';';
                }
            }
            return new string(buf);
        }

        static IEnumerable<string> SplitStatements(string s)
        {
            s = BlockLineBreaks(s);
            // Statements end at a newline or ';' outside brackets and strings, so calls and arrays can span lines
            var parts = new List<string>();
            int depth = 0, start = 0; bool inStr = false; char q = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == q) inStr = false; continue; }
                if (c == '"' || c == '\'') { inStr = true; q = c; continue; }
                if (c == '#' && depth == 0 && !(i + 1 < s.Length && s[i + 1] == '{')) { int hashAt = i; while (i < s.Length && s[i] != '\n') i++; parts.Add(s.Substring(start, hashAt - start)); start = i + 1; continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth = Math.Max(0, depth - 1);
                else if ((c == '\n' || c == ';') && depth == 0) { parts.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            if (start < s.Length) parts.Add(s.Substring(start));
            foreach (var part in parts)
            {
                var t = Regex.Replace(part, @"\s*\n\s*", " ").Trim();
                if (t.Length > 0 && !t.StartsWith("#")) yield return t;
            }
        }

        // ------------------------------------------------------------------ tiny expression evaluator
        object Eval(string e, Interpreter ctx)
        {
            e = e.Trim();
            if (e.Length == 0) return null;
            if (e.StartsWith("(") && MatchingParen(e, 0) == e.Length - 1) return Eval(e.Substring(1, e.Length - 2), ctx);

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

            // Ternary: cond ? a : b
            int qm = FindTopLevel(e, " ? ");
            if (qm > 0)
            {
                int colon = FindTopLevel(e.Substring(qm + 3), " : ");
                if (colon >= 0)
                {
                    string rest = e.Substring(qm + 3);
                    return Truthy(Eval(e.Substring(0, qm), ctx)) ? Eval(rest.Substring(0, colon), ctx) : Eval(rest.Substring(colon + 3), ctx);
                }
            }
            // Boolean operators
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
            // Ranges: 0...16, 1..5
            var rg = Regex.Match(e, @"^(-?\w+|\([^()]*\))\s*(\.\.\.?)\s*(-?\w+|\([^()]*\))$");
            if (rg.Success && !e.Contains("\""))
            {
                object lo = Eval(rg.Groups[1].Value, ctx), hi = Eval(rg.Groups[3].Value, ctx);
                if ((lo is int) && (hi is int))
                {
                    int l = (int)lo, h = (int)hi + (rg.Groups[2].Value == "..." ? 0 : 1);
                    var r = new List<object>(); for (int k = l; k < h && r.Count < 100000; k++) r.Add(k); return r;
                }
            }
            // Arithmetic, left to right: a - b + c
            foreach (var group in new[] { new[] { "+", "-" }, new[] { "*", "/", "%" } })
            {
                int best = -1; string bop = null;
                foreach (var op in group) { int i = FindLastTopLevel(e, op); if (i > best) { best = i; bop = op; } }
                if (best > 0) return Arith(Eval(e.Substring(0, best), ctx), Eval(e.Substring(best + 1), ctx), bop[0]);
            }

            // One-line blocks: list.each { |x| ... }, party.count { |p| p.able? }, ivs.map { |v| v * 2 }
            if (e.EndsWith("}"))
            {
                int ob = -1;
                for (int k = 0; k < e.Length; k++)
                {
                    if (e[k] == '"' || e[k] == '\'') { int cl = e.IndexOf(e[k], k + 1); if (cl < 0) break; k = cl; continue; }
                    if (e[k] == '{' && MatchingBracket(e, k) == e.Length - 1) { ob = k; break; }
                    if (e[k] == '(' || e[k] == '[' || e[k] == '{') { int cl = MatchingBracket(e, k); if (cl < 0) break; k = cl; }
                }
                var plain = ob > 0 ? Regex.Match(e.Substring(0, ob), @"^(pbFadeOutIn|pbFadeOutInWithMusic|pbFadeOutAndHide|pbHideVisibleObjects|pbRunWithFade)(?:\((.*)\))?\s*$") : Match.Empty;
                if (plain.Success)
                {
                    // A fade around a block: the block runs (the screen flashes briefly)
                    var body = SplitStatements(e.Substring(ob + 1, e.Length - ob - 2).Replace(";", "\n")).ToList();
                    int bp = 0; ExecBlock(ParseBlock(body, ref bp), ctx);
                    return true;
                }
                var bm = ob > 0 ? Regex.Match(e.Substring(0, ob), @"^(.+)\.(\w+[?!]?)(?:\((.*)\))?\s*$") : Match.Empty;
                if (bm.Success)
                {
                    var inner = Regex.Match(e.Substring(ob + 1, e.Length - ob - 2), @"^\s*\|\s*([^|]*)\|(.*)$", RegexOptions.Singleline);
                    string vars = inner.Success ? inner.Groups[1].Value : "", code = inner.Success ? inner.Groups[2].Value : e.Substring(ob + 1, e.Length - ob - 2);
                    return BlockCall(Eval(bm.Groups[1].Value, ctx), bm.Groups[2].Value, bm.Groups[3].Success ? Eval(bm.Groups[3].Value, ctx) : null, vars.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToArray(), code.Trim(), ctx);
                }
            }

            // A member of a literal or a bracketed value: [1, 2].include?(2), (1..10).to_a, list.map { … }.max
            {
                int lastDot = -1;
                for (int k = 0; k < e.Length; k++)
                {
                    char ch = e[k];
                    if (ch == '"' || ch == '\'') { int cl = e.IndexOf(ch, k + 1); if (cl < 0) break; k = cl; continue; }
                    if (ch == '(' || ch == '[' || ch == '{') { int cl = MatchingBracket(e, k); if (cl < 0) break; k = cl; continue; }
                    if (ch == '.' && k > 0 && (e[k - 1] == ']' || e[k - 1] == ')' || e[k - 1] == '}') && k + 1 < e.Length && char.IsLetter(e[k + 1])) lastDot = k;
                }
                if (lastDot > 0)
                {
                    var lm2 = Regex.Match(e.Substring(lastDot + 1), @"^(\w+[?!]?)(?:\((.*)\))?$");
                    string recvText = e.Substring(0, lastDot);
                    if (lm2.Success && (recvText.StartsWith("[") || recvText.StartsWith("(") || recvText.EndsWith("}") || recvText.EndsWith("]")))
                    {
                        var margs = lm2.Groups[2].Success && lm2.Groups[2].Value.Trim().Length > 0 ? SplitTopLevel(lm2.Groups[2].Value, ',').Select(x => Eval(x, ctx)).ToList() : new List<object>();
                        return Member(Eval(recvText, ctx), lm2.Groups[1].Value, margs);
                    }
                }
            }

            // Literals
            if (e == "true") return true;
            if (e == "false") return false;
            if (e == "nil") return null;
            int n; if (int.TryParse(e, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            double dbl; if (Regex.IsMatch(e, @"^-?\d+\.\d+$") && double.TryParse(e, NumberStyles.Float, CultureInfo.InvariantCulture, out dbl)) return dbl;
            if (e.StartsWith(":") && Regex.IsMatch(e, @"^:\w+$")) return e.Substring(1);
            if ((e.StartsWith("\"") && e.EndsWith("\"")) || (e.StartsWith("'") && e.EndsWith("'"))) return Unquote(e);
            if (e.StartsWith("[") && MatchingBracket(e, 0) == e.Length - 1)
            {
                string inner = e.Substring(1, e.Length - 2).Trim();
                return inner.Length == 0 ? new List<object>() : SplitTopLevel(inner, ',').Where(x => x.Trim().Length > 0).Select(x => Eval(x, ctx)).ToList();
            }
            var intl = Regex.Match(e, @"^_I(?:NTL|SPRINTF)?\((.*)\)$", RegexOptions.Singleline);
            if (intl.Success)
            {
                var parts = SplitTopLevel(intl.Groups[1].Value, ',').Select(p => Eval(p, ctx)).ToList();
                string fmt = Convert.ToString(parts[0]);
                for (int k = 1; k < parts.Count; k++) fmt = fmt.Replace("{" + k + "}", Convert.ToString(parts[k]));
                return fmt;
            }

            // Global state
            var m = Regex.Match(e, @"^\$game_switches\[(.+)\]$");
            if (m.Success) return _game.SwitchOn(ToInt(Eval(m.Groups[1].Value, ctx)), ctx != null ? ctx.MapId : _curMap, ctx != null ? ctx.EventId : _curEvent);
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
            m = Regex.Match(e, @"^\$(?:player|Trainer)\.(has_\w+|pokedex|seen_purify_chamber)\??$");
            if (m.Success) return _game.State.Flags.Contains(m.Groups[1].Value == "pokedex" ? "has_pokedex" : m.Groups[1].Value);
            m = Regex.Match(e, @"^\$(?:player|Trainer)\.(?:pokedex\.)?(seen_count|owned_count|pokedexSeen|pokedexOwned)$");
            if (m.Success) return m.Groups[1].Value.StartsWith("seen") || m.Groups[1].Value == "pokedexSeen" ? _game.State.Seen.Count : _game.State.Caught.Count;

            m = Regex.Match(e, @"^Settings::(\w+)$");
            if (m.Success) return SettingsConstant(m.Groups[1].Value);
            if (e == "$player.coins" || e == "$Trainer.coins") return _game.State.Coins;
            if (e == "$player.battle_points") return _game.State.BattlePoints;
            m = Regex.Match(e, @"^\$stats\.set_time_to_(\w+?)(?:\((.*)\))?$");
            if (m.Success) { World.GameStats.SetTimeTo(_game, m.Groups[1].Value + (m.Groups[2].Success && m.Groups[2].Value != "" ? "_" + Eval(m.Groups[2].Value, ctx) : "")); return true; }
            m = Regex.Match(e, @"^\$stats\.(\w+)\[(.+)\]$");
            if (m.Success) return World.GameStats.Int(_game, m.Groups[1].Value + "_" + ToInt(Eval(m.Groups[2].Value, ctx)));
            if (Regex.IsMatch(e, @"^\$[A-Za-z_]\w*$") && _globals.ContainsKey(e)) return _globals[e];
            m = Regex.Match(e, @"^\$stats\.(\w+)$");
            if (m.Success) return World.GameStats.Get(_game, m.Groups[1].Value);
            if (e == "$player.badge_count" || e == "$Trainer.numbadges") return _game.State.Badges.Count;
            m = Regex.Match(e, @"^\$(?:player|Trainer)\.badges\[(.+)\]$");
            if (m.Success) return _game.State.Badges.Contains(ToInt(Eval(m.Groups[1].Value, ctx)));

            // Indexing a local: pkmn.iv[:HP]
            m = Regex.Match(e, @"^([a-z_]\w*(?:\.\w+[?!]?)*)\[(.+)\]$");
            if (m.Success && _locals.ContainsKey(m.Groups[1].Value.Split('.')[0]))
            {
                var coll = Eval(m.Groups[1].Value, ctx);
                var idx = Eval(m.Groups[2].Value, ctx);
                var ivs = coll as List<int>;
                if (ivs != null) { int s = StatIndex(idx); return s >= 0 && s < ivs.Count ? (object)ivs[s] : null; }
                var dict = coll as Dictionary<string, object>;
                if (dict != null) { object dv; return dict.TryGetValue(Convert.ToString(idx, CultureInfo.InvariantCulture), out dv) ? dv : null; }
                var lst = coll as System.Collections.IList;
                if (lst != null) { int li = ToInt(idx); if (li < 0) li += lst.Count; return li >= 0 && li < lst.Count ? lst[li] : null; }
            }

            // Indexing any value: pbHiddenPower(pkmn)[0], list[1], hash[:key]
            if (e.EndsWith("]") && !e.StartsWith("[") && !e.StartsWith("$game_"))
            {
                int open = -1;
                for (int i = 0; i < e.Length; i++)
                {
                    if (e[i] == '"' || e[i] == '\'') { int close = e.IndexOf(e[i], i + 1); if (close < 0) break; i = close; continue; }
                    if (e[i] == '[' && i > 0 && MatchingBracket(e, i) == e.Length - 1) { open = i; break; }
                    if (e[i] == '(' || e[i] == '{') { int close = MatchingBracket(e, i); if (close < 0) break; i = close; }
                }
                if (open > 0)
                {
                    var coll = Eval(e.Substring(0, open), ctx);
                    var idx = Eval(e.Substring(open + 1, e.Length - open - 2), ctx);
                    var dict2 = coll as Dictionary<string, object>;
                    if (dict2 != null) { object dv; return dict2.TryGetValue(Convert.ToString(idx, CultureInfo.InvariantCulture), out dv) ? dv : null; }
                    var ivs2 = coll as List<int>;
                    if (ivs2 != null) { int s = idx is string ? StatIndex(idx) : ToInt(idx); return s >= 0 && s < ivs2.Count ? (object)ivs2[s] : null; }
                    var lst2 = coll as System.Collections.IList;
                    if (lst2 != null) { int li = ToInt(idx); if (li < 0) li += lst2.Count; return li >= 0 && li < lst2.Count ? lst2[li] : null; }
                    var str2 = coll as string;
                    if (str2 != null) { int si = ToInt(idx); return si >= 0 && si < str2.Length ? str2.Substring(si, 1) : null; }
                }
            }

            // Ruby hash literal: { :A => :B, "x" => 1 }
            if (e.StartsWith("{") && MatchingBracket(e, 0) == e.Length - 1 && FindTopLevel(e.Substring(1, e.Length - 2), "=>") >= 0)
            {
                var h = new Dictionary<string, object>();
                foreach (var pair in SplitTopLevel(e.Substring(1, e.Length - 2), ','))
                {
                    int arrow = FindTopLevel(pair, "=>");
                    if (arrow < 0) continue;
                    h[Convert.ToString(Eval(pair.Substring(0, arrow), ctx), CultureInfo.InvariantCulture)] = Eval(pair.Substring(arrow + 2), ctx);
                }
                return h;
            }
            if (e == "{}") return new Dictionary<string, object>();

            // proc { |pkmn| ... }  (filters passed to pbChoosePokemon and friends)
            var pm = Regex.Match(e, @"^proc\s*(?:\{\s*\|(\w+)\|(.*)\}|do\s*\|(\w+)\|(.*)end)$", RegexOptions.Singleline);
            if (pm.Success) return new ScriptProc { Param = pm.Groups[1].Success && pm.Groups[1].Value.Length > 0 ? pm.Groups[1].Value : pm.Groups[3].Value, Body = (pm.Groups[2].Success && pm.Groups[2].Value.Length > 0 ? pm.Groups[2].Value : pm.Groups[4].Value).Trim() };

            // Member of a call's result: pbGetPokemon(1).egg?, GameData::Item.get(x).name
            var cm = Regex.Match(e, @"^[\$@]?[\w:.]+[?!]?\s*\(");
            if (cm.Success)
            {
                int open = cm.Length - 1, close = MatchingParen(e, open);
                if (close > 0 && close + 1 < e.Length && e[close + 1] == '.')
                {
                    var rest = Regex.Match(e.Substring(close + 2), @"^(\w+[?!]?)\s*(?:\((.*)\))?$", RegexOptions.Singleline);
                    if (rest.Success)
                    {
                        object obj = Eval(e.Substring(0, close + 1), ctx);
                        var margs = rest.Groups[2].Success && rest.Groups[2].Value.Trim().Length > 0 ? SplitTopLevel(rest.Groups[2].Value, ',').Select(x => Eval(x, ctx)).ToList() : new List<object>();
                        return Member(obj, rest.Groups[1].Value, margs);
                    }
                }
            }

            // Method call:  name(args)  or  Obj.name(args)  or bare name
            m = Regex.Match(e, @"^([\$@]?[\w:.]+[?!]?)\s*(?:\((.*)\))?\s*(\{.*\})?$", RegexOptions.Singleline);
            if (m.Success)
            {
                string name = m.Groups[1].Value;
                var args = m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0
                    ? SplitTopLevel(m.Groups[2].Value, ',').Select(a => Eval(a, ctx)).ToList()
                    : new List<object>();
                // Local variables and their members: pkmn, pkmn.name, data.price
                string first = name.Split('.')[0];
                if (Regex.IsMatch(first, @"^[a-z_]\w*$") && _locals.ContainsKey(first))
                {
                    object obj = _locals[first];
                    var members = name.Split('.').Skip(1).ToList();
                    for (int k = 0; k < members.Count; k++) obj = Member(obj, members[k], k == members.Count - 1 ? args : new List<object>());
                    return obj;
                }
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
            // A script-made global's member: $speeches.push(text)
            int gd = name.IndexOf('.');
            if (name.StartsWith("$") && gd > 0 && _globals.ContainsKey(name.Substring(0, gd)))
            {
                object gobj = _globals[name.Substring(0, gd)];
                var gparts = name.Substring(gd + 1).Split('.');
                for (int k = 0; k < gparts.Length; k++) gobj = Member(gobj, gparts[k], k == gparts.Length - 1 ? args : new List<object>());
                return gobj;
            }
            // The longest registered start: $game_temp.last_battle_record.nil?, pbBattleChallenge.extra.pbPrepareSwaps
            int dot = name.LastIndexOf('.');
            for (int cut = dot; cut > 0; cut = name.LastIndexOf('.', cut - 1))
            {
                if (!_commands.TryGetValue(name.Substring(0, cut), out h)) continue;
                var parts = name.Substring(cut + 1).Split('.');
                Handler direct;
                object obj = h(ctx, new List<object>());
                if (parts.Length == 1 && (obj == null || obj is Interpreter) && parts[0] != "nil?" && _commands.TryGetValue(parts[0], out direct)) return direct(ctx, args);
                for (int k = 0; k < parts.Length; k++) obj = Member(obj, parts[k], k == parts.Length - 1 ? args : new List<object>());
                return obj;
            }
            if (dot > 0 && _commands.TryGetValue(name.Substring(dot + 1), out h)) return h(ctx, args);
            WarnOnce("Unsupported Essentials call: " + name + " (ignored)");
            return null;
        }

        void Assign(string lhs, object val, Interpreter ctx)
        {
            // A local's element: best[0] = x, counts[:key] = 3
            var im = Regex.Match(lhs, @"^([a-z_]\w*)\[(.+)\]$");
            if (im.Success && _locals.ContainsKey(im.Groups[1].Value))
            {
                var coll = _locals[im.Groups[1].Value];
                var idx = Eval(im.Groups[2].Value, ctx);
                var dict = coll as Dictionary<string, object>;
                if (dict != null) { dict[Convert.ToString(idx, CultureInfo.InvariantCulture)] = val; return; }
                var ivs = coll as List<int>;
                if (ivs != null) { int si = StatIndex(idx); if (si >= 0 && si < ivs.Count) ivs[si] = ToInt(val); return; }
                var lst = coll as System.Collections.IList;
                if (lst != null) { int k = ToInt(idx); if (k < 0) k += lst.Count; while (lst.Count <= k) lst.Add(null); if (k >= 0) lst[k] = val; return; }
            }
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
            var fm = Regex.Match(lhs, @"^\$(?:player|Trainer)\.(has_\w+|pokedex|seen_purify_chamber)$");
            if (fm.Success)
            {
                string flag = fm.Groups[1].Value == "pokedex" ? "has_pokedex" : fm.Groups[1].Value;
                if (Truthy(val)) _game.State.Flags.Add(flag); else _game.State.Flags.Remove(flag);
                return;
            }
            if (lhs == "$scene" && Convert.ToString(val) == "Scene_Credits") { _game.PushScene(new UI.CreditsScene(_game)); return; }
            if (lhs == "$player.coins" || lhs == "$Trainer.coins") { _game.State.Coins = Math.Max(0, Math.Min(99999, ToInt(val))); return; }
            if (lhs == "$player.battle_points") { _game.State.BattlePoints = Math.Max(0, Math.Min(9999, ToInt(val))); return; }
            var bm = Regex.Match(lhs, @"^\$(?:player|Trainer)\.badges\[(.+)\]$");
            if (bm.Success) { int b = ToInt(Eval(bm.Groups[1].Value, ctx)); if (Truthy(val)) _game.State.Badges.Add(b); else _game.State.Badges.Remove(b); _game.Map.RefreshEvents(); return; }
            if (lhs.StartsWith("$player.") && (lhs.EndsWith("_storage_creator") || lhs.EndsWith("mystery_gift_unlocked"))) { if (Truthy(val)) _game.State.Flags.Add(lhs.Substring(8)); else _game.State.Flags.Remove(lhs.Substring(8)); return; }
            // Locals: x = ..., pkmn.item = :LEFTOVERS, pkmn.iv[:HP] = 20
            if (Regex.IsMatch(lhs, @"^[a-z_]\w*$")) { _locals[lhs] = val; return; }
            var lm = Regex.Match(lhs, @"^([a-z_]\w*)\.(\w+)(?:\[(.+)\])?$");
            if (lm.Success && _locals.ContainsKey(lm.Groups[1].Value))
            {
                SetMember(_locals[lm.Groups[1].Value], lm.Groups[2].Value, lm.Groups[3].Success ? Eval(lm.Groups[3].Value, ctx) : null, val);
                return;
            }
            var sm = Regex.Match(lhs, @"^\$stats\.(\w+)\[(.+)\]$");
            if (sm.Success) { World.GameStats.Set(_game, sm.Groups[1].Value + "_" + ToInt(Eval(sm.Groups[2].Value, ctx)), ToInt(val)); return; }
            if (lhs.StartsWith("$stats.")) { World.GameStats.Set(_game, lhs.Substring(7), ToInt(val)); return; }
            // Ruby globals made by scripts ($speeches = [], $DEBUG = true) live as long as the game runs
            if (Regex.IsMatch(lhs, @"^\$[A-Za-z_]\w*$")) { _globals[lhs] = val; return; }
            var cm = Regex.Match(lhs, @"^get_character\((.*)\)\.(\w+)$");
            if (cm.Success && ctx != null)
            {
                var ch = ctx.GetCharacter(ToInt(Eval(cm.Groups[1].Value, ctx)));
                if (ch != null)
                {
                    switch (cm.Groups[2].Value)
                    {
                        case "pattern": ch.Pattern = ToInt(val); return;
                        case "direction": ch.Turn(ToInt(val)); return;
                        case "opacity": ch.Opacity = ToInt(val); return;
                        case "through": ch.Through = Truthy(val); return;
                        case "move_speed": ch.MoveSpeed = ToInt(val); return;
                        case "character_name": ch.CharacterName = Convert.ToString(val); return;
                        case "transparent": ch.Transparent = Truthy(val); return;
                    }
                }
            }
            if (lhs == "$PokemonGlobal.encounter_version") { _game.State.Extras.EncounterVersion = ToInt(val); return; }
            if (lhs == "$PokemonGlobal.nextBattleBGM" || lhs == "$PokemonGlobal.next_battle_bgm") { _game.State.Extras.NextBattleBgm = AudioName(val); return; }
            if (lhs == "$PokemonGlobal.nextBattleME" || lhs == "$PokemonGlobal.nextBattleVictoryME" || lhs == "$PokemonGlobal.next_battle_victory_me") { _game.State.Extras.NextBattleMe = AudioName(val); return; }
            if (lhs == "$game_system.save_disabled") { if (Truthy(val)) _game.State.Flags.Add("save_disabled"); else _game.State.Flags.Remove("save_disabled"); return; }
            if (lhs == "$game_system.menu_disabled") { if (Truthy(val)) _game.State.Flags.Add("menu_disabled"); else _game.State.Flags.Remove("menu_disabled"); return; }
            if (lhs == "$game_system.encounter_disabled") { if (Truthy(val)) _game.State.Flags.Add("encounter_disabled"); else _game.State.Flags.Remove("encounter_disabled"); return; }
            if (lhs == "$PokemonMap.whiteFluteUsed" || lhs == "$PokemonMap.blackFluteUsed")
            {
                bool white = lhs.EndsWith("whiteFluteUsed");
                if (Truthy(val)) { _game.State.Extras.Flute = white ? 1 : -1; _game.State.Extras.FluteMap = _game.Map.Id; }
                else if (_game.State.Extras.Flute == (white ? 1 : -1)) _game.State.Extras.Flute = 0;
                return;
            }
            if (lhs == "$PokemonGlobal.dungeon_area") { _game.State.Extras.DungeonArea = val == null ? null : Convert.ToString(val, CultureInfo.InvariantCulture); return; }
            if (lhs == "$PokemonGlobal.dungeon_version") { _game.State.Extras.DungeonVersion = ToInt(val); return; }
            WarnOnce("Can't assign to " + lhs);
        }

        void RememberMoved(GameEvent ch)
        {
            if (_game.State.Extras.MovedEvents == null) _game.State.Extras.MovedEvents = new Dictionary<string, List<int>>();
            _game.State.Extras.MovedEvents[_game.Map.Id + "," + ch.Id] = new List<int> { -1, -1, 2 };
        }

        // ------------------------------------------------------------------ built-in commands
        void RegisterBuiltins()
        {
            Handler noop = (c, a) => true;
            foreach (var n in new[] { "pbCallBub", "pbFadeOutIn",
                                      "pbUpdateSceneMap", "pbShowMap", "pbTownMap", "pbSetEscapePoint",
                                      "pbEraseEscapePoint",
                                      "pbUnlockWallpaper" })
                Register(n, noop);
            RegisterSceneCalls();

            Register("pbCommonEvent", (c, a) => c != null && c.CallCommonEvent(ToInt(Arg(a, 0))));
            Register("pbMessage", (c, a) => { _game.Message.Show(Str(a, 0)); return 0; });
            Register("Kernel.pbMessage", (c, a) => { _game.Message.Show(Str(a, 0)); return 0; });
            // Phone calls made from common events (Essentials' Phone::Call.start_message / end_message)
            Register("Phone::Call.start_message", (c, a) => { _game.Audio.PlaySe("Phone call"); _game.Message.Show("......\\wt[5] ......\\wt[5]"); return true; });
            Register("Phone::Call.end_message", (c, a) => { _game.Message.Show("Click!\\wt[10]\\n......\\wt[5] ......"); return true; });
            Register("GameData::Stat.get", (c, a) =>
            {
                string id = Str(a, 0);
                var names = new Dictionary<string, string> { { "HP", "HP" }, { "ATTACK", "Attack" }, { "DEFENSE", "Defense" }, { "SPECIAL_ATTACK", "Sp. Atk" }, { "SPECIAL_DEFENSE", "Sp. Def" }, { "SPEED", "Speed" }, { "ACCURACY", "accuracy" }, { "EVASION", "evasiveness" } };
                string n; return new Dictionary<string, object> { { "id", id }, { "name", names.TryGetValue(id ?? "", out n) ? n : id }, { "real_name", names.TryGetValue(id ?? "", out n) ? n : id } };
            });
            Register("pbConfirmMessage", (c, a) =>
            {
                bool result = false;
                _game.Message.Show(Str(a, 0), new List<string> { "Yes", "No" }, 1, r => result = r == 0);
                return true; // answered asynchronously; conditions default to "yes"
            });

            // Temp switches: like self switches, but forgotten when the player leaves the map
            Func<Interpreter, string, string> tkey = (c, ch) => (c != null ? c.MapId + "," + c.EventId : _curMap + "," + _curEvent) + "," + ch;
            Register("setTempSwitchOn", (c, a) => { _game.TempSwitches.Add(tkey(c, Str(a, 0))); if (_game.Map != null) _game.Map.RefreshEvents(); return true; });
            Register("setTempSwitchOff", (c, a) => { _game.TempSwitches.Remove(tkey(c, Str(a, 0))); if (_game.Map != null) _game.Map.RefreshEvents(); return true; });
            Register("isTempSwitchOn?", (c, a) => _game.TempSwitches.Contains(tkey(c, Str(a, 0))));
            Register("isTempSwitchOff?", (c, a) => !_game.TempSwitches.Contains(tkey(c, Str(a, 0))));
            Register("tsOn?", (c, a) => _game.TempSwitches.Contains(tkey(c, Str(a, 0))));
            Register("tsOff?", (c, a) => !_game.TempSwitches.Contains(tkey(c, Str(a, 0))));
            // Time of day (Essentials PBDayNight): day 6-20, morning 6-12, afternoon 12-17, evening 17-20, night 20-6
            Register("PBDayNight.isDay?", (c, a) => { int h = DateTime.Now.Hour; return h >= 6 && h < 20; });
            Register("PBDayNight.isNight?", (c, a) => { int h = DateTime.Now.Hour; return h < 6 || h >= 20; });
            Register("PBDayNight.isMorning?", (c, a) => { int h = DateTime.Now.Hour; return h >= 6 && h < 12; });
            Register("PBDayNight.isAfternoon?", (c, a) => { int h = DateTime.Now.Hour; return h >= 12 && h < 17; });
            Register("PBDayNight.isEvening?", (c, a) => { int h = DateTime.Now.Hour; return h >= 17 && h < 20; });
            Register("pbIsWeekday", (c, a) =>
            {
                // pbIsWeekday(variable or -1, day, day...): 0 = Sunday
                int wd = (int)DateTime.Now.DayOfWeek;
                int v = a.Count > 0 ? ToInt(a[0]) : -1;
                if (v > 0) _game.State.SetVar(v, wd);
                return a.Skip(1).Any(x => ToInt(x) == wd);
            });
            // Timed events: pbSetEventTime records now; cooledDown?(seconds) / cooledDownDays?(days) once enough time has passed
            Func<Interpreter, string> ekey = c => c != null ? c.MapId + "," + c.EventId : _curMap + "," + _curEvent;
            Register("pbSetEventTime", (c, a) =>
            {
                long now = (long)(DateTime.Now - new DateTime(2000, 1, 1)).TotalSeconds;
                _game.State.EventTimes[ekey(c)] = now;
                int map = c != null ? c.MapId : _curMap;
                foreach (var x in a) _game.State.EventTimes[map + "," + ToInt(x)] = now;
                return true;
            });
            Register("cooledDown?", (c, a) =>
            {
                long t; if (!_game.State.EventTimes.TryGetValue(ekey(c), out t)) return true;
                long now = (long)(DateTime.Now - new DateTime(2000, 1, 1)).TotalSeconds;
                return now >= t + ToInt(Arg(a, 0));
            });
            Register("cooledDownDays?", (c, a) =>
            {
                long t; if (!_game.State.EventTimes.TryGetValue(ekey(c), out t)) return true;
                var then = new DateTime(2000, 1, 1).AddSeconds(t).Date;
                return (DateTime.Now.Date - then).TotalDays >= Math.Max(1, ToInt(Arg(a, 0)));
            });
            Register("$player.male?", (c, a) => PlayerGender() != "Female");
            Register("$player.female?", (c, a) => PlayerGender() == "Female");
            Register("$Trainer.male?", (c, a) => PlayerGender() != "Female");
            Register("$Trainer.female?", (c, a) => PlayerGender() == "Female");
            Register("pbInChallenge?", (c, a) => World.BattleTower.InProgress(_game) || (_game.State.Extras.Frontier != null && _game.State.Extras.Frontier.InChallenge));
            Register("$PokemonGlobal.encounter_version", (c, a) => _game.State.Extras.EncounterVersion);
            Register("$PokemonGlobal.dungeon_area", (c, a) => _game.State.Extras.DungeonArea);
            Register("$PokemonGlobal.dungeon_version", (c, a) => _game.State.Extras.DungeonVersion);
            Register("pbFirstAblePokemon", (c, a) =>
            {
                int i = _game.State.Party.FindIndex(PokeCreator.Battles.Battle.Able);
                if (a.Count > 0) _game.State.SetVar(ToInt(a[0]), i);
                return i >= 0 ? _game.State.Party[i] : null;
            });
            Register("GameData::Ribbon.get", (c, a) => new RibbonInfo(Str(a, 0)));
            Register("pbNextMysteryGiftID", (c, a) => { var w = World.MysteryGift.Waiting(_game); return w.Count > 0 ? (object)w[0].Id : 0; });
            Register("pbReceiveMysteryGift", (c, a) => World.MysteryGift.Receive(_game, a.Count > 0 && a[0] != null && !(a[0] is int) ? Str(a, 0) : null));
            Register("pbMysteryGiftWaiting?", (c, a) => World.MysteryGift.Waiting(_game).Count > 0);
            Register("$game_player.has_follower?", (c, a) => false);
            Register("get_event", (c, a) => _game.Map == null ? null : _game.Map.Events.FirstOrDefault(e => e.Id == ToInt(Arg(a, 0))));
            Register("pbSetSelfSwitch", (c, a) =>
            {
                int ev = ToInt(Arg(a, 0)); string ch = Str(a, 1); bool v = a.Count < 3 || Truthy(a[2]);
                int map = a.Count > 3 ? ToInt(a[3]) : _game.Map.Id;
                _game.State.SetSelf(map, ev, ch, v); _game.Map.RefreshEvents(); return true;
            });
            Register("get_character", (c, a) => ToInt(Arg(a, 0)));
            Register("get_self", (c, a) => 0);
            // Trainers: intro music, "!" and walking up to the player, music back afterwards
            Register("pbTrainerIntro", (c, a) =>
            {
                var tt = _game.Project.TrainerTypes.FirstOrDefault(t => t.Id == Str(a, 0));
                if (tt != null && !string.IsNullOrEmpty(tt.IntroBGM)) _game.Audio.PlayMe(tt.IntroBGM);
                return true;
            });
            Register("pbTrainerEnd", (c, a) => { _game.RestoreMapBgm(); return true; });
            // Field moves and overworld objects
            Register("pbCut", (c, a) => FieldMoves.Cut(_game, c));
            Register("pbRockSmash", (c, a) => FieldMoves.RockSmash(_game, c));
            Register("pbSmashThisEvent", (c, a) => { FieldMoves.SmashThisEvent(_game, c); return true; });
            Register("pbRockSmashRandomEncounter", (c, a) => { FieldMoves.RockSmashEncounter(_game); return true; });
            Register("pbHeadbutt", (c, a) => { FieldMoves.Headbutt(_game); return true; });
            Register("pbStrength", (c, a) => { FieldMoves.Strength(_game, null); return true; });
            Register("pbPushThisBoulder", (c, a) => { FieldMoves.PushThisBoulder(_game, c); return true; });
            Register("pbSurf", (c, a) => { FieldMoves.OfferSurf(_game); return true; });
            Register("pbPickBerry", (c, a) => { FieldMoves.PickBerry(_game, c, Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1); return true; });
            Register("pbBerryPlant", (c, a) => { FieldMoves.BerryPlant(_game, c); return true; });
            Register("pbCanUseHiddenMove?", (c, a) => true);
            // Hall of Fame and credits
            Register("pbHallOfFameEntry", (c, a) => { _game.PushScene(new UI.HallOfFameScene(_game)); return true; });
            Register("Scene_Credits.new", (c, a) => "Scene_Credits");
            Register("pbHallOfFamePC", (c, a) => { _game.PushScene(new UI.HallOfFameScene(_game, true)); return true; });
            Register("pbShowMap", (c, a) => { _game.PushScene(new UI.TownMapScene(_game, false, null)); return true; });
            Register("pbTownMap", (c, a) => { _game.PushScene(new UI.TownMapScene(_game, false, null)); return true; });
            Register("pbSetEscapePoint", (c, a) => { FieldMoves.SetEscapePoint(_game); return true; });
            Register("pbEraseEscapePoint", (c, a) => { _game.State.EscapePoint = null; return true; });
            Register("pbMountBike", (c, a) => { if (!_game.Player.Cycling) { _game.Player.Cycling = true; _game.PlayMapMusic(); } return true; });
            Register("pbDismountBike", (c, a) => { if (_game.Player.Cycling) { _game.Player.Cycling = false; _game.PlayMapMusic(); } return true; });
            Register("pbCanUseBike?", (c, a) => FieldMoves.CanBike(_game));
            Register("$PokemonGlobal.bicycle", (c, a) => _game.Player.Cycling);
            Register("$PokemonGlobal.surfing", (c, a) => _game.Player.Surfing);
            Register("pbNoticePlayer", (c, a) =>
            {
                if (c == null) return true;
                var ch = c.GetCharacter(a.Count > 0 && a[0] != null ? ToInt(a[0]) : 0) as GameEvent;
                if (ch == null) return true;
                _game.Exclaim(ch, 30);
                int dist = Math.Abs(ch.X - _game.Player.X) + Math.Abs(ch.Y - _game.Player.Y);
                var route = new PokeCreator.Maps.MoveRoute { Repeat = false };
                route.List.Add(new PokeCreator.Maps.EventCommand(15, 0, 6));          // wait while the "!" shows
                for (int i = 1; i < dist; i++) route.List.Add(new PokeCreator.Maps.EventCommand(10, 0)); // step toward the player
                route.List.Add(new PokeCreator.Maps.EventCommand(25, 0));             // face the player
                route.List.Add(new PokeCreator.Maps.EventCommand(0, 0));
                ch.ForceMoveRoute(route);
                RememberMoved(ch);
                _game.Player.TurnToward(ch);
                c.WaitForMoves();
                return true;
            });
            Register("pbMapInterpreter", (c, a) => c);
            Register("$PokemonMap.addMovedEvent", (c, a) => { var ch = (a.Count > 0 ? a[0] : null) as GameEvent ?? (c != null ? c.GetCharacter(0) as GameEvent : null); if (ch != null) RememberMoved(ch); return true; });
            Register("$PokemonMap.movedEvent?", (c, a) => { var ch = (a.Count > 0 ? a[0] : null) as GameEvent; return ch != null && _game.State.Extras.MovedEvents != null && _game.State.Extras.MovedEvents.ContainsKey(_game.Map.Id + "," + ch.Id); });
            Register("$player.last_party", (c, a) => _game.State.Party.LastOrDefault());
            Register("$Trainer.last_party", (c, a) => _game.State.Party.LastOrDefault());

            Register("pbReceiveItem", (c, a) => GiveItem(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1, "obtained"));
            Register("pbItemBall", (c, a) => GiveItem(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1, "found"));
            Register("pbGetItem", (c, a) => GiveItem(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1, "obtained"));
            Register("$bag.add", (c, a) => { _game.State.AddItem(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1); return true; });
            Register("$bag.has?", (c, a) => _game.State.Bag.ContainsKey(Str(a, 0)));
            Register("$PokemonBag.pbStoreItem", (c, a) => { _game.State.AddItem(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 1); return true; });

            Register("pbAddPokemon", (c, a) => AddPokemonArg(a, true));
            Register("pbAddPokemonSilent", (c, a) => AddPokemonArg(a, false));
            Register("pbAddToParty", (c, a) => AddPokemonArg(a, true));
            Register("pbAddToPartySilent", (c, a) => AddPokemonArg(a, false));
            Register("Pokemon.new", (c, a) =>
            {
                var pk = PokeCreator.Pkmn.Pokemon.Create(_game.Project, Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 5, _game.Rng);
                pk.OTName = _game.State.PlayerName; pk.OTId = _game.State.TrainerId;
                return pk;
            });
            Register("GameData::Item.get", (c, a) => _game.Project.Items.FirstOrDefault(i => i.Id == Str(a, 0)));
            Register("GameData::Species.get", (c, a) => _game.Project.Species.FirstOrDefault(s => s.Id == Str(a, 0) && s.Form == 0));
            Register("GameData::Move.get", (c, a) => _game.Project.Moves.FirstOrDefault(m => m.Id == Str(a, 0)));
            Register("GameData::Item.exists?", (c, a) => _game.Project.Items.Any(i => i.Id == Str(a, 0)));
            Register("GameData::Species.exists?", (c, a) => _game.Project.Species.Any(s => s.Id == Str(a, 0)));
            Register("get_self.onEvent?", (c, a) => { var ev = c == null || _game.Map == null ? null : _game.Map.Events.FirstOrDefault(x => x.Id == c.EventId); return ev != null && ev.X == _game.Player.X && ev.Y == _game.Player.Y; });
            foreach (var f in new[] { "Followers.toggle", "Followers.update", "Followers.move_followers", "Followers.turn_followers" })
                Register(f, (c, a) => true);
            Register("pbPokerus?", (c, a) => World.Pokerus.AnyInfected(_game));
            Register("Pokemon::MAX_NAME_SIZE", (c, a) => 10);
            Register("pbChooseMove", (c, a) =>
            {
                // pbChooseMove(pkmn, variable, nameVariable): variable = move index (-1 = cancelled)
                var pk = Arg(a, 0) as PokeCreator.Pkmn.Pokemon;
                int v = a.Count > 1 ? ToInt(a[1]) : 1, nv = a.Count > 2 ? ToInt(a[2]) : 0;
                _game.State.SetVar(v, -1);
                if (pk == null) return true;
                var names = pk.Moves.Select(m => PokeCreator.Pkmn.ItemEffects.MoveName(_game.Project, m.Id)).ToList();
                names.Add("Cancel");
                _game.Message.Show("Which move?", names, names.Count - 1, k =>
                {
                    if (k < 0 || k >= pk.Moves.Count) { _game.State.SetVar(v, -1); return; }
                    _game.State.SetVar(v, k);
                    if (nv > 0) _game.State.SetStr(nv, names[k]);
                    if (_game.Map != null) _game.Map.RefreshEvents();
                });
                return true;
            });
            Register("pbRelearnMoveScreen", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                var pk = Arg(a, 0) as PokeCreator.Pkmn.Pokemon;
                if (pk == null) return false;
                var moves = RelearnableMoves(pk);
                if (moves.Count == 0) return false;
                _game.PromptPending = true;
                var names = moves.Select(m => PokeCreator.Pkmn.ItemEffects.MoveName(_game.Project, m)).ToList();
                names.Add("Cancel");
                _game.Message.Show("Teach which move to " + pk.Name(_game.Project) + "?", names, names.Count - 1, k =>
                {
                    if (k < 0 || k >= moves.Count) { _game.SetPrompt(c, false); return; }
                    UI.ItemFlow.LearnMove(_game, pk, moves[k], learned => _game.SetPrompt(c, learned));
                });
                return false;
            });
            Register("pbTextEntry", (c, a) =>
            {
                // pbTextEntry(help text, min length, max length, variable)
                int v = a.Count > 3 ? ToInt(a[3]) : 1;
                int max = a.Count > 2 ? Math.Max(1, ToInt(a[2])) : 10;
                _game.State.SetStr(v, "");
                _game.AskName(Str(a, 0), "", max, n => _game.State.SetStr(v, n ?? ""), c);
                return true;
            });
            Register("pbCaveEntrance", (c, a) => true);
            Register("pbToneChangeAll", (c, a) =>
            {
                var t = Arg(a, 0) as List<object>;
                if (t != null) _game.SetScreenTone(t, a.Count > 1 ? ToInt(a[1]) * 3 / 2 : 0);
                return true;
            });
            Register("Tone.new", (c, a) => a.Select(x => (object)ToInt(x)).ToList());
            Register("pbEventScreen", (c, a) => true);
            Register("pbCaveExit", (c, a) => true);
            Register("pbCaveEntranceEx", (c, a) => true);
            Register("pbCaveExitEx", (c, a) => true);
            Register("$player.pokedex.unlock", (c, a) => true);
            Register("$player.pokedex.lock", (c, a) => true);
            Register("pbTrainerCheck", (c, a) => true);
            Register("$player.first_pokemon", (c, a) => _game.State.Party.FirstOrDefault(p => !p.IsEgg && !p.Fainted));
            Register("$player.first_party", (c, a) => _game.State.Party.FirstOrDefault());
            Register("Pokemon.play_cry", (c, a) => { var pk = PokeCreator.Pkmn.Pokemon.Create(_game.Project, Str(a, 0), 1, _game.Rng); _game.Audio.PlayCry(pk); return true; });
            Register("pbAddForeignPokemon", (c, a) =>
            {
                // pbAddForeignPokemon(species, level, owner, nickname, gender)
                var pk = PokeCreator.Pkmn.Pokemon.Create(_game.Project, Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 5, _game.Rng);
                if (pk.Data(_game.Project) == null || _game.State.Party.Count >= GameState.MaxParty) return false;
                pk.OTName = a.Count > 2 ? Str(a, 2) : "Stranger"; pk.OTId = _game.Rng.Next(65536);
                if (a.Count > 3 && Str(a, 3).Length > 0) pk.Nickname = Str(a, 3);
                _game.State.Party.Add(pk); _game.State.Register(pk);
                _game.Audio.PlayMe("Pkmn get");
                _game.Message.Show("\\PN received a Pokémon from " + pk.OTName + ".");
                return true;
            });
            Register("pbStartTrade", (c, a) =>
            {
                // pbStartTrade(partyIndex, newSpecies, nickname, trainerName, gender)
                int i = ToInt(Arg(a, 0));
                if (i < 0 || i >= _game.State.Party.Count) return false;
                var old = _game.State.Party[i];
                var pk = PokeCreator.Pkmn.Pokemon.Create(_game.Project, Str(a, 1), old.Level, _game.Rng);
                if (pk.Data(_game.Project) == null) return false;
                pk.OTName = a.Count > 3 ? Str(a, 3) : "Stranger"; pk.OTId = _game.Rng.Next(65536);
                if (a.Count > 2 && Str(a, 2).Length > 0) pk.Nickname = Str(a, 2);
                pk.MetMap = _game.Map.Id;
                _game.State.Party[i] = pk; _game.State.Register(pk);
                string oldName = old.Name(_game.Project), newName = pk.Name(_game.Project);
                _game.Audio.PlayMe("Evolution success");
                _game.Message.Show("For " + pk.OTName + "'s " + newName + ", \\PN sent over " + oldName + ".", null, -1, _ =>
                    _game.Message.Show(pk.OTName + " sent over " + newName + ".\\nTake good care of " + newName + "!", null, -1, __ =>
                    {
                        // Trade evolutions
                        var to = PokeCreator.Pkmn.Growth.CheckTradeEvolution(_game.Project, pk, old);
                        if (to != null) _game.PushScene(new UI.EvolutionScene(_game, pk, to, false, null));
                    }));
                return true;
            });
            Register("pbMoveTutorChoose", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                string move = Str(a, 0);
                _game.PromptPending = true;
                Func<PokeCreator.Pkmn.Pokemon, bool> able = pk => !pk.IsEgg && PokeCreator.Pkmn.Growth.CanLearnFromItem(_game.Project, pk, move) && !pk.Moves.Any(m => m.Id == move);
                _game.PushScene(new UI.PartyScene(_game, "Teach which Pokémon?", pk => able(pk) ? "ABLE" : "NOT ABLE", (i, close) =>
                {
                    var pk = _game.State.Party[i];
                    if (!able(pk)) { _game.Audio.PlaySe("GUI sel buzzer"); return; }
                    close();
                    UI.ItemFlow.LearnMove(_game, pk, move, learned => _game.SetPrompt(c, learned));
                }, () => _game.SetPrompt(c, false)));
                return false;
            });
            Register("pbBoxesFull?", (c, a) => _game.State.Party.Count >= GameState.MaxParty && _game.State.Storage.Full);
            Register("pbGetStorageCreator", (c, a) => "someone");
            Register("$player.seen_storage_creator", (c, a) => _game.State.Flags.Contains("seen_storage_creator"));
            Register("pbSaveScreen", (c, a) =>
            {
                object answer;
                if (_game.TakePrompt(c, out answer)) return answer;
                _game.PromptPending = true;
                _game.Message.Show("Would you like to save the game?", new List<string> { "Yes", "No" }, 1, k =>
                {
                    if (k != 0) { _game.SetPrompt(c, false); return; }
                    SaveSystem.Save(_game);
                    _game.Audio.PlaySe("GUI save game");
                    _game.Message.Show("\\PN saved the game.", null, -1, _ => _game.SetPrompt(c, true));
                });
                return false;
            });
            Handler heal = (c, a) => { foreach (var pk in _game.State.Party) pk.Heal(_game.Project); return true; };
            Register("pbHealAll", heal);
            Register("$player.heal_party", heal);
            Register("$Trainer.heal_party", heal);
            Register("$player.able_pokemon_count", (c, a) => _game.State.Party.Count(pk => !pk.Fainted));
            Register("$player.party_full?", (c, a) => _game.State.Party.Count >= GameState.MaxParty);
            Register("pbPokeCenterPC", (c, a) => { PcMenu.Open(_game, true); return true; });
            Register("pbTrainerPC", (c, a) => { PcMenu.Open(_game, false); return true; });
            // pbPokemonMart([:POTION, :ANTIDOTE, ...], speech = nil, cantsell = false)
            Register("pbPokemonMart", (c, a) =>
            {
                var stock = Arg(a, 0) as List<object>;
                var ids = stock == null ? new List<string>() : stock.Select(x => Convert.ToString(x)).ToList();
                var mart = new MartScene(_game, ids, a.Count > 2 && Truthy(a[2]));
                mart.OnClosed = () => MartScene.PriceOverrides.Clear();
                _game.PushScene(mart);
                return true;
            });
            Register("pbSellItem", (c, a) => true);

            // Battles. The event waits for the battle; a Conditional Branch re-checks afterwards and gets the result.
            Register("pbSetPokemonCenter", (c, a) =>
            {
                if (_game.Map == null) return true;
                _game.State.HealMap = _game.Map.Id; _game.State.HealX = _game.Player.X; _game.State.HealY = _game.Player.Y; _game.State.HealDir = 2;
                return true;
            });
            Register("setBattleRule", (c, a) => { _battleRules[Str(a, 0)] = a.Count > 1 ? a[1] : true; return true; });
            Register("TrainerBattle.start", (c, a) => TrainerBattleArgs(a, c));
            Register("pbTrainerBattle", (c, a) =>
            {
                if (a.Count > 5 && Truthy(a[5])) _battleRules["canLose"] = true;
                return TrainerBattle(Str(a, 0), Str(a, 1), a.Count > 4 ? ToInt(a[4]) : 0, c);
            });
            // pbDoubleTrainerBattle(type1, name1, version1, speech1, type2, name2, version2, speech2)
            Register("pbDoubleTrainerBattle", (c, a) =>
            {
                if (a.Count > 8 && Truthy(a[8])) _battleRules["canLose"] = true;
                return TrainerBattleList(new List<object[]> { new object[] { Str(a, 0), Str(a, 1), a.Count > 2 ? ToInt(a[2]) : 0 }, new object[] { Str(a, 4), Str(a, 5), a.Count > 6 ? ToInt(a[6]) : 0 } }, c);
            });
            Register("WildBattle.start", (c, a) =>
            {
                // WildBattle.start(:SPECIES, level), two pairs for a double battle, three for a triple
                if (a.Count >= 6 && a[2] is string && a[4] is string)
                {
                    if (_game.ResumeOutcome.HasValue && c != null && _game.ResumeOwner == c) { var o = _game.ResumeOutcome.Value; _game.ResumeOutcome = null; return (int)o; }
                    int ov = OutcomeVar(); HandOffRules();
                    _game.StartTripleWildBattle(Str(a, 0), ToInt(a[1]), Str(a, 2), ToInt(a[3]), Str(a, 4), ToInt(a[5]), o => _game.State.SetVar(ov, (int)o));
                    return 1;
                }
                if (a.Count >= 4 && a[2] is string) return DoubleWildBattle(Str(a, 0), ToInt(a[1]), Str(a, 2), ToInt(a[3]), false, c);
                return WildBattle(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 5, false, c);
            });
            Register("pbDoubleWildBattle", (c, a) => DoubleWildBattle(Str(a, 0), ToInt(Arg(a, 1)), Str(a, 2), ToInt(Arg(a, 3)), true, c));
            Register("pbWildBattle", (c, a) =>
            {
                if (a.Count > 4 && Truthy(a[4])) _battleRules["canLose"] = true;
                if (a.Count > 2 && a[2] != null) _battleRules["outcomeVar"] = ToInt(a[2]);
                return WildBattle(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 5, true, c);
            });

            // Variables, bag and screen helpers
            Register("pbSet", (c, a) =>
            {
                if (Arg(a, 1) is string) _game.State.SetStr(ToInt(Arg(a, 0)), (string)Arg(a, 1));
                else _game.State.SetVar(ToInt(Arg(a, 0)), ToInt(Arg(a, 1)));
                _game.Map.RefreshEvents(); return Arg(a, 1);
            });
            Register("pbGet", (c, a) =>
            {
                int id = ToInt(Arg(a, 0)); string s;
                return _game.State.StringVars.TryGetValue(id, out s) ? (object)s : _game.State.GetVar(id);
            });
            Register("$bag.remove", (c, a) => { bool had = _game.State.Bag.ContainsKey(Str(a, 0)); _game.State.AddItem(Str(a, 0), -(a.Count > 1 ? ToInt(a[1]) : 1)); return had; });
            Register("$bag.can_add?", (c, a) => true);
            Register("$bag.quantity", (c, a) => { int n; return _game.State.Bag.TryGetValue(Str(a, 0), out n) ? n : 0; });
            Register("$player.has_species?", (c, a) => _game.Party.Any(p => p.Species.Equals(Str(a, 0), StringComparison.OrdinalIgnoreCase)));
            Register("Tone.new", (c, a) => a.Select(x => (object)ToInt(x)).ToList());
            Register("pbToneChangeAll", (c, a) => { _game.SetScreenTone(Arg(a, 0) as List<object>, ToInt(Arg(a, 1)) * 3 / 2); return true; });
            Register("rename_choice", (c, a) => { if (c != null && (a.Count < 3 || Truthy(a[2]))) c.ChoiceRenames[ToInt(Arg(a, 0))] = Str(a, 1); return true; });
            Register("hide_choice", (c, a) => { if (c != null && (a.Count < 2 || Truthy(a[1]))) c.ChoiceHidden.Add(ToInt(Arg(a, 0))); return true; });
            Register("$player.pokemon_party", (c, a) => _game.State.Party.Where(p => !p.IsEgg).ToList());
            Register("$player.able_party", (c, a) => _game.State.Party.Where(p => !p.IsEgg && !p.Fainted).ToList());
            Register("$player.party", (c, a) => _game.State.Party);
            Register("$Trainer.party", (c, a) => _game.State.Party);
            Register("follower_animation", (c, a) => { var f = World.Followers.Characters.FirstOrDefault(); if (f != null) _game.Exclaim(f, 30); return true; });
            Register("setPrice", (c, a) =>
            {
                string id = Str(a, 0); if (string.IsNullOrEmpty(id)) return false;
                int buy = a.Count > 1 && a[1] != null ? ToInt(a[1]) : -1, sell = a.Count > 2 && a[2] != null ? ToInt(a[2]) : (buy >= 0 ? buy / 2 : -1);
                MartScene.PriceOverrides[id] = new[] { buy, sell };
                return true;
            });
            Register("$game_screen.weather", (c, a) =>
            {
                object t = Arg(a, 0); string ts = t as string;
                int type = ts != null ? (int.TryParse(ts, out type) ? type : WeatherSystem.TypeFromName(ts.TrimStart(':'))) : ToInt(t);
                _game.Weather.Set(type, a.Count > 1 ? ToInt(a[1]) : 5, a.Count > 2 ? ToInt(a[2]) : 0);
                return true;
            });
            // Event variables (Essentials' setVariable / getVariable: a number kept for this event)
            Register("setVariable", (c, a) =>
            {
                if (c == null) return false;
                if (_game.State.Extras.EventVars == null) _game.State.Extras.EventVars = new Dictionary<string, int>();
                _game.State.Extras.EventVars[c.MapId + "," + c.EventId] = ToInt(Arg(a, 0));
                return true;
            });
            Register("getVariable", (c, a) =>
            {
                int v; return c != null && _game.State.Extras.EventVars != null && _game.State.Extras.EventVars.TryGetValue(c.MapId + "," + c.EventId, out v) ? v : 0;
            });
            Register("setSellPrice", (c, a) => { string id = Str(a, 0); int[] o; if (!MartScene.PriceOverrides.TryGetValue(id, out o)) o = new[] { -1, -1 }; o[1] = ToInt(Arg(a, 1)); MartScene.PriceOverrides[id] = o; return true; });
            foreach (var n in new[] { "setBattleRule", "$game_screen.weather", "pbPickBerry", "setPrice", "setVariable", "pbToneChangeAll" })
                if (!_commands.ContainsKey(n)) Register(n, noop);
            // ---- Pokégear phone and trainer rematches
            Func<Interpreter, object, int> eventOf = (c, o2) => { int id = o2 is string ? 0 : ToInt(o2); return id > 0 ? id : (c != null ? c.EventId : 0); };
            Func<Interpreter, int> mapOf = c => c != null && c.MapId > 0 ? c.MapId : (_game.Map != null ? _game.Map.Id : 0);
            Register("Phone.can_add?", (c, a) => Phone.CanAdd(_game, Str(a, 0), Str(a, 1)));
            Register("Phone.add", (c, a) =>
            {
                // Phone.add(event, :TYPE, "Name", startVersion) for trainers; Phone.add(event, "Name", commonEvent) for people
                string a1 = Str(a, 1);
                if (_game.Project.TrainerTypes.Any(t => t.Id == a1) && a.Count > 2)
                    Phone.AddTrainer(_game, mapOf(c), eventOf(c, Arg(a, 0)), a1, Str(a, 2), a.Count > 3 ? ToInt(a[3]) : 0);
                else Phone.AddPerson(_game, mapOf(c), eventOf(c, Arg(a, 0)), a1, a.Count > 2 ? ToInt(a[2]) : 0);
                return true;
            });
            Register("pbPhoneRegister", (c, a) => { Phone.AddTrainer(_game, mapOf(c), eventOf(c, Arg(a, 0)), Str(a, 1), Str(a, 2), 0); return true; });
            Register("pbPhoneRegisterBattle", (c, a) =>
            {
                // pbPhoneRegisterBattle(message, event, :TYPE, "Name", maxBattles)
                string type = Str(a, 2), name = Str(a, 3);
                if (!Phone.CanAdd(_game, type, name)) return true;
                int mapId = mapOf(c), evId = eventOf(c, Arg(a, 1));
                Action ask = () => _game.Message.Show("Register " + name + " in the Pokégear?", new List<string> { "Yes", "No" }, 1, ch =>
                {
                    if (ch != 0) return;
                    Phone.AddTrainer(_game, mapId, evId, type, name, 0);
                    _game.Message.Show("\\PN registered " + name + " in the Pokégear.");
                });
                string msg = Str(a, 0);
                if (msg.Length > 0) _game.Message.Show(msg, null, -1, _ => ask()); else ask();
                return true;
            });
            Handler ready = (c, a) => Phone.ReadyToBattle(_game, Str(a, 0), Str(a, 1));
            Register("Phone.can_rematch?", ready); Register("Phone.rematch?", ready); Register("pbPhoneReadyToBattle?", ready);
            // v21 rematches: Phone.battle(:TYPE, "Name") fights the trainer's current version; Phone.reset_after_win(...) after a win
            Register("Phone.battle", (c, a) => TrainerBattle(Str(a, 0), Str(a, 1), Phone.Variant(_game, Str(a, 0), Str(a, 1)), c));
            Register("Phone.reset_after_win", (c, a) => { Phone.Rematched(_game, Str(a, 0), Str(a, 1)); return true; });
            Handler variant = (c, a) => Phone.Variant(_game, Str(a, 0), Str(a, 1));
            Register("Phone.variant", variant); Register("pbPhoneBattleCount", variant);
            Handler rematched = (c, a) => { Phone.Rematched(_game, Str(a, 0), Str(a, 1)); return true; };
            Register("Phone.increment_version", rematched); Register("pbPhoneIncrement", rematched); Register("Phone.rematched", rematched);
            Register("$player.has_pokegear=", (c, a) => { if (Truthy(Arg(a, 0))) _game.State.Flags.Add("has_pokegear"); else _game.State.Flags.Remove("has_pokegear"); return true; });

            Register("pbGetPokemon", (c, a) => { int i = _game.State.GetVar(ToInt(Arg(a, 0))); return i >= 0 && i < _game.State.Party.Count ? _game.State.Party[i] : null; });
            Register("pbPartyCount", (c, a) => _game.Party.Count);
            Register("pbChooseNonEggPokemon", (c, a) => { ChoosePokemon(a, pk => !pk.IsEgg); return true; });
            Register("pbChoosePokemon", (c, a) => { ChoosePokemon(a, pk => true); return true; });
            Register("pbChooseAblePokemon", (c, a) => { ChoosePokemon(a, pk => !pk.IsEgg && !pk.Fainted); return true; });
            Register("$player.has_other_able_pokemon?", (c, a) => { int i = ToInt(Arg(a, 0)); return _game.State.Party.Where((pk, k) => k != i).Any(pk => !pk.IsEgg && !pk.Fainted); });
            Register("cannot_store", (c, a) => false);
            // Day Care (Essentials v21 DayCare class)
            Register("DayCare.count", (c, a) => DayCare.Count(_game));
            Register("DayCare.deposit", (c, a) => { DayCare.Deposit(_game, ToInt(Arg(a, 0))); return true; });
            Register("DayCare.withdraw", (c, a) => { DayCare.Withdraw(_game, ToInt(Arg(a, 0))); return true; });
            Register("DayCare.egg_generated?", (c, a) => _game.State.DayCareEgg);
            Register("DayCare.collect_egg", (c, a) => { DayCare.CollectEgg(_game); return true; });
            Register("DayCare.reset_egg_counters", (c, a) => { _game.State.DayCareEgg = false; _game.State.DayCareEggSteps = 0; return true; });
            Register("DayCare.get_compatibility", (c, a) => { int v = DayCare.Compatibility(_game); if (a.Count > 0) _game.State.SetVar(ToInt(a[0]), v); return v; });
            Register("DayCare.get_details", (c, a) =>
            {
                int slot = ToInt(Arg(a, 0));
                if (slot < 0 || slot >= _game.State.DayCare.Count) return false;
                if (a.Count > 1) _game.State.SetStr(ToInt(a[1]), _game.State.DayCare[slot].Pokemon.Name(_game.Project));
                if (a.Count > 2) _game.State.SetVar(ToInt(a[2]), DayCare.Cost(_game, slot));
                return true;
            });
            Register("DayCare.get_level_gain", (c, a) =>
            {
                int slot = ToInt(Arg(a, 0));
                if (slot < 0 || slot >= _game.State.DayCare.Count) return false;
                if (a.Count > 1) _game.State.SetStr(ToInt(a[1]), _game.State.DayCare[slot].Pokemon.Name(_game.Project));
                if (a.Count > 2) _game.State.SetVar(ToInt(a[2]), DayCare.LevelGain(_game, slot));
                return true;
            });
            Register("DayCare.choose", (c, a) =>
            {
                int v = a.Count > 1 ? ToInt(a[1]) : 1;
                var names = _game.State.DayCare.Select(s => s.Pokemon.Name(_game.Project) + " (Lv. " + s.Pokemon.Level + ")").ToList();
                if (names.Count == 0) { _game.State.SetVar(v, -1); return true; }
                names.Add("Cancel");
                _game.Message.Show(Str(a, 0), names, names.Count - 1, k => _game.State.SetVar(v, k == names.Count - 1 ? -1 : k));
                return true;
            });
            // Older (v19/v20) Day Care names
            foreach (var pair in new[] { new[] { "pbDayCareDeposit", "DayCare.deposit" }, new[] { "pbDayCareWithdraw", "DayCare.withdraw" }, new[] { "pbDayCareChoose", "DayCare.choose" },
                                         new[] { "pbDayCareGetCompat", "DayCare.get_compatibility" }, new[] { "pbEggGenerated?", "DayCare.egg_generated?" }, new[] { "pbDayCareGenerateEgg", "DayCare.collect_egg" },
                                         new[] { "pbDayCareDeposited", "DayCare.count" }, new[] { "pbDayCareGetDeposited", "DayCare.get_details" }, new[] { "pbDayCareGetLevelGain", "DayCare.get_level_gain" } })
                if (_commands.ContainsKey(pair[1])) _commands[pair[0]] = _commands[pair[1]];
            Register("pbEventCanReachPlayer?", (c, a) => false);
            Register("pbFacingTerrainTag", (c, a) => 0);
            Register("pbGetTimeNow", (c, a) => DateTime.Now);
            Register("PBDayNight.isNight?", (c, a) => DateTime.Now.Hour < 6 || DateTime.Now.Hour >= 20);
            Register("PBDayNight.isDay?", (c, a) => DateTime.Now.Hour >= 6 && DateTime.Now.Hour < 20);
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

        /// <summary>The rules not used for choosing the battle (weather, terrain, noExp...) go with the next battle.</summary>
        void HandOffRules()
        {
            if (_battleRules.Count > 0) _game.NextBattleRules = new Dictionary<string, object>(_battleRules, StringComparer.OrdinalIgnoreCase);
            _battleRules.Clear();
        }

        bool TakeRule(string name) { object v; bool on = _battleRules.TryGetValue(name, out v) && Truthy(v); return on; }

        /// <summary>TrainerBattle.start(:TYPE, "Name"[, version][, :TYPE2, "Name2"[, version2]]).</summary>
        object TrainerBattleArgs(List<object> a, Interpreter ctx)
        {
            var list = new List<object[]>();
            int i = 0;
            while (i + 1 < a.Count && list.Count < 2)
            {
                string type = Str(a, i), name = Str(a, i + 1); i += 2;
                int version = 0;
                if (i < a.Count && !(a[i] is string) && a[i] != null) { version = ToInt(a[i]); i++; }
                list.Add(new object[] { type, name, version });
            }
            if (list.Count == 0) return true;
            return TrainerBattleList(list, ctx);
        }

        /// <summary>One trainer (a double battle if setBattleRule("double") was used), or two trainers in a double battle.</summary>
        object TrainerBattleList(List<object[]> trainers, Interpreter ctx)
        {
            bool triple = IsTripleRule();
            bool dbl = trainers.Count > 1 || triple || IsDoubleRule();
            if (!dbl) return TrainerBattle((string)trainers[0][0], (string)trainers[0][1], (int)trainers[0][2], ctx);
            if (_game.ResumeOutcome.HasValue && ctx != null && _game.ResumeOwner == ctx)
            {
                var o = _game.ResumeOutcome.Value; _game.ResumeOutcome = null;
                return o == PokeCreator.Battles.BattleOutcome.Win;
            }
            bool canLose = TakeRule("canLose");
            int outcomeVar = OutcomeVar();
            HandOffRules();
            var t2 = trainers.Count > 1 ? trainers[1] : null;
            if (!_game.StartDoubleTrainerBattle((string)trainers[0][0], (string)trainers[0][1], (int)trainers[0][2], t2 != null ? (string)t2[0] : null, t2 != null ? (string)t2[1] : null, t2 != null ? (int)t2[2] : 0, canLose, o => _game.State.SetVar(outcomeVar, (int)o), triple))
            {
                WarnOnce("Double trainer battle skipped (trainer not found in the database, or no Pokémon that can battle)");
                return true;
            }
            return false;
        }

        /// <summary>setBattleRule("triple") / ("3v3") / ("size", "3v3").</summary>
        bool IsTripleRule()
        {
            object v;
            if (TakeRule("triple") || TakeRule("3v3")) return true;
            if (_battleRules.TryGetValue("size", out v) && Convert.ToString(v).StartsWith("3v")) { _battleRules.Remove("size"); return true; }
            return false;
        }

        /// <summary>setBattleRule("double") / ("2v2") / ("size", "2v2").</summary>
        bool IsDoubleRule()
        {
            object v;
            if (TakeRule("double") || TakeRule("2v2")) return true;
            return _battleRules.TryGetValue("size", out v) && Convert.ToString(v).StartsWith("2v");
        }

        object DoubleWildBattle(string s1, int l1, string s2, int l2, bool asBool, Interpreter ctx)
        {
            if (_game.ResumeOutcome.HasValue && ctx != null && _game.ResumeOwner == ctx)
            {
                var o = _game.ResumeOutcome.Value; _game.ResumeOutcome = null;
                return asBool ? (object)(o != PokeCreator.Battles.BattleOutcome.Loss) : (int)o;
            }
            int outcomeVar = OutcomeVar();
            HandOffRules();
            _game.StartDoubleWildBattle(s1, l1, s2, l2, o => _game.State.SetVar(outcomeVar, (int)o));
            return asBool ? (object)true : 1;
        }

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
            HandOffRules();
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
            HandOffRules();
            _game.StartWildBattle(species, level, o => _game.State.SetVar(outcomeVar, (int)o));
            return asBool ? (object)true : 1;
        }

        object AddPokemonArg(List<object> a, bool announce)
        {
            var given = Arg(a, 0) as PokeCreator.Pkmn.Pokemon;
            if (given != null) return AddPokemon(given, announce);
            return AddPokemon(Str(a, 0), a.Count > 1 ? ToInt(a[1]) : 5, announce);
        }

        object AddPokemon(string species, int level, bool announce)
        {
            var pk = PokeCreator.Pkmn.Pokemon.Create(_game.Project, species, level, _game.Rng);
            if (pk.Data(_game.Project) == null) { WarnOnce("Unknown species " + species); return false; }
            return AddPokemon(pk, announce);
        }

        object AddPokemon(PokeCreator.Pkmn.Pokemon pk, bool announce)
        {
            if (pk.Data(_game.Project) == null) { WarnOnce("Unknown species " + pk.Species); return false; }
            if (string.IsNullOrEmpty(pk.OTName)) { pk.OTName = _game.State.PlayerName; pk.OTId = _game.State.TrainerId; }
            pk.MetMap = _game.Map.Id;
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

        // The event a script switch is being checked for (no interpreter is running then)
        int _curMap, _curEvent;

        /// <summary>Evaluates an Essentials script switch ("s:" name) for an event.</summary>
        public bool EvaluateSwitchScript(string script, int mapId, int eventId)
        {
            int om = _curMap, oe = _curEvent;
            _curMap = mapId; _curEvent = eventId;
            try { return Evaluate(script, null); }
            finally { _curMap = om; _curEvent = oe; }
        }

        string PlayerGender()
        {
            var look = _game.Player != null ? _game.Player.Look : null;
            var tt = look != null ? _game.Project.TrainerTypes.FirstOrDefault(t => t.Id == look.TrainerType) : null;
            return tt != null ? tt.Gender ?? "Male" : "Male";
        }

        /// <summary>A Ruby proc passed to a script call: proc { |pkmn| pkmn.can_relearn_move? }.</summary>
        class ScriptProc { public string Param, Body; }

        /// <summary>Calls a proc with one argument (the parameter becomes a local variable).</summary>
        bool CallProc(ScriptProc p, object arg)
        {
            var saved = _locals;
            _locals = new Dictionary<string, object>(saved);
            _locals[p.Param] = arg;
            try { return Truthy(Eval(p.Body, null)); }
            catch { return true; }
            finally { _locals = saved; }
        }

        /// <summary>Moves a Pokémon can relearn: its level-up moves up to its level that it doesn't know.</summary>
        List<string> RelearnableMoves(PokeCreator.Pkmn.Pokemon pk)
        {
            var d = pk.Data(_game.Project);
            if (d == null || pk.IsEgg) return new List<string>();
            return d.Moves.Where(m => m.Level <= pk.Level).Select(m => m.Move).Distinct().Where(m => !pk.Moves.Any(x => x.Id == m) && _game.Project.Moves.Any(x => x.Id == m)).ToList();
        }

        static int StatIndex(object key)
        {
            string k = Convert.ToString(key).ToUpperInvariant();
            switch (k) { case "HP": return 0; case "ATTACK": return 1; case "DEFENSE": return 2; case "SPECIAL_ATTACK": case "SPATK": return 3; case "SPECIAL_DEFENSE": case "SPDEF": return 4; case "SPEED": return 5; }
            int n; return int.TryParse(k, out n) ? n : -1;
        }

        static object SettingsConstant(string name)
        {
            switch (name)
            {
                case "MAX_COINS": return 99999;
                case "MAX_BATTLE_POINTS": return 9999;
                case "MAX_MONEY": return 999999;
                case "MAX_PLAYER_NAME_SIZE": return 10;
                case "MAX_PARTY_SIZE": return GameState.MaxParty;
                case "MAXIMUM_LEVEL": return PokeCreator.Pkmn.GrowthRates.MaxLevel;
                case "NUM_STORAGE_BOXES": return 40;
                case "EGG_LEVEL": return 1;
                default: return 0;
            }
        }

        /// <summary>Reads a member of an object held in a local variable (Pokémon, item, species, move data).</summary>
        object Member(object obj, string member, List<object> args)
        {
            // Ruby conversions that work on anything
            if (member == "to_s") return obj == null ? "" : obj is bool ? ((bool)obj ? "true" : "false") : Convert.ToString(obj, CultureInfo.InvariantCulture);
            if (member == "to_i") { if (obj == null) return 0; int iv; return obj is string ? (int.TryParse((string)obj, out iv) ? iv : 0) : ToInt(obj); }
            if (member == "nil?") return obj == null;
            // Data records built as hashes (GameData::Stat.get(x).name)
            var rec = obj as Dictionary<string, object>;
            if (rec != null && args.Count == 0 && rec.ContainsKey(member)) return rec[member];
            if (member == "is_a?" || member == "kind_of?")
            {
                string t = args.Count > 0 ? Convert.ToString(args[0]) : "";
                switch (t)
                {
                    case "Symbol": case "String": return obj is string;
                    case "Integer": case "Numeric": case "Fixnum": return obj is int || obj is double;
                    case "Array": return obj is System.Collections.IList && !(obj is string);
                    case "Hash": return obj is Dictionary<string, object>;
                    case "Pokemon": return obj is PokeCreator.Pkmn.Pokemon;
                    default: return obj != null && obj.GetType().Name == t;
                }
            }
            if (obj == null) return null;
            var hash = obj as Dictionary<string, object>;
            if (hash != null)
            {
                switch (member)
                {
                    case "merge!": case "update": { var o = args.Count > 0 ? args[0] as Dictionary<string, object> : null; if (o != null) foreach (var kv in o) hash[kv.Key] = kv.Value; return hash; }
                    case "merge": { var copy = new Dictionary<string, object>(hash); var o = args.Count > 0 ? args[0] as Dictionary<string, object> : null; if (o != null) foreach (var kv in o) copy[kv.Key] = kv.Value; return copy; }
                    case "keys": return hash.Keys.Cast<object>().ToList();
                    case "values": return hash.Values.ToList();
                    case "has_key?": case "key?": case "include?": return args.Count > 0 && hash.ContainsKey(Convert.ToString(args[0], CultureInfo.InvariantCulture));
                    case "length": case "size": case "count": return hash.Count;
                    case "empty?": return hash.Count == 0;
                    case "delete": { object v = null; if (args.Count > 0) { string k = Convert.ToString(args[0], CultureInfo.InvariantCulture); hash.TryGetValue(k, out v); hash.Remove(k); } return v; }
                }
            }
            var list = obj as System.Collections.IList;
            if (list != null && !(obj is string))
            {
                var items = list.Cast<object>().ToList();
                switch (member)
                {
                    case "length": case "size": case "count": return items.Count;
                    case "empty?": return items.Count == 0;
                    case "first": return items.FirstOrDefault();
                    case "last": return items.LastOrDefault();
                    case "sample": return items.Count > 0 ? items[_game.Rng.Next(items.Count)] : null;
                    case "include?": return args.Count > 0 && items.Any(x => Equals(Convert.ToString(x, CultureInfo.InvariantCulture), Convert.ToString(args[0], CultureInfo.InvariantCulture)));
                    case "push": case "<<": foreach (var x in args) list.Add(x); return list;
                    case "compact!": case "compact": for (int i = list.Count - 1; i >= 0; i--) if (list[i] == null) list.RemoveAt(i); return list;
                    case "sum": return items.Sum(x => ToInt(x));
                    case "max": return items.Count > 0 ? items.Max(x => ToInt(x)) : 0;
                    case "min": return items.Count > 0 ? items.Min(x => ToInt(x)) : 0;
                    case "uniq": return items.Distinct().ToList();
                    case "uniq!": { var u = items.Distinct().ToList(); list.Clear(); foreach (var x in u) list.Add(x); return list; }
                    case "reverse": { var r = items.ToList(); r.Reverse(); return r; }
                    case "sort": return items.OrderBy(x => x is int ? (object)(int)x : Convert.ToString(x, CultureInfo.InvariantCulture)).ToList();
                    case "shuffle": return items.OrderBy(_ => _game.Rng.Next()).ToList();
                    case "pop": { if (list.Count == 0) return null; var x = list[list.Count - 1]; list.RemoveAt(list.Count - 1); return x; }
                    case "shift": { if (list.Count == 0) return null; var x = list[0]; list.RemoveAt(0); return x; }
                    case "unshift": { for (int k = args.Count - 1; k >= 0; k--) list.Insert(0, args[k]); return list; }
                    case "delete": { object hit = null; for (int k = list.Count - 1; k >= 0; k--) if (SameValue(list[k], args.Count > 0 ? args[0] : null)) { hit = list[k]; list.RemoveAt(k); } return hit; }
                    case "delete_at": { int k = ToInt(args.Count > 0 ? args[0] : null); if (k < 0) k += list.Count; if (k < 0 || k >= list.Count) return null; var x = list[k]; list.RemoveAt(k); return x; }
                    case "index": { for (int k = 0; k < items.Count; k++) if (SameValue(items[k], args.Count > 0 ? args[0] : null)) return k; return null; }
                    case "clear": list.Clear(); return list;
                    case "concat": case "+": { var r = items.ToList(); foreach (var a2 in args) { var l2 = a2 as System.Collections.IList; if (l2 != null) r.AddRange(l2.Cast<object>()); } if (member == "concat") { foreach (var x in r.Skip(items.Count)) list.Add(x); return list; } return r; }
                    case "flatten": return items.SelectMany(x => x is System.Collections.IList && !(x is string) ? ((System.Collections.IList)x).Cast<object>() : new[] { x }).ToList();
                    case "join": return string.Join(args.Count > 0 ? Convert.ToString(args[0]) : "", items.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)));
                    case "any?": return items.Count > 0;
                    case "none?": return items.Count == 0;
                    case "each_value": return items;
                    case "to_a": case "dup": case "clone": return items.ToList();
                    case "rand": return items.Count > 0 ? items[_game.Rng.Next(items.Count)] : null;
                }
            }
            if (obj is string)
            {
                var s = (string)obj;
                switch (member)
                {
                    case "length": case "size": return s.Length;
                    case "upcase": return s.ToUpperInvariant();
                    case "downcase": return s.ToLowerInvariant();
                    case "empty?": return s.Length == 0;
                    case "to_sym": return s;
                    case "capitalize": return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
                    case "strip": return s.Trim();
                    case "include?": return args.Count > 0 && s.Contains(Convert.ToString(args[0], CultureInfo.InvariantCulture));
                    case "start_with?": return args.Count > 0 && s.StartsWith(Convert.ToString(args[0], CultureInfo.InvariantCulture));
                    case "end_with?": return args.Count > 0 && s.EndsWith(Convert.ToString(args[0], CultureInfo.InvariantCulture));
                    case "reverse": return new string(s.Reverse().ToArray());
                    case "chars": return s.Select(ch => (object)ch.ToString()).ToList();
                    case "split": return s.Split(new[] { args.Count > 0 ? Convert.ToString(args[0]) : " " }, StringSplitOptions.None).Cast<object>().ToList();
                }
            }
            var pk = obj as PokeCreator.Pkmn.Pokemon;
            var P = _game.Project;
            if (pk != null)
            {
                switch (member)
                {
                    case "name": return pk.Name(P);
                    case "speciesName": return pk.Data(P) != null ? pk.Data(P).Name : pk.Species;
                    case "species": return pk.Species;
                    case "level": return pk.Level;
                    case "form": return pk.Form;
                    case "item": return pk.Item;
                    case "hasItem?": return args.Count == 0 ? !string.IsNullOrEmpty(pk.Item) : pk.Item == Convert.ToString(args[0]);
                    case "isSpecies?": return pk.Species == Convert.ToString(args.Count > 0 ? args[0] : null);
                    case "shiny?": return pk.Shiny;
                    case "male?": return pk.Gender == "M";
                    case "female?": return pk.Gender == "F";
                    case "egg?": case "isEgg?": return pk.IsEgg;
                    case "fainted?": return pk.Fainted;
                    case "able?": return !pk.Fainted && !pk.IsEgg;
                    case "hp": return pk.HP;
                    case "totalhp": return pk.TotalHP(P);
                    case "nature": return pk.Nature;
                    case "happiness": return pk.Happiness;
                    case "pokerus": return pk.Pokerus;
                    case "shadowPokemon?": case "shadow?": return pk.ShadowPokemon;
                    case "makeShadow": PokeCreator.Pkmn.Shadow.Make(_game.Project, pk); return true;
                    case "heart_gauge": case "heartgauge": return pk.HeartGauge;
                    case "heartStage": return PokeCreator.Pkmn.Shadow.Stage(pk);
                    case "purifiable?": return PokeCreator.Pkmn.Shadow.Purifiable(pk);
                    case "adjustHeart": PokeCreator.Pkmn.Shadow.AdjustHeart(pk, args.Count > 0 ? ToInt(args[0]) : 0); return true;
                    case "pokerusStage": return pk.PokerusStage;
                    case "givePokerus": pk.GivePokerus(_game.Rng, args.Count > 0 ? ToInt(args[0]) : 0); return true;
                    case "resetPokerusTime": if (pk.PokerusStage == 1) pk.Pokerus = (pk.Pokerus & 0xF0) | ((pk.Pokerus >> 4) % 4 + 1); return true;
                    case "lowerPokerusCount": pk.LowerPokerusCount(1); return true;
                    case "ability": case "ability_id": return pk.AbilityId(P);
                    case "iv": return pk.IV;
                    case "ev": return pk.EV;
                    case "owner": case "ot": return pk.OTName;
                    case "hasType?": return pk.Types(P).Contains(Convert.ToString(args.Count > 0 ? args[0] : null));
                    case "hasMove?": case "knowsMove?": return pk.Moves.Any(m => m.Id == Convert.ToString(args.Count > 0 ? args[0] : null));
                    case "learn_move":
                        {
                            string id = Convert.ToString(args.Count > 0 ? args[0] : null);
                            var md = P.Moves.FirstOrDefault(x => x.Id == id);
                            if (md != null && !pk.Moves.Any(x => x.Id == id)) { if (pk.Moves.Count >= 4) pk.Moves.RemoveAt(0); pk.Moves.Add(new PokeCreator.Pkmn.PokemonMove { Id = id, PP = md.TotalPP }); }
                            return true;
                        }
                    case "heal": pk.Heal(P); return true;
                    case "numMoves": return pk.Moves.Count;
                    case "foreign?": return pk.OTName != _game.State.PlayerName || pk.OTId != _game.State.TrainerId;
                    case "can_relearn_move?": return RelearnableMoves(pk).Count > 0;
                    case "forget_move_at_index": { int i = ToInt(args.Count > 0 ? args[0] : null); if (i >= 0 && i < pk.Moves.Count && pk.Moves.Count > 1) pk.Moves.RemoveAt(i); return true; }
                    case "forget_move": { string id = Convert.ToString(args.Count > 0 ? args[0] : null); if (pk.Moves.Count > 1) pk.Moves.RemoveAll(x => x.Id == id); return true; }
                    case "moves": return pk.Moves;
                    case "calc_stats": return true;
                    case "record_first_moves": case "reset_moves": return true;
                    case "giveRibbon": { string r = Convert.ToString(args.Count > 0 ? args[0] : ""); if (r.Length > 0 && !pk.Ribbons.Contains(r)) pk.Ribbons.Add(r); return true; }
                    case "hasRibbon?": return pk.Ribbons.Contains(Convert.ToString(args.Count > 0 ? args[0] : ""));
                    case "takeRibbon": pk.Ribbons.Remove(Convert.ToString(args.Count > 0 ? args[0] : "")); return true;
                    case "numRibbons": return pk.Ribbons.Count;
                    case "ribbons": return pk.Ribbons.Cast<object>().ToList();
                    case "clearAllRibbons": pk.Ribbons.Clear(); return true;
                    case "upgradeRibbon":
                        {
                            // Gives the next ribbon of a series (Normal -> Super -> Hyper -> Master); nil if it has the last one
                            var series = args.Select(x => Convert.ToString(x)).ToList();
                            if (series.Count == 0) return null;
                            for (int i = series.Count - 2; i >= 0; i--)
                                if (pk.Ribbons.Contains(series[i])) { pk.Ribbons[pk.Ribbons.IndexOf(series[i])] = series[i + 1]; return series[i + 1]; }
                            if (pk.Ribbons.Contains(series[series.Count - 1])) return null;
                            pk.Ribbons.Add(series[0]); return series[0];
                        }
                    case "changeHappiness": pk.ChangeHappiness(Convert.ToString(args.Count > 0 ? args[0] : "")); return true;
                    case "cool": return pk.Cool;
                    case "beauty": return pk.Beauty;
                    case "cute": return pk.Cute;
                    case "smart": return pk.Smart;
                    case "tough": return pk.Tough;
                    case "sheen": return pk.Sheen;
                    case "calcIV": case "ivs": return pk.IV;
                    case "item_id": return pk.Item;
                    case "gender": return pk.Gender == "M" ? 0 : pk.Gender == "F" ? 1 : 2;
                    case "ability_index": return pk.AbilityIndex;
                    case "poke_ball": case "ballused": return pk.Ball;
                    case "makeMale": pk.Gender = "M"; return true;
                    case "makeFemale": pk.Gender = "F"; return true;
                    case "makeShiny": pk.Shiny = true; return true;
                    case "makeNotShiny": pk.Shiny = false; return true;
                    case "genderless?": return string.IsNullOrEmpty(pk.Gender);
                    case "types": return pk.Types(P).Cast<object>().ToList();
                    case "type1": return pk.Types(P).FirstOrDefault();
                    case "type2": { var ts = pk.Types(P); return ts.Count > 1 ? ts[1] : ts.FirstOrDefault(); }
                    case "getMoveList": { var d = pk.Data(P); return d == null ? new List<object>() : d.Moves.Select(m => (object)new List<object> { m.Level, m.Move }).ToList(); }
                    case "baseStats": { var d = pk.Data(P); return d == null ? new List<object>() : d.BaseStats.Cast<object>().ToList(); }
                    case "exp": return pk.Exp;
                    case "status": return string.IsNullOrEmpty(pk.Status) ? "NONE" : pk.Status;
                    case "heal_status": pk.Status = ""; return true;
                    case "heal_HP": pk.HP = pk.TotalHP(P); return true;
                    case "heal_PP": pk.Heal(P); return true;
                    case "obtain_map": return pk.MetMap;
                    case "obtain_level": return pk.MetLevel;
                }
                WarnOnce("Unsupported Pokémon member: " + member);
                return null;
            }
            // Data records (GameData::Item.get(x).name ...): properties by name
            if (member == "name" || member == "real_name")
            {
                var prop = obj.GetType().GetProperty("Name");
                if (prop != null) return prop.GetValue(obj, null);
            }
            if (member == "id") { var prop = obj.GetType().GetProperty("Id"); if (prop != null) return prop.GetValue(obj, null); }
            string pascal = string.Concat(member.TrimEnd('?', '!').Split('_').Select(w => w.Length == 0 ? "" : char.ToUpperInvariant(w[0]) + w.Substring(1)));
            var pp = obj.GetType().GetProperty(pascal) ?? obj.GetType().GetProperty(member.TrimEnd('?', '!'));
            if (pp != null) return pp.GetValue(obj, null);
            // Script API objects (pbBugContestState...): public methods by name
            string bare = member.TrimEnd('?', '!');
            var mi = obj.GetType().GetMethods().FirstOrDefault(x => (x.Name == bare || x.Name == pascal) && x.GetParameters().Length == args.Count);
            if (mi != null)
            {
                var ps = mi.GetParameters();
                var conv = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    var t = ps[i].ParameterType;
                    conv[i] = t == typeof(int) ? (object)ToInt(args[i]) : t == typeof(string) ? (object)(args[i] == null ? null : Convert.ToString(args[i], CultureInfo.InvariantCulture)) : t == typeof(bool) ? (object)Truthy(args[i]) : args[i];
                }
                return mi.Invoke(obj, conv);
            }
            WarnOnce("Unsupported member: " + obj.GetType().Name + "." + member);
            return null;
        }

        void SetMember(object obj, string member, object index, object val)
        {
            var pk = obj as PokeCreator.Pkmn.Pokemon;
            if (pk == null) { WarnOnce("Can't set " + member); return; }
            switch (member)
            {
                case "item": pk.Item = val == null ? null : Convert.ToString(val); FormRules.UpdateItemForm(_game.Project, pk); return;
                case "form": pk.Form = ToInt(val); return;
                case "level": pk.Level = Math.Max(1, Math.Min(PokeCreator.Pkmn.GrowthRates.MaxLevel, ToInt(val))); return;
                case "name": pk.Nickname = val == null || Convert.ToString(val).Length == 0 ? null : Convert.ToString(val); return;
                case "shiny": pk.Shiny = Truthy(val); return;
                case "nature": pk.Nature = Convert.ToString(val); return;
                case "happiness": pk.Happiness = Math.Max(0, Math.Min(255, ToInt(val))); return;
                case "cool": pk.Cool = Math.Max(0, Math.Min(255, ToInt(val))); return;
                case "beauty": pk.Beauty = Math.Max(0, Math.Min(255, ToInt(val))); return;
                case "cute": pk.Cute = Math.Max(0, Math.Min(255, ToInt(val))); return;
                case "smart": pk.Smart = Math.Max(0, Math.Min(255, ToInt(val))); return;
                case "tough": pk.Tough = Math.Max(0, Math.Min(255, ToInt(val))); return;
                case "sheen": pk.Sheen = Math.Max(0, Math.Min(255, ToInt(val))); return;
                case "ability": pk.Ability = Convert.ToString(val); return;
                case "owner": case "ot": pk.OTName = Convert.ToString(val); return;
                case "gender": pk.Gender = ToInt(val) == 1 ? "F" : ToInt(val) == 0 ? "M" : ""; return;
                case "iv": { int s = StatIndex(index); if (s >= 0 && s < 6) pk.IV[s] = Math.Max(0, Math.Min(31, ToInt(val))); return; }
                case "ability_index": pk.AbilityIndex = ToInt(val); pk.Ability = null; return;
                case "poke_ball": case "ballused": pk.Ball = val == null ? null : Convert.ToString(val); return;
                case "species": pk.Species = Convert.ToString(val); return;
                case "exp": pk.Exp = ToInt(val); return;
                case "hp": pk.HP = Math.Max(0, Math.Min(pk.TotalHP(_game.Project), ToInt(val))); return;
                case "status": pk.Status = val == null || Convert.ToString(val) == "NONE" ? "" : Convert.ToString(val); return;
                case "obtain_text": case "obtain_map": case "obtain_level": case "hatched_map": return;
                case "ev": { int s = StatIndex(index); if (s >= 0 && s < 6) pk.EV[s] = Math.Max(0, Math.Min(252, ToInt(val))); return; }
            }
            WarnOnce("Can't set Pokémon member " + member);
        }

        /// <summary>pbChoosePokemon(var, nameVar): the party screen; var = index (-1 if cancelled), nameVar = name.</summary>
        void ChoosePokemon(List<object> a, Func<PokeCreator.Pkmn.Pokemon, bool> allowed)
        {
            int v = a.Count > 0 ? ToInt(a[0]) : 1, nameVar = a.Count > 1 ? ToInt(a[1]) : 0;
            var proc = a.Count > 2 ? a[2] as ScriptProc : null;
            if (proc != null) { var basic = allowed; allowed = pk => basic(pk) && CallProc(proc, pk); }
            _game.State.SetVar(v, -1);
            _game.PushScene(new UI.PartyScene(_game, "Choose a Pokémon.", pk => allowed(pk) ? null : "NOT ABLE", (i, close) =>
            {
                var pk = _game.State.Party[i];
                if (!allowed(pk)) { _game.Audio.PlaySe("GUI sel buzzer"); return; }
                _game.State.SetVar(v, i);
                if (nameVar > 0) _game.State.SetStr(nameVar, pk.Name(_game.Project));
                close();
                if (_game.Map != null) _game.Map.RefreshEvents();
            }, () => { _game.State.SetVar(v, -1); }));
        }
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
            if (op == '+' && (a is string || b is string)) return Convert.ToString(a, CultureInfo.InvariantCulture) + Convert.ToString(b, CultureInfo.InvariantCulture);
            var la = a as System.Collections.IList; var lb = b as System.Collections.IList;
            if (la != null && !(a is string))
            {
                var items = la.Cast<object>().ToList();
                if (op == '+' && lb != null) { items.AddRange(lb.Cast<object>()); return items; }
                if (op == '-' && lb != null) { var rm = lb.Cast<object>().ToList(); return items.Where(it => !rm.Any(r => SameValue(it, r))).ToList(); }
                if (op == '*') { var r = new List<object>(); for (int k = 0; k < ToInt(b); k++) r.AddRange(items); return r; }
            }
            if (op == '*' && a is string) return string.Concat(Enumerable.Repeat((string)a, Math.Max(0, ToInt(b))));
            if (a is double || b is double)
            {
                double dx = Convert.ToDouble(a ?? 0, CultureInfo.InvariantCulture), dy = Convert.ToDouble(b ?? 0, CultureInfo.InvariantCulture);
                switch (op) { case '+': return dx + dy; case '-': return dx - dy; case '*': return dx * dy; case '/': return dy == 0 ? 0 : dx / dy; case '%': return dy == 0 ? 0 : dx % dy; }
            }
            int x = ToInt(a), y = ToInt(b);
            switch (op)
            {
                case '+': return x + y; case '-': return x - y; case '*': return x * y;
                case '/': return y == 0 ? 0 : (int)Math.Floor((double)x / y);
                case '%': if (y == 0) return 0; int m = x % y; return m != 0 && ((m < 0) != (y < 0)) ? m + y : m;
            }
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
        static int FindLastTopLevel(string s, string op)
        {
            int last = -1, from = 0;
            while (true)
            {
                int i = FindTopLevel(s.Substring(from), op);
                if (i < 0) break;
                // FindTopLevel works on the tail: make sure the cut isn't inside brackets of the whole string
                int abs = from + i;
                if (DepthAt(s, abs) == 0) last = abs;
                from = abs + op.Length;
                if (from >= s.Length) break;
            }
            return last;
        }

        static int DepthAt(string s, int pos)
        {
            int depth = 0; bool inStr = false; char q = '\0';
            for (int i = 0; i < pos; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == q) inStr = false; continue; }
                if (c == '"' || c == '\'') { inStr = true; q = c; continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
            }
            return inStr ? 1 : depth;
        }

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

        static int MatchingBracket(string s, int open)
        {
            int depth = 0; bool inStr = false; char q = '\0';
            for (int i = open; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == q) inStr = false; continue; }
                if (c == '"' || c == '\'') { inStr = true; q = c; continue; }
                if (c == '[' || c == '(' || c == '{') depth++;
                else if (c == ']' || c == ')' || c == '}') { depth--; if (depth == 0) return i; }
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
