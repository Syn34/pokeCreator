using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using S = PokeCreator.Pkmn.Stat;

namespace PokeCreator.Battles
{
    /// <summary>More abilities: switch-in effects, damage modifiers, reactions to being hit, priority, end of turn.</summary>
    public partial class Battle
    {
        static readonly string[] UntraceableAbilities = { "TRACE", "MULTITYPE", "RKSSYSTEM", "ILLUSION", "IMPOSTER", "FORECAST", "FLOWERGIFT", "ZENMODE", "STANCECHANGE", "SCHOOLING", "COMATOSE", "SHIELDSDOWN", "DISGUISE", "BATTLEBOND", "POWERCONSTRUCT", "ICEFACE", "GULPMISSILE", "NEUTRALIZINGGAS", "HUNGERSWITCH", "RECEIVER", "POWEROFALCHEMY", "WONDERGUARD", "ZEROTOHERO", "COMMANDER" };

        Battler AllyOf(Battler b) { return Ally(b); }
        readonly HashSet<PokeCreator.Pkmn.Pokemon> _entryBoostUsed = new HashSet<PokeCreator.Pkmn.Pokemon>();

        /// <summary>The stat Protosynthesis / Quark Drive raises: the highest of Attack, Defense, Sp. Atk, Sp. Def, Speed.</summary>
        int BoostedStat(Battler b)
        {
            bool on = (HasAbility(b, "PROTOSYNTHESIS") && EffWeather == Sun) || (HasAbility(b, "QUARKDRIVE") && Terrain == "Electric") || b.BoosterEnergy;
            if (!on || !(HasAbility(b, "PROTOSYNTHESIS") || HasAbility(b, "QUARKDRIVE"))) return -1;
            int best = S.Attack, bestV = -1;
            foreach (int s in new[] { S.Attack, S.Defense, S.SpAtk, S.SpDef, S.Speed })
            {
                int v = Stat(b, s);
                if (v > bestV) { bestV = v; best = s; }
            }
            return best;
        }

        void MoreSwitchInAbilities(Battler b)
        {
            var foes = Live(1 - b.Side);
            var foe = foes.FirstOrDefault();
            switch (AbilityOf(b))
            {
                case "TRACE":
                    {
                        var opts = foes.Where(f => !UntraceableAbilities.Contains(AbilityOf(f))).ToList();
                        if (opts.Count == 0) break;
                        var f2 = opts[Rng.Next(opts.Count)];
                        Splash(b);
                        b.AbilityOverride = AbilityOf(f2);
                        Say(Name(b) + " traced " + NameL(f2) + "'s " + AbilityName(f2) + "!");
                        SwitchInAbility(b);
                        break;
                    }
                case "FRISK":
                    foreach (var f in foes.Where(x => !string.IsNullOrEmpty(x.Pk.Item)))
                    {
                        var it = P.Items.FirstOrDefault(i => i.Id == f.Pk.Item);
                        Splash(b); Say(Name(b) + " frisked " + NameL(f) + " and found its " + (it != null ? it.Name : f.Pk.Item) + "!");
                    }
                    break;
                case "ANTICIPATION":
                    if (foes.Any(f => f.Pk.Moves.Select(m => Move(m.Id)).Any(md => md != null && md.Category != "Status" && (Effectiveness(md.Type, b.Pk) > 1 || (md.FunctionCode ?? "").StartsWith("OHKO")))))
                    { Splash(b); Say(Name(b) + " shuddered!"); }
                    break;
                case "FOREWARN":
                    {
                        MoveData best = null; Battler who = null;
                        foreach (var f in foes) foreach (var m in f.Pk.Moves) { var md = Move(m.Id); if (md != null && (best == null || md.Power > best.Power)) { best = md; who = f; } }
                        if (best != null) { Splash(b); Say(Name(b) + " was alerted to " + NameL(who) + "'s " + (best.Name ?? best.Id) + "!"); }
                        break;
                    }
                case "UNNERVE": if (foe != null) { Splash(b); Say(Cap(SideName(1 - b.Side, IsWild)) + " is too nervous to eat Berries!"); } break;
                case "ELECTRICSURGE": Splash(b); StartTerrain("Electric", b); break;
                case "GRASSYSURGE": Splash(b); StartTerrain("Grassy", b); break;
                case "MISTYSURGE": Splash(b); StartTerrain("Misty", b); break;
                case "PSYCHICSURGE": Splash(b); StartTerrain("Psychic", b); break;
                case "INTREPIDSWORD": if (_entryBoostUsed.Add(b.Pk)) { Splash(b); ChangeStat(b, S.Attack, 1); } break;
                case "DAUNTLESSSHIELD": if (_entryBoostUsed.Add(b.Pk)) { Splash(b); ChangeStat(b, S.Defense, 1); } break;
                case "SCREENCLEANER":
                    if (Reflect.Concat(LightScreen).Concat(AuroraVeil).Any(x => x > 0))
                    { Splash(b); for (int s = 0; s < 2; s++) Reflect[s] = LightScreen[s] = AuroraVeil[s] = 0; Say("The effects of protective barriers disappeared!"); }
                    break;
                case "FAIRYAURA": Splash(b); Say(Name(b) + " is radiating a fairy aura!"); break;
                case "DARKAURA": Splash(b); Say(Name(b) + " is radiating a dark aura!"); break;
                case "AURABREAK": Splash(b); Say(Name(b) + " reversed all other Pokémon's auras!"); break;
                case "COMATOSE": Splash(b); Say(Name(b) + " is drowsing!"); break;
                case "NEUTRALIZINGGAS": Splash(b); Say("Neutralizing gas filled the area!"); break;
                case "PROTOSYNTHESIS": case "QUARKDRIVE":
                    if (BoostedStat(b) >= 0) { Splash(b); Say(Name(b) + "'s " + StatName(BoostedStat(b)) + " was heightened!"); }
                    break;
            }
            b.StartItem = b.Pk.Item;
            FormAbilities(b, false);
        }

