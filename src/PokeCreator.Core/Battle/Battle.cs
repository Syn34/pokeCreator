using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PokeCreator.Data;
using PokeCreator.Pkmn;
using PokeCreator.Projects;

namespace PokeCreator.Battles
{
    public enum BattleEventKind { Message, Hp, Faint, SendOut, Withdraw, StatChange, Status, Exp, LevelUp, Catch, Flee, Hit, Miss, MoveUsed,
        /// <summary>A move's animation: Side/Party/Slot = user, Value/Value2 = target side/slot, Text = move.</summary>
        MoveAnim,
        /// <summary>A common animation (Text = "StatUp", "Poison", "Rain"...) on Side/Slot; Side -1 = whole field.</summary>
        CommonAnim,
        /// <summary>A Pokémon changed form (Mega Evolution): its sprite and databox update.</summary>
        FormChange }

    /// <summary>Something the battle screen shows, in order: a message, an HP bar change, a Pokémon fainting...</summary>
    public class BattleEvent
    {
        public BattleEventKind Kind;
        public int Side;            // 0 player, 1 opponent
        public int Party;           // party index on that side
        public int Slot;            // position on the field (0, or 0/1 in double battles) for SendOut / Faint
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
        public int Slot;        // which of the player's Pokémon this is for (double battles)
        public int TargetSide = -1, TargetSlot; // chosen target (-1 = automatic)
        public bool Mega;       // Mega Evolve before moving
        public static BattleCommand Fight(int slot) { return new BattleCommand { Kind = BattleCommandKind.Fight, Index = slot }; }
        public static BattleCommand Fight(int slot, int fieldSlot, int targetSide, int targetSlot) { return new BattleCommand { Kind = BattleCommandKind.Fight, Index = slot, Slot = fieldSlot, TargetSide = targetSide, TargetSlot = targetSlot }; }
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
        public int Slot;                // position on the field
        public bool FaintShown;         // "X fainted!" already shown
        public bool BatonPassing;       // switching out with Baton Pass
        public bool BoosterEnergy, TruantSkip, DisguiseBroken;
        /// <summary>Battle-only changes: types (Soak, Reflect Type, Burn Up...), stats (Transform, Power Trick, Power/Guard Split), moves (Transform, Mimic).</summary>
        public List<string> TypesOverride;
        public int[] StatsOverride;
        public List<PokemonMove> OriginalMoves;
        public int HealBlockTurns, EmbargoTurns, ThroatChopTurns, LaserFocus;
        public bool Tormented, Imprisoning, MagicCoat, Rage, Grudge, Powdered, Electrified, TransformedMoves, AteBerry, Octolocked, BeakBlast;
        public string LastHitType;
        public string StartItem, ConsumedBerry, ConsumedItem, CudChewBerry, UsedItemThisTurn;
        public int CudChewTurns, MetronomeCount;
        public bool MicleBoost, BindBand, Ejected, MimicryActive, ProteanUsed, Charged, TeraShellShown;
        // Effects from moves (cleared when the Pokémon leaves the field, as a new Battler is made)
        public int TauntTurns, EncoreTurns, DisableTurns, PerishCount = -1, LockTurns, RolloutCount, FuryCutterCount, StockpileCount, LockOnTurns, AirborneTurns, SubstituteHP;
        public string EncoreMove, DisableMove, LockedMove, LastMoveUsed, PlannedMove, AbilityOverride;
        public int LockedSlot;
        public bool LeechSeeded, Cursed, Nightmare, Endure, DestinyBond, Trapped, AquaRing, Ingrain, Foresight, HelpingHand, Grounded;
        public bool TookDamageThisTurn, StatsLoweredThisTurn, ActedThisTurn, LastMoveFailed, SwitchOutPending;
        public int LastDamageTaken; public bool LastDamagePhysical;
        public Battler AttractedTo;
        public Battler(Pokemon pk, int side, int idx) { Pk = pk; Side = side; PartyIndex = idx; }
        public void Reset() { Stages = new int[8]; Confusion = 0; ToxicCount = 0; BindTurns = 0; CritStage = 0; Flinched = false; MustRecharge = false; Protected = false; ProtectChain = 0; Charging = null; Invulnerable = false; TurnsOut = 0; }
    }

    public enum BattleOutcome { Ongoing = 0, Win = 1, Loss = 2, Escaped = 3, Caught = 4 }

    /// <summary>
    /// Single and double battles (wild or trainer). The screen asks for a command for each of the player's Pokémon,
    /// calls DoTurn, and plays the returned events. State changes (HP, PP, status, EXP) are applied as the turn runs.
    /// </summary>
    public partial class Battle
    {
        public readonly Project P;
        public readonly Random Rng;
        public readonly List<Pokemon>[] Parties = new List<Pokemon>[2];
        /// <summary>Pokémon on the field: Field[side][slot]; slot 1 is only used in double battles. Empty = null.</summary>
        public readonly Battler[][] Field = { new Battler[3], new Battler[3] };
        /// <summary>1 for single battles, 2 for double battles.</summary>
        public int SlotsPerSide { get; private set; }
        /// <summary>More than one Pokémon per side (double or triple battle).</summary>
        public bool IsDouble { get { return SlotsPerSide >= 2; } }
        public bool IsTriple { get { return SlotsPerSide == 3; } }
        /// <summary>Slot 0 of each side (what single-battle code means by "the" Pokémon).</summary>
        public Battler[] Active { get { return new[] { Field[0][0], Field[1][0] }; } }
        public readonly bool IsWild;
        public string TrainerName, TrainerTypeName, PlayerName = "Red";
        /// <summary>Two-trainer double battles: the second trainer, and which trainer (0/1) owns each foe party member.</summary>
        public string Trainer2Name, Trainer2TypeName;
        public int[] FoeOwner;
        bool TwoTrainers { get { return FoeOwner != null && !string.IsNullOrEmpty(Trainer2Name); } }
        /// <summary>"Camper Jeff" for the trainer owning this foe party member.</summary>
        string TrainerLabel(int party) { return (TwoTrainers && party < FoeOwner.Length && FoeOwner[party] == 1 ? Trainer2TypeName + " " + Trainer2Name : TrainerTypeName + " " + TrainerName).Trim(); }
        string AllTrainers { get { return TwoTrainers ? TrainerTypeName + " " + TrainerName + " and " + Trainer2TypeName + " " + Trainer2Name : TrainerTypeName + " " + TrainerName; } }
        public int TrainerSkill;
        public BattleOutcome Outcome;
        public int Turn, RunAttempts;
        /// <summary>Player slots whose Pokémon fainted and must be replaced before the next turn.</summary>
        public readonly List<int> PlayerSlotsToFill = new List<int>();
        public bool NeedPlayerSwitch { get { return PlayerSlotsToFill.Count > 0; } }
        /// <summary>"Switch" battle style: before a trainer sends in their next Pokémon, the player may change theirs.</summary>
        public bool SwitchStyle;
        /// <summary>Items the opposing trainer(s) can use (e.g. Full Restore), each once.</summary>
        public readonly List<string> FoeItems = new List<string>();
        int _pendingFoe = -1, _pendingFoeSlot;
        /// <summary>A trainer's next Pokémon is waiting so the player can choose to switch (FinishFoeSendOut).</summary>
        public bool PendingFoeSendOut { get { return _pendingFoe >= 0; } }
        public Pokemon PendingFoePokemon { get { return _pendingFoe >= 0 ? Parties[1][_pendingFoe] : null; } }
        public string PendingFoeTrainer { get { return _pendingFoe >= 0 ? TrainerLabel(_pendingFoe) : ""; } }

        /// <summary>Completes a held-back send-out; switchTo = the player's new Pokémon (party index) or -1 to keep it.</summary>
        public List<BattleEvent> FinishFoeSendOut(int switchTo)
        {
            if (Recording != null) Recording.Add(new List<int> { 2, switchTo });
            _ev = new List<BattleEvent>();
            if (_pendingFoe < 0) return _ev;
            if (switchTo >= 0 && Field[0][0] != null) SwitchPlayer(0, switchTo);
            var nb = Place(1, _pendingFoeSlot, _pendingFoe);
            _pendingFoe = -1;
            Say(TrainerLabel(nb.PartyIndex) + " sent out " + nb.Pk.Name(P) + "!");
            SendOutEvent(nb);
            MarkParticipant();
            SwitchInAbility(nb);
            return _ev;
        }
        public readonly HashSet<int> LeveledUp = new HashSet<int>();  // party indexes to check for evolution afterwards
        public Pokemon CaughtPokemon;
        readonly Dictionary<int, HashSet<int>> _participants = new Dictionary<int, HashSet<int>>(); // foe party index -> player party indexes
        List<BattleEvent> _ev;

        public Battle(Project p, List<Pokemon> playerParty, List<Pokemon> foeParty, bool wild, Random rng, bool doubleBattle = false, bool tripleBattle = false)
        {
            P = p; Rng = rng ?? new Random(); Parties[0] = playerParty; Parties[1] = foeParty; IsWild = wild;
            // A double (triple) battle needs two (three) Pokémon that can fight on each side
            int mine = playerParty.Count(Able), theirs = foeParty.Count(Able);
            SlotsPerSide = tripleBattle && mine >= 3 && theirs >= 3 ? 3 : (doubleBattle || tripleBattle) && mine >= 2 && theirs >= 2 ? 2 : 1;
        }

        /// <summary>Whether two Pokémon are next to each other (in triple battles the ones at opposite ends aren't).</summary>
        public bool Near(Battler a, Battler b)
        {
            if (a == null || b == null || a == b) return false;
            if (!IsTriple) return true;
            return Math.Abs(a.Slot - b.Slot) <= 1;
        }

        /// <summary>The Pokémon on the same side next to this one.</summary>
        List<Battler> Allies(Battler b) { return Live(b.Side).Where(x => x != b && Near(x, b)).ToList(); }

        /// <summary>Every Pokémon on the field (fainted ones included until they're replaced).</summary>
        public IEnumerable<Battler> OnField
        {
            get { for (int s = 0; s < 2; s++) for (int i = 0; i < SlotsPerSide; i++) if (Field[s][i] != null) yield return Field[s][i]; }
        }
        /// <summary>Pokémon of a side that can still fight.</summary>
        public List<Battler> Live(int side) { return Field[side].Take(SlotsPerSide).Where(b => b != null && !b.Pk.Fainted).ToList(); }
        bool OnFieldParty(int side, int party) { return Field[side].Take(SlotsPerSide).Any(b => b != null && b.PartyIndex == party); }
        Battler Ally(Battler b) { if (!IsDouble) return null; return Allies(b).FirstOrDefault(); }
        /// <summary>An opposing Pokémon that can be hit: the chosen one, or another if it fainted.</summary>
        Battler OpponentFor(Battler user, Battler preferred)
        {
            if (preferred != null && !preferred.Pk.Fainted && preferred.Side != user.Side) return preferred;
            var foes = Live(1 - user.Side);
            if (foes.Count == 0) return null;
            return foes.FirstOrDefault(f => f.Slot == user.Slot) ?? foes.FirstOrDefault(f => Near(f, user)) ?? foes[0];
        }

        string Names(List<Battler> list)
        {
            if (list.Count == 0) return "";
            if (list.Count == 1) return list[0].Pk.Name(P);
            return string.Join(", ", list.Take(list.Count - 1).Select(b => b.Pk.Name(P))) + " and " + list[list.Count - 1].Pk.Name(P);
        }

        Battler Place(int side, int slot, int party)
        {
            var b = new Battler(Parties[side][party], side, party) { Slot = slot };
            var old = Field[side][slot];
            RestoreBattleMoves(old);
            if (old != null && old.BatonPassing && !old.Pk.Fainted) PassEffects(old, b);
            Field[side][slot] = b;
            return b;
        }

        /// <summary>The next able party members not already on the field.</summary>
        List<int> Bench(int side) { return Enumerable.Range(0, Parties[side].Count).Where(i => Able(Parties[side][i]) && !OnFieldParty(side, i) && !(side == 0 && IsPartnerIndex(i))).ToList(); }
        /// <summary>The bench for one foe slot: with two trainers, slot 0 is the first trainer's and slot 1 the second's.</summary>
        List<int> FoeBench(int slot) { var b = Bench(1); return TwoTrainers ? b.Where(i => i < FoeOwner.Length && FoeOwner[i] == slot).ToList() : b; }

        // ------------------------------------------------------------------ helpers
        public static bool Able(Pokemon pk) { return pk != null && !pk.IsEgg && !pk.Fainted; }
        public int FirstAble(int side) { return Parties[side].FindIndex(Able); }
        public bool AnyAble(int side) { return Parties[side].Any(Able); }
        string Name(Battler b) { return b.Side == (FlipNames ? 0 : 1) ? (IsWild ? "The wild " : "The opposing ") + b.Pk.Name(P) : b.Pk.Name(P); }
        string NameCap(Battler b) { return Name(b); }
        /// <summary>Name in the middle of a sentence: "the wild Pidgey", "the opposing Onix".</summary>
        string NameL(Battler b) { string n = Name(b); return n.StartsWith("The ") ? "the " + n.Substring(4) : n; }
        MoveData Move(string id) { return P.Moves.FirstOrDefault(m => m.Id == id); }
        void Say(string text) { _ev.Add(new BattleEvent { Kind = BattleEventKind.Message, Text = text }); }
        void Hp(Battler b, int from) { _ev.Add(new BattleEvent { Kind = BattleEventKind.Hp, Side = b.Side, Party = b.PartyIndex, Slot = b.Slot, Value = from, Value2 = b.Pk.HP }); }
        void SendOutEvent(Battler b) { SentOut.Add(b.Pk); _ev.Add(new BattleEvent { Kind = BattleEventKind.SendOut, Side = b.Side, Party = b.PartyIndex, Slot = b.Slot }); }

