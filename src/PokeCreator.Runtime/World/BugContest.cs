using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.World
{
    public class BugContestState
    {
        public bool InProgress { get; set; }
        /// <summary>The contest ended and the judges haven't announced the results yet.</summary>
        public bool Undecided { get; set; }
        public int Balls { get; set; }
        /// <summary>When the contest ends (DateTime ticks).</summary>
        public long EndsAt { get; set; }
        public Pokemon Caught { get; set; }
        /// <summary>The party members left at the reception, and which one the player took.</summary>
        public List<Pokemon> LeftBehind { get; set; }
        public int StartMap { get; set; }
        public int StartX { get; set; }
        public int StartY { get; set; }
        public int StartDir { get; set; }
        public int Place { get; set; }
        /// <summary>When the last contest was held (DateTime ticks) — pbContestHeld? is true for a day after.</summary>
        public long LastContest { get; set; }
        /// <summary>The judged top places (name, Pokémon, score) once pbStartJudging ran.</summary>
        public List<ContestPlace> Results { get; set; }
        public BugContestState() { LeftBehind = new List<Pokemon>(); Place = -1; }
    }

    public class ContestPlace { public string Name { get; set; } public string Pokemon { get; set; } public int Score { get; set; } }

    /// <summary>
    /// The Bug-Catching Contest (pbBugContestState): the player enters with one Pokémon and some Sport Balls, catches
    /// one Pokémon from the contest maps' BugContest encounters before time runs out, and the judges score it against
    /// other contestants (level, IVs, HP left and rarity, as in Essentials).
    /// </summary>
    public static class BugContest
    {
        public static readonly string[] Contestants = { "Bug Catcher Ed", "Bug Catcher Benny", "Bug Catcher Josh", "Camper Barry", "Cool Trainer Nick", "Lass Abby", "Picnicker Cindy", "Youngster Samuel" };

        public static BugContestState State(Game g)
        {
            if (g.State.Extras.BugContest == null) g.State.Extras.BugContest = new BugContestState();
            return g.State.Extras.BugContest;
        }

        public static bool InProgress(Game g) { return g.State != null && g.State.Extras.BugContest != null && g.State.Extras.BugContest.InProgress; }

        /// <summary>A contest map: one whose encounter table has BugContest encounters.</summary>
        public static bool ContestMap(Game g)
        {
            return g.Map != null && g.Project.Encounters.Any(e => e.Map == g.Map.Id && e.Methods.Any(m => m.Type == "BugContest" && m.Slots.Count > 0));
        }

        public static bool InField(Game g) { return InProgress(g) && ContestMap(g); }

        /// <summary>pbBugContestState.pbSetPokemon(index): the player enters with this party member; the rest wait at the reception.</summary>
        public static void SetPokemon(Game g, int index)
        {
            var s = State(g);
            var party = g.State.Party;
            if (index < 0 || index >= party.Count) return;
            var chosen = party[index];
            s.LeftBehind = party.Where(p => p != chosen).ToList();
            party.Clear(); party.Add(chosen);
        }

        /// <summary>pbBugContestState.pbStart(balls): the contest begins (the timer runs from now).</summary>
        public static void Start(Game g, int balls)
        {
            var s = State(g);
            s.InProgress = true; s.Undecided = false; s.Caught = null; s.Place = -1; s.Results = null;
            s.LastContest = DateTime.Now.Ticks;
            s.Balls = balls > 0 ? balls : Math.Max(1, g.Project.Settings.BugContestBalls);
            s.EndsAt = DateTime.Now.AddMinutes(Math.Max(1, g.Project.Settings.BugContestMinutes)).Ticks;
            s.StartMap = g.Map.Id; s.StartX = g.Player.X; s.StartY = g.Player.Y; s.StartDir = g.Player.Direction;
        }

        /// <summary>Ends the contest: the party is put back together and the judging is pending.</summary>
        public static void End(Game g, bool goBack)
        {
            var s = State(g);
            if (!s.InProgress) return;
            s.InProgress = false; s.Undecided = true;
            foreach (var pk in s.LeftBehind) if (!g.State.Party.Contains(pk)) g.State.Party.Add(pk);
            s.LeftBehind.Clear();
            if (goBack && s.StartMap > 0) g.RequestTransfer(s.StartMap, s.StartX, s.StartY, s.StartDir > 0 ? s.StartDir : 2, true);
        }

        /// <summary>Checks the clock (called every frame on the map).</summary>
        public static void Update(Game g)
        {
            if (!InProgress(g) || g.SceneOpen || g.MessageBusy || g.InterpreterRunning) return;
            if (DateTime.Now.Ticks < State(g).EndsAt) return;
            g.Message.Show("ANNOUNCER: BEEEEEP! Time's up!", null, -1, _ =>
                g.Message.Show("ANNOUNCER: The Bug-Catching Contest is over!", null, -1, __ => End(g, true)));
        }

        /// <summary>A Pokémon caught in the contest: the player keeps only one.</summary>
        public static void OnCaught(Game g, Pokemon pk, Action next)
        {
            var s = State(g);
            if (s.Caught == null) { s.Caught = pk; next(); return; }
            g.Message.Show("You already caught a " + s.Caught.Name(g.Project) + ". Switch Pokémon?", new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c == 0) { g.Message.Show("You released " + s.Caught.Name(g.Project) + ".", null, -1, _ => { s.Caught = pk; next(); }); }
                else g.Message.Show("You released " + pk.Name(g.Project) + ".", null, -1, _ => next());
            });
        }

        /// <summary>After a contest battle: running out of balls ends the contest.</summary>
        public static void AfterBattle(Game g)
        {
            var s = State(g);
            if (s.InProgress && s.Balls <= 0)
                g.Message.Show("ANNOUNCER: You're out of Sport Balls! The Bug-Catching Contest is over for you!", null, -1, _ => End(g, true));
        }

        /// <summary>Essentials' contest score: level x4, IVs (up to 100), HP left (up to 100), rarity 60-100.</summary>
        public static int Score(Game g, Pokemon pk)
        {
            if (pk == null) return 0;
            int level = pk.Level * 4;
            int iv = (int)Math.Floor(pk.IV.Take(6).Sum() * 100.0 / 186);
            int hp = (int)Math.Floor(100.0 * pk.HP / Math.Max(1, pk.TotalHP(g.Project)));
            var d = pk.Data(g.Project);
            int rate = d != null && d.CatchRate > 0 ? d.CatchRate : 255;
            int rare = 60 + (rate <= 120 ? 20 : 0) + (rate <= 60 ? 20 : 0);
            return level + iv + hp + rare;
        }

        /// <summary>
        /// pbBugContestState.pbJudge: scores the player's catch against three other contestants, announces the top three,
        /// gives the player's prize, and sends the caught Pokémon to the party (or the PC).
        /// </summary>
        /// <summary>pbStartJudging: scores the player's catch against three contestants (Place 0-2, or 3 when not placed).</summary>
        public static void ComputeResults(Game g)
        {
            var s = State(g);
            if (s.InProgress) End(g, false);
            var table = g.Project.Encounters.SelectMany(e => e.Methods.Where(m => m.Type == "BugContest")).SelectMany(m => m.Slots).ToList();
            var results = new List<ContestPlace>();
            foreach (var n in Contestants.OrderBy(_ => g.Rng.Next()).Take(3))
            {
                if (table.Count == 0) break;
                var slot = table[g.Rng.Next(table.Count)];
                var pk = Pokemon.Create(g.Project, slot.Species, g.Rng.Next(slot.MinLevel, Math.Max(slot.MinLevel, slot.MaxLevel) + 1), g.Rng);
                pk.HP = Math.Max(1, pk.TotalHP(g.Project) * (40 + g.Rng.Next(61)) / 100);
                results.Add(new ContestPlace { Name = n, Pokemon = pk.Name(g.Project), Score = Score(g, pk) });
            }
            while (results.Count < 3) results.Add(new ContestPlace { Name = Contestants[results.Count], Pokemon = "Caterpie", Score = 100 + results.Count });
            if (s.Caught != null) results.Add(new ContestPlace { Name = g.State.PlayerName, Pokemon = s.Caught.Name(g.Project), Score = Score(g, s.Caught) });
            results = results.OrderByDescending(r => r.Score).ToList();
            int place = s.Caught != null ? results.FindIndex(r => r.Name == g.State.PlayerName) : -1;
            s.Place = place < 0 || place > 2 ? 3 : place;
            s.Results = results.Take(3).ToList();
            s.Undecided = false;
        }

        /// <summary>pbEnd after judging: the caught Pokémon joins the party (or the PC).</summary>
        public static void FinishJudged(Game g)
        {
            var s = State(g);
            if (s.InProgress) End(g, false);
            var caught = s.Caught; s.Caught = null; s.Undecided = false;
            if (caught == null) return;
            caught.OTName = g.State.PlayerName; caught.OTId = g.State.TrainerId;
            g.State.Register(caught);
            if (g.State.Party.Count < GameState.MaxParty) g.State.Party.Add(caught);
            else { g.State.Storage.Add(caught); g.Message.Show(caught.Name(g.Project) + " was sent to the PC."); }
        }

        public static void Judge(Game g, Action done)
        {
            var s = State(g);
            var table = g.Project.Encounters.Where(e => e.Methods.Any(m => m.Type == "BugContest")).SelectMany(e => e.Methods.Where(m => m.Type == "BugContest")).SelectMany(m => m.Slots).ToList();
            var results = new List<Tuple<string, string, int>>();
            var names = Contestants.OrderBy(_ => g.Rng.Next()).Take(3).ToList();
            foreach (var n in names)
            {
                if (table.Count == 0) break;
                var slot = table[g.Rng.Next(table.Count)];
                var pk = Pokemon.Create(g.Project, slot.Species, g.Rng.Next(slot.MinLevel, Math.Max(slot.MinLevel, slot.MaxLevel) + 1), g.Rng);
                pk.HP = Math.Max(1, pk.TotalHP(g.Project) * (40 + g.Rng.Next(61)) / 100);
                results.Add(Tuple.Create(n, pk.Name(g.Project), Score(g, pk)));
            }
            if (s.Caught != null) results.Add(Tuple.Create(g.State.PlayerName, s.Caught.Name(g.Project), Score(g, s.Caught)));
            results = results.OrderByDescending(r => r.Item3).ToList();
            s.Place = s.Caught != null ? results.FindIndex(r => r.Item1 == g.State.PlayerName) : -1;
            s.Undecided = false;
            var msgs = new List<string> { "We will now announce the winners of the Bug-Catching Contest!" };
            string[] ord = { "first", "second", "third" };
            for (int i = Math.Min(2, results.Count - 1); i >= 0; i--)
                msgs.Add("Placing " + ord[i] + " was " + results[i].Item1 + ", who caught a " + results[i].Item2 + "! The score was " + results[i].Item3 + " points!");
            string prize = s.Place == 0 ? g.Project.Settings.BugContestPrize1 : s.Place == 1 ? g.Project.Settings.BugContestPrize2 : s.Place == 2 ? g.Project.Settings.BugContestPrize3 : g.Project.Settings.BugContestPrizeOther;
            var caught = s.Caught; s.Caught = null;
            Action giveAll = () =>
            {
                if (s.Place >= 0 && !string.IsNullOrEmpty(prize) && g.Project.Items.Any(i => i.Id == prize))
                {
                    g.State.AddItem(prize, 1);
                    var it = g.Project.Items.First(i => i.Id == prize);
                    msgs.Add("\\PN received " + (it.Name ?? prize) + "!");
                }
                if (caught != null)
                {
                    caught.OTName = g.State.PlayerName; caught.OTId = g.State.TrainerId;
                    g.State.Register(caught);
                    if (g.State.Party.Count < GameState.MaxParty) g.State.Party.Add(caught);
                    else { g.State.Storage.Add(caught); msgs.Add(caught.Name(g.Project) + " was sent to the PC."); }
                }
            };
            giveAll();
            Action<int> show = null;
            show = i => { if (i >= msgs.Count) { if (done != null) done(); return; } g.Message.Show(msgs[i], null, -1, _ => show(i + 1)); };
            show(0);
        }
    }
}