        /// <summary>Normal-type moves becoming another type (Pixilate, Refrigerate, Aerilate, Galvanize) and Liquid Voice.</summary>
        string AbilityMoveType(Battler user, MoveData md, string type)
        {
            if (type == "NORMAL")
            {
                if (HasAbility(user, "PIXILATE")) return "FAIRY";
                if (HasAbility(user, "REFRIGERATE")) return "ICE";
                if (HasAbility(user, "AERILATE")) return "FLYING";
                if (HasAbility(user, "GALVANIZE")) return "ELECTRIC";
            }
            if (HasAbility(user, "LIQUIDVOICE") && md.Flags.Contains("Sound")) return "WATER";
            return type;
        }

        double MorePowerMod(Battler user, Battler target, MoveData md, string type, string code)
        {
            double m = 1;
            if (md.Type == "NORMAL" && type != "NORMAL" && (HasAbility(user, "PIXILATE") || HasAbility(user, "REFRIGERATE") || HasAbility(user, "AERILATE") || HasAbility(user, "GALVANIZE"))) m *= 1.2;
            if (HasAbility(user, "RIVALRY") && user.Pk.Gender != "" && target.Pk.Gender != "") m *= user.Pk.Gender == target.Pk.Gender ? 1.25 : 0.75;
            if (HasAbility(user, "STAKEOUT") && target.SwitchedIn) m *= 2;
            if (HasAbility(user, "PUNKROCK") && md.Flags.Contains("Sound")) m *= 1.3;
            if (TargetHas(user, target, "PUNKROCK") && md.Flags.Contains("Sound")) m *= 0.5;
            if (HasAbility(user, "FLAREBOOST") && md.Category == "Special" && (user.Pk.Status ?? "").ToUpperInvariant() == "BURN") m *= 1.5;
            if (HasAbility(user, "TOXICBOOST") && md.Category == "Physical" && (user.Pk.Status ?? "").ToUpperInvariant() == "POISON") m *= 1.5;
            if ((HasAbility(user, "STEELWORKER") || HasAbility(user, "STEELYSPIRIT")) && type == "STEEL") m *= 1.5;
            if (HasAbility(user, "TRANSISTOR") && type == "ELECTRIC") m *= 1.3;
            if (HasAbility(user, "DRAGONSMAW") && type == "DRAGON") m *= 1.5;
            if (HasAbility(user, "ROCKYPAYLOAD") && type == "ROCK") m *= 1.5;
            if (HasAbility(user, "WATERBUBBLE") && type == "WATER") m *= 2;
            if (TargetHas(user, target, "WATERBUBBLE") && type == "FIRE") m *= 0.5;
            if (TargetHas(user, target, "PURIFYINGSALT") && type == "GHOST") m *= 0.5;
            bool auraBreak = OnField.Any(b => HasAbility(b, "AURABREAK"));
            if ((type == "FAIRY" && OnField.Any(b => HasAbility(b, "FAIRYAURA"))) || (type == "DARK" && OnField.Any(b => HasAbility(b, "DARKAURA"))))
                m *= auraBreak ? 0.75 : 4.0 / 3;
            if (HasAbility(user, "SUPREMEOVERLORD")) m *= 1 + 0.1 * Math.Min(5, Parties[user.Side].Count(p => p.Fainted));
            return m;
        }