        /// <summary>Starts the battle: send-out messages. Call once.</summary>
        public List<BattleEvent> Start()
        {
            InitialWeather = Weather;
            if (StartOverride != null) return StartOverride();
            _ev = new List<BattleEvent>();
            var foes = new List<Battler>();
            if (TwoTrainers)
                for (int i = 0; i < SlotsPerSide; i++) { var fb = FoeBench(i); if (fb.Count > 0) foes.Add(Place(1, i, fb[0])); }
            else
            {
                var foeBench = Bench(1);
                for (int i = 0; i < SlotsPerSide && i < foeBench.Count; i++) foes.Add(Place(1, i, foeBench[i]));
            }
            if (IsWild)
            {
                foreach (var b in foes) SendOutEvent(b);
                Say(foes.Count >= 2 ? "Oh! A wild " + Names(foes) + " appeared!" : "A wild " + foes[0].Pk.Name(P) + " appeared!");
            }
            else if (TwoTrainers)
            {
                Say("You are challenged by " + AllTrainers + "!");
                foreach (var b in foes) { Say(TrainerLabel(b.PartyIndex) + " sent out " + b.Pk.Name(P) + "!"); SendOutEvent(b); }
            }
            else if (!FlipNames)
            {
                Say((TrainerTypeName + " " + TrainerName).Trim() + " would like to battle!");
                Say((TrainerTypeName + " " + TrainerName).Trim() + " sent out " + Names(foes) + "!");
                foreach (var b in foes) SendOutEvent(b);
            }
            else Say((TrainerTypeName + " " + TrainerName).Trim() + " would like to battle!");
            var mine = new List<Battler>();
            if (Safari)
            {
                // Safari Zone: the player's Pokémon stays in its Ball
                var own = Bench(0);
                if (own.Count > 0) Place(0, 0, own[0]);
                SafariSetup();
                return _ev;
            }
            if (HasPartner) mine = PlacePlayerSideWithPartner();
            else
            {
                var myBench = Bench(0);
                for (int i = 0; i < SlotsPerSide && i < myBench.Count; i++) mine.Add(Place(0, i, myBench[i]));
                if (FlipNames)
                {
                    // Worded for the player on side 1: side 0 is the other player
                    Say((TrainerTypeName + " " + TrainerName).Trim() + " sent out " + Names(mine) + "!");
                    Say("Go! " + string.Join(" and ", foes.Select(f => f.Pk.Name(P))) + "!");
                    foreach (var b in foes) SendOutEvent(b);
                }
                else Say("Go! " + Names(mine) + "!");
            }
            foreach (var b in mine) SendOutEvent(b);
            MarkParticipant();
            if (Weather != NoWeather) Say(WeatherFieldText(Weather));
            foreach (var b in OnField.OrderByDescending(x => Speed(x)).ToList()) SwitchInAbility(b);
            return _ev;
        }

        void MarkParticipant()
        {
            foreach (var foe in Live(1))
            {
                HashSet<int> set;
                if (!_participants.TryGetValue(foe.PartyIndex, out set)) _participants[foe.PartyIndex] = set = new HashSet<int>();
                foreach (var me in Live(0)) set.Add(me.PartyIndex);
            }
        }

        /// <summary>Moves the player's Pokémon can use (with PP); Struggle if none.</summary>
        /// <summary>Whether the Pokémon has a move it can pick (PP left and not blocked by Taunt / Disable / Encore); if not, it uses Struggle.</summary>
        public bool HasUsableMove(Battler b) { for (int i = 0; i < b.Pk.Moves.Count; i++) if (b.Pk.Moves[i].PP > 0 && CanSelect(b, i)) return true; return false; }

        // ------------------------------------------------------------------ turns
        public List<BattleEvent> DoTurn(BattleCommand player)
        {
            if (player != null) player.Slot = 0;
            return DoTurn(new List<BattleCommand> { player });
        }

        /// <summary>One turn. commands: one per player Pokémon on the field (BattleCommand.Slot says which).</summary>
        public List<BattleEvent> DoTurn(List<BattleCommand> commands)
        {
            if (TurnOverride != null) return TurnOverride(commands);
            if (Recording != null)
            {
                var step = new List<int> { 0 };
                foreach (var c in commands.Where(x => x != null)) step.AddRange(EncodeCommand(c));
                Recording.Add(step);
            }
            _ev = new List<BattleEvent>();
            Turn++;
            foreach (var b in OnField) { b.SwitchedIn = false; b.MovedLast = false; }
            var orders = new List<KeyValuePair<Battler, BattleCommand>>();
            foreach (var me in Live(0))
            {
                if (IsPartner(me))
                {
                    orders.Add(new KeyValuePair<Battler, BattleCommand>(me, me.Charging != null || me.MustRecharge ? BattleCommand.Fight(me.ChargingSlot) : ChooseAI(me)));
                    continue;
                }
                var cmd = commands.FirstOrDefault(c => c != null && c.Slot == me.Slot) ?? BattleCommand.Fight(-1);
                // A charging or recharging Pokémon has its command decided for it
                if (me.Charging != null || me.MustRecharge) cmd = new BattleCommand { Kind = BattleCommandKind.Fight, Index = me.ChargingSlot, Slot = me.Slot, TargetSide = cmd.TargetSide, TargetSlot = cmd.TargetSlot };
                else if (FacilityRule == "palace") cmd = PalaceCommand(me);
                else if (FacilityRule == "arena" && cmd.Kind == BattleCommandKind.Switch) cmd = BattleCommand.Fight(-1);
                orders.Add(new KeyValuePair<Battler, BattleCommand>(me, cmd));
            }
            foreach (var foe in Live(1))
            {
                if (ExternalCommand != null)
                {
                    var ec = foe.Charging != null || foe.MustRecharge ? BattleCommand.Fight(foe.ChargingSlot) : ExternalCommand(foe);
                    ec.Slot = foe.Slot;
                    orders.Add(new KeyValuePair<Battler, BattleCommand>(foe, ec));
                    continue;
                }
                if (FacilityRule == "palace") { orders.Add(new KeyValuePair<Battler, BattleCommand>(foe, foe.Charging != null || foe.MustRecharge ? BattleCommand.Fight(foe.ChargingSlot) : PalaceCommand(foe))); continue; }
                var item = foe.Charging == null && !foe.MustRecharge && FacilityRule == null ? FoeItemChoice(foe) : null;
                if (item != null) { FoeUseItem(foe, item); continue; }
                var sw = foe.Charging == null && !foe.MustRecharge && foe.LockedMove == null ? AISwitch(foe, TrainerSkill) : null;
                if (sw != null) { orders.Add(new KeyValuePair<Battler, BattleCommand>(foe, sw)); continue; }
                orders.Add(new KeyValuePair<Battler, BattleCommand>(foe, foe.Charging != null || foe.MustRecharge ? BattleCommand.Fight(foe.ChargingSlot) : ChooseAI(foe)));
            }

            // Non-move actions go first: run, balls, items, switching
            foreach (var o in orders.Where(x => x.Key.Side == 0).ToList())
            {
                var cmd = o.Value;
                if (cmd.Kind == BattleCommandKind.Run) { if (TryRun()) return _ev; orders.Remove(o); break; }
                if (cmd.Kind == BattleCommandKind.Ball) { ThrowBall(cmd.Item); if (Outcome != BattleOutcome.Ongoing) return _ev; orders.Remove(o); }
                else if (cmd.Kind == BattleCommandKind.Item)
                {
                    UseItem(cmd.Item, cmd.Index, cmd.TargetSlot, o.Key);
                    if (Outcome != BattleOutcome.Ongoing) return _ev;
                    orders.Remove(o);
                }
                else if (cmd.Kind == BattleCommandKind.Switch)
                {
                    if (IsTrapped(o.Key)) Say(Name(o.Key) + " can't be switched out!");
                    else
                    {
                        // Pursuit: a foe using it hits the Pokémon before it leaves, with double power
                        foreach (var po in orders.Where(x => x.Key.Side != o.Key.Side && x.Value.Kind == BattleCommandKind.Fight).ToList())
                        {
                            var pmd = MoveFor(po.Key, po.Value.Index);
                            if (pmd == null || pmd.FunctionCode != "PursueSwitchingFoe" || po.Key.Pk.Fainted || o.Key.Pk.Fainted) continue;
                            Say(Name(o.Key) + " is being withdrawn...");
                            _pursuitBoost = true;
                            Act(po.Key, o.Key, po.Value.Index);
                            _pursuitBoost = false;
                            orders.Remove(po);
                        }
                        if (!o.Key.Pk.Fainted && Outcome == BattleOutcome.Ongoing) SwitchPlayer(o.Key.Slot, cmd.Index);
                    }
                    orders.Remove(o);
                }
            }
            // Side 1 switching by command (link battles)
            foreach (var o in orders.Where(x => x.Key.Side == 1 && x.Value.Kind == BattleCommandKind.Switch).ToList())
            {
                if (!IsTrapped(o.Key) && o.Value.Index >= 0 && o.Value.Index < Parties[1].Count && Able(Parties[1][o.Value.Index]) && !OnFieldParty(1, o.Value.Index))
                    SwitchFoe(o.Key.Slot, o.Value.Index);
                else if (IsTrapped(o.Key)) Say(Name(o.Key) + " can't be switched out!");
            }
            orders.RemoveAll(o => o.Value.Kind != BattleCommandKind.Fight);
            foreach (var o in orders)
            {
                int forced = ForcedSlot(o.Key);
                if (forced >= 0 && o.Key.Charging == null) o.Value.Index = forced;
                var pmd = MoveFor(o.Key, o.Value.Index); o.Key.PlannedMove = pmd != null ? pmd.Id : null;
            }

            StartOfTurnBookkeeping();
            // Mega Evolution happens before any move, fastest first
            foreach (var o in orders.OrderByDescending(x => Speed(x.Key)).ToList())
                if ((o.Key.Side == 0 && o.Value.Mega) || (o.Key.Side == 1 && (ExternalCommand != null ? o.Value.Mega && MegaForm(o.Key.Pk) != null && !_megaUsed[1] : AIWantsMega(o.Key)))) MegaEvolve(o.Key);

            // Moves: priority, then speed (ties at random)
            var tie = orders.ToDictionary(o => o.Key, o => Rng.Next(1000));
            _quickClaw.Clear();
            var sub = orders.ToDictionary(o => o.Key, o => SubPriority(o.Key, MoveFor(o.Key, o.Value.Index)));
            orders = orders.OrderByDescending(o => EffectivePriority(o.Key, MoveFor(o.Key, o.Value.Index))).ThenByDescending(o => sub[o.Key])
                           .ThenByDescending(o => TrickRoom > 0 ? -Speed(o.Key) : Speed(o.Key)).ThenBy(o => tie[o.Key]).ToList();
            // A Quick Claw only matters when it changed the order
            foreach (var o in orders.ToList()) if (sub[o.Key] > 0 && orders.Where(x => x.Key != o.Key && EffectivePriority(x.Key, MoveFor(x.Key, x.Value.Index)) == EffectivePriority(o.Key, MoveFor(o.Key, o.Value.Index))).All(x => Speed(x.Key) <= Speed(o.Key) && TrickRoom == 0)) _quickClaw.Remove(o.Key);
            _orders = orders;
            foreach (var o in orders)
                if (o.Key.PlannedMove != null && Move(o.Key.PlannedMove) != null && Move(o.Key.PlannedMove).FunctionCode == "BurnAttackerBeforeUserActs")
                { o.Key.BeakBlast = true; Say(Name(o.Key) + " started heating up its beak!"); }
            for (int i = 0; i < orders.Count; i++)
            {
                _orderIndex = i;
                var user = orders[i].Key; var cmd = orders[i].Value;
                if (Outcome != BattleOutcome.Ongoing) break;
                if (user.Pk.Fainted || Field[user.Side][user.Slot] != user || user.Ejected) continue;
                if (Live(1 - user.Side).Count == 0 && Live(user.Side).Count > 0 && !SelfTargeting(user, cmd.Index)) continue;
                user.MovedLast = i == orders.Count - 1;
                Battler target = cmd.TargetSide >= 0 && cmd.TargetSlot >= 0 && cmd.TargetSlot < SlotsPerSide ? Field[cmd.TargetSide][cmd.TargetSlot] : null;
                if (target != null && target.Side == user.Side && target != user && target.Pk.Fainted) target = null;
                if (target == null || target.Side != user.Side) target = OpponentFor(user, target);
                AnnounceMoveFirst(user);
                Act(user, target ?? user, cmd.Index);
                CheckAllItems();
            }

            if (Outcome == BattleOutcome.Ongoing && Live(0).Count > 0 && Live(1).Count > 0) { EndOfTurn(); ArenaEndOfTurn(); }
            if (Outcome == BattleOutcome.Ongoing && WildFlees && IsWild) WildTriesToFlee();
            AfterFaints();
            if (Outcome == BattleOutcome.Ongoing) ResolveSwitchOuts();
            foreach (var b in OnField)
            {
                b.ProtectedLast = b.Protected; b.Protected = false; b.Flinched = false; b.TurnsOut++;
                b.TookDamageThisTurn = false; b.StatsLoweredThisTurn = false; b.UsedItemThisTurn = null; b.ActedThisTurn = false; b.HelpingHand = false; b.Endure = false;
            }
            _centerOfAttention[0] = _centerOfAttention[1] = null;
            return _ev;
        }

