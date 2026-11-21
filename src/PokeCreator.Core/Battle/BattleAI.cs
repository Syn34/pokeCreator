using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PokeCreator.Data;
using PokeCreator.Pkmn;

namespace PokeCreator.Battles
{
    /// <summary>
    /// Trainer AI for skilled trainers (skill 32+, as Essentials' AI levels): moves are scored by the damage they'd do
    /// (as a share of the target's HP, KOs first, priority to finish), abilities that block a type, whether a status
    /// move would work, healing when low, setting up when healthy, hazards and Protect; skill 48+ also switches out of
    /// a hopeless match-up to a Pokémon that does better.
    /// </summary>
    public partial class Battle
    {
        static readonly Dictionary<string, string> AbsorbAbilities = new Dictionary<string, string>
        {
            { "LEVITATE", "GROUND" }, { "FLASHFIRE", "FIRE" }, { "WATERABSORB", "WATER" }, { "STORMDRAIN", "WATER" }, { "DRYSKIN", "WATER" },
            { "VOLTABSORB", "ELECTRIC" }, { "LIGHTNINGROD", "ELECTRIC" }, { "MOTORDRIVE", "ELECTRIC" }, { "SAPSIPPER", "GRASS" }, { "EARTHEATER", "GROUND" }, { "WELLBAKEDBODY", "FIRE" },
        };

        /// <summary>Expected damage without randomness or side effects (an estimate for choosing moves).</summary>
        double EstimateDamage(Battler user, Battler target, MoveData md, int skill)
        {
            if (md == null || md.Category == "Status") return 0;
            double eff = EffectivenessOn(md.Type, target);
            if (skill >= 32)
            {
                string ab = target.Pk.AbilityId(P) ?? "";
                string blocked;
                if (AbsorbAbilities.TryGetValue(ab, out blocked) && blocked == md.Type && !HasAbility(user, "MOLDBREAKER")) eff = 0;
                if (ab == "WONDERGUARD" && eff <= 1) eff = 0;
                if (md.Type == "GROUND" && target.Pk.Item == "AIRBALLOON") eff = 0;
            }
            if (eff <= 0) return 0;
            bool physical = md.Category == "Physical";
            int a = Stat(user, physical ? PokeCreator.Pkmn.Stat.Attack : PokeCreator.Pkmn.Stat.SpAtk);
            int d = Stat(target, physical ? PokeCreator.Pkmn.Stat.Defense : PokeCreator.Pkmn.Stat.SpDef);
            int power = md.Power <= 1 ? 60 : md.Power;
            string code = md.FunctionCode ?? "";
            if (code.StartsWith("HitTwoTimes")) power *= 2;
            else if (code.StartsWith("HitTwoToFiveTimes")) power *= 3;
            double dmg = Math.Floor(Math.Floor(2.0 * user.Pk.Level / 5 + 2) * power * a / Math.Max(1, d) / 50) + 2;
            if (TypesOf(user).Contains(md.Type)) dmg *= HasAbility(user, "ADAPTABILITY") ? 2 : 1.5;
            dmg *= eff;
            if (physical && (user.Pk.Status ?? "").ToUpperInvariant() == "BURN" && !HasAbility(user, "GUTS")) dmg /= 2;
            if (Weather == Sun && md.Type == "FIRE" || Weather == Rain && md.Type == "WATER") dmg *= 1.5;
            if (Weather == Sun && md.Type == "WATER" || Weather == Rain && md.Type == "FIRE") dmg *= 0.5;
            double acc = md.Accuracy > 0 ? md.Accuracy / 100.0 : 1;
            return dmg * acc;
        }

        bool StatusWouldWork(Battler user, Battler target, MoveData md)
        {
            string code = md.FunctionCode ?? "";
            var types = TypesOf(target);
            string ab = target.Pk.AbilityId(P) ?? "";
            if (target.SubstituteHP > 0 && md.Target != "User") return false;
            if (code.EndsWith("ConfuseTarget")) return target.Confusion <= 0 && ab != "OWNTEMPO";
            if (!string.IsNullOrEmpty(target.Pk.Status)) return false;
            if (code.Contains("Sleep")) return ab != "INSOMNIA" && ab != "VITALSPIRIT" && ab != "SWEETVEIL" && !(md.Flags != null && md.Flags.Contains("Powder") && types.Contains("GRASS"));
            if (code.Contains("Paralyze")) return !types.Contains("ELECTRIC") && ab != "LIMBER" && !(md.Type == "ELECTRIC" && types.Contains("GROUND"));
            if (code.Contains("Burn")) return !types.Contains("FIRE") && ab != "WATERVEIL" && ab != "WATERBUBBLE";
            if (code.Contains("Poison")) return !types.Contains("POISON") && !types.Contains("STEEL") && ab != "IMMUNITY";
            return true;
        }

