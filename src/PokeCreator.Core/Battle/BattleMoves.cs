using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;

namespace PokeCreator.Battles
{
    /// <summary>
    /// Move effects beyond damage and stat changes: screens, terrains, Taunt, Substitute, Leech Seed, Wish, Perish Song,
    /// variable power and type, Knock Off, U-turn, Counter... (Essentials function codes).
    /// </summary>
    public partial class Battle
    {
        // ------------------------------------------------------------------ side and field state
        public readonly int[] Reflect = new int[2], LightScreen = new int[2], AuroraVeil = new int[2], Tailwind = new int[2], Safeguard = new int[2], Mist = new int[2];
        public int TrickRoom, Gravity, TerrainTurns;
        /// <summary>Entry hazards on each side: Spikes layers (0-3), Toxic Spikes layers (0-2), Stealth Rock, Sticky Web.</summary>
        public readonly int[] Spikes = new int[2], ToxicSpikes = new int[2];
        public readonly bool[] StealthRock = new bool[2], StickyWeb = new bool[2];
        /// <summary>"", "Electric", "Grassy", "Misty", "Psychic".</summary>
        public string Terrain = "";
        readonly Dictionary<int, int> _wishHeal = new Dictionary<int, int>();   // side*2+slot -> HP healed at the end of next turn
        readonly Dictionary<int, int> _wishTurns = new Dictionary<int, int>();
        class DelayedAttack { public int Side, Slot, Turns, Damage; public string Move; public string User; }
        readonly List<DelayedAttack> _delayed = new List<DelayedAttack>();
        /// <summary>Follow Me / Rage Powder: moves aimed at this side go to this Pokémon this turn.</summary>
        readonly Battler[] _centerOfAttention = new Battler[2];
        string _lastMoveInBattle;

        bool IsGrounded(Battler b)
        {
            if (b.Grounded || Gravity > 0) return true;
            if (b.AirborneTurns > 0 || TypesOf(b).Contains("FLYING") || HasAbility(b, "LEVITATE") || b.Pk.Item == "AIRBALLOON") return false;
            return true;
        }

        static string SideName(int side, bool isWild) { return side == 0 ? "your team" : (isWild ? "the wild Pokémon" : "the opposing team"); }

        int ScreenTurns(Battler user) { return user.Pk.Item == "LIGHTCLAY" ? 8 : 5; }

        bool StartTerrain(string t, Battler user)
        {
            if (Terrain == t) { Say("But it failed!"); return false; }
            Terrain = t; TerrainTurns = user != null && user.Pk.Item == "TERRAINEXTENDER" ? 8 : 5;
            switch (t)
            {
                case "Electric": Say("An electric current ran across the battlefield!"); break;
                case "Grassy": Say("Grass grew to cover the battlefield!"); break;
                case "Misty": Say("Mist swirled around the battlefield!"); break;
                case "Psychic": Say("The battlefield got weird!"); break;
            }
            return true;
        }

        /// <summary>Damage multiplier from screens, terrains and Helping Hand.</summary>
        double FieldDamageMod(Battler user, Battler target, MoveData md, string type, bool crit)
        {
            double m = 1;
            bool physical = md.Category == "Physical";
            if (!crit && user.Side != target.Side && !HasAbility(user, "INFILTRATOR"))
            {
                bool screen = AuroraVeil[target.Side] > 0 || (physical ? Reflect[target.Side] > 0 : LightScreen[target.Side] > 0);
                if (screen) m *= IsDouble ? 2.0 / 3 : 0.5;
            }
            if (user.HelpingHand) m *= 1.5;
            if (IsGrounded(user))
            {
                if (Terrain == "Electric" && type == "ELECTRIC") m *= 1.3;
                if (Terrain == "Grassy" && type == "GRASS") m *= 1.3;
                if (Terrain == "Psychic" && type == "PSYCHIC") m *= 1.3;
            }
            if (Terrain == "Misty" && type == "DRAGON" && IsGrounded(target)) m *= 0.5;
            if (MudSport > 0 && type == "ELECTRIC") m /= 3;
            if (WaterSport > 0 && type == "FIRE") m /= 3;
            if (Terrain == "Grassy" && (md.Id == "EARTHQUAKE" || md.Id == "BULLDOZE" || md.Id == "MAGNITUDE")) m *= 0.5;
            return m;
        }

        // ------------------------------------------------------------------ power and type
        /// <summary>Base power for moves whose power varies (Return, Low Kick, Gyro Ball, Stored Power...); -1 = not one of them.</summary>
        int SpecialPower(Battler user, Battler target, MoveData md, string code)
        {
            var tw = target.Pk.Data(P); var uw = user.Pk.Data(P);
            double tWeight = tw != null && tw.Weight > 0 ? tw.Weight : 30, uWeight = uw != null && uw.Weight > 0 ? uw.Weight : 30;
            if (HasAbility(target, "HEAVYMETAL")) tWeight *= 2; if (HasAbility(target, "LIGHTMETAL")) tWeight /= 2;
            if (HasAbility(user, "HEAVYMETAL")) uWeight *= 2; if (HasAbility(user, "LIGHTMETAL")) uWeight /= 2;
            if (Holds(target, "FLOATSTONE")) tWeight /= 2; if (Holds(user, "FLOATSTONE")) uWeight /= 2;
            switch (code)
            {
                case "PowerHigherWithUserHappiness": return Math.Max(1, user.Pk.Happiness * 2 / 5);
                case "PowerLowerWithUserHappiness": return Math.Max(1, (255 - user.Pk.Happiness) * 2 / 5);
                case "TypeDependsOnUserIVs": return 60;
                case "PowerHigherWithTargetWeight":
                    return tWeight >= 200 ? 120 : tWeight >= 100 ? 100 : tWeight >= 50 ? 80 : tWeight >= 25 ? 60 : tWeight >= 10 ? 40 : 20;
                case "PowerHigherWithUserHeavierThanTarget":
                    {
                        double r = uWeight / Math.Max(0.1, tWeight);
                        return r >= 5 ? 120 : r >= 4 ? 100 : r >= 3 ? 80 : r >= 2 ? 60 : 40;
                    }
                case "PowerHigherWithTargetFasterThanUser": return Math.Max(1, Math.Min(150, 25 * Speed(target) / Math.Max(1, Speed(user)) + 1));
                case "PowerHigherWithUserFasterThanTarget":
                    {
                        double r = Speed(user) / (double)Math.Max(1, Speed(target));
                        return r >= 4 ? 150 : r >= 3 ? 120 : r >= 2 ? 80 : r >= 1 ? 60 : 40;
                    }
                case "PowerHigherWithUserPositiveStatStages": return 20 + 20 * user.Stages.Take(8).Where(x => x > 0).Sum();
                case "PowerHigherWithTargetPositiveStatStages": return Math.Min(200, 60 + 20 * target.Stages.Take(8).Where(x => x > 0).Sum());
                case "RandomPowerDoublePowerIfTargetUnderground":
                    {
                        int r = Rng.Next(100);
                        int mag = r < 5 ? 4 : r < 15 ? 5 : r < 35 ? 6 : r < 65 ? 7 : r < 85 ? 8 : r < 95 ? 9 : 10;
                        Say("Magnitude " + mag + "!");
                        return new[] { 10, 30, 50, 70, 90, 110, 150 }[mag - 4];
                    }
                case "PowerHigherWithLessPP":
                    {
                        var pm = user.Pk.Moves.FirstOrDefault(m => m.Id == md.Id);
                        int pp = pm != null ? pm.PP : 0;
                        return pp >= 4 ? 40 : pp == 3 ? 50 : pp == 2 ? 60 : pp == 1 ? 80 : 200;
                    }
                case "PowerDependsOnUserStockpile": return Math.Max(1, user.StockpileCount * 100);
                case "MultiTurnAttackPowersUpEachTurn": return md.Power * (1 << Math.Min(4, user.RolloutCount));
                case "PowerHigherWithConsecutiveUse": return Math.Min(160, md.Power * (1 << Math.Min(3, user.FuryCutterCount)));
            }
            return MoreSpecialPower(user, target, md, code);
        }

