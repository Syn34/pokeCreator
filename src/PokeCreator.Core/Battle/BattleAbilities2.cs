using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using S = PokeCreator.Pkmn.Stat;

namespace PokeCreator.Battles
{
    /// <summary>
    /// The remaining abilities: primal weather, Ruin abilities, Protean / Libero, Dancer, ally boosts,
    /// abilities reacting to hits or stat changes, and the Generation 9 additions.
    /// </summary>
    public partial class Battle
    {
        /// <summary>Primal weather: "HeavyRain", "HarshSun", "StrongWinds" or "".</summary>
        public string PrimalWeather = "";

        static readonly HashSet<string> WindMoves = new HashSet<string> { "AIRCUTTER", "BLEAKWINDSTORM", "BLIZZARD", "FAIRYWIND", "GUST", "HEATWAVE", "HURRICANE", "ICYWIND", "PETALBLIZZARD", "SANDSEARSTORM", "SANDSTORM", "SPRINGTIDESTORM", "TAILWIND", "TWISTER", "WHIRLWIND", "WILDBOLTSTORM" };
        static readonly HashSet<string> SlicingMoves = new HashSet<string> { "AERIALACE", "AIRCUTTER", "AIRSLASH", "AQUACUTTER", "BEHEMOTHBLADE", "BITTERBLADE", "CEASELESSEDGE", "CROSSPOISON", "CUT", "FURYCUTTER", "KOWTOWCLEAVE", "LEAFBLADE", "NIGHTSLASH", "POPULATIONBOMB", "PSYBLADE", "PSYCHOCUT", "RAZORLEAF", "RAZORSHELL", "SACREDSWORD", "SECRETSWORD", "SLASH", "SOLARBLADE", "STONEAXE", "XSCISSOR", "TACHYONCUTTER", "MIGHTYCLEAVE" };
        static readonly string[] AromaVeilCodes = { "DisableTargetStatusMoves", "DisableTargetUsingSameMoveConsecutively", "AttractTarget", "DisableTargetLastMoveUsed", "DisableTargetUsingDifferentMove", "DisableTargetHealingMoves" };

        bool _dancing, _copyingStats, _mirroring;
        readonly HashSet<PokeCreator.Pkmn.Pokemon> _onceAbilityUsed = new HashSet<PokeCreator.Pkmn.Pokemon>();

        bool IsWindMove(MoveData md) { return md != null && WindMoves.Contains(md.Id); }
        bool IsSlicingMove(MoveData md) { return md != null && SlicingMoves.Contains(md.Id); }
        bool Floats(Battler b) { return HasAbility(b, "LEVITATE") || HasAbility(b, "EELEVATE"); }

        /// <summary>The highest of a Pokémon's Attack, Defense, Sp. Atk, Sp. Def and Speed (Beast Boost).</summary>
        int HighestStat(Battler b)
        {
            int best = S.Attack, bestV = -1;
            foreach (int s in new[] { S.Attack, S.Defense, S.SpAtk, S.SpDef, S.Speed })
            {
                int v = StatsOf(b)[s];
                if (v > bestV) { bestV = v; best = s; }
            }
            return best;
        }

        // ------------------------------------------------------------------ primal weather
        void StartPrimal(Battler b, string kind)
        {
            if (PrimalWeather == kind) return;
            Splash(b);
            PrimalWeather = kind;
            if (kind == "HeavyRain") { Weather = Rain; WeatherTurns = 0; CommonAnim("HeavyRain", null); Say("A heavy rain began to fall!"); }
            else if (kind == "HarshSun") { Weather = Sun; WeatherTurns = 0; CommonAnim("HarshSun", null); Say("The sunlight turned extremely harsh!"); }
            else { Weather = NoWeather; WeatherTurns = 0; CommonAnim("StrongWinds", null); Say("Mysterious strong winds are protecting Flying-type Pokémon!"); }
        }

        string PrimalAbility(string kind) { return kind == "HeavyRain" ? "PRIMORDIALSEA" : kind == "HarshSun" ? "DESOLATELAND" : "DELTASTREAM"; }

        /// <summary>Primal weather ends when no Pokémon with its ability is left on the field.</summary>
        void CheckPrimalEnd()
        {
            if (PrimalWeather == "") return;
            string ab = PrimalAbility(PrimalWeather);
            if (OnField.Any(b => b != null && !b.Pk.Fainted && HasAbility(b, ab))) return;
            Say(PrimalWeather == "HeavyRain" ? "The heavy rain has lifted!" : PrimalWeather == "HarshSun" ? "The harsh sunlight faded!" : "The mysterious strong winds have dissipated!");
            PrimalWeather = ""; Weather = NoWeather; WeatherTurns = 0;
        }

