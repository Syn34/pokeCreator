using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Pkmn;

namespace PokeCreator.Battles
{
    /// <summary>
    /// A partner trainer (pbRegisterPartner): the battle becomes a double battle with the partner's Pokémon in the
    /// player's second slot, chosen by the AI. Partner Pokémon come after the player's in Parties[0] and gain no EXP.
    /// </summary>
    public partial class Battle
    {
        /// <summary>Index in Parties[0] where the partner's Pokémon start (-1 = no partner).</summary>
        public int PartnerFrom = -1;
        public string PartnerName, PartnerTypeName;
        public int PartnerSkill = 32;

        public bool HasPartner { get { return PartnerFrom >= 0; } }
        public bool IsPartnerIndex(int party) { return PartnerFrom >= 0 && party >= PartnerFrom; }
        public bool IsPartner(Battler b) { return b != null && b.Side == 0 && IsPartnerIndex(b.PartyIndex); }

        /// <summary>The player's Pokémon on the field that the player gives commands to.</summary>
        public List<Battler> PlayerControlled() { return Live(0).Where(b => !IsPartner(b)).ToList(); }

        /// <summary>Adds a partner trainer's team before Start(). Single battles become doubles (the opponent may still have one Pokémon out).</summary>
        public void AddPartner(List<Pokemon> partnerParty, string name, string typeName, int skill)
        {
            if (partnerParty == null || !partnerParty.Any(Able) || IsTriple || !Parties[0].Any(Able)) return;
            var combined = new List<Pokemon>(Parties[0]);
            PartnerFrom = combined.Count;
            combined.AddRange(partnerParty);
            Parties[0] = combined;
            PartnerName = name; PartnerTypeName = typeName; PartnerSkill = skill;
            SlotsPerSide = 2;
        }

        List<int> PartnerBench() { return Enumerable.Range(0, Parties[0].Count).Where(i => IsPartnerIndex(i) && Able(Parties[0][i]) && !OnFieldParty(0, i)).ToList(); }

        string PartnerLabel { get { return (string.IsNullOrEmpty(PartnerTypeName) ? "" : PartnerTypeName + " ") + PartnerName; } }

        /// <summary>Start(): the player's first Pokémon in slot 0, the partner's in slot 1.</summary>
        List<Battler> PlacePlayerSideWithPartner()
        {
            var mine = new List<Battler>();
            var own = Bench(0);
            if (own.Count > 0) mine.Add(Place(0, 0, own[0]));
            Say("Go! " + mine[0].Pk.Name(P) + "!");
            var pb = PartnerBench();
            if (pb.Count > 0)
            {
                var b = Place(0, 1, pb[0]);
                Say(PartnerLabel + " sent out " + b.Pk.Name(P) + "!");
                mine.Add(b);
            }
            return mine;
        }

        /// <summary>A fainted partner Pokémon is replaced by the partner's next one straight away.</summary>
        void PartnerReplace(Battler me)
        {
            var pb = PartnerBench();
            if (pb.Count == 0) { Field[0][me.Slot] = null; return; }
            var nb = Place(0, me.Slot, pb[0]);
            Say(PartnerLabel + " sent out " + nb.Pk.Name(P) + "!");
            SendOutEvent(nb);
            MarkParticipant();
            SwitchInAbility(nb);
        }
    }
}