        /// <summary>Power multiplier from the situation: Hex, Facade, Brine, Payback, Earthquake on Dig...</summary>
        double SituationPower(Battler user, Battler target, MoveData md, string code)
        {
            string ts = (target.Pk.Status ?? "").ToUpperInvariant(), us = (user.Pk.Status ?? "").ToUpperInvariant();
            switch (code)
            {
                case "DoublePowerIfTargetStatusProblem": return ts.Length > 0 ? 2 : 1;
                case "DoublePowerIfUserPoisonedBurnedParalyzed": return us == "POISON" || us == "BURN" || us == "PARALYSIS" ? 2 : 1;
                case "DoublePowerIfTargetPoisoned": return ts == "POISON" ? 2 : 1;
                case "DoublePowerIfTargetHPLessThanHalf": return target.Pk.HP * 2 <= target.Pk.TotalHP(P) ? 2 : 1;
                case "DoublePowerIfUserHasNoItem": return string.IsNullOrEmpty(user.Pk.Item) ? 2 : 1;
                case "DoublePowerIfTargetActed": return target.ActedThisTurn ? 2 : 1;
                case "DoublePowerIfTargetNotActed": return !target.ActedThisTurn ? 2 : 1;
                case "DoublePowerIfUserLostHPThisTurn": return user.TookDamageThisTurn ? 2 : 1;
                case "DoublePowerIfTargetLostHPThisTurn": return target.TookDamageThisTurn ? 2 : 1;
                case "DoublePowerIfUserStatsLoweredThisTurn": return user.StatsLoweredThisTurn ? 2 : 1;
                case "DoublePowerIfUserLastMoveFailed": return user.LastMoveFailed ? 2 : 1;
                case "DoublePowerIfTargetAsleepCureTarget": return ts == "SLEEP" ? 2 : 1;
                case "DoublePowerIfTargetParalyzedCureTarget": return ts == "PARALYSIS" ? 2 : 1;
                case "DoublePowerIfTargetUnderground": case "RandomPowerDoublePowerIfTargetUnderground": return target.Charging == "DIG" ? 2 : 1;
                case "DoublePowerIfTargetUnderwater": return target.Charging == "DIVE" ? 2 : 1;
                case "DoublePowerIfTargetInSky": return target.Charging == "FLY" || target.Charging == "BOUNCE" || target.Charging == "SKYDROP" ? 2 : 1;
                case "DoublePowerInElectricTerrain": return Terrain == "Electric" && IsGrounded(target) ? 2 : 1;
                case "RemoveTargetItem": return !string.IsNullOrEmpty(target.Pk.Item) ? 1.5 : 1;   // Knock Off
                case "HitsAllFoesAndPowersUpInPsychicTerrain": return Terrain == "Psychic" && IsGrounded(user) ? 1.5 : 1;
            }
            return MoreSituationPower(user, target, md, code);
        }

        /// <summary>Moves whose type changes: Hidden Power, Judgment, Revelation Dance, Terrain Pulse...</summary>
        string SpecialType(Battler user, MoveData md, string code)
        {
            switch (code)
            {
                case "TypeDependsOnUserIVs":
                    {
                        string[] types = { "FIGHTING", "FLYING", "POISON", "GROUND", "ROCK", "BUG", "GHOST", "STEEL", "FIRE", "WATER", "GRASS", "ELECTRIC", "PSYCHIC", "ICE", "DRAGON", "DARK" };
                        var iv = user.Pk.IV;
                        int bits = (iv[0] & 1) | ((iv[1] & 1) << 1) | ((iv[2] & 1) << 2) | ((iv[5] & 1) << 3) | ((iv[3] & 1) << 4) | ((iv[4] & 1) << 5);
                        return types[bits * 15 / 63];
                    }
                case "TypeIsUserFirstType": { var t = TypesOf(user); return t.Count > 0 ? t[0] : md.Type; }
                case "TypeAndPowerDependOnTerrain":
                    switch (Terrain) { case "Electric": return "ELECTRIC"; case "Grassy": return "GRASS"; case "Misty": return "FAIRY"; case "Psychic": return "PSYCHIC"; }
                    break;
                case "TypeDependsOnUserPlate":
                    {
                        var plate = user.Pk.Item ?? "";
                        var map = new Dictionary<string, string> { { "FLAMEPLATE", "FIRE" }, { "SPLASHPLATE", "WATER" }, { "ZAPPLATE", "ELECTRIC" }, { "MEADOWPLATE", "GRASS" }, { "ICICLEPLATE", "ICE" }, { "FISTPLATE", "FIGHTING" }, { "TOXICPLATE", "POISON" }, { "EARTHPLATE", "GROUND" }, { "SKYPLATE", "FLYING" }, { "MINDPLATE", "PSYCHIC" }, { "INSECTPLATE", "BUG" }, { "STONEPLATE", "ROCK" }, { "SPOOKYPLATE", "GHOST" }, { "DRACOPLATE", "DRAGON" }, { "DREADPLATE", "DARK" }, { "IRONPLATE", "STEEL" }, { "PIXIEPLATE", "FAIRY" } };
                        string t; if (map.TryGetValue(plate, out t)) return t;
                        break;
                    }
            }
            return MoreSpecialType(user, md, code, md.Type);
        }

        /// <summary>Two-turn moves that the target can still be hit through (Earthquake on Dig, Thunder on Fly...).</summary>
        bool HitsInvulnerable(Battler target, string code)
        {
            string c = target.Charging ?? "";
            if (c == "DIG") return code.Contains("TargetUnderground");
            if (c == "DIVE") return code.Contains("TargetUnderwater");
            if (c == "FLY" || c == "BOUNCE" || c == "SKYDROP") return code.Contains("InSky") || code.Contains("TargetInSky");
            return false;
        }

