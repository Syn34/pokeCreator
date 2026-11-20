using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;
using S = PokeCreator.Pkmn.Stat;

namespace PokeCreator.Battles
{
    /// <summary>
    /// Held items in battle: berries (HP, status, pinch, type-resist), herbs, items that react to being hit,
    /// end-of-turn items, turn-order items, power and stat items, and the abilities tied to them
    /// (Gluttony, Ripen, Cud Chew, Symbiosis, Pickup, Ball Fetch, Cheek Pouch, Unnerve / As One).
    /// </summary>
    public partial class Battle
    {
        static readonly Dictionary<string, string> ResistBerries = new Dictionary<string, string>
        {
            { "OCCABERRY", "FIRE" }, { "PASSHOBERRY", "WATER" }, { "WACANBERRY", "ELECTRIC" }, { "RINDOBERRY", "GRASS" }, { "YACHEBERRY", "ICE" },
            { "CHOPLEBERRY", "FIGHTING" }, { "KEBIABERRY", "POISON" }, { "SHUCABERRY", "GROUND" }, { "COBABERRY", "FLYING" }, { "PAYAPABERRY", "PSYCHIC" },
            { "TANGABERRY", "BUG" }, { "CHARTIBERRY", "ROCK" }, { "KASIBBERRY", "GHOST" }, { "HABANBERRY", "DRAGON" }, { "COLBURBERRY", "DARK" },
            { "BABIRIBERRY", "STEEL" }, { "ROSELIBERRY", "FAIRY" }, { "CHILANBERRY", "NORMAL" }
        };
        static readonly Dictionary<string, int> PinchStatBerries = new Dictionary<string, int>
        {
            { "LIECHIBERRY", S.Attack }, { "GANLONBERRY", S.Defense }, { "SALACBERRY", S.Speed }, { "PETAYABERRY", S.SpAtk }, { "APICOTBERRY", S.SpDef }
        };
        /// <summary>Confusion berries and the stat whose lowering nature dislikes their flavour.</summary>
        static readonly Dictionary<string, int> ConfuseBerries = new Dictionary<string, int>
        {
            { "FIGYBERRY", S.Attack }, { "WIKIBERRY", S.SpAtk }, { "MAGOBERRY", S.Speed }, { "AGUAVBERRY", S.SpDef }, { "IAPAPABERRY", S.Defense }
        };
        static readonly string[] AsOneAbilities = { "ASONECHILLINGNEIGH", "ASONEGRIMNEIGH", "ASONEGLASTRIER", "ASONESPECTRIER" };
        static readonly string[] Gems = { "NORMAL", "FIRE", "WATER", "ELECTRIC", "GRASS", "ICE", "FIGHTING", "POISON", "GROUND", "FLYING", "PSYCHIC", "BUG", "ROCK", "GHOST", "DRAGON", "DARK", "STEEL", "FAIRY" };

        double _gemBoost = 1;
        readonly HashSet<Battler> _quickClaw = new HashSet<Battler>();
        bool _checkingItems;
        string _fetchBall;

        /// <summary>The held item has its effect: not under Magic Room, Embargo or Klutz.</summary>
        bool ItemWorks(Battler b)
        {
            return b != null && !string.IsNullOrEmpty(b.Pk.Item) && MagicRoom == 0 && b.EmbargoTurns == 0 && !HasAbility(b, "KLUTZ");
        }
        bool Holds(Battler b, string item) { return ItemWorks(b) && b.Pk.Item == item; }

        /// <summary>Unnerve and As One keep the other side from eating Berries.</summary>
        bool CanEatBerries(Battler b)
        {
            return !Live(1 - b.Side).Any(f => HasAbility(f, "UNNERVE") || AsOneAbilities.Any(a => HasAbility(f, a)));
        }

