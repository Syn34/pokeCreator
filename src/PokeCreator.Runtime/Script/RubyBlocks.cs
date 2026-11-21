using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PokeCreator.Runtime.Script
{
    /// <summary>
    /// Ruby control flow in event scripts: if / elsif / else / unless, case / when, while / until, for … in,
    /// .each / .each_with_index / .times / loop do blocks, begin / rescue, break / next / return and
    /// "statement if condition" modifiers — so Essentials' example scripts (the stats judge, the move tutor
    /// lists…) run as written.
    /// </summary>
    public partial class EssentialsCompat
    {
        class RNode
        {
            public string Kind;                 // stmt, if, while, until, for, each, loop, case, begin, raw
            public string Text;                 // statement / condition / collection expression
            public string Var, Var2;            // block variables
            public string Method;               // each, each_with_index, times, reverse_each, each_key...
            public List<KeyValuePair<string, List<RNode>>> Branches; // if/elsif conditions or when values
            public List<RNode> Body;
            public List<RNode> Else;
        }

        enum Flow { None, Break, Next, Return }
        int _steps;

        static readonly Regex ReIf = new Regex(@"^(if|unless)\s+(.+?)(?:\s+then)?$");
        static readonly Regex ReWhile = new Regex(@"^(while|until)\s+(.+?)(?:\s+do)?$");
        static readonly Regex ReFor = new Regex(@"^for\s+(\w+)(?:\s*,\s*(\w+))?\s+in\s+(.+?)(?:\s+do)?$");
        static readonly Regex ReEach = new Regex(@"^(.+?)\.(each_with_index|each_pair|each_key|each_value|reverse_each|each|times|upto\((.+)\)|downto\((.+)\))\s+do(?:\s*\|\s*(\w+)(?:\s*,\s*(\w+))?\s*\|)?$");
        static readonly Regex ReDoBlock = new Regex(@"\bdo(\s*\|[^|]*\|)?$");

        static bool IsBlockStart(string s)
        {
            return ReIf.IsMatch(s) || ReWhile.IsMatch(s) || ReFor.IsMatch(s) || s == "loop do" || s == "begin" || s.StartsWith("case ") || s == "case" || ReDoBlock.IsMatch(s);
        }

        static bool IsTerminator(string s)
        {
            return s == "end" || s == "else" || s.StartsWith("elsif ") || s.StartsWith("when ") || s == "rescue" || s.StartsWith("rescue ") || s == "ensure" || s.StartsWith("end.") || Regex.IsMatch(s, @"^end\s+(if|unless)\s");
        }

        List<RNode> ParseBlock(List<string> st, ref int i)
        {
            var list = new List<RNode>();
            while (i < st.Count)
            {
                string s = st[i];
                if (IsTerminator(s)) return list;
                i++;
                Match m;
                if ((m = ReIf.Match(s)).Success)
                {
                    var n = new RNode { Kind = "if", Branches = new List<KeyValuePair<string, List<RNode>>>() };
                    string cond = m.Groups[1].Value == "unless" ? "!(" + m.Groups[2].Value + ")" : m.Groups[2].Value;
                    n.Branches.Add(new KeyValuePair<string, List<RNode>>(cond, ParseBlock(st, ref i)));
                    while (i < st.Count && st[i].StartsWith("elsif "))
                    {
                        string c2 = Regex.Replace(st[i].Substring(6), @"\s+then$", ""); i++;
                        n.Branches.Add(new KeyValuePair<string, List<RNode>>(c2, ParseBlock(st, ref i)));
                    }
                    if (i < st.Count && st[i] == "else") { i++; n.Else = ParseBlock(st, ref i); }
                    SkipEnd(st, ref i);
                    list.Add(n); continue;
                }
                if ((m = ReWhile.Match(s)).Success)
                {
                    var n = new RNode { Kind = m.Groups[1].Value, Text = m.Groups[2].Value };
                    n.Body = ParseBlock(st, ref i); SkipEnd(st, ref i); list.Add(n); continue;
                }
                if ((m = ReFor.Match(s)).Success)
                {
                    var n = new RNode { Kind = "each", Method = m.Groups[2].Success ? "each_pair" : "each", Var = m.Groups[1].Value, Var2 = m.Groups[2].Success ? m.Groups[2].Value : null, Text = m.Groups[3].Value };
                    n.Body = ParseBlock(st, ref i); SkipEnd(st, ref i); list.Add(n); continue;
                }
                if (s == "loop do")
                {
                    var n = new RNode { Kind = "loop" };
                    n.Body = ParseBlock(st, ref i); SkipEnd(st, ref i); list.Add(n); continue;
                }
                if (s == "begin")
                {
                    var n = new RNode { Kind = "begin" };
                    n.Body = ParseBlock(st, ref i);
                    if (i < st.Count && (st[i] == "rescue" || st[i].StartsWith("rescue "))) { i++; n.Else = ParseBlock(st, ref i); }
                    if (i < st.Count && st[i] == "ensure") { i++; n.Body.AddRange(ParseBlock(st, ref i)); }
                    SkipEnd(st, ref i); list.Add(n); continue;
                }
                if (s == "case" || s.StartsWith("case "))
                {
                    var n = new RNode { Kind = "case", Text = s.Length > 4 ? s.Substring(5).Trim() : "", Branches = new List<KeyValuePair<string, List<RNode>>>() };
                    while (i < st.Count && !st[i].StartsWith("when ") && st[i] != "else" && st[i] != "end") i++;
                    while (i < st.Count && st[i].StartsWith("when "))
                    {
                        string w = st[i].Substring(5).Trim(); i++;
                        string inline = null;
                        var tm = Regex.Match(w, @"^(.+?)\s+then\s+(.+)$");
                        if (tm.Success) { w = tm.Groups[1].Value; inline = tm.Groups[2].Value; }
                        else w = Regex.Replace(w, @"\s+then$", "");
                        var body = ParseBlock(st, ref i);
                        if (inline != null) body.Insert(0, new RNode { Kind = "stmt", Text = inline });
                        n.Branches.Add(new KeyValuePair<string, List<RNode>>(w, body));
                    }
                    if (i < st.Count && st[i] == "else") { i++; n.Else = ParseBlock(st, ref i); }
                    SkipEnd(st, ref i); list.Add(n); continue;
                }
                if ((m = ReEach.Match(s)).Success)
                {
                    string method = m.Groups[2].Value;
                    var n = new RNode { Kind = "each", Text = m.Groups[1].Value, Method = method, Var = m.Groups[5].Success ? m.Groups[5].Value : null, Var2 = m.Groups[6].Success ? m.Groups[6].Value : null };
                    if (method.StartsWith("upto(")) { n.Method = "upto"; n.Var2 = null; n.Branches = new List<KeyValuePair<string, List<RNode>>> { new KeyValuePair<string, List<RNode>>(m.Groups[3].Value, null) }; }
                    if (method.StartsWith("downto(")) { n.Method = "downto"; n.Branches = new List<KeyValuePair<string, List<RNode>>> { new KeyValuePair<string, List<RNode>>(m.Groups[4].Value, null) }; }
                    n.Body = ParseBlock(st, ref i); SkipEnd(st, ref i); list.Add(n); continue;
                }
                if (ReDoBlock.IsMatch(s))
                {
                    // A block this runner doesn't know: skip it as a whole
                    var n = new RNode { Kind = "raw", Text = s };
                    n.Body = ParseBlock(st, ref i); SkipEnd(st, ref i); list.Add(n); continue;
                }
                list.Add(new RNode { Kind = "stmt", Text = s });
            }
            return list;
        }

        static void SkipEnd(List<string> st, ref int i)
        {
            if (i < st.Count && (st[i] == "end" || st[i].StartsWith("end.") || st[i].StartsWith("end "))) i++;
        }

        Flow ExecBlock(List<RNode> nodes, Interpreter ctx)
        {
            foreach (var n in nodes)
            {
                if (++_steps > 20000) { WarnOnce("A script loop ran too long and was stopped."); return Flow.Return; }
                var f = ExecNode(n, ctx);
                if (f != Flow.None) return f;
                if (_game.PromptPending) return Flow.Return;
            }
            return Flow.None;
        }

        Flow ExecNode(RNode n, Interpreter ctx)
        {
            switch (n.Kind)
            {
                case "stmt": return ExecStatement(n.Text, ctx);
                case "if":
                    foreach (var b in n.Branches)
                    {
                        bool hit = Cond(b.Key, ctx);
                        if (_game.PromptPending) return Flow.Return; // the condition asked the player: wait for the answer
                        if (hit) return ExecBlock(b.Value, ctx);
                    }
                    return n.Else != null ? ExecBlock(n.Else, ctx) : Flow.None;
                case "while":
                case "until":
                    for (int guard = 0; guard < 10000; guard++)
                    {
                        bool c = Cond(n.Text, ctx);
                        if (_game.PromptPending) return Flow.Return;
                        if (n.Kind == "while" ? !c : c) break;
                        var f = ExecBlock(n.Body, ctx);
                        if (f == Flow.Break) break;
                        if (f == Flow.Return) return f;
                    }
                    return Flow.None;
                case "loop":
                    for (int guard = 0; guard < 10000; guard++)
                    {
                        var f = ExecBlock(n.Body, ctx);
                        if (f == Flow.Break) break;
                        if (f == Flow.Return) return f;
                    }
                    return Flow.None;
                case "begin":
                    try { return ExecBlock(n.Body, ctx); }
                    catch (Exception) { return n.Else != null ? ExecBlock(n.Else, ctx) : Flow.None; }
                case "case":
                    {
                        object v = n.Text.Length > 0 ? Eval(n.Text, ctx) : null;
                        if (_game.PromptPending) return Flow.Return;
                        foreach (var b in n.Branches)
                        {
                            foreach (var opt in SplitTopLevel(b.Key, ','))
                            {
                                bool hit;
                                if (n.Text.Length == 0) hit = Cond(opt, ctx);
                                else
                                {
                                    var rm = Regex.Match(opt.Trim(), @"^(-?\d+)\.\.(\.?)(-?\d+)$");
                                    if (rm.Success) { int lo = int.Parse(rm.Groups[1].Value), hi = int.Parse(rm.Groups[3].Value), x; hit = v != null && int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out x) && x >= lo && (rm.Groups[2].Value == "." ? x < hi : x <= hi); }
                                    else hit = SameValue(v, Eval(opt, ctx));
                                }
                                if (hit) return ExecBlock(b.Value, ctx);
                            }
                        }
                        return n.Else != null ? ExecBlock(n.Else, ctx) : Flow.None;
                    }
                case "each": return ExecEach(n, ctx);
                case "raw":
                    {
                        // The party .each block of older scripts keeps working through its own handler
                        var pm = Regex.Match(n.Text, @"\.each\s+do\s*\|(\w+)\|$");
                        if (pm.Success) EachPokemon(pm.Groups[1].Value, n.Body.Where(b => b.Kind == "stmt").Select(b => b.Text).ToList());
                        else WarnOnce("Can't run the block: " + n.Text);
                        return Flow.None;
                    }
            }
            return Flow.None;
        }

        Flow ExecEach(RNode n, Interpreter ctx)
        {
            var items = new List<object[]>();
            object coll = Eval(n.Text, ctx);
            switch (n.Method)
            {
                case "times": { int c = ToInt(coll); for (int k = 0; k < c; k++) items.Add(new object[] { k }); break; }
                case "upto": { int a = ToInt(coll), b = ToInt(Eval(n.Branches[0].Key, ctx)); for (int k = a; k <= b; k++) items.Add(new object[] { k }); break; }
                case "downto": { int a = ToInt(coll), b = ToInt(Eval(n.Branches[0].Key, ctx)); for (int k = a; k >= b; k--) items.Add(new object[] { k }); break; }
                default:
                    {
                        var dict = coll as IDictionary;
                        if (dict != null)
                        {
                            foreach (DictionaryEntry de in dict)
                            {
                                if (n.Method == "each_key") items.Add(new object[] { de.Key });
                                else if (n.Method == "each_value") items.Add(new object[] { de.Value });
                                else items.Add(new object[] { de.Key, de.Value });
                            }
                            break;
                        }
                        if (coll is string) { items.Add(new object[] { coll }); break; }
                        var en = coll as IEnumerable;
                        if (en == null) { if (coll != null) WarnOnce("Can't loop over " + n.Text); break; }
                        int idx = 0;
                        foreach (var o in en.Cast<object>().ToList()) items.Add(n.Method == "each_with_index" ? new object[] { o, idx++ } : new object[] { o });
                        if (n.Method == "reverse_each") items.Reverse();
                        break;
                    }
            }
            foreach (var it in items)
            {
                if (n.Var != null) _locals[n.Var] = it[0];
                if (n.Var2 != null) _locals[n.Var2] = it.Length > 1 ? it[1] : null;
                else if (n.Var != null && it.Length > 1 && n.Method == "each_pair") _locals[n.Var] = new List<object> { it[0], it[1] };
                var f = ExecBlock(n.Body, ctx);
                if (f == Flow.Break) break;
                if (f == Flow.Return) return f;
            }
            return Flow.None;
        }

        Flow ExecStatement(string s, Interpreter ctx)
        {
            if (s == "break") return Flow.Break;
            if (s == "next") return Flow.Next;
            if (s == "return" || s.StartsWith("return ")) return Flow.Return;
            // "statement if condition" / "statement unless condition"
            string cond; bool negate;
            string body = SplitModifier(s, out cond, out negate);
            if (cond != null)
            {
                bool c = Cond(cond, ctx);
                if (_game.PromptPending) return Flow.Return;
                if (negate ? c : !c) return Flow.None;
                return ExecStatement(body, ctx);
            }
            // "statement rescue fallback"
            int rescueAt = FindTopLevel(s, " rescue ");
            if (rescueAt > 0)
            {
                try { Eval(s.Substring(0, rescueAt), ctx); } catch { try { Eval(s.Substring(rescueAt + 8), ctx); } catch { } }
                return Flow.None;
            }
            try { Eval(s, ctx); }
            catch (Exception ex) { WarnOnce("Script error in \"" + s + "\": " + ex.Message); }
            return Flow.None;
        }

        /// <summary>A method called with a { |x| ... } block.</summary>
        object BlockCall(object recv, string method, object arg, string[] vars, string code, Interpreter ctx)
        {
            var seq = new List<object[]>();
            if (method == "times") { int c = ToInt(recv); for (int k = 0; k < c; k++) seq.Add(new object[] { k }); method = "each"; }
            else if (method == "upto" || method == "downto")
            {
                int a = ToInt(recv), b = ToInt(arg);
                if (method == "upto") for (int k = a; k <= b; k++) seq.Add(new object[] { k }); else for (int k = a; k >= b; k--) seq.Add(new object[] { k });
                method = "each";
            }
            else
            {
                var dict = recv as IDictionary;
                if (dict != null) foreach (DictionaryEntry de in dict) seq.Add(method == "each_value" ? new object[] { de.Value } : method == "each_key" ? new object[] { de.Key } : new object[] { de.Key, de.Value });
                else if (recv is IEnumerable && !(recv is string)) { int i = 0; foreach (var o in ((IEnumerable)recv).Cast<object>().ToList()) seq.Add(method == "each_with_index" ? new object[] { o, i++ } : new object[] { o }); }
            }
            var stmts = SplitStatements(code.Replace(";", "\n")).ToList();
            int pos = 0; var body = ParseBlock(stmts, ref pos);
            Func<object[], object> run = item =>
            {
                for (int k = 0; k < vars.Length; k++) _locals[vars[k]] = k < item.Length ? item[k] : null;
                if (vars.Length == 1 && item.Length > 1) _locals[vars[0]] = item.ToList();
                object last = null;
                foreach (var n in body)
                {
                    if (n.Kind == "stmt") { string cond; bool neg; var b2 = SplitModifier(n.Text, out cond, out neg); if (cond != null) { bool cv = Cond(cond, ctx); if (neg ? cv : !cv) { last = null; continue; } } if (b2 == "next") break; last = Eval(b2, ctx); }
                    else ExecNode(n, ctx);
                }
                return last;
            };
            var items = seq.Select(x => x.Length == 1 ? x[0] : (object)x.ToList()).ToList();
            switch (method)
            {
                case "each": case "each_with_index": case "each_value": case "each_key": case "each_pair": case "reverse_each":
                    if (method == "reverse_each") seq.Reverse();
                    foreach (var it in seq) run(it);
                    return recv;
                case "map": case "collect": return seq.Select(run).ToList();
                case "select": case "filter": case "find_all": return seq.Where(x => Truthy(run(x))).Select(x => x.Length == 1 ? x[0] : (object)x.ToList()).ToList();
                case "reject": return seq.Where(x => !Truthy(run(x))).Select(x => x.Length == 1 ? x[0] : (object)x.ToList()).ToList();
                case "count": return seq.Count(x => Truthy(run(x)));
                case "any?": return seq.Any(x => Truthy(run(x)));
                case "all?": return seq.All(x => Truthy(run(x)));
                case "none?": return !seq.Any(x => Truthy(run(x)));
                case "find": case "detect": { var hit = seq.FirstOrDefault(x => Truthy(run(x))); return hit == null ? null : hit.Length == 1 ? hit[0] : hit.ToList(); }
                case "find_index": case "index": { for (int k = 0; k < seq.Count; k++) if (Truthy(run(seq[k]))) return k; return null; }
                case "sum": return seq.Sum(x => ToInt(run(x)));
                case "max_by": return seq.Count == 0 ? null : items[seq.Select((x, k) => new { v = ToInt(run(x)), k }).OrderByDescending(t => t.v).First().k];
                case "min_by": return seq.Count == 0 ? null : items[seq.Select((x, k) => new { v = ToInt(run(x)), k }).OrderBy(t => t.v).First().k];
                case "sort_by": return seq.Select((x, k) => new { v = run(x), k }).OrderBy(t => t.v is int ? (object)(int)t.v : Convert.ToString(t.v, CultureInfo.InvariantCulture)).Select(t => items[t.k]).ToList();
                case "delete_if": case "reject!": case "select!": case "keep_if":
                    {
                        var list = recv as IList;
                        if (list == null) return recv;
                        bool keepTrue = method == "select!" || method == "keep_if";
                        for (int k = list.Count - 1; k >= 0; k--) { bool r = Truthy(run(seq[k])); if (keepTrue ? !r : r) list.RemoveAt(k); }
                        return list;
                    }
            }
            WarnOnce("Unsupported block method: " + method);
            return null;
        }

        bool Cond(string c, Interpreter ctx)
        {
            try { return Truthy(Eval(c.Trim(), ctx)); }
            catch (Exception ex) { WarnOnce("Condition error in \"" + c + "\": " + ex.Message); return false; }
        }

        static bool SameValue(object a, object b)
        {
            if (a == null || b == null) return a == b;
            string sa = Convert.ToString(a, CultureInfo.InvariantCulture).TrimStart(':'), sb = Convert.ToString(b, CultureInfo.InvariantCulture).TrimStart(':');
            return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Finds a trailing " if cond" / " unless cond" outside strings and brackets.</summary>
        static string SplitModifier(string s, out string cond, out bool negate)
        {
            cond = null; negate = false;
            if (Regex.IsMatch(s, @"^(if|unless|elsif|while|until)\b")) return s;
            int depth = 0; bool inStr = false; char q = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == q) inStr = false; continue; }
                if (c == '"' || c == '\'') { inStr = true; q = c; continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (depth == 0 && c == ' ')
                {
                    if (string.CompareOrdinal(s, i, " if ", 0, 4) == 0) { cond = s.Substring(i + 4); return s.Substring(0, i).Trim(); }
                    if (string.CompareOrdinal(s, i, " unless ", 0, 8) == 0) { cond = s.Substring(i + 8); negate = true; return s.Substring(0, i).Trim(); }
                }
            }
            return s;
        }
    }
}