        // ------------------------------------------------------------------ status moves
        /// <summary>Status moves handled here; returns false if the code isn't one of them.</summary>
        bool SpecialStatusMove(Battler user, Battler target, MoveData md, string code)
        {
            string n = Name(user), tn = Name(target);
            int side = user.Side, foeSide = 1 - user.Side;
            switch (code)
            {
                // Screens and side effects
                case "StartWeakenPhysicalDamageAgainstUserSide":
                    if (Reflect[side] > 0) { Say("But it failed!"); return true; }
                    Reflect[side] = ScreenTurns(user); Say("Reflect made " + SideName(side, IsWild) + " stronger against physical moves!"); return true;
                case "StartWeakenSpecialDamageAgainstUserSide":
                    if (LightScreen[side] > 0) { Say("But it failed!"); return true; }
                    LightScreen[side] = ScreenTurns(user); Say("Light Screen made " + SideName(side, IsWild) + " stronger against special moves!"); return true;
                case "StartWeakenDamageAgainstUserSideIfHail":
                    if (AuroraVeil[side] > 0 || EffWeather != Hail) { Say("But it failed!"); return true; }
                    AuroraVeil[side] = ScreenTurns(user); Say("Aurora Veil made " + SideName(side, IsWild) + " stronger against physical and special moves!"); return true;
                case "StartUserSideDoubleSpeed":
                    if (Tailwind[side] > 0) { Say("But it failed!"); return true; }
                    Tailwind[side] = 4; Say("The Tailwind blew from behind " + SideName(side, IsWild) + "!"); return true;
                case "StartUserSideImmunityToInflictedStatus":
                    if (Safeguard[side] > 0) { Say("But it failed!"); return true; }
                    Safeguard[side] = 5; Say(Cap(SideName(side, IsWild)) + " cloaked itself in a mystical veil!"); return true;
                case "StartUserSideImmunityToStatStageLowering":
                    if (Mist[side] > 0) { Say("But it failed!"); return true; }
                    Mist[side] = 5; Say(Cap(SideName(side, IsWild)) + " became shrouded in mist!"); return true;
                case "StartSlowerBattlersActFirst":
                    if (TrickRoom > 0) { TrickRoom = 0; Say("The twisted dimensions returned to normal!"); }
                    else { TrickRoom = 5; Say(n + " twisted the dimensions!"); }
                    return true;
                case "StartGravity":
                    if (Gravity > 0) { Say("But it failed!"); return true; }
                    Gravity = 5; Say("Gravity intensified!");
                    foreach (var b in OnField) if (b.Charging == "FLY" || b.Charging == "BOUNCE") { b.Charging = null; b.Invulnerable = false; }
                    return true;
                // Entry hazards
                case "AddSpikesToFoeSide":
                    if (Spikes[foeSide] >= 3) { Say("But it failed!"); return true; }
                    Spikes[foeSide]++; Say("Spikes were scattered on the ground all around " + SideName(foeSide, IsWild) + "!"); return true;
                case "AddToxicSpikesToFoeSide":
                    if (ToxicSpikes[foeSide] >= 2) { Say("But it failed!"); return true; }
                    ToxicSpikes[foeSide]++; Say("Poison spikes were scattered on the ground all around " + SideName(foeSide, IsWild) + "!"); return true;
                case "AddStealthRocksToFoeSide":
                    if (StealthRock[foeSide]) { Say("But it failed!"); return true; }
                    StealthRock[foeSide] = true; Say("Pointed stones float in the air around " + SideName(foeSide, IsWild) + "!"); return true;
                case "AddStickyWebToFoeSide":
                    if (StickyWeb[foeSide]) { Say("But it failed!"); return true; }
                    StickyWeb[foeSide] = true; Say("A sticky web has been laid out on the ground around " + SideName(foeSide, IsWild) + "!"); return true;
                case "LowerTargetEvasion1RemoveSideEffects":
                    ChangeStat(target, 7, -1, user);
                    Reflect[target.Side] = LightScreen[target.Side] = AuroraVeil[target.Side] = Safeguard[target.Side] = Mist[target.Side] = 0;
                    if (ClearHazards(0) | ClearHazards(1)) Say("The hazards were blown away!");
                    if (Terrain != "") { Terrain = ""; TerrainTurns = 0; }
                    return true;
                case "SwapSideEffects":
                    foreach (var arr in new[] { Reflect, LightScreen, AuroraVeil, Tailwind, Safeguard, Mist, Spikes, ToxicSpikes }) { int t = arr[0]; arr[0] = arr[1]; arr[1] = t; }
                    { bool t1 = StealthRock[0]; StealthRock[0] = StealthRock[1]; StealthRock[1] = t1; t1 = StickyWeb[0]; StickyWeb[0] = StickyWeb[1]; StickyWeb[1] = t1; }
                    Say(n + " swapped the battle effects affecting each side of the field!"); return true;
                case "SwitchOutUserPassOnEffects":
                    if ((user.Side == 1 ? FoeBench(user.Slot) : Bench(user.Side)).Count == 0 || IsWild && user.Side == 1) { Say("But it failed!"); return true; }
                    user.BatonPassing = true; user.SwitchOutPending = true; return true;
                case "SwitchOutUserStatusMove":
                    if (IsWild && user.Side == 1) { Outcome = BattleOutcome.Escaped; Say(n + " fled from battle!"); _ev.Add(new BattleEvent { Kind = BattleEventKind.Flee }); return true; }
                    if ((user.Side == 1 ? FoeBench(user.Slot) : Bench(user.Side)).Count == 0) { if (IsWild) { Outcome = BattleOutcome.Escaped; Say(n + " fled from battle!"); _ev.Add(new BattleEvent { Kind = BattleEventKind.Flee }); } else Say("But it failed!"); return true; }
                    user.SwitchOutPending = true; return true;

                case "StartElectricTerrain": StartTerrain("Electric", user); return true;
                case "StartGrassyTerrain": StartTerrain("Grassy", user); return true;
                case "StartMistyTerrain": StartTerrain("Misty", user); return true;
                case "StartPsychicTerrain": StartTerrain("Psychic", user); return true;

                // Effects on the target
                case "DisableTargetStatusMoves":
                    if (target.TauntTurns > 0 || TargetHas(user, target, "OBLIVIOUS")) { Say("But it failed!"); return true; }
                    target.TauntTurns = 3; Say(tn + " fell for the taunt!"); return true;
                case "DisableTargetUsingDifferentMove":
                    if (target.EncoreTurns > 0 || string.IsNullOrEmpty(target.LastMoveUsed) || !target.Pk.Moves.Any(m => m.Id == target.LastMoveUsed && m.PP > 0)) { Say("But it failed!"); return true; }
                    target.EncoreTurns = 3; target.EncoreMove = target.LastMoveUsed; Say(tn + " must do an encore!"); return true;
                case "DisableTargetLastMoveUsed":
                    if (target.DisableTurns > 0 || string.IsNullOrEmpty(target.LastMoveUsed)) { Say("But it failed!"); return true; }
                    target.DisableTurns = 4; target.DisableMove = target.LastMoveUsed;
                    Say(tn + "'s " + MoveNameOf(target.LastMoveUsed) + " was disabled!"); return true;
                case "StartLeechSeedTarget":
                    if (target.LeechSeeded || TypesOf(target).Contains("GRASS") || target.SubstituteHP > 0) { Say("It doesn't affect " + NameL(target) + "..."); return true; }
                    target.LeechSeeded = true; Say(tn + " was seeded!"); return true;
                case "TrapTargetInBattle":
                    if (target.Trapped) { Say("But it failed!"); return true; }
                    target.Trapped = true; Say(tn + " can no longer escape!"); return true;
                case "AttractTarget":
                    {
                        bool opposite = (user.Pk.Gender == "M" && target.Pk.Gender == "F") || (user.Pk.Gender == "F" && target.Pk.Gender == "M");
                        if (!opposite || target.AttractedTo != null || TargetHas(user, target, "OBLIVIOUS")) { Say("But it failed!"); return true; }
                        target.AttractedTo = user; Say(tn + " fell in love!"); DestinyKnot(target, user); return true;
                    }
                case "StartNegateTargetEvasionStatStageAndGhostImmunity":
                case "StartNegateTargetEvasionStatStageAndDarkImmunity":
                    target.Foresight = true; if (target.Stages[7] > 0) target.Stages[7] = 0; Say(n + " identified " + NameL(target) + "!"); return true;
                case "EnsureNextMoveAlwaysHits":
                    user.LockOnTurns = 2; Say(n + " took aim at " + NameL(target) + "!"); return true;
                case "CurseTargetOrLowerUserSpd1RaiseUserAtkDef1":
                    if (TypesOf(user).Contains("GHOST"))
                    {
                        if (target.Cursed) { Say("But it failed!"); return true; }
                        int f = user.Pk.HP; user.Pk.HP = Math.Max(0, user.Pk.HP - user.Pk.TotalHP(P) / 2); Hp(user, f);
                        target.Cursed = true; Say(n + " cut its own HP and put a curse on " + NameL(target) + "!");
                        return true;
                    }
                    ChangeStat(user, PokeCreator.Pkmn.Stat.Speed, -1); ChangeStat(user, PokeCreator.Pkmn.Stat.Attack, 1); ChangeStat(user, PokeCreator.Pkmn.Stat.Defense, 1);
                    return true;
                case "StartDamageTargetEachTurnIfTargetAsleep":
                    if ((target.Pk.Status ?? "").ToUpperInvariant() != "SLEEP" || target.Nightmare) { Say("But it failed!"); return true; }
                    target.Nightmare = true; Say(tn + " began having a nightmare!"); return true;
                case "LowerPPOfTargetLastMoveBy4":
                case "LowerPPOfTargetLastMoveBy3":
                    {
                        var pm = target.Pk.Moves.FirstOrDefault(m => m.Id == target.LastMoveUsed);
                        if (pm == null || pm.PP == 0) { Say("But it failed!"); return true; }
                        int cut = Math.Min(pm.PP, code.EndsWith("4") ? 4 : 3); pm.PP -= cut;
                        Say("It reduced the PP of " + NameL(target) + "'s " + MoveNameOf(pm.Id) + " by " + cut + "!"); return true;
                    }
                case "UserTargetAverageHP":
                    {
                        int avg = (user.Pk.HP + target.Pk.HP) / 2;
                        int fu = user.Pk.HP, ft = target.Pk.HP;
                        user.Pk.HP = Math.Min(user.Pk.TotalHP(P), avg); target.Pk.HP = Math.Min(target.Pk.TotalHP(P), avg);
                        Hp(user, fu); Hp(target, ft); Say("The battlers shared their pain!"); return true;
                    }
                case "UserCopyTargetStatStages":
                    for (int i = 0; i < 8; i++) user.Stages[i] = target.Stages[i];
                    Say(n + " copied " + NameL(target) + "'s stat changes!"); return true;
                case "UserTargetSwapStatStages":
                    for (int i = 1; i < 8; i++) { int t = user.Stages[i]; user.Stages[i] = target.Stages[i]; target.Stages[i] = t; }
                    Say(n + " switched stat changes with the target!"); return true;
                case "UserTargetSwapAtkSpAtkStages":
                    foreach (int i in new[] { 1, 3 }) { int t = user.Stages[i]; user.Stages[i] = target.Stages[i]; target.Stages[i] = t; }
                    Say(n + " switched all changes to its Attack and Sp. Atk with the target!"); return true;
                case "UserTargetSwapDefSpDefStages":
                    foreach (int i in new[] { 2, 4 }) { int t = user.Stages[i]; user.Stages[i] = target.Stages[i]; target.Stages[i] = t; }
                    Say(n + " switched all changes to its Defense and Sp. Def with the target!"); return true;
                case "InvertTargetStatStages":
                    for (int i = 1; i < 8; i++) target.Stages[i] = -target.Stages[i];
                    Say(tn + "'s stat changes were all reversed!"); return true;
                case "NegateTargetAbility":
                    target.AbilitySuppressed = true; Say(tn + "'s Ability was suppressed!"); return true;
                case "SetTargetAbilityToInsomnia": target.AbilityOverride = "INSOMNIA"; Say(tn + " acquired Insomnia!"); return true;
                case "SetTargetAbilityToSimple": target.AbilityOverride = "SIMPLE"; Say(tn + " acquired Simple!"); return true;
                case "SetTargetAbilityToUserAbility": target.AbilityOverride = AbilityOf(user); Say(tn + " acquired " + AbilityName(user) + "!"); return true;
                case "SetUserAbilityToTargetAbility": user.AbilityOverride = AbilityOf(target); Say(n + " copied " + NameL(target) + "'s " + AbilityName(target) + "!"); return true;
                case "UserTargetSwapAbilities":
                    {
                        string a1 = AbilityOf(user), a2 = AbilityOf(target);
                        user.AbilityOverride = a2; target.AbilityOverride = a1;
                        Say(n + " swapped Abilities with its target!"); return true;
                    }
                case "GiveUserStatusToTarget":
                    {
                        string st = (user.Pk.Status ?? "").ToUpperInvariant();
                        if (st.Length == 0 || !CanStatus(target, st, false, user)) { Say("But it failed!"); return true; }
                        Inflict(target, st, false, user);
                        user.Pk.Status = ""; user.ToxicCount = 0; StatusEvent(user); Say(n + " was cured of its status condition.");
                        return true;
                    }
                case "LowerPoisonedTargetAtkSpAtkSpd1":
                    if ((target.Pk.Status ?? "").ToUpperInvariant() != "POISON") { Say("But it failed!"); return true; }
                    ChangeStat(target, PokeCreator.Pkmn.Stat.Attack, -1, user); ChangeStat(target, PokeCreator.Pkmn.Stat.SpAtk, -1, user); ChangeStat(target, PokeCreator.Pkmn.Stat.Speed, -1, user);
                    return true;
                case "StartPerishCountsForAllBattlers":
                    foreach (var b in OnField.Where(x => !x.Pk.Fainted && x.PerishCount < 0 && !HasAbility(x, "SOUNDPROOF"))) b.PerishCount = 3;
                    Say("All Pokémon that heard the song will faint in three turns!"); return true;
                case "PowerUpAllyMove":
                    {
                        var ally = Ally(user);
                        if (ally == null) { Say("But it failed!"); return true; }
                        ally.HelpingHand = true; Say(n + " is ready to help " + NameL(ally) + "!"); return true;
                    }
                case "RedirectAllMovesToUser":
                    if (!IsDouble) { Say("But it failed!"); return true; }
                    _centerOfAttention[side] = user; Say(n + " became the center of attention!"); return true;
                case "StartTargetAirborneAndAlwaysHitByMoves":
                    target.AirborneTurns = 3; Say(tn + " was hurled into the air!"); return true;
                case "StartUserAirborne":
                    if (user.AirborneTurns > 0 || Gravity > 0) { Say("But it failed!"); return true; }
                    user.AirborneTurns = 5; Say(n + " levitated with electromagnetism!"); return true;

                // Effects on the user
                case "UserMakeSubstitute":
                    {
                        int cost = user.Pk.TotalHP(P) / 4;
                        if (user.SubstituteHP > 0) { Say(n + " already has a substitute!"); return true; }
                        if (user.Pk.HP <= cost || cost < 1) { Say("But it does not have enough HP left to make a substitute!"); return true; }
                        int f = user.Pk.HP; user.Pk.HP -= cost; Hp(user, f);
                        user.SubstituteHP = cost; user.BindTurns = 0;
                        Say(n + " put in a substitute!"); return true;
                    }
                case "MaxUserAttackLoseHalfOfTotalHP":
                    {
                        int half = user.Pk.TotalHP(P) / 2;
                        if (user.Pk.HP <= half || user.Stages[PokeCreator.Pkmn.Stat.Attack] >= 6) { Say("But it failed!"); return true; }
                        int f = user.Pk.HP; user.Pk.HP -= half; Hp(user, f);
                        user.Stages[PokeCreator.Pkmn.Stat.Attack] = 6;
                        _ev.Add(new BattleEvent { Kind = BattleEventKind.StatChange, Side = user.Side, Party = user.PartyIndex, Value = 6 });
                        Say(n + " cut its own HP and maximized its Attack!"); return true;
                    }
                case "UserEnduresFaintingThisTurn":
                    {
                        bool ok = Rng.NextDouble() < Math.Pow(1.0 / 3, user.ProtectedLast ? user.ProtectChain : 0);
                        if (!ok) { user.ProtectChain = 0; Say("But it failed!"); return true; }
                        user.Endure = true; user.Protected = false; user.ProtectChain = user.ProtectedLast ? user.ProtectChain + 1 : 1;
                        Say(n + " braced itself!"); return true;
                    }
                case "AttackerFaintsIfUserFaints":
                    user.DestinyBond = true; Say(n + " is hoping to take its attacker down with it!"); return true;
                case "HealUserPositionNextTurn":
                    {
                        int k = side * 2 + user.Slot;
                        if (_wishTurns.ContainsKey(k)) { Say("But it failed!"); return true; }
                        _wishTurns[k] = 2; _wishHeal[k] = Math.Max(1, user.Pk.TotalHP(P) / 2);
                        Say(n + " made a wish!"); return true;
                    }
                case "CureUserPartyStatus":
                    foreach (var pk in Parties[side]) if (!pk.Fainted) pk.Status = "";
                    foreach (var b in Live(side)) { b.ToxicCount = 0; StatusEvent(b); }
                    Say(md.Id == "HEALBELL" ? "A bell chimed!" : "A soothing aroma wafted through the area!"); return true;
                case "CureUserBurnPoisonParalysis":
                    {
                        string st = (user.Pk.Status ?? "").ToUpperInvariant();
                        if (st != "BURN" && st != "POISON" && st != "PARALYSIS") { Say("But it failed!"); return true; }
                        user.Pk.Status = ""; user.ToxicCount = 0; StatusEvent(user); Say(n + "'s status returned to normal!"); return true;
                    }
                case "StartHealUserEachTurn":
                    if (user.AquaRing) { Say("But it failed!"); return true; }
                    user.AquaRing = true; Say(n + " surrounded itself with a veil of water!"); return true;
                case "StartHealUserEachTurnTrapUserInBattle":
                    if (user.Ingrain) { Say("But it failed!"); return true; }
                    user.Ingrain = true; user.Trapped = true; Say(n + " planted its roots!"); return true;
                case "HealTargetHalfOfTotalHP":
                case "HealTargetDependingOnGrassyTerrain":
                    {
                        int max = target.Pk.TotalHP(P);
                        if (target.Pk.HP >= max) { Say(tn + "'s HP is full!"); return true; }
                        int amt = code == "HealTargetDependingOnGrassyTerrain" && Terrain == "Grassy" ? max * 3 / 4 : (max + 1) / 2;
                        int f = target.Pk.HP; target.Pk.HP = Math.Min(max, target.Pk.HP + amt); Hp(target, f);
                        Say(tn + "'s HP was restored."); return true;
                    }
                case "HealUserAndAlliesQuarterOfTotalHP":
                case "HealUserAndAlliesQuarterOfTotalHPCureStatus":
                    foreach (var b in Live(side))
                    {
                        int max = b.Pk.TotalHP(P);
                        if (b.Pk.HP < max) { int f = b.Pk.HP; b.Pk.HP = Math.Min(max, b.Pk.HP + max / 4); Hp(b, f); Say(Name(b) + "'s HP was restored."); }
                        if (code.EndsWith("CureStatus") && !string.IsNullOrEmpty(b.Pk.Status)) { b.Pk.Status = ""; StatusEvent(b); }
                    }
                    return true;
                case "CureTargetStatusHealUserHalfOfTotalHP":
                    {
                        if (string.IsNullOrEmpty(target.Pk.Status)) { Say("But it failed!"); return true; }
                        target.Pk.Status = ""; StatusEvent(target); Say(tn + "'s status returned to normal!");
                        int max = user.Pk.TotalHP(P); int f = user.Pk.HP; user.Pk.HP = Math.Min(max, user.Pk.HP + max / 2); if (user.Pk.HP > f) Hp(user, f);
                        return true;
                    }
                case "HealUserDependingOnSandstorm":
                    {
                        int max = user.Pk.TotalHP(P);
                        if (user.Pk.HP >= max) { Say(n + "'s HP is full!"); return true; }
                        int f = user.Pk.HP; user.Pk.HP = Math.Min(max, user.Pk.HP + (EffWeather == Sandstorm ? max * 2 / 3 : (max + 1) / 2)); Hp(user, f);
                        Say(n + " had its HP restored."); return true;
                    }
                case "UserAddStockpileRaiseDefSpDef1":
                    if (user.StockpileCount >= 3) { Say(n + " can't stockpile any more!"); return true; }
                    user.StockpileCount++; Say(n + " stockpiled " + user.StockpileCount + "!");
                    ChangeStat(user, PokeCreator.Pkmn.Stat.Defense, 1); ChangeStat(user, PokeCreator.Pkmn.Stat.SpDef, 1);
                    return true;
                case "HealUserDependingOnUserStockpile":
                    {
                        if (user.StockpileCount == 0) { Say("But it failed to swallow a thing!"); return true; }
                        int max = user.Pk.TotalHP(P), amt = user.StockpileCount == 1 ? max / 4 : user.StockpileCount == 2 ? max / 2 : max;
                        int f = user.Pk.HP; user.Pk.HP = Math.Min(max, user.Pk.HP + amt); Hp(user, f);
                        user.StockpileCount = 0; Say(n + "'s HP was restored."); return true;
                    }
                case "DoesNothingFailsIfNoAlly": if (Ally(user) == null) Say("But it failed!"); return true;
                case "DoubleMoneyGainedFromBattle": Say("Everyone is caught up in the happy atmosphere!"); return true;

                // Moves that use other moves
                case "UseRandomMove":
                    {
                        var pool = P.Moves.Where(m => CanBeCalled(m) && m.FunctionCode != "UseRandomMove").ToList();
                        if (pool.Count == 0) { Say("But it failed!"); return true; }
                        var pick = pool[Rng.Next(pool.Count)];
                        Say("Waggling a finger let it use " + (pick.Name ?? pick.Id) + "!");
                        CallMove(user, target, pick);
                        return true;
                    }
                case "UseRandomUserMoveIfAsleep":
                    {
                        if ((user.Pk.Status ?? "").ToUpperInvariant() != "SLEEP") { Say("But it failed!"); return true; }
                        var opts = user.Pk.Moves.Select(m => Move(m.Id)).Where(m => m != null && m.FunctionCode != "UseRandomUserMoveIfAsleep" && !(m.FunctionCode ?? "").StartsWith("TwoTurnAttack")).ToList();
                        if (opts.Count == 0) { Say("But it failed!"); return true; }
                        CallMove(user, target, opts[Rng.Next(opts.Count)]);
                        return true;
                    }
                case "UseLastMoveUsed":
                    {
                        var m = string.IsNullOrEmpty(_lastMoveInBattle) ? null : Move(_lastMoveInBattle);
                        if (m == null || m.FunctionCode == "UseLastMoveUsed") { Say("But it failed!"); return true; }
                        CallMove(user, target, m);
                        return true;
                    }
                case "UseLastMoveUsedByTarget":
                    {
                        var m = string.IsNullOrEmpty(target.LastMoveUsed) ? null : Move(target.LastMoveUsed);
                        if (m == null) { Say("But it failed!"); return true; }
                        CallMove(user, target, m);
                        return true;
                    }
                case "UseRandomMoveFromUserParty":
                    {
                        var opts = Parties[side].Where(pk => pk != user.Pk && !pk.IsEgg).SelectMany(pk => pk.Moves).Select(m => Move(m.Id)).Where(m => m != null && m.FunctionCode != "UseRandomMoveFromUserParty").ToList();
                        if (opts.Count == 0) { Say("But it failed!"); return true; }
                        CallMove(user, target, opts[Rng.Next(opts.Count)]);
                        return true;
                    }
                case "UseMoveDependingOnEnvironment":
                    {
                        string id = Terrain == "Electric" ? "THUNDERBOLT" : Terrain == "Grassy" ? "ENERGYBALL" : Terrain == "Misty" ? "MOONBLAST" : Terrain == "Psychic" ? "PSYCHIC" : "TRIATTACK";
                        var m = Move(id);
                        if (m == null) { Say("But it failed!"); return true; }
                        Say("Nature Power turned into " + (m.Name ?? m.Id) + "!");
                        CallMove(user, target, m);
                        return true;
                    }
            }
            return MoreStatusMoves(user, target, md, code);
        }

