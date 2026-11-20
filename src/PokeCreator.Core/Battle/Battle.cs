using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PokeCreator.Data;
using PokeCreator.Pkmn;
using PokeCreator.Projects;

namespace PokeCreator.Battles
{
    public enum BattleEventKind { Message, Hp, Faint, SendOut, Withdraw, StatChange, Status, Exp, LevelUp, Catch, Flee, Hit, Miss, MoveUsed }

    /// <summary>Something the battle screen shows, in order: a message, an HP bar change, a Pokémon fainting...</summary>
    public class BattleEvent
    {
        public BattleEventKind Kind;
        public int Side;            // 0 player, 1 opponent
        public int Party;           // party index on that side
        public string Text;
        public int Value, Value2;   // HP from/to, EXP amount, shakes...
        public bool Flag;           // caught, critical...
        public List<string> Moves;  // moves to learn after a level up
        public override string ToString() { return Kind + " s" + Side + " p" + Party + " " + Text + " " + Value + "/" + Value2; }
    }

    public enum BattleCommandKind { Fight, Switch, Item, Ball, Run }

    public class BattleCommand
    {
        public BattleCommandKind Kind;
        public int Index;       // move slot, party index to switch to / use the item on
        public string Item;
        public static BattleCommand Fight(int slot) { return new BattleCommand { Kind = BattleCommandKind.Fight, Index = slot }; }
        public static BattleCommand Switch(int party) { return new BattleCommand { Kind = BattleCommandKind.Switch, Index = party }; }
        public static BattleCommand UseItem(string item, int party) { return new BattleCommand { Kind = BattleCommandKind.Item, Item = item, Index = party }; }
        public static BattleCommand Ball(string item) { return new BattleCommand { Kind = BattleCommandKind.Ball, Item = item }; }
        public static BattleCommand Run() { return new BattleCommand { Kind = BattleCommandKind.Run }; }
    }

    /// <summary>A Pokémon's battle-only state (stat stages, confusion, trapping...).</summary>
    public class Battler
    {
        public Pokemon Pk;
        public int Side, PartyIndex;
        public int[] Stages = new int[8]; // Atk, Def, Spd, SpAtk, SpDef (Stat indexes 1-5), 6 accuracy, 7 evasion
        public int Confusion, SleepTurns, ToxicCount, BindTurns, CritStage;
        public bool Flinched, MustRecharge, Protected, ProtectedLast;
        public int ProtectChain;
        public string Charging;         // two-turn move waiting to hit
        public int ChargingSlot;
        public bool Invulnerable;       // Fly / Dig first turn
        public bool SwitchedIn;         // sent out this turn
        public int TurnsOut;
        public bool FlashFire;          // Flash Fire boost active
        public bool AbilitySuppressed;  // Gastro Acid etc.
        public bool MovedLast;          // moved after the other Pokémon this turn (Analytic)
        public Battler(Pokemon pk, int side, int idx) { Pk = pk; Side = side; PartyIndex = idx; }
        public void Reset() { Stages = new int[8]; Confusion = 0; ToxicCount = 0; BindTurns = 0; CritStage = 0; Flinched = false; MustRecharge = false; Protected = false; ProtectChain = 0; Charging = null; Invulnerable = false; TurnsOut = 0; }
    }

    public enum BattleOutcome { Ongoing = 0, Win = 1, Loss = 2, Escaped = 3, Caught = 4 }

    /// <summary>
    /// Single battles (wild or trainer). The screen asks for a command each turn, calls DoTurn, and plays the
    /// returned events. State changes (HP, PP, status, EXP) are applied to the Pokémon as the turn runs.
    /// </summary>
    public partial class Battle
    {
        public readonly Project P;
        public readonly Random Rng;
        public readonly List<Pokemon>[] Parties = new List<Pokemon>[2];
        public readonly Battler[] Active = new Battler[2];
        public readonly bool IsWild;
        public string TrainerName, TrainerTypeName, PlayerName = "Red";
        public int TrainerSkill;
        public BattleOutcome Outcome;
        public int Turn, RunAttempts;
        public bool NeedPlayerSwitch;      // the player's Pokémon fainted and another must come out
        public readonly HashSet<int> LeveledUp = new HashSet<int>();  // party indexes to check for evolution afterwards
        public Pokemon CaughtPokemon;
        readonly Dictionary<int, HashSet<int>> _participants = new Dictionary<int, HashSet<int>>(); // foe party index -> player party indexes
        List<BattleEvent> _ev;

        public Battle(Project p, List<Pokemon> playerParty, List<Pokemon> foeParty, bool wild, Random rng)
        {
            P = p; Rng = rng ?? new Random(); Parties[0] = playerParty; Parties[1] = foeParty; IsWild = wild;
        }

        // ------------------------------------------------------------------ helpers
        public static bool Able(Pokemon pk) { return pk != null && !pk.IsEgg && !pk.Fainted; }
        public int FirstAble(int side) { return Parties[side].FindIndex(Able); }
        public bool AnyAble(int side) { return Parties[side].Any(Able); }
        string Name(Battler b) { return b.Side == 1 ? (IsWild ? "The wild " : "The opposing ") + b.Pk.Name(P) : b.Pk.Name(P); }
        string NameCap(Battler b) { return Name(b); }
        /// <summary>Name in the middle of a sentence: "the wild Pidgey", "the opposing Onix".</summary>
        string NameL(Battler b) { string n = Name(b); return n.StartsWith("The ") ? "the " + n.Substring(4) : n; }
        MoveData Move(string id) { return P.Moves.FirstOrDefault(m => m.Id == id); }
        void Say(string text) { _ev.Add(new BattleEvent { Kind = BattleEventKind.Message, Text = text }); }
        void Hp(Battler b, int from) { _ev.Add(new BattleEvent { Kind = BattleEventKind.Hp, Side = b.Side, Party = b.PartyIndex, Value = from, Value2 = b.Pk.HP }); }

