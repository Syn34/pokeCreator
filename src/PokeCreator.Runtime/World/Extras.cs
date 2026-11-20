using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.World
{
    /// <summary>A partner trainer travelling with the player (pbRegisterPartner).</summary>
    public class PartnerInfo
    {
        public string TrainerType { get; set; }
        public string Name { get; set; }
        public int Version { get; set; }
        public List<Pokemon> Party { get; set; }
        public PartnerInfo() { Party = new List<Pokemon>(); }
    }

    /// <summary>Saved state for the optional Essentials systems: partner, roamers, Safari Zone, Bug-Catching Contest, Mystery Gift...</summary>
    public class GameExtras
    {
        public PartnerInfo Partner { get; set; }
        public List<RoamerState> Roamers { get; set; }
        public SafariState Safari { get; set; }
        public BugContestState BugContest { get; set; }
        /// <summary>Day number of the last Pokérus day count.</summary>
        public int PokerusDay { get; set; }
        /// <summary>A roamer was already met since the last map change.</summary>
        public bool RoamedAlready { get; set; }
    }
}