        /// <summary>Moves Metronome and friends may call: no Max/Z moves, Struggle or other move-calling moves.</summary>
        static bool CanBeCalled(MoveData m)
        {
            if (m == null || string.IsNullOrEmpty(m.Name) || m.Id == "STRUGGLE") return false;
            if (m.Id.StartsWith("MAX") || m.Id.StartsWith("GMAX") || m.Id.EndsWith("Z")) return false;
            string c = m.FunctionCode ?? "";
            if (c.StartsWith("UseRandom") || c.StartsWith("UseLastMove") || c == "UseMoveDependingOnEnvironment" || c.StartsWith("ProtectUser") || c == "UserEnduresFaintingThisTurn" || c == "TransformUserIntoTarget") return false;
            if (m.Flags != null && m.Flags.Contains("CannotMetronome")) return false;
            return true;
        }

        /// <summary>Effects before a damaging move hits: Brick Break and Psychic Fangs break screens.</summary>
        void BeforeDamage(Battler user, Battler target, MoveData md, string code)
        {
            if (code == "RemoveScreens" && target.Side != user.Side && (Reflect[target.Side] > 0 || LightScreen[target.Side] > 0 || AuroraVeil[target.Side] > 0))
            {
                Reflect[target.Side] = LightScreen[target.Side] = AuroraVeil[target.Side] = 0;
                Say("It shattered the barrier!");
            }
            if (code == "RemoveTerrain" && Terrain != "") { Terrain = ""; TerrainTurns = 0; Say("The battlefield returned to normal."); }
        }

        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }
        string MoveNameOf(string id) { var m = Move(id); return m != null ? m.Name ?? id : id; }

