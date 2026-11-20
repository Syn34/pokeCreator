using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PokeCreator.Json;
using PokeCreator.Pkmn;
using PokeCreator.Projects;

namespace PokeCreator.Runtime.World
{
    /// <summary>Everything needed to resume a game. Stored as readable JSON.</summary>
    public class SaveData
    {
        public int Version { get; set; }
        public string GameTitle { get; set; }
        public string SavedAt { get; set; }
        public string PlayerName { get; set; }
        public int PlayerCharacter { get; set; }
        public int TrainerId { get; set; }
        public long PlayFrames { get; set; }
        public int Money { get; set; }
        public int Steps { get; set; }
        public int MapId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Direction { get; set; }
        public string Location { get; set; }
        public string Bgm { get; set; }
        public List<int> SwitchesOn { get; set; }
        public Dictionary<string, int> Variables { get; set; }
        public Dictionary<string, string> StringVariables { get; set; }
        public List<string> SelfSwitches { get; set; }
        public Dictionary<string, int> Bag { get; set; }
        public List<Pokemon> Party { get; set; }
        public List<Pokemon> Storage { get; set; }
        public List<string> Flags { get; set; }
        public List<string> Seen { get; set; }
        public List<string> Caught { get; set; }
        public int RepelSteps { get; set; }
        public List<int> HealPoint { get; set; }
        public Dictionary<string, int> PcItems { get; set; }
        public bool Surfing { get; set; }
        public bool Cycling { get; set; }
        public bool Diving { get; set; }
        public List<int> Weather { get; set; }
        public List<int> Visited { get; set; }
        public List<int> EscapePoint { get; set; }
        public List<DayCareSlot> DayCare { get; set; }
        public List<HallOfFameEntry> HallOfFame { get; set; }
        public GameExtras Extras { get; set; }
        public int Coins { get; set; }
        public int BattlePoints { get; set; }
        public List<int> Badges { get; set; }
        public Dictionary<string, long> EventTimes { get; set; }
        public Dictionary<string, BerryPlantState> BerryPlants { get; set; }
        public List<PhoneContact> Phone { get; set; }
        public int DayCareEggSteps { get; set; }
        public bool DayCareEgg { get; set; }
        public SaveData()
        {
            Version = 1; SwitchesOn = new List<int>(); Variables = new Dictionary<string, int>(); SelfSwitches = new List<string>();
            Bag = new Dictionary<string, int>(); Party = new List<Pokemon>(); Storage = new List<Pokemon>();
            Flags = new List<string>(); Seen = new List<string>(); Caught = new List<string>();
        }
    }

    /// <summary>What the title screen shows about the save file.</summary>
    public class SaveSummary
    {
        public string PlayerName, Location;
        public long PlayFrames;
        public int PartyCount;
    }

    /// <summary>
    /// Saves to %AppData%\PokeCreator\Saves\&lt;game title&gt;\Game.json (writable even when the
    /// game is installed in Program Files). The previous save is kept as Game.bak.json.
    /// </summary>
    public static class SaveSystem
    {
        public static string Folder(Project p)
        {
            string title = string.IsNullOrEmpty(p.Settings.Title) ? p.Info.Name : p.Settings.Title;
            foreach (var c in Path.GetInvalidFileNameChars()) title = title.Replace(c, '_');
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PokeCreator", "Saves", title.Trim());
        }

        public static string FilePath(Project p) { return Path.Combine(Folder(p), "Game.json"); }

        public static bool Exists(Project p) { return File.Exists(FilePath(p)); }

        public static SaveSummary Peek(Project p)
        {
            try
            {
                if (!Exists(p)) return null;
                var d = Json.Json.FromFile<SaveData>(FilePath(p));
                return new SaveSummary { PlayerName = d.PlayerName, Location = d.Location, PlayFrames = d.PlayFrames, PartyCount = d.Party.Count };
            }
            catch { return null; }
        }

