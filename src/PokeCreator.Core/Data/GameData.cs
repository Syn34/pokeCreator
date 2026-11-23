using System;
using System.Collections.Generic;
using PokeCreator.Json;

namespace PokeCreator.Data
{
    // ---------------------------------------------------------------------------------
    // Editor metadata. The Studio builds its forms from these attributes, so adding a new
    // field to any data class automatically gives it an editor control.
    // ---------------------------------------------------------------------------------

    [AttributeUsage(AttributeTargets.Property)]
    public class FieldAttribute : Attribute
    {
        public string Label;
        public string Help;
        /// <summary>Name of the database table this field refers to ("types", "moves", ...).</summary>
        public string Ref;
        /// <summary>Fixed list of allowed values (shown as a dropdown).</summary>
        public string[] Choices;
        /// <summary>Asset folder for file pickers, e.g. "Graphics/Characters".</summary>
        public string Asset;
        public bool Multiline;
        public string Group = "General";
        public FieldAttribute(string label) { Label = label; }
    }

    /// <summary>Base class of every database entry. Id is the internal name, e.g. "PIKACHU".</summary>
    public abstract class GameRecord
    {
        [Field("ID", Help = "Internal name used by events and scripts. Capitals, no spaces.", Group = "General")]
        public string Id { get; set; }

        [JsonIgnore]
        public virtual string DisplayName { get { return Id; } }

        public override string ToString() { return DisplayName; }
    }

    public static class Choices
    {
        public static readonly string[] Stats = { "HP", "ATTACK", "DEFENSE", "SPECIAL_ATTACK", "SPECIAL_DEFENSE", "SPEED" };
        public static readonly string[] MoveCategories = { "Physical", "Special", "Status" };
        public static readonly string[] MoveTargets = { "None", "User", "NearAlly", "UserOrNearAlly", "AllAllies", "UserAndAllies", "NearFoe", "RandomNearFoe", "AllNearFoes", "Foe", "AllFoes", "NearOther", "AllNearOthers", "Other", "AllBattlers", "UserSide", "FoeSide", "BothSides" };
        public static readonly string[] GenderRatios = { "AlwaysMale", "FemaleOneEighth", "Female25Percent", "Female50Percent", "Female75Percent", "FemaleSevenEighths", "AlwaysFemale", "Genderless" };
        public static readonly string[] GrowthRates = { "Medium", "Erratic", "Fluctuating", "Parabolic", "Fast", "Slow" };
        public static readonly string[] EggGroups = { "Monster", "Water1", "Bug", "Flying", "Field", "Fairy", "Grass", "Humanlike", "Water3", "Mineral", "Amorphous", "Water2", "Ditto", "Dragon", "Undiscovered" };
        public static readonly string[] Colors = { "Red", "Blue", "Yellow", "Green", "Black", "Brown", "Purple", "Gray", "White", "Pink" };
        public static readonly string[] Shapes = { "Head", "Serpentine", "Finned", "HeadArms", "HeadBase", "BipedalTail", "HeadLegs", "Quadruped", "Winged", "Multiped", "MultiBody", "Bipedal", "MultiWinged", "Insectoid" };
        public static readonly string[] Habitats = { "None", "Grassland", "Forest", "WatersEdge", "Sea", "Cave", "Mountain", "RoughTerrain", "Urban", "Rare" };
        public static readonly string[] Genders = { "Male", "Female", "Unknown", "Mixed" };
        public static readonly string[] FieldUses = { "", "OnPokemon", "Direct", "TM", "HM", "TR" };
        public static readonly string[] BattleUses = { "", "OnPokemon", "OnMove", "OnBattler", "OnFoe", "Direct" };
        public static readonly string[] Pockets = { "1", "2", "3", "4", "5", "6", "7", "8" };
        public static readonly string[] EncounterTypes = { "Land", "LandDay", "LandNight", "LandMorning", "LandAfternoon", "LandEvening", "PokeRadar", "Cave", "CaveDay", "CaveNight", "CaveMorning", "CaveAfternoon", "CaveEvening", "Water", "WaterDay", "WaterNight", "WaterMorning", "WaterAfternoon", "WaterEvening", "OldRod", "GoodRod", "SuperRod", "RockSmash", "HeadbuttLow", "HeadbuttHigh", "BugContest" };
        public static readonly string[] Natures = { "", "HARDY", "LONELY", "BRAVE", "ADAMANT", "NAUGHTY", "BOLD", "DOCILE", "RELAXED", "IMPISH", "LAX", "TIMID", "HASTY", "SERIOUS", "JOLLY", "NAIVE", "MODEST", "MILD", "QUIET", "BASHFUL", "RASH", "CALM", "GENTLE", "SASSY", "CAREFUL", "QUIRKY" };
        public static readonly string[] EvolutionMethods = { "Level", "LevelMale", "LevelFemale", "LevelDay", "LevelNight", "LevelMorning", "LevelAfternoon", "LevelEvening", "LevelNoWeather", "LevelSun", "LevelRain", "LevelSnow", "LevelSandstorm", "LevelCycling", "LevelSurfing", "LevelDiving", "LevelDarkness", "LevelDarkInParty", "AttackGreater", "AtkDefEqual", "DefenseGreater", "Silcoon", "Cascoon", "Ninjask", "Shedinja", "Happiness", "HappinessMale", "HappinessFemale", "HappinessDay", "HappinessNight", "HappinessMove", "HappinessMoveType", "HappinessHoldItem", "MaxHappiness", "Beauty", "HoldItem", "HoldItemMale", "HoldItemFemale", "DayHoldItem", "NightHoldItem", "HoldItemHappiness", "HasMove", "HasMoveType", "HasInParty", "Location", "LocationFlag", "Region", "Item", "ItemMale", "ItemFemale", "ItemDay", "ItemNight", "ItemHappiness", "Trade", "TradeMale", "TradeFemale", "TradeDay", "TradeNight", "TradeItem", "TradeSpecies", "BattleDealCriticalHit", "Event", "EventAfterDamageTaken" };
    }

