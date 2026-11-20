using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeCreator.Battles
{
    /// <summary>
    /// Hooks for link (online / LAN) battles and other outside control: the opponent's commands can come from outside
    /// instead of the AI, the main calls can be routed elsewhere, and messages can be worded for the player on side 1.
    /// Both players run the same battle (same seed, same commands) from side 0's point of view, so they stay in step.
    /// </summary>
    public partial class Battle
    {
        /// <summary>Side 1's command for one of its Pokémon (instead of the AI); null = use the AI.</summary>
        public Func<Battler, BattleCommand> ExternalCommand;
        /// <summary>Routes Start / DoTurn / SwitchAfterFaint elsewhere (a link battle's view of the battle).</summary>
        public Func<List<BattleEvent>> StartOverride;
        public Func<List<BattleCommand>, List<BattleEvent>> TurnOverride;
        public Func<int, int, List<BattleEvent>> SwitchAfterFaintOverride;
        /// <summary>Messages worded for the player on side 1 (side 0's Pokémon are "the opposing" ones).</summary>
        public bool FlipNames;

        /// <summary>Encodes a command for sending: kind, index, slot, target side, target slot, mega.</summary>
        public static List<int> EncodeCommand(BattleCommand c)
        {
            return new List<int> { (int)c.Kind, c.Index, c.Slot, c.TargetSide, c.TargetSlot, c.Mega ? 1 : 0 };
        }

        public static BattleCommand DecodeCommand(List<int> v)
        {
            if (v == null || v.Count < 6) return BattleCommand.Fight(-1);
            return new BattleCommand { Kind = (BattleCommandKind)v[0], Index = v[1], Slot = v[2], TargetSide = v[3], TargetSlot = v[4], Mega = v[5] != 0 };
        }

        /// <summary>A side-1 Pokémon switching out by command (link battles).</summary>
        void SwitchFoe(int slot, int party)
        {
            var old = Field[1][slot];
            if (old != null)
            {
                SwitchOutAbility(old);
                Say(FlipNames ? old.Pk.Name(P) + ", come back!" : TrainerLabel(old.PartyIndex) + " withdrew " + old.Pk.Name(P) + "!");
                _ev.Add(new BattleEvent { Kind = BattleEventKind.Withdraw, Side = 1, Party = old.PartyIndex, Slot = slot });
            }
            var b = Place(1, slot, party);
            b.SwitchedIn = true;
            Say(FlipNames ? "Go! " + b.Pk.Name(P) + "!" : TrainerLabel(party) + " sent out " + b.Pk.Name(P) + "!");
            SendOutEvent(b);
            MarkParticipant();
            SwitchInAbility(b);
        }

        /// <summary>Copies an event with its sides swapped (showing a link battle from side 1).</summary>
        public static BattleEvent Flip(BattleEvent e)
        {
            var f = new BattleEvent { Kind = e.Kind, Side = e.Side >= 0 ? 1 - e.Side : e.Side, Party = e.Party, Slot = e.Slot, Value = e.Value, Value2 = e.Value2, Text = e.Text, Flag = e.Flag, Moves = e.Moves };
            if (e.Kind == BattleEventKind.MoveAnim && e.Value >= 0) f.Value = 1 - e.Value;
            return f;
        }

        /// <summary>For a link battle's view: the outcome as the player on side 1 sees it.</summary>
        public static BattleOutcome FlipOutcome(BattleOutcome o)
        {
            return o == BattleOutcome.Win ? BattleOutcome.Loss : o == BattleOutcome.Loss ? BattleOutcome.Win : o;
        }

        /// <summary>Lets a view battle mirror another (its outcome).</summary>
        public void SetOutcome(BattleOutcome o) { Outcome = o; }
    }
}