        /// <summary>Starts the battle: send-out messages. Call once.</summary>
        public List<BattleEvent> Start()
        {
            _ev = new List<BattleEvent>();
            int f = FirstAble(1);
            Active[1] = new Battler(Parties[1][f], 1, f);
            if (IsWild)
            {
                _ev.Add(new BattleEvent { Kind = BattleEventKind.SendOut, Side = 1, Party = f });
                Say("A wild " + Active[1].Pk.Name(P) + " appeared!");
            }
            else
            {
                Say(TrainerTypeName + " " + TrainerName + " would like to battle!");
                Say(TrainerTypeName + " " + TrainerName + " sent out " + Active[1].Pk.Name(P) + "!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.SendOut, Side = 1, Party = f });
            }
            int pi = FirstAble(0);
            Active[0] = new Battler(Parties[0][pi], 0, pi);
            Say("Go! " + Active[0].Pk.Name(P) + "!");
            _ev.Add(new BattleEvent { Kind = BattleEventKind.SendOut, Side = 0, Party = pi });
            MarkParticipant();
            if (Weather != NoWeather) Say(WeatherFieldText(Weather));
            foreach (var b in Active.OrderByDescending(x => Speed(x)).ToList()) SwitchInAbility(b);
            return _ev;
        }

        void MarkParticipant()
        {
            HashSet<int> set;
            if (!_participants.TryGetValue(Active[1].PartyIndex, out set)) _participants[Active[1].PartyIndex] = set = new HashSet<int>();
            set.Add(Active[0].PartyIndex);
        }

        /// <summary>Moves the player's Pokémon can use (with PP); Struggle if none.</summary>
        public bool HasUsableMove(Battler b) { return b.Pk.Moves.Any(m => m.PP > 0); }

        // ------------------------------------------------------------------ turns
        public List<BattleEvent> DoTurn(BattleCommand player)
        {
            _ev = new List<BattleEvent>();
            Turn++;
            var me = Active[0]; var foe = Active[1];
            me.SwitchedIn = foe.SwitchedIn = false;
            // A charging or recharging Pokémon has its command decided for it
            if (me.Charging != null || me.MustRecharge) player = BattleCommand.Fight(me.ChargingSlot);
            var foeCmd = foe.Charging != null || foe.MustRecharge ? BattleCommand.Fight(foe.ChargingSlot) : ChooseAI(foe, me);

            // Non-move actions go first: run, items, balls, switching
            if (player.Kind == BattleCommandKind.Run)
            {
                if (TryRun()) return _ev;
                player = null;
            }
            else if (player.Kind == BattleCommandKind.Ball) { ThrowBall(player.Item); if (Outcome != BattleOutcome.Ongoing) return _ev; player = null; }
            else if (player.Kind == BattleCommandKind.Item) { UseItem(player.Item, player.Index); player = null; }
            else if (player.Kind == BattleCommandKind.Switch) { SwitchPlayer(player.Index); player = null; me = Active[0]; }

            if (player != null && foeCmd.Kind == BattleCommandKind.Fight)
            {
                // Both attack: priority, then speed (ties at random)
                var myMove = MoveFor(me, player.Index); var foeMove = MoveFor(foe, foeCmd.Index);
                int pa = myMove != null ? myMove.Priority : 0, pb = foeMove != null ? foeMove.Priority : 0;
                bool meFirst = pa != pb ? pa > pb : Speed(me) != Speed(foe) ? Speed(me) > Speed(foe) : Rng.Next(2) == 0;
                me.MovedLast = !meFirst; foe.MovedLast = meFirst;
                if (meFirst) { Act(me, foe, player.Index); if (!Ended()) Act(foe, me, foeCmd.Index); }
                else { Act(foe, me, foeCmd.Index); if (!Ended()) Act(me, foe, player.Index); }
            }
            else if (player != null) { me.MovedLast = true; Act(me, foe, player.Index); }
            else if (foeCmd.Kind == BattleCommandKind.Fight) { foe.MovedLast = true; Act(foe, Active[0], foeCmd.Index); }

            if (!Ended()) EndOfTurn();
            AfterFaints();
            foreach (var b in Active) if (b != null) { b.ProtectedLast = b.Protected; b.Protected = false; b.Flinched = false; b.TurnsOut++; }
            return _ev;
        }

        bool Ended() { return Outcome != BattleOutcome.Ongoing || Active[0].Pk.Fainted || Active[1].Pk.Fainted; }

        MoveData MoveFor(Battler b, int slot)
        {
            if (b.Charging != null) return Move(b.Charging);
            if (!HasUsableMove(b)) return Move("STRUGGLE");
            return slot >= 0 && slot < b.Pk.Moves.Count ? Move(b.Pk.Moves[slot].Id) : null;
        }

        int Speed(Battler b)
        {
            int s = Stat(b, PokeCreator.Pkmn.Stat.Speed);
            if ((b.Pk.Status ?? "").ToUpperInvariant() == "PARALYSIS" && !HasAbility(b, "QUICKFEET")) s /= 2;
            return Math.Max(1, (int)(s * AbilitySpeed(b)));
        }

        static double StageMult(int s) { return s >= 0 ? (2.0 + s) / 2.0 : 2.0 / (2.0 - s); }
        static double AccMult(int s) { return s >= 0 ? (3.0 + s) / 3.0 : 3.0 / (3.0 - s); }

        int Stat(Battler b, int stat, bool ignoreUp = false, bool ignoreDown = false)
        {
            int v = b.Pk.Stats(P)[stat];
            int st = b.Stages[stat];
            if ((ignoreUp && st > 0) || (ignoreDown && st < 0)) st = 0;
            return Math.Max(1, (int)(v * StageMult(st)));
        }

        // ------------------------------------------------------------------ AI
        BattleCommand ChooseAI(Battler me, Battler foe)
        {
            var usable = Enumerable.Range(0, me.Pk.Moves.Count).Where(i => me.Pk.Moves[i].PP > 0).ToList();
            if (usable.Count == 0) return BattleCommand.Fight(-1);
            if (IsWild || TrainerSkill <= 0) return BattleCommand.Fight(usable[Rng.Next(usable.Count)]);
            // Smarter trainers: favour the move that does the most damage, with some variety and status moves now and then
            int best = usable[0]; double bestScore = -1;
            foreach (int i in usable)
            {
                var md = Move(me.Pk.Moves[i].Id);
                double score;
                if (md == null) score = 0;
                else if (md.Category == "Status") score = foe.Pk.Status == "" && IsStatusMove(md) ? 40 : 15;
                else
                {
                    double eff = Effectiveness(md.Type, foe.Pk);
                    double stab = me.Pk.Types(P).Contains(md.Type) ? 1.5 : 1;
                    score = Math.Max(1, md.Power <= 1 ? 60 : md.Power) * eff * stab * (md.Accuracy > 0 ? md.Accuracy / 100.0 : 1);
                }
                score *= 0.85 + Rng.NextDouble() * 0.3;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return BattleCommand.Fight(best);
        }

        static bool IsStatusMove(MoveData md) { var f = md.FunctionCode ?? ""; return Regex.IsMatch(f, "(Sleep|Poison|Paralyze|Burn|Freeze|Confuse)Target$"); }

        // ------------------------------------------------------------------ type chart
        /// <summary>Multiplier of one attacking type against one defending type.</summary>
        double TypeVs(string moveType, string defType)
        {
            var td = P.Types.FirstOrDefault(x => x.Id == defType);
            if (td == null) return 1;
            if (td.Immunities.Contains(moveType)) return 0;
            if (td.Weaknesses.Contains(moveType)) return 2;
            if (td.Resistances.Contains(moveType)) return 0.5;
            return 1;
        }

        public double Effectiveness(string moveType, Pokemon target)
        {
            double m = 1;
            foreach (var t in target.Types(P).Distinct())
            {
                var td = P.Types.FirstOrDefault(x => x.Id == t);
                if (td == null) continue;
                if (td.Immunities.Contains(moveType)) return 0;
                if (td.Weaknesses.Contains(moveType)) m *= 2;
                else if (td.Resistances.Contains(moveType)) m *= 0.5;
            }
            return m;
        }

        // ------------------------------------------------------------------ using a move
        void Act(Battler user, Battler target, int slot)
        {
            if (user.Pk.Fainted || Outcome != BattleOutcome.Ongoing) return;
            string status = (user.Pk.Status ?? "").ToUpperInvariant();
            string n = Name(user);
            if (user.MustRecharge) { user.MustRecharge = false; Say(n + " must recharge!"); return; }
            if (status == "SLEEP")
            {
                if (user.SleepTurns <= 0) user.SleepTurns = 1 + Rng.Next(3);
                user.SleepTurns -= HasAbility(user, "EARLYBIRD") ? 2 : 1;
                if (user.SleepTurns > 0) { Say(n + " is fast asleep."); user.Charging = null; return; }
                user.Pk.Status = ""; StatusEvent(user); Say(n + " woke up!");
            }
            if (status == "FROZEN")
            {
                if (Rng.Next(5) != 0) { Say(n + " is frozen solid!"); return; }
                user.Pk.Status = ""; StatusEvent(user); Say(n + " thawed out!");
            }
            if (user.Flinched) { Say(n + " flinched and couldn't move!"); user.Charging = null; return; }
            if (user.Confusion > 0)
            {
                user.Confusion--;
                if (user.Confusion == 0) Say(n + " snapped out of its confusion!");
                else
                {
                    Say(n + " is confused!");
                    if (Rng.Next(3) == 0)
                    {
                        int dmg = DamageFormula(user, user, 40, true, null, false, 1, false);
                        int from = user.Pk.HP; user.Pk.HP = Math.Max(0, user.Pk.HP - dmg); Hp(user, from);
                        Say("It hurt itself in its confusion!");
                        user.Charging = null;
                        return;
                    }
                }
            }
            if (status == "PARALYSIS" && Rng.Next(4) == 0) { Say(n + " is paralyzed! It can't move!"); user.Charging = null; return; }

            // Which move
            MoveData md; PokemonMove pm = null;
            if (user.Charging != null) { md = Move(user.Charging); }
            else if (!HasUsableMove(user)) { md = Move("STRUGGLE") ?? new MoveData { Id = "STRUGGLE", Name = "Struggle", Type = "NORMAL", Category = "Physical", Power = 50, FunctionCode = "Struggle" }; }
            else
            {
                if (slot < 0 || slot >= user.Pk.Moves.Count || user.Pk.Moves[slot].PP <= 0) slot = user.Pk.Moves.FindIndex(m => m.PP > 0);
                pm = user.Pk.Moves[slot];
                md = Move(pm.Id);
                if (md == null) { Say(n + " used " + pm.Id + "!"); Say("But nothing happened!"); pm.PP--; return; }
            }
            string code = md.FunctionCode ?? "None";

            // Two-turn moves: charge first
            if (user.Charging == null && code.StartsWith("TwoTurnAttack") && !(code == "TwoTurnAttackOneTurnInSun" && EffWeather == Sun) && user.Pk.Item != "POWERHERB")
            {
                if (pm != null) pm.PP = Math.Max(0, pm.PP - 1);
                user.Charging = md.Id; user.ChargingSlot = slot;
                user.Invulnerable = code.Contains("Invulnerable");
                Say(n + ChargeText(md.Id));
                return;
            }
            bool wasCharging = user.Charging != null;
            user.Charging = null; user.Invulnerable = false;
            if (pm != null && !wasCharging) pm.PP = Math.Max(0, pm.PP - 1);

            Say(n + " used " + (md.Name ?? md.Id) + "!");
            _ev.Add(new BattleEvent { Kind = BattleEventKind.MoveUsed, Side = user.Side, Party = user.PartyIndex, Text = md.Id });
            bool selfTarget = md.Target == "User" || md.Target == "UserSide" || md.Target == "BothSides" || md.Target == "UserAndAllies" || md.Target == "FoeSide" && md.Category == "Status" && code.StartsWith("Add");

            if (code == "ProtectUser" || code.StartsWith("ProtectUser"))
            {
                bool ok = Rng.NextDouble() < Math.Pow(1.0 / 3, user.ProtectedLast ? user.ProtectChain : 0);
                if (!ok) { user.ProtectChain = 0; Say("But it failed!"); return; }
                user.Protected = true; user.ProtectChain = user.ProtectedLast ? user.ProtectChain + 1 : 1;
                Say(n + " protected itself!");
                return;
            }
            if (!selfTarget)
            {
                if (target.Pk.Fainted) { Say("But there was no target..."); return; }
                if (target.Protected && md.Flags.Contains("CanProtect")) { Say(Name(target) + " protected itself!"); return; }
                if (target.Invulnerable && md.Accuracy > 0) { Say(Name(user) + "'s attack missed!"); return; }
                if (AbilityAbsorbs(user, target, md, MoveType(user, md, code))) return;
                if (!Hits(user, target, md, code)) { Say(Name(user) + "'s attack missed!"); _ev.Add(new BattleEvent { Kind = BattleEventKind.Miss, Side = target.Side, Party = target.PartyIndex }); return; }
            }
            if (md.Category == "Status") { StatusMove(user, target, md, code); return; }
            DamagingMove(user, target, md, code);
        }

        static string ChargeText(string move)
        {
            switch (move)
            {
                case "SOLARBEAM": case "SOLARBLADE": return " absorbed light!";
                case "FLY": return " flew up high!";
                case "DIG": return " burrowed its way under the ground!";
                case "DIVE": return " hid underwater!";
                case "SKULLBASH": return " tucked in its head!";
                case "SKYATTACK": return " became cloaked in a harsh light!";
                case "RAZORWIND": return " whipped up a whirlwind!";
                case "BOUNCE": return " sprang up!";
                default: return " is charging up!";
            }
        }

        bool Hits(Battler user, Battler target, MoveData md, string code)
        {
            if (code == "OHKO" || code.StartsWith("OHKO"))
            {
                if (target.Pk.Level > user.Pk.Level) return false;
                return Rng.Next(100) < 30 + user.Pk.Level - target.Pk.Level;
            }
            if (md.Accuracy <= 0) return true;
            bool always;
            double mod = AccuracyMod(user, target, md, code, out always);
            if (always) return true;
            int evade = TargetHas(user, target, "UNAWARE") ? 0 : target.Stages[7];
            if (HasAbility(user, "KEENEYE") && evade > 0) evade = 0;
            double acc = md.Accuracy * mod * AccMult(Math.Max(-6, Math.Min(6, user.Stages[6] - evade)));
            return Rng.Next(100) < acc;
        }

        // ------------------------------------------------------------------ damage
        int DamageFormula(Battler user, Battler target, int power, bool physical, string type, bool crit, double typeMod, bool stab, MoveData md = null, string code = "")
        {
            int atkStat = physical ? PokeCreator.Pkmn.Stat.Attack : PokeCreator.Pkmn.Stat.SpAtk;
            int defStat = physical ? PokeCreator.Pkmn.Stat.Defense : PokeCreator.Pkmn.Stat.SpDef;
            // Unaware ignores the other side's stat stages
            bool ignoreAtk = md != null && TargetHas(user, target, "UNAWARE"), ignoreDef = md != null && HasAbility(user, "UNAWARE");
            int a = Stat(user, atkStat, ignoreAtk, crit || ignoreAtk), d = Stat(target, defStat, crit || ignoreDef, ignoreDef);
            if (md != null)
            {
                power = Math.Max(1, (int)(power * PowerMod(user, target, md, type, code ?? "", power)));
                a = Math.Max(1, (int)(a * AttackMod(user, target, physical, type)));
                d = Math.Max(1, (int)(d * DefenseMod(user, target, physical)));
            }
            double dmg = Math.Floor(Math.Floor(Math.Floor(2.0 * user.Pk.Level / 5 + 2) * power * a / d) / 50) + 2;
            if (crit) dmg = Math.Floor(dmg * 1.5);
            dmg = Math.Floor(dmg * (85 + Rng.Next(16)) / 100.0);
            if (stab) dmg = Math.Floor(dmg * (HasAbility(user, "ADAPTABILITY") ? 2 : 1.5));
            dmg = Math.Floor(dmg * typeMod);
            if (physical && (user.Pk.Status ?? "").ToUpperInvariant() == "BURN" && !HasAbility(user, "GUTS")) dmg = Math.Floor(dmg / 2);
            if (md != null) dmg = Math.Floor(dmg * FinalMod(user, target, type, typeMod, crit));
            return Math.Max(typeMod > 0 ? 1 : 0, (int)dmg);
        }

        void DamagingMove(Battler user, Battler target, MoveData md, string code)
        {
            string mtype = MoveType(user, md, code);
            double typeMod = md.Id == "STRUGGLE" ? 1 : Effectiveness(mtype, target.Pk);
            if (typeMod == 0 && HasAbility(user, "SCRAPPY") && (mtype == "NORMAL" || mtype == "FIGHTING") && target.Pk.Types(P).Contains("GHOST"))
                typeMod = target.Pk.Types(P).Where(t => t != "GHOST").Aggregate(1.0, (acc, t) => acc * TypeVs(mtype, t));
            if (typeMod == 0) { Say("It doesn't affect " + NameL(target) + "..."); return; }
            if (typeMod <= 1 && md.Id != "STRUGGLE" && TargetHas(user, target, "WONDERGUARD")) { Splash(target); Say("It doesn't affect " + NameL(target) + "..."); return; }
            if ((code == "OHKO" || code.StartsWith("OHKO")) && TargetHas(user, target, "STURDY")) { Splash(target); Say("It doesn't affect " + NameL(target) + "..."); return; }
            bool fullHp = target.Pk.HP >= target.Pk.TotalHP(P);

            int hits = 1;
            if (code.StartsWith("HitTwoToFiveTimes")) { int r = Rng.Next(100); hits = HasAbility(user, "SKILLLINK") ? 5 : r < 35 ? 2 : r < 70 ? 3 : r < 85 ? 4 : 5; }
            else if (code.StartsWith("HitTwoTimes")) hits = 2;
            else if (code.StartsWith("HitThreeTimes")) hits = 3;
            int total = 0, done = 0;
            bool anyCrit = false;
            string enduredBy = null;
            for (int h = 0; h < hits && !target.Pk.Fainted && !user.Pk.Fainted; h++)
            {
                int dmg;
                bool crit = false;
                int fixedDmg = FixedDamage(user, target, md, code);
                if (fixedDmg >= 0) dmg = fixedDmg;
                else
                {
                    int power = md.Power <= 1 ? VariablePower(user, target, md, code) : md.Power;
                    if (code == "HitThreeTimesPowersUpWithEachHit") power = md.Power * (h + 1);
                    int stage = user.CritStage + (md.Flags.Contains("HighCriticalHitRate") ? 1 : 0) + (HasAbility(user, "SUPERLUCK") ? 1 : 0)
                              + (user.Pk.Item == "SCOPELENS" || user.Pk.Item == "RAZORCLAW" ? 1 : 0);
                    crit = code == "AlwaysCriticalHit" || stage >= 3 || Rng.Next(stage == 0 ? 24 : stage == 1 ? 8 : 2) == 0;
                    if (TargetHas(user, target, "BATTLEARMOR") || TargetHas(user, target, "SHELLARMOR")) crit = false;
                    bool physical = md.Category == "Physical";
                    if (code == "UseTargetDefenseInsteadOfTargetSpDef") physical = true; // simplification: Psyshock hits Defense
                    dmg = DamageFormula(user, target, power, physical, mtype, crit, typeMod, user.Pk.Types(P).Contains(mtype), md, code);
                }
                if (code == "CannotMakeTargetFaint" && dmg >= target.Pk.HP) dmg = target.Pk.HP - 1;
                // Sturdy / Focus Sash: survive a hit from full HP with 1 HP
                bool endured = false;
                if (dmg >= target.Pk.HP && target.Pk.HP >= target.Pk.TotalHP(P) && fullHp && h == 0 && target.Pk.HP > 1)
                {
                    if (TargetHas(user, target, "STURDY")) { dmg = target.Pk.HP - 1; endured = true; }
                    else if (target.Pk.Item == "FOCUSSASH") { dmg = target.Pk.HP - 1; endured = true; }
                }
                int from = target.Pk.HP;
                target.Pk.HP = Math.Max(0, target.Pk.HP - dmg);
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Hit, Side = target.Side, Party = target.PartyIndex, Flag = crit, Value = (int)(typeMod * 100) });
                Hp(target, from);
                total += from - target.Pk.HP; done++;
                if (crit) { Say("A critical hit!"); anyCrit = true; }
                if (endured) enduredBy = TargetHas(user, target, "STURDY") ? "STURDY" : "FOCUSSASH";
            }
            if (FixedDamage(user, target, md, code) < 0)
            {
                if (typeMod > 1) Say("It's super effective!");
                else if (typeMod < 1) Say("It's not very effective...");
            }
            if (enduredBy == "STURDY") { Splash(target); Say(Name(target) + " endured the hit!"); }
            else if (enduredBy == "FOCUSSASH") { Say(Name(target) + " hung on using its Focus Sash!"); target.Pk.Item = null; }
            if (code == "OHKO" || code.StartsWith("OHKO")) Say("It's a one-hit KO!");
            if (hits > 1) Say("The Pokémon was hit " + done + " time" + (done == 1 ? "" : "s") + "!");

            // After-effects
            if (total > 0) ContactAbilities(user, target, md);
            if ((code.StartsWith("Recoil") && !HasAbility(user, "ROCKHEAD") && !HasAbility(user, "MAGICGUARD")) || md.Id == "STRUGGLE")
            {
                int recoil = md.Id == "STRUGGLE" ? Math.Max(1, user.Pk.TotalHP(P) / 4) : code.StartsWith("RecoilHalf") ? total / 2 : code.StartsWith("RecoilThird") ? total / 3 : total / 4;
                if (recoil > 0 && !user.Pk.Fainted) { int f = user.Pk.HP; user.Pk.HP = Math.Max(0, user.Pk.HP - Math.Max(1, recoil)); Hp(user, f); Say(Name(user) + " is damaged by recoil!"); }
            }
            if (code.StartsWith("HealUserBy") && total > 0 && !user.Pk.Fainted)
            {
                int heal = code.Contains("ThreeQuarters") ? total * 3 / 4 : Math.Max(1, total / 2);
                int f = user.Pk.HP; user.Pk.HP = Math.Min(user.Pk.TotalHP(P), user.Pk.HP + heal);
                if (user.Pk.HP > f) { Hp(user, f); Say(Name(target) + " had its energy drained!"); }
            }
            if (code == "UserFaintsExplosive" || code.StartsWith("UserFaints")) { int f = user.Pk.HP; user.Pk.HP = 0; Hp(user, f); }
            if (code == "AttackAndSkipNextTurn") user.MustRecharge = true;
            if (code.StartsWith("BindTarget") && !target.Pk.Fainted && target.BindTurns == 0) { target.BindTurns = 4 + Rng.Next(2); Say(Name(target) + " was squeezed by " + NameL(user) + "!"); }
            if (code == "RaiseUserAttack3IfTargetFaints" && target.Pk.Fainted) ChangeStat(user, PokeCreator.Pkmn.Stat.Attack, 3);
            // Secondary effect: status / stat change / flinch, at the effect chance (0 = always)
            if ((total > 0 || FixedDamage(user, target, md, code) >= 0) && !(HasAbility(user, "SHEERFORCE") && md.EffectChance > 0))
            {
                int chance = md.EffectChance > 0 ? md.EffectChance : 100;
                if (HasAbility(user, "SERENEGRACE")) chance *= 2;
                if (Rng.Next(100) < chance) SecondaryEffect(user, target, md, code);
            }
            if (total > 0 && user.Pk.Item == "LIFEORB" && !user.Pk.Fainted && !HasAbility(user, "MAGICGUARD") && !(HasAbility(user, "SHEERFORCE") && md.EffectChance > 0))
                Residual(user, Math.Max(1, user.Pk.TotalHP(P) / 10), Name(user) + " lost some of its HP!");
            if (target.Pk.Fainted) OnKnockOut(user);
        }

