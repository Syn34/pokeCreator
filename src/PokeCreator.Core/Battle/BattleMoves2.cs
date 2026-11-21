using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;
using S = PokeCreator.Pkmn.Stat;

namespace PokeCreator.Battles
{
    /// <summary>
    /// The rarer move effects: type changes (Soak, Reflect Type, Conversion, Burn Up), Transform, Mimic, Sketch, Trick,
    /// Heal Block, Torment, Imprison, Laser Focus, rooms, sports, Fling, Natural Gift, Beat Up, Present, Retaliate,
    /// Echoed Voice, Round, Fusion moves, Spectral Thief, Throat Chop, Feint, Instruct, After You, Quash, Ally Switch...
    /// </summary>
    public partial class Battle
    {
        public readonly int[] LuckyChant = new int[2];
        /// <summary>Pledge combinations: sea of fire (hurts each turn), swamp (quarter Speed), rainbow (double effect chances).</summary>
        public readonly int[] SeaOfFire = new int[2], Swamp = new int[2], Rainbow = new int[2];
        bool _pursuitBoost;
        Battler _pledgeUser; string _pledgeCode;
        double _pledgeBoost = 1;
        public int MudSport, WaterSport, MagicRoom, WonderRoom, FairyLock;
        readonly bool[] _faintedLastTurn = new bool[2], _faintedThisTurn = new bool[2];
        int _echoedVoice; bool _echoedThisTurn;
        bool _roundUsedThisTurn, _ionDeluge;
        string _lastMoveThisTurn;
        readonly Dictionary<Pokemon, string> _itemBeforeBattle = new Dictionary<Pokemon, string>();
        List<KeyValuePair<Battler, BattleCommand>> _orders; int _orderIndex;

        static readonly Dictionary<string, int> FlingPower = new Dictionary<string, int>
        {
            { "IRONBALL", 130 }, { "HARDSTONE", 100 }, { "RAREBONE", 100 }, { "THICKCLUB", 90 }, { "DEEPSEATOOTH", 90 }, { "GRIPCLAW", 90 },
            { "POWERBRACER", 70 }, { "POWERBELT", 70 }, { "POWERLENS", 70 }, { "POWERBAND", 70 }, { "POWERANKLET", 70 }, { "POWERWEIGHT", 70 },
            { "DRAGONFANG", 70 }, { "POISONBARB", 70 }, { "ASSAULTVEST", 80 }, { "QUICKCLAW", 80 }, { "STICKYBARB", 80 }, { "ROCKYHELMET", 60 },
            { "LEFTOVERS", 10 }, { "SITRUSBERRY", 10 }, { "ORANBERRY", 10 }, { "LIFEORB", 30 }, { "CHOICEBAND", 10 }, { "CHOICESCARF", 10 }, { "CHOICESPECS", 10 }, { "KINGSROCK", 30 }, { "RAZORFANG", 30 }
        };

        static string NaturalGiftType(string berry)
        {
            switch (berry)
            {
                case "CHERIBERRY": case "OCCABERRY": return "FIRE"; case "CHESTOBERRY": case "PASSHOBERRY": return "WATER";
                case "PECHABERRY": case "WACANBERRY": return "ELECTRIC"; case "RAWSTBERRY": case "RINDOBERRY": return "GRASS";
                case "ASPEARBERRY": case "YACHEBERRY": return "ICE"; case "LEPPABERRY": case "CHOPLEBERRY": return "FIGHTING";
                case "ORANBERRY": case "KEBIABERRY": return "POISON"; case "PERSIMBERRY": case "SHUCABERRY": return "GROUND";
                case "LUMBERRY": case "COBABERRY": return "FLYING"; case "SITRUSBERRY": case "PAYAPABERRY": return "PSYCHIC";
                case "FIGYBERRY": case "TANGABERRY": return "BUG"; case "WIKIBERRY": case "CHARTIBERRY": return "ROCK";
                case "MAGOBERRY": case "KASIBBERRY": return "GHOST"; case "AGUAVBERRY": case "HABANBERRY": return "DRAGON";
                case "IAPAPABERRY": case "COLBURBERRY": return "DARK"; case "ROSELIBERRY": return "FAIRY"; case "CHILANBERRY": return "NORMAL";
                case "BABIRIBERRY": return "STEEL";
            }
            return "NORMAL";
        }

        static bool IsHealingMove(MoveData md)
        {
            string c = md.FunctionCode ?? "";
            return c.StartsWith("HealUser") || c.StartsWith("HealTarget") || c.Contains("HealUserBy") || c == "UserTargetAverageHP" || c == "HealUserFullyAndFallAsleep";
        }