        /// <summary>The held item is used up: berries count as eaten (Belch, Cud Chew, Harvest, Recycle), and an ally with Symbiosis passes its own.</summary>
        void UseUpItem(Battler b)
        {
            string item = b.Pk.Item;
            if (string.IsNullOrEmpty(item)) return;
            NoteItemChange(b.Pk);
            if (item.EndsWith("BERRY"))
            {
                b.ConsumedBerry = item; b.AteBerry = true;
                if (HasAbility(b, "CUDCHEW")) { b.CudChewBerry = item; b.CudChewTurns = 2; }
            }
            else b.ConsumedItem = item;
            b.UsedItemThisTurn = item;
            b.Pk.Item = null;
            var giver = Allies(b).FirstOrDefault(a => HasAbility(a, "SYMBIOSIS") && !string.IsNullOrEmpty(a.Pk.Item));
            if (giver != null && !b.Pk.Fainted)
            {
                NoteItemChange(giver.Pk);
                b.Pk.Item = giver.Pk.Item; giver.Pk.Item = null;
                Splash(giver); Say(Name(giver) + " shared its " + ItemName(b.Pk.Item) + " with " + NameL(b) + "!");
            }
        }

        int Ripened(Battler b, int amount) { return HasAbility(b, "RIPEN") ? amount * 2 : amount; }

        void HealBy(Battler b, int amount, string text)
        {
            int max = b.Pk.TotalHP(P);
            if (b.Pk.HP >= max || b.Pk.Fainted) return;
            int f = b.Pk.HP; b.Pk.HP = Math.Min(max, b.Pk.HP + Math.Max(1, amount)); Hp(b, f);
            if (text != null) Say(text);
        }

        /// <summary>
        /// Eats a berry (held, or eaten by Bug Bite / Pluck / Cud Chew): applies its effect. With <paramref name="always"/>
        /// the effect applies even if its condition isn't met (a berry eaten off another Pokémon). True if it did something.
        /// </summary>
        bool BerryEffect(Battler b, string berry, bool always)
        {
            int max = b.Pk.TotalHP(P), hp = b.Pk.HP;
            string bn = ItemName(berry), st = (b.Pk.Status ?? "").ToUpperInvariant();
            bool gluttony = HasAbility(b, "GLUTTONY");
            bool quarter = hp * (gluttony ? 2 : 4) <= max;
            int stat;
            switch (berry)
            {
                case "ORANBERRY":
                    if (!always && hp * 2 > max || hp >= max) return false;
                    HealBy(b, Ripened(b, 10), Name(b) + " restored its health using its " + bn + "!"); break;
                case "SITRUSBERRY":
                    if (!always && hp * 2 > max || hp >= max) return false;
                    HealBy(b, Ripened(b, max / 4), Name(b) + " restored its health using its " + bn + "!"); break;
                case "BERRYJUICE":
                    if (!always && hp * 2 > max || hp >= max) return false;
                    HealBy(b, 20, Name(b) + " restored its health using its " + bn + "!"); break;
                case "CHERIBERRY": if (st != "PARALYSIS") return false; CureStatus(b, " eating its " + bn); break;
                case "CHESTOBERRY": if (st != "SLEEP") return false; CureStatus(b, " eating its " + bn); break;
                case "PECHABERRY": if (st != "POISON") return false; CureStatus(b, " eating its " + bn); break;
                case "RAWSTBERRY": if (st != "BURN") return false; CureStatus(b, " eating its " + bn); break;
                case "ASPEARBERRY": if (st != "FROZEN") return false; CureStatus(b, " eating its " + bn); break;
                case "PERSIMBERRY":
                    if (b.Confusion == 0) return false;
                    b.Confusion = 0; Say(Name(b) + " snapped out of its confusion using its " + bn + "!"); break;
                case "LUMBERRY":
                    if (st == "" && b.Confusion == 0) return false;
                    if (st != "") CureStatus(b, " eating its " + bn);
                    if (b.Confusion > 0) { b.Confusion = 0; Say(Name(b) + " snapped out of its confusion!"); }
                    break;
                case "LEPPABERRY":
                    {
                        var m = b.Pk.Moves.FirstOrDefault(x => x.PP == 0) ?? (always ? b.Pk.Moves.OrderBy(x => x.PP).FirstOrDefault() : null);
                        if (m == null) return false;
                        var md = Move(m.Id);
                        m.PP = Math.Min(md != null ? md.TotalPP : 99, m.PP + Ripened(b, 10));
                        Say(Name(b) + " restored its " + (md != null ? md.Name : m.Id) + "'s PP using its " + bn + "!");
                        break;
                    }
                case "LANSATBERRY":
                    if (!always && !quarter) return false;
                    b.CritStage += 2; Say(Name(b) + " used its " + bn + " to get pumped!"); break;
                case "STARFBERRY":
                    {
                        if (!always && !quarter) return false;
                        var up = Enumerable.Range(1, 5).Where(s => b.Stages[s] < 6).ToList();
                        if (up.Count == 0) return false;
                        Say(Name(b) + " used its " + bn + "!");
                        ChangeStat(b, up[Rng.Next(up.Count)], Ripened(b, 2));
                        break;
                    }
                case "MICLEBERRY":
                    if (!always && !quarter) return false;
                    b.MicleBoost = true; Say(Name(b) + " boosted the accuracy of its next move using its " + bn + "!"); break;
                case "CUSTAPBERRY":
                    return false; // used when moves are ordered
                default:
                    if (PinchStatBerries.TryGetValue(berry, out stat))
                    {
                        if (!always && !quarter || b.Stages[stat] >= 6) return false;
                        Say(Name(b) + " used its " + bn + "!");
                        ChangeStat(b, stat, Ripened(b, 1));
                        break;
                    }
                    if (ConfuseBerries.TryGetValue(berry, out stat))
                    {
                        if (!always && !quarter || hp >= max) return false;
                        HealBy(b, Ripened(b, max / 3), Name(b) + " restored its health using its " + bn + "!");
                        if (Natures.Lowered(b.Pk.Nature) == stat) Confuse(b, false);
                        break;
                    }
                    return false;
            }
            if (HasAbility(b, "CHEEKPOUCH") && b.Pk.HP < max) { Splash(b); HealBy(b, max / 3, Name(b) + "'s HP was restored."); }
            return true;
        }