        int FixedDamage(Battler user, Battler target, MoveData md, string code)
        {
            var m = Regex.Match(code, @"^FixedDamage(\d+)$");
            if (m.Success) return int.Parse(m.Groups[1].Value);
            if (code == "FixedDamageUserLevel") return user.Pk.Level;
            if (code == "FixedDamageHalfTargetHP") return Math.Max(1, target.Pk.HP / 2);
            if (code == "FixedDamageUserLevelRandom") return Math.Max(1, user.Pk.Level * (50 + Rng.Next(101)) / 100);
            if (code == "OHKO" || code.StartsWith("OHKO")) return target.Pk.HP;
            return -1;
        }

        int VariablePower(Battler user, Battler target, MoveData md, string code)
        {
            int hp = user.Pk.HP, max = user.Pk.TotalHP(P);
            switch (code)
            {
                case "PowerHigherWithUserHP": return Math.Max(1, 150 * hp / Math.Max(1, max));
                case "PowerLowerWithUserHP":
                    {
                        int r = 48 * hp / Math.Max(1, max);
                        return r < 2 ? 200 : r < 5 ? 150 : r < 10 ? 100 : r < 17 ? 80 : r < 33 ? 40 : 20;
                    }
                case "PowerHigherWithTargetHP": return Math.Max(1, 120 * target.Pk.HP / Math.Max(1, target.Pk.TotalHP(P)));
                default: return 60;
            }
        }