        /// <summary>Uses another move (Metronome, Sleep Talk, Copycat...): its own targets, no PP.</summary>
        int _callDepth;

        void CallMove(Battler user, Battler target, MoveData md)
        {
            // A called move can't call another move (Mirror Move copying Mirror Move would never end)
            if (_callDepth > 0 || !CanBeCalled(md)) { Say("But it failed!"); return; }
            _callDepth++;
            try { CallMoveCore(user, target, md); }
            finally { _callDepth--; }
        }

        void CallMoveCore(Battler user, Battler target, MoveData md)
        {
            string code = md.FunctionCode ?? "None";
            Say(Name(user) + " used " + (md.Name ?? md.Id) + "!");
            _ev.Add(new BattleEvent { Kind = BattleEventKind.MoveUsed, Side = user.Side, Party = user.PartyIndex, Text = md.Id });
            bool selfTarget = md.Target == "User" || md.Target == "UserSide" || md.Target == "BothSides" || md.Target == "UserAndAllies" || md.Target == "UserOrNearAlly";
            if (!selfTarget) target = OpponentFor(user, target != null && target.Side != user.Side ? target : null);
            if (target == null) { Say("But there was no target..."); return; }
            if (!selfTarget && !Hits(user, target, md, code)) { Say(Name(user) + "'s attack missed!"); return; }
            MoveAnim(user, selfTarget ? user : target, md);
            if (md.Category == "Status") StatusMove(user, target, md, code); else DamagingMove(user, target, md, code);
        }