        /// <summary>Restrictions on top of Taunt / Disable / Encore: Heal Block, Torment, Imprison, Throat Chop. Null = allowed.</summary>
        string MoreMoveRestriction(Battler b, MoveData md)
        {
            if (md == null) return null;
            string mn = md.Name ?? md.Id;
            if (b.HealBlockTurns > 0 && IsHealingMove(md)) return Name(b) + " can't use " + mn + " because of Heal Block!";
            if (b.Tormented && md.Id == b.LastMoveUsed && md.Id != "STRUGGLE") return Name(b) + " can't use the same move twice in a row due to the torment!";
            if (b.ThroatChopTurns > 0 && md.Flags.Contains("Sound")) return Name(b) + " can't use " + mn + " because of Throat Chop!";
            if (Live(1 - b.Side).Any(f => f.Imprisoning && f.Pk.Moves.Any(m => m.Id == md.Id))) return Name(b) + " can't use its sealed " + mn + "!";
            return null;
        }

        void ReplaceMovesForBattle(Battler b, List<PokemonMove> moves)
        {
            if (b.OriginalMoves == null) b.OriginalMoves = b.Pk.Moves;
            b.Pk.Moves = moves;
        }

        /// <summary>Puts back moves changed for the battle (Transform, Mimic) when the Pokémon leaves the field.</summary>
        void RestoreBattleMoves(Battler b)
        {
            if (b == null || b.OriginalMoves == null) return;
            // PP used on real moves carries over
            foreach (var m in b.OriginalMoves) { var cur = b.Pk.Moves.FirstOrDefault(x => x.Id == m.Id && !b.TransformedMoves); if (cur != null && !ReferenceEquals(cur, m)) m.PP = cur.PP; }
            b.Pk.Moves = b.OriginalMoves; b.OriginalMoves = null;
        }

        void NoteItemChange(Pokemon pk)
        {
            if (Parties[0].Contains(pk) && !_itemBeforeBattle.ContainsKey(pk)) _itemBeforeBattle[pk] = pk.Item;
        }