        // ------------------------------------------------------------------ effects
        static readonly string[] StatTokens = { "MainStats", "Attack", "Defense", "SpAtk", "SpDef", "Speed", "Accuracy", "Evasion", "Atk", "Def", "Spd", "Acc", "Eva" };

        /// <summary>Stat indexes named in a function code fragment like "AtkSpAtk" or "MainStats".</summary>
        static List<int> ParseStats(string s)
        {
            var list = new List<int>();
            int i = 0;
            while (i < s.Length)
            {
                string tok = StatTokens.FirstOrDefault(t => string.CompareOrdinal(s, i, t, 0, t.Length) == 0);
                if (tok == null) break;
                switch (tok)
                {
                    case "MainStats": list.AddRange(new[] { 1, 2, 4, 5, 3 }); break;
                    case "Attack": case "Atk": list.Add(PokeCreator.Pkmn.Stat.Attack); break;
                    case "Defense": case "Def": list.Add(PokeCreator.Pkmn.Stat.Defense); break;
                    case "SpAtk": list.Add(PokeCreator.Pkmn.Stat.SpAtk); break;
                    case "SpDef": list.Add(PokeCreator.Pkmn.Stat.SpDef); break;
                    case "Speed": case "Spd": list.Add(PokeCreator.Pkmn.Stat.Speed); break;
                    case "Accuracy": case "Acc": list.Add(6); break;
                    case "Evasion": case "Eva": list.Add(7); break;
                }
                i += tok.Length;
            }
            return list;
        }