        public static void Save(Game g)
        {
            var s = g.State;
            if (s.Extras.MovedEvents != null && g.Map != null)
                foreach (var ev in g.Map.Events)
                {
                    string key = g.Map.Id + "," + ev.Id;
                    if (s.Extras.MovedEvents.ContainsKey(key)) s.Extras.MovedEvents[key] = new List<int> { ev.X, ev.Y, ev.Direction };
                }
            var d = new SaveData
            {
                GameTitle = g.Project.Settings.Title, SavedAt = DateTime.Now.ToString("s"),
                PlayerName = s.PlayerName, PlayerCharacter = s.PlayerCharacter, TrainerId = s.TrainerId, PlayFrames = s.PlayFrames,
                Money = s.Money, Steps = s.Steps,
                MapId = g.Map.Id, X = g.Player.X, Y = g.Player.Y, Direction = g.Player.Direction,
                Location = g.Message.Expand(g.Map.DisplayName), Bgm = g.Audio.CurrentBgm,
                SwitchesOn = s.Switches.Where(kv => kv.Value).Select(kv => kv.Key).OrderBy(x => x).ToList(),
                Variables = s.Variables.Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                StringVariables = s.StringVars.Count > 0 ? s.StringVars.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value) : null,
                SelfSwitches = s.SelfSwitches.OrderBy(x => x).ToList(),
                Bag = new Dictionary<string, int>(s.Bag),
                Party = s.Party, Storage = s.Storage,
                Flags = s.Flags.OrderBy(x => x).ToList(), Seen = s.Seen.OrderBy(x => x).ToList(), Caught = s.Caught.OrderBy(x => x).ToList(),
                RepelSteps = s.RepelSteps,
                Surfing = g.Player != null && g.Player.Surfing,
                Cycling = g.Player != null && g.Player.Cycling,
                Diving = g.Player != null && g.Player.Diving,
                Weather = g.Weather.Type != 0 ? new List<int> { g.Weather.Type, g.Weather.Power } : null,
                Visited = s.Visited.OrderBy(v => v).ToList(),
                EscapePoint = s.EscapePoint,
                DayCare = s.DayCare.Count > 0 ? s.DayCare : null,
                HallOfFame = s.HallOfFame.Count > 0 ? s.HallOfFame : null,
                Extras = s.Extras,
                Coins = s.Coins, BattlePoints = s.BattlePoints,
                Badges = s.Badges.Count > 0 ? s.Badges.OrderBy(b => b).ToList() : null,
                EventTimes = s.EventTimes.Count > 0 ? new Dictionary<string, long>(s.EventTimes) : null,
                BerryPlants = s.BerryPlants.Count > 0 ? new Dictionary<string, BerryPlantState>(s.BerryPlants) : null,
                Phone = s.Phone.Count > 0 ? s.Phone : null,
                DayCareEggSteps = s.DayCareEggSteps,
                DayCareEgg = s.DayCareEgg,
                PcItems = s.PcItems.Count > 0 ? new Dictionary<string, int>(s.PcItems) : null,
                HealPoint = s.HealMap > 0 ? new List<int> { s.HealMap, s.HealX, s.HealY, s.HealDir } : null
            };
            Directory.CreateDirectory(Folder(g.Project));
            string path = FilePath(g.Project), bak = Path.Combine(Folder(g.Project), "Game.bak.json");
            if (File.Exists(path)) File.Copy(path, bak, true);
            Json.Json.ToFile(path, d);
        }

        /// <summary>Loads the save into a fresh GameState and returns the saved position.</summary>
        public static SaveData Load(Project p, GameState s)
        {
            var d = Json.Json.FromFile<SaveData>(FilePath(p));
            s.PlayerName = d.PlayerName; s.PlayerCharacter = d.PlayerCharacter; s.TrainerId = d.TrainerId; s.PlayFrames = d.PlayFrames;
            s.Money = d.Money; s.Steps = d.Steps;
            s.Switches.Clear(); foreach (var id in d.SwitchesOn) s.Switches[id] = true;
            s.Variables.Clear(); foreach (var kv in d.Variables) { int id; if (int.TryParse(kv.Key, out id)) s.Variables[id] = kv.Value; }
            s.StringVars.Clear();
            if (d.StringVariables != null) foreach (var kv in d.StringVariables) { int id; if (int.TryParse(kv.Key, out id)) s.StringVars[id] = kv.Value; }
            s.SelfSwitches.Clear(); foreach (var k in d.SelfSwitches) s.SelfSwitches.Add(k);
            s.Bag.Clear(); foreach (var kv in d.Bag) s.Bag[kv.Key] = kv.Value;
            s.Party = d.Party ?? new List<Pokemon>();
            s.Storage = d.Storage ?? new List<Pokemon>();
            s.Flags.Clear(); if (d.Flags != null) foreach (var f in d.Flags) s.Flags.Add(f);
            s.Seen.Clear(); if (d.Seen != null) foreach (var f in d.Seen) s.Seen.Add(f);
            s.Caught.Clear(); if (d.Caught != null) foreach (var f in d.Caught) s.Caught.Add(f);
            foreach (var pk in s.Party.Concat(s.Storage)) s.Register(pk); // saves from before the Pokédex
            s.RepelSteps = d.RepelSteps;
            if (d.Visited != null) foreach (var v in d.Visited) s.Visited.Add(v);
            s.EscapePoint = d.EscapePoint;
            s.DayCare = d.DayCare ?? new List<DayCareSlot>();
            s.HallOfFame = d.HallOfFame ?? new List<HallOfFameEntry>();
            s.Extras = d.Extras ?? new GameExtras();
            s.Coins = d.Coins; s.BattlePoints = d.BattlePoints;
            s.Badges.Clear(); if (d.Badges != null) foreach (var b in d.Badges) s.Badges.Add(b);
            s.EventTimes.Clear(); if (d.EventTimes != null) foreach (var kv in d.EventTimes) s.EventTimes[kv.Key] = kv.Value;
            s.BerryPlants = d.BerryPlants ?? new Dictionary<string, BerryPlantState>();
            s.Phone = d.Phone ?? new List<PhoneContact>();
            s.DayCareEggSteps = d.DayCareEggSteps;
            s.DayCareEgg = d.DayCareEgg;
            s.PcItems.Clear(); if (d.PcItems != null) foreach (var kv in d.PcItems) s.PcItems[kv.Key] = kv.Value;
            if (d.HealPoint != null && d.HealPoint.Count >= 4) { s.HealMap = d.HealPoint[0]; s.HealX = d.HealPoint[1]; s.HealY = d.HealPoint[2]; s.HealDir = d.HealPoint[3]; }
            return d;
        }
    }
}