        double MoreAttackMod(Battler user, Battler target, bool physical, string type)
        {
            double m = 1;
            if (physical && HasAbility(user, "GORILLATACTICS")) m *= 1.5;
            if (HasAbility(user, "SLOWSTART") && physical && user.TurnsOut < 5) m *= 0.5;
            if (HasAbility(user, "DEFEATIST") && user.Pk.HP * 2 <= user.Pk.TotalHP(P)) m *= 0.5;
            int bs = BoostedStat(user);
            if ((bs == S.Attack && physical) || (bs == S.SpAtk && !physical)) m *= 1.3;
            if ((HasAbility(user, "PLUS") || HasAbility(user, "MINUS")) && !physical) { var al = AllyOf(user); if (al != null && (HasAbility(al, "PLUS") || HasAbility(al, "MINUS"))) m *= 1.5; }
            if (TargetHas(user, target, "PURIFYINGSALT") && type == "GHOST") m *= 1; // handled in power
            return m;
        }

        double MoreDefenseMod(Battler user, Battler target, bool physical)
        {
            double m = 1;
            if (physical && TargetHas(user, target, "GRASSPELT") && Terrain == "Grassy") m *= 1.5;
            int bs = BoostedStat(target);
            if ((bs == S.Defense && physical) || (bs == S.SpDef && !physical)) m *= 1.3;
            return m;
        }

        double MoreFinalMod(Battler user, Battler target, MoveData md, string type, double typeMod)
        {
            double m = 1;
            if (TargetHas(user, target, "FLUFFY"))
            {
                if (md.Flags.Contains("Contact") && !HasAbility(user, "LONGREACH")) m *= 0.5;
                if (type == "FIRE") m *= 2;
            }
            if (TargetHas(user, target, "ICESCALES") && md.Category == "Special") m *= 0.5;
            if (typeMod > 1 && HasAbility(user, "NEUROFORCE")) m *= 1.25;
            var al = AllyOf(target);
            if (al != null && HasAbility(al, "FRIENDGUARD") && user.Side != target.Side) m *= 0.75;
            return m;
        }

        /// <summary>Priority after Prankster, Gale Wings and Triage.</summary>
        int EffectivePriority(Battler b, MoveData md)
        {
            if (md == null) return 0;
            int p = md.Priority;
            if (HasAbility(b, "PRANKSTER") && md.Category == "Status") p++;
            if (HasAbility(b, "GALEWINGS") && md.Type == "FLYING" && b.Pk.HP >= b.Pk.TotalHP(P)) p++;
            string c = md.FunctionCode ?? "";
            if (HasAbility(b, "TRIAGE") && (c.StartsWith("HealUser") || c.StartsWith("HealTarget") || c.Contains("HealUserBy"))) p += 3;
            if (c == "HigherPriorityInGrassyTerrain" && Terrain == "Grassy" && IsGrounded(b)) p++;
            return p;
        }

        /// <summary>Abilities that stop a move outright before it hits (Queenly Majesty, Prankster vs Dark types, Telepathy, Damp, Magic Bounce...). True = stopped.</summary>
        bool AbilityStopsMove(Battler user, Battler target, MoveData md, string code)
        {
            if (target == null || target == user) return false;
            bool foe = target.Side != user.Side;
            if (foe && EffectivePriority(user, md) > 0)
            {
                foreach (var b in new[] { target, AllyOf(target) })
                    if (b != null && (TargetHas(user, b, "QUEENLYMAJESTY") || TargetHas(user, b, "DAZZLING") || TargetHas(user, b, "ARMORTAIL")))
                    { Splash(b); Say(Name(target) + " cannot use " + (md.Name ?? md.Id) + "!"); return true; }
            }
            if (foe && md.Category == "Status" && HasAbility(user, "PRANKSTER") && TypesOf(target).Contains("DARK"))
            { Say("It doesn't affect " + NameL(target) + "..."); return true; }
            if (!foe && md.Category != "Status" && TargetHas(user, target, "TELEPATHY")) { Splash(target); Say(Name(target) + " avoids attacks by its ally Pokémon!"); return true; }
            if (md.Category == "Status" && md.Accuracy > 0 && TargetHas(user, target, "WONDERSKIN") && Rng.Next(100) >= 50) { Splash(target); Say(Name(user) + "'s attack missed!"); return true; }
            return false;
        }