    // ---------------------------------------------------------------------------------
    public class TypeData : GameRecord
    {
        [Field("Name")] public string Name { get; set; }
        [Field("Icon position", Help = "Row of this type's icon in Graphics/UI/types.png")] public int IconPosition { get; set; }
        [Field("Special type", Help = "Gen 1-3: moves of this type are special.")] public bool IsSpecialType { get; set; }
        [Field("Pseudo type", Help = "Not a real type (e.g. ???).")] public bool IsPseudoType { get; set; }
        [Field("Weak to (×2)", Ref = "types", Group = "Matchups", Help = "Attacks of these types are super effective against this type (double damage).")] public List<string> Weaknesses { get; set; }
        [Field("Resists (×½)", Ref = "types", Group = "Matchups", Help = "Attacks of these types are not very effective against this type (half damage).")] public List<string> Resistances { get; set; }
        [Field("Immune to (×0)", Ref = "types", Group = "Matchups", Help = "Attacks of these types have no effect on this type. Every type not listed in these three does normal damage.")] public List<string> Immunities { get; set; }
        [Field("Flags")] public List<string> Flags { get; set; }

        public TypeData() { Weaknesses = new List<string>(); Resistances = new List<string>(); Immunities = new List<string>(); Flags = new List<string>(); }
        public override string DisplayName { get { return Name ?? Id; } }
    }

    public class AbilityData : GameRecord
    {
        [Field("Name")] public string Name { get; set; }
        [Field("Description", Multiline = true)] public string Description { get; set; }
        [Field("Flags")] public List<string> Flags { get; set; }
        public AbilityData() { Flags = new List<string>(); }
        public override string DisplayName { get { return Name ?? Id; } }
    }

    public class MoveData : GameRecord
    {
        [Field("Name")] public string Name { get; set; }
        [Field("Type", Ref = "types")] public string Type { get; set; }
        [Field("Category", Choices = new[] { "Physical", "Special", "Status" })] public string Category { get; set; }
        [Field("Power", Help = "0 for status moves, 1 for variable power.", Group = "Battle")] public int Power { get; set; }
        [Field("Accuracy", Help = "0 = never misses.", Group = "Battle")] public int Accuracy { get; set; }
        [Field("PP", Group = "Battle")] public int TotalPP { get; set; }
        [Field("Target", Ref = "@MoveTargets", Group = "Battle")] public string Target { get; set; }
        [Field("Priority", Group = "Battle")] public int Priority { get; set; }
        [Field("Function code", Help = "Name of the battle effect handler.", Group = "Battle")] public string FunctionCode { get; set; }
        [Field("Effect chance %", Group = "Battle")] public int EffectChance { get; set; }
        [Field("Flags", Help = "Contact, CanProtect, CanMirrorMove, Sound, Punching ...", Group = "Battle")] public List<string> Flags { get; set; }
        [Field("Description", Multiline = true)] public string Description { get; set; }
        public MoveData() { Flags = new List<string>(); }
        public override string DisplayName { get { return Name ?? Id; } }
    }

    public class ItemData : GameRecord
    {
        [Field("Name")] public string Name { get; set; }
        [Field("Plural name")] public string NamePlural { get; set; }
        [Field("Portion name")] public string PortionName { get; set; }
        [Field("Portion plural")] public string PortionNamePlural { get; set; }
        [Field("Bag pocket", Help = "1 Items, 2 Medicine, 3 Poké Balls, 4 TMs, 5 Berries, 6 Mail, 7 Battle Items, 8 Key Items")] public int Pocket { get; set; }
        [Field("Price", Group = "Shop")] public int Price { get; set; }
        [Field("Sell price", Help = "Leave 0 for half of Price.", Group = "Shop")] public int SellPrice { get; set; }
        [Field("BP price", Group = "Shop")] public int BPPrice { get; set; }
        [Field("Field use", Choices = new[] { "", "OnPokemon", "Direct", "TM", "HM", "TR" }, Group = "Usage")] public string FieldUse { get; set; }
        [Field("Battle use", Choices = new[] { "", "OnPokemon", "OnMove", "OnBattler", "OnFoe", "Direct" }, Group = "Usage")] public string BattleUse { get; set; }
        [Field("Consumable", Group = "Usage")] public bool? Consumable { get; set; }
        [Field("Teaches move", Ref = "moves", Group = "Usage")] public string Move { get; set; }
        [Field("Flags", Group = "Usage")] public List<string> Flags { get; set; }
        [Field("Description", Multiline = true)] public string Description { get; set; }
        [Field("Hours per growth stage", Group = "Berry plant", Help = "Berries only (PBS berry_plants.txt). 0 = the usual value for this berry.")] public int HoursPerStage { get; set; }
        [Field("Drying per hour", Group = "Berry plant", Help = "Soil moisture lost each hour (0 = 15).")] public int DryingPerHour { get; set; }
        [Field("Yield (min,max)", Group = "Berry plant"), JsonCompact] public List<int> Yield { get; set; }
        public ItemData() { Flags = new List<string>(); Pocket = 1; }
        public override string DisplayName { get { return Name ?? Id; } }
    }