        void CureStatus(Battler b, string how)
        {
            string st = (b.Pk.Status ?? "").ToUpperInvariant();
            b.Pk.Status = ""; b.ToxicCount = 0; b.SleepTurns = 0; StatusEvent(b);
            string what = st == "PARALYSIS" ? " was cured of paralysis" : st == "SLEEP" ? " woke up" : st == "POISON" ? " was cured of its poisoning" : st == "BURN" ? "'s burn was healed" : st == "FROZEN" ? " thawed out" : "'s status condition was cured";
            Say(Name(b) + what + (how != null ? " by" + how : "") + "!");
        }

        /// <summary>Checks the items that trigger on their own: berries at low HP or with a status, herbs, seeds, Room Service, Booster Energy.</summary>
        void CheckHeldItems(Battler b)
        {
            if (b == null || b.Pk.Fainted || !ItemWorks(b) || _checkingItems) return;
            _checkingItems = true;
            try
            {
                string item = b.Pk.Item;
                if (item.EndsWith("BERRY") || item == "BERRYJUICE")
                {
                    if ((item != "BERRYJUICE" && !CanEatBerries(b)) || ResistBerries.ContainsKey(item)) return;
                    if (BerryEffect(b, item, false)) UseUpItem(b);
                    return;
                }
                switch (item)
                {
                    case "WHITEHERB":
                        if (Enumerable.Range(1, 7).Any(s => b.Stages[s] < 0))
                        {
                            for (int s = 1; s < 8; s++) if (b.Stages[s] < 0) b.Stages[s] = 0;
                            Say(Name(b) + " returned its stats to normal using its White Herb!");
                            UseUpItem(b);
                        }
                        break;
                    case "MENTALHERB":
                        if (b.AttractedTo != null || b.TauntTurns > 0 || b.EncoreTurns > 0 || b.Tormented || b.DisableTurns > 0 || b.HealBlockTurns > 0)
                        {
                            b.AttractedTo = null; b.TauntTurns = 0; b.EncoreTurns = 0; b.Tormented = false; b.DisableTurns = 0; b.HealBlockTurns = 0;
                            Say(Name(b) + " used its Mental Herb to come back to its senses!");
                            UseUpItem(b);
                        }
                        break;
                    case "ELECTRICSEED": case "GRASSYSEED": case "MISTYSEED": case "PSYCHICSEED":
                        {
                            string terr = item == "ELECTRICSEED" ? "Electric" : item == "GRASSYSEED" ? "Grassy" : item == "MISTYSEED" ? "Misty" : "Psychic";
                            int stat = item == "ELECTRICSEED" || item == "GRASSYSEED" ? S.Defense : S.SpDef;
                            if (Terrain == terr && b.Stages[stat] < 6) { Say(Name(b) + " used its " + ItemName(item) + "!"); ChangeStat(b, stat, 1); UseUpItem(b); }
                            break;
                        }
                    case "ROOMSERVICE":
                        if (TrickRoom > 0 && b.Stages[S.Speed] > -6) { Say(Name(b) + " used its Room Service!"); ChangeStat(b, S.Speed, -1); UseUpItem(b); }
                        break;
                    case "BOOSTERENERGY":
                        if ((HasAbility(b, "PROTOSYNTHESIS") || HasAbility(b, "QUARKDRIVE")) && !b.BoosterEnergy && BoostedStat(b) < 0)
                        {
                            b.BoosterEnergy = true;
                            Say(Name(b) + " used its Booster Energy to activate its " + AbilityName(b) + "!");
                            UseUpItem(b);
                            Say(Name(b) + "'s " + StatName(BoostedStat(b)) + " was heightened!");
                        }
                        break;
                    case "EJECTPACK":
                        if (b.StatsLoweredThisTurn && !b.SwitchOutPending && (b.Side == 0 ? Bench(0) : FoeBench(b.Slot)).Count > 0 && !IsWildFoe(b))
                        {
                            Say(Name(b) + " is switched out by the Eject Pack!");
                            UseUpItem(b); SwitchOutNow(b);
                        }
                        break;
                }
            }
            finally { _checkingItems = false; }
        }