        /// <summary>Magic Bounce reflects status moves aimed at it back at the user.</summary>
        bool MagicBounce(Battler user, Battler target, MoveData md, string code)
        {
            if (md.Category != "Status" || target.Side == user.Side || !md.Flags.Contains("CanMirrorMove") || !(TargetHas(user, target, "MAGICBOUNCE") || target.MagicCoat) || _bouncing) return false;
            if (!target.MagicCoat) Splash(target);
            Say(Name(target) + " bounced the " + (md.Name ?? md.Id) + " back!");
            _bouncing = true;
            try { StatusMove(target, user, md, code); } finally { _bouncing = false; }
            return true;
        }
        bool _bouncing;

        bool DampBlocks(Battler user, string code)
        {
            if (!code.StartsWith("UserFaints") && code != "UserLosesHalfOfTotalHPExplosive") return false;
            var damp = OnField.FirstOrDefault(b => !b.Pk.Fainted && b != user && HasAbility(b, "DAMP") && !MoldBreaker(user));
            if (damp == null) return false;
            Splash(damp); Say(Name(user) + " cannot use its move!");
            return true;
        }

        /// <summary>Reactions to being hit by a damaging move: Weak Armor, Stamina, Justified, Anger Point, Cursed Body, Aftermath...</summary>
        void DamagedAbilities(Battler user, Battler target, MoveData md, string type, bool crit, int hpBefore)
        {
            if (target == user) return;
            string ta = target.AbilitySuppressed ? "" : AbilityOf(target);
            int max = target.Pk.TotalHP(P);
            bool contact = md.Flags.Contains("Contact") && !HasAbility(user, "LONGREACH") && user.Pk.Item != "PROTECTIVEPADS";
            if (target.Pk.Fainted)
            {
                if (target.Grudge && !user.Pk.Fainted) { var gm = user.Pk.Moves.FirstOrDefault(m => m.Id == md.Id); if (gm != null) { gm.PP = 0; Say(Name(user) + "'s " + (md.Name ?? md.Id) + " lost all its PP due to the grudge!"); } }
                if (ta == "AFTERMATH" && contact && !user.Pk.Fainted && !OnField.Any(b => HasAbility(b, "DAMP")))
                { Splash(target); Residual(user, Math.Max(1, user.Pk.TotalHP(P) / 4), Name(user) + " was caught in the aftermath!"); }
                if (ta == "INNARDSOUT" && !user.Pk.Fainted) { Splash(target); Residual(user, Math.Max(1, hpBefore), Name(user) + " was hurt!"); }
                return;
            }
            if (target.BeakBlast && contact && !user.Pk.Fainted && CanStatus(user, "BURN", false, target)) Inflict(user, "BURN", false, target);
            if (target.Rage && target.Stages[S.Attack] < 6) { Say(Name(target) + "'s rage is building!"); ChangeStat(target, S.Attack, 1); }
            switch (ta)
            {
                case "WEAKARMOR": if (md.Category == "Physical") { Splash(target); ChangeStat(target, S.Defense, -1); ChangeStat(target, S.Speed, 2); } break;
                case "STAMINA": Splash(target); ChangeStat(target, S.Defense, 1); break;
                case "JUSTIFIED": if (type == "DARK") { Splash(target); ChangeStat(target, S.Attack, 1); } break;
                case "RATTLED": if (type == "BUG" || type == "GHOST" || type == "DARK") { Splash(target); ChangeStat(target, S.Speed, 1); } break;
                case "STEAMENGINE": if (type == "FIRE" || type == "WATER") { Splash(target); ChangeStat(target, S.Speed, 6); } break;
                case "WATERCOMPACTION": if (type == "WATER") { Splash(target); ChangeStat(target, S.Defense, 2); } break;
                case "THERMALEXCHANGE": if (type == "FIRE") { Splash(target); ChangeStat(target, S.Attack, 1); } break;
                case "ANGERPOINT":
                    if (crit && target.Stages[S.Attack] < 6)
                    { Splash(target); target.Stages[S.Attack] = 6; _ev.Add(new BattleEvent { Kind = BattleEventKind.StatChange, Side = target.Side, Party = target.PartyIndex, Value = 6 }); Say(Name(target) + " maxed its Attack!"); }
                    break;
                case "BERSERK": case "ANGERSHELL":
                    if (hpBefore * 2 > max && target.Pk.HP * 2 <= max)
                    {
                        Splash(target);
                        if (ta == "BERSERK") ChangeStat(target, S.SpAtk, 1);
                        else { ChangeStat(target, S.Attack, 1); ChangeStat(target, S.SpAtk, 1); ChangeStat(target, S.Speed, 1); ChangeStat(target, S.Defense, -1); ChangeStat(target, S.SpDef, -1); }
                    }
                    break;
                case "CURSEDBODY":
                    if (!user.Pk.Fainted && user.DisableTurns == 0 && Rng.Next(100) < 30) { Splash(target); user.DisableTurns = 4; user.DisableMove = md.Id; Say(Name(user) + "'s " + (md.Name ?? md.Id) + " was disabled!"); }
                    break;
                case "COTTONDOWN":
                    Splash(target);
                    foreach (var b in OnField.Where(x => x != target && !x.Pk.Fainted).ToList()) ChangeStat(b, S.Speed, -1, target);
                    break;
                case "SANDSPIT": if (Weather != Sandstorm) { Splash(target); StartWeather(Sandstorm, target); } break;
                case "GOOEY": case "TANGLINGHAIR": if (contact && !user.Pk.Fainted) { Splash(target); ChangeStat(user, S.Speed, -1, target); } break;
                case "MUMMY": case "LINGERINGAROMA":
                    if (contact && !user.Pk.Fainted && AbilityOf(user) != ta && !UntraceableAbilities.Contains(AbilityOf(user)))
                    { Splash(target); user.AbilityOverride = ta; Say(Name(user) + "'s Ability became " + AbilityName(target) + "!"); }
                    break;
                case "CUTECHARM":
                    if (contact && !user.Pk.Fainted && user.AttractedTo == null && Rng.Next(100) < 30 && user.Pk.Gender != "" && target.Pk.Gender != "" && user.Pk.Gender != target.Pk.Gender)
                    { Splash(target); user.AttractedTo = target; Say(Name(user) + " fell in love!"); }
                    break;
            }
            // The attacker's own contact abilities
            if (contact && !target.Pk.Fainted && HasAbility(user, "POISONTOUCH") && Rng.Next(100) < 30 && CanStatus(target, "POISON", false, user))
            { Splash(user); Inflict(target, "POISON", false, user); }
            if (!target.Pk.Fainted && HasAbility(user, "TOXICCHAIN") && Rng.Next(100) < 30 && CanStatus(target, "TOXIC", false, user))
            { Splash(user); Inflict(target, "TOXIC", false, user); }
        }

