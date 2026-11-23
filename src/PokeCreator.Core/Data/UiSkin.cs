using System;
using System.Collections.Generic;
using PokeCreator.Json;

namespace PokeCreator.Data
{
    /// <summary>
    /// Custom UI pictures for battles and menus (Data/ui.json; Studio: Game Settings → Appearance (Game UI)).
    /// Every picture is optional: anything left empty is drawn the built-in way. Pictures come from Graphics/UI
    /// (sub-folders allowed, e.g. "BW2 Battle UI/Databoxes/databox_player"). Positions inside a data box are in the
    /// picture's own pixels (before Picture scale); positions on the screen are screen pixels.
    /// </summary>
    public class UiSkin
    {
        // ------------------------------------------------------------------ general
        [Field("Picture scale", Group = "General", Help = "How many times bigger the pictures (and the positions inside a data box) are drawn: 2 for DS-size pieces such as BW2's, 1 for pictures made at the game's size. 0 = 1.")]
        public int Scale { get; set; }
        [Field("Text colour", Group = "General", Help = "Colour of names and numbers drawn on custom data boxes, as #RRGGBB (empty = dark grey).")]
        public string TextColor { get; set; }
        [Field("Text shadow colour", Group = "General", Help = "#RRGGBB (empty = light grey).")]
        public string TextShadow { get; set; }

        // ------------------------------------------------------------------ battle start
        public static readonly string[] IntroStyles = { "Slide in", "Fade in", "Appear" };
        public static readonly string[] ExitStyles = { "Slide off the screen", "Fade out", "Disappear" };
        public static readonly string[] SendOutStyles = { "Poké Ball", "Fade in", "Flash of light", "Battle animation", "Appear" };

        [Field("Battle intro", Group = "Battle start", Choices = new[] { "Slide in", "Fade in", "Appear" }, Help = "How the Pokémon / trainers come on screen when a battle starts (empty = Slide in).")]
        public string IntroStyle { get; set; }
        [Field("Hide your trainer", Group = "Battle start", Help = "Ticked: your trainer isn't shown at the start. Unticked: your trainer's back sprite (Graphics/Trainers/<trainer type>_back, frames side by side) stands on your side until your first Pokémon comes out.")]
        public bool HidePlayerTrainer { get; set; }
        [Field("Trainers leave by", Group = "Battle start", Choices = new[] { "Slide off the screen", "Fade out", "Disappear" }, Help = "How the opposing trainer and your trainer leave before their Pokémon come out: Slide off the screen (Gen 3-4), Fade out (Gen 5) or Disappear (empty = Slide off the screen).")]
        public string TrainerExit { get; set; }
        [Field("Pokémon come out with", Group = "Battle start", Choices = new[] { "Poké Ball", "Fade in", "Flash of light", "Battle animation", "Appear" }, Help = "Poké Ball: its own ball (item icon, if there is one) is thrown and opens; Fade in; Flash of light; Battle animation (pick it below); Appear (empty = Poké Ball). Wild Pokémon just appear.")]
        public string SendOutStyle { get; set; }
        [Field("Send-out animation", Ref = "animations", Group = "Battle start", Help = "With \"Battle animation\": the animation played on the place the Pokémon comes out (Database → Battle Animations).")]
        public string SendOutAnimation { get; set; }
        [Field("Send-out sound", Asset = "Audio/SE", Group = "Battle start", Help = "Plays as each Pokémon comes out (empty = Battle recall).")]
        public string SendOutSE { get; set; }

        // ------------------------------------------------------------------ data boxes
        [Field("Foe data box", Asset = "Graphics/UI", Group = "Battle: data boxes", Help = "Picture behind the opposing Pokémon's name, level and HP bar.")]
        public string FoeBox { get; set; }
        [Field("Foe box position (x,y)", Group = "Battle: data boxes", Help = "Top-left corner on the screen, in screen pixels (empty = 16,30). In double / triple battles the next boxes go below it."), JsonCompact]
        public List<int> FoeBoxPos { get; set; }
        [Field("Foe data box (doubles)", Asset = "Graphics/UI", Group = "Battle: data boxes", Help = "Smaller box for double and triple battles (empty = the foe data box).")]
        public string FoeBoxSmall { get; set; }
        [Field("Player data box", Asset = "Graphics/UI", Group = "Battle: data boxes", Help = "Picture behind your Pokémon's name, level, HP bar, HP numbers and EXP bar.")]
        public string PlayerBox { get; set; }
        [Field("Player box position (x,y)", Group = "Battle: data boxes", Help = "Top-left corner on the screen (empty = bottom right, above the menu)."), JsonCompact]
        public List<int> PlayerBoxPos { get; set; }
        [Field("Player data box (doubles)", Asset = "Graphics/UI", Group = "Battle: data boxes", Help = "Smaller box for double and triple battles, without HP numbers (empty = the player data box).")]
        public string PlayerBoxSmall { get; set; }