    /// <summary>
    /// Phone conversation lines (PBS phone.txt): ID "Default" for everyone, or "TRAINERTYPE,Name" for one trainer.
    /// Lines can use \TN (trainer's name), \TP (one of their Pokémon), \TM (where they are) and \TE (a wild Pokémon there).
    /// </summary>
    public class PhoneMessageData : GameRecord
    {
        [Field("Greetings", Multiline = true)] public List<string> Intro { get; set; }
        [Field("Chat (first part)", Multiline = true)] public List<string> Body1 { get; set; }
        [Field("Chat (second part)", Multiline = true)] public List<string> Body2 { get; set; }
        [Field("Rematch request", Multiline = true)] public List<string> BattleRequest { get; set; }
        [Field("Goodbyes", Multiline = true)] public List<string> End { get; set; }
        public PhoneMessageData() { Intro = new List<string>(); Body1 = new List<string>(); Body2 = new List<string>(); BattleRequest = new List<string>(); End = new List<string>(); }
    }

    /// <summary>A ribbon (PBS ribbons.txt): its name, description and position in the ribbon icon sheet.</summary>
    public class RibbonData : GameRecord
    {
        [Field("Name")] public string Name { get; set; }
        [Field("Icon position")] public int IconPosition { get; set; }
        [Field("Description", Multiline = true)] public string Description { get; set; }
        [Field("Flags")] public List<string> Flags { get; set; }
        public RibbonData() { Flags = new List<string>(); }
        public override string DisplayName { get { return Name ?? Id; } }
    }

    public class LevelMove
    {
        public int Level { get; set; }
        public string Move { get; set; }
        public override string ToString() { return "Lv " + Level + ": " + Move; }
    }

    public class StatValue
    {
        public string Stat { get; set; }
        public int Value { get; set; }
        public override string ToString() { return Stat + " +" + Value; }
    }

    public class Evolution
    {
        public string Species { get; set; }
        public string Method { get; set; }
        public string Parameter { get; set; }
        public override string ToString() { return Species + " (" + Method + (string.IsNullOrEmpty(Parameter) ? "" : " " + Parameter) + ")"; }
    }

    public class SpeciesData : GameRecord
    {
        [Field("Species", Help = "Base species internal name (same as ID for form 0).")] public string Species { get; set; }
        [Field("Form number")] public int Form { get; set; }
        [Field("Name")] public string Name { get; set; }
        [Field("Form name")] public string FormName { get; set; }
        [Field("Category", Help = "e.g. \"Seed Pokémon\" (without the word Pokémon)", Group = "Pokédex")] public string Category { get; set; }
        [Field("Pokédex entry", Multiline = true, Group = "Pokédex")] public string Pokedex { get; set; }

        [Field("Types", Ref = "types", Group = "Battle")] public List<string> Types { get; set; }
        [Field("Base stats", Help = "HP, Atk, Def, Spd, SpAtk, SpDef", Group = "Battle"), JsonCompact] public List<int> BaseStats { get; set; }
        [Field("Abilities", Ref = "abilities", Group = "Battle")] public List<string> Abilities { get; set; }
        [Field("Hidden abilities", Ref = "abilities", Group = "Battle")] public List<string> HiddenAbilities { get; set; }
        [Field("EV yield", Group = "Battle")] public List<StatValue> EVs { get; set; }
        [Field("Base EXP", Group = "Battle")] public int BaseExp { get; set; }
        [Field("Growth rate", Choices = new[] { "Medium", "Erratic", "Fluctuating", "Parabolic", "Fast", "Slow" }, Group = "Battle")] public string GrowthRate { get; set; }
        [Field("Catch rate", Group = "Battle")] public int CatchRate { get; set; }
        [Field("Base happiness", Group = "Battle")] public int Happiness { get; set; }

        [Field("Level-up moves", Group = "Moves")] public List<LevelMove> Moves { get; set; }
        [Field("Tutor/TM moves", Ref = "moves", Group = "Moves")] public List<string> TutorMoves { get; set; }
        [Field("Egg moves", Ref = "moves", Group = "Moves")] public List<string> EggMoves { get; set; }

        [Field("Evolutions", Group = "Evolution")] public List<Evolution> Evolutions { get; set; }

