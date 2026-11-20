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
        public ChallengeState Challenge { get; set; }
        /// <summary>Mystery Gift IDs already received.</summary>
        public List<string> GiftsReceived { get; set; }
        /// <summary>The address last typed to join a link.</summary>
        public string LastLinkAddress { get; set; }
        /// <summary>$PokemonGlobal.encounter_version: which version of a map's encounter tables is used (0 = the normal one).</summary>
        public int EncounterVersion { get; set; }
        /// <summary>NPCs following the player.</summary>
        public List<FollowerInfo> Followers { get; set; }
        /// <summary>Voltorb Flip level reached (1-8).</summary>
        public int VoltorbLevel { get; set; }
        /// <summary>Triple Triad cards owned: species → count.</summary>
        public Dictionary<string, int> TriadCards { get; set; }
        /// <summary>Essentials Battle Frontier challenge (pbBattleChallenge) and records.</summary>
        public FrontierState Frontier { get; set; }
        /// <summary>Day number of the last Pokérus day count.</summary>
        public int PokerusDay { get; set; }
        /// <summary>A roamer was already met since the last map change.</summary>
        public bool RoamedAlready { get; set; }
    }
}