        /// <summary>Normal weather can't replace primal weather.</summary>
        bool PrimalBlocksWeather()
        {
            if (PrimalWeather == "") return false;
            Say(PrimalWeather == "HeavyRain" ? "There is no relief from this heavy rain!" : PrimalWeather == "HarshSun" ? "The extremely harsh sunlight was not lessened at all!" : "The mysterious strong winds blow on regardless!");
            return true;
        }

        /// <summary>Fire moves fizzle in heavy rain, Water moves evaporate in harsh sunlight.</summary>
        bool PrimalStopsMove(string type)
        {
            if (EffWeather == NoWeather) return false;
            if (PrimalWeather == "HeavyRain" && type == "FIRE") { Say("The Fire-type attack fizzled out in the heavy rain!"); return true; }
            if (PrimalWeather == "HarshSun" && type == "WATER") { Say("The Water-type attack evaporated in the extremely harsh sunlight!"); return true; }
            return false;
        }

        /// <summary>Strong winds: moves super effective against Flying are normally effective on Flying types.</summary>
        double StrongWindsMod(string type, Battler target, double typeMod)
        {
            if (PrimalWeather != "StrongWinds" || typeMod <= 1 || !TypesOf(target).Contains("FLYING")) return typeMod;
            if (TypeVs(type, "FLYING") <= 1) return typeMod;
            Say("The mysterious strong winds weakened the attack!");
            return typeMod / 2;
        }

        // ------------------------------------------------------------------ switch-in
        void Gen9SwitchIn(Battler b)
        {
            var foes = Live(1 - b.Side);
            switch (AbilityOf(b))
            {
                case "PRIMORDIALSEA": StartPrimal(b, "HeavyRain"); break;
                case "DESOLATELAND": StartPrimal(b, "HarshSun"); break;
                case "DELTASTREAM": StartPrimal(b, "StrongWinds"); break;
                case "TABLETSOFRUIN": Splash(b); Say(Name(b) + "'s Tablets of Ruin weakened the Attack of all surrounding Pokémon!"); break;
                case "SWORDOFRUIN": Splash(b); Say(Name(b) + "'s Sword of Ruin weakened the Defense of all surrounding Pokémon!"); break;
                case "VESSELOFRUIN": Splash(b); Say(Name(b) + "'s Vessel of Ruin weakened the Sp. Atk of all surrounding Pokémon!"); break;
                case "BEADSOFRUIN": Splash(b); Say(Name(b) + "'s Beads of Ruin weakened the Sp. Def of all surrounding Pokémon!"); break;
                case "ORICHALCUMPULSE":
                    if (EffWeather == Sun) { Splash(b); Say(Name(b) + " basked in the sunlight, sending its ancient pulse into a frenzy!"); }
                    else if (PrimalWeather == "") { Splash(b); Weather = Sun; WeatherTurns = b.Pk.Item == "HEATROCK" ? 8 : 5; CommonAnim("Sun", null); Say(Name(b) + " turned the sunlight harsh, sending its ancient pulse into a frenzy!"); }
                    break;
                case "HADRONENGINE":
                    if (Terrain == "Electric") { Splash(b); Say(Name(b) + " used the Electric Terrain to energize its futuristic engine!"); }
                    else { Splash(b); StartTerrain("Electric", b); Say(Name(b) + " turned the ground into Electric Terrain, energizing its futuristic engine!"); }
                    break;
                case "ASONECHILLINGNEIGH": case "ASONEGRIMNEIGH": case "ASONEGLASTRIER": case "ASONESPECTRIER":
                    Splash(b); Say(Name(b) + " has two Abilities!");
                    if (foes.Count > 0) Say(Cap(SideName(1 - b.Side, IsWild)) + " is too nervous to eat Berries!");
                    break;
                case "CURIOUSMEDICINE":
                    foreach (var al in Allies(b).Where(a => a.Stages.Any(s => s != 0)))
                    { Splash(b); al.Stages = new int[8]; Say(Name(al) + "'s stat changes were removed!"); }
                    break;
                case "COSTAR":
                    {
                        var al = Allies(b).FirstOrDefault();
                        if (al == null) break;
                        Splash(b);
                        for (int i = 0; i < 8; i++) b.Stages[i] = al.Stages[i];
                        b.CritStage = al.CritStage;
                        Say(Name(b) + " copied " + NameL(al) + "'s stat changes!");
                        break;
                    }
                case "HOSPITALITY":
                    foreach (var al in Allies(b).Where(a => a.Pk.HP < a.Pk.TotalHP(P)))
                    { Splash(b); HealBy(al, al.Pk.TotalHP(P) / 4, Name(al) + " drank down all the matcha that " + NameL(b) + " made!"); }
                    break;
                case "SUPERSWEETSYRUP":
                    if (foes.Count == 0 || !_onceAbilityUsed.Add(b.Pk)) break;
                    Splash(b); Say("A supersweet aroma is wafting from the syrup covering " + NameL(b) + "!");
                    foreach (var f in foes) ChangeStat(f, 7, -1, b);
                    break;
                case "EMBODYASPECT":
                    {
                        if (b.Pk.Species != "OGERPON" || !_onceAbilityUsed.Add(b.Pk)) break;
                        int f = b.Pk.Form % 4;
                        int stat = f == 1 ? S.SpDef : f == 2 ? S.Attack : f == 3 ? S.Defense : S.Speed;
                        Splash(b); Say(Name(b) + "'s mask shone brightly!");
                        ChangeStat(b, stat, 1);
                        break;
                    }
                case "TERASHIFT":
                    if (b.Pk.Species == "TERAPAGOS" && b.Pk.Form == 0 && P.Species.Any(s => s.Id == "TERAPAGOS_1"))
                    { Splash(b); ChangeForm(b, 1, Name(b) + " transformed!"); }
                    break;
                case "TERAFORMZERO":
                    if (Weather != NoWeather || Terrain != "")
                    {
                        Splash(b);
                        if (PrimalWeather == "") { Weather = NoWeather; WeatherTurns = 0; }
                        Terrain = ""; TerrainTurns = 0;
                        Say("The effects of the weather and terrain disappeared!");
                    }
                    break;
                case "WINDRIDER":
                    if (Tailwind[b.Side] > 0 && b.Stages[S.Attack] < 6) { Splash(b); ChangeStat(b, S.Attack, 1); }
                    break;
                case "MIMICRY": UpdateMimicry(b); break;
            }
        }