        bool MoreStatusMoves(Battler user, Battler target, MoveData md, string code)
        {
            string n = Name(user), tn = Name(target);
            var al = Ally(user);
            switch (code)
            {
                case "SetTargetTypesToWater":
                case "SetTargetTypesToPsychic":
                    {
                        string t = code.EndsWith("Water") ? "WATER" : "PSYCHIC";
                        if (TypesOf(target).Count == 1 && TypesOf(target)[0] == t) { Say("But it failed!"); return true; }
                        target.TypesOverride = new List<string> { t };
                        Say(tn + " transformed into the " + TypeName(t) + " type!"); return true;
                    }
                case "SetUserTypesToTargetTypes":
                    user.TypesOverride = new List<string>(TypesOf(target));
                    Say(n + "'s type changed to match " + NameL(target) + "'s!"); return true;
                case "SetUserTypesToUserMoveType":
                    {
                        var mv = user.Pk.Moves.Select(m => Move(m.Id)).FirstOrDefault(m => m != null && !TypesOf(user).Contains(m.Type));
                        if (mv == null) { Say("But it failed!"); return true; }
                        user.TypesOverride = new List<string> { mv.Type };
                        Say(n + " transformed into the " + TypeName(mv.Type) + " type!"); return true;
                    }
                case "SetUserTypesToResistLastAttack":
                    {
                        if (string.IsNullOrEmpty(user.LastHitType)) { Say("But it failed!"); return true; }
                        var opts = P.Types.Where(t => t.Resistances.Contains(user.LastHitType) || t.Immunities.Contains(user.LastHitType)).Select(t => t.Id).ToList();
                        if (opts.Count == 0) { Say("But it failed!"); return true; }
                        string pick = opts[Rng.Next(opts.Count)];
                        user.TypesOverride = new List<string> { pick };
                        Say(n + " transformed into the " + TypeName(pick) + " type!"); return true;
                    }
                case "SetUserTypesBasedOnEnvironment":
                    {
                        string t = Terrain == "Electric" ? "ELECTRIC" : Terrain == "Grassy" ? "GRASS" : Terrain == "Misty" ? "FAIRY" : Terrain == "Psychic" ? "PSYCHIC" : EnvironmentType();
                        user.TypesOverride = new List<string> { t };
                        Say(n + " transformed into the " + TypeName(t) + " type!"); return true;
                    }
                case "TransformUserIntoTarget":
                    {
                        if (user.TransformedMoves || target.TransformedMoves || target.SubstituteHP > 0) { Say("But it failed!"); return true; }
                        user.TypesOverride = new List<string>(TypesOf(target));
                        var st = (int[])StatsOf(target).Clone(); st[0] = StatsOf(user)[0];
                        user.StatsOverride = st;
                        for (int i = 0; i < 8; i++) user.Stages[i] = target.Stages[i];
                        user.AbilityOverride = AbilityOf(target);
                        ReplaceMovesForBattle(user, target.Pk.Moves.Select(m => new PokemonMove { Id = m.Id, PP = 5 }).ToList());
                        user.TransformedMoves = true;
                        Say(n + " transformed into " + target.Pk.Name(P) + "!"); return true;
                    }
                case "ReplaceMoveThisBattleWithTargetLastMoveUsed":
                case "ReplaceMoveWithTargetLastMoveUsed":
                    {
                        var lm = string.IsNullOrEmpty(target.LastMoveUsed) ? null : Move(target.LastMoveUsed);
                        int slot = user.Pk.Moves.FindIndex(m => m.Id == md.Id);
                        if (lm == null || slot < 0 || user.Pk.Moves.Any(m => m.Id == lm.Id) || lm.Id == "STRUGGLE" || (lm.FunctionCode ?? "").StartsWith("Replace")) { Say("But it failed!"); return true; }
                        if (code == "ReplaceMoveWithTargetLastMoveUsed") user.Pk.Moves[slot] = new PokemonMove { Id = lm.Id, PP = Math.Max(1, lm.TotalPP) }; // Sketch: for good
                        else
                        {
                            var copy = user.Pk.Moves.Select(m => new PokemonMove { Id = m.Id, PP = m.PP, PPUps = m.PPUps }).ToList();
                            copy[slot] = new PokemonMove { Id = lm.Id, PP = 5 };
                            ReplaceMovesForBattle(user, copy);
                        }
                        Say(n + (code == "ReplaceMoveWithTargetLastMoveUsed" ? " sketched " : " learned ") + (lm.Name ?? lm.Id) + "!"); return true;
                    }
                case "UserTargetSwapItems":
                    {
                        if ((string.IsNullOrEmpty(user.Pk.Item) && string.IsNullOrEmpty(target.Pk.Item)) || TargetHas(user, target, "STICKYHOLD") || (IsWild && target.Side == 1 && false)) { Say("But it failed!"); return true; }
                        NoteItemChange(user.Pk); NoteItemChange(target.Pk);
                        string a = user.Pk.Item; user.Pk.Item = target.Pk.Item; target.Pk.Item = a;
                        Say(n + " switched items with its target!");
                        if (!string.IsNullOrEmpty(user.Pk.Item)) Say(n + " obtained " + ItemName(user.Pk.Item) + ".");
                        if (!string.IsNullOrEmpty(target.Pk.Item)) Say(tn + " obtained " + ItemName(target.Pk.Item) + ".");
                        return true;
                    }
                case "TargetTakesUserItem":
                    if (string.IsNullOrEmpty(user.Pk.Item) || !string.IsNullOrEmpty(target.Pk.Item)) { Say("But it failed!"); return true; }
                    NoteItemChange(user.Pk); NoteItemChange(target.Pk);
                    target.Pk.Item = user.Pk.Item; user.Pk.Item = null;
                    Say(tn + " received " + ItemName(target.Pk.Item) + " from " + NameL(user) + "!"); return true;
                case "RestoreUserConsumedItem":
                    if (!string.IsNullOrEmpty(user.Pk.Item) || user.ConsumedBerry == null) { Say("But it failed!"); return true; }
                    user.Pk.Item = user.ConsumedBerry; user.ConsumedBerry = null;
                    Say(n + " found one " + ItemName(user.Pk.Item) + "!"); return true;
                case "UserSwapBaseAtkDef":
                    {
                        var st = (int[])StatsOf(user).Clone(); int t = st[S.Attack]; st[S.Attack] = st[S.Defense]; st[S.Defense] = t;
                        user.StatsOverride = st; Say(n + " switched its Attack and Defense!"); return true;
                    }
                case "UserTargetAverageBaseAtkSpAtk":
                case "UserTargetAverageBaseDefSpDef":
                    {
                        int a = code.Contains("Atk") ? S.Attack : S.Defense, b2 = code.Contains("Atk") ? S.SpAtk : S.SpDef;
                        var su = (int[])StatsOf(user).Clone(); var stg = (int[])StatsOf(target).Clone();
                        int avA = (su[a] + stg[a]) / 2, avB = (su[b2] + stg[b2]) / 2;
                        su[a] = stg[a] = avA; su[b2] = stg[b2] = avB;
                        user.StatsOverride = su; target.StatsOverride = stg;
                        Say(n + (code.Contains("Atk") ? " shared its power with the target!" : " shared its guard with the target!")); return true;
                    }
                case "UserTargetSwapBaseSpeed":
                    {
                        var su = (int[])StatsOf(user).Clone(); var stg = (int[])StatsOf(target).Clone();
                        int t = su[S.Speed]; su[S.Speed] = stg[S.Speed]; stg[S.Speed] = t;
                        user.StatsOverride = su; target.StatsOverride = stg;
                        Say(n + " switched Speed with its target!"); return true;
                    }
                case "DisableTargetHealingMoves":
                    if (target.HealBlockTurns > 0) { Say("But it failed!"); return true; }
                    target.HealBlockTurns = 5; Say(tn + " was prevented from healing!"); return true;
                case "DisableTargetUsingSameMoveConsecutively":
                    if (target.Tormented) { Say("But it failed!"); return true; }
                    target.Tormented = true; Say(tn + " was subjected to torment!"); return true;
                case "DisableTargetMovesKnownByUser":
                    if (user.Imprisoning) { Say("But it failed!"); return true; }
                    user.Imprisoning = true; Say(n + " sealed any moves its target shares with it!"); return true;
                case "StartTargetCannotUseItem":
                    target.EmbargoTurns = 5; Say(tn + " can't use items anymore!"); return true;
                case "EnsureNextCriticalHit":
                    user.LaserFocus = 2; Say(n + " concentrated intensely!"); return true;
                case "StartPreventCriticalHitsAgainstUserSide":
                    if (LuckyChant[user.Side] > 0) { Say("But it failed!"); return true; }
                    LuckyChant[user.Side] = 5; Say("The Lucky Chant shielded " + SideName(user.Side, IsWild) + " from critical hits!"); return true;
                case "StartWeakenElectricMoves":
                    if (MudSport > 0) { Say("But it failed!"); return true; }
                    MudSport = 5; Say("Electricity's power was weakened!"); return true;
                case "StartWeakenFireMoves":
                    if (WaterSport > 0) { Say("But it failed!"); return true; }
                    WaterSport = 5; Say("Fire's power was weakened!"); return true;
                case "StartNegateHeldItems":
                    MagicRoom = MagicRoom > 0 ? 0 : 5;
                    Say(MagicRoom > 0 ? "It created a bizarre area in which Pokémon's held items lose their effects!" : "The area returned to normal!"); return true;
                case "StartSwapAllBattlersBaseDefensiveStats":
                    WonderRoom = WonderRoom > 0 ? 0 : 5;
                    Say(WonderRoom > 0 ? "It created a bizarre area in which Defense and Sp. Def stats are swapped!" : "Wonder Room wore off, and Defense and Sp. Def stats returned to normal!"); return true;
                case "TrapAllBattlersInBattleForOneTurn":
                    if (FairyLock > 0) { Say("But it failed!"); return true; }
                    FairyLock = 2; Say("No one will be able to run away during the next turn!"); return true;
                case "BounceBackProblemCausingStatusMoves":
                    user.MagicCoat = true; Say(n + " shrouded itself with Magic Coat!"); return true;
                case "StartRaiseUserAtk1WhenDamaged":
                    user.Rage = true; return true;
                case "SetAttackerMovePPTo0IfUserFaints":
                    user.Grudge = true; Say(n + " wants its target to bear a grudge!"); return true;
                case "TargetNextFireMoveDamagesTarget":
                    target.Powdered = true; Say(tn + " is covered in powder!"); return true;
                case "TargetMovesBecomeElectric":
                    target.Electrified = true; Say(tn + "'s moves have been electrified!"); return true;
                case "RedirectAllMovesToTarget":
                    if (!IsDouble) { Say("But it failed!"); return true; }
                    _centerOfAttention[target.Side] = target; Say(tn + " became the center of attention!"); return true;
                case "RaiseAlliesAtkDef1":
                    if (al == null) { Say("But it failed!"); return true; }
                    ChangeStat(al, S.Attack, 1); ChangeStat(al, S.Defense, 1); return true;
                case "RaiseGrassBattlersDef1":
                    {
                        var grass = OnField.Where(b => !b.Pk.Fainted && TypesOf(b).Contains("GRASS")).ToList();
                        if (grass.Count == 0) { Say("But it failed!"); return true; }
                        foreach (var b in grass) ChangeStat(b, S.Defense, 1);
                        return true;
                    }
                case "RaiseGroundedGrassBattlersAtkSpAtk1":
                    {
                        var grass = OnField.Where(b => !b.Pk.Fainted && TypesOf(b).Contains("GRASS") && IsGrounded(b)).ToList();
                        if (grass.Count == 0) { Say("But it failed!"); return true; }
                        foreach (var b in grass) { ChangeStat(b, S.Attack, 1); ChangeStat(b, S.SpAtk, 1); }
                        return true;
                    }
                case "RaisePlusMinusUserAndAlliesAtkSpAtk1":
                case "RaisePlusMinusUserAndAlliesDefSpDef1":
                    {
                        var who = Live(user.Side).Where(b => HasAbility(b, "PLUS") || HasAbility(b, "MINUS")).ToList();
                        if (who.Count == 0) { Say("But it failed!"); return true; }
                        foreach (var b in who)
                        {
                            if (code.Contains("Atk")) { ChangeStat(b, S.Attack, 1); ChangeStat(b, S.SpAtk, 1); }
                            else { ChangeStat(b, S.Defense, 1); ChangeStat(b, S.SpDef, 1); }
                        }
                        return true;
                    }
                case "AllBattlersConsumeBerry":
                    {
                        bool any = false;
                        foreach (var b in OnField.Where(x => !x.Pk.Fainted && !string.IsNullOrEmpty(x.Pk.Item) && x.Pk.Item.EndsWith("BERRY")).ToList())
                        {
                            any = true; int max = b.Pk.TotalHP(P);
                            if (b.Pk.Item == "SITRUSBERRY" || b.Pk.Item == "ORANBERRY") { int f = b.Pk.HP; b.Pk.HP = Math.Min(max, b.Pk.HP + (b.Pk.Item == "ORANBERRY" ? 10 : max / 4)); if (b.Pk.HP > f) Hp(b, f); }
                            Say(Name(b) + " ate its " + ItemName(b.Pk.Item) + "!");
                            NoteItemChange(b.Pk); b.ConsumedBerry = b.Pk.Item; b.Pk.Item = null;
                        }
                        if (!any) Say("But it failed!");
                        return true;
                    }
                case "TargetUsesItsLastUsedMoveAgain":
                    {
                        var lm = string.IsNullOrEmpty(target.LastMoveUsed) ? null : Move(target.LastMoveUsed);
                        if (lm == null || !CanBeCalled(lm) || lm.FunctionCode == md.FunctionCode || target.Charging != null) { Say("But it failed!"); return true; }
                        Say(tn + " followed " + NameL(user) + "'s instructions!");
                        CallMove(target, OpponentFor(target, null), lm);
                        return true;
                    }
                case "UseMoveTargetIsAboutToUse":
                    {
                        var pm = target.PlannedMove != null ? Move(target.PlannedMove) : null;
                        if (pm == null || target.ActedThisTurn || pm.Category == "Status" || !CanBeCalled(pm)) { Say("But it failed!"); return true; }
                        CallMove(user, target, pm);
                        return true;
                    }
                case "TargetActsNext":
                case "TargetActsLast":
                    {
                        if (_orders == null) { Say("But it failed!"); return true; }
                        int idx = _orders.FindIndex(o => o.Key == target);
                        if (idx <= _orderIndex) { Say("But it failed!"); return true; }
                        var ord = _orders[idx]; _orders.RemoveAt(idx);
                        if (code == "TargetActsNext") { _orders.Insert(_orderIndex + 1, ord); Say(tn + " took the kind offer!"); }
                        else { _orders.Add(ord); Say(tn + "'s move was postponed!"); }
                        return true;
                    }
                case "UserSwapsPositionsWithAlly":
                    {
                        if (al == null) { Say("But it failed!"); return true; }
                        int s1 = user.Slot, s2 = al.Slot, side = user.Side;
                        Field[side][s1] = al; Field[side][s2] = user; user.Slot = s2; al.Slot = s1;
                        foreach (var b in new[] { user, al })
                        {
                            _ev.Add(new BattleEvent { Kind = BattleEventKind.Withdraw, Side = side, Party = b.PartyIndex, Slot = b == user ? s1 : s2 });
                        }
                        SendOutEvent(user); SendOutEvent(al);
                        Say(n + " and " + NameL(al) + " switched places!"); return true;
                    }
                case "RaiseTargetRandomStat2":
                    {
                        var opts = Enumerable.Range(1, 7).Where(st => target.Stages[st] < 6).ToList();
                        if (opts.Count == 0) { Say("But it failed!"); return true; }
                        ChangeStat(target, opts[Rng.Next(opts.Count)], 2); return true;
                    }
                case "UserConsumeBerryRaiseDefense2":
                    {
                        if (string.IsNullOrEmpty(user.Pk.Item) || !user.Pk.Item.EndsWith("BERRY")) { Say("But it failed!"); return true; }
                        int max = user.Pk.TotalHP(P);
                        Say(n + " ate its " + ItemName(user.Pk.Item) + "!");
                        if (user.Pk.Item == "SITRUSBERRY" || user.Pk.Item == "ORANBERRY") { int f = user.Pk.HP; user.Pk.HP = Math.Min(max, user.Pk.HP + (user.Pk.Item == "ORANBERRY" ? 10 : max / 4)); if (user.Pk.HP > f) Hp(user, f); }
                        if (user.Pk.Item == "LUMBERRY" && !string.IsNullOrEmpty(user.Pk.Status)) { user.Pk.Status = ""; StatusEvent(user); }
                        NoteItemChange(user.Pk); user.ConsumedBerry = user.Pk.Item; user.AteBerry = true; user.Pk.Item = null;
                        ChangeStat(user, S.Defense, 2); return true;
                    }
                case "CorrodeTargetItem":
                    if (string.IsNullOrEmpty(target.Pk.Item) || TargetHas(user, target, "STICKYHOLD")) { Say("It doesn't affect " + NameL(target) + "..."); return true; }
                    NoteItemChange(target.Pk);
                    Say(n + " corroded " + NameL(target) + "'s " + ItemName(target.Pk.Item) + "!"); target.Pk.Item = null; return true;
                case "StealAndUseBeneficialStatusMove":
                    Say(n + " waits for a target to make a move!"); return true;
            }
            return false;
        }