        // ------------------------------------------------------------------ after damage
        /// <summary>Effects of damaging moves that hit (Knock Off, U-turn, Rapid Spin, Smack Down...).</summary>
        void DamagingAfterEffects(Battler user, Battler target, MoveData md, string code, int total)
        {
            if (total <= 0) return;
            switch (code)
            {
                case "RemoveTargetItem":
                    if (!string.IsNullOrEmpty(target.Pk.Item) && !target.Pk.Fainted && !TargetHas(user, target, "STICKYHOLD"))
                    {
                        var it = P.Items.FirstOrDefault(i => i.Id == target.Pk.Item);
                        Say(Name(user) + " knocked off " + NameL(target) + "'s " + (it != null ? it.Name : target.Pk.Item) + "!");
                        if (target.Side == 1) target.Pk.Item = null; // the player's items come back after the battle in the games; here only foes lose theirs
                    }
                    break;
                case "UserTakesTargetItem":
                    if (string.IsNullOrEmpty(user.Pk.Item) && !string.IsNullOrEmpty(target.Pk.Item) && !TargetHas(user, target, "STICKYHOLD") && user.Side == 0 && IsWild)
                    {
                        var it = P.Items.FirstOrDefault(i => i.Id == target.Pk.Item);
                        Say(Name(user) + " stole " + NameL(target) + "'s " + (it != null ? it.Name : target.Pk.Item) + "!");
                        user.Pk.Item = target.Pk.Item; target.Pk.Item = null;
                    }
                    break;
                case "UserConsumeTargetBerry":
                    if (!string.IsNullOrEmpty(target.Pk.Item) && target.Pk.Item.EndsWith("BERRY"))
                    {
                        var it = P.Items.FirstOrDefault(i => i.Id == target.Pk.Item);
                        Say(Name(user) + " stole and ate its target's " + (it != null ? it.Name : target.Pk.Item) + "!");
                        int max = user.Pk.TotalHP(P);
                        if (target.Pk.Item == "SITRUSBERRY" || target.Pk.Item == "ORANBERRY") { int f = user.Pk.HP; user.Pk.HP = Math.Min(max, user.Pk.HP + (target.Pk.Item == "ORANBERRY" ? 10 : max / 4)); if (user.Pk.HP > f) Hp(user, f); }
                        if (target.Side == 1) target.Pk.Item = null;
                    }
                    break;
                case "RemoveUserBindingAndEntryHazards":
                    if (user.BindTurns > 0 || user.LeechSeeded) { user.BindTurns = 0; user.LeechSeeded = false; }
                    if (ClearHazards(user.Side)) Say(Name(user) + " blew away the hazards!");
                    if (!user.Pk.Fainted) ChangeStat(user, PokeCreator.Pkmn.Stat.Speed, 1);
                    break;
                case "HitsTargetInSkyGroundsTarget":
                    if (!target.Pk.Fainted && !IsGrounded(target)) { target.Grounded = true; Say(Name(target) + " fell straight down!"); }
                    if (target.Charging == "FLY" || target.Charging == "BOUNCE") { target.Charging = null; target.Invulnerable = false; }
                    break;
                case "TrapTargetInBattle":
                case "TrapTargetInBattleMainEffect":
                    if (!target.Pk.Fainted && !target.Trapped) { target.Trapped = true; Say(Name(target) + " can no longer escape!"); }
                    break;
                case "TrapUserAndTargetInBattle":
                    if (!target.Pk.Fainted) { target.Trapped = true; user.Trapped = true; Say("Neither Pokémon can run away!"); }
                    break;
                case "ResetTargetStatStages":
                    if (!target.Pk.Fainted && target.Stages.Any(x => x != 0)) { target.Stages = new int[8]; Say(Name(target) + "'s stat changes were removed!"); }
                    break;
                case "CureTargetBurn":
                    if (!target.Pk.Fainted && (target.Pk.Status ?? "").ToUpperInvariant() == "BURN") { target.Pk.Status = ""; StatusEvent(target); Say(Name(target) + "'s burn was healed."); }
                    break;
                case "DoublePowerIfTargetAsleepCureTarget":
                case "DoublePowerIfTargetParalyzedCureTarget":
                    {
                        string want = code.Contains("Asleep") ? "SLEEP" : "PARALYSIS";
                        if (!target.Pk.Fainted && (target.Pk.Status ?? "").ToUpperInvariant() == want) { target.Pk.Status = ""; StatusEvent(target); Say(Name(target) + (want == "SLEEP" ? " woke up!" : " was cured of paralysis.")); }
                        break;
                    }
                case "UserLosesHalfOfTotalHP":
                case "UserLosesHalfOfTotalHPExplosive":
                    if (!user.Pk.Fainted && !HasAbility(user, "MAGICGUARD")) { int f = user.Pk.HP; user.Pk.HP = Math.Max(0, user.Pk.HP - (user.Pk.TotalHP(P) + 1) / 2); Hp(user, f); }
                    break;
                case "SwitchOutUserDamagingMove":
                    if (!user.Pk.Fainted && Bench(user.Side).Count > 0) { if (user.Side == 1 && !IsWild) SwitchOutNow(user); else user.SwitchOutPending = true; }
                    break;
                case "SwitchOutTargetDamagingMove":
                    if (!target.Pk.Fainted && target.SubstituteHP == 0 && !HasAbility(target, "SUCTIONCUPS") && !target.Ingrain)
                    {
                        if (IsWild && target.Side == 1 && !IsDouble) { Outcome = BattleOutcome.Escaped; Say(Name(target) + " was blown away!"); _ev.Add(new BattleEvent { Kind = BattleEventKind.Flee }); }
                        else ForceSwitch(target);
                    }
                    break;
                case "MultiTurnAttackConfuseUserAtEnd":
                    if (user.LockTurns == 0 && user.LockedMove == null) { user.LockedMove = md.Id; user.LockTurns = 1 + Rng.Next(2); }
                    break;
                case "MultiTurnAttackPowersUpEachTurn":
                    if (user.LockedMove == null) { user.LockedMove = md.Id; user.LockTurns = 4; }
                    user.RolloutCount++;
                    break;
                case "PowerHigherWithConsecutiveUse": user.FuryCutterCount++; break;
                case "LowerTargetHPToUserHP": break; // handled as fixed damage
            }
            MoreDamagingAfter(user, target, md, code, total);
        }