        bool SelfTargeting(Battler b, int slot)
        {
            var md = MoveFor(b, slot);
            return md != null && (md.Target == "User" || md.Target == "UserSide" || md.Target == "BothSides" || md.Target == "UserAndAllies" || md.Target == "UserOrNearAlly");
        }

        bool Ended() { return Outcome != BattleOutcome.Ongoing || Live(0).Count == 0 || Live(1).Count == 0; }

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
            if (Tailwind[b.Side] > 0) s *= 2;
            if (Swamp[b.Side] > 0) s /= 4;
            return Math.Max(1, (int)(s * AbilitySpeed(b) * ItemSpeedMod(b)));
        }

        static double StageMult(int s) { return s >= 0 ? (2.0 + s) / 2.0 : 2.0 / (2.0 - s); }
        static double AccMult(int s) { return s >= 0 ? (3.0 + s) / 3.0 : 3.0 / (3.0 - s); }

        /// <summary>A battler's types, after battle changes.</summary>
        public List<string> TypesOf(Battler b) { return b.TypesOverride ?? b.Pk.Types(P); }
        /// <summary>A battler's stats, after battle changes.</summary>
        int[] StatsOf(Battler b) { return b.StatsOverride ?? b.Pk.Stats(P); }
        /// <summary>Type effectiveness against a battler (its battle types).</summary>
        double EffectivenessOn(string moveType, Battler target)
        {
            double m = 1;
            foreach (var t in TypesOf(target).Distinct())
            {
                var td = P.Types.FirstOrDefault(x => x.Id == t);
                if (td == null) continue;
                if (Inverse) { m *= InverseMod(td, moveType); continue; }
                if (td.Immunities.Contains(moveType)) return 0;
                if (td.Weaknesses.Contains(moveType)) m *= 2;
                else if (td.Resistances.Contains(moveType)) m *= 0.5;
            }
            return m;
        }

        int Stat(Battler b, int stat, bool ignoreUp = false, bool ignoreDown = false)
        {
            int v = StatsOf(b)[stat];
            int st = b.Stages[stat];
            if ((ignoreUp && st > 0) || (ignoreDown && st < 0)) st = 0;
            return Math.Max(1, (int)(v * StageMult(st)));
        }

        // ------------------------------------------------------------------ AI
        /// <summary>A trainer's item worth using now: healing at 1/4 HP or less, or a status cure.</summary>
        string FoeItemChoice(Battler foe)
        {
            if (IsWild || FoeItems.Count == 0) return null;
            int hp = foe.Pk.HP, max = foe.Pk.TotalHP(P);
            foreach (var id in FoeItems.Distinct().ToList())
            {
                var it = P.Items.FirstOrDefault(i => i.Id == id);
                if (it == null) continue;
                var kind = ItemEffects.Info(P, it).Kind;
                if ((kind == ItemKind.HealHP || kind == ItemKind.FullRestore) && hp * 4 <= max && Rng.Next(100) < 80) return id;
                if (kind == ItemKind.CureStatus && !string.IsNullOrEmpty(foe.Pk.Status) && ItemEffects.Works(P, foe.Pk, it, ItemEffects.Info(P, it), new EvolutionContext()) && Rng.Next(100) < 50) return id;
            }
            return null;
        }

        void FoeUseItem(Battler foe, string id)
        {
            FoeItems.Remove(id);
            var it = P.Items.FirstOrDefault(i => i.Id == id);
            string iname = it != null ? it.Name : id;
            int from = foe.Pk.HP;
            ItemEffects.Use(P, foe.Pk, it, ItemEffects.Info(P, it), -1, new EvolutionContext());
            Say(TrainerLabel(foe.PartyIndex) + " used " + (StartsWithVowel(iname) ? "an " : "a ") + iname + "!");
            if (foe.Pk.HP != from) { Hp(foe, from); Say(Name(foe) + "'s HP was restored."); }
            StatusEvent(foe);
        }

        /// <summary>The AI's command for one of its Pokémon: a move and, in doubles, which of the player's Pokémon to hit.</summary>
        BattleCommand ChooseAI(Battler me)
        {
            var targets = Live(1 - me.Side);
            if (IsTriple && targets.Any(x => Near(x, me))) targets = targets.Where(x => Near(x, me)).ToList();
            if (targets.Count == 0) return BattleCommand.Fight(-1);
            var t = targets[Rng.Next(targets.Count)];
            if (!IsWild && TrainerSkill > 0 && targets.Count > 1) t = targets.OrderByDescending(x => BestScore(me, x)).First();
            var cmd = ChooseAI(me, t);
            cmd.TargetSide = t.Side; cmd.TargetSlot = t.Slot;
            return cmd;
        }

        double BestScore(Battler me, Battler foe)
        {
            double best = 0;
            foreach (var m in me.Pk.Moves.Where(x => x.PP > 0))
            {
                var md = Move(m.Id);
                if (md == null || md.Category == "Status") continue;
                double eff = EffectivenessOn(md.Type, foe);
                best = Math.Max(best, Math.Max(1, md.Power <= 1 ? 60 : md.Power) * eff * (TypesOf(me).Contains(md.Type) ? 1.5 : 1));
            }
            return best;
        }

