using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.World
{
    public class ChallengeState
    {
        public bool InProgress { get; set; }
        public int Wins { get; set; }
        public int Rounds { get; set; }
        public bool Double { get; set; }
        /// <summary>null / "tower", "palace", "arena" or "factory".</summary>
        public string Rule { get; set; }
        public int Level { get; set; }
        /// <summary>1 = won every round, 2 = lost, 3 = gave up.</summary>
        public int Decision { get; set; }
        /// <summary>The real party while the challenge team (copies at the challenge level) is in use.</summary>
        public List<Pokemon> RealParty { get; set; }
        /// <summary>Best winning streak, per mode ("single" / "double").</summary>
        public Dictionary<string, int> Records { get; set; }
        public Dictionary<string, int> Streaks { get; set; }
        public ChallengeState() { Records = new Dictionary<string, int>(); Streaks = new Dictionary<string, int>(); }
    }

    /// <summary>
    /// The Battle Tower (pbBattleTower / pbBattleChallenge): the player picks 3 Pokémon (4 for doubles), which fight at
    /// the challenge level as copies, against a row of generated trainers whose teams get stronger as the streak grows.
    /// The team is healed between battles; wins earn Battle Points; records are kept per mode.
    /// </summary>
    public static class BattleTower
    {
        public static readonly string[] FirstNames = { "Alex", "Jamie", "Morgan", "Riley", "Casey", "Taylor", "Jordan", "Quinn", "Avery", "Dakota", "Sky", "Rowan", "Kai", "Remy", "Sage", "Ash", "Robin", "Drew", "Emery", "Finley" };
        static readonly string[] HeldItems = { "LEFTOVERS", "SITRUSBERRY", "LUMBERRY", "FOCUSSASH", "CHOICEBAND", "CHOICESCARF", "CHOICESPECS", "LIFEORB", "EXPERTBELT", "QUICKCLAW", "WHITEHERB", "BRIGHTPOWDER", "SHELLBELL", "SCOPELENS" };
        public static readonly string[] Intros = { "I've been waiting for a worthy opponent!", "Let's see what your team can do!", "My Pokémon are in top form today!", "I won't go easy on you!", "This is where your streak ends!", "Show me your strongest Pokémon!" };

        public static ChallengeState State(Game g)
        {
            if (g.State.Extras.Challenge == null) g.State.Extras.Challenge = new ChallengeState();
            return g.State.Extras.Challenge;
        }

        public static bool InProgress(Game g) { return g.State != null && g.State.Extras.Challenge != null && g.State.Extras.Challenge.InProgress; }

        public static Pokemon Copy(Game g, Pokemon pk, int level)
        {
            var c = Json.Json.Deserialize<Pokemon>(Json.Json.Write(pk));
            if (level > 0)
            {
                c.Level = level;
                var d = c.Data(g.Project);
                c.Exp = GrowthRates.ExpForLevel(d != null ? d.GrowthRate : "Medium", level);
            }
            c.Heal(g.Project);
            return c;
        }

        /// <summary>Starts a challenge: choose the team, then fight the rounds one after another.</summary>
        public static void Run(Game g, int rounds, bool dbl, int level) { Run(g, rounds, dbl, level, null); }

        static string Mode(ChallengeState s) { string m = s.Double ? "double" : "single"; return string.IsNullOrEmpty(s.Rule) || s.Rule == "tower" ? m : s.Rule + "_" + m; }

        /// <summary>A Battle Frontier run: rule null/"tower", "palace" (Pokémon act on their own), "arena" (judged after 3 turns, singles) or "factory" (rental Pokémon, swaps after wins).</summary>
        public static void Run(Game g, int rounds, bool dbl, int level, string rule)
        {
            var s = State(g);
            if (rule == "arena") dbl = false;
            s.Rule = rule == "tower" ? null : rule; s.Level = level;
            int size = dbl ? 4 : 3;
            if (rule == "factory") { g.ScriptHolds++; s.Double = dbl; ChooseRentals(g, size, team => BeginWith(g, team, rounds, dbl, level)); return; }
            if (g.State.Party.Count(p => !p.IsEgg) < size) { g.Message.Show("You need at least " + size + " Pokémon to take the challenge."); return; }
            g.ScriptHolds++;
            var chosen = new List<int>();
            Action pick = null;
            pick = () =>
            {
                if (chosen.Count >= size) { Begin(g, chosen, rounds, dbl, level); return; }
                g.PushScene(new UI.PartyScene(g, "Choose Pokémon " + (chosen.Count + 1) + " of " + size + " for the challenge.", null, (i, close) =>
                {
                    var pk = g.State.Party[i];
                    if (pk.IsEgg) { g.Message.Show("An Egg can't take part!"); return; }
                    var sd = pk.Data(g.Project);
                    if (pk.ShadowPokemon || (sd != null && sd.Flags != null && (sd.Flags.Contains("Legendary") || sd.Flags.Contains("Mythical")))) { g.Message.Show(pk.Name(g.Project) + " can't take part in this challenge!"); return; }
                    if (chosen.Contains(i)) { g.Message.Show("That Pokémon is already entered!"); return; }
                    if (chosen.Any(j => g.State.Party[j].Species == pk.Species)) { g.Message.Show("You can't enter two of the same species!"); return; }
                    close(); chosen.Add(i); pick();
                }, () => { g.ScriptHolds--; g.Message.Show("You didn't enter the challenge."); }));
            };
            pick();
        }

        static void Begin(Game g, List<int> chosen, int rounds, bool dbl, int level)
        {
            BeginWith(g, chosen.Select(i => Copy(g, g.State.Party[i], level)).ToList(), rounds, dbl, level);
        }

        static void BeginWith(Game g, List<Pokemon> team, int rounds, bool dbl, int level)
        {
            var s = State(g);
            s.InProgress = true; s.Wins = 0; s.Rounds = Math.Max(1, rounds); s.Double = dbl; s.Decision = 0;
            s.RealParty = g.State.Party.ToList();
            g.State.Party = team;
            NextRound(g, level);
        }

        /// <summary>Battle Factory: six rentals; the player keeps the given number.</summary>
        static void ChooseRentals(Game g, int size, Action<List<Pokemon>> done)
        {
            var s = State(g);
            string mode = Mode(s); int streak; s.Streaks.TryGetValue(mode, out streak);
            var left = new List<Pokemon>(); var seen = new HashSet<string>();
            while (left.Count < 6) { var pk = Generate(g, streak / 7, s.Level > 0 ? s.Level : 50); if (seen.Add(pk.Species)) left.Add(pk); }
            var chosen = new List<Pokemon>();
            Action pick = null;
            pick = () =>
            {
                if (chosen.Count >= size) { done(chosen); return; }
                var names = left.Select(p => p.Name(g.Project) + " Lv" + p.Level).ToList();
                g.Message.Show("Choose rental Pokémon " + (chosen.Count + 1) + " of " + size + ".", names, -1, k =>
                {
                    if (k >= 0 && k < left.Count) { chosen.Add(left[k]); left.RemoveAt(k); }
                    pick();
                });
            };
            pick();
        }

        /// <summary>Battle Factory: after a win, the player may swap one Pokémon for one of the beaten trainer's.</summary>
        static void OfferSwap(Game g, List<Pokemon> foes, Action next)
        {
            g.Message.Show("Would you like to swap a Pokémon?", new List<string> { "Yes", "No" }, 1, yn =>
            {
                if (yn != 0) { next(); return; }
                var mine = g.State.Party.Select(p => p.Name(g.Project) + " Lv" + p.Level).ToList(); mine.Add("Cancel");
                g.Message.Show("Give up which Pokémon?", mine, mine.Count - 1, a =>
                {
                    if (a < 0 || a >= g.State.Party.Count) { next(); return; }
                    var theirs = foes.Select(p => p.Name(g.Project) + " Lv" + p.Level).ToList(); theirs.Add("Cancel");
                    g.Message.Show("Take which Pokémon?", theirs, theirs.Count - 1, b =>
                    {
                        if (b < 0 || b >= foes.Count) { next(); return; }
                        var got = Copy(g, foes[b], foes[b].Level); got.Heal(g.Project);
                        g.State.Party[a] = got;
                        g.Message.Show("You received " + got.Name(g.Project) + "!", null, -1, _ => next());
                    });
                });
            });
        }

        public static Pokemon Generate(Game g, int set, int level)
        {
            int min = Math.Min(560, 330 + set * 45), max = Math.Min(680, 470 + set * 45);
            var pool = g.Project.Species.Where(sp => sp.Form == 0 && sp.BaseStats != null && sp.BaseStats.Count == 6 && sp.CatchRate > 3
                && sp.BaseStats.Sum() >= min && sp.BaseStats.Sum() <= max && (sp.Evolutions == null || sp.Evolutions.Count == 0 || sp.BaseStats.Sum() >= 450)).ToList();
            if (pool.Count == 0) pool = g.Project.Species.Where(sp => sp.Form == 0).ToList();
            var pk = Pokemon.Create(g.Project, pool[g.Rng.Next(pool.Count)].Id, level > 0 ? level : 50, g.Rng);
            var items = HeldItems.Where(i => g.Project.Items.Any(x => x.Id == i)).ToList();
            if (items.Count > 0) pk.Item = items[g.Rng.Next(items.Count)];
            for (int i = 0; i < 6; i++) pk.IV[i] = Math.Min(31, 10 + set * 4 + g.Rng.Next(12));
            pk.Heal(g.Project);
            return pk;
        }

        static void NextRound(Game g, int level)
        {
            var s = State(g);
            string mode = Mode(s);
            int streak; s.Streaks.TryGetValue(mode, out streak);
            int set = (streak + s.Wins) / 7;
            var types = g.Project.TrainerTypes.Where(t => !g.Project.Players.Any(p => p.TrainerType == t.Id) && !t.Id.StartsWith("LEADER") && !t.Id.StartsWith("ELITE") && !t.Id.StartsWith("CHAMPION") && !t.Id.Contains("RIVAL")).ToList();
            if (types.Count == 0) types = g.Project.TrainerTypes.ToList();
            var tt = types.Count > 0 ? types[g.Rng.Next(types.Count)] : null;
            string name = FirstNames[g.Rng.Next(FirstNames.Length)];
            int count = s.Double ? 4 : 3;
            var used = new HashSet<string>();
            var foes = new List<Pokemon>();
            for (int i = 0; i < count; i++)
            {
                Pokemon pk = null;
                for (int t = 0; t < 10 && (pk == null || used.Contains(pk.Species)); t++) pk = Generate(g, set, level);
                used.Add(pk.Species); foes.Add(pk);
            }
            foreach (var pk in g.State.Party) pk.Heal(g.Project);
            var foeCopies = foes.Select(f => Copy(g, f, f.Level)).ToList();
            string label = (tt != null && !string.IsNullOrEmpty(tt.Name) ? tt.Name + " " : "") + name;
            g.Message.Show("Battle " + (s.Wins + 1) + " of " + s.Rounds + "!", null, -1, _ =>
                g.Message.Show(label + ": " + Intros[g.Rng.Next(Intros.Length)], null, -1, __ =>
                    g.StartFacilityBattle(tt, name, foes.Select(f => f).ToList(), s.Double, s.Rule, o =>
                    {
                        g.ResumeOutcome = null;
                        if (o == PokeCreator.Battles.BattleOutcome.Win)
                        {
                            s.Wins++;
                            if (s.Wins >= s.Rounds) { Finish(g, 1); return; }
                            g.Message.Show("Congratulations! You won battle " + s.Wins + "!", new List<string> { "Continue", "Quit" }, 0, c =>
                            {
                                if (c == 1) Finish(g, 3);
                                else if (s.Rule == "factory") OfferSwap(g, foeCopies, () => NextRound(g, level));
                                else NextRound(g, level);
                            });
                        }
                        else Finish(g, 2);
                    })));
        }

        static void Finish(Game g, int decision)
        {
            var s = State(g);
            s.Decision = decision; s.InProgress = false;
            if (s.RealParty != null) { g.State.Party = s.RealParty; s.RealParty = null; }
            string mode = Mode(s);
            int streak; s.Streaks.TryGetValue(mode, out streak);
            streak = decision == 2 ? 0 : streak + s.Wins;
            s.Streaks[mode] = streak;
            int best; s.Records.TryGetValue(mode, out best);
            int run = decision == 2 ? s.Wins : streak;
            if (run > best) s.Records[mode] = run;
            int bp = s.Wins + (decision == 1 ? Math.Max(3, s.Rounds / 2) : 0);
            g.State.BattlePoints = Math.Min(9999, g.State.BattlePoints + bp);
            string result = decision == 1 ? "Congratulations! You beat all " + s.Rounds + " trainers!" : decision == 2 ? "Your challenge ended with " + s.Wins + " win" + (s.Wins == 1 ? "" : "s") + "." : "You stopped after " + s.Wins + " win" + (s.Wins == 1 ? "" : "s") + ".";
            g.Message.Show(result, null, -1, _ =>
                g.Message.Show(bp > 0 ? "\\PN earned " + bp + " Battle Point" + (bp == 1 ? "" : "s") + "!" : "Better luck next time!", null, -1, __ => g.ScriptHolds = Math.Max(0, g.ScriptHolds - 1)));
        }
    }
}