        static readonly Regex StatCode = new Regex(@"^(Raise|Lower)(User|Target)((?:MainStats|Attack|Defense|SpAtk|SpDef|Speed|Accuracy|Evasion|Atk|Def|Spd|Acc|Eva)+)(\d)", RegexOptions.Compiled);

        static string StatName(int s) { return s == 6 ? "accuracy" : s == 7 ? "evasiveness" : PokeCreator.Pkmn.Stat.Names[s]; }

        bool ChangeStat(Battler b, int stat, int amount, Battler source = null)
        {
            if (amount < 0 && AbilityBlocksStatDrop(b, stat, source))
            {
                Splash(b); Say(Name(b) + "'s " + StatName(stat) + " was not lowered!");
                return false;
            }
            if (HasAbility(b, "SIMPLE")) amount *= 2;
            if (HasAbility(b, "CONTRARY")) amount = -amount;
            int old = b.Stages[stat];
            int nv = Math.Max(-6, Math.Min(6, old + amount));
            string who = Name(b) + "'s " + StatName(stat);
            if (nv == old) { Say(who + (amount > 0 ? " won't go any higher!" : " won't go any lower!")); return false; }
            b.Stages[stat] = nv;
            int d = Math.Abs(nv - old);
            Say(who + (amount > 0 ? (d >= 3 ? " rose drastically!" : d == 2 ? " rose sharply!" : " rose!") : (d >= 3 ? " severely fell!" : d == 2 ? " harshly fell!" : " fell!")));
            _ev.Add(new BattleEvent { Kind = BattleEventKind.StatChange, Side = b.Side, Party = b.PartyIndex, Value = amount });
            // Defiant / Competitive: lowered by a foe -> sharply raise Attack / Sp. Atk
            if (amount < 0 && source != null && source.Side != b.Side)
            {
                if (HasAbility(b, "DEFIANT") && b.Stages[PokeCreator.Pkmn.Stat.Attack] < 6) { Splash(b); ChangeStat(b, PokeCreator.Pkmn.Stat.Attack, 2); }
                if (HasAbility(b, "COMPETITIVE") && b.Stages[PokeCreator.Pkmn.Stat.SpAtk] < 6) { Splash(b); ChangeStat(b, PokeCreator.Pkmn.Stat.SpAtk, 2); }
            }
            return true;
        }

        void StatusEvent(Battler b) { _ev.Add(new BattleEvent { Kind = BattleEventKind.Status, Side = b.Side, Party = b.PartyIndex, Text = b.Pk.Status ?? "" }); }

        bool CanStatus(Battler b, string status, bool showFail, Battler source = null)
        {
            if (b.Pk.Fainted) return false;
            var types = b.Pk.Types(P);
            string have = (b.Pk.Status ?? "").ToUpperInvariant();
            if (have.Length > 0) { if (showFail) Say(have == status ? Name(b) + AlreadyText(status) : "But it failed!"); return false; }
            bool immune = (status == "POISON" || status == "TOXIC") && (types.Contains("POISON") || types.Contains("STEEL"))
                       || status == "BURN" && types.Contains("FIRE")
                       || status == "PARALYSIS" && types.Contains("ELECTRIC")
                       || status == "FROZEN" && types.Contains("ICE");
            if (immune) { if (showFail) Say("It doesn't affect " + NameL(b) + "..."); return false; }
            if (AbilityPreventsStatus(b, status, source))
            {
                if (showFail) { if (status == "FROZEN" && EffWeather == Sun) Say("But it failed!"); else { Splash(b); Say("It doesn't affect " + NameL(b) + "..."); } }
                return false;
            }
            return true;
        }