        [Field("Gender ratio", Choices = new[] { "AlwaysMale", "FemaleOneEighth", "Female25Percent", "Female50Percent", "Female75Percent", "FemaleSevenEighths", "AlwaysFemale", "Genderless" }, Group = "Breeding")] public string GenderRatio { get; set; }
        [Field("Egg groups", Ref = "@EggGroups", Group = "Breeding")] public List<string> EggGroups { get; set; }
        [Field("Hatch steps", Group = "Breeding")] public int HatchSteps { get; set; }
        [Field("Incense", Ref = "items", Group = "Breeding")] public string Incense { get; set; }
        [Field("Offspring", Ref = "species", Group = "Breeding")] public List<string> Offspring { get; set; }

        [Field("Height (m)", Group = "Pokédex")] public double Height { get; set; }
        [Field("Weight (kg)", Group = "Pokédex")] public double Weight { get; set; }
        [Field("Color", Choices = new[] { "Red", "Blue", "Yellow", "Green", "Black", "Brown", "Purple", "Gray", "White", "Pink" }, Group = "Pokédex")] public string Color { get; set; }
        [Field("Shape", Ref = "@Shapes", Group = "Pokédex")] public string Shape { get; set; }
        [Field("Habitat", Ref = "@Habitats", Group = "Pokédex")] public string Habitat { get; set; }
        [Field("Generation", Group = "Pokédex")] public int Generation { get; set; }
        [Field("Pokédex form name", Group = "Pokédex")] public string PokedexForm { get; set; }

        [Field("Wild item (common)", Ref = "items", Group = "Wild")] public List<string> WildItemCommon { get; set; }
        [Field("Wild item (uncommon)", Ref = "items", Group = "Wild")] public List<string> WildItemUncommon { get; set; }
        [Field("Wild item (rare)", Ref = "items", Group = "Wild")] public List<string> WildItemRare { get; set; }

        [Field("Mega stone", Ref = "items", Group = "Forms")] public string MegaStone { get; set; }
        [Field("Shadow heart gauge", Group = "Shadow", Help = "Heart gauge size when this species is a Shadow Pokémon (0 = 4000). PBS shadow_pokemon.txt.")] public int ShadowGaugeSize { get; set; }
        [Field("Shadow moves", Group = "Shadow", Help = "Shadow moves it knows as a Shadow Pokémon (empty = Shadow Rush).")] public List<string> ShadowMoves { get; set; }
        [Field("Mega move", Ref = "moves", Group = "Forms")] public string MegaMove { get; set; }
        [Field("Mega message", Group = "Forms")] public int MegaMessage { get; set; }
        [Field("Flags")] public List<string> Flags { get; set; }

        [Field("Back sprite offset", Help = "x, y", Group = "Sprites"), JsonCompact] public List<int> BackSprite { get; set; }
        [Field("Front sprite offset", Help = "x, y", Group = "Sprites"), JsonCompact] public List<int> FrontSprite { get; set; }
        [Field("Front altitude", Group = "Sprites")] public int FrontSpriteAltitude { get; set; }
        [Field("Shadow X", Group = "Sprites")] public int ShadowX { get; set; }
        [Field("Shadow size", Group = "Sprites")] public int ShadowSize { get; set; }

        public SpeciesData()
        {
            Types = new List<string>(); BaseStats = new List<int> { 1, 1, 1, 1, 1, 1 };
            Abilities = new List<string>(); HiddenAbilities = new List<string>(); EVs = new List<StatValue>();
            Moves = new List<LevelMove>(); TutorMoves = new List<string>(); EggMoves = new List<string>();
            Evolutions = new List<Evolution>(); EggGroups = new List<string>(); Offspring = new List<string>();
            WildItemCommon = new List<string>(); WildItemUncommon = new List<string>(); WildItemRare = new List<string>();
            Flags = new List<string>(); BackSprite = new List<int>(); FrontSprite = new List<int>();
            GrowthRate = "Medium"; GenderRatio = "Female50Percent"; CatchRate = 255; Happiness = 70; HatchSteps = 1; ShadowSize = 2;
        }
        public override string DisplayName
        {
            get
            {
                string n = Name ?? Species ?? Id;
                if (Form > 0) n += " (" + (string.IsNullOrEmpty(FormName) ? "form " + Form : FormName) + ")";
                return n;
            }
        }
    }

    public class TrainerTypeData : GameRecord
    {
        [Field("Name")] public string Name { get; set; }
        [Field("Gender", Choices = new[] { "Male", "Female", "Unknown", "Mixed" })] public string Gender { get; set; }
        [Field("Base money")] public int BaseMoney { get; set; }
        [Field("AI skill level")] public int SkillLevel { get; set; }
        [Field("Flags")] public List<string> Flags { get; set; }
        [Field("Intro music", Asset = "Audio/ME", Group = "Music")] public string IntroBGM { get; set; }
        [Field("Battle music", Asset = "Audio/BGM", Group = "Music")] public string BattleBGM { get; set; }
        [Field("Victory music", Asset = "Audio/BGM", Group = "Music")] public string VictoryBGM { get; set; }
        public TrainerTypeData() { Flags = new List<string>(); Gender = "Unknown"; BaseMoney = 30; }
        public override string DisplayName { get { return Id + "  (" + (Name ?? "") + ")"; } }
    }