        bool ClearHazards(int side)
        {
            bool any = Spikes[side] > 0 || ToxicSpikes[side] > 0 || StealthRock[side] || StickyWeb[side];
            Spikes[side] = ToxicSpikes[side] = 0; StealthRock[side] = StickyWeb[side] = false;
            return any;
        }

        /// <summary>Spikes, Toxic Spikes, Stealth Rock and Sticky Web hurt a Pokémon as it comes in.</summary>
        void EntryHazards(Battler b)
        {
            int s = b.Side, max = b.Pk.TotalHP(P);
            if (b.Pk.Item == "HEAVYDUTYBOOTS") return;
            if (StealthRock[s] && !HasAbility(b, "MAGICGUARD"))
            {
                double eff = EffectivenessOn("ROCK", b);
                int dmg = Math.Max(1, (int)(max * eff / 8));
                if (eff > 0) Residual(b, Math.Min(b.Pk.HP, dmg), "Pointed stones dug into " + NameL(b) + "!");
            }
            if (b.Pk.Fainted) return;
            bool grounded = IsGrounded(b);
            if (Spikes[s] > 0 && grounded && !HasAbility(b, "MAGICGUARD"))
                Residual(b, Math.Max(1, max / (Spikes[s] == 1 ? 8 : Spikes[s] == 2 ? 6 : 4)), Name(b) + " is hurt by the spikes!");
            if (b.Pk.Fainted) return;
            if (ToxicSpikes[s] > 0 && grounded)
            {
                if (TypesOf(b).Contains("POISON")) { ToxicSpikes[s] = 0; Say(Name(b) + " absorbed the poison spikes!"); }
                else if (CanStatus(b, "POISON", false)) Inflict(b, ToxicSpikes[s] >= 2 ? "TOXIC" : "POISON", false);
            }
            if (StickyWeb[s] && grounded) { Say(Name(b) + " was caught in a sticky web!"); ChangeStat(b, PokeCreator.Pkmn.Stat.Speed, -1); }
        }

        /// <summary>Baton Pass: stat changes and some effects carry over to the Pokémon coming in.</summary>
        void PassEffects(Battler from, Battler to)
        {
            for (int i = 0; i < 8; i++) to.Stages[i] = from.Stages[i];
            to.CritStage = from.CritStage; to.Confusion = from.Confusion; to.SubstituteHP = from.SubstituteHP;
            to.LeechSeeded = from.LeechSeeded; to.Cursed = from.Cursed; to.AquaRing = from.AquaRing; to.Ingrain = from.Ingrain; to.Trapped = from.Trapped || from.Ingrain;
            to.PerishCount = from.PerishCount; to.LockOnTurns = from.LockOnTurns; to.AirborneTurns = from.AirborneTurns;
        }

        /// <summary>Dragon Tail / Circle Throw / Roar in trainer battles: the target is replaced by a random bench Pokémon.</summary>
        void ForceSwitch(Battler target)
        {
            var bench = target.Side == 1 ? FoeBench(target.Slot) : Bench(target.Side);
            if (bench.Count == 0) return;
            int party = bench[Rng.Next(bench.Count)];
            _ev.Add(new BattleEvent { Kind = BattleEventKind.Withdraw, Side = target.Side, Party = target.PartyIndex, Slot = target.Slot });
            var nb = Place(target.Side, target.Slot, party);
            Say(Name(nb) + " was dragged out!");
            SendOutEvent(nb);
            MarkParticipant();
            SwitchInAbility(nb);
        }

