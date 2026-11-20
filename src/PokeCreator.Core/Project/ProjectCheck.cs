using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using PokeCreator.Data;
using PokeCreator.Json;
using PokeCreator.Maps;

namespace PokeCreator.Projects
{
    public enum ProblemSeverity { Error, Warning, Info }

    /// <summary>One thing wrong with a project, and where to go to fix it.</summary>
    public class Problem
    {
        public ProblemSeverity Severity;
        public string Where;     // "Map 003: Route 1, event 004 (Youngster)"
        public string Message;   // "Character graphic 'trainer_YOUNGSTER' is missing from Graphics/Characters"
        /// <summary>
        /// Where to jump to: "map:N", "map:N:event:E", "commonevent:N", "db:table:ID", "db:table",
        /// "settings", "players", "connections", "file:path:line".
        /// </summary>
        public string Target;
        public override string ToString() { return Severity + "  " + Where + "  " + Message; }
    }

    /// <summary>
    /// Looks through a project for things that would break or look wrong in the game: missing graphics and audio,
    /// warps to maps that don't exist, references to unknown Pokémon, items or trainers, and so on.
    /// </summary>
    public class ProjectCheck
    {
        readonly Project _p;
        readonly List<Problem> _out = new List<Problem>();
        readonly Dictionary<string, HashSet<string>> _ids = new Dictionary<string, HashSet<string>>();
        readonly Dictionary<string, bool> _assetCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        ProjectCheck(Project p)
        {
            _p = p;
            foreach (var t in p.Tables)
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in t.Items.OfType<GameRecord>()) if (!string.IsNullOrEmpty(r.Id)) set.Add(r.Id);
                _ids[t.Key] = set;
            }
        }

        public static List<Problem> Run(Project p)
        {
            var c = new ProjectCheck(p);
            c.CheckSettings();
            c.CheckDatabase();
            c.CheckMaps();
            c.CheckCommonEvents();
            return c._out.OrderBy(x => x.Severity).ToList();
        }

        void Add(ProblemSeverity s, string where, string target, string msg) { _out.Add(new Problem { Severity = s, Where = where, Target = target, Message = msg }); }

        bool HasAsset(string folder, string name)
        {
            string key = folder + "/" + name;
            bool ok;
            if (!_assetCache.TryGetValue(key, out ok)) _assetCache[key] = ok = _p.FindAsset(folder, name) != null;
            return ok;
        }

        bool Known(string table, string id) { HashSet<string> s; return string.IsNullOrEmpty(id) || !_ids.TryGetValue(table, out s) || s.Contains(id); }

        static string TableLabel(string key)
        {
            switch (key)
            {
                case "species": return "Pokémon"; case "items": return "item"; case "moves": return "move"; case "abilities": return "ability";
                case "types": return "type"; case "trainer_types": return "trainer type"; default: return key;
            }
        }

        string MapName(int id) { var i = _p.GetMapInfo(id); return "Map " + id.ToString("000") + (i != null ? ": " + i.Name : ""); }

        // ------------------------------------------------------------------ settings and players
        void CheckSettings()
        {
            var s = _p.Settings;
            var start = _p.LoadMap(s.StartMap);
            if (_p.GetMapInfo(s.StartMap) == null || start == null) Add(ProblemSeverity.Error, "Game Settings", "settings", "The start map (" + s.StartMap + ") doesn't exist, so a new game can't start.");
            else if (s.StartX < 0 || s.StartY < 0 || s.StartX >= start.Width || s.StartY >= start.Height) Add(ProblemSeverity.Error, "Game Settings", "settings", "The start position (" + s.StartX + ", " + s.StartY + ") is outside " + MapName(s.StartMap) + " (" + start.Width + "×" + start.Height + ").");
            CheckFields(s, "Game Settings", "settings");
            string font = _p.MessageFontFile();
            if (font == null) Add(ProblemSeverity.Error, "Game Settings", "settings", "There is no font, so message text won't show. Put a .ttf in the project's Fonts folder, or the FireRed font (power green.ttf from Pokémon Essentials) in PokeCreator's BaseData/Fonts folder.");
            else if (s.UseProjectFont && _p.InProjectFonts(s.Font) == null)
                Add(ProblemSeverity.Warning, "Game Settings", "settings", "'Use my own font' is on, but this project's Fonts folder has no font" + (string.IsNullOrEmpty(s.Font) ? "" : " called '" + s.Font + "'") + ", so the game uses " + Path.GetFileName(font) + ".");
            else if (!s.UseProjectFont && _p.ProjectFonts.Length > 0)
                Add(ProblemSeverity.Info, "Game Settings", "settings", "This project's Fonts folder has " + _p.ProjectFonts.Length + " font(s), but 'Use my own font' is off, so the game uses the default " + Path.GetFileName(font) + ".");
            if (_p.Players.Count == 0) Add(ProblemSeverity.Error, "Player Characters", "players", "There are no player characters.");
            for (int i = 0; i < _p.Players.Count; i++) CheckFields(_p.Players[i], "Player character " + (i + 1), "players");
        }

        /// <summary>Checks every property marked [Field(Ref=...)] or [Field(Asset=...)] on an object.</summary>
        void CheckFields(object o, string where, string target)
        {
            foreach (var prop in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var f = (FieldAttribute)Attribute.GetCustomAttribute(prop, typeof(FieldAttribute));
                if (f == null || (f.Ref == null && f.Asset == null)) continue;
                if (o is GameSettings && prop.Name == "Font") continue; // checked in CheckSettings, with the fallbacks
                object v = prop.GetValue(o, null);
                var values = v is string ? new[] { (string)v } : v is IEnumerable ? ((IEnumerable)v).OfType<string>().ToArray() : new string[0];
                foreach (var val in values.Where(x => !string.IsNullOrEmpty(x)))
                {
                    if (f.Ref != null && !Known(f.Ref, val)) Add(ProblemSeverity.Error, where, target, f.Label + ": there is no " + TableLabel(f.Ref) + " called " + val + ".");
                    if (f.Asset != null && !HasAsset(f.Asset, val)) Add(ProblemSeverity.Warning, where, target, f.Label + ": '" + val + "' is missing from " + f.Asset + ".");
                }
            }
        }

        // ------------------------------------------------------------------ database
        void CheckDatabase()
        {
            foreach (var t in _p.Tables)
            {
                var seen = new HashSet<string>();
                foreach (var r in t.Items.OfType<GameRecord>())
                {
                    string where = t.Label + ": " + r.Id, target = "db:" + t.Key + ":" + r.Id;
                    if (string.IsNullOrEmpty(r.Id)) { Add(ProblemSeverity.Error, t.Label, "db:" + t.Key, "A record has no ID."); continue; }
                    // Pokémon forms share an ID with their base form
                    var sp = r as SpeciesData;
                    string key = sp != null ? r.Id + "#" + sp.Form : r.Id;
                    if (!seen.Add(key)) Add(ProblemSeverity.Error, where, target, "Two records have the ID " + r.Id + "; events and scripts can only reach one of them.");
                    CheckFields(r, where, target);
                }
            }
            foreach (var tr in _p.Trainers)
            {
                string where = "Trainers: " + tr.TrainerType + " " + tr.Name, target = "db:trainers:" + tr.Id;
                if (tr.Pokemon == null || tr.Pokemon.Count == 0) Add(ProblemSeverity.Warning, where, target, "This trainer has no Pokémon.");
                else foreach (var pk in tr.Pokemon)
                    {
                        if (!Known("species", pk.Species)) Add(ProblemSeverity.Error, where, target, "Party: there is no Pokémon called " + pk.Species + ".");
                        if (!Known("items", pk.Item)) Add(ProblemSeverity.Error, where, target, pk.Species + " holds an unknown item " + pk.Item + ".");
                        if (!Known("abilities", pk.Ability)) Add(ProblemSeverity.Error, where, target, pk.Species + " has an unknown ability " + pk.Ability + ".");
                        if (pk.Moves != null) foreach (var m in pk.Moves) if (!Known("moves", m)) Add(ProblemSeverity.Error, where, target, pk.Species + " knows an unknown move " + m + ".");
                        if (pk.Level < 1 || pk.Level > 100) Add(ProblemSeverity.Warning, where, target, pk.Species + " is level " + pk.Level + " (should be 1-100).");
                    }
            }
            foreach (var en in _p.Encounters)
            {
                string where = "Wild Encounters: " + MapName(en.Map), target = "db:encounters:" + en.Id;
                if (_p.GetMapInfo(en.Map) == null) Add(ProblemSeverity.Warning, "Wild Encounters: " + en.Id, target, "Map " + en.Map + " doesn't exist.");
                if (en.Methods != null) foreach (var m in en.Methods) if (m.Slots != null) foreach (var s in m.Slots)
                                if (!Known("species", s.Species)) Add(ProblemSeverity.Error, where, target, m.Type + ": there is no Pokémon called " + s.Species + ".");
            }
            // Missing images are listed in each editor's "missing details" filter; just count them here
            int missingTrainers = _p.TrainerTypes.Count(t => !HasAsset("Graphics/Trainers", t.Id));
            if (missingTrainers > 0) Add(ProblemSeverity.Info, "Trainer Types", "db:trainer_types", missingTrainers + " trainer type(s) have no trainer picture. Use the Show filter to list them.");
            int missingFront = _p.Species.Count(s => { string sp = string.IsNullOrEmpty(s.Species) ? s.Id : s.Species; return !HasAsset("Graphics/Pokemon/Front", s.Form > 0 ? sp + "_" + s.Form : sp); });
            if (missingFront > 0) Add(ProblemSeverity.Info, "Pokémon", "db:species", missingFront + " Pokémon have no front sprite. Use the Show filter to list them.");
            int missingItems = _p.Items.Count(i => !HasAsset("Graphics/Items", i.Id));
            if (missingItems > 0) Add(ProblemSeverity.Info, "Items", "db:items", missingItems + " item(s) have no icon. Use the Show filter to list them.");
        }

        // ------------------------------------------------------------------ maps
        void CheckMaps()
        {
            foreach (var info in _p.MapInfos.OrderBy(m => m.Id))
            {
                string where = MapName(info.Id), target = "map:" + info.Id;
                var map = _p.LoadMap(info.Id);
                if (map == null) { Add(ProblemSeverity.Error, where, target, "The map file Data/Maps/Map" + info.Id.ToString("000") + ".json is missing."); continue; }
                if (info.ParentId != 0 && _p.GetMapInfo(info.ParentId) == null) Add(ProblemSeverity.Warning, where, target, "Its parent map (" + info.ParentId + ") doesn't exist; it shows at the top level.");
                var ts = _p.GetTileset(map.TilesetId);
                if (ts == null) Add(ProblemSeverity.Error, where, target, "Its tileset (" + map.TilesetId + ") doesn't exist.");
                else
                {
                    foreach (var sheet in ts.Sheets) if (!string.IsNullOrEmpty(sheet.Name) && !HasAsset("Graphics/Tilesets", sheet.Name)) Add(ProblemSeverity.Warning, where, target, "Tile sheet '" + sheet.Name + "' (tileset " + ts.Name + ") is missing from Graphics/Tilesets.");
                    if (ts.AutotileNames != null) foreach (var a in ts.AutotileNames) if (!string.IsNullOrEmpty(a) && !HasAsset("Graphics/Autotiles", a)) Add(ProblemSeverity.Warning, where, target, "Autotile '" + a + "' (tileset " + ts.Name + ") is missing from Graphics/Autotiles.");
                }
                if (map.Bgm != null && !string.IsNullOrEmpty(map.Bgm.Name) && !HasAsset("Audio/BGM", map.Bgm.Name)) Add(ProblemSeverity.Warning, where, target, "Its music '" + map.Bgm.Name + "' is missing from Audio/BGM.");
                if (map.Bgs != null && !string.IsNullOrEmpty(map.Bgs.Name) && !HasAsset("Audio/BGS", map.Bgs.Name)) Add(ProblemSeverity.Warning, where, target, "Its background sound '" + map.Bgs.Name + "' is missing from Audio/BGS.");
                var spots = new Dictionary<long, MapEvent>();
                foreach (var ev in map.Events)
                {
                    string ew = where + ", event " + ev.Id.ToString("000") + (string.IsNullOrEmpty(ev.Name) ? "" : " (" + ev.Name + ")");
                    string et = target + ":event:" + ev.Id;
                    if (ev.X < 0 || ev.Y < 0 || ev.X >= map.Width || ev.Y >= map.Height) Add(ProblemSeverity.Warning, ew, et, "It sits outside the map (" + ev.X + ", " + ev.Y + "), so the player can never reach it.");
                    MapEvent other;
                    if (spots.TryGetValue(ev.X * 100000L + ev.Y, out other)) Add(ProblemSeverity.Info, ew, et, "It's on the same tile as event " + other.Id.ToString("000") + ".");
                    else spots[ev.X * 100000L + ev.Y] = ev;
                    for (int pi = 0; pi < ev.Pages.Count; pi++)
                    {
                        var pg = ev.Pages[pi];
                        string pw = ev.Pages.Count > 1 ? ew + ", page " + (pi + 1) : ew;
                        if (pg.Graphic != null && !string.IsNullOrEmpty(pg.Graphic.CharacterName) && !HasAsset("Graphics/Characters", pg.Graphic.CharacterName))
                            Add(ProblemSeverity.Warning, pw, et, "Its graphic '" + pg.Graphic.CharacterName + "' is missing from Graphics/Characters.");
                        CheckCommands(pg.List, pw, et, map);
                    }
                }
            }
            foreach (var c in _p.Connections)
            {
                if (_p.GetMapInfo(c.Map1) == null || _p.GetMapInfo(c.Map2) == null)
                    Add(ProblemSeverity.Error, "Map connections", "connections", "The connection " + c + " joins a map that doesn't exist.");
            }
        }

        void CheckCommonEvents()
        {
            var seen = new HashSet<int>();
            foreach (var ce in _p.CommonEvents)
            {
                string where = "Common event " + ce, target = "commonevent:" + ce.Id;
                if (!seen.Add(ce.Id)) Add(ProblemSeverity.Error, where, target, "Two common events have the ID " + ce.Id + ".");
                if (ce.Trigger != 0 && ce.List.Count(c => c.Code != 0) == 0) Add(ProblemSeverity.Info, where, target, "It runs automatically but has no commands.");
                CheckCommands(ce.List, where, target, null);
            }
        }

        // pbAddPokemon(:PIKACHU, 5), pbReceiveItem(:POTION), pbTrainerBattle(:YOUNGSTER, "Ben")...
        static readonly Regex SpeciesCall = new Regex(@"\b(pbAddPokemon|pbAddPokemonSilent|pbAddToParty|pbAddToPartySilent|pbWildBattle|WildBattle\.start)\s*\(\s*:([A-Za-z0-9_]+)", RegexOptions.Compiled);
        static readonly Regex ItemCall = new Regex(@"(pbReceiveItem|pbItemBall|pbGiveItem|pbStoreItem|\$bag\.add|\$bag\.has\?|\$PokemonBag\.pbStoreItem)\s*\(\s*:([A-Za-z0-9_]+)", RegexOptions.Compiled);
        // TrainerBattle.start(:TYPE, "Name", version) / pbTrainerBattle(:TYPE, "Name", endSpeech, double, version)
        static readonly Regex TrainerCall = new Regex(@"\b(pbTrainerBattle|pbDoubleTrainerBattle|TrainerBattle\.start)\s*\(\s*:([A-Za-z0-9_]+)\s*,\s*""([^""]*)""(?:\s*,\s*(\d+)\s*[,)])?", RegexOptions.Compiled);

        void CheckCommands(List<EventCommand> list, string where, string target, MapData map)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                switch (c.Code)
                {
                    case 201:
                        if (c.IntParam(0) == 0)
                        {
                            int id = c.IntParam(1), x = c.IntParam(2), y = c.IntParam(3);
                            var dest = _p.GetMapInfo(id) != null ? _p.LoadMap(id) : null;
                            if (dest == null) Add(ProblemSeverity.Error, where, target, "Transfer Player goes to map " + id + ", which doesn't exist.");
                            else if (x < 0 || y < 0 || x >= dest.Width || y >= dest.Height) Add(ProblemSeverity.Error, where, target, "Transfer Player goes to (" + x + ", " + y + "), outside " + MapName(id) + " (" + dest.Width + "×" + dest.Height + ").");
                        }
                        break;
                    case 117:
                        if (_p.GetCommonEvent(c.IntParam(0)) == null) Add(ProblemSeverity.Error, where, target, "Call Common Event calls " + c.IntParam(0) + ", which doesn't exist.");
                        break;
                    case 119:
                        {
                            string label = c.StrParam(0);
                            if (!list.Any(x => x.Code == 118 && x.StrParam(0) == label)) Add(ProblemSeverity.Error, where, target, "Jump to Label '" + label + "' has no matching label.");
                            break;
                        }
                    case 241: CheckAudio(c, "Audio/BGM", "Play BGM", where, target); break;
                    case 245: CheckAudio(c, "Audio/BGS", "Play BGS", where, target); break;
                    case 249: CheckAudio(c, "Audio/ME", "Play ME", where, target); break;
                    case 250: CheckAudio(c, "Audio/SE", "Play SE", where, target); break;
                    case 209:
                        {
                            int who = c.IntParam(0);
                            if (who > 0 && map != null && !map.Events.Any(e => e.Id == who)) Add(ProblemSeverity.Error, where, target, "Set Move Route moves event " + who + ", which isn't on this map.");
                            break;
                        }
                    case 202:
                        {
                            int who = c.IntParam(0);
                            if (who > 0 && map != null && !map.Events.Any(e => e.Id == who)) Add(ProblemSeverity.Error, where, target, "Set Event Location moves event " + who + ", which isn't on this map.");
                            break;
                        }
                    case 355:
                        {
                            // A script can span the 355 line and the 655 lines after it
                            string script = c.StrParam(0);
                            for (int k = i + 1; k < list.Count && list[k].Code == 655; k++) script += "\n" + list[k].StrParam(0);
                            CheckScript(script, where, target);
                            break;
                        }
                    case 111:
                        if (c.IntParam(0) == 12) CheckScript(c.StrParam(1), where, target);
                        break;
                }
            }
        }

        void CheckAudio(EventCommand c, string folder, string what, string where, string target)
        {
            var a = c.Params.Count > 0 ? c.Params[0] as JsonObject : null;
            string name = a == null ? null : a.GetString("Name");
            if (!string.IsNullOrEmpty(name) && !HasAsset(folder, name)) Add(ProblemSeverity.Warning, where, target, what + ": '" + name + "' is missing from " + folder + ".");
        }

        void CheckScript(string script, string where, string target)
        {
            if (string.IsNullOrEmpty(script)) return;
            foreach (Match m in SpeciesCall.Matches(script))
                if (!Known("species", m.Groups[2].Value)) Add(ProblemSeverity.Error, where, target, m.Groups[1].Value + " uses :" + m.Groups[2].Value + ", but there is no such Pokémon.");
            foreach (Match m in ItemCall.Matches(script))
                if (!Known("items", m.Groups[2].Value)) Add(ProblemSeverity.Error, where, target, m.Groups[1].Value + " uses :" + m.Groups[2].Value + ", but there is no such item.");
            foreach (Match m in TrainerCall.Matches(script))
            {
                string type = m.Groups[2].Value, name = m.Groups[3].Value;
                // Only TrainerBattle.start has the version right after the name; for the old call, any version counts
                int version = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : m.Groups[1].Value == "TrainerBattle.start" ? 0 : -1;
                if (!Known("trainer_types", type)) Add(ProblemSeverity.Error, where, target, "Trainer battle uses trainer type :" + type + ", which doesn't exist.");
                else if (!_p.Trainers.Any(t => t.TrainerType == type && t.Name == name && (version < 0 || t.Version == version)))
                    Add(ProblemSeverity.Error, where, target, "Trainer battle against " + type + " \"" + name + "\"" + (version > 0 ? " (version " + version + ")" : "") + ", but there is no such trainer in the database.");
            }
        }
    }
}
