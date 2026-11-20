using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PokeCreator.Data;
using PokeCreator.Maps;

namespace PokeCreator.Projects
{
    /// <summary>A translation: original text → translated text (empty = not translated yet).</summary>
    public class LanguageFile
    {
        public string Language { get; set; }
        public Dictionary<string, string> Texts { get; set; }
        public LanguageFile() { Texts = new Dictionary<string, string>(); }
    }

    /// <summary>
    /// Translations (Essentials' message tables): Languages/&lt;name&gt;.json in the project holds every message,
    /// choice and database name with its translation. The Studio exports and refreshes the files; the game shows the
    /// player's chosen language (Options → Language) by swapping texts as they're shown and names in the database.
    /// </summary>
    public static class Localization
    {
        public static string Folder(Project p) { return Path.Combine(p.Root, "Languages"); }

        public static List<string> Available(Project p)
        {
            var dir = Folder(p);
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
        }

        public static LanguageFile Load(Project p, string name)
        {
            try
            {
                var f = Path.Combine(Folder(p), name + ".json");
                if (File.Exists(f)) return Json.Json.FromFile<LanguageFile>(f) ?? new LanguageFile { Language = name };
            }
            catch { }
            return new LanguageFile { Language = name };
        }

        public static void Save(Project p, string name, LanguageFile lf)
        {
            Directory.CreateDirectory(Folder(p));
            Json.Json.ToFile(Path.Combine(Folder(p), name + ".json"), lf);
        }

        /// <summary>A message as the game shows it: Show Text plus its continuation lines, joined with spaces.</summary>
        public static string JoinMessage(List<EventCommand> list, int index)
        {
            string text = list[index].StrParam(0);
            for (int i = index + 1; i < list.Count && list[i].Code == 401; i++)
            {
                string t = list[i].StrParam(0);
                if (t.Length > 0 && text.Length > 0 && !text.EndsWith(" ")) text += " ";
                text += t;
            }
            return text;
        }

        static readonly Regex ScriptString = new Regex(@"(?:pbMessage|pbConfirmMessage|_INTL)\s*\(\s*""((?:[^""\\]|\\.)*)""");

        static void FromCommands(List<EventCommand> list, List<string> into)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c.Code == 101) into.Add(JoinMessage(list, i));
                else if (c.Code == 102)
                {
                    var raw = c.Params.Count > 0 ? c.Params[0] as List<object> : null;
                    if (raw != null) foreach (var o in raw) into.Add(Convert.ToString(o));
                }
                else if (c.Code == 355 || c.Code == 655)
                    foreach (Match m in ScriptString.Matches(c.StrParam(0))) into.Add(Regex.Unescape(m.Groups[1].Value));
            }
        }

        /// <summary>Every text a translator needs: event messages and choices, then database names and descriptions.</summary>
        public static List<string> CollectTexts(Project p)
        {
            var all = new List<string>();
            foreach (var info in p.MapInfos.OrderBy(m => m.Id))
            {
                all.Add(info.Name);
                var map = p.LoadMap(info.Id);
                if (map == null) continue;
                if (map.Properties != null) all.Add(map.Properties.Name);
                foreach (var ev in map.Events) foreach (var page in ev.Pages) FromCommands(page.List, all);
            }
            foreach (var ce in p.CommonEvents) FromCommands(ce.List, all);
            foreach (var s in p.Species) { all.Add(s.Name); all.Add(s.FormName); all.Add(s.Category); all.Add(s.Pokedex); }
            foreach (var m in p.Moves) { all.Add(m.Name); all.Add(m.Description); }
            foreach (var it in p.Items) { all.Add(it.Name); all.Add(it.NamePlural); all.Add(it.Description); }
            foreach (var a in p.Abilities) { all.Add(a.Name); all.Add(a.Description); }
            foreach (var t in p.Types) all.Add(t.Name);
            foreach (var tt in p.TrainerTypes) all.Add(tt.Name);
            foreach (var tr in p.Trainers) { all.Add(tr.LoseText); }
            var seen = new HashSet<string>();
            return all.Where(s => !string.IsNullOrWhiteSpace(s) && seen.Add(s)).ToList();
        }

        /// <summary>Creates or refreshes Languages/name.json: new texts are added (untranslated), existing translations kept. Returns how many still need translating.</summary>
        public static int Export(Project p, string name)
        {
            var lf = Load(p, name);
            if (string.IsNullOrEmpty(lf.Language)) lf.Language = name;
            var texts = CollectTexts(p);
            var fresh = new Dictionary<string, string>();
            foreach (var t in texts) { string tr; fresh[t] = lf.Texts.TryGetValue(t, out tr) ? tr : ""; }
            // Keep translations of texts that were removed, at the end, in case they come back
            foreach (var kv in lf.Texts) if (!fresh.ContainsKey(kv.Key) && !string.IsNullOrEmpty(kv.Value)) fresh[kv.Key] = kv.Value;
            lf.Texts = fresh;
            Save(p, name, lf);
            return fresh.Count(kv => string.IsNullOrEmpty(kv.Value) && texts.Contains(kv.Key));
        }

        // ------------------------------------------------------------------ in the game
        static Dictionary<string, string> _current;
        static readonly List<Action> _restore = new List<Action>();
        public static string CurrentName { get; private set; }

        /// <summary>The translation of a text in the current language (or the text itself).</summary>
        public static string T(string s)
        {
            string t;
            if (_current == null || string.IsNullOrEmpty(s) || !_current.TryGetValue(s, out t) || string.IsNullOrEmpty(t)) return s;
            return t;
        }

        static void Swap<T>(T obj, Func<T, string> get, Action<T, string> set)
        {
            string orig = get(obj);
            if (string.IsNullOrEmpty(orig)) return;
            string tr = T(orig);
            if (tr == orig) return;
            set(obj, tr);
            _restore.Add(() => set(obj, orig));
        }

        /// <summary>Switches the game to a language (null or "" = the original texts).</summary>
        public static void Apply(Project p, string name)
        {
            foreach (var r in _restore) r();
            _restore.Clear();
            _current = null; CurrentName = null;
            if (string.IsNullOrEmpty(name)) return;
            var lf = Load(p, name);
            if (lf.Texts.Count == 0) return;
            _current = lf.Texts; CurrentName = name;
            foreach (var s in p.Species) { Swap(s, x => x.Name, (x, v) => x.Name = v); Swap(s, x => x.FormName, (x, v) => x.FormName = v); Swap(s, x => x.Category, (x, v) => x.Category = v); Swap(s, x => x.Pokedex, (x, v) => x.Pokedex = v); }
            foreach (var m in p.Moves) { Swap(m, x => x.Name, (x, v) => x.Name = v); Swap(m, x => x.Description, (x, v) => x.Description = v); }
            foreach (var it in p.Items) { Swap(it, x => x.Name, (x, v) => x.Name = v); Swap(it, x => x.NamePlural, (x, v) => x.NamePlural = v); Swap(it, x => x.Description, (x, v) => x.Description = v); }
            foreach (var a in p.Abilities) { Swap(a, x => x.Name, (x, v) => x.Name = v); Swap(a, x => x.Description, (x, v) => x.Description = v); }
            foreach (var t in p.Types) Swap(t, x => x.Name, (x, v) => x.Name = v);
            foreach (var tt in p.TrainerTypes) Swap(tt, x => x.Name, (x, v) => x.Name = v);
            foreach (var tr in p.Trainers) Swap(tr, x => x.LoseText, (x, v) => x.LoseText = v);
            foreach (var mi in p.MapInfos) Swap(mi, x => x.Name, (x, v) => x.Name = v);
        }
    }
}