        /// <summary>Zen Mode, Schooling, Shields Down, Forecast, Flower Gift, Hunger Switch and Power Construct: forms that follow HP, weather or turns.</summary>
        void FormAbilities(Battler b, bool endOfTurn)
        {
            if (b == null || b.Pk.Fainted) return;
            int hp = b.Pk.HP, max = b.Pk.TotalHP(P), form = b.Pk.Form;
            switch (AbilityOf(b))
            {
                case "ZENMODE":
                    if (b.Pk.Species != "DARMANITAN") break;
                    if (hp * 2 <= max && form % 2 == 0) ChangeForm(b, form + 1, Name(b) + " triggered Zen Mode!");
                    else if (hp * 2 > max && form % 2 == 1) ChangeForm(b, form - 1, "Zen Mode ended!");
                    break;
                case "SCHOOLING":
                    if (b.Pk.Species != "WISHIWASHI") break;
                    if (b.Pk.Level >= 20 && hp * 4 > max && form == 0) ChangeForm(b, 1, Name(b) + " formed a school!");
                    else if ((hp * 4 <= max || b.Pk.Level < 20) && form == 1) ChangeForm(b, 0, Name(b) + " stopped schooling!");
                    break;
                case "SHIELDSDOWN":
                    if (b.Pk.Species != "MINIOR") break;
                    if (hp * 2 <= max && form < 7) ChangeForm(b, 7 + Math.Abs((b.Pk.IV[0] + b.Pk.IV[1] * 3 + b.Pk.IV[2] * 7) % 7), "Its shell burst!");
                    else if (hp * 2 > max && form >= 7) ChangeForm(b, 0, Name(b) + " shielded itself!");
                    break;
                case "FORECAST":
                    if (b.Pk.Species != "CASTFORM") break;
                    {
                        int want = EffWeather == Sun ? 1 : EffWeather == Rain ? 2 : EffWeather == Hail ? 3 : 0;
                        if (want != form) ChangeForm(b, want, Name(b) + " transformed!");
                    }
                    break;
                case "FLOWERGIFT":
                    if (b.Pk.Species != "CHERRIM") break;
                    { int want = EffWeather == Sun ? 1 : 0; if (want != form) ChangeForm(b, want, Name(b) + " transformed!"); }
                    break;
                case "HUNGERSWITCH":
                    if (endOfTurn && b.Pk.Species == "MORPEKO") ChangeForm(b, form == 0 ? 1 : 0, null);
                    break;
                case "POWERCONSTRUCT":
                    if (endOfTurn && b.Pk.Species == "ZYGARDE" && hp * 2 <= max && form < 2)
                    {
                        Say("You sense the presence of many!");
                        ChangeForm(b, 2, Name(b) + " transformed into its Complete Forme!");
                    }
                    break;
            }
        }