        string TypeName(string t) { var td = P.Types.FirstOrDefault(x => x.Id == t); return td != null && td.Name != null ? td.Name : t; }
        string ItemName(string id) { var it = P.Items.FirstOrDefault(i => i.Id == id); return it != null ? it.Name : id; }

        bool MoreMoveFails(Battler user, Battler target, MoveData md, string code)
        {
            switch (code)
            {
                case "ThrowUserItemAtTarget": return string.IsNullOrEmpty(user.Pk.Item) || MagicRoom > 0 || HasAbility(user, "KLUTZ");
                case "TypeAndPowerDependOnUserBerry": return string.IsNullOrEmpty(user.Pk.Item) || !user.Pk.Item.EndsWith("BERRY");
                case "UserLosesFireType": return !TypesOf(user).Contains("FIRE");
                case "UsedAfterUserTakesPhysicalDamage": return !(user.TookDamageThisTurn && user.LastDamagePhysical);
                case "FailsIfUserNotConsumedBerry": return user.ConsumedBerry == null && !user.AteBerry;
            }
            return false;
        }

        int MoreSpecialPower(Battler user, Battler target, MoveData md, string code)
        {
            switch (code)
            {
                case "ThrowUserItemAtTarget": { int p; return FlingPower.TryGetValue(user.Pk.Item ?? "", out p) ? p : (user.Pk.Item ?? "").EndsWith("BERRY") ? 10 : 30; }
                case "TypeAndPowerDependOnUserBerry": return 80;
                case "RandomlyDamageOrHealTarget": return _presentPower;
                case "PowerHigherWithConsecutiveUseOnUserSide": return Math.Min(200, 40 * (1 + _echoedVoice));
                case "HitOncePerUserTeamMember": { var d = _beatUpMember != null ? _beatUpMember.Data(P) : null; return 5 + (d != null && d.BaseStats != null && d.BaseStats.Count > 1 ? d.BaseStats[1] : 50) / 10; }
            }
            return -1;
        }
        int _presentPower = 40;
        Pokemon _beatUpMember;