        BattleCommand ChooseAI(Battler me, Battler foe)
        {
            var usable = Enumerable.Range(0, me.Pk.Moves.Count).Where(i => me.Pk.Moves[i].PP > 0 && CanSelect(me, i)).ToList();
            if (usable.Count == 0) usable = Enumerable.Range(0, me.Pk.Moves.Count).Where(i => me.Pk.Moves[i].PP > 0).ToList();
            if (usable.Count == 0) return BattleCommand.Fight(-1);
            int skill = IsPartner(me) ? PartnerSkill : TrainerSkill;
            if ((IsWild && me.Side == 1) || skill <= 0) return BattleCommand.Fight(usable[Rng.Next(usable.Count)]);
            if (skill >= 32) return SmartAI(me, foe, usable, skill);
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
                    double eff = EffectivenessOn(md.Type, foe);
                    double stab = TypesOf(me).Contains(md.Type) ? 1.5 : 1;
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

        /// <summary>Inverse Battle (setBattleRule("inverseBattle")): weaknesses and resistances swap, immunities become weaknesses.</summary>
        public bool Inverse;
        /// <summary>Species the player has caught (critical captures) and whether they have the Catching Charm.</summary>
        public int CaughtCount;
        public bool CatchingCharm;
        // Obedience (as Essentials): a Pokémon from another trainer above the badge level may ignore orders.
        // -1 turns the checks off (link and facility battles)
        public int ObedienceBadges = -1;
        public int PlayerId;
        public bool AnyCanDisobey;

        public bool Foreign(Pokemon pk)
        {
            if (string.IsNullOrEmpty(pk.OTName)) return false;
            return pk.OTName != PlayerName || (pk.OTId != 0 && PlayerId != 0 && pk.OTId != PlayerId);
        }

        /// <summary>Badge level a player's Pokémon obeys up to (10 per badge, any level with 8).</summary>
        public static int ObedienceLevel(int badges) { return badges >= 8 ? GrowthRates.MaxLevel : 10 * (badges + 1); }

        // True when the chosen move goes ahead (slot may change to another move); false when the turn is lost
        bool Obeys(Battler user, ref int slot)
        {
            if (ObedienceBadges < 0 || user.Side != 0 || IsPartnerIndex(user.PartyIndex)) return true;
            if (user.Charging != null || user.LockedMove != null || !HasUsableMove(user)) return true;
            if (!AnyCanDisobey && !Foreign(user.Pk)) return true;
            int cap = ObedienceLevel(ObedienceBadges), lvl = user.Pk.Level;
            if (lvl <= cap || (lvl + cap) * Rng.Next(256) / 256 < cap) return true;
            string n = Name(user);
            user.Rage = false;
            bool asleep = (user.Pk.Status ?? "").ToUpperInvariant() == "SLEEP";
            if (asleep && UsableWhileAsleep(user, slot)) { Say(n + " ignored orders and kept sleeping!"); return false; }
            if ((lvl + cap) * Rng.Next(256) / 256 < cap)
            {
                Say(n + " ignored orders!");
                var others = new List<int>();
                for (int i = 0; i < user.Pk.Moves.Count; i++) if (i != slot && CanSelect(user, i)) others.Add(i);
                if (others.Count == 0) return false;
                slot = others[Rng.Next(others.Count)];
                return true;
            }
            int c = lvl - cap, r = Rng.Next(256);
            if (r < c && CanStatus(user, "SLEEP", false))
            {
                user.Pk.Status = "SLEEP"; user.SleepTurns = 1 + Rng.Next(3); StatusEvent(user);
                CommonAnim("Sleep", user); Say(n + " began to nap!");
                return false;
            }
            r -= c;
            if (r < c && !asleep)
            {
                int dmg = DamageFormula(user, user, 40, true, null, false, 1, false);
                int from = user.Pk.HP; user.Pk.HP = Math.Max(0, user.Pk.HP - dmg); Hp(user, from);
                Say(n + " won't obey! It hurt itself in its confusion!");
                return false;
            }
            string[] refuse = { " won't obey!", " turned away!", " is loafing around!", " pretended not to notice!" };
            Say(n + refuse[Rng.Next(4)]);
            return false;
        }
        /// <summary>
        /// Where the battle happens (Essentials' environments: Grass, TallGrass, MovingWater, StillWater, Puddle,
        /// Underwater, Cave, Rock, Sand, Forest, ForestGrass, Snow, Ice, Volcano, Graveyard, Sky, Space, UltraSpace).
        /// Nature Power, Camouflage and Secret Power depend on it.
        /// </summary>
        public string Environment;

        string Env { get { return (Environment ?? "").Replace(" ", "").ToLowerInvariant(); } }

        /// <summary>Nature Power's move for the environment (Gen 7 table).</summary>
        string EnvironmentMove()
        {
            switch (Env)
            {
                case "grass": case "tallgrass": case "forest": case "forestgrass": return "ENERGYBALL";
                case "movingwater": case "stillwater": case "underwater": return "HYDROPUMP";
                case "puddle": return "MUDBOMB";
                case "cave": return "POWERGEM";
                case "rock": case "sand": return "EARTHPOWER";
                case "snow": case "ice": return "ICEBEAM";
                case "volcano": return "LAVAPLUME";
                case "graveyard": return "SHADOWBALL";
                case "sky": return "AIRSLASH";
                case "space": case "ultraspace": return "DRACOMETEOR";
            }
            return "TRIATTACK";
        }

        /// <summary>Camouflage's type for the environment.</summary>
        string EnvironmentType()
        {
            switch (Env)
            {
                case "grass": case "tallgrass": case "forestgrass": return "GRASS";
                case "forest": return "BUG";
                case "movingwater": case "stillwater": case "underwater": return "WATER";
                case "puddle": case "rock": case "sand": return "GROUND";
                case "cave": return "ROCK";
                case "snow": case "ice": return "ICE";
                case "volcano": return "FIRE";
                case "graveyard": return "GHOST";
                case "sky": return "FLYING";
                case "space": case "ultraspace": return "DRAGON";
            }
            return "NORMAL";
        }

        /// <summary>Secret Power's added effect for the environment (30% chance already rolled).</summary>
        void SecretPowerByEnvironment(Battler user, Battler target)
        {
            switch (Env)
            {
                case "grass": case "tallgrass": case "forest": case "forestgrass": if (CanStatus(target, "SLEEP", false, user)) Inflict(target, "SLEEP", false, user); return;
                case "movingwater": case "stillwater": ChangeStat(target, PokeCreator.Pkmn.Stat.Attack, -1, user); return;
                case "underwater": ChangeStat(target, PokeCreator.Pkmn.Stat.Defense, -1, user); return;
                case "puddle": case "sand": ChangeStat(target, 6, -1, user); return;
                case "cave": case "rock": if (!target.Pk.Fainted) target.Flinched = true; return;
                case "snow": case "ice": if (CanStatus(target, "FROZEN", false, user)) Inflict(target, "FROZEN", false, user); return;
                case "volcano": if (CanStatus(target, "BURN", false, user)) Inflict(target, "BURN", false, user); return;
                case "graveyard": case "sky": case "space": case "ultraspace": ChangeStat(target, Env == "sky" ? PokeCreator.Pkmn.Stat.Speed : PokeCreator.Pkmn.Stat.SpAtk, -1, user); return;
            }
            if (CanStatus(target, "PARALYSIS", false, user)) Inflict(target, "PARALYSIS", false, user);
        }
        /// <summary>Exp. All (key item EXPALL or Game Settings): Pokémon that didn't fight still get half the EXP.</summary>
        public bool ExpAll;
        static double InverseMod(TypeData td, string moveType)
        {
            if (td.Immunities.Contains(moveType) || td.Resistances.Contains(moveType)) return 2;
            if (td.Weaknesses.Contains(moveType)) return 0.5;
            return 1;
        }

        public double Effectiveness(string moveType, Pokemon target)
        {
            double m = 1;
            foreach (var t in target.Types(P).Distinct())
            {
                var td = P.Types.FirstOrDefault(x => x.Id == t);
                if (td == null) continue;
                if (Inverse) { m *= InverseMod(td, moveType); continue; }
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
            user.DestinyBond = false;
            user.ActedThisTurn = true;
            if (user.MustRecharge) { user.MustRecharge = false; Say(n + " must recharge!"); return; }
            if (HasAbility(user, "TRUANT"))
            {
                if (user.TruantSkip) { user.TruantSkip = false; Splash(user); Say(n + " is loafing around!"); return; }
                user.TruantSkip = true;
            }
            if (status == "SLEEP")
            {
                if (user.SleepTurns <= 0) user.SleepTurns = 1 + Rng.Next(3);
                user.SleepTurns -= HasAbility(user, "EARLYBIRD") ? 2 : 1;
                if (user.SleepTurns > 0 && !UsableWhileAsleep(user, slot)) { CommonAnim("Sleep", user); Say(n + " is fast asleep."); user.Charging = null; EndLock(user); return; }
                if (user.SleepTurns > 0) { CommonAnim("Sleep", user); Say(n + " is fast asleep."); }
                else {
                user.Pk.Status = ""; StatusEvent(user); Say(n + " woke up!");
                }
            }
            if (status == "FROZEN")
            {
                if (Rng.Next(5) != 0) { CommonAnim("Frozen", user); Say(n + " is frozen solid!"); return; }
                user.Pk.Status = ""; StatusEvent(user); Say(n + " thawed out!");
            }
            if (!Obeys(user, ref slot)) { user.Charging = null; return; }
            if (user.Flinched)
            {
                Say(n + " flinched and couldn't move!"); user.Charging = null;
                if (HasAbility(user, "STEADFAST")) { Splash(user); ChangeStat(user, PokeCreator.Pkmn.Stat.Speed, 1); }
                return;
            }
            if (user.Confusion > 0)
            {
                user.Confusion--;
                if (user.Confusion == 0) Say(n + " snapped out of its confusion!");
                else
                {
                    CommonAnim("Confusion", user);
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
            if (status == "PARALYSIS" && Rng.Next(4) == 0) { CommonAnim("Paralysis", user); Say(n + " is paralyzed! It can't move!"); user.Charging = null; EndLock(user); return; }
            if (user.AttractedTo != null)
            {
                if (!OnField.Contains(user.AttractedTo) || user.AttractedTo.Pk.Fainted) user.AttractedTo = null;
                else
                {
                    CommonAnim("Attract", user);
                    Say(n + " is in love with " + NameL(user.AttractedTo) + "!");
                    if (Rng.Next(2) == 0) { Say(n + " is immobilized by love!"); user.Charging = null; EndLock(user); return; }
                }
            }

            // Which move
            MoveData md; PokemonMove pm = null;
            if (user.Charging != null) { md = Move(user.Charging); }
            else if (!HasUsableMove(user)) { md = Move("STRUGGLE") ?? new MoveData { Id = "STRUGGLE", Name = "Struggle", Type = "NORMAL", Category = "Physical", Power = 50, FunctionCode = "Struggle" }; }
            else
            {
                int forced = ForcedSlot(user);
                if (forced >= 0) slot = forced;
                if (slot < 0 || slot >= user.Pk.Moves.Count || user.Pk.Moves[slot].PP <= 0) slot = user.Pk.Moves.FindIndex(m => m.PP > 0);
                pm = user.Pk.Moves[slot];
                md = Move(pm.Id);
                if (md == null) { Say(n + " used " + pm.Id + "!"); Say("But nothing happened!"); pm.PP--; return; }
            }
            string code = md.FunctionCode ?? "None";
            if (pm != null && user.Charging == null)
            {
                if (user.TauntTurns > 0 && md.Category == "Status") { Say(n + " can't use " + (md.Name ?? md.Id) + " after the taunt!"); return; }
                if (user.DisableTurns > 0 && md.Id == user.DisableMove) { Say(n + "'s " + (md.Name ?? md.Id) + " is disabled!"); return; }
                var restriction = MoreMoveRestriction(user, md);
                if (restriction != null) { Say(restriction); return; }
                if (Gravity > 0 && (code.Contains("InSky") && code.StartsWith("TwoTurn") || code == "StartUserAirborne" || md.Id == "SPLASH" || md.Id == "HIGHJUMPKICK" || md.Id == "JUMPKICK")) { Say(n + " can't use " + (md.Name ?? md.Id) + " because of gravity!"); return; }
            }
            bool continuing = user.LockedMove != null && user.LockedMove == md.Id;
            if (continuing) user.LockTurns--;
            if (md.Id != user.LastMoveUsed) { user.FuryCutterCount = 0; user.MetronomeCount = 0; if (!continuing) user.RolloutCount = 0; }
            else if (!user.LastMoveFailed) user.MetronomeCount++;
            user.LastMoveUsed = md.Id; if (md.Id != "STRUGGLE") _lastMoveInBattle = md.Id;
            if (code != "StartRaiseUserAtk1WhenDamaged") user.Rage = false;
            user.Grudge = false;
            user.LastMoveFailed = false;

            // Two-turn moves: charge first
            if (user.Charging == null && code.StartsWith("TwoTurnAttack") && !(code == "TwoTurnAttackOneTurnInSun" && EffWeather == Sun) && user.Pk.Item != "POWERHERB")
            {
                if (pm != null) pm.PP = Math.Max(0, pm.PP - 1);
                user.Charging = md.Id; user.ChargingSlot = slot;
                user.Invulnerable = code.Contains("Invulnerable");
                MoveAnim(user, target, md, 1, true);
                Say(n + ChargeText(md.Id));
                return;
            }
            bool wasCharging = user.Charging != null;
            user.Charging = null; user.Invulnerable = false;
            if (pm != null && !wasCharging && !continuing) pm.PP = Math.Max(0, pm.PP - 1);

            StanceChange(user, md);
            Say(n + " used " + (md.Name ?? md.Id) + "!");
            ArenaMoveUsed(user, md);
            ProteanBefore(user, md, code);
            if (code == "RaiseUserSpDef1PowerUpElectricMove") { user.Charged = true; Say(n + " began charging power!"); }
            _ev.Add(new BattleEvent { Kind = BattleEventKind.MoveUsed, Side = user.Side, Party = user.PartyIndex, Text = md.Id });
            bool selfTarget = md.Target == "User" || md.Target == "UserSide" || md.Target == "BothSides" || md.Target == "UserAndAllies" || md.Target == "UserOrNearAlly" || md.Target == "FoeSide" && md.Category == "Status" && code.StartsWith("Add");

            if (code == "ProtectUser" || code.StartsWith("ProtectUser"))
            {
                bool ok = Rng.NextDouble() < Math.Pow(1.0 / 3, user.ProtectedLast ? user.ProtectChain : 0);
                if (!ok) { user.ProtectChain = 0; Say("But it failed!"); return; }
                user.Protected = true; user.ProtectChain = user.ProtectedLast ? user.ProtectChain + 1 : 1;
                Say(n + " protected itself!");
                return;
            }
            if (selfTarget)
            {
                MoveAnim(user, md.Target == "FoeSide" ? (Live(1 - user.Side).FirstOrDefault() ?? user) : user, md);
                if (md.Category == "Status") StatusMove(user, target, md, code); else DamagingMove(user, target, md, code);
                if (!user.LastMoveFailed) DancerAfter(user, target, md);
                return;
            }
            _missedThisMove = false;
            if (HasAbility(user, "MYCELIUMMIGHT") && md.Category == "Status") _ignoreAbilityMove = true;
            ActOnTargets(user, target, md, code);
            if (!user.LastMoveFailed) DancerAfter(user, target, md);
            ItemsAfterMove(user, md, _missedThisMove);
            _ignoreAbilityMove = false;
            _lastMoveThisTurn = md.Id;
            if (user.LaserFocus > 0 && md.Category != "Status") user.LaserFocus = 0;
            // Outrage / Rollout: the lock ends when the turns run out or the move missed
            if (user.LockedMove != null && (user.LockTurns <= 0 || user.LastMoveFailed) && !user.Pk.Fainted)
            {
                bool confuse = code == "MultiTurnAttackConfuseUserAtEnd" && !user.LastMoveFailed;
                EndLock(user);
                if (confuse && user.Confusion == 0 && !HasAbility(user, "OWNTEMPO")) { Say(n + " became confused due to fatigue!"); user.Confusion = 2 + Rng.Next(4); }
            }
        }

        void ActOnTargets(Battler user, Battler target, MoveData md, string code)
        {
            // Who the move hits: one target, or every foe / every other Pokémon (double battles)
            var targets = new List<Battler>();
            string tg = md.Target ?? "NearOther";
            if (tg == "AllNearFoes") targets.AddRange(Live(1 - user.Side).Where(f => Near(f, user)));
            else if (tg == "AllFoes") targets.AddRange(Live(1 - user.Side));
            else if (tg == "AllNearOthers") { targets.AddRange(Live(1 - user.Side).Where(f => Near(f, user))); targets.AddRange(Allies(user)); }
            else if (tg == "AllBattlers") { targets.AddRange(Live(1 - user.Side)); targets.AddRange(Live(user.Side).Where(x => x != user)); }
            else if (tg == "RandomNearFoe") { var f = Live(1 - user.Side).Where(x => Near(x, user)).ToList(); if (f.Count > 0) targets.Add(f[Rng.Next(f.Count)]); }
            else if (tg == "NearAlly") { var al = Ally(user); if (al == null) { Say("But it failed!"); return; } targets.Add(al); }
            else if (tg == "AllAllies") { var als = Live(user.Side).Where(x => x != user).ToList(); if (als.Count == 0) { Say("But it failed!"); return; } targets.AddRange(als); }
            else
            {
                // Follow Me / Rage Powder pull single-target moves aimed at that side
                var center = target != null ? _centerOfAttention[target.Side] : null;
                if (center != null && center.Side != user.Side && !center.Pk.Fainted && OnField.Contains(center) && code != "CannotBeRedirected" && !HasAbility(user, "STALWART") && !HasAbility(user, "PROPELLERTAIL")) target = center;
                // Triple battles: single-target moves only reach Pokémon next to the user (unless they can hit any target)
                if (target != null && IsTriple && target.Side != user.Side && !Near(target, user) && tg != "Other" && tg != "Foe")
                    target = Live(target.Side).FirstOrDefault(f => Near(f, user)) ?? target;
                targets.Add(target);
            }
            targets.RemoveAll(t => t == null || t.Pk.Fainted);
            if (targets.Count == 0) { Say("But there was no target..."); return; }
            _spread = targets.Count > 1 ? 0.75 : 1;
            bool animShown = false;
            foreach (var t in targets)
            {
                if (user.Pk.Fainted || Outcome != BattleOutcome.Ongoing) break;
                if (t.Pk.Fainted) continue;
                double pierce = 1;
                if (t.Protected && md.Flags.Contains("CanProtect") && !code.StartsWith("RemoveProtections") && !code.StartsWith("HoopaRemoveProtections"))
                {
                    bool contactMove = md.Flags.Contains("Contact") && md.Category != "Status";
                    if (contactMove && HasAbility(user, "UNSEENFIST")) { }
                    else if (contactMove && HasAbility(user, "PIERCINGDRILL")) pierce = 0.25;
                    else { Say(Name(t) + " protected itself!"); continue; }
                }
                if (t.Invulnerable && md.Accuracy > 0 && !HitsInvulnerable(t, code) && user.LockOnTurns == 0) { Say(Name(user) + "'s attack missed!"); user.LastMoveFailed = true; ArenaMissed(user); continue; }
                if (t.SubstituteHP > 0 && md.Category == "Status" && t.Side != user.Side && !md.Flags.Contains("Sound") && !HasAbility(user, "INFILTRATOR")) { Say("But it failed!"); user.LastMoveFailed = true; continue; }
                if (AbilityAbsorbs(user, t, md, MoveType(user, md, code))) continue;
                if (Gen9Absorbs(user, t, md, MoveType(user, md, code), code)) continue;
                if (AbilityStopsMove(user, t, md, code)) { user.LastMoveFailed = true; continue; }
                if (!Hits(user, t, md, code)) { user.LastMoveFailed = true; _missedThisMove = true; ArenaMissed(user); Say(targets.Count > 1 ? Name(t) + " avoided the attack!" : Name(user) + "'s attack missed!"); _ev.Add(new BattleEvent { Kind = BattleEventKind.Miss, Side = t.Side, Party = t.PartyIndex }); continue; }
                if (!animShown) { MoveAnim(user, t, md); animShown = true; }
                if (MagicBounce(user, t, md, code)) continue;
                double spreadBefore = _spread; _spread *= pierce;
                if (md.Category == "Status") StatusMove(user, t, md, code);
                else DamagingMove(user, t, md, code);
                _spread = spreadBefore;
            }
            _spread = 1;
        }

        bool CommonAnimBefore(string name, Battler b) { CommonAnim(name, b); return true; }

        /// <summary>A move's animation; hit > 0 = an extra hit or a charging turn (Essentials' hit number), Flag = charging.</summary>
        void MoveAnim(Battler user, Battler target, MoveData md, int hit = 0, bool charging = false)
        {
            _ev.Add(new BattleEvent { Kind = BattleEventKind.MoveAnim, Side = user.Side, Party = user.PartyIndex, Slot = user.Slot, Value = target != null ? target.Side : user.Side, Value2 = target != null ? target.Slot : user.Slot, Text = hit > 0 ? md.Id + ":" + hit : md.Id, Flag = charging });
        }

        /// <summary>Shows one of the common animations (Poison, Burn, StatUp, Rain...) on a Pokémon, or on the field if b is null.</summary>
        void CommonAnim(string name, Battler b)
        {
            _ev.Add(new BattleEvent { Kind = BattleEventKind.CommonAnim, Side = b != null ? b.Side : -1, Party = b != null ? b.PartyIndex : 0, Slot = b != null ? b.Slot : 0, Text = name });
        }

        /// <summary>Damage multiplier for moves hitting more than one Pokémon (0.75).</summary>
        double _spread = 1;

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
            if (md.Accuracy <= 0 || user.LockOnTurns > 0) return true;
            bool always;
            double mod = AccuracyMod(user, target, md, code, out always);
            if (always) return true;
            int evade = TargetHas(user, target, "UNAWARE") || target.Foresight ? Math.Min(0, target.Stages[7]) : target.Stages[7];
            if ((HasAbility(user, "KEENEYE") || HasAbility(user, "MINDSEYE") || HasAbility(user, "ILLUMINATE")) && evade > 0) evade = 0;
            double acc = md.Accuracy * mod * AccMult(Math.Max(-6, Math.Min(6, user.Stages[6] - evade)));
            return Rng.Next(100) < acc;
        }

        // ------------------------------------------------------------------ damage
        int DamageFormula(Battler user, Battler target, int power, bool physical, string type, bool crit, double typeMod, bool stab, MoveData md = null, string code = "")
        {
            int atkStat = physical ? PokeCreator.Pkmn.Stat.Attack : PokeCreator.Pkmn.Stat.SpAtk;
            int defStat = physical ? PokeCreator.Pkmn.Stat.Defense : PokeCreator.Pkmn.Stat.SpDef;
            code = code ?? "";
            if (code == "UseTargetDefenseInsteadOfTargetSpDef") defStat = PokeCreator.Pkmn.Stat.Defense;
            if (WonderRoom > 0) defStat = defStat == PokeCreator.Pkmn.Stat.Defense ? PokeCreator.Pkmn.Stat.SpDef : PokeCreator.Pkmn.Stat.Defense;
            if (code == "UseUserDefenseInsteadOfUserAttack") atkStat = PokeCreator.Pkmn.Stat.Defense;
            // Unaware ignores the other side's stat stages
            bool ignoreAtk = md != null && TargetHas(user, target, "UNAWARE"), ignoreDef = md != null && (HasAbility(user, "UNAWARE") || code == "IgnoreTargetDefSpDefEvaStatStages");
            var atkSource = code == "UseTargetAttackInsteadOfUserAttack" ? target : user;
            int a = Stat(atkSource, atkStat, ignoreAtk, crit || ignoreAtk), d = Stat(target, defStat, crit || ignoreDef, ignoreDef);
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
            if (md != null) dmg = Math.Floor(dmg * FinalMod(user, target, type, typeMod, crit) * FieldDamageMod(user, target, md, type, crit) * MoreFinalMod(user, target, md, type, typeMod) * _spread);
            return Math.Max(typeMod > 0 ? 1 : 0, (int)dmg);
        }

        void DamagingMove(Battler user, Battler target, MoveData md, string code)
        {
            if (MoveFails(user, target, md, code)) { user.LastMoveFailed = true; Say("But it failed!"); return; }
            if (DampBlocks(user, code)) { user.LastMoveFailed = true; return; }
            if (PresentHeals(user, target, code)) return;
            if (code == "FirePledge" || code == "GrassPledge" || code == "WaterPledge") PledgeBefore(user, code);
            if (code == "HealAllyOrDamageFoe" && target.Side == user.Side && target != user)
            {
                int max = target.Pk.TotalHP(P);
                if (target.Pk.HP >= max || target.HealBlockTurns > 0) { Say("But it failed!"); return; }
                int f = target.Pk.HP; target.Pk.HP = Math.Min(max, target.Pk.HP + max / 2); Hp(target, f);
                Say(Name(target) + "'s HP was restored."); return;
            }
            _ignoreAbilityMove = code == "IgnoreTargetAbility" || code == "CategoryDependsOnHigherDamageIgnoreTargetAbility";
            int hpStart = target.Pk.HP;
            BeforeDamage(user, target, md, code);
            MoreBeforeDamage(user, target, md, code);
            string mtype = MoveType(user, md, code);
            if (PowderExplodes(user, mtype)) { user.LastMoveFailed = true; return; }
            if (PrimalStopsMove(mtype)) { user.LastMoveFailed = true; return; }
            double typeMod = md.Id == "STRUGGLE" ? 1 : mtype == "SHADOW" ? (target.Pk.ShadowPokemon ? 0.5 : 2) : EffectivenessOn(mtype, target);
            if (code == "EffectivenessIncludesFlyingType" && typeMod > 0) typeMod *= EffectivenessOn("FLYING", target);
            if (typeMod == 0 && mtype == "GROUND" && TypesOf(target).Contains("FLYING") && (IsGrounded(target) || code == "HitsTargetInSkyGroundsTarget"))
                typeMod = TypesOf(target).Where(t => t != "FLYING").Aggregate(1.0, (acc, t) => acc * TypeVs(mtype, t));
            if (typeMod > 0 && mtype == "GROUND" && target.AirborneTurns > 0 && Gravity == 0 && !target.Grounded) typeMod = 0;
            if (typeMod == 0 && target.Foresight && (mtype == "NORMAL" || mtype == "FIGHTING") && TypesOf(target).Contains("GHOST"))
                typeMod = TypesOf(target).Where(t => t != "GHOST").Aggregate(1.0, (acc, t) => acc * TypeVs(mtype, t));
            if (typeMod == 0 && HasAbility(user, "SCRAPPY") && (mtype == "NORMAL" || mtype == "FIGHTING") && TypesOf(target).Contains("GHOST"))
                typeMod = TypesOf(target).Where(t => t != "GHOST").Aggregate(1.0, (acc, t) => acc * TypeVs(mtype, t));
            if (typeMod == 0 && HasAbility(user, "MINDSEYE") && (mtype == "NORMAL" || mtype == "FIGHTING") && TypesOf(target).Contains("GHOST"))
                typeMod = TypesOf(target).Where(t => t != "GHOST").Aggregate(1.0, (acc, t) => acc * TypeVs(mtype, t));
            if (typeMod == 0 && Holds(target, "RINGTARGET"))
                typeMod = TypesOf(target).Aggregate(1.0, (acc, t) => { double v = TypeVs(mtype, t); return acc * (v == 0 ? 1 : v); });
            typeMod = TeraShell(user, target, StrongWindsMod(mtype, target, typeMod));
            if (typeMod == 0) { Say("It doesn't affect " + NameL(target) + "..."); return; }
            if (typeMod <= 1 && md.Id != "STRUGGLE" && TargetHas(user, target, "WONDERGUARD")) { Splash(target); Say("It doesn't affect " + NameL(target) + "..."); return; }
            if ((code == "OHKO" || code.StartsWith("OHKO")) && TargetHas(user, target, "STURDY")) { Splash(target); Say("It doesn't affect " + NameL(target) + "..."); return; }
            bool fullHp = target.Pk.HP >= target.Pk.TotalHP(P);
            if (code == "AttackTwoTurnsLater")
            {
                ScheduleDelayed(user, target, md, DamageFormula(user, target, md.Power, md.Category == "Physical", mtype, false, typeMod, TypesOf(user).Contains(mtype), md, code));
                return;
            }
            bool hitSub = false;
            UseGem(user, md, mtype, code);

            int hits = 1;
            if (code.StartsWith("HitTwoToFiveTimes")) { int r = Rng.Next(100); hits = HasAbility(user, "SKILLLINK") ? 5 : Holds(user, "LOADEDDICE") ? 4 + Rng.Next(2) : r < 35 ? 2 : r < 70 ? 3 : r < 85 ? 4 : 5; }
            else if (code.StartsWith("HitTwoTimes")) hits = 2;
            else if (code.StartsWith("HitThreeTimes")) hits = 3;
            bool parental = hits == 1 && HasAbility(user, "PARENTALBOND") && _spread == 1 && FixedDamage(user, target, md, code) < 0 && !code.StartsWith("TwoTurn") && !code.StartsWith("UserFaints");
            if (parental) hits = 2;
            List<Pokemon> beatUp = null;
            if (code == "HitOncePerUserTeamMember")
            {
                beatUp = Parties[user.Side].Where(p => !p.Fainted && !p.IsEgg && string.IsNullOrEmpty(p.Status)).ToList();
                if (!beatUp.Contains(user.Pk)) beatUp.Insert(0, user.Pk);
                hits = beatUp.Count;
            }
            int total = 0, done = 0;
            bool anyCrit = false;
            string enduredBy = null;
            for (int h = 0; h < hits && !target.Pk.Fainted && !user.Pk.Fainted; h++)
            {
                if (h > 0) MoveAnim(user, target, md, h);
                if (beatUp != null) _beatUpMember = beatUp[h];
                int dmg;
                bool crit = false;
                int fixedDmg = FixedDamage(user, target, md, code);
                if (fixedDmg >= 0) dmg = fixedDmg;
                else
                {
                    int sp = SpecialPower(user, target, md, code);
                    int power = sp >= 0 ? sp : md.Power <= 1 ? VariablePower(user, target, md, code) : md.Power;
                    power = Math.Max(1, (int)(power * SituationPower(user, target, md, code)));
                    if (code == "HitThreeTimesPowersUpWithEachHit") power = md.Power * (h + 1);
                    int stage = user.CritStage + (md.Flags.Contains("HighCriticalHitRate") ? 1 : 0) + (HasAbility(user, "SUPERLUCK") ? 1 : 0)
                              + (Holds(user, "SCOPELENS") || Holds(user, "RAZORCLAW") ? 1 : 0)
                              + ((Holds(user, "LEEK") || Holds(user, "STICK")) && (user.Pk.Species == "FARFETCHD" || user.Pk.Species == "SIRFETCHD") ? 2 : 0)
                              + (Holds(user, "LUCKYPUNCH") && user.Pk.Species == "CHANSEY" ? 2 : 0);
                    crit = code == "AlwaysCriticalHit" || stage >= 3 || Rng.Next(stage == 0 ? 24 : stage == 1 ? 8 : 2) == 0
                        || (HasAbility(user, "MERCILESS") && (target.Pk.Status ?? "").ToUpperInvariant() == "POISON") || user.LaserFocus > 0;
                    if (LuckyChant[target.Side] > 0) crit = false;
                    if (TargetHas(user, target, "BATTLEARMOR") || TargetHas(user, target, "SHELLARMOR")) crit = false;
                    bool physical = md.Category == "Physical";
                    if (code == "CategoryDependsOnHigherDamageIgnoreTargetAbility") physical = Stat(user, PokeCreator.Pkmn.Stat.Attack) > Stat(user, PokeCreator.Pkmn.Stat.SpAtk);
                    dmg = DamageFormula(user, target, power, physical, mtype, crit, typeMod, TypesOf(user).Contains(mtype), md, code);
                }
                if (parental && h == 1) dmg = Math.Max(1, dmg / 4);
                if (h == 0 && fixedDmg < 0 && dmg > 0 && (target.SubstituteHP == 0 || md.Flags.Contains("Sound") || HasAbility(user, "INFILTRATOR")))
                    dmg = Math.Max(1, (int)(dmg * ResistBerry(user, target, mtype, typeMod)));
                if (code == "CannotMakeTargetFaint" && dmg >= target.Pk.HP) dmg = target.Pk.HP - 1;
                // Disguise takes the first hit
                if (dmg > 0 && !target.DisguiseBroken && TargetHas(user, target, "DISGUISE") && target != user)
                {
                    target.DisguiseBroken = true; done++;
                    Splash(target); Say("Its disguise served it as a decoy!");
                    int fd = target.Pk.HP; target.Pk.HP = Math.Max(1, target.Pk.HP - target.Pk.TotalHP(P) / 8); Hp(target, fd);
                    Say(Name(target) + "'s disguise was busted!");
                    if (target.Pk.Species == "MIMIKYU") ChangeForm(target, 1, null);
                    continue;
                }
                // A substitute takes the hit instead
                if (target.SubstituteHP > 0 && target != user && !md.Flags.Contains("Sound") && !HasAbility(user, "INFILTRATOR"))
                {
                    int taken = Math.Min(target.SubstituteHP, dmg);
                    target.SubstituteHP -= taken; total += taken; done++; hitSub = true;
                    if (crit) Say("A critical hit!");
                    Say("The substitute took damage for " + NameL(target) + "!");
                    if (target.SubstituteHP <= 0) { target.SubstituteHP = 0; Say(Name(target) + "'s substitute faded!"); break; }
                    continue;
                }
                // Sturdy / Focus Sash: survive a hit from full HP with 1 HP
                bool endured = false;
                if (dmg >= target.Pk.HP && target.Pk.HP >= target.Pk.TotalHP(P) && fullHp && h == 0 && target.Pk.HP > 1)
                {
                    if (TargetHas(user, target, "STURDY")) { dmg = target.Pk.HP - 1; endured = true; }
                    else if (target.Pk.Item == "FOCUSSASH") { dmg = target.Pk.HP - 1; endured = true; }
                }
                bool enduredMove = false;
                if (!endured && dmg >= target.Pk.HP && target.Endure && target.Pk.HP > 0) { dmg = target.Pk.HP - 1; enduredMove = true; }
                if (!endured && !enduredMove && dmg >= target.Pk.HP && target.Pk.HP > 1 && Holds(target, "FOCUSBAND") && Rng.Next(10) == 0) { dmg = target.Pk.HP - 1; endured = true; enduredBy = "FOCUSBAND"; }
                int from = target.Pk.HP;
                target.Pk.HP = Math.Max(0, target.Pk.HP - dmg);
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Hit, Side = target.Side, Party = target.PartyIndex, Flag = crit, Value = (int)(typeMod * 100) });
                Hp(target, from);
                total += from - target.Pk.HP; done++;
                if (from > target.Pk.HP) { target.TookDamageThisTurn = true; target.LastDamageTaken = from - target.Pk.HP; target.LastDamagePhysical = md.Category == "Physical"; target.LastHitType = mtype; }
                if (target.Pk.Fainted) _faintedThisTurn[target.Side] = true;
                if (enduredMove) enduredBy = "ENDURE";
                if (crit) { Say("A critical hit!"); anyCrit = true; }
                if (endured && enduredBy != "FOCUSBAND") enduredBy = TargetHas(user, target, "STURDY") ? "STURDY" : "FOCUSSASH";
            }
            if (FixedDamage(user, target, md, code) < 0)
            {
                if (typeMod > 1) Say("It's super effective!");
                else if (typeMod < 1) Say("It's not very effective...");
            }
            if (enduredBy == "STURDY") { Splash(target); Say(Name(target) + " endured the hit!"); }
            else if (enduredBy == "ENDURE") Say(Name(target) + " endured the hit!");
            else if (enduredBy == "FOCUSSASH") { Say(Name(target) + " hung on using its Focus Sash!"); UseUpItem(target); }
            else if (enduredBy == "FOCUSBAND") Say(Name(target) + " hung on using its Focus Band!");
            _gemBoost = 1;
            if (code == "OHKO" || code.StartsWith("OHKO")) Say("It's a one-hit KO!");
            if (hits > 1) Say("The Pokémon was hit " + done + " time" + (done == 1 ? "" : "s") + "!");

            // After-effects
            if (hitSub)
            {
                if (code.StartsWith("Recoil") && total > 0 && !HasAbility(user, "ROCKHEAD") && !HasAbility(user, "MAGICGUARD")) { int f = user.Pk.HP; user.Pk.HP = Math.Max(0, user.Pk.HP - Math.Max(1, total / 4)); Hp(user, f); Say(Name(user) + " is damaged by recoil!"); }
                DamagingAfterEffects(user, target, md, code == "SwitchOutUserDamagingMove" || code.StartsWith("MultiTurnAttack") || code == "PowerHigherWithConsecutiveUse" ? code : "None", total);
                return;
            }
            if (total > 0) ContactAbilities(user, target, md);
            if (total > 0) DamagedAbilities(user, target, md, mtype, anyCrit, hpStart);
            if (total > 0) Gen9Damaged(user, target, md, mtype, hpStart, md.Flags.Contains("Contact") && !HasAbility(user, "LONGREACH") && !Holds(user, "PROTECTIVEPADS") && !Holds(user, "PUNCHINGGLOVE"));
            if (target.Pk.Fainted && hpStart > 0) SoulHeart();
            ItemsAfterHit(user, target, md, mtype, typeMod, total, md.Flags.Contains("Contact") && !HasAbility(user, "LONGREACH") && !Holds(user, "PROTECTIVEPADS") && !Holds(user, "PUNCHINGGLOVE"));
            DamagingAfterEffects(user, target, md, code, total);
            if ((code.StartsWith("Recoil") && !HasAbility(user, "ROCKHEAD") && !HasAbility(user, "MAGICGUARD")) || md.Id == "STRUGGLE")
            {
                int recoil = md.Id == "STRUGGLE" ? Math.Max(1, user.Pk.TotalHP(P) / 4) : code.StartsWith("RecoilHalf") ? total / 2 : code.StartsWith("RecoilThird") ? total / 3 : total / 4;
                if (recoil > 0 && !user.Pk.Fainted) { int f = user.Pk.HP; user.Pk.HP = Math.Max(0, user.Pk.HP - Math.Max(1, recoil)); Hp(user, f); Say(Name(user) + " is damaged by recoil!"); }
            }
            if (code.StartsWith("HealUserBy") && total > 0 && !user.Pk.Fainted)
            {
                int heal = code.Contains("ThreeQuarters") ? total * 3 / 4 : Math.Max(1, total / 2);
                if (Holds(user, "BIGROOT")) heal = heal * 13 / 10;
                if (TargetHas(user, target, "LIQUIDOOZE")) { Splash(target); Residual(user, heal, Name(user) + " sucked up the liquid ooze!"); heal = 0; }
                int f = user.Pk.HP; user.Pk.HP = Math.Min(user.Pk.TotalHP(P), user.Pk.HP + heal);
                if (user.Pk.HP > f) { Hp(user, f); Say(Name(target) + " had its energy drained!"); }
            }
            if (code == "UserFaintsExplosive" || code.StartsWith("UserFaints")) { int f = user.Pk.HP; user.Pk.HP = 0; Hp(user, f); }
            if (code == "AttackAndSkipNextTurn") user.MustRecharge = true;
            if (code.StartsWith("BindTarget") && !target.Pk.Fainted && target.BindTurns == 0) { target.BindTurns = Holds(user, "GRIPCLAW") ? 7 : 4 + Rng.Next(2); target.BindBand = Holds(user, "BINDINGBAND"); Say(Name(target) + " was squeezed by " + NameL(user) + "!"); }
            if (code == "RaiseUserAttack3IfTargetFaints" && target.Pk.Fainted) ChangeStat(user, PokeCreator.Pkmn.Stat.Attack, 3);
            // Secondary effect: status / stat change / flinch, at the effect chance (0 = always)
            if ((total > 0 || FixedDamage(user, target, md, code) >= 0) && !(HasAbility(user, "SHEERFORCE") && md.EffectChance > 0))
            {
                int chance = md.EffectChance > 0 ? md.EffectChance : 100;
                if (HasAbility(user, "SERENEGRACE")) chance *= 2;
                if (Rainbow[user.Side] > 0) chance *= 2;
                if (Rng.Next(100) < chance) SecondaryEffect(user, target, md, code);
            }
            if (total > 0 && user.Pk.Item == "LIFEORB" && !user.Pk.Fainted && !HasAbility(user, "MAGICGUARD") && !(HasAbility(user, "SHEERFORCE") && md.EffectChance > 0))
                Residual(user, Math.Max(1, user.Pk.TotalHP(P) / 10), Name(user) + " lost some of its HP!");
            if (target.Pk.Fainted) OnKnockOut(user);
            if (target.Pk.Fainted && target.DestinyBond && target.Side != user.Side && !user.Pk.Fainted)
            {
                Say(Name(target) + " took its attacker down with it!");
                int f = user.Pk.HP; user.Pk.HP = 0; Hp(user, f);
            }
        }

        /// <summary>Damaging moves that fail before they hit (Sucker Punch, Fake Out, Focus Punch, Counter...).</summary>
        bool MoveFails(Battler user, Battler target, MoveData md, string code)
        {
            switch (code)
            {
                case "FailsIfTargetActed":
                    {
                        var planned = target.PlannedMove != null ? Move(target.PlannedMove) : null;
                        return target.ActedThisTurn || planned == null || planned.Category == "Status";
                    }
                case "FailsIfNotUserFirstTurn": return user.TurnsOut > 0;
                case "FailsIfUserDamagedThisTurn": return user.TookDamageThisTurn;
                case "FailsIfTargetHasNoItem": return string.IsNullOrEmpty(target.Pk.Item);
                case "FailsUnlessTargetSharesTypeWithUser": return !TypesOf(user).Intersect(TypesOf(target)).Any();
                case "FailsIfUserHasUnusedMove": return user.Pk.Moves.Count < 2;
                case "CounterPhysicalDamage": return !(user.TookDamageThisTurn && user.LastDamagePhysical);
                case "CounterSpecialDamage": return !(user.TookDamageThisTurn && !user.LastDamagePhysical);
                case "CounterDamagePlusHalf": return !user.TookDamageThisTurn;
                case "LowerTargetHPToUserHP": return target.Pk.HP <= user.Pk.HP;
                case "PowerDependsOnUserStockpile": return user.StockpileCount == 0;
            }
            return MoreMoveFails(user, target, md, code);
        }

        /// <summary>The move slot a Pokémon must use: Outrage / Rollout lock, then Encore.</summary>
        int ForcedSlot(Battler b)
        {
            if (b.LockedMove != null)
            {
                int i = b.Pk.Moves.FindIndex(m => m.Id == b.LockedMove);
                if (i >= 0) return i;
                EndLock(b);
            }
            if (b.EncoreTurns > 0)
            {
                int i = b.Pk.Moves.FindIndex(m => m.Id == b.EncoreMove && m.PP > 0);
                if (i >= 0) return i;
                b.EncoreTurns = 0; b.EncoreMove = null;
            }
            return -1;
        }

        /// <summary>Sleep Talk and Snore work while asleep.</summary>
        bool UsableWhileAsleep(Battler b, int slot)
        {
            if (slot < 0 || slot >= b.Pk.Moves.Count || b.Charging != null) return false;
            var md = Move(b.Pk.Moves[slot].Id);
            var c = md != null ? md.FunctionCode ?? "" : "";
            return c == "UseRandomUserMoveIfAsleep" || c == "FlinchTargetFailsIfUserNotAsleep";
        }

        void EndLock(Battler b) { b.LockedMove = null; b.LockTurns = 0; b.RolloutCount = 0; }

        /// <summary>Whether a move can be picked now (Taunt, Disable, Encore, Outrage).</summary>
        public bool CanSelect(Battler b, int slot)
        {
            if (slot < 0 || slot >= b.Pk.Moves.Count) return false;
            int forced = ForcedSlot(b);
            if (forced >= 0) return slot == forced;
            var md = Move(b.Pk.Moves[slot].Id);
            if (md == null) return true;
            if (b.TauntTurns > 0 && md.Category == "Status") return false;
            if (b.DisableTurns > 0 && md.Id == b.DisableMove) return false;
            if (MoreMoveRestriction(b, md) != null) return false;
            return true;
        }

        /// <summary>Why a move can't be picked, or null.</summary>
        public string CannotSelectReason(Battler b, int slot)
        {
            if (CanSelect(b, slot)) return null;
            var md = Move(b.Pk.Moves[slot].Id);
            string mn = md != null ? md.Name ?? md.Id : b.Pk.Moves[slot].Id;
            if (b.LockedMove != null || b.EncoreTurns > 0) return Name(b) + " must use " + MoveNameOf(b.LockedMove ?? b.EncoreMove) + "!";
            var more = MoreMoveRestriction(b, md);
            if (more != null) return more;
            if (b.TauntTurns > 0) return Name(b) + " can't use " + mn + " after the taunt!";
            return Name(b) + "'s " + mn + " is disabled!";
        }

        public bool IsTrapped(Battler b)
        {
            if (TypesOf(b).Contains("GHOST") || b.Pk.Item == "SHEDSHELL") return false;
            return b.Trapped || b.Ingrain || b.BindTurns > 0 || FairyLock > 0;
        }

        int FixedDamage(Battler user, Battler target, MoveData md, string code)
        {
            var m = Regex.Match(code, @"^FixedDamage(\d+)$");
            if (m.Success) return int.Parse(m.Groups[1].Value);
            if (code == "FixedDamageUserLevel") return user.Pk.Level;
            if (code == "FixedDamageHalfTargetHP") return Math.Max(1, target.Pk.HP / 2);
            if (code == "FixedDamageUserLevelRandom") return Math.Max(1, user.Pk.Level * (50 + Rng.Next(101)) / 100);
            if (code == "OHKO" || code.StartsWith("OHKO")) return target.Pk.HP;
            if (code == "CounterPhysicalDamage" || code == "CounterSpecialDamage") return Math.Max(1, user.LastDamageTaken * 2);
            if (code == "CounterDamagePlusHalf") return Math.Max(1, user.LastDamageTaken * 3 / 2);
            if (code == "LowerTargetHPToUserHP") return Math.Max(0, target.Pk.HP - user.Pk.HP);
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
            if (MirrorArmor(b, stat, amount, source)) return false;
            if (amount < 0 && source != null && source.Side != b.Side && Holds(b, "CLEARAMULET"))
            {
                Say("The effects of " + NameL(b) + "'s Clear Amulet prevent its stats from being lowered!");
                return false;
            }
            if (amount < 0 && source != null && source.Side != b.Side && Mist[b.Side] > 0 && !HasAbility(source, "INFILTRATOR"))
            {
                Say(Name(b) + " is protected by the mist!");
                return false;
            }
            if (amount < 0 && source != null && source.Side != b.Side && b.SubstituteHP > 0 && source != b) { Say("But it failed!"); return false; }
            if (HasAbility(b, "SIMPLE")) amount *= 2;
            if (HasAbility(b, "CONTRARY")) amount = -amount;
            int old = b.Stages[stat];
            int nv = Math.Max(-6, Math.Min(6, old + amount));
            string who = Name(b) + "'s " + StatName(stat);
            if (nv == old) { Say(who + (amount > 0 ? " won't go any higher!" : " won't go any lower!")); return false; }
            b.Stages[stat] = nv;
            if (nv < old) b.StatsLoweredThisTurn = true;
            if (nv > old) CopyBoosts(b, stat, nv - old);
            int d = Math.Abs(nv - old);
            _ev.Add(new BattleEvent { Kind = BattleEventKind.StatChange, Side = b.Side, Party = b.PartyIndex, Value = amount });
            Say(who + (amount > 0 ? (d >= 3 ? " rose drastically!" : d == 2 ? " rose sharply!" : " rose!") : (d >= 3 ? " severely fell!" : d == 2 ? " harshly fell!" : " fell!")));
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
            var types = TypesOf(b);
            string have = (b.Pk.Status ?? "").ToUpperInvariant();
            if (have.Length > 0) { if (showFail) Say(have == status ? Name(b) + AlreadyText(status) : "But it failed!"); return false; }
            bool immune = (status == "POISON" || status == "TOXIC") && (types.Contains("POISON") || types.Contains("STEEL")) && !(source != null && HasAbility(source, "CORROSION"))
                       || status == "BURN" && types.Contains("FIRE")
                       || status == "PARALYSIS" && types.Contains("ELECTRIC")
                       || status == "FROZEN" && types.Contains("ICE");
            if (immune) { if (showFail) Say("It doesn't affect " + NameL(b) + "..."); return false; }
            if (source != null && source != b && source.Side != b.Side && Safeguard[b.Side] > 0 && !HasAbility(source, "INFILTRATOR"))
            { if (showFail) Say(Name(b) + " is protected by Safeguard!"); return false; }
            if (IsGrounded(b) && (Terrain == "Misty" || Terrain == "Electric" && status == "SLEEP"))
            { if (showFail) Say(Name(b) + (Terrain == "Misty" ? " surrounds itself with a protective mist!" : " is protected by the Electric Terrain!")); return false; }
            if (source != null && source != b && source.Side != b.Side && b.SubstituteHP > 0) { if (showFail) Say("But it failed!"); return false; }
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
            SynchronizeStatus(b, status, source);
            if ((status == "POISON" || status == "TOXIC") && source != null && source != b && HasAbility(source, "POISONPUPPETEER") && !b.Pk.Fainted) { Splash(source); Confuse(b, false); }
            return true;
        }

        bool Confuse(Battler b, bool showFail)
        {
            if (b.Pk.Fainted) return false;
            if (b.Confusion > 0) { if (showFail) Say(Name(b) + " is already confused."); return false; }
            if (HasAbility(b, "OWNTEMPO")) { if (showFail) { Splash(b); Say(Name(b) + " doesn't become confused!"); } return false; }
            b.Confusion = 2 + Rng.Next(4);
            CommonAnim("Confusion", b);
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
            if (SpecialStatusMove(user, target, md, code)) return;
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
            if (code == "ResetAllBattlersStatStages") { foreach (var b in OnField) b.Stages = new int[8]; Say("All stat changes were eliminated!"); return; }
            if (code == "SwitchOutTargetStatusMove" && !(IsWild && target.Side == 1 && !IsDouble))
            {
                if (target.Ingrain || HasAbility(target, "SUCTIONCUPS") || (target.Side == 1 ? FoeBench(target.Slot) : Bench(target.Side)).Count == 0) { Say("But it failed!"); return; }
                ForceSwitch(target);
                return;
            }
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
            foreach (var b in OnField.Where(x => !x.Pk.Fainted).OrderByDescending(x => Speed(x)).ToList())
            {
                if (b.Pk.Fainted) continue;
                EndOfTurnAbilities(b);
                MoreEndOfTurnAbilities(b);
                Gen9EndOfTurn(b);
                if (b.Pk.Fainted) continue;
                int max = b.Pk.TotalHP(P);
                string st = (b.Pk.Status ?? "").ToUpperInvariant();
                if (st == "BURN" && !HasAbility(b, "MAGICGUARD")) CommonAnim("Burn", b);
                else if (st == "POISON" && !HasAbility(b, "MAGICGUARD") && !HasAbility(b, "POISONHEAL")) CommonAnim(b.ToxicCount > 0 ? "Toxic" : "Poison", b);
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
                    else if (CommonAnimBefore("Bind", b)) Residual(b, Math.Max(1, max / (b.BindBand ? 6 : 8)), Name(b) + " is hurt by the squeeze!");
                }
                if (!b.Pk.Fainted && Holds(b, "LEFTOVERS") && b.Pk.HP < max)
                {
                    int f = b.Pk.HP; b.Pk.HP = Math.Min(max, b.Pk.HP + Math.Max(1, max / 16)); Hp(b, f);
                    Say(Name(b) + " restored a little HP using its Leftovers!");
                }
                ItemsEndOfTurn(b);
                ItemAbilitiesEndOfTurn(b);
                CheckHeldItems(b);
            }
            MoveEffectsEndOfTurn();
            CheckAllItems();
        }