        /// <summary>Mimicry: the Pokémon's type follows the terrain.</summary>
        void UpdateMimicry(Battler b)
        {
            if (b == null || b.Pk.Fainted || !HasAbility(b, "MIMICRY")) return;
            string t = Terrain == "Electric" ? "ELECTRIC" : Terrain == "Grassy" ? "GRASS" : Terrain == "Misty" ? "FAIRY" : Terrain == "Psychic" ? "PSYCHIC" : null;
            if (t == null)
            {
                if (b.TypesOverride != null && b.MimicryActive) { b.TypesOverride = null; b.MimicryActive = false; Say(Name(b) + " returned to its original type!"); }
                return;
            }
            if (b.TypesOverride != null && b.TypesOverride.Count == 1 && b.TypesOverride[0] == t) return;
            b.TypesOverride = new List<string> { t }; b.MimicryActive = true;
            Splash(b); Say(Name(b) + "'s type changed to " + TypeName(t) + "!");
        }

        // ------------------------------------------------------------------ before the move
        /// <summary>Protean / Libero: the user becomes the type of the move it's about to use (once per entry).</summary>
        void ProteanBefore(Battler user, MoveData md, string code)
        {
            if (!(HasAbility(user, "PROTEAN") || HasAbility(user, "LIBERO")) || user.ProteanUsed || md.Id == "STRUGGLE") return;
            string t = MoveType(user, md, code);
            var types = TypesOf(user);
            if (types.Count == 1 && types[0] == t) return;
            user.ProteanUsed = true;
            user.TypesOverride = new List<string> { t };
            Splash(user); Say(Name(user) + " transformed into the " + TypeName(t) + " type!");
        }

