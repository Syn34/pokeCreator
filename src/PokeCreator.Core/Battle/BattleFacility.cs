using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeCreator.Battles
{
    /// <summary>
    /// Battle Frontier rules (as Essentials' Battle Palace and Battle Arena):
    /// Palace — no one gives commands: each Pokémon picks attacking, defensive or support moves by its nature
    /// (more defensive natures change their minds at low HP).
    /// Arena — no switching; after three turns facing each other the judges score Mind (attacking moves used),
    /// Skill (moves that landed) and Body (HP kept) and the loser of the judgement faints.
    /// </summary>
    public partial class Battle
    {
        /// <summary>When set, every player decision is added here (0 = turn + encoded commands, 1 = switch after faint, 2 = shift) so the battle can be played back.</summary>
        public List<List<int>> Recording;
        /// <summary>The weather when the battle began (before abilities), for playing it back.</summary>
        public string InitialWeather;

        /// <summary>"palace", "arena" or null.</summary>
        public string FacilityRule;

        // Attack / defense / support % by nature: {high HP, low HP}
        static readonly Dictionary<string, int[]> PalaceTable = new Dictionary<string, int[]>
        {
            { "HARDY", new[] { 61, 7, 32, 61, 7, 32 } }, { "LONELY", new[] { 20, 25, 55, 84, 8, 8 } },
            { "BRAVE", new[] { 70, 15, 15, 32, 60, 8 } }, { "ADAMANT", new[] { 38, 31, 31, 70, 15, 15 } },
            { "NAUGHTY", new[] { 20, 70, 10, 70, 22, 8 } }, { "BOLD", new[] { 30, 20, 50, 32, 58, 10 } },
            { "DOCILE", new[] { 56, 22, 22, 56, 22, 22 } }, { "RELAXED", new[] { 25, 15, 60, 75, 15, 10 } },
            { "IMPISH", new[] { 69, 6, 25, 28, 55, 17 } }, { "LAX", new[] { 35, 10, 55, 29, 6, 65 } },
            { "TIMID", new[] { 62, 10, 28, 30, 20, 50 } }, { "HASTY", new[] { 58, 37, 5, 88, 6, 6 } },
            { "SERIOUS", new[] { 34, 11, 55, 29, 11, 60 } }, { "JOLLY", new[] { 35, 5, 60, 35, 60, 5 } },
            { "NAIVE", new[] { 56, 22, 22, 56, 22, 22 } }, { "MODEST", new[] { 35, 45, 20, 34, 60, 6 } },
            { "MILD", new[] { 44, 50, 6, 34, 6, 60 } }, { "QUIET", new[] { 56, 22, 22, 56, 22, 22 } },
            { "BASHFUL", new[] { 30, 58, 12, 30, 58, 12 } }, { "RASH", new[] { 30, 13, 57, 27, 6, 67 } },
            { "CALM", new[] { 40, 50, 10, 25, 62, 13 } }, { "GENTLE", new[] { 18, 70, 12, 90, 5, 5 } },
            { "SASSY", new[] { 88, 6, 6, 22, 20, 58 } }, { "CAREFUL", new[] { 42, 50, 8, 42, 5, 53 } },
            { "QUIRKY", new[] { 56, 22, 22, 55, 20, 25 } },
        };

        /// <summary>0 = attack, 1 = defense (protect, raise own stats, heal), 2 = support (everything else).</summary>
        int PalaceCategory(PokeCreator.Data.MoveData md)
        {
            if (md.Category != "Status") return 0;
            string code = md.FunctionCode ?? "";
            if (code.StartsWith("Protect") || code.StartsWith("RaiseUser") || code.StartsWith("HealUser") || md.Target == "User" || md.Target == "UserSide") return 1;
            return 2;
        }

        BattleCommand PalaceCommand(Battler me)
        {
            var usable = Enumerable.Range(0, me.Pk.Moves.Count).Where(i => me.Pk.Moves[i].PP > 0 && CanSelect(me, i)).ToList();
            if (usable.Count == 0) return ChooseAI(me);
            int[] t;
            if (!PalaceTable.TryGetValue((me.Pk.Nature ?? "").ToUpperInvariant(), out t)) t = new[] { 56, 22, 22, 56, 22, 22 };
            bool low = me.Pk.HP * 2 <= me.Pk.TotalHP(P);
            int a = low ? t[3] : t[0], d = low ? t[4] : t[1];
            int roll = Rng.Next(100), want = roll < a ? 0 : roll < a + d ? 1 : 2;
            var pick = usable.Where(i => { var md = Move(me.Pk.Moves[i].Id); return md != null && PalaceCategory(md) == want; }).ToList();
            if (pick.Count == 0) pick = usable; // "It didn't heed its nature" — any move will do
            var cmd = BattleCommand.Fight(pick[Rng.Next(pick.Count)]);
            var foes = Live(1 - me.Side);
            if (foes.Count > 0) { var f = foes[Rng.Next(foes.Count)]; cmd.TargetSide = f.Side; cmd.TargetSlot = f.Slot; }
            cmd.Slot = me.Slot;
            return cmd;
        }

        // ---- Battle Arena
        readonly Dictionary<Battler, int> _arenaMind = new Dictionary<Battler, int>(), _arenaSkill = new Dictionary<Battler, int>(), _arenaStartHp = new Dictionary<Battler, int>();
        PokeCreator.Pkmn.Pokemon _arenaA, _arenaB;
        int _arenaTurns;

        void ArenaMoveUsed(Battler user, PokeCreator.Data.MoveData md)
        {
            if (FacilityRule != "arena") return;
            int v; _arenaMind.TryGetValue(user, out v);
            string code = md.FunctionCode ?? "";
            _arenaMind[user] = v + (md.Category != "Status" ? 1 : code.StartsWith("Protect") ? -1 : 0);
            _arenaSkill.TryGetValue(user, out v); _arenaSkill[user] = v + 1;
        }

        void ArenaMissed(Battler user)
        {
            if (FacilityRule != "arena") return;
            int v; _arenaSkill.TryGetValue(user, out v); _arenaSkill[user] = v - 2;
        }

        void ArenaEndOfTurn()
        {
            if (FacilityRule != "arena" || Outcome != BattleOutcome.Ongoing) return;
            var a = Live(0).FirstOrDefault(); var b = Live(1).FirstOrDefault();
            if (a == null || b == null) return;
            if (a.Pk != _arenaA || b.Pk != _arenaB)
            {
                // A new pairing: the judges start over (this turn counts as the first)
                _arenaA = a.Pk; _arenaB = b.Pk; _arenaTurns = 0;
                _arenaMind.Clear(); _arenaSkill.Clear(); _arenaStartHp.Clear();
                _arenaStartHp[a] = a.Pk.TotalHP(P); _arenaStartHp[b] = b.Pk.TotalHP(P);
            }
            _arenaTurns++;
            if (_arenaTurns < 3) return;
            Say("The battle is over! It's now up to the judges!");
            int pa = 0, pb = 0;
            Action<int, int> score = (x, y) => { if (x > y) pa += 2; else if (y > x) pb += 2; else { pa++; pb++; } };
            int ma, mb, sa, sb;
            _arenaMind.TryGetValue(a, out ma); _arenaMind.TryGetValue(b, out mb);
            _arenaSkill.TryGetValue(a, out sa); _arenaSkill.TryGetValue(b, out sb);
            score(ma, mb); score(sa, sb);
            score(a.Pk.HP * 1000 / Math.Max(1, a.Pk.TotalHP(P)), b.Pk.HP * 1000 / Math.Max(1, b.Pk.TotalHP(P)));
            Say("Judgement: " + Name(a) + " " + pa + " - " + pb + " " + Name(b) + "!");
            var losers = pa > pb ? new[] { b } : pb > pa ? new[] { a } : new[] { a, b };
            foreach (var l in losers) { int f = l.Pk.HP; l.Pk.HP = 0; Hp(l, f); Say(Name(l) + " lost the judgement!"); }
            _arenaA = _arenaB = null; _arenaTurns = 0;
        }
    }
}