        static string AlreadyText(string s)
        {
            switch (s) { case "SLEEP": return " is already asleep!"; case "BURN": return " already has a burn."; case "PARALYSIS": return " is already paralyzed!"; case "FROZEN": return " is already frozen!"; default: return " is already poisoned."; }
        }

        bool Inflict(Battler b, string status, bool showFail, Battler source = null)
        {
            if (!CanStatus(b, status, showFail, source)) return false;
            b.Pk.Status = status == "TOXIC" ? "POISON" : status;
            if (status == "TOXIC") b.ToxicCount = 1;
            if (status == "SLEEP") b.SleepTurns = 1 + Rng.Next(3);
            StatusEvent(b);
            switch (status)
            {
                case "SLEEP": Say(Name(b) + " fell asleep!"); break;
                case "POISON": Say(Name(b) + " was poisoned!"); break;
                case "TOXIC": Say(Name(b) + " was badly poisoned!"); break;
                case "BURN": Say(Name(b) + " was burned!"); break;
                case "PARALYSIS": Say(Name(b) + " is paralyzed! It may be unable to move!"); break;
                case "FROZEN": Say(Name(b) + " was frozen solid!"); break;
            }
            return true;
        }

        bool Confuse(Battler b, bool showFail)
        {
            if (b.Pk.Fainted) return false;
            if (b.Confusion > 0) { if (showFail) Say(Name(b) + " is already confused."); return false; }
            if (HasAbility(b, "OWNTEMPO")) { if (showFail) { Splash(b); Say(Name(b) + " doesn't become confused!"); } return false; }
            b.Confusion = 2 + Rng.Next(4);
            Say(Name(b) + " became confused!");
            return true;
        }

        /// <summary>Statuses named at the end of a code: "BurnTarget", "ParalyzeFlinchTarget", "HitTwoTimesPoisonTarget"...</summary>
        static string StatusFromCode(string code)
        {
            if (code.EndsWith("BadPoisonTarget")) return "TOXIC";
            if (code.Contains("SleepTarget")) return "SLEEP";
            if (code.EndsWith("PoisonTarget")) return "POISON";
            if (code.Contains("ParalyzeTarget") || code.StartsWith("ParalyzeFlinch")) return "PARALYSIS";
            if (code.Contains("BurnTarget") || code.StartsWith("BurnFlinch")) return "BURN";
            if (code.Contains("FreezeTarget") || code.StartsWith("FreezeFlinch")) return "FROZEN";
            return null;
        }

        /// <summary>Applies a damaging move's extra effect (or nothing if the code isn't one we know).</summary>
        void SecondaryEffect(Battler user, Battler target, MoveData md, string code)
        {
            bool shield = TargetHas(user, target, "SHIELDDUST") || target.Pk.Item == "COVERTCLOAK";
            if (code == "ParalyzeBurnOrFreezeTarget") { if (!target.Pk.Fainted && !shield) Inflict(target, new[] { "PARALYSIS", "BURN", "FROZEN" }[Rng.Next(3)], false, user); return; }
            string st = StatusFromCode(code);
            if (st != null && !target.Pk.Fainted && !shield) Inflict(target, st, false, user);
            if (code.Contains("Flinch") && !target.Pk.Fainted && !shield && !TargetHas(user, target, "INNERFOCUS")) target.Flinched = true;
            if (code.Contains("ConfuseTarget") && !target.Pk.Fainted && !shield) Confuse(target, false);
            var m = StatCode.Match(code);
            if (m.Success)
            {
                var b = m.Groups[2].Value == "User" ? user : target;
                if (!b.Pk.Fainted && !(b == target && shield)) foreach (int s in ParseStats(m.Groups[3].Value)) ChangeStat(b, s, (m.Groups[1].Value == "Raise" ? 1 : -1) * int.Parse(m.Groups[4].Value), user);
            }
        }