        /// <summary>Dancer: another Pokémon's dance move is copied straight away.</summary>
        void DancerAfter(Battler user, Battler target, MoveData md)
        {
            if (_dancing || md == null || !md.Flags.Contains("Dance") || _callDepth > 0) return;
            var dancers = OnField.Where(b => b != user && b != null && !b.Pk.Fainted && HasAbility(b, "DANCER")).OrderByDescending(b => Speed(b)).ToList();
            foreach (var d in dancers)
            {
                if (d.Pk.Fainted || Outcome != BattleOutcome.Ongoing) break;
                _dancing = true;
                try
                {
                    Splash(d);
                    Battler t = md.Category == "Status" && md.Target == "User" ? d : (user.Side != d.Side ? user : (target != null && target.Side != d.Side ? target : null));
                    CallMove(d, t, md);
                }
                finally { _dancing = false; }
            }
        }

        // ------------------------------------------------------------------ damage modifiers
        double Gen9PowerMod(Battler user, Battler target, MoveData md, string type)
        {
            double m = 1;
            foreach (var al in Allies(user))
            {
                if (HasAbility(al, "BATTERY") && md.Category == "Special") m *= 1.3;
                if (HasAbility(al, "POWERSPOT")) m *= 1.3;
            }
            if (HasAbility(user, "SHARPNESS") && IsSlicingMove(md)) m *= 1.5;
            if (HasAbility(user, "FIREMANE") && type == "FIRE") m *= 1.5;
            if (md.Type == "NORMAL" && type == "DRAGON" && HasAbility(user, "DRAGONIZE")) m *= 1.2;
            if (user.Charged && type == "ELECTRIC") m *= 2;
            return m;
        }

        double Gen9AttackMod(Battler user, Battler target, bool physical)
        {
            double m = 1;
            if (physical && HasAbility(user, "ORICHALCUMPULSE") && EffWeather == Sun) m *= 5461.0 / 4096;
            if (!physical && HasAbility(user, "HADRONENGINE") && Terrain == "Electric") m *= 5461.0 / 4096;
            string ruin = physical ? "TABLETSOFRUIN" : "VESSELOFRUIN";
            if (!HasAbility(user, ruin) && OnField.Any(b => b != null && !b.Pk.Fainted && b != user && HasAbility(b, ruin))) m *= 0.75;
            return m;
        }

        double Gen9DefenseMod(Battler user, Battler target, bool physical)
        {
            string ruin = physical ? "SWORDOFRUIN" : "BEADSOFRUIN";
            if (!HasAbility(target, ruin) && OnField.Any(b => b != null && !b.Pk.Fainted && b != target && HasAbility(b, ruin))) return 0.75;
            return 1;
        }

        double Gen9FinalMod(Battler user, Battler target, MoveData md)
        {
            double m = 1;
            if (TargetHas(user, target, "AURAGUARD") && md.Flags.Contains("Contact") && !HasAbility(user, "LONGREACH")) m *= 0.5;
            return m;
        }

        /// <summary>Tera Shell: at full HP every hit is not very effective.</summary>
        double TeraShell(Battler user, Battler target, double typeMod)
        {
            if (typeMod <= 0 || !TargetHas(user, target, "TERASHELL") || target.Pk.HP < target.Pk.TotalHP(P)) return typeMod;
            if (!target.TeraShellShown) { target.TeraShellShown = true; Splash(target); Say(Name(target) + " made its shell gleam! It's distorting type matchups!"); }
            return Math.Min(typeMod, 0.5);
        }