    public class TrainerPokemon
    {
        public string Species { get; set; }
        public int Level { get; set; }
        public int Form { get; set; }
        public string Name { get; set; }
        public List<string> Moves { get; set; }
        public string Ability { get; set; }
        public int? AbilityIndex { get; set; }
        public string Item { get; set; }
        public string Gender { get; set; }
        public string Nature { get; set; }
        [JsonCompact] public List<int> IV { get; set; }
        [JsonCompact] public List<int> EV { get; set; }
        public int? Happiness { get; set; }
        public bool Shiny { get; set; }
        public bool SuperShiny { get; set; }
        public bool Shadow { get; set; }
        public string Ball { get; set; }
        public TrainerPokemon() { Moves = new List<string>(); IV = new List<int>(); EV = new List<int>(); Level = 5; }
        public override string ToString() { return Species + " Lv." + Level + (string.IsNullOrEmpty(Name) ? "" : " \"" + Name + "\""); }
    }

    public class TrainerData : GameRecord
    {
        [Field("Trainer type", Ref = "trainer_types")] public string TrainerType { get; set; }
        [Field("Name")] public string Name { get; set; }
        [Field("Version", Help = "Use different versions for rematches.")] public int Version { get; set; }
        [Field("Items", Ref = "items")] public List<string> Items { get; set; }
        [Field("Text when defeated", Multiline = true)] public string LoseText { get; set; }
        [Field("Party", Group = "Party")] public List<TrainerPokemon> Pokemon { get; set; }
        public TrainerData() { Items = new List<string>(); Pokemon = new List<TrainerPokemon>(); }
        public static string MakeId(string type, string name, int version) { return type + "," + name + (version > 0 ? "," + version : ""); }
        public override string DisplayName { get { return Name + " (" + TrainerType + (Version > 0 ? " v" + Version : "") + ")"; } }
    }

    public class EncounterSlot
    {
        public int Chance { get; set; }
        public string Species { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public override string ToString() { return Chance + "% " + Species + " Lv." + MinLevel + (MaxLevel != MinLevel ? "-" + MaxLevel : ""); }
    }

    public class EncounterMethod
    {
        public string Type { get; set; }
        public int Density { get; set; }
        public List<EncounterSlot> Slots { get; set; }
        public EncounterMethod() { Slots = new List<EncounterSlot>(); }
        public override string ToString() { return Type + " (" + Slots.Count + " slots)"; }
    }

    public class EncounterData : GameRecord
    {
        [Field("Map ID")] public int Map { get; set; }
        [Field("Version")] public int Version { get; set; }
        [Field("Comment")] public string Comment { get; set; }
        [Field("Encounter methods", Group = "Encounters")] public List<EncounterMethod> Methods { get; set; }
        public EncounterData() { Methods = new List<EncounterMethod>(); }
        public override string DisplayName { get { return "Map " + Map.ToString("000") + (Version > 0 ? "," + Version : "") + (string.IsNullOrEmpty(Comment) ? "" : "  " + Comment); } }
    }