        /// <summary>U-turn / Volt Switch: the AI switches at once; the player picks a Pokémon after the turn (PlayerSlotsToFill).</summary>
        /// <summary>
        /// Switches a Pokémon out mid-turn (Eject Button, Eject Pack, Emergency Exit, U-turn): an opponent is replaced at once;
        /// the player's Pokémon stops acting this turn and the player picks its replacement after the turn.
        /// </summary>
        void SwitchOutNow(Battler b)
        {
            if (b.Pk.Fainted) return;
            if (b.Side == 1)
            {
                var bench = FoeBench(b.Slot);
                if (bench.Count == 0) return;
                SwitchOutAbility(b);
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Withdraw, Side = 1, Party = b.PartyIndex, Slot = b.Slot });
                var nb = Place(1, b.Slot, AINextPokemon(b.Slot));
                Say(TrainerLabel(nb.PartyIndex) + " sent out " + nb.Pk.Name(P) + "!");
                SendOutEvent(nb); MarkParticipant(); SwitchInAbility(nb);
                return;
            }
            b.SwitchOutPending = true; b.Ejected = true;
        }

        void ResolveSwitchOuts()
        {
            foreach (var b in OnField.Where(x => x.SwitchOutPending && !x.Pk.Fainted).ToList())
            {
                b.SwitchOutPending = false;
                if (b.Side == 1)
                {
                    var bench = FoeBench(b.Slot);
                    if (bench.Count == 0) continue;
                    _ev.Add(new BattleEvent { Kind = BattleEventKind.Withdraw, Side = 1, Party = b.PartyIndex, Slot = b.Slot });
                    var nb = Place(1, b.Slot, AINextPokemon(b.Slot));
                    Say(TrainerLabel(nb.PartyIndex) + " sent out " + nb.Pk.Name(P) + "!");
                    SendOutEvent(nb); MarkParticipant(); SwitchInAbility(nb);
                }
                else if (Bench(0).Count > 0 && !PlayerSlotsToFill.Contains(b.Slot)) PlayerSlotsToFill.Add(b.Slot);
            }
        }

        // ------------------------------------------------------------------ end of turn
        void MoveEffectsEndOfTurn()
        {
            foreach (var b in OnField.Where(x => !x.Pk.Fainted).OrderByDescending(x => Speed(x)).ToList())
            {
                int max = b.Pk.TotalHP(P);
                // Wish
                int k = b.Side * 3 + b.Slot, turns;
                if (_wishTurns.TryGetValue(k, out turns) && --turns <= 0)
                {
                    _wishTurns.Remove(k);
                    int heal = _wishHeal[k]; _wishHeal.Remove(k);
                    if (b.Pk.HP < max) { Say("The wish came true!"); int f = b.Pk.HP; b.Pk.HP = Math.Min(max, b.Pk.HP + heal); Hp(b, f); }
                }
                else if (_wishTurns.ContainsKey(k)) _wishTurns[k] = turns;
                if (Terrain == "Grassy" && IsGrounded(b) && b.Pk.HP < max) { int f = b.Pk.HP; b.Pk.HP = Math.Min(max, b.Pk.HP + Math.Max(1, max / 16)); Hp(b, f); Say(Name(b) + "'s HP was restored."); }
                if ((b.AquaRing || b.Ingrain) && b.Pk.HP < max)
                {
                    int amt = Math.Max(1, max / 16); if (b.Pk.Item == "BIGROOT") amt = amt * 13 / 10;
                    int f = b.Pk.HP; b.Pk.HP = Math.Min(max, b.Pk.HP + amt); Hp(b, f);
                    Say(b.AquaRing ? "A veil of water restored " + NameL(b) + "'s HP!" : Name(b) + " absorbed nutrients with its roots!");
                }
                // Leech Seed: drains 1/8 to the Pokémon opposite
                if (b.LeechSeeded && !HasAbility(b, "MAGICGUARD"))
                {
                    CommonAnim("LeechSeed", b);
                    var sapper = Live(1 - b.Side).FirstOrDefault();
                    int dmg = Math.Min(b.Pk.HP, Math.Max(1, max / 8));
                    Residual(b, dmg, Name(b) + "'s health is sapped by Leech Seed!");
                    if (sapper != null && sapper.Pk.HP < sapper.Pk.TotalHP(P)) { int f = sapper.Pk.HP; sapper.Pk.HP = Math.Min(sapper.Pk.TotalHP(P), sapper.Pk.HP + dmg); Hp(sapper, f); }
                }
                if (b.Pk.Fainted) continue;
                if (b.Nightmare && (b.Pk.Status ?? "").ToUpperInvariant() == "SLEEP") Residual(b, Math.Max(1, max / 4), Name(b) + " is locked in a nightmare!");
                else b.Nightmare = false;
                if (b.Cursed && !b.Pk.Fainted) Residual(b, Math.Max(1, max / 4), Name(b) + " is afflicted by the curse!");
                // Counters
                if (b.TauntTurns > 0 && --b.TauntTurns == 0) Say(Name(b) + "'s taunt wore off!");
                if (b.EncoreTurns > 0 && --b.EncoreTurns == 0) { b.EncoreMove = null; Say(Name(b) + "'s encore ended!"); }
                if (b.DisableTurns > 0 && --b.DisableTurns == 0) { Say(Name(b) + "'s " + MoveNameOf(b.DisableMove) + " is no longer disabled!"); b.DisableMove = null; }
                if (b.LockOnTurns > 0) b.LockOnTurns--;
                if (b.AirborneTurns > 0) b.AirborneTurns--;
                if (b.PerishCount >= 0 && !b.Pk.Fainted)
                {
                    b.PerishCount--;
                    Say(Name(b) + "'s perish count fell to " + Math.Max(0, b.PerishCount) + "!");
                    if (b.PerishCount <= 0) { int f = b.Pk.HP; b.Pk.HP = 0; Hp(b, f); }
                }
            }
            // Future Sight / Doom Desire
            foreach (var d in _delayed.ToList())
            {
                if (--d.Turns > 0) continue;
                _delayed.Remove(d);
                var t = Field[d.Side][d.Slot];
                if (t == null || t.Pk.Fainted) continue;
                var fmd = Move(d.Move);
                if (fmd != null) MoveAnim(t, t, fmd, 1);
                Say(Name(t) + " took the " + MoveNameOf(d.Move) + " attack!");
                int f = t.Pk.HP; t.Pk.HP = Math.Max(0, t.Pk.HP - d.Damage); Hp(t, f);
            }
            // Side and field counters
            for (int s = 0; s < 2; s++)
            {
                if (Reflect[s] > 0 && --Reflect[s] == 0) Say(Cap(SideName(s, IsWild)) + "'s Reflect wore off!");
                if (LightScreen[s] > 0 && --LightScreen[s] == 0) Say(Cap(SideName(s, IsWild)) + "'s Light Screen wore off!");
                if (AuroraVeil[s] > 0 && --AuroraVeil[s] == 0) Say(Cap(SideName(s, IsWild)) + "'s Aurora Veil wore off!");
                if (Tailwind[s] > 0 && --Tailwind[s] == 0) Say(Cap(SideName(s, IsWild)) + "'s Tailwind petered out!");
                if (Safeguard[s] > 0 && --Safeguard[s] == 0) Say(Cap(SideName(s, IsWild)) + " is no longer protected by Safeguard!");
                if (Mist[s] > 0 && --Mist[s] == 0) Say(Cap(SideName(s, IsWild)) + " is no longer protected by mist!");
            }
            if (TrickRoom > 0 && --TrickRoom == 0) Say("The twisted dimensions returned to normal!");
            if (Gravity > 0 && --Gravity == 0) Say("Gravity returned to normal!");
            if (TerrainTurns > 0 && --TerrainTurns == 0) { Say("The battlefield returned to normal."); Terrain = ""; }
            MoreEndOfTurn();
        }

        /// <summary>Future Sight: the damage is worked out now and lands two turns later.</summary>
        void ScheduleDelayed(Battler user, Battler target, MoveData md, int damage)
        {
            if (_delayed.Any(d => d.Side == target.Side && d.Slot == target.Slot)) { Say("But it failed!"); return; }
            _delayed.Add(new DelayedAttack { Side = target.Side, Slot = target.Slot, Turns = 3, Damage = damage, Move = md.Id });
            Say(Name(user) + (md.Id == "DOOMDESIRE" ? " chose Doom Desire as its destiny!" : " foresaw an attack!"));
        }
    }
}