        double MoreSituationPower(Battler user, Battler target, MoveData md, string code)
        {
            switch (code)
            {
                case "DoublePowerIfAllyFaintedLastTurn": return _faintedLastTurn[user.Side] ? 2 : 1;
                case "UsedAfterAllyRoundWithDoublePower": return _roundUsedThisTurn ? 2 : 1;
                case "DoublePowerAfterFusionFlare": return _lastMoveThisTurn == "FUSIONFLARE" ? 2 : 1;
                case "DoublePowerAfterFusionBolt": return _lastMoveThisTurn == "FUSIONBOLT" ? 2 : 1;
                case "PursueSwitchingFoe": return _pursuitBoost ? 2 : 1;
                case "FirePledge": case "GrassPledge": case "WaterPledge": return _pledgeBoost;
            }
            return 1;
        }

        string MoreSpecialType(Battler user, MoveData md, string code, string type)
        {
            string item = MagicRoom > 0 ? "" : user.Pk.Item ?? "";
            switch (code)
            {
                case "TypeDependsOnUserMemory":
                    if (item.EndsWith("MEMORY")) { string t = item.Substring(0, item.Length - 6); if (P.Types.Any(x => x.Id == t)) return t; }
                    break;
                case "TypeDependsOnUserDrive":
                    switch (item) { case "BURNDRIVE": return "FIRE"; case "CHILLDRIVE": return "ICE"; case "DOUSEDRIVE": return "WATER"; case "SHOCKDRIVE": return "ELECTRIC"; }
                    break;
                case "TypeAndPowerDependOnUserBerry": return NaturalGiftType(user.Pk.Item);
                case "TypeDependsOnUserMorpekoFormRaiseUserSpeed1": return user.Pk.Form == 1 ? "DARK" : "ELECTRIC";
            }
            if (user.Electrified) return "ELECTRIC";
            if (_ionDeluge && type == "NORMAL") return "ELECTRIC";
            return null;
        }