    /// <summary>Global game settings (Essentials metadata.txt section 0).</summary>
    public class GameSettings
    {
        [Field("Game title")] public string Title { get; set; }
        [Field("Start map ID", Group = "New game")] public int StartMap { get; set; }
        [Field("Start X", Group = "New game")] public int StartX { get; set; }
        [Field("Start Y", Group = "New game")] public int StartY { get; set; }
        [Field("Start direction", Help = "2 down, 4 left, 6 right, 8 up", Group = "New game")] public int StartDirection { get; set; }
        [Field("Default player", Help = "Index into Players", Group = "New game")] public int DefaultPlayer { get; set; }
        [Field("Player is chosen in the intro", Help = "On: a new game starts with no player character (nothing is drawn) until an event calls Change Player Character / pbChangePlayer, as in Essentials' intro. Off: the Default player is used from the start.", Group = "New game")] public bool PlayerChosenInIntro { get; set; }
        [Field("Player name", Group = "New game")] public string DefaultPlayerName { get; set; }
        [Field("Starting money", Group = "New game")] public int StartMoney { get; set; }
        [Field("Running needs Running Shoes", Help = "On: the player can only run after an event sets $player.has_running_shoes = true (as in Essentials). Off: running works from the start.", Group = "New game")] public bool RunningShoesRequired { get; set; }
        [Field("Item in PC at start", Ref = "items", Group = "New game")] public List<string> StartItemStorage { get; set; }
        [Field("Home (map,x,y,dir)", Help = "Where the player goes after blacking out with no Pokémon Center.", Group = "New game"), JsonCompact] public List<int> Home { get; set; }
        [Field("PC storage creator")] public string StorageCreator { get; set; }
        [Field("Screen width", Group = "Display")] public int ScreenWidth { get; set; }
        [Field("Screen height", Group = "Display")] public int ScreenHeight { get; set; }
        [Field("Window scale", Group = "Display")] public int WindowScale { get; set; }
        [Field("Use my own font", Help = "Off: the game uses PokeCreator's default font (the FireRed font in BaseData/Fonts), even if this project's Fonts folder has fonts in it. On: it uses the Message font below from this project's Fonts folder.", Group = "Display")] public bool UseProjectFont { get; set; }
        [Field("Message font", Asset = "Fonts", Help = "Used when 'Use my own font' is on. When it's off, you can still pick another default font here.", Group = "Display")] public string Font { get; set; }
        [Field("Font size", Group = "Display")] public int FontSize { get; set; }
        [Field("Windowskin", Asset = "Graphics/Windowskins", Group = "Display", Help = "RMXP system windowskin (kept from imported games; the frames below are what the game draws).")] public string Windowskin { get; set; }
        [Field("Speech frame", Asset = "Graphics/Windowskins", Group = "Display", Help = "Message box frame, e.g. speech hgss 1. Empty or missing = PokeCreator's built-in box.")] public string SpeechFrame { get; set; }
        [Field("Menu frame", Asset = "Graphics/Windowskins", Group = "Display", Help = "Frame for menus and choice boxes, e.g. choice 1. Empty or missing = built-in box.")] public string MenuFrame { get; set; }
        [Field("Wild battle BGM", Asset = "Audio/BGM", Group = "Music")] public string WildBattleBGM { get; set; }
        [Field("Trainer battle BGM", Asset = "Audio/BGM", Group = "Music")] public string TrainerBattleBGM { get; set; }
        [Field("Wild victory ME", Asset = "Audio/BGM", Group = "Music")] public string WildVictoryBGM { get; set; }
        [Field("Trainer victory ME", Asset = "Audio/BGM", Group = "Music")] public string TrainerVictoryBGM { get; set; }
        [Field("Wild capture ME", Asset = "Audio/ME", Group = "Music", Help = "Plays when a wild Pokémon is caught (default: Battle capture success).")] public string WildCaptureME { get; set; }
        [Field("Surf BGM", Asset = "Audio/BGM", Group = "Music")] public string SurfBGM { get; set; }
        [Field("Bicycle BGM", Asset = "Audio/BGM", Group = "Music")] public string BicycleBGM { get; set; }
        [Field("Title music", Asset = "Audio/BGM", Group = "Music")] public string TitleBGM { get; set; }
        [Field("Credits music", Asset = "Audio/BGM", Group = "Music")] public string CreditsBGM { get; set; }
        [Field("Normal hit", Asset = "Audio/SE", Group = "Battle sound effects", Help = "Plays when a move does normal damage (empty = Battle damage normal).")] public string HitNormalSE { get; set; }
        [Field("Super effective hit", Asset = "Audio/SE", Group = "Battle sound effects", Help = "Plays when a move is super effective (empty = Battle damage super).")] public string HitSuperSE { get; set; }
        [Field("Not very effective hit", Asset = "Audio/SE", Group = "Battle sound effects", Help = "Plays when a move is not very effective (empty = Battle damage weak).")] public string HitWeakSE { get; set; }
        [Field("No effect", Asset = "Audio/SE", Group = "Battle sound effects", Help = "Plays when a move has no effect on its target (empty = no sound, as in Essentials).")] public string NoEffectSE { get; set; }
        [Field("Safari steps", Group = "Safari Zone", Help = "Steps allowed in a Safari game (0 = no limit).")] public int SafariSteps { get; set; }
        [Field("Using animated sprites", Group = "Graphics", Help = "Battles, the summary and the Pokédex use animated sprites (Gen 5 style) from Graphics/Pokemon/Animated when a Pokémon has one, and Studio lists Pokémon missing them.")] public bool UseAnimatedSprites { get; set; }
        [Field("Animated sprite speed", Group = "Graphics", Help = "Game frames each picture of an animated sprite stays on screen (0 = 3; 60 frames a second).")] public int AnimatedSpriteSpeed { get; set; }
        [Field("Most money", Group = "Bag", Help = "0 = 999,999.")] public int MaxMoney { get; set; }
        [Field("Most Game Corner coins", Group = "Bag", Help = "0 = 99,999.")] public int MaxCoins { get; set; }
        [Field("Most of one item", Group = "Bag", Help = "0 = 999.")] public int MaxPerSlot { get; set; }
        [Field("Bag pocket names", Group = "Bag", Help = "Names of the 8 bag pockets, in order (empty = Items, Medicine, Poké Balls, TMs & HMs, Berries, Mail, Battle Items, Key Items).")] public List<string> BagPocketNames { get; set; }
        [Field("Maximum level", Group = "Pokémon", Help = "Highest level Pokémon can reach (0 = 100). Essentials' MAXIMUM_LEVEL.")] public int MaxLevel { get; set; }
        [Field("Shiny odds (1 in N)", Group = "Pokémon", Help = "0 = 1 in 4096.")] public int ShinyOdds { get; set; }
        [Field("Hyper Training level", Group = "Battle", Help = "Level a Pokémon needs before Bottle Caps work (0 = the maximum level, as Essentials).")] public int HyperTrainingLevel { get; set; }
        [Field("No double battles in tall grass", Group = "Battle", Help = "Otherwise 30% of encounters in tall grass (terrain tag 10) are wild double battles when the player has two Pokémon that can fight, as in Essentials.")] public bool NoTallGrassDoubles { get; set; }
        [Field("No disobedience", Group = "Battle", Help = "Otherwise Pokémon received from other trainers (trades) above the badge level - 10 per badge, any level with 8 - may ignore orders in battle, as in Essentials.")] public bool NoDisobedience { get; set; }
        [Field("Any high-level Pokémon can disobey", Group = "Battle", Help = "The badge-level rule applies to the player's own Pokémon too, not just traded ones.")] public bool AnyHighLevelCanDisobey { get; set; }
        [Field("No battle transitions", Group = "Battle", Help = "Battles start straight away instead of after the flash and wipe to black.")] public bool NoBattleTransitions { get; set; }
        [Field("Exp. All always on", Group = "Battle", Help = "Every party Pokémon that didn't fight gets half the EXP (as the Gen 6+ Exp. Share). Otherwise this happens only with the EXPALL key item or $player.has_exp_all = true. Pokémon holding an Exp. Share always share EXP.")] public bool ExpAll { get; set; }
        [Field("Following Pokémon", Group = "Field", Help = "The first party Pokémon walks behind the player, using Graphics/Characters/Followers/SPECIES (and Followers shiny/). Pokémon without a sprite don't follow. Players can turn it off in Options; scripts: FollowingPkmn.toggle / toggle_on / toggle_off.")] public bool FollowingPokemon { get; set; }
        [Field("Poison hurts in the field", Group = "Field", Help = "Poisoned party Pokémon lose 1 HP every 4 steps (Gen 1-4). They recover at 1 HP unless the next option is on.")] public bool PoisonInField { get; set; }
        [Field("Poison can faint in the field", Group = "Field", Help = "With field poison on: Pokémon can faint from it (Gen 1-3).")] public bool PoisonFaintInField { get; set; }
        [Field("Fishing hooks by itself", Group = "Field", Help = "Off: after \"Oh! A bite!\" the player must press the Action button quickly or the Pokémon gets away.")] public bool FishingAutoHook { get; set; }
        [Field("PC boxes", Group = "Pokémon Storage", Help = "Number of Pokémon Storage boxes (30 Pokémon each).")] public int StorageBoxes { get; set; }
        [Field("Safari Balls", Group = "Safari Zone", Help = "Safari Balls given at the start of a Safari game.")] public int SafariBalls { get; set; }
        [Field("Contest length (minutes)", Group = "Bug-Catching Contest")] public int BugContestMinutes { get; set; }
        [Field("Sport Balls", Group = "Bug-Catching Contest")] public int BugContestBalls { get; set; }
        [Field("1st prize", Ref = "items", Group = "Bug-Catching Contest")] public string BugContestPrize1 { get; set; }
        [Field("2nd prize", Ref = "items", Group = "Bug-Catching Contest")] public string BugContestPrize2 { get; set; }
        [Field("3rd prize", Ref = "items", Group = "Bug-Catching Contest")] public string BugContestPrize3 { get; set; }
        [Field("Other places' prize", Ref = "items", Group = "Bug-Catching Contest")] public string BugContestPrizeOther { get; set; }
        [Field("Roaming Pokémon", Group = "Roaming", Help = "Pokémon that move to another map each time the player changes map; a wild encounter on their map has a 1 in 4 chance of being them. Their HP carries over between meetings; once caught or defeated they're gone.")] public List<RoamerDef> Roamers { get; set; }
        [Field("Credits", Multiline = true, Group = "Ending", Help = "Text that scrolls after the Hall of Fame ($scene = Scene_Credits.new). One line per row; an empty line makes a gap.")] public string Credits { get; set; }