        bool IsWildFoe(Battler b) { return IsWild && b.Side == 1; }

        void CheckAllItems()
        {
            CheckPrimalEnd();
            foreach (var b in OnField.Where(x => x != null && !x.Pk.Fainted).ToList()) UpdateMimicry(b);
            foreach (var b in OnField.Where(x => x != null && !x.Pk.Fainted).OrderByDescending(x => Speed(x)).ToList()) CheckHeldItems(b);
        }

        /// <summary>A type-resist berry halving a super-effective hit (Chilan: any Normal-type hit). Returns the damage multiplier.</summary>
        double ResistBerry(Battler user, Battler target, string type, double typeMod)
        {
            string resist;
            if (!ItemWorks(target) || !ResistBerries.TryGetValue(target.Pk.Item, out resist) || resist != type) return 1;
            if (type != "NORMAL" && typeMod <= 1) return 1;
            if (!CanEatBerries(target)) return 1;
            Say("The " + ItemName(target.Pk.Item) + " weakened the damage to " + NameL(target) + "!");
            double m = HasAbility(target, "RIPEN") ? 0.25 : 0.5;
            UseUpItem(target);
            if (HasAbility(target, "CHEEKPOUCH")) { Splash(target); HealBy(target, target.Pk.TotalHP(P) / 3, Name(target) + "'s HP was restored."); }
            return m;
        }

        /// <summary>A Gem matching the move's type is used up for a 1.3x boost.</summary>
        void UseGem(Battler user, MoveData md, string type, string code)
        {
            _gemBoost = 1;
            if (!ItemWorks(user) || user.Pk.Item != type + "GEM" || code.StartsWith("UserFaints") || md.Category == "Status") return;
            Say("The " + ItemName(user.Pk.Item) + " strengthened " + NameL(user) + "'s power!");
            UseUpItem(user);
            _gemBoost = 1.3;
        }