        void StatusMove(Battler user, Battler target, MoveData md, string code)
        {
            bool did = false;
            var m = StatCode.Match(code);
            if (m.Success)
            {
                var b = m.Groups[2].Value == "User" ? user : target;
                foreach (int s in ParseStats(m.Groups[3].Value)) did |= ChangeStat(b, s, (m.Groups[1].Value == "Raise" ? 1 : -1) * int.Parse(m.Groups[4].Value), user);
                if (code.EndsWith("ConfuseTarget")) did |= Confuse(target, true);
                return;
            }
            switch (code)
            {
                case "StartRainWeather": StartWeather(Rain, user); return;
                case "StartSunWeather": StartWeather(Sun, user); return;
                case "StartSandstormWeather": StartWeather(Sandstorm, user); return;
                case "StartHailWeather": StartWeather(Hail, user); return;
            }
            string st = StatusFromCode(code);
            if (st != null) { Inflict(target, st, true, user); return; }
            if (code.EndsWith("ConfuseTarget")) { Confuse(target, true); return; }
            if (code.StartsWith("HealUserHalfOfTotalHP") || code.StartsWith("HealUserDependingOnWeather") || code == "HealUserHalfOfTotalHPLoseFlyingTypeThisTurn")
            {
                int max = user.Pk.TotalHP(P);
                if (user.Pk.HP >= max) { Say(Name(user) + "'s HP is full!"); return; }
                int f = user.Pk.HP; user.Pk.HP = Math.Min(max, user.Pk.HP + (code.StartsWith("HealUserDependingOnWeather") ? WeatherHeal(max) : (max + 1) / 2)); Hp(user, f);
                Say(Name(user) + " had its HP restored.");
                return;
            }
            if (code == "HealUserFullyAndFallAsleep")
            {
                int max = user.Pk.TotalHP(P);
                if (user.Pk.HP >= max) { Say("But it failed!"); return; }
                int f = user.Pk.HP; user.Pk.HP = max; user.Pk.Status = "SLEEP"; user.SleepTurns = 3; StatusEvent(user); Hp(user, f);
                Say(Name(user) + " slept and became healthy!");
                return;
            }
            if (code == "RaiseUserCriticalHitRate2" || code == "RaiseUserCriticalHitRate") { user.CritStage = Math.Min(3, user.CritStage + 2); Say(Name(user) + " is getting pumped!"); return; }
            if (code == "ResetAllBattlersStatStages") { foreach (var b in Active) if (b != null) b.Stages = new int[8]; Say("All stat changes were eliminated!"); return; }
            if ((code == "SwitchOutTargetStatusMove" || code == "FleeFromBattle" || code == "SwitchOutUserStatusMove") && IsWild)
            {
                Outcome = BattleOutcome.Escaped;
                Say(code == "SwitchOutTargetStatusMove" ? Name(target) + " fled in fear!" : Name(user) + " fled from battle!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Flee });
                return;
            }
            if (code == "DoesNothingUnusableInGravity" || code == "DoesNothingCongratulations") { Say("But nothing happened!"); return; }
            Say("But it failed!");
        }

        // ------------------------------------------------------------------ end of turn
        void EndOfTurn()
        {
            WeatherEndOfTurn();
            foreach (var b in Active.OrderByDescending(x => Speed(x)))
            {
                if (b.Pk.Fainted) continue;
                EndOfTurnAbilities(b);
                if (b.Pk.Fainted) continue;
                int max = b.Pk.TotalHP(P);
                string st = (b.Pk.Status ?? "").ToUpperInvariant();
                if (st == "BURN") Residual(b, Math.Max(1, max / (HasAbility(b, "HEATPROOF") ? 32 : 16)), Name(b) + " was hurt by its burn!");
                else if (st == "POISON" && HasAbility(b, "POISONHEAL"))
                {
                    if (b.Pk.HP < max) { Splash(b); int f = b.Pk.HP; b.Pk.HP = Math.Min(max, b.Pk.HP + Math.Max(1, max / 8)); Hp(b, f); Say(Name(b) + " restored its HP."); }
                }
                else if (st == "POISON")
                {
                    if (b.ToxicCount > 0) { Residual(b, Math.Max(1, max * b.ToxicCount / 16), Name(b) + " was hurt by poison!"); b.ToxicCount = Math.Min(15, b.ToxicCount + 1); }
                    else Residual(b, Math.Max(1, max / 8), Name(b) + " was hurt by poison!");
                }
                if (!b.Pk.Fainted && b.BindTurns > 0)
                {
                    b.BindTurns--;
                    if (b.BindTurns == 0) Say(Name(b) + " was freed!");
                    else Residual(b, Math.Max(1, max / 8), Name(b) + " is hurt by the squeeze!");
                }
                if (!b.Pk.Fainted && b.Pk.Item == "LEFTOVERS" && b.Pk.HP < max)
                {
                    int f = b.Pk.HP; b.Pk.HP = Math.Min(max, b.Pk.HP + Math.Max(1, max / 16)); Hp(b, f);
                    Say(Name(b) + " restored a little HP using its Leftovers!");
                }
                // Berries that heal at half HP
                if (!b.Pk.Fainted && b.Pk.HP <= max / 2 && (b.Pk.Item == "ORANBERRY" || b.Pk.Item == "SITRUSBERRY"))
                {
                    int f = b.Pk.HP; b.Pk.HP = Math.Min(max, b.Pk.HP + (b.Pk.Item == "ORANBERRY" ? 10 : max / 4)); Hp(b, f);
                    var it = P.Items.FirstOrDefault(i => i.Id == b.Pk.Item);
                    Say(Name(b) + " restored its health using its " + (it != null ? it.Name : b.Pk.Item) + "!");
                    b.Pk.Item = null;
                }
            }
        }

        void Residual(Battler b, int dmg, string text)
        {
            if (HasAbility(b, "MAGICGUARD")) return; // no indirect damage
            int f = b.Pk.HP; b.Pk.HP = Math.Max(0, b.Pk.HP - dmg); Hp(b, f); Say(text);
        }

        // ------------------------------------------------------------------ fainting, EXP, next Pokémon
        void AfterFaints()
        {
            var foe = Active[1]; var me = Active[0];
            if (foe.Pk.Fainted && Outcome == BattleOutcome.Ongoing)
            {
                Say(Name(foe) + " fainted!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Faint, Side = 1, Party = foe.PartyIndex });
                GiveExp(foe);
                if (!AnyAble(1))
                {
                    Outcome = BattleOutcome.Win;
                    if (!IsWild) Say("You defeated " + TrainerTypeName + " " + TrainerName + "!");
                }
                else if (!IsWild)
                {
                    int next = AINextPokemon();
                    Active[1] = new Battler(Parties[1][next], 1, next);
                    Say(TrainerTypeName + " " + TrainerName + " sent out " + Active[1].Pk.Name(P) + "!");
                    _ev.Add(new BattleEvent { Kind = BattleEventKind.SendOut, Side = 1, Party = next });
                    if (!me.Pk.Fainted) MarkParticipant();
                    SwitchInAbility(Active[1]);
                }
            }
            if (me.Pk.Fainted && Outcome != BattleOutcome.Escaped && Outcome != BattleOutcome.Caught)
            {
                Say(me.Pk.Name(P) + " fainted!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Faint, Side = 0, Party = me.PartyIndex });
                if (!AnyAble(0))
                {
                    if (Outcome != BattleOutcome.Win) Outcome = BattleOutcome.Loss;
                    Say(PlayerName + " is out of usable Pokémon!");
                }
                else if (Outcome == BattleOutcome.Ongoing) NeedPlayerSwitch = true;
            }
        }

        int AINextPokemon()
        {
            var me = Active[0];
            int best = -1; double bestScore = -1;
            for (int i = 0; i < Parties[1].Count; i++)
            {
                if (!Able(Parties[1][i])) continue;
                double score = 1;
                if (TrainerSkill > 0) foreach (var t in Parties[1][i].Types(P)) score *= 1 / Math.Max(0.25, Effectiveness(t, me.Pk) == 0 ? 0.25 : Effectiveness(t, me.Pk));
                if (best < 0 || score > bestScore + 0.01) { best = i; bestScore = score; }
                if (TrainerSkill <= 0) break; // simple trainers send their next Pokémon in order
            }
            return best;
        }

        /// <summary>EXP for every Pokémon that fought the fainted foe (Gen 5 scaled formula), plus EVs.</summary>
        void GiveExp(Battler foe)
        {
            HashSet<int> set;
            if (!_participants.TryGetValue(foe.PartyIndex, out set)) return;
            var who = set.Where(i => i < Parties[0].Count && Able(Parties[0][i])).ToList();
            if (who.Count == 0) return;
            var fd = foe.Pk.Data(P);
            int baseExp = fd != null && fd.BaseExp > 0 ? fd.BaseExp : 50;
            int lf = foe.Pk.Level;
            foreach (int i in who)
            {
                var pk = Parties[0][i];
                if (pk.Level >= GrowthRates.MaxLevel) continue;
                int lp = pk.Level;
                double exp = baseExp * lf / 5.0 / who.Count;
                exp *= Math.Pow((2.0 * lf + 10) / (lf + lp + 10), 2.5);
                exp = Math.Floor(exp) + 1;
                if (!IsWild) exp = Math.Floor(exp * 1.5);
                if (!string.IsNullOrEmpty(pk.OTName) && pk.OTName != PlayerName) exp = Math.Floor(exp * 1.5);
                if (pk.Item == "LUCKYEGG") exp = Math.Floor(exp * 1.5);
                int amount = (int)exp;
                // EVs
                if (fd != null)
                    foreach (var ev in fd.EVs)
                    {
                        int s = PokeCreator.Pkmn.Stat.IndexOf(ev.Stat);
                        if (s < 0) continue;
                        int add = Math.Min(ev.Value, Math.Min(ItemEffects.MaxEV - pk.EV[s], ItemEffects.MaxTotalEV - pk.EV.Sum()));
                        if (add > 0) pk.EV[s] += add;
                    }
                Say(pk.Name(P) + " gained " + amount + " Exp. Points!");
                int expBefore = pk.Exp;
                var data = pk.Data(P);
                string rate = data != null ? data.GrowthRate : "Medium";
                pk.Exp = Math.Min(GrowthRates.ExpForLevel(rate, GrowthRates.MaxLevel), pk.Exp + amount);
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Exp, Side = 0, Party = i, Value = expBefore, Value2 = pk.Exp });
                while (pk.Level < GrowthRates.MaxLevel && pk.Exp >= GrowthRates.ExpForLevel(rate, pk.Level + 1))
                {
                    int exp0 = pk.Exp, hpFrom = pk.HP;
                    var moves = Growth.LevelUp(P, pk);
                    pk.Exp = exp0;
                    LeveledUp.Add(i);
                    _ev.Add(new BattleEvent { Kind = BattleEventKind.LevelUp, Side = 0, Party = i, Value = pk.Level, Value2 = hpFrom, Moves = moves, Text = pk.Name(P) + " grew to Lv. " + pk.Level + "!" });
                }
            }
        }

        // ------------------------------------------------------------------ player actions
        /// <summary>Sends out a different Pokémon (after a faint, or as the turn's action).</summary>
        public List<BattleEvent> SwitchAfterFaint(int party)
        {
            _ev = new List<BattleEvent>();
            NeedPlayerSwitch = false;
            Active[0] = new Battler(Parties[0][party], 0, party);
            Say("Go! " + Active[0].Pk.Name(P) + "!");
            _ev.Add(new BattleEvent { Kind = BattleEventKind.SendOut, Side = 0, Party = party });
            MarkParticipant();
            SwitchInAbility(Active[0]);
            return _ev;
        }

        void SwitchPlayer(int party)
        {
            var old = Active[0];
            SwitchOutAbility(old);
            Say(old.Pk.Name(P) + ", come back!");
            _ev.Add(new BattleEvent { Kind = BattleEventKind.Withdraw, Side = 0, Party = old.PartyIndex });
            Active[0] = new Battler(Parties[0][party], 0, party) { SwitchedIn = true };
            Say("Go! " + Active[0].Pk.Name(P) + "!");
            _ev.Add(new BattleEvent { Kind = BattleEventKind.SendOut, Side = 0, Party = party });
            MarkParticipant();
            SwitchInAbility(Active[0]);
        }

        bool TryRun()
        {
            if (!IsWild) { Say("No! There's no running from a Trainer battle!"); return false; }
            RunAttempts++;
            var me = Active[0]; var foe = Active[1];
            bool ghost = me.Pk.Types(P).Contains("GHOST");
            if (HasAbility(me, "RUNAWAY") || me.Pk.Item == "SMOKEBALL" || ghost)
            {
                Outcome = BattleOutcome.Escaped;
                Say("You got away safely!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Flee });
                return true;
            }
            bool grounded = !me.Pk.Types(P).Contains("FLYING") && !HasAbility(me, "LEVITATE") && me.Pk.Item != "AIRBALLOON";
            if ((HasAbility(foe, "SHADOWTAG") && !HasAbility(me, "SHADOWTAG")) || (HasAbility(foe, "ARENATRAP") && grounded) || (HasAbility(foe, "MAGNETPULL") && me.Pk.Types(P).Contains("STEEL")) || me.BindTurns > 0)
            {
                Say("You can't escape!");
                return false;
            }
            int a = Speed(Active[0]), b = Math.Max(1, Speed(Active[1]));
            int f = a * 128 / b + 30 * RunAttempts;
            if (a >= b || f > 255 || Rng.Next(256) < f)
            {
                Outcome = BattleOutcome.Escaped;
                Say("You got away safely!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Flee });
                return true;
            }
            Say("You couldn't get away!");
            return false;
        }

        void UseItem(string itemId, int party)
        {
            var item = P.Items.FirstOrDefault(i => i.Id == itemId);
            if (item == null) return;
            var pk = Parties[0][party];
            var info = ItemEffects.Info(P, item);
            int from = pk.HP;
            var o = ItemEffects.Use(P, pk, item, info, -1, new EvolutionContext());
            Say(PlayerName + " used the " + (item.Name ?? item.Id) + ".");
            if (Active[0].PartyIndex == party && pk.HP != from) Hp(Active[0], from);
            if (Active[0].PartyIndex == party) StatusEvent(Active[0]);
            Say(o.Message);
        }

        // ------------------------------------------------------------------ catching
        static double BallModifier(string ball, Battle b)
        {
            var foe = b.Active[1].Pk; var types = foe.Types(b.P);
            switch (ball)
            {
                case "GREATBALL": case "SAFARIBALL": case "SPORTBALL": return 1.5;
                case "ULTRABALL": return 2;
                case "MASTERBALL": return 255;
                case "NETBALL": return types.Contains("WATER") || types.Contains("BUG") ? 3.5 : 1;
                case "DIVEBALL": return 1;
                case "NESTBALL": return Math.Max(1, (41 - foe.Level) / 10.0);
                case "REPEATBALL": return 1;
                case "TIMERBALL": return Math.Min(4, 1 + b.Turn * 1229 / 4096.0);
                case "QUICKBALL": return b.Turn <= 1 ? 5 : 1;
                case "DUSKBALL": return DateTime.Now.Hour < 6 || DateTime.Now.Hour >= 20 ? 3 : 1;
                case "LEVELBALL":
                    {
                        int me = b.Active[0].Pk.Level, them = foe.Level;
                        return me >= them * 4 ? 8 : me >= them * 2 ? 4 : me > them ? 2 : 1;
                    }
                default: return 1;
            }
        }

        /// <summary>For the Repeat Ball: species already caught.</summary>
        public Func<string, bool> AlreadyCaught = s => false;

        void ThrowBall(string ball)
        {
            var item = P.Items.FirstOrDefault(i => i.Id == ball);
            string bname = item != null ? item.Name : ball;
            Say(PlayerName + " threw " + (StartsWithVowel(bname) ? "an " : "a ") + bname + "!");
            if (!IsWild)
            {
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Catch, Value = -1 });
                Say("The Trainer blocked your Poké Ball! Don't be a thief!");
                return;
            }
            var foe = Active[1].Pk;
            var fd = foe.Data(P);
            int rate = fd != null && fd.CatchRate > 0 ? fd.CatchRate : 45;
            double mod = BallModifier(ball, this);
            if (ball == "REPEATBALL" && AlreadyCaught(foe.Species)) mod = 3.5;
            int max = foe.TotalHP(P);
            string st = (foe.Status ?? "").ToUpperInvariant();
            double statusMod = st == "SLEEP" || st == "FROZEN" ? 2.5 : st.Length > 0 ? 1.5 : 1;
            double a = (3.0 * max - 2.0 * foe.HP) * rate * mod / (3.0 * max) * statusMod;
            int shakes;
            bool caught;
            if (mod >= 255 || a >= 255) { shakes = 4; caught = true; }
            else
            {
                double bv = 65536 / Math.Pow(255 / Math.Max(1, a), 0.1875);
                shakes = 0;
                while (shakes < 4 && Rng.Next(65536) < bv) shakes++;
                caught = shakes == 4;
            }
            _ev.Add(new BattleEvent { Kind = BattleEventKind.Catch, Value = Math.Min(3, shakes), Flag = caught, Text = ball });
            if (caught)
            {
                Say("Gotcha! " + foe.Name(P) + " was caught!");
                foe.Ball = ball;
                Active[1].Reset();
                CaughtPokemon = foe;
                Outcome = BattleOutcome.Caught;
            }
            else Say(shakes == 0 ? "Oh no! The Pokémon broke free!" : shakes == 1 ? "Aww! It appeared to be caught!" : shakes == 2 ? "Aargh! Almost had it!" : "Gah! It was so close, too!");
        }

        static bool StartsWithVowel(string s) { return !string.IsNullOrEmpty(s) && "AEIOUaeiou".IndexOf(s[0]) >= 0; }
    }
}
