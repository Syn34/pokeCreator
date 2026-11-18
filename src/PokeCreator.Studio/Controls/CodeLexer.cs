using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PokeCreator.Studio.Controls
{
    /// <summary>What a piece of code is, for colouring. Mirrors Visual Studio's C# classifications.</summary>
    public enum CodeKind : byte
    {
        Plain, Keyword, Control, Comment, DocTag, String, Escape, Number, Preprocessor, Operator,
        Class, Interface, Enum, Struct, Delegate, Method, JsonKey
    }

    public struct CodeSpan
    {
        public int Start, Length; public CodeKind Kind;
        public CodeSpan(int s, int l, CodeKind k) { Start = s; Length = l; Kind = k; }
    }

    /// <summary>Visual Studio 2022's default C# colours for the Dark and Light themes.</summary>
    public static class CodeColors
    {
        static Color H(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
        public static Color Background(bool dark) { return dark ? H(0x1E1E1E) : H(0xFFFFFF); }
        public static Color GutterBack(bool dark) { return dark ? H(0x1E1E1E) : H(0xFFFFFF); }
        public static Color LineNumber(bool dark) { return dark ? H(0x2B91AF) : H(0x2B91AF); }
        public static Color CurrentLineNumber(bool dark) { return dark ? H(0xEAEAEA) : H(0x000000); }
        public static Color Divider(bool dark) { return dark ? H(0x3F3F46) : H(0xE6E6E6); }

        public static Color Of(CodeKind k, bool dark)
        {
            if (dark)
                switch (k)
                {
                    case CodeKind.Keyword: return H(0x569CD6);
                    case CodeKind.Control: return H(0xD8A0DF);
                    case CodeKind.Comment: return H(0x57A64A);
                    case CodeKind.DocTag: return H(0x608B4E);
                    case CodeKind.String: return H(0xD69D85);
                    case CodeKind.Escape: return H(0xFFD68F);
                    case CodeKind.Number: return H(0xB5CEA8);
                    case CodeKind.Preprocessor: return H(0x9B9B9B);
                    case CodeKind.Operator: return H(0xB4B4B4);
                    case CodeKind.Class: return H(0x4EC9B0);
                    case CodeKind.Delegate: return H(0x4EC9B0);
                    case CodeKind.Interface: return H(0xB8D7A3);
                    case CodeKind.Enum: return H(0xB8D7A3);
                    case CodeKind.Struct: return H(0x86C691);
                    case CodeKind.Method: return H(0xDCDCAA);
                    case CodeKind.JsonKey: return H(0x9CDCFE);
                    default: return H(0xDCDCDC);
                }
            switch (k)
            {
                case CodeKind.Keyword: return H(0x0000FF);
                case CodeKind.Control: return H(0x8F08C4);
                case CodeKind.Comment: return H(0x008000);
                case CodeKind.DocTag: return H(0x808080);
                case CodeKind.String: return H(0xA31515);
                case CodeKind.Escape: return H(0xB776FB);
                case CodeKind.Number: return H(0x000000);
                case CodeKind.Preprocessor: return H(0x808080);
                case CodeKind.Operator: return H(0x000000);
                case CodeKind.Class: case CodeKind.Delegate: case CodeKind.Interface: case CodeKind.Enum: case CodeKind.Struct: return H(0x2B91AF);
                case CodeKind.Method: return H(0x74531F);
                case CodeKind.JsonKey: return H(0x2E75B6);
                default: return H(0x000000);
            }
        }
    }

    /// <summary>
    /// Single-pass C# / JSON tokenizer. Type names come from the .NET and PokeCreator assemblies (so
    /// Pokemon, Game, List and Console get class colours like in Visual Studio) plus types declared in the file.
    /// </summary>
    public static class CodeLexer
    {
        static readonly HashSet<string> Keywords = new HashSet<string>(("abstract as base bool byte char checked class const decimal default delegate double enum event explicit extern " +
            "false fixed float implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref sbyte sealed " +
            "short sizeof stackalloc static string struct this true typeof uint ulong unchecked unsafe ushort using virtual void volatile var dynamic nameof async get set add remove " +
            "partial where value global alias record init nint nuint when unmanaged notnull managed file required scoped").Split(' '));
        static readonly HashSet<string> ControlKeywords = new HashSet<string>("if else switch case do for foreach while break continue goto return throw try catch finally yield await".Split(' '));
        static readonly HashSet<string> ContextualOnlyBeforeBrace = new HashSet<string>("get set add remove init value where alias global file required scoped managed unmanaged notnull partial when record async".Split(' '));

        static Dictionary<string, CodeKind> _known;
        static HashSet<string> _namespaces;
        static readonly object _lock = new object();

        /// <summary>Public type names from .NET and the engine, with their kind (class, struct, enum...).</summary>
        public static Dictionary<string, CodeKind> KnownTypes
        {
            get
            {
                lock (_lock)
                {
                    if (_known != null) return _known;
                    var d = new Dictionary<string, CodeKind>();
                    var ns = new HashSet<string>();
                    var asms = new List<Assembly> { typeof(object).Assembly, typeof(Uri).Assembly, typeof(Enumerable).Assembly, typeof(Projects.Project).Assembly };
                    try
                    {
                        string game = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Game.exe");
                        if (System.IO.File.Exists(game)) asms.Add(Assembly.LoadFrom(game));
                    }
                    catch { }
                    foreach (var a in asms)
                    {
                        Type[] types;
                        try { types = a.GetExportedTypes(); }
                        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null && t.IsPublic).ToArray(); }
                        catch { continue; }
                        foreach (var t in types)
                        {
                            if (t.Namespace != null) foreach (var part in t.Namespace.Split('.')) ns.Add(part);
                            string n = t.Name; int tick = n.IndexOf('`'); if (tick > 0) n = n.Substring(0, tick);
                            if (n.Length < 2 || t.IsNested && !t.IsNestedPublic) continue;
                            CodeKind k = t.IsEnum ? CodeKind.Enum : t.IsInterface ? CodeKind.Interface : t.IsValueType ? CodeKind.Struct
                                : typeof(Delegate).IsAssignableFrom(t) ? CodeKind.Delegate : CodeKind.Class;
                            if (!d.ContainsKey(n)) d[n] = k;
                        }
                    }
                    _namespaces = ns;
                    _known = d;
                    return d;
                }
            }
        }

        static HashSet<string> Namespaces { get { var k = KnownTypes; return _namespaces; } }

        public static List<CodeSpan> Lex(string text, string language)
        {
            if (language == "json") return LexJson(text);
            if (language == "cs") return LexCs(text);
            return new List<CodeSpan>();
        }

        // ------------------------------------------------------------------ JSON
        static List<CodeSpan> LexJson(string t)
        {
            var spans = new List<CodeSpan>();
            int i = 0, n = t.Length;
            while (i < n)
            {
                char c = t[i];
                if (c == '"')
                {
                    int s = i++;
                    var escapes = new List<CodeSpan>();
                    while (i < n && t[i] != '"' && t[i] != '\n')
                    {
                        if (t[i] == '\\' && i + 1 < n) { int len = t[i + 1] == 'u' ? Math.Min(6, n - i) : 2; escapes.Add(new CodeSpan(i, len, CodeKind.Escape)); i += len; }
                        else i++;
                    }
                    if (i < n && t[i] == '"') i++;
                    int j = i; while (j < n && (t[j] == ' ' || t[j] == '\t')) j++;
                    bool key = j < n && t[j] == ':';
                    spans.Add(new CodeSpan(s, i - s, key ? CodeKind.JsonKey : CodeKind.String));
                    if (!key) spans.AddRange(escapes);
                }
                else if (c == '-' || char.IsDigit(c))
                {
                    int s = i++;
                    while (i < n && (char.IsLetterOrDigit(t[i]) || t[i] == '.' || t[i] == '+' || t[i] == '-')) i++;
                    spans.Add(new CodeSpan(s, i - s, CodeKind.Number));
                }
                else if (char.IsLetter(c))
                {
                    int s = i; while (i < n && char.IsLetter(t[i])) i++;
                    string w = t.Substring(s, i - s);
                    if (w == "true" || w == "false" || w == "null") spans.Add(new CodeSpan(s, i - s, CodeKind.Keyword));
                }
                else if (c == '/' && i + 1 < n && t[i + 1] == '/') { int s = i; while (i < n && t[i] != '\n') i++; spans.Add(new CodeSpan(s, i - s, CodeKind.Comment)); }
                else i++;
            }
            return spans;
        }

        // ------------------------------------------------------------------ C#
        static bool IdStart(char c) { return char.IsLetter(c) || c == '_'; }
        static bool IdPart(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        static readonly Regex DeclaredType = new Regex(@"\b(class|struct|interface|enum|record|delegate\s+\w+(?:<[^>]*>)?)\s+(@?[A-Za-z_]\w*)", RegexOptions.Compiled);

        static List<CodeSpan> LexCs(string t)
        {
            var known = KnownTypes;
            var local = new Dictionary<string, CodeKind>();
            foreach (Match m in DeclaredType.Matches(t))
            {
                string kw = m.Groups[1].Value;
                local[m.Groups[2].Value.TrimStart('@')] = kw == "struct" ? CodeKind.Struct : kw == "interface" ? CodeKind.Interface : kw == "enum" ? CodeKind.Enum : kw.StartsWith("delegate") ? CodeKind.Delegate : CodeKind.Class;
            }
            var spans = new List<CodeSpan>();
            var lx = new CsState { T = t, Spans = spans, Known = known, Local = local };
            lx.Run(0, t.Length, false);
            spans.RemoveAll(sp => sp.Length <= 0);
            return spans;
        }

        class CsState
        {
            public string T;
            public List<CodeSpan> Spans;
            public Dictionary<string, CodeKind> Known, Local;
            string _prevWord = "";   // last identifier or keyword
            char _prevSym = '\n';    // last non-space character before the current token
            bool _prevWasType;       // previous identifier was a type or namespace segment

            CodeKind? TypeKind(string w)
            {
                CodeKind k;
                if (Local.TryGetValue(w, out k)) return k;
                if (Known.TryGetValue(w, out k)) return k;
                return null;
            }

            int SkipSpace(int i, int end) { while (i < end && char.IsWhiteSpace(T[i])) i++; return i; }

            /// <summary>Skips a balanced generic argument list starting at '&lt;'. Returns the index after '&gt;' or -1.</summary>
            int SkipGeneric(int i, int end)
            {
                int depth = 0;
                for (; i < end; i++)
                {
                    char c = T[i];
                    if (c == '<') depth++;
                    else if (c == '>') { if (--depth == 0) return i + 1; }
                    else if (!(IdPart(c) || char.IsWhiteSpace(c) || c == ',' || c == '.' || c == '?' || c == '[' || c == ']')) return -1;
                }
                return -1;
            }

            bool AtLineStart(int i)
            {
                for (int j = i - 1; j >= 0; j--) { char c = T[j]; if (c == '\n') return true; if (c != ' ' && c != '\t') return false; }
                return true;
            }

            public void Run(int i, int end, bool inInterpolation)
            {
                int braceDepth = 0;
                while (i < end)
                {
                    char c = T[i];
                    char next = i + 1 < end ? T[i + 1] : '\0';
                    if (char.IsWhiteSpace(c)) { i++; continue; }

                    if (inInterpolation)
                    {
                        if (c == '{') braceDepth++;
                        else if (c == '}') { if (braceDepth == 0) return; braceDepth--; }
                    }

                    // Comments
                    if (c == '/' && next == '/')
                    {
                        int s = i; while (i < end && T[i] != '\n') i++;
                        if (s + 2 < end && T[s + 2] == '/' && (s + 3 >= end || T[s + 3] != '/')) DocComment(s, i);
                        else Spans.Add(new CodeSpan(s, i - s, CodeKind.Comment));
                        continue;
                    }
                    if (c == '/' && next == '*')
                    {
                        int s = i; int e = T.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        i = e < 0 || e + 2 > end ? end : e + 2;
                        Spans.Add(new CodeSpan(s, i - s, CodeKind.Comment));
                        continue;
                    }
                    // Preprocessor
                    if (c == '#' && AtLineStart(i))
                    {
                        int s = i; while (i < end && T[i] != '\n') i++;
                        Spans.Add(new CodeSpan(s, i - s, CodeKind.Preprocessor));
                        continue;
                    }
                    // Strings
                    if (c == '"' || ((c == '@' || c == '$') && (next == '"' || ((next == '@' || next == '$') && i + 2 < end && T[i + 2] == '"'))))
                    {
                        int q = i; bool verbatim = false, interp = false;
                        while (T[q] != '"') { if (T[q] == '@') verbatim = true; if (T[q] == '$') interp = true; q++; }
                        bool raw = !verbatim && q + 2 < end && T[q + 1] == '"' && T[q + 2] == '"';
                        i = String(i, q, end, verbatim, interp, raw);
                        _prevSym = '"'; _prevWord = ""; _prevWasType = false;
                        continue;
                    }
                    if (c == '\'')
                    {
                        int s = i++;
                        while (i < end && T[i] != '\'' && T[i] != '\n') { if (T[i] == '\\') { Spans.Add(new CodeSpan(i, Math.Min(2, end - i), CodeKind.Escape)); i += 2; } else i++; }
                        if (i < end && T[i] == '\'') i++;
                        AddUnder(s, i, CodeKind.String);
                        _prevSym = '\''; continue;
                    }
                    // Numbers
                    if (char.IsDigit(c) || (c == '.' && char.IsDigit(next)))
                    {
                        int s = i;
                        if (c == '0' && (next == 'x' || next == 'X' || next == 'b' || next == 'B')) i += 2;
                        while (i < end && (char.IsLetterOrDigit(T[i]) || T[i] == '_' || (T[i] == '.' && i + 1 < end && char.IsDigit(T[i + 1])) ||
                            ((T[i] == '+' || T[i] == '-') && (T[i - 1] == 'e' || T[i - 1] == 'E') && !(c == '0' && (next == 'x' || next == 'X'))))) i++;
                        Spans.Add(new CodeSpan(s, i - s, CodeKind.Number));
                        _prevSym = '0'; _prevWord = ""; _prevWasType = false;
                        continue;
                    }
                    // Identifiers and keywords
                    if (IdStart(c) || (c == '@' && i + 1 < end && IdStart(next)))
                    {
                        int s = i; bool escaped = c == '@'; if (escaped) i++;
                        while (i < end && IdPart(T[i])) i++;
                        string w = T.Substring(escaped ? s + 1 : s, i - (escaped ? s + 1 : s));
                        Identifier(s, i, w, escaped, end);
                        continue;
                    }
                    // Operators and punctuation
                    if ("+-*/%=<>!&|^~?:".IndexOf(c) >= 0) Spans.Add(new CodeSpan(i, 1, CodeKind.Operator));
                    _prevSym = c;
                    if (c != '.') { _prevWasType = false; if (c != '<' && c != ',' && c != '>') _prevWord = ""; }
                    i++;
                }
            }

            void Identifier(int s, int i, string w, bool escaped, int end)
            {
                string prevWord = _prevWord; char prevSym = _prevSym; bool prevWasType = _prevWasType;
                _prevSym = 'a'; _prevWord = w; _prevWasType = false;
                if (!escaped)
                {
                    if (ControlKeywords.Contains(w)) { Spans.Add(new CodeSpan(s, i - s, CodeKind.Control)); return; }
                    if (Keywords.Contains(w))
                    {
                        bool keyword = true;
                        if (ContextualOnlyBeforeBrace.Contains(w))
                        {
                            // get/set/value... are only keywords in their special places, otherwise they're names
                            int j = SkipSpace(i, end);
                            char after = j < end ? T[j] : '\0';
                            if (w == "value") keyword = false;
                            else if (w == "get" || w == "set" || w == "init" || w == "add" || w == "remove") keyword = after == ';' || after == '{' || (after == '=' && j + 1 < end && T[j + 1] == '>');
                            else if (w == "partial") keyword = true;
                            else if (w == "async") keyword = after != '(' || prevSym == '=';
                            else if (w == "where") keyword = prevSym == '>' || prevSym == ')';
                            else if (w == "when") keyword = prevSym == ')';
                            else keyword = false;
                        }
                        if (keyword) { Spans.Add(new CodeSpan(s, i - s, CodeKind.Keyword)); if (w == "using" || w == "namespace") _prevWasType = false; return; }
                    }
                }
                int k = SkipSpace(i, end);
                char nc = k < end ? T[k] : '\0';
                // Generic: Foo<...>( is a generic method unless Foo is a type; Foo<...> otherwise is a type
                int afterGeneric = nc == '<' ? SkipGeneric(k, end) : -1;
                char ncAfter = afterGeneric > 0 ? Peek(afterGeneric, end) : nc;
                var tk = TypeKind(w);
                bool memberAccess = prevSym == '.';
                bool afterNew = prevWord == "new" && prevSym == 'a';
                bool declaration = prevSym == 'a' && (prevWord == "class" || prevWord == "struct" || prevWord == "interface" || prevWord == "enum" || prevWord == "record");

                CodeKind? kind = null;
                bool attribute = char.IsUpper(w[0]) && IsAttributeContext(s);
                if (attribute) kind = tk ?? TypeKind(w + "Attribute") ?? CodeKind.Class;
                else if (declaration) kind = tk ?? CodeKind.Class;
                else if (afterNew) kind = tk ?? (char.IsUpper(w[0]) ? CodeKind.Class : (CodeKind?)null);
                else if (ncAfter == '(') kind = CodeKind.Method;
                else if (tk != null && (!memberAccess || prevWasType)) kind = tk;
                else if (afterGeneric > 0 && char.IsUpper(w[0])) kind = CodeKind.Class;
                else if (!memberAccess && char.IsUpper(w[0]) && LooksLikeDeclaredType(k, end)) kind = CodeKind.Class;

                if (kind != null) Spans.Add(new CodeSpan(s, i - s, kind.Value));
                _prevWasType = kind != null && kind != CodeKind.Method || Namespaces.Contains(w);
            }

            char Peek(int i, int end) { i = SkipSpace(i, end); return i < end ? T[i] : '\0'; }
            char PrevNonSpace(int i) { for (int j = i - 1; j >= 0; j--) if (!char.IsWhiteSpace(T[j])) return T[j]; return '\0'; }

            /// <summary>An identifier directly inside [...] at the start of a line, e.g. [ScriptCommand("x")].</summary>
            bool IsAttributeContext(int s)
            {
                int j = s - 1;
                while (j >= 0 && (char.IsWhiteSpace(T[j]) && T[j] != '\n')) j--;
                if (j < 0) return false;
                if (T[j] == ',') { int k = T.LastIndexOf('[', j); return k >= 0 && T.IndexOf(']', k) > j && AtLineStart(k); }
                if (T[j] == ':') { j--; while (j >= 0 && IdPart(T[j])) j--; while (j >= 0 && T[j] == ' ') j--; }
                return j >= 0 && T[j] == '[' && AtLineStart(j);
            }

            /// <summary>"Foo bar =", "Foo bar;", "Foo bar)" and so on: Foo is a type in a declaration.</summary>
            bool LooksLikeDeclaredType(int k, int end)
            {
                int j = k;
                if (j < end && T[j] == '[') { while (j < end && T[j] != ']') j++; j = SkipSpace(j + 1, end); }
                if (j < end && T[j] == '?') j = SkipSpace(j + 1, end);
                if (j >= end || !IdStart(T[j])) return false;
                int s = j; while (j < end && IdPart(T[j])) j++;
                string w = T.Substring(s, j - s);
                if (Keywords.Contains(w) && w != "value" || ControlKeywords.Contains(w)) return false;
                int after = SkipSpace(j, end);
                char c = after < end ? T[after] : '\0';
                // foreach (Foo x in ...)
                if (c == 'i' && after + 2 <= end && T.Substring(after, 2) == "in" && (after + 2 == end || !IdPart(T[after + 2]))) return true;
                return c == '=' || c == ';' || c == ',' || c == ')' || c == '{';
            }

            void DocComment(int s, int e)
            {
                // /// text in comment green, <tags> and the slashes in the doc-tag colour
                Spans.Add(new CodeSpan(s, 3, CodeKind.DocTag));
                int i = s + 3;
                while (i < e)
                {
                    int lt = T.IndexOf('<', i, e - i);
                    if (lt < 0) { Spans.Add(new CodeSpan(i, e - i, CodeKind.Comment)); break; }
                    if (lt > i) Spans.Add(new CodeSpan(i, lt - i, CodeKind.Comment));
                    int gt = T.IndexOf('>', lt, e - lt);
                    if (gt < 0) { Spans.Add(new CodeSpan(lt, e - lt, CodeKind.Comment)); break; }
                    Spans.Add(new CodeSpan(lt, gt + 1 - lt, CodeKind.DocTag));
                    i = gt + 1;
                }
            }

            /// <summary>Adds a span, splitting it around escape spans already added inside it.</summary>
            void AddUnder(int s, int e, CodeKind k)
            {
                // Escapes were added first (they're inside the string); add the string pieces between them
                int insertAt = Spans.Count;
                while (insertAt > 0 && Spans[insertAt - 1].Start >= s) insertAt--;
                var inner = Spans.GetRange(insertAt, Spans.Count - insertAt);
                Spans.RemoveRange(insertAt, Spans.Count - insertAt);
                int p = s;
                foreach (var sp in inner)
                {
                    if (sp.Start > p) Spans.Add(new CodeSpan(p, sp.Start - p, k));
                    Spans.Add(sp);
                    p = Math.Max(p, sp.Start + sp.Length);
                }
                if (e > p) Spans.Add(new CodeSpan(p, e - p, k));
            }

            int String(int s, int q, int end, bool verbatim, bool interp, bool raw)
            {
                int i = q + 1;
                int pieceStart = s;
                int quotes = 1;
                if (raw) { quotes = 0; while (q + quotes < end && T[q + quotes] == '"') quotes++; i = q + quotes; }
                while (i < end)
                {
                    char c = T[i];
                    if (raw)
                    {
                        if (c == '"') { int n = 0; while (i + n < end && T[i + n] == '"') n++; if (n >= quotes) { i += quotes; break; } i += n; continue; }
                    }
                    else if (c == '"')
                    {
                        if (verbatim && i + 1 < end && T[i + 1] == '"') { Spans.Add(new CodeSpan(pieceStart, i - pieceStart, CodeKind.String)); Spans.Add(new CodeSpan(i, 2, CodeKind.Escape)); i += 2; pieceStart = i; continue; }
                        i++; break;
                    }
                    else if (c == '\n' && !verbatim) break;
                    else if (c == '\\' && !verbatim)
                    {
                        Spans.Add(new CodeSpan(pieceStart, i - pieceStart, CodeKind.String));
                        int len = 2;
                        if (i + 1 < end) { char e = T[i + 1]; if (e == 'u') len = 6; else if (e == 'U') len = 10; else if (e == 'x') { len = 2; while (len < 6 && i + len < end && Uri.IsHexDigit(T[i + len])) len++; } }
                        len = Math.Min(len, end - i);
                        Spans.Add(new CodeSpan(i, len, CodeKind.Escape)); i += len; pieceStart = i; continue;
                    }
                    if (interp && c == '{')
                    {
                        if (i + 1 < end && T[i + 1] == '{') { Spans.Add(new CodeSpan(pieceStart, i - pieceStart, CodeKind.String)); Spans.Add(new CodeSpan(i, 2, CodeKind.Escape)); i += 2; pieceStart = i; continue; }
                        Spans.Add(new CodeSpan(pieceStart, i - pieceStart, CodeKind.String));
                        // The code inside { } is coloured like normal code; find the matching brace
                        int close = MatchBrace(i, end);
                        var saveWord = _prevWord; var saveSym = _prevSym; var saveType = _prevWasType;
                        _prevSym = '{'; _prevWord = "";
                        Run(i + 1, close, true);
                        _prevWord = saveWord; _prevSym = saveSym; _prevWasType = saveType;
                        i = Math.Min(end, close + 1); pieceStart = i; continue;
                    }
                    if (interp && c == '}' && i + 1 < end && T[i + 1] == '}') { Spans.Add(new CodeSpan(pieceStart, i - pieceStart, CodeKind.String)); Spans.Add(new CodeSpan(i, 2, CodeKind.Escape)); i += 2; pieceStart = i; continue; }
                    i++;
                }
                if (i > pieceStart) Spans.Add(new CodeSpan(pieceStart, i - pieceStart, CodeKind.String));
                return i;
            }

            int MatchBrace(int open, int end)
            {
                int depth = 0;
                for (int i = open; i < end; i++)
                {
                    char c = T[i];
                    if (c == '"') { i++; while (i < end && T[i] != '"' && T[i] != '\n') { if (T[i] == '\\') i++; i++; } continue; }
                    if (c == '{') depth++;
                    else if (c == '}') { if (--depth == 0) return i; }
                    else if (c == '\n') return i;
                }
                return end;
            }
        }
    }
}