        /// <summary>Before a damaging move hits: Spectral Thief, Feint lifting Protect...</summary>
        void MoreBeforeDamage(Battler user, Battler target, MoveData md, string code)
        {
            if ((code.StartsWith("RemoveProtections") || code.StartsWith("HoopaRemoveProtections")) && target.Protected)
            { target.Protected = false; Say(Name(target) + " fell for the feint!"); }
            if (code == "UserStealTargetPositiveStatStages" && target.Stages.Skip(1).Any(x => x > 0))
            {
                Say(Name(user) + " stole the target's boosted stats!");
                for (int i = 1; i < 8; i++) if (target.Stages[i] > 0) { user.Stages[i] = Math.Min(6, user.Stages[i] + target.Stages[i]); target.Stages[i] = 0; }
            }
        }

        /// <summary>After a damaging move hits: Burn Up, Incinerate, Flame Burst, Throat Chop, Core Enforcer, Fling and Natural Gift using up the item...</summary>
        void MoreDamagingAfter(Battler user, Battler target, MoveData md, string code, int total)
        {
            switch (code)
            {
                case "UserLosesFireType":
                    { var t = TypesOf(user).Where(x => x != "FIRE").ToList(); user.TypesOverride = t.Count > 0 ? t : new List<string> { "NORMAL" }; Say(Name(user) + " burned itself out!"); break; }
                case "DestroyTargetBerryOrGem":
                    if (!target.Pk.Fainted && !string.IsNullOrEmpty(target.Pk.Item) && (target.Pk.Item.EndsWith("BERRY") || target.Pk.Item.EndsWith("GEM")))
                    { NoteItemChange(target.Pk); Say(Name(target) + "'s " + ItemName(target.Pk.Item) + " was incinerated!"); target.Pk.Item = null; }
                    break;
                case "DamageTargetAlly":
                    { var ta = Ally(target); if (ta != null && !ta.Pk.Fainted && !HasAbility(ta, "MAGICGUARD")) Residual(ta, Math.Max(1, ta.Pk.TotalHP(P) / 16), "The bursting flame hit " + NameL(ta) + "!"); break; }
                case "DisableTargetSoundMoves": if (!target.Pk.Fainted) target.ThroatChopTurns = 3; break;
                case "NegateTargetAbilityIfTargetActed": if (!target.Pk.Fainted && target.ActedThisTurn) { target.AbilitySuppressed = true; Say(Name(target) + "'s Ability was suppressed!"); } break;
                case "TypeDependsOnUserMorpekoFormRaiseUserSpeed1": if (!user.Pk.Fainted) ChangeStat(user, S.Speed, 1); break;
                case "ThrowUserItemAtTarget":
                    if (!string.IsNullOrEmpty(user.Pk.Item))
                    {
                        string it = user.Pk.Item; NoteItemChange(user.Pk); user.Pk.Item = null;
                        if (!target.Pk.Fainted)
                        {
                            if (it == "FLAMEORB" && CanStatus(target, "BURN", false, user)) Inflict(target, "BURN", false, user);
                            else if (it == "TOXICORB" && CanStatus(target, "TOXIC", false, user)) Inflict(target, "TOXIC", false, user);
                            else if ((it == "KINGSROCK" || it == "RAZORFANG") && !target.ActedThisTurn) target.Flinched = true;
                            else if (it == "LIGHTBALL" && CanStatus(target, "PARALYSIS", false, user)) Inflict(target, "PARALYSIS", false, user);
                        }
                    }
                    break;
                case "TypeAndPowerDependOnUserBerry":
                    if (!string.IsNullOrEmpty(user.Pk.Item)) { NoteItemChange(user.Pk); user.ConsumedBerry = user.Pk.Item; user.Pk.Item = null; }
                    break;
                case "EffectDependsOnEnvironment":
                    if (!target.Pk.Fainted && Rng.Next(100) < 30)
                    {
                        if (Terrain == "Grassy") { if (CanStatus(target, "SLEEP", false, user)) Inflict(target, "SLEEP", false, user); }
                        else if (Terrain == "Misty") ChangeStat(target, S.SpAtk, -1, user);
                        else if (Terrain == "Psychic") ChangeStat(target, S.Speed, -1, user);
                        else SecretPowerByEnvironment(user, target);
                    }
                    break;
                case "NormalMovesBecomeElectric": _ionDeluge = true; break;
                case "TrapTargetInBattleLowerTargetDefSpDef1EachTurn": if (!target.Pk.Fainted && !target.Octolocked) { target.Octolocked = true; target.Trapped = true; Say(Name(target) + " can no longer escape!"); } break;
            }
            if (code == "UsedAfterAllyRoundWithDoublePower") _roundUsedThisTurn = true;
            if (code == "FirePledge" || code == "GrassPledge" || code == "WaterPledge") FinishPledge(user, target, code);
            if (code == "PowerHigherWithConsecutiveUseOnUserSide") { _echoedThisTurn = true; _echoedVoice++; }
        }