        /// <summary>Power multipliers from held items: Muscle Band, Wise Glasses, incenses, plates, orbs, masks, Metronome, gems.</summary>
        double ItemPowerMod(Battler user, Battler target, MoveData md, string type)
        {
            double m = _gemBoost;
            if (!ItemWorks(user)) return m;
            string it = user.Pk.Item, sp = user.Pk.Species;
            bool physical = md.Category == "Physical";
            switch (it)
            {
                case "MUSCLEBAND": if (physical) m *= 1.1; break;
                case "WISEGLASSES": if (!physical) m *= 1.1; break;
                case "PUNCHINGGLOVE": if (md.Flags.Contains("Punching")) m *= 1.1; break;
                case "SEAINCENSE": case "WAVEINCENSE": if (type == "WATER") m *= 1.2; break;
                case "ODDINCENSE": if (type == "PSYCHIC") m *= 1.2; break;
                case "ROCKINCENSE": if (type == "ROCK") m *= 1.2; break;
                case "ROSEINCENSE": if (type == "GRASS") m *= 1.2; break;
                case "ADAMANTORB": case "ADAMANTCRYSTAL": if (sp == "DIALGA" && (type == "DRAGON" || type == "STEEL")) m *= 1.2; break;
                case "LUSTROUSORB": case "LUSTROUSGLOBE": if (sp == "PALKIA" && (type == "DRAGON" || type == "WATER")) m *= 1.2; break;
                case "GRISEOUSORB": case "GRISEOUSCORE": if (sp == "GIRATINA" && (type == "DRAGON" || type == "GHOST")) m *= 1.2; break;
                case "SOULDEW": if ((sp == "LATIOS" || sp == "LATIAS") && (type == "DRAGON" || type == "PSYCHIC")) m *= 1.2; break;
                case "CORNERSTONEMASK": case "WELLSPRINGMASK": case "HEARTHFLAMEMASK": if (sp == "OGERPON") m *= 1.2; break;
                case "METRONOME": m *= 1 + 0.2 * Math.Min(5, user.MetronomeCount); break;
                default:
                    if (it.EndsWith("PLATE"))
                    {
                        var map = new Dictionary<string, string> { { "FLAMEPLATE", "FIRE" }, { "SPLASHPLATE", "WATER" }, { "ZAPPLATE", "ELECTRIC" }, { "MEADOWPLATE", "GRASS" }, { "ICICLEPLATE", "ICE" }, { "FISTPLATE", "FIGHTING" }, { "TOXICPLATE", "POISON" }, { "EARTHPLATE", "GROUND" }, { "SKYPLATE", "FLYING" }, { "MINDPLATE", "PSYCHIC" }, { "INSECTPLATE", "BUG" }, { "STONEPLATE", "ROCK" }, { "SPOOKYPLATE", "GHOST" }, { "DRACOPLATE", "DRAGON" }, { "DREADPLATE", "DARK" }, { "IRONPLATE", "STEEL" }, { "PIXIEPLATE", "FAIRY" } };
                        string pt; if (map.TryGetValue(it, out pt) && pt == type) m *= 1.2;
                    }
                    break;
            }
            return m;
        }

        /// <summary>Defense multipliers from species items (Deep Sea Scale, Metal Powder).</summary>
        double ItemDefenseMod(Battler target, bool physical)
        {
            if (!ItemWorks(target)) return 1;
            if (!physical && target.Pk.Item == "DEEPSEASCALE" && target.Pk.Species == "CLAMPERL") return 2;
            if (physical && target.Pk.Item == "METALPOWDER" && target.Pk.Species == "DITTO" && target.StatsOverride == null) return 2;
            return 1;
        }

        double ItemSpeedMod(Battler b)
        {
            if (!ItemWorks(b)) return 1;
            if (b.Pk.Item == "QUICKPOWDER" && b.Pk.Species == "DITTO" && b.StatsOverride == null) return 2;
            if (b.Pk.Item == "IRONBALL") return 0.5;
            return 1;
        }

        /// <summary>
        /// Order within a priority bracket: Quick Claw, Custap Berry and Quick Draw go first; Lagging Tail, Full Incense,
        /// Stall and Mycelium Might (status moves) go last.
        /// </summary>
        int SubPriority(Battler b, MoveData md)
        {
            if (md == null) return 0;
            int max = b.Pk.TotalHP(P);
            if (Holds(b, "QUICKCLAW") && Rng.Next(100) < 20) { _quickClaw.Add(b); return 1; }
            if (Holds(b, "CUSTAPBERRY") && CanEatBerries(b) && b.Pk.HP * (HasAbility(b, "GLUTTONY") ? 2 : 4) <= max) { _quickClaw.Add(b); return 1; }
            if (HasAbility(b, "QUICKDRAW") && md.Category != "Status" && Rng.Next(100) < 30) { _quickClaw.Add(b); return 1; }
            if (Holds(b, "LAGGINGTAIL") || Holds(b, "FULLINCENSE") || HasAbility(b, "STALL")) return -1;
            if (HasAbility(b, "MYCELIUMMIGHT") && md.Category == "Status") return -1;
            return 0;
        }