        [Field("Foe: name (x,y)", Group = "Battle: positions in the foe box", Help = "Where the name starts, in the box picture's pixels. Can be above the box (negative y)."), JsonCompact] public List<int> FoeName { get; set; }
        [Field("Foe: level (x,y)", Group = "Battle: positions in the foe box", Help = "Where the level number starts."), JsonCompact] public List<int> FoeLevel { get; set; }
        [Field("Foe: HP bar (x,y)", Group = "Battle: positions in the foe box", Help = "Top-left corner of the HP bar frame."), JsonCompact] public List<int> FoeHpBar { get; set; }
        [Field("Foe: status (x,y)", Group = "Battle: positions in the foe box", Help = "Status icon (sleep, poison...)."), JsonCompact] public List<int> FoeStatus { get; set; }
        [Field("Foe: caught mark (x,y)", Group = "Battle: positions in the foe box", Help = "Ball shown on wild Pokémon you've caught before."), JsonCompact] public List<int> FoeCaught { get; set; }

        [Field("Player: name (x,y)", Group = "Battle: positions in the player box"), JsonCompact] public List<int> PlayerName { get; set; }
        [Field("Player: level (x,y)", Group = "Battle: positions in the player box"), JsonCompact] public List<int> PlayerLevel { get; set; }
        [Field("Player: HP bar (x,y)", Group = "Battle: positions in the player box"), JsonCompact] public List<int> PlayerHpBar { get; set; }
        [Field("Player: HP numbers (x,y)", Group = "Battle: positions in the player box", Help = "Right edge and top of the \"cur/max\" HP numbers."), JsonCompact] public List<int> PlayerHpNumbers { get; set; }
        [Field("Player: EXP bar (x,y)", Group = "Battle: positions in the player box"), JsonCompact] public List<int> PlayerExpBar { get; set; }
        [Field("Player: status (x,y)", Group = "Battle: positions in the player box"), JsonCompact] public List<int> PlayerStatus { get; set; }

        // ------------------------------------------------------------------ bars
        [Field("HP bar frame", Asset = "Graphics/UI", Group = "Battle: HP and EXP bars", Help = "The empty bar's outline; the fill is drawn centred inside it. Also used on the party screen.")] public string HpFrame { get; set; }
        [Field("HP fill: empty", Asset = "Graphics/UI", Group = "Battle: HP and EXP bars", Help = "Drawn under the coloured fill (the lost HP).")] public string HpEmpty { get; set; }
        [Field("HP fill: green", Asset = "Graphics/UI", Group = "Battle: HP and EXP bars", Help = "More than half the HP left.")] public string HpGreen { get; set; }
        [Field("HP fill: yellow", Asset = "Graphics/UI", Group = "Battle: HP and EXP bars", Help = "A fifth to a half left.")] public string HpYellow { get; set; }
        [Field("HP fill: red", Asset = "Graphics/UI", Group = "Battle: HP and EXP bars", Help = "Less than a fifth left.")] public string HpRed { get; set; }
        [Field("EXP bar frame", Asset = "Graphics/UI", Group = "Battle: HP and EXP bars")] public string ExpFrame { get; set; }
        [Field("EXP fill: empty", Asset = "Graphics/UI", Group = "Battle: HP and EXP bars")] public string ExpEmpty { get; set; }
        [Field("EXP fill", Asset = "Graphics/UI", Group = "Battle: HP and EXP bars")] public string ExpFill { get; set; }