        /// <summary>Present heals instead of hurting (20% of the time).</summary>
        bool PresentHeals(Battler user, Battler target, string code)
        {
            if (code != "RandomlyDamageOrHealTarget") return false;
            int r = Rng.Next(100);
            _presentPower = r < 40 ? 40 : r < 70 ? 80 : r < 80 ? 120 : 0;
            if (_presentPower > 0) return false;
            int max = target.Pk.TotalHP(P);
            if (target.Pk.HP >= max) { Say(Name(target) + " can't receive the gift!"); return true; }
            int f = target.Pk.HP; target.Pk.HP = Math.Min(max, target.Pk.HP + max / 4); Hp(target, f);
            Say(Name(target) + "'s HP was restored.");
            return true;
        }

        /// <summary>Powder: a powdered Pokémon using a Fire move explodes instead.</summary>
        bool PowderExplodes(Battler user, string type)
        {
            if (!user.Powdered || type != "FIRE") return false;
            Say("When the flame touched the powder on the Pokémon, it exploded!");
            if (!HasAbility(user, "MAGICGUARD")) { int f = user.Pk.HP; user.Pk.HP = Math.Max(0, user.Pk.HP - Math.Max(1, user.Pk.TotalHP(P) / 4)); Hp(user, f); }
            return true;
        }