        /// <summary>Shows what let a Pokémon move first (Quick Claw, Custap Berry, Quick Draw) just before it acts.</summary>
        void AnnounceMoveFirst(Battler b)
        {
            if (!_quickClaw.Remove(b)) return;
            if (Holds(b, "QUICKCLAW")) Say(Name(b) + " can act faster than normal, thanks to its Quick Claw!");
            else if (Holds(b, "CUSTAPBERRY")) { Say(Name(b) + " can act faster than normal, thanks to its Custap Berry!"); UseUpItem(b); }
            else if (HasAbility(b, "QUICKDRAW")) { Splash(b); Say(Name(b) + " can act faster than normal, thanks to its Quick Draw!"); }
        }

        /// <summary>Items that react to a damaging hit: on the target (Weakness Policy, Air Balloon, Kee Berry, Red Card...) and on the attacker (Shell Bell, Throat Spray).</summary>
        void ItemsAfterHit(Battler user, Battler target, MoveData md, string type, double typeMod, int total, bool contact)
        {
            if (total <= 0 || user == target) return;
            bool physical = md.Category == "Physical";
            if (!target.Pk.Fainted && ItemWorks(target))
            {
                string it = target.Pk.Item;
                int max = target.Pk.TotalHP(P);
                switch (it)
                {
                    case "AIRBALLOON": Say(Name(target) + "'s Air Balloon popped!"); UseUpItem(target); break;
                    case "WEAKNESSPOLICY":
                        if (typeMod > 1 && (target.Stages[S.Attack] < 6 || target.Stages[S.SpAtk] < 6))
                        { Say("The Weakness Policy was activated!"); UseUpItem(target); ChangeStat(target, S.Attack, 2); ChangeStat(target, S.SpAtk, 2); }
                        break;
                    case "ABSORBBULB": if (type == "WATER") ItemStat(target, S.SpAtk); break;
                    case "CELLBATTERY": if (type == "ELECTRIC") ItemStat(target, S.Attack); break;
                    case "LUMINOUSMOSS": if (type == "WATER") ItemStat(target, S.SpDef); break;
                    case "SNOWBALL": if (type == "ICE") ItemStat(target, S.Attack); break;
                    case "KEEBERRY": if (physical && CanEatBerries(target) && target.Stages[S.Defense] < 6) { Say(Name(target) + " used its Kee Berry!"); UseUpItem(target); ChangeStat(target, S.Defense, Ripened(target, 1)); } break;
                    case "MARANGABERRY": if (!physical && CanEatBerries(target) && target.Stages[S.SpDef] < 6) { Say(Name(target) + " used its Maranga Berry!"); UseUpItem(target); ChangeStat(target, S.SpDef, Ripened(target, 1)); } break;
                    case "ENIGMABERRY": if (typeMod > 1 && CanEatBerries(target) && target.Pk.HP < max) { UseUpItem(target); HealBy(target, Ripened(target, max / 4), Name(target) + " restored its health using its Enigma Berry!"); } break;
                    case "JABOCABERRY": case "ROWAPBERRY":
                        if ((it == "JABOCABERRY") == physical && CanEatBerries(target) && !user.Pk.Fainted)
                        { UseUpItem(target); Residual(user, Math.Max(1, Ripened(target, user.Pk.TotalHP(P) / 8)), Name(user) + " was hurt by " + NameL(target) + "'s " + ItemName(it) + "!"); }
                        break;
                    case "STICKYBARB":
                        if (contact && !user.Pk.Fainted && string.IsNullOrEmpty(user.Pk.Item)) { NoteItemChange(target.Pk); NoteItemChange(user.Pk); user.Pk.Item = it; target.Pk.Item = null; Say("The Sticky Barb attached itself to " + NameL(user) + "!"); }
                        break;
                    case "REDCARD":
                        if (!user.Pk.Fainted && !IsWildFoe(user) && (user.Side == 0 ? Bench(0) : FoeBench(user.Slot)).Count > 0 && !HasAbility(user, "GUARDDOG"))
                        {
                            Say(Name(target) + " held up its Red Card against " + NameL(user) + "!");
                            UseUpItem(target);
                            ForceSwitch(user);
                        }
                        break;
                    case "EJECTBUTTON":
                        if (!target.SwitchOutPending && !IsWildFoe(target) && (target.Side == 0 ? Bench(0) : FoeBench(target.Slot)).Count > 0)
                        { Say(Name(target) + " is switched out with the Eject Button!"); UseUpItem(target); SwitchOutNow(target); }
                        break;
                }
            }
            if (!user.Pk.Fainted && ItemWorks(user))
            {
                if (user.Pk.Item == "SHELLBELL") HealBy(user, Math.Max(1, total / 8), Name(user) + " restored a little HP using its Shell Bell!");
            }
        }