        public GameSettings()
        {
            Title = "My Pokémon Game"; StartMap = 1; StartDirection = 2; DefaultPlayerName = "Red"; StartMoney = 3000;
            StartItemStorage = new List<string>(); Home = new List<int>(); Roamers = new List<RoamerDef>(); SafariSteps = 600; SafariBalls = 30; StorageBoxes = 40;
            BugContestMinutes = 20; BugContestBalls = 20; BugContestPrize1 = "SUNSTONE"; BugContestPrize2 = "EVERSTONE"; BugContestPrize3 = "SITRUSBERRY"; BugContestPrizeOther = "ORANBERRY";
            ScreenWidth = 512; ScreenHeight = 384; WindowScale = 1; Font = "power green.ttf"; FontSize = 26; Windowskin = "";
            SpeechFrame = "speech hgss 1"; MenuFrame = "choice 1";
        }
    }

    /// <summary>A roaming Pokémon (Essentials' ROAMING_SPECIES): moves between maps and can be met in their wild encounters.</summary>
    public class RoamerDef
    {
        [Field("Species", Ref = "species")] public string Species { get; set; }
        [Field("Level")] public int Level { get; set; }
        [Field("Game switch", Help = "It roams while this switch is on (0 = always).")] public int Switch { get; set; }
        [Field("Encounter", Choices = new[] { "Any", "Land", "Surfing", "Fishing", "Water" }, Help = "Which wild encounters it can appear in: Land = grass and caves, Water = surfing or fishing.")] public string Encounter { get; set; }
        [Field("Battle music", Asset = "Audio/BGM")] public string BattleBGM { get; set; }
        [Field("Maps", Help = "Map IDs it roams between, e.g. 5,6,7,12. Empty = every map with wild encounters.")] public string Maps { get; set; }
        [Field("Flees", Help = "Tries to run away at the end of every turn, as in the games.")] public bool Flees { get; set; }
        public RoamerDef() { Level = 40; Encounter = "Any"; Flees = true; }
        public override string ToString() { return Species + " Lv. " + Level; }
    }

