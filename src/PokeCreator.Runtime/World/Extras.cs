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
        /// <summary>Events moved by pbNoticePlayer / $PokemonMap.addMovedEvent on the current map: "map,event" → x, y, direction (kept when saving there).</summary>
        public Dictionary<string, List<int>> MovedEvents { get; set; }
        /// <summary>The random dungeon being explored (its layout seed) and $PokemonGlobal.dungeon_area / dungeon_version.</summary>
        public DungeonState Dungeon { get; set; }
        public string DungeonArea { get; set; }
        public int DungeonVersion { get; set; }
        /// <summary>The battle kept with pbRecordLastBattle (watched with pbPlayLastBattle).</summary>
        public BattleRecord SavedBattle { get; set; }
        /// <summary>Pokédexes the player can use (regional dex numbers; -1 = National). Null = the default (the first regional dex, or National if there are none).</summary>
        public List<int> UnlockedDexes { get; set; }
        /// <summary>pbTimeEvent: the time (ticks) stored for a variable.</summary>
        public Dictionary<string, long> TimeEvents { get; set; }
        /// <summary>Change Battle BGM / Battle End ME (event commands 132 / 133).</summary>
        public string BattleBgm { get; set; }
        public string BattleEndMe { get; set; }
        /// <summary>$PokemonGlobal.nextBattleBGM / nextBattleME: music for the next battle only.</summary>
        public string NextBattleBgm { get; set; }
        public string NextBattleMe { get; set; }
        /// <summary>Letters kept in the PC mailbox.</summary>
        public List<PokeCreator.Pkmn.PokemonMail> Mailbox { get; set; }
        /// <summary>White Flute (1) / Black Flute (-1) used on map FluteMap: more / fewer wild Pokémon there.</summary>
        public int Flute { get; set; }
        public int FluteMap { get; set; }
        /// <summary>Music chosen on the Pokégear jukebox (plays instead of map music until turned off).</summary>
        public string JukeboxBgm { get; set; }
        /// <summary>Play statistics ($stats).</summary>
        public Dictionary<string, int> Stats { get; set; }
        /// <summary>Day number of the last Pokérus day count.</summary>
        public int PokerusDay { get; set; }
        /// <summary>A roamer was already met since the last map change.</summary>
        public bool RoamedAlready { get; set; }
    }
}