        /// <summary>
        /// The first Pledge of a turn waits for an ally's different Pledge; the second one hits harder and leaves a
        /// sea of fire (Fire + Grass), a swamp (Grass + Water) or a rainbow (Water + Fire).
        /// </summary>
        void PledgeBefore(Battler user, string code)
        {
            _pledgeBoost = 1;
            if (_pledgeUser != null && _pledgeUser != user && _pledgeUser.Side == user.Side && _pledgeCode != code && !_pledgeUser.Pk.Fainted)
                _pledgeBoost = 150.0 / 80;
        }

        void FinishPledge(Battler user, Battler target, string code)
        {
            if (_pledgeBoost > 1)
            {
                var pair = new HashSet<string> { code, _pledgeCode };
                int foe = 1 - user.Side;
                if (pair.Contains("FirePledge") && pair.Contains("GrassPledge")) { SeaOfFire[foe] = 4; Say("A sea of fire enveloped " + SideName(foe, IsWild) + "!"); }
                else if (pair.Contains("GrassPledge") && pair.Contains("WaterPledge")) { Swamp[foe] = 4; Say("A swamp enveloped " + SideName(foe, IsWild) + "!"); }
                else if (pair.Contains("WaterPledge") && pair.Contains("FirePledge")) { Rainbow[user.Side] = 4; Say("A rainbow appeared in the sky on " + SideName(user.Side, IsWild) + "'s side!"); }
                _pledgeUser = null; _pledgeCode = null; _pledgeBoost = 1;
                return;
            }
            _pledgeUser = user; _pledgeCode = code;
        }

        void MoreEndOfTurn()
        {
            for (int s = 0; s < 2; s++)
            {
                if (SeaOfFire[s] > 0)
                {
                    foreach (var b in Live(s).Where(x => !TypesOf(x).Contains("FIRE")).ToList()) Residual(b, Math.Max(1, b.Pk.TotalHP(P) / 8), Name(b) + " was hurt by the sea of fire!");
                    if (--SeaOfFire[s] == 0) Say("The sea of fire around " + SideName(s, IsWild) + " disappeared!");
                }
                if (Swamp[s] > 0 && --Swamp[s] == 0) Say("The swamp around " + SideName(s, IsWild) + " disappeared!");
                if (Rainbow[s] > 0 && --Rainbow[s] == 0) Say("The rainbow on " + SideName(s, IsWild) + "'s side disappeared!");
            }
            foreach (var b in OnField.Where(x => !x.Pk.Fainted).ToList())
            {
                if (b.Octolocked) { ChangeStat(b, S.Defense, -1); ChangeStat(b, S.SpDef, -1); }
                b.BeakBlast = false;
                if (b.HealBlockTurns > 0 && --b.HealBlockTurns == 0) Say(Name(b) + "'s Heal Block wore off!");
                if (b.EmbargoTurns > 0 && --b.EmbargoTurns == 0) Say(Name(b) + " can use items again!");
                if (b.ThroatChopTurns > 0) b.ThroatChopTurns--;
                if (b.LaserFocus > 0) b.LaserFocus--;
                b.MagicCoat = false; b.Powdered = false; b.Electrified = false;
            }
            for (int s = 0; s < 2; s++) if (LuckyChant[s] > 0 && --LuckyChant[s] == 0) Say(Cap(SideName(s, IsWild)) + "'s Lucky Chant wore off!");
            if (MudSport > 0 && --MudSport == 0) Say("The effects of Mud Sport have faded.");
            if (WaterSport > 0 && --WaterSport == 0) Say("The effects of Water Sport have faded.");
            if (MagicRoom > 0 && --MagicRoom == 0) Say("Magic Room wore off, and held items' effects returned to normal!");
            if (WonderRoom > 0 && --WonderRoom == 0) Say("Wonder Room wore off, and Defense and Sp. Def stats returned to normal!");
            if (FairyLock > 0) FairyLock--;
        }

        /// <summary>Per-turn bookkeeping for Retaliate, Echoed Voice, Round, the Fusion moves and Ion Deluge.</summary>
        void StartOfTurnBookkeeping()
        {
            _pledgeUser = null; _pledgeCode = null; _pledgeBoost = 1; _pursuitBoost = false;
            foreach (var b in OnField) b.BeakBlast = false;
            _faintedLastTurn[0] = _faintedThisTurn[0]; _faintedLastTurn[1] = _faintedThisTurn[1];
            _faintedThisTurn[0] = _faintedThisTurn[1] = false;
            if (!_echoedThisTurn) _echoedVoice = 0;
            _echoedThisTurn = false; _roundUsedThisTurn = false; _ionDeluge = false; _lastMoveThisTurn = null;
        }

        /// <summary>Items swapped by Trick, Bestow, Fling... return to the player's Pokémon after the battle.</summary>
        void RestoreItems()
        {
            foreach (var kv in _itemBeforeBattle) kv.Key.Item = kv.Value;
            _itemBeforeBattle.Clear();
            foreach (var side in Field) foreach (var b in side) if (b != null) RestoreBattleMoves(b);
        }
    }
}