    public class PlayerCharacter
    {
        [Field("Trainer type", Ref = "trainer_types")] public string TrainerType { get; set; }
        [Field("Walk sprite", Asset = "Graphics/Characters")] public string WalkCharset { get; set; }
        [Field("Run sprite", Asset = "Graphics/Characters")] public string RunCharset { get; set; }
        [Field("Cycle sprite", Asset = "Graphics/Characters")] public string CycleCharset { get; set; }
        [Field("Surf sprite", Asset = "Graphics/Characters")] public string SurfCharset { get; set; }
        [Field("Dive sprite", Asset = "Graphics/Characters")] public string DiveCharset { get; set; }
        [Field("Fish sprite", Asset = "Graphics/Characters")] public string FishCharset { get; set; }
        [Field("Surf-fish sprite", Asset = "Graphics/Characters")] public string SurfFishCharset { get; set; }
        public override string ToString() { return TrainerType + " (" + WalkCharset + ")"; }
    }

    public class MapConnection
    {
        public int Map1 { get; set; }
        public string Edge1 { get; set; }
        public int Offset1 { get; set; }
        public int Map2 { get; set; }
        public string Edge2 { get; set; }
        public int Offset2 { get; set; }
        public override string ToString() { return Map1 + " " + Edge1 + " <-> " + Map2 + " " + Edge2; }
    }

    public class TownMapPoint
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string Name { get; set; }
        public string PointOfInterest { get; set; }
        public int FlyMap { get; set; }
        public int FlyX { get; set; }
        public int FlyY { get; set; }
        public string Switch { get; set; }
        public override string ToString() { return X + "," + Y + " " + Name; }
    }

    public class TownMapRegion : GameRecord
    {
        [Field("Region name")] public string Name { get; set; }
        [Field("Map image", Asset = "Graphics/UI/Town Map")] public string Filename { get; set; }
        [Field("Points", Group = "Points")] public List<TownMapPoint> Points { get; set; }
        public TownMapRegion() { Points = new List<TownMapPoint>(); }
        public override string DisplayName { get { return Id + ": " + Name; } }
    }

    /// <summary>A Mystery Gift: a Pokémon or an item players receive from the delivery person after redeeming it.</summary>
    public class MysteryGiftData : GameRecord
    {
        [Field("Title", Help = "Shown when the gift is received, e.g. \"Shiny Pikachu event\".")] public string Name { get; set; }
        [Field("Code", Help = "Players type this on the title screen's Mystery Gift option. Empty = every player can receive it.")] public string Code { get; set; }
        [Field("Kind", Choices = new[] { "Pokemon", "Item" })] public string Kind { get; set; }
        [Field("Species", Ref = "species", Group = "Pokémon")] public string Species { get; set; }
        [Field("Level", Group = "Pokémon")] public int Level { get; set; }
        [Field("Form", Group = "Pokémon")] public int Form { get; set; }
        [Field("Shiny", Group = "Pokémon")] public bool Shiny { get; set; }
        [Field("Nature", Group = "Pokémon", Help = "Empty = random")] public string Nature { get; set; }
        [Field("Ability", Ref = "abilities", Group = "Pokémon", Help = "Empty = normal")] public string Ability { get; set; }
        [Field("Held item", Ref = "items", Group = "Pokémon")] public string HeldItem { get; set; }
        [Field("Moves", Ref = "moves", Group = "Pokémon", Help = "Empty = the usual moves for its level")] public List<string> Moves { get; set; }
        [Field("Original trainer", Group = "Pokémon", Help = "Empty = the player")] public string OTName { get; set; }
        [Field("Poké Ball", Ref = "items", Group = "Pokémon")] public string Ball { get; set; }
        [Field("Item", Ref = "items", Group = "Item")] public string Item { get; set; }
        [Field("Quantity", Group = "Item")] public int Quantity { get; set; }
        public MysteryGiftData() { Kind = "Pokemon"; Level = 5; Quantity = 1; Moves = new List<string>(); Ball = "CHERISHBALL"; }
        public override string DisplayName { get { return Id + ": " + (string.IsNullOrEmpty(Name) ? (Kind == "Item" ? Item : Species) : Name); } }
    }

    public class RegionalDex : GameRecord
    {
        [Field("Name")] public string Name { get; set; }
        [Field("Species", Ref = "species")] public List<string> Species { get; set; }
        [Field("Numbering starts at 0", Help = "The first species is #000 (Essentials' DEXES_WITH_OFFSETS), e.g. Victini in Unova.")] public bool StartsAtZero { get; set; }
        public RegionalDex() { Species = new List<string>(); }
        public override string DisplayName { get { return "Dex " + Id + (string.IsNullOrEmpty(Name) ? "" : ": " + Name); } }
    }
}