        /// <summary>Stance Change: Aegislash takes Blade Forme to attack and Shield Forme for King's Shield.</summary>
        void StanceChange(Battler b, MoveData md)
        {
            if (b.Pk.Species != "AEGISLASH" || !HasAbility(b, "STANCECHANGE") || md == null) return;
            if (md.Category != "Status" && b.Pk.Form == 0) ChangeForm(b, 1, "Changed to Blade Forme!");
            else if (md.Id == "KINGSSHIELD" && b.Pk.Form == 1) ChangeForm(b, 0, "Changed to Shield Forme!");
        }

        void MoreEndOfTurnAbilities(Battler b)
        {
            if (b.Pk.Fainted) return;
            FormAbilities(b, true);
            switch (AbilityOf(b))
            {
                case "MOODY":
                    {
                        var up = Enumerable.Range(1, 5).Where(s => b.Stages[s] < 6).ToList();
                        if (up.Count == 0) break;
                        Splash(b);
                        int u = up[Rng.Next(up.Count)];
                        ChangeStat(b, u, 2);
                        var down = Enumerable.Range(1, 5).Where(s => s != u && b.Stages[s] > -6).ToList();
                        if (down.Count > 0) ChangeStat(b, down[Rng.Next(down.Count)], -1);
                        break;
                    }
                case "HEALER":
                    {
                        var al = AllyOf(b);
                        if (al != null && !string.IsNullOrEmpty(al.Pk.Status) && Rng.Next(100) < 30) { Splash(b); al.Pk.Status = ""; al.ToxicCount = 0; StatusEvent(al); Say(Name(al) + "'s status condition was cured!"); }
                        break;
                    }
                case "SLOWSTART": if (b.TurnsOut == 4) { Splash(b); Say(Name(b) + " finally got its act together!"); } break;
                case "HARVEST":
                    if (string.IsNullOrEmpty(b.Pk.Item) && b.ConsumedBerry != null && (EffWeather == Sun || Rng.Next(2) == 0))
                    {
                        Splash(b); b.Pk.Item = b.ConsumedBerry;
                        var it = P.Items.FirstOrDefault(i => i.Id == b.ConsumedBerry);
                        Say(Name(b) + " harvested one " + (it != null ? it.Name : b.ConsumedBerry) + "!");
                        b.ConsumedBerry = null;
                    }
                    break;
            }
        }

        /// <summary>Synchronize: a Pokémon given poison, burn or paralysis by another passes it back.</summary>
        void SynchronizeStatus(Battler b, string status, Battler source)
        {
            if (source == null || source == b || source.Pk.Fainted || !HasAbility(b, "SYNCHRONIZE")) return;
            string s = status == "TOXIC" ? "TOXIC" : status;
            if (s != "POISON" && s != "TOXIC" && s != "BURN" && s != "PARALYSIS") return;
            if (!CanStatus(source, s, false, b)) return;
            Splash(b);
            Inflict(source, s, false, null);
        }
    }
}