        /// <summary>Abilities that take a move instead of letting it hit: Well-Baked Body, Wind Rider, Earth Eater, Good as Gold, Aroma Veil.</summary>
        bool Gen9Absorbs(Battler user, Battler target, MoveData md, string type, string code)
        {
            if (target == user || target.Pk.Fainted) return false;
            if (type == "FIRE" && md.Category != "Status" && TargetHas(user, target, "WELLBAKEDBODY"))
            { Splash(target); if (!ChangeStat(target, S.Defense, 2)) Say("It doesn't affect " + NameL(target) + "..."); return true; }
            if (IsWindMove(md) && md.Target != "UserSide" && TargetHas(user, target, "WINDRIDER"))
            { Splash(target); if (!ChangeStat(target, S.Attack, 1)) Say("It doesn't affect " + NameL(target) + "..."); return true; }
            if (type == "GROUND" && TargetHas(user, target, "EARTHEATER"))
            {
                Splash(target);
                if (target.Pk.HP >= target.Pk.TotalHP(P)) Say(Name(target) + "'s HP is full!");
                else HealBy(target, target.Pk.TotalHP(P) / 4, Name(target) + " restored HP using its Earth Eater!");
                return true;
            }
            if (type == "GROUND" && md.Category != "Status" && TargetHas(user, target, "EELEVATE"))
            { Splash(target); Say("It doesn't affect " + NameL(target) + "..."); return true; }
            if (md.Category == "Status" && target.Side != user.Side && TargetHas(user, target, "GOODASGOLD"))
            { Splash(target); Say("It doesn't affect " + NameL(target) + "..."); return true; }
            if (md.Category == "Status" && AromaVeilCodes.Contains(code))
            {
                var veil = new[] { target }.Concat(Allies(target)).FirstOrDefault(b => TargetHas(user, b, "AROMAVEIL"));
                if (veil != null) { Splash(veil); Say(Name(target) + " is protected by an aromatic veil!"); return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------ reactions to being hit
        void Gen9Damaged(Battler user, Battler target, MoveData md, string type, int hpBefore, bool contact)
        {
            string ta = target.AbilitySuppressed ? "" : AbilityOf(target);
            int max = target.Pk.TotalHP(P);
            if (!target.Pk.Fainted)
            {
                switch (ta)
                {
                    case "COLORCHANGE":
                        {
                            var types = TypesOf(target);
                            if (type != null && !(types.Count == 1 && types[0] == type)) { target.TypesOverride = new List<string> { type }; Splash(target); Say(Name(target) + "'s type changed to " + TypeName(type) + "!"); }
                            break;
                        }
                    case "PERISHBODY":
                        if (contact && target.PerishCount < 0 && user.PerishCount < 0 && !user.Pk.Fainted)
                        { Splash(target); target.PerishCount = 3; user.PerishCount = 3; Say("Both Pokémon will faint in three turns!"); }
                        break;
                    case "WANDERINGSPIRIT":
                        if (contact && !user.Pk.Fainted && !UntraceableAbilities.Contains(AbilityOf(user)) && user.Pk.Item != "ABILITYSHIELD")
                        {
                            Splash(target);
                            string mine = AbilityOf(target);
                            target.AbilityOverride = AbilityOf(user); user.AbilityOverride = mine;
                            Say(Name(target) + " swapped Abilities with its target!");
                        }
                        break;
                    case "SEEDSOWER": if (Terrain != "Grassy") { Splash(target); StartTerrain("Grassy", target); } break;
                    case "TOXICDEBRIS":
                        if (md.Category == "Physical" && ToxicSpikes[user.Side] < 2)
                        { Splash(target); ToxicSpikes[user.Side]++; Say("Poison spikes were scattered on the ground all around " + SideName(user.Side, IsWild) + "!"); }
                        break;
                    case "WINDPOWER": if (IsWindMove(md) && !target.Charged) { Splash(target); target.Charged = true; Say("Being hit by " + (md.Name ?? md.Id) + " charged " + NameL(target) + " with power!"); } break;
                    case "ELECTROMORPHOSIS": if (!target.Charged) { Splash(target); target.Charged = true; Say("Being hit by " + (md.Name ?? md.Id) + " charged " + NameL(target) + " with power!"); } break;
                    case "SPICYSPRAY": if (!user.Pk.Fainted && CanStatus(user, "BURN", false, target)) { Splash(target); Inflict(user, "BURN", false, target); } break;
                    case "PICKPOCKET":
                        if (contact && !user.Pk.Fainted && string.IsNullOrEmpty(target.Pk.Item) && !string.IsNullOrEmpty(user.Pk.Item) && !HasAbility(user, "STICKYHOLD"))
                        {
                            NoteItemChange(user.Pk); NoteItemChange(target.Pk);
                            target.Pk.Item = user.Pk.Item; user.Pk.Item = null;
                            Splash(target); Say(Name(target) + " stole " + NameL(user) + "'s " + ItemName(target.Pk.Item) + "!");
                        }
                        break;
                    case "WIMPOUT": case "EMERGENCYEXIT":
                        if (hpBefore * 2 > max && target.Pk.HP * 2 <= max) EmergencyExit(target);
                        break;
                }
            }
            // The attacker's own abilities
            if (!user.Pk.Fainted)
            {
                if (HasAbility(user, "MAGICIAN") && string.IsNullOrEmpty(user.Pk.Item) && !string.IsNullOrEmpty(target.Pk.Item) && !HasAbility(target, "STICKYHOLD") && md.Category != "Status")
                {
                    NoteItemChange(user.Pk); NoteItemChange(target.Pk);
                    user.Pk.Item = target.Pk.Item; target.Pk.Item = null;
                    Splash(user); Say(Name(user) + " stole " + NameL(target) + "'s " + ItemName(user.Pk.Item) + "!");
                }
                if (HasAbility(user, "STENCH") && !target.Pk.Fainted && !target.ActedThisTurn && !(md.FunctionCode ?? "").Contains("Flinch") && !TargetHas(user, target, "INNERFOCUS") && Rng.Next(100) < 10)
                    target.Flinched = true;
                if (user.Charged && MoveType(user, md, md.FunctionCode ?? "") == "ELECTRIC") user.Charged = false;
            }
        }

        /// <summary>Wimp Out / Emergency Exit: the Pokémon retreats (a wild one flees) when its HP falls to half.</summary>
        void EmergencyExit(Battler b)
        {
            if (IsWildFoe(b))
            {
                Splash(b);
                Outcome = BattleOutcome.Escaped;
                Say(Name(b) + " fled from battle!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Flee });
                return;
            }
            if ((b.Side == 0 ? Bench(0) : FoeBench(b.Slot)).Count == 0) return;
            Splash(b);
            SwitchOutNow(b);
        }

        /// <summary>Soul-Heart and the neigh abilities when a Pokémon faints; Beast Boost / Eelevate raise the highest stat.</summary>
        void Gen9KnockOut(Battler user)
        {
            if (!user.Pk.Fainted)
            {
                if ((HasAbility(user, "CHILLINGNEIGH") || HasAbility(user, "ASONEGLASTRIER") || HasAbility(user, "ASONECHILLINGNEIGH")) && user.Stages[S.Attack] < 6) { Splash(user); ChangeStat(user, S.Attack, 1); }
                if ((HasAbility(user, "GRIMNEIGH") || HasAbility(user, "ASONESPECTRIER") || HasAbility(user, "ASONEGRIMNEIGH")) && user.Stages[S.SpAtk] < 6) { Splash(user); ChangeStat(user, S.SpAtk, 1); }
                if (HasAbility(user, "EELEVATE")) { int s = HighestStat(user); if (user.Stages[s] < 6) { Splash(user); ChangeStat(user, s, 1); } }
            }
        }

        void SoulHeart()
        {
            foreach (var b in OnField.Where(x => x != null && !x.Pk.Fainted && HasAbility(x, "SOULHEART") && x.Stages[S.SpAtk] < 6).ToList())
            { Splash(b); ChangeStat(b, S.SpAtk, 1); }
        }

        // ------------------------------------------------------------------ stat changes
        /// <summary>Mirror Armor bounces a stat drop back at the Pokémon that caused it. True = handled.</summary>
        bool MirrorArmor(Battler b, int stat, int amount, Battler source)
        {
            if (amount >= 0 || source == null || source == b || _mirroring || !TargetHas(source, b, "MIRRORARMOR")) return false;
            Splash(b);
            _mirroring = true;
            try { if (!source.Pk.Fainted) ChangeStat(source, stat, amount, b); }
            finally { _mirroring = false; }
            return true;
        }

        /// <summary>Opportunist and the Mirror Herb copy an opponent's stat boosts.</summary>
        void CopyBoosts(Battler b, int stat, int amount)
        {
            if (amount <= 0 || _copyingStats || b.Pk.Fainted) return;
            _copyingStats = true;
            try
            {
                foreach (var f in Live(1 - b.Side).ToList())
                {
                    if (HasAbility(f, "OPPORTUNIST") && f.Stages[stat] < 6) { Splash(f); ChangeStat(f, stat, amount); }
                    else if (Holds(f, "MIRRORHERB") && f.Stages[stat] < 6) { Say(Name(f) + " used its Mirror Herb to mirror its opponent's stat changes!"); UseUpItem(f); ChangeStat(f, stat, amount); }
                }
            }
            finally { _copyingStats = false; }
        }

        // ------------------------------------------------------------------ end of turn
        void Gen9EndOfTurn(Battler b)
        {
            if (b.Pk.Fainted) return;
            if (HasAbility(b, "BADDREAMS"))
            {
                bool shown = false;
                foreach (var f in Live(1 - b.Side).Where(x => (x.Pk.Status ?? "").ToUpperInvariant() == "SLEEP" || HasAbility(x, "COMATOSE")).ToList())
                {
                    if (!shown) { Splash(b); shown = true; }
                    Residual(f, Math.Max(1, f.Pk.TotalHP(P) / 8), Name(f) + " is tormented!");
                }
            }
        }
    }
}