        // ------------------------------------------------------------------ icons and numbers
        [Field("Status icons folder", Asset = "Graphics/UI", AssetFolder = true, Group = "Battle: icons and numbers", Help = "Folder with status_SLP, status_PSN, status_TOX, status_BRN, status_PAR, status_FRZ, status_FNT (and status_PKRS) pictures. Used in battle and on the party screen.")] public string StatusFolder { get; set; }
        [Field("Number pictures folder", Asset = "Graphics/UI", AssetFolder = true, Group = "Battle: icons and numbers", Help = "Folder with digit_0 … digit_9 pictures, used for levels and HP numbers on custom data boxes (empty = the game font).")] public string DigitsFolder { get; set; }
        [Field("HP slash", Asset = "Graphics/UI", Group = "Battle: icons and numbers", Help = "The \"/\" between current and maximum HP when number pictures are used.")] public string HpSlash { get; set; }
        [Field("\"Lv\" picture", Asset = "Graphics/UI", Group = "Battle: icons and numbers", Help = "Drawn before the level number (leave empty if the data box picture already shows Lv).")] public string LvIcon { get; set; }
        [Field("Male symbol", Asset = "Graphics/UI", Group = "Battle: icons and numbers")] public string Male { get; set; }
        [Field("Female symbol", Asset = "Graphics/UI", Group = "Battle: icons and numbers")] public string Female { get; set; }
        [Field("Caught mark", Asset = "Graphics/UI", Group = "Battle: icons and numbers")] public string Caught { get; set; }
        [Field("Party ball: OK", Asset = "Graphics/UI", Group = "Battle: icons and numbers", Help = "Party line-up at the start of a trainer battle: a Pokémon that can fight.")] public string BallOk { get; set; }
        [Field("Party ball: status", Asset = "Graphics/UI", Group = "Battle: icons and numbers", Help = "A Pokémon with a status problem.")] public string BallStatus { get; set; }
        [Field("Party ball: fainted", Asset = "Graphics/UI", Group = "Battle: icons and numbers")] public string BallFainted { get; set; }
        [Field("Party ball: empty", Asset = "Graphics/UI", Group = "Battle: icons and numbers", Help = "An empty party place.")] public string BallEmpty { get; set; }

        // ------------------------------------------------------------------ battle menus
        [Field("Battle message box", Asset = "Graphics/UI", Group = "Battle: menus", Help = "Behind battle messages, across the bottom of the screen (drawn at Picture scale, bottom-aligned).")] public string BattleMessage { get; set; }
        [Field("Command menu", Asset = "Graphics/UI", Group = "Battle: menus", Help = "Behind \"What will … do?\" and Fight / Bag / Pokémon / Run, across the bottom of the screen.")] public string CommandMenu { get; set; }
        [Field("Move menu", Asset = "Graphics/UI", Group = "Battle: menus", Help = "Behind the four moves and their PP / type, across the bottom of the screen.")] public string FightMenu { get; set; }

        // ------------------------------------------------------------------ menus
        [Field("Selection arrow", Asset = "Graphics/UI", Group = "Menus", Help = "Replaces the ▶ cursor in every menu.")] public string Arrow { get; set; }
        [Field("Party screen background", Asset = "Graphics/UI", Group = "Menus", Help = "Stretched to fill the screen.")] public string PartyBg { get; set; }
        [Field("Bag background", Asset = "Graphics/UI", Group = "Menus")] public string BagBg { get; set; }
        [Field("Summary background", Asset = "Graphics/UI", Group = "Menus")] public string SummaryBg { get; set; }
        [Field("Pokédex background", Asset = "Graphics/UI", Group = "Menus")] public string DexBg { get; set; }
        [Field("Trainer card background", Asset = "Graphics/UI", Group = "Menus")] public string TrainerCardBg { get; set; }
        [Field("Other menus background", Asset = "Graphics/UI", Group = "Menus", Help = "Options, Controls and name entry.")] public string MenuBg { get; set; }

        /// <summary>Picture scale (at least 1).</summary>
        public int S { get { return Scale < 1 ? 1 : Scale; } }

        /// <summary>A position field, or the default when it's not set.</summary>
        public static int[] Pos(List<int> v, int dx, int dy) { return v != null && v.Count >= 2 ? new[] { v[0], v[1] } : new[] { dx, dy }; }

        /// <summary>Parses "#RRGGBB" (null if empty or not a colour).</summary>
        public static int[] Rgb(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            string h = hex.Trim().TrimStart('#');
            if (h.Length != 6) return null;
            try { return new[] { Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16) }; }
            catch (FormatException) { return null; }
        }

        public static readonly string[] StatusCodes = { "SLP", "PSN", "TOX", "BRN", "PAR", "FRZ", "FNT", "PKRS" };
    }
}