        void ItemStat(Battler b, int stat)
        {
            if (b.Stages[stat] >= 6) return;
            Say(Name(b) + " used its " + ItemName(b.Pk.Item) + "!");
            UseUpItem(b);
            ChangeStat(b, stat, 1);
        }

        /// <summary>Throat Spray after a sound move; Blunder Policy after a miss.</summary>
        void ItemsAfterMove(Battler user, MoveData md, bool missed)
        {
            if (user.Pk.Fainted || !ItemWorks(user)) return;
            if (user.Pk.Item == "THROATSPRAY" && md.Flags.Contains("Sound") && md.Category != "Status" && !missed && user.Stages[S.SpAtk] < 6)
            { Say(Name(user) + " used its Throat Spray!"); UseUpItem(user); ChangeStat(user, S.SpAtk, 1); }
            if (user.Pk.Item == "BLUNDERPOLICY" && missed && !(md.FunctionCode ?? "").StartsWith("OHKO") && user.Stages[S.Speed] < 6)
            { Say(Name(user) + " used its Blunder Policy!"); UseUpItem(user); ChangeStat(user, S.Speed, 2); }
        }

        /// <summary>End-of-turn items: Black Sludge, Sticky Barb, Flame Orb, Toxic Orb.</summary>
        void ItemsEndOfTurn(Battler b)
        {
            if (b.Pk.Fainted || !ItemWorks(b)) return;
            int max = b.Pk.TotalHP(P);
            switch (b.Pk.Item)
            {
                case "BLACKSLUDGE":
                    if (TypesOf(b).Contains("POISON")) HealBy(b, max / 16, Name(b) + " restored a little HP using its Black Sludge!");
                    else Residual(b, Math.Max(1, max / 8), Name(b) + " is hurt by its Black Sludge!");
                    break;
                case "STICKYBARB": Residual(b, Math.Max(1, max / 8), Name(b) + " is hurt by its Sticky Barb!"); break;
                case "FLAMEORB": if (string.IsNullOrEmpty(b.Pk.Status) && CanStatus(b, "BURN", false)) { Inflict(b, "BURN", false); } break;
                case "TOXICORB": if (string.IsNullOrEmpty(b.Pk.Status) && CanStatus(b, "TOXIC", false)) { Inflict(b, "TOXIC", false); } break;
            }
        }

        /// <summary>Items that show when the holder comes out (Air Balloon), then the self-triggering ones.</summary>
        void ItemsOnSwitchIn(Battler b)
        {
            if (b.Pk.Fainted) return;
            if (Holds(b, "AIRBALLOON")) Say(Name(b) + " floats in the air with its Air Balloon!");
            CheckHeldItems(b);
        }

        /// <summary>Adrenaline Orb: being intimidated raises Speed.</summary>
        void Intimidated(Battler b)
        {
            if (Holds(b, "ADRENALINEORB") && b.Stages[S.Speed] < 6) { Say(Name(b) + " used its Adrenaline Orb!"); UseUpItem(b); ChangeStat(b, S.Speed, 1); }
        }

