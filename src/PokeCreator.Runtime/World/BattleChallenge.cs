using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.World
{
    /// <summary>Rules made by pbBattleTowerRules / pbBattleFactoryRules / pbBattlePalaceRules / pbBattleArenaRules / the cups.</summary>
    public class ChallengeRules
    {
        public string Kind { get; set; }
        public bool Double { get; set; }
        public bool OpenLevel { get; set; }
        public int Size { get; set; }
        public int MaxLevel { get; set; }
        public ChallengeRules() { Kind = "tower"; Size = 3; MaxLevel = 100; }
        public int Level { get { return OpenLevel ? 0 : Math.Min(50, MaxLevel); } }
    }

    public class FrontierRecord { public int PreviousWins { get; set; } public int MaxWins { get; set; } }

    public class FrontierState
    {
        public string Id { get; set; }
        public int Rounds { get; set; }
        public ChallengeRules Rules { get; set; }
        public bool InChallenge { get; set; }
        public bool Started { get; set; }
        public bool Resting { get; set; }
        public int Wins { get; set; }
        public int Decision { get; set; }
        public List<int> Entries { get; set; }
        public List<Pokemon> RealParty { get; set; }
        public List<Pokemon> Rentals { get; set; }
        /// <summary>The last opponent's team (the Battle Factory lets the player swap for one of them).</summary>
        public List<Pokemon> LastFoes { get; set; }
        public string NextType { get; set; }
        public string NextName { get; set; }
        public int StartMap { get; set; }
        public int StartX { get; set; }
        public int StartY { get; set; }
        public int StartDir { get; set; }
        public Dictionary<string, FrontierRecord> Records { get; set; }
        public FrontierState() { Entries = new List<int>(); Records = new Dictionary<string, FrontierRecord>(); }
    }

    /// <summary>
    /// Essentials' Battle Frontier calls (pbBattleChallenge): set / start / pbBattleChallengeBattle / pbAddWin /
    /// pbMatchOver? / setDecision / pbGoToStart / pbGoOn / pbRest / pbEnd / pbCancel, pbEntryScreen, pbHasEligible?,
    /// rentals for the Battle Factory, records — so the example Battle Tower, Factory, Palace and Arena events work.
    /// Opponents are generated trainers whose teams grow stronger every 7 wins of the streak.
    /// </summary>
    public class BattleChallengeApi
    {
        readonly Game _g;
        public BattleChallengeApi(Game g) { _g = g; }
        FrontierState S { get { if (_g.State.Extras.Frontier == null) _g.State.Extras.Frontier = new FrontierState(); return _g.State.Extras.Frontier; } }
        public BattleChallengeApi extra { get { return this; } }

        public void set(string id, int rounds, ChallengeRules rules)
        {
            var s = S;
            s.Id = id ?? "challenge"; s.Rounds = Math.Max(1, rounds); s.Rules = rules ?? new ChallengeRules();
            s.InChallenge = true; s.Started = false; s.Resting = false; s.Wins = 0; s.Decision = 0; s.Rentals = null;
            s.StartMap = _g.Map.Id; s.StartX = _g.Player.X; s.StartY = _g.Player.Y; s.StartDir = _g.Player.Direction;
        }

        public void start() { start(0, S.Rounds); }
        public void start(int area, int rounds)
        {
            var s = S;
            if (s.Rules == null) s.Rules = new ChallengeRules();
            if (rounds > 0) s.Rounds = rounds;
            s.Started = true; s.Wins = 0; s.Decision = 0;
            s.StartMap = _g.Map.Id; s.StartX = _g.Player.X; s.StartY = _g.Player.Y; s.StartDir = _g.Player.Direction;
            s.RealParty = _g.State.Party.ToList();
            List<Pokemon> team;
            if (s.Rules.Kind == "factory")
            {
                if (s.Rentals == null) pbPrepareRentals();
                team = s.Rentals.Take(s.Rules.Size).ToList();
            }
            else
            {
                var picks = s.Entries.Where(i => i >= 0 && i < _g.State.Party.Count).ToList();
                if (picks.Count == 0) picks = Enumerable.Range(0, _g.State.Party.Count).Where(i => !_g.State.Party[i].IsEgg).Take(s.Rules.Size).ToList();
                team = picks.Select(i => BattleTower.Copy(_g, _g.State.Party[i], s.Rules.Level)).ToList();
            }
            _g.State.Party = team;
            NextOpponent();
        }

        void NextOpponent()
        {
            var types = _g.Project.TrainerTypes.Where(t => !_g.Project.Players.Any(p => p.TrainerType == t.Id) && !t.Id.StartsWith("LEADER") && !t.Id.StartsWith("ELITE") && !t.Id.StartsWith("CHAMPION") && !t.Id.Contains("RIVAL")).ToList();
            if (types.Count == 0) types = _g.Project.TrainerTypes.ToList();
            S.NextType = types.Count > 0 ? types[_g.Rng.Next(types.Count)].Id : "";
            S.NextName = BattleTower.FirstNames[_g.Rng.Next(BattleTower.FirstNames.Length)];
        }

        public bool pbInChallenge() { return S.InChallenge; }
        public bool pbInProgress() { return S.InChallenge && S.Started; }
        public int battleNumber { get { return S.Wins + 1; } }
        // After a pbBattleTower run (and no Frontier challenge), these report the Battle Tower's result
        bool TowerResult { get { return !S.InChallenge && _g.State.Extras.Challenge != null && string.IsNullOrEmpty(S.Id); } }
        public int wins { get { return TowerResult ? _g.State.Extras.Challenge.Wins : S.Wins; } }
        public int decision { get { return TowerResult ? _g.State.Extras.Challenge.Decision : S.Decision; } }
        public void setDecision(int d) { S.Decision = d; }
        public void pbAddWin() { S.Wins++; NextOpponent(); }
        public bool pbMatchOver() { return S.Wins >= S.Rounds || S.Decision != 0; }
        public bool pbResting() { return S.Resting; }
        public void pbGoOn() { S.Resting = false; foreach (var p in _g.State.Party) p.Heal(_g.Project); }
        public void pbRest() { S.Resting = true; try { SaveSystem.Save(_g); } catch { } }
        public void pbGoToStart()
        {
            var s = S;
            if (s.StartMap > 0) _g.RequestTransfer(s.StartMap, s.StartX, s.StartY, s.StartDir > 0 ? s.StartDir : 2, true);
        }

        void Restore()
        {
            var s = S;
            if (s.RealParty != null) { _g.State.Party = s.RealParty; s.RealParty = null; }
        }

        public void pbEnd()
        {
            var s = S;
            Restore();
            if (s.Started && !string.IsNullOrEmpty(s.Id))
            {
                FrontierRecord r;
                if (!s.Records.TryGetValue(s.Id, out r)) s.Records[s.Id] = r = new FrontierRecord();
                r.PreviousWins = s.Wins;
                if (s.Wins > r.MaxWins) r.MaxWins = s.Wins;
            }
            s.InChallenge = false; s.Started = false; s.Resting = false; s.Entries.Clear(); s.Rentals = null;
        }

        public void pbCancel() { Restore(); var s = S; s.InChallenge = false; s.Started = false; s.Resting = false; s.Entries.Clear(); s.Rentals = null; }

        public int getPreviousWins(string id) { FrontierRecord r; return S.Records.TryGetValue(id ?? "", out r) ? r.PreviousWins : 0; }
        public int getMaxWins(string id) { FrontierRecord r; return S.Records.TryGetValue(id ?? "", out r) ? r.MaxWins : 0; }

        // Battle Factory rentals and swaps
        public void pbPrepareRentals()
        {
            var s = S;
            int set = s.Wins / 7, level = s.Rules != null && s.Rules.Level > 0 ? s.Rules.Level : 100;
            var used = new HashSet<string>();
            s.Rentals = new List<Pokemon>();
            while (s.Rentals.Count < 6)
            {
                var pk = BattleTower.Generate(_g, set, level);
                if (used.Add(pk.Species)) s.Rentals.Add(pk);
            }
        }
        public bool pbChooseRentals() { return true; }

        /// <summary>Battle Factory: pick the team from the six rentals; it replaces the party for the challenge.</summary>
        public void ChooseRentals(Action<bool> done)
        {
            var s = S;
            if (s.Rentals == null) pbPrepareRentals();
            int need = s.Rules != null ? Math.Max(1, s.Rules.Size) : 3;
            var left = s.Rentals.ToList(); var chosen = new List<Pokemon>();
            Action pick = null;
            pick = () =>
            {
                if (chosen.Count >= need)
                {
                    s.Rentals = chosen.Concat(left).ToList();
                    if (s.Started) { if (s.RealParty == null) s.RealParty = _g.State.Party.ToList(); _g.State.Party = chosen.ToList(); }
                    done(true); return;
                }
                var names = left.Select(p => p.Name(_g.Project) + " Lv" + p.Level + "  " + string.Join("/", p.Moves.Select(m => { var md = _g.Project.Moves.FirstOrDefault(x => x.Id == m.Id); return md != null ? md.Name : m.Id; }).Take(2))).ToList();
                _g.Message.Show("Choose rental Pokémon " + (chosen.Count + 1) + " of " + need + ".", names, -1, k =>
                {
                    if (k < 0 || k >= left.Count) { pick(); return; }
                    chosen.Add(left[k]); left.RemoveAt(k); pick();
                });
            };
            pick();
        }
        public void pbPrepareSwaps() { }
        public bool pbChooseSwaps() { return false; }

        /// <summary>Battle Factory: offer to trade one of the player's rentals for one of the beaten trainer's Pokémon.</summary>
        public void ChooseSwap(Action<bool> done)
        {
            var s = S;
            if (s.LastFoes == null || s.LastFoes.Count == 0) { done(false); return; }
            {
                _g.PushScene(new UI.PartyScene(_g, "Swap which Pokémon?", null, (i, close) =>
                {
                    close();
                    var names = s.LastFoes.Select(f => f.Name(_g.Project) + " Lv" + f.Level).ToList();
                    names.Add("Cancel");
                    _g.Message.Show("Take which Pokémon?", names, names.Count - 1, k =>
                    {
                        if (k < 0 || k >= s.LastFoes.Count) { done(false); return; }
                        var got = s.LastFoes[k]; got.Heal(_g.Project);
                        _g.State.Party[i] = got; s.LastFoes.RemoveAt(k);
                        _g.Message.Show("You received " + got.Name(_g.Project) + "!", null, -1, _ => done(true));
                    });
                }, () => done(false)));
            }
        }

        /// <summary>pbBattleChallengeBattle: fights the next trainer; the answer (won?) comes when the event checks again.</summary>
        public void Battle(Action<bool> done)
        {
            var s = S;
            foreach (var p in _g.State.Party) p.Heal(_g.Project);
            int count = s.Rules != null && s.Rules.Double ? 4 : 3;
            int level = s.Rules != null && s.Rules.Level > 0 ? s.Rules.Level : (int)Math.Max(5, _g.State.Party.Where(p => !p.IsEgg).Select(p => p.Level).DefaultIfEmpty(50).Average());
            int set = s.Wins / 7;
            var foes = new List<Pokemon>(); var used = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                Pokemon pk = null;
                for (int t = 0; t < 10 && (pk == null || used.Contains(pk.Species)); t++) pk = BattleTower.Generate(_g, set, level);
                used.Add(pk.Species); foes.Add(pk);
            }
            s.LastFoes = foes.Select(f => BattleTower.Copy(_g, f, f.Level)).ToList();
            var tt = _g.Project.TrainerTypes.FirstOrDefault(t => t.Id == s.NextType);
            _g.StartFacilityBattle(tt, s.NextName ?? "Trainer", foes, s.Rules != null && s.Rules.Double, s.Rules != null ? s.Rules.Kind : null, o =>
            {
                _g.ResumeOutcome = null;
                foreach (var p in _g.State.Party) p.Heal(_g.Project);
                done(o == PokeCreator.Battles.BattleOutcome.Win);
            });
        }

        public string BeginSpeech() { return BattleTower.Intros[_g.Rng.Next(BattleTower.Intros.Length)]; }
        public string NextGraphic() { return "trainer_" + (S.NextType ?? ""); }
    }
}