        BattleCommand SmartAI(Battler me, Battler foe, List<int> usable, int skill)
        {
            int best = usable[0]; double bestScore = double.MinValue;
            int maxHp = Math.Max(1, me.Pk.TotalHP(P));
            double myHp = me.Pk.HP / (double)maxHp;
            int foesLeft = Parties[foe.Side].Count(Able);
            foreach (int i in usable)
            {
                var md = Move(me.Pk.Moves[i].Id);
                double score = 0;
                if (md == null) score = 0;
                else if (md.Category != "Status")
                {
                    double est = EstimateDamage(me, foe, md, skill);
                    double pct = est / Math.Max(1, foe.Pk.HP);
                    score = Math.Min(120, pct * 100);
                    if (pct >= 1) score += 60 + (md.Priority > 0 ? 40 : 0);   // a KO, faster with priority
                    if (est <= 0) score = -50;                                  // wouldn't affect it
                    string code = md.FunctionCode ?? "";
                    if (code.StartsWith("RecoilHalf") || code.StartsWith("RecoilThird")) score *= myHp > 0.5 ? 0.9 : 0.7;
                    if (code.Contains("TwoTurn") || code.StartsWith("AttackAndSkipNextTurn")) score *= 0.7;
                    if (code == "UserFaintsExplosive") score = myHp < 0.25 && pct >= 0.8 ? score : -20;
                }
                else
                {
                    string code = md.FunctionCode ?? "";
                    if (IsStatusMove(md) || code.EndsWith("ConfuseTarget") || code.Contains("Toxic")) score = StatusWouldWork(me, foe, md) ? (code.Contains("Sleep") ? 55 : 38) : -40;
                    else if (code.StartsWith("HealUser")) score = myHp < 0.35 ? 70 : myHp < 0.6 ? 30 : -20;
                    else if (code.StartsWith("RaiseUser")) score = myHp > 0.7 && me.Stages.Skip(1).Take(5).Max() < 2 ? 32 : 0;
                    else if (code.StartsWith("Protect")) score = me.ProtectedLast ? -30 : 8 + (!string.IsNullOrEmpty(foe.Pk.Status) ? 15 : 0);
                    else if (code.StartsWith("AddSpikes") || code.StartsWith("AddStealthRocks") || code.StartsWith("AddToxicSpikes") || code.StartsWith("AddStickyWeb")) score = foesLeft > 1 ? 30 : -10;
                    else if (code.StartsWith("LowerTarget")) score = 14;
                    else if (code.StartsWith("Start") && code.EndsWith("Weather")) score = 18;
                    else score = 12;
                }
                if (skill < 100) score *= 0.85 + Rng.NextDouble() * 0.3;
                else score += Rng.NextDouble(); // tie-breaking only
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return BattleCommand.Fight(best);
        }

        /// <summary>Skill 48+: switch out when nothing it has does much and a benched Pokémon matches up far better.</summary>
        BattleCommand AISwitch(Battler me, int skill)
        {
            if (skill < 48 || IsWild || me.Side != 1 || IsTrapped(me) || FacilityRule != null) return null;
            var foes = Live(0);
            if (foes.Count == 0) return null;
            var foe = foes[0];
            Func<Pokemon, double> matchup = pk =>
            {
                // Best effectiveness of its moves against the foe minus how hard the foe's types hit it
                double attack = pk.Moves.Select(m => Move(m.Id)).Where(m => m != null && m.Category != "Status").Select(m => Effectiveness(m.Type, foe.Pk) * (pk.Types(P).Contains(m.Type) ? 1.5 : 1)).DefaultIfEmpty(0).Max();
                double defense = TypesOf(foe).Select(t => Effectiveness(t, pk)).DefaultIfEmpty(1).Max();
                return attack - defense;
            };
            double now = matchup(me.Pk);
            double bestDmg = me.Pk.Moves.Where(m => m.PP > 0).Select(m => EstimateDamage(me, foe, Move(m.Id), skill)).DefaultIfEmpty(0).Max() / Math.Max(1, foe.Pk.HP);
            if (now > -0.5 || bestDmg > 0.25) return null;
            int bestIdx = -1; double bestVal = now + 1.5;
            for (int i = 0; i < Parties[1].Count; i++)
            {
                var pk = Parties[1][i];
                if (!Able(pk) || OnFieldParty(1, i)) continue;
                double v = matchup(pk);
                if (v > bestVal) { bestVal = v; bestIdx = i; }
            }
            if (bestIdx < 0 || Rng.Next(100) >= 60) return null;
            var cmd = BattleCommand.Switch(bestIdx); cmd.Slot = me.Slot;
            return cmd;
        }
    }
}