        /// <summary>Pickup (picks up an item another Pokémon used this turn), Ball Fetch, and Cud Chew at the end of the turn.</summary>
        void ItemAbilitiesEndOfTurn(Battler b)
        {
            if (b.Pk.Fainted) return;
            if (HasAbility(b, "PICKUP") && string.IsNullOrEmpty(b.Pk.Item))
            {
                var src = OnField.Where(x => x != b && x.UsedItemThisTurn != null).ToList();
                if (src.Count > 0)
                {
                    var from = src[Rng.Next(src.Count)];
                    NoteItemChange(b.Pk); b.Pk.Item = from.UsedItemThisTurn; from.UsedItemThisTurn = null;
                    Splash(b); Say(Name(b) + " found one " + ItemName(b.Pk.Item) + "!");
                }
            }
            if (HasAbility(b, "BALLFETCH") && string.IsNullOrEmpty(b.Pk.Item) && _fetchBall != null && b.Side == 0)
            {
                NoteItemChange(b.Pk); b.Pk.Item = _fetchBall; _fetchBall = "";
                Splash(b); Say(Name(b) + " found a " + ItemName(b.Pk.Item) + "!");
            }
            if (b.CudChewTurns > 0 && --b.CudChewTurns == 0 && b.CudChewBerry != null && HasAbility(b, "CUDCHEW"))
            {
                Splash(b);
                string berry = b.CudChewBerry; b.CudChewBerry = null;
                if (!BerryEffect(b, berry, true)) Say(Name(b) + " ate its " + ItemName(berry) + " again!");
            }
        }

        /// <summary>A failed throw: Ball Fetch picks up the first Poké Ball that didn't catch the Pokémon.</summary>
        bool _missedThisMove;

        /// <summary>Destiny Knot: a holder that falls in love makes the other Pokémon fall in love too.</summary>
        void DestinyKnot(Battler lover, Battler by)
        {
            if (Holds(lover, "DESTINYKNOT") && by.AttractedTo == null && !by.Pk.Fainted && !HasAbility(by, "OBLIVIOUS"))
            { by.AttractedTo = lover; Say(Name(by) + " fell in love from the Destiny Knot!"); }
        }

        void BallFailed(string ball) { if (_fetchBall == null) _fetchBall = ball; }

        /// <summary>Pickup and Honey Gather after a battle: the Pokémon may find an item (only if it holds none).</summary>
        public static List<string> PostBattlePickup(PokeCreator.Projects.Project p, List<Pokemon> party, Random rng)
        {
            var found = new List<string>();
            string[] common = { "POTION", "ANTIDOTE", "SUPERPOTION", "GREATBALL", "REPEL", "ESCAPEROPE", "FULLHEAL", "HYPERPOTION", "ULTRABALL", "REVIVE", "RARECANDY", "SUNSTONE", "MOONSTONE", "HEARTSCALE", "FULLRESTORE", "MAXREVIVE", "PPUP", "MAXELIXIR" };
            foreach (var pk in party)
            {
                if (pk == null || pk.IsEgg || pk.Fainted || !string.IsNullOrEmpty(pk.Item)) continue;
                string ab = pk.AbilityId(p) ?? "";
                string item = null;
                if (ab == "PICKUP" && rng.Next(10) == 0)
                {
                    int tier = Math.Min(8, (pk.Level - 1) / 10);
                    int r = rng.Next(100);
                    int idx = tier + (r < 30 ? 0 : r < 40 ? 1 : r < 50 ? 2 : r < 60 ? 3 : r < 70 ? 4 : r < 80 ? 5 : r < 90 ? 6 : r < 94 ? 7 : r < 98 ? 8 : 9);
                    item = common[Math.Min(common.Length - 1, idx)];
                }
                else if (ab == "HONEYGATHER" && rng.Next(100) < 5 + (pk.Level - 1) / 10 * 5) item = "HONEY";
                if (item != null && p.Items.Any(i => i.Id == item)) { pk.Item = item; found.Add(item); }
            }
            return found;
        }
    }
}