        void Residual(Battler b, int dmg, string text)
        {
            if (HasAbility(b, "MAGICGUARD")) return; // no indirect damage
            int f = b.Pk.HP; b.Pk.HP = Math.Max(0, b.Pk.HP - dmg); Hp(b, f); Say(text);
        }

        // ------------------------------------------------------------------ fainting, EXP, next Pokémon
        void AfterFaints()
        {
            // Opponents that fainted: EXP, then the trainer sends out another (or the slot stays empty)
            for (int slot = 0; slot < SlotsPerSide; slot++)
            {
                var foe = Field[1][slot];
                if (foe == null || !foe.Pk.Fainted || foe.FaintShown || Outcome != BattleOutcome.Ongoing) continue;
                foe.FaintShown = true;
                Say(Name(foe) + " fainted!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Faint, Side = 1, Party = foe.PartyIndex, Slot = slot });
                GiveExp(foe);
            }
            if (Outcome == BattleOutcome.Ongoing)
            {
                if (!AnyAble(1))
                {
                    if (Field[1].Take(SlotsPerSide).Any(b => b != null && b.Pk.Fainted))
                    {
                        Outcome = BattleOutcome.Win;
                        if (FlipNames) Say(PlayerName + " is out of usable Pokémon!");
                        else if (!IsWild) Say("You defeated " + AllTrainers.Trim() + "!");
                    }
                }
                else
                    for (int slot = 0; slot < SlotsPerSide; slot++)
                    {
                        var foe = Field[1][slot];
                        if (foe == null || !foe.Pk.Fainted) continue;
                        if (!IsWild && FoeBench(slot).Count > 0)
                        {
                            int next = AINextPokemon(slot);
                            // Switch style (single battles): hold the send-out so the player can change Pokémon first
                            if (SwitchStyle && !IsDouble && Live(0).Count > 0 && Bench(0).Count > 0)
                            {
                                _pendingFoe = next; _pendingFoeSlot = slot;
                                continue;
                            }
                            var nb = Place(1, slot, next);
                            Say(FlipNames ? "Go! " + nb.Pk.Name(P) + "!" : TrainerLabel(next) + " sent out " + nb.Pk.Name(P) + "!");
                            SendOutEvent(nb);
                            MarkParticipant();
                            SwitchInAbility(nb);
                        }
                        else Field[1][slot] = null;
                    }
            }
            // The player's Pokémon that fainted: the player picks replacements (PlayerSlotsToFill)
            if (Outcome != BattleOutcome.Escaped && Outcome != BattleOutcome.Caught)
            {
                bool anyFainted = false;
                for (int slot = 0; slot < SlotsPerSide; slot++)
                {
                    var me = Field[0][slot];
                    if (me == null || !me.Pk.Fainted || me.FaintShown) continue;
                    me.FaintShown = true; anyFainted = true;
                    // Friendship drops (a lot if the foe was 30+ levels higher)
                    int foeLevel = Field[1].Where(f => f != null).Select(f => f.Pk.Level).DefaultIfEmpty(0).Max();
                    me.Pk.ChangeHappiness(foeLevel >= me.Pk.Level + 30 ? "faintbad" : "faint");
                    Say(Name(me) + " fainted!");
                    _ev.Add(new BattleEvent { Kind = BattleEventKind.Faint, Side = 0, Party = me.PartyIndex, Slot = slot });
                }
                if (!AnyAble(0))
                {
                    if (anyFainted)
                    {
                        if (Outcome != BattleOutcome.Win) Outcome = BattleOutcome.Loss;
                        Say(FlipNames ? "You defeated " + TrainerName + "!" : PlayerName + " is out of usable Pokémon!");
                    }
                }
                else if (Outcome == BattleOutcome.Ongoing)
                {
                    int bench = Bench(0).Count;
                    for (int slot = 0; slot < SlotsPerSide; slot++)
                    {
                        var me = Field[0][slot];
                        if (me == null || !me.Pk.Fainted || PlayerSlotsToFill.Contains(slot)) continue;
                        if (IsPartner(me)) { PartnerReplace(me); continue; }
                        if (bench > 0) { PlayerSlotsToFill.Add(slot); bench--; }
                        else Field[0][slot] = null;
                    }
                }
            }
        }

        int AINextPokemon(int slot = 0)
        {
            var allowed = FoeBench(slot);
            var me = Live(0).FirstOrDefault() ?? Field[0].FirstOrDefault(x => x != null);
            int best = -1; double bestScore = -1;
            for (int i = 0; i < Parties[1].Count; i++)
            {
                if (!allowed.Contains(i)) continue;
                double score = 1;
                if (TrainerSkill > 0 && me != null) foreach (var t in Parties[1][i].Types(P)) score *= 1 / Math.Max(0.25, Effectiveness(t, me.Pk) == 0 ? 0.25 : Effectiveness(t, me.Pk));
                if (best < 0 || score > bestScore + 0.01) { best = i; bestScore = score; }
                if (TrainerSkill <= 0) break; // simple trainers send their next Pokémon in order
            }
            return best;
        }

        /// <summary>EXP for every Pokémon that fought the fainted foe (Gen 5 scaled formula), plus EVs.</summary>
        void GiveExp(Battler foe)
        {
            HashSet<int> set;
            if (NoExp) return;
            if (!_participants.TryGetValue(foe.PartyIndex, out set)) set = new HashSet<int>();
            var who = set.Where(i => i < Parties[0].Count && Able(Parties[0][i]) && !IsPartnerIndex(i)).ToList();
            // Exp. Share holders split half of the EXP (Essentials' rules); Exp. All gives everyone else half
            var share = Enumerable.Range(0, Parties[0].Count).Where(i => Able(Parties[0][i]) && !IsPartnerIndex(i) && Parties[0][i].Item == "EXPSHARE").ToList();
            var gainers = who.Union(share).ToList();
            if (ExpAll) gainers = gainers.Union(Enumerable.Range(0, Parties[0].Count).Where(i => Able(Parties[0][i]) && !IsPartnerIndex(i))).ToList();
            if (gainers.Count == 0) return;
            var fd = foe.Pk.Data(P);
            int baseExp = fd != null && fd.BaseExp > 0 ? fd.BaseExp : 50;
            int lf = foe.Pk.Level;
            foreach (int i in gainers.OrderBy(x => x))
            {
                var pk = Parties[0][i];
                if (pk.Level >= GrowthRates.MaxLevel) continue;
                int lp = pk.Level;
                bool fought = who.Contains(i), holds = share.Contains(i);
                double part;
                if (share.Count > 0 && (fought || holds))
                {
                    if (who.Count == 0) part = 1.0 / share.Count;
                    else if (fought) part = 0.5 / who.Count + (holds ? 0.5 / share.Count : 0);
                    else part = 0.5 / share.Count;
                }
                else if (fought) part = 1.0 / who.Count;
                else part = 0.5; // Exp. All
                double exp = baseExp * lf / 5.0 * part;
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
                        int gain = ev.Value * (pk.Pokerus != 0 ? 2 : 1);
                        int add = Math.Min(gain, Math.Min(ItemEffects.MaxEV - pk.EV[s], ItemEffects.MaxTotalEV - pk.EV.Sum()));
                        if (add > 0) pk.EV[s] += add;
                    }
                if (pk.ShadowPokemon) { pk.SavedExp += amount; continue; } // Shadow Pokémon store their EXP until purified
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
        /// <summary>Sends out a Pokémon in place of one that fainted (the first slot waiting, or the given slot).</summary>
        public List<BattleEvent> SwitchAfterFaint(int party) { return SwitchAfterFaint(PlayerSlotsToFill.Count > 0 ? PlayerSlotsToFill[0] : 0, party); }

        public List<BattleEvent> SwitchAfterFaint(int slot, int party)
        {
            if (Recording != null) Recording.Add(new List<int> { 1, slot, party });
            if (SwitchAfterFaintOverride != null) return SwitchAfterFaintOverride(slot, party);
            _ev = new List<BattleEvent>();
            PlayerSlotsToFill.Remove(slot);
            var old = Field[0][slot];
            if (old != null && !old.Pk.Fainted)
            {
                SwitchOutAbility(old);
                Say(old.Pk.Name(P) + ", come back!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Withdraw, Side = 0, Party = old.PartyIndex, Slot = slot });
            }
            var b = Place(0, slot, party);
            Say(FlipNames ? TrainerName + " sent out " + b.Pk.Name(P) + "!" : "Go! " + b.Pk.Name(P) + "!");
            SendOutEvent(b);
            MarkParticipant();
            SwitchInAbility(b);
            return _ev;
        }

        void SwitchPlayer(int slot, int party)
        {
            var old = Field[0][slot];
            if (old != null)
            {
                SwitchOutAbility(old);
                Say(FlipNames ? TrainerName + " withdrew " + old.Pk.Name(P) + "!" : old.Pk.Name(P) + ", come back!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Withdraw, Side = 0, Party = old.PartyIndex, Slot = slot });
            }
            var b = Place(0, slot, party);
            b.SwitchedIn = true;
            Say(FlipNames ? TrainerName + " sent out " + b.Pk.Name(P) + "!" : "Go! " + b.Pk.Name(P) + "!");
            SendOutEvent(b);
            MarkParticipant();
            SwitchInAbility(b);
        }

        bool TryRun()
        {
            if (!IsWild) { Say("No! There's no running from a Trainer battle!"); return false; }
            if (CannotRun) { Say("You can't escape!"); return false; }
            RunAttempts++;
            var me = Live(0).FirstOrDefault() ?? Field[0][0]; var foe = Live(1).FirstOrDefault() ?? Field[1][0];
            bool ghost = TypesOf(me).Contains("GHOST");
            if (HasAbility(me, "RUNAWAY") || me.Pk.Item == "SMOKEBALL" || ghost)
            {
                Outcome = BattleOutcome.Escaped;
                Say("You got away safely!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Flee });
                return true;
            }
            bool grounded = !TypesOf(me).Contains("FLYING") && !HasAbility(me, "LEVITATE") && me.Pk.Item != "AIRBALLOON";
            if ((HasAbility(foe, "SHADOWTAG") && !HasAbility(me, "SHADOWTAG")) || (HasAbility(foe, "ARENATRAP") && grounded) || (HasAbility(foe, "MAGNETPULL") && TypesOf(me).Contains("STEEL")) || me.BindTurns > 0 || me.Trapped || me.Ingrain || FairyLock > 0)
            {
                Say("You can't escape!");
                return false;
            }
            int a = Live(0).Select(x => Speed(x)).DefaultIfEmpty(1).Max(), b = Math.Max(1, Live(1).Select(x => Speed(x)).DefaultIfEmpty(1).Max());
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

        /// <summary>
        /// X items, Dire Hit, Guard Spec., flutes and Poké Doll-style items used from the Bag in battle
        /// (Essentials' BattleUse OnBattler / Direct). Returns false if the item isn't one of them.
        /// </summary>
        public static bool IsBattlerItem(string id)
        {
            return XItemStat(id) >= 0 || id == "DIREHIT" || id == "DIREHIT2" || id == "DIREHIT3" || id == "GUARDSPEC" || id == "YELLOWFLUTE" || id == "REDFLUTE"
                || id == "POKEDOLL" || id == "FLUFFYTAIL" || id == "POKETOY";
        }
        /// <summary>Items used on the whole side or the battle, not on a chosen Pokémon.</summary>
        public static bool IsDirectBattleItem(string id) { return id == "GUARDSPEC" || id == "POKEDOLL" || id == "FLUFFYTAIL" || id == "POKETOY"; }

        static int XItemStat(string id)
        {
            string b = id.TrimEnd('2', '3', '6');
            switch (b)
            {
                case "XATTACK": return 1; case "XDEFENSE": case "XDEFEND": return 2; case "XSPEED": return 3;
                case "XSPATK": case "XSPECIAL": return 4; case "XSPDEF": return 5; case "XACCURACY": return 6;
            }
            return -1;
        }

        void UseItem(string itemId, int party, int moveSlot = -1, Battler user = null)
        {
            var item = P.Items.FirstOrDefault(i => i.Id == itemId);
            if (item == null) return;
            if (IsBattlerItem(itemId)) { UseBattlerItem(item, party, user); return; }
            var pk = Parties[0][party];
            var info = ItemEffects.Info(P, item);
            int from = pk.HP;
            var o = ItemEffects.Use(P, pk, item, info, moveSlot, new EvolutionContext());
            Say(PlayerName + " used the " + (item.Name ?? item.Id) + ((item.Name ?? item.Id).EndsWith(".") ? "" : "."));
            var onField = Field[0].Take(SlotsPerSide).FirstOrDefault(x => x != null && x.PartyIndex == party);
            if (onField != null && pk.HP != from) Hp(onField, from);
            if (onField != null) StatusEvent(onField);
            Say(o.Message);
        }

        void UseBattlerItem(ItemData item, int party, Battler user)
        {
            string id = item.Id, iname = item.Name ?? id;
            Say(PlayerName + " used the " + iname + (iname.EndsWith(".") ? "" : "."));
            if (id == "POKEDOLL" || id == "FLUFFYTAIL" || id == "POKETOY")
            {
                if (!IsWild || CannotRun) { Say("But it had no effect!"); return; }
                Outcome = BattleOutcome.Escaped;
                Say("You got away safely!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Flee });
                return;
            }
            if (id == "GUARDSPEC")
            {
                if (Mist[0] > 0) { Say("But it had no effect!"); return; }
                Mist[0] = 5; Say("Your team became shrouded in mist!");
                return;
            }
            var b = Field[0].Take(SlotsPerSide).FirstOrDefault(x => x != null && x.PartyIndex == party && !x.Pk.Fainted) ?? user;
            if (b == null) { Say("But it had no effect!"); return; }
            int stat = XItemStat(id);
            if (stat >= 0)
            {
                // Gen 7+: X items raise by 2 stages; X Attack 2 / 3 / 6 by that many
                char last = id[id.Length - 1];
                int amount = last == '2' ? 2 : last == '3' ? 3 : last == '6' ? 6 : 2;
                if (b.Stages[stat] >= 6) { Say("But it had no effect!"); return; }
                ChangeStat(b, stat, amount, b);
                b.Pk.ChangeHappiness("battleitem");
                return;
            }
            if (id.StartsWith("DIREHIT"))
            {
                int amount = id == "DIREHIT3" ? 3 : 2;
                if (b.CritStage >= 2) { Say("But it had no effect!"); return; }
                b.CritStage = Math.Min(3, b.CritStage + amount);
                Say(Name(b) + " is getting pumped!");
                return;
            }
            if (id == "YELLOWFLUTE") { if (b.Confusion <= 0) { Say("But it had no effect!"); return; } b.Confusion = 0; Say(Name(b) + " snapped out of its confusion!"); return; }
            if (id == "REDFLUTE") { if (b.AttractedTo == null) { Say("But it had no effect!"); return; } b.AttractedTo = null; Say(Name(b) + " got over its infatuation!"); return; }
        }

        // ------------------------------------------------------------------ catching
        static double BallModifier(string ball, Battle b)
        {
            var target = b.Live(1).FirstOrDefault() ?? b.Field[1][0];
            var foe = target.Pk; var types = foe.Types(b.P);
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
                        int me = (b.Live(0).FirstOrDefault() ?? b.Field[0][0]).Pk.Level, them = foe.Level;
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
            if (DisablePokeBalls) { Say("You can't throw a Poké Ball now!"); return; }
            Say(PlayerName + " threw " + (StartsWithVowel(bname) ? "an " : "a ") + bname + "!");
            if (!IsWild && !(CanSnag && Live(1).Count == 1 && Live(1)[0].Pk.ShadowPokemon))
            {
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Catch, Value = -1 });
                Say("The Trainer blocked your Poké Ball! Don't be a thief!");
                return;
            }
            if (Live(1).Count > 1) { Say("It's no good! It's impossible to aim when there's more than one Pokémon!"); _ev.Add(new BattleEvent { Kind = BattleEventKind.Catch, Value = -1 }); return; }
            var foeB = Live(1).FirstOrDefault();
            if (foeB == null) return;
            var foe = foeB.Pk;
            var fd = foe.Data(P);
            int rate = _catchRateOverride.HasValue ? _catchRateOverride.Value : fd != null && fd.CatchRate > 0 ? fd.CatchRate : 45;
            double mod = BallModifier(ball, this);
            if (ball == "REPEATBALL" && AlreadyCaught(foe.Species)) mod = 3.5;
            int max = foe.TotalHP(P);
            string st = (foe.Status ?? "").ToUpperInvariant();
            double statusMod = st == "SLEEP" || st == "FROZEN" ? 2.5 : st.Length > 0 ? 1.5 : 1;
            double a = (3.0 * max - 2.0 * foe.HP) * rate * mod / (3.0 * max) * statusMod;
            // Gen 8: low-level Pokémon are easier to catch
            if (foe.Level <= 20) a *= (30 - foe.Level) / 10.0;
            int shakes;
            bool caught, critical = false;
            if (mod >= 255 || a >= 255) { shakes = 4; caught = true; }
            else
            {
                double bv = 65536 / Math.Pow(255 / Math.Max(1, a), 0.1875);
                // Critical capture (Gen 5+): more likely the more species have been caught; one shake decides it
                double cm = CaughtCount > 600 ? 2.5 : CaughtCount > 450 ? 2 : CaughtCount > 300 ? 1.5 : CaughtCount > 150 ? 1 : CaughtCount > 30 ? 0.5 : 0;
                if (CatchingCharm) cm *= 2;
                if (cm > 0 && Rng.Next(256) < (int)(Math.Min(255, a) * cm / 6)) critical = true;
                shakes = 0;
                if (critical) { caught = Rng.Next(65536) < bv; shakes = caught ? 4 : 0; }
                else
                {
                    while (shakes < 4 && Rng.Next(65536) < bv) shakes++;
                    caught = shakes == 4;
                }
            }
            if (critical) Say("A critical catch!");
            _ev.Add(new BattleEvent { Kind = BattleEventKind.Catch, Value = critical ? (caught ? 1 : 0) : Math.Min(3, shakes), Flag = caught, Text = ball });
            if (caught && !IsWild)
            {
                // Snagging a trainer's Shadow Pokémon: it's the player's now, and the trainer sends out the next one
                Say("Gotcha! " + foe.Name(P) + " was snagged!");
                foe.Ball = ball;
                foeB.Reset();
                Snagged.Add(foe);
                var gone = Json.Json.Deserialize<Pokemon>(Json.Json.Write(foe)); gone.HP = 0;
                Parties[1][foeB.PartyIndex] = gone;
                var stub = new Battler(gone, 1, foeB.PartyIndex) { Slot = foeB.Slot, FaintShown = true };
                Field[1][foeB.Slot] = stub;
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Withdraw, Side = 1, Party = foeB.PartyIndex, Slot = foeB.Slot });
                return;
            }
            if (caught)
            {
                Say("Gotcha! " + foe.Name(P) + " was caught!");
                foe.Ball = ball;
                foeB.Reset();
                CaughtPokemon = foe;
                Outcome = BattleOutcome.Caught;
            }
            else BallFailed(ball);
            if (!caught) Say(shakes == 0 ? "Oh no! The Pokémon broke free!" : shakes == 1 ? "Aww! It appeared to be caught!" : shakes == 2 ? "Aargh! Almost had it!" : "Gah! It was so close, too!");
        }

        static bool StartsWithVowel(string s) { return !string.IsNullOrEmpty(s) && "AEIOUaeiou".IndexOf(s[0]) >= 0; }
    }
}
