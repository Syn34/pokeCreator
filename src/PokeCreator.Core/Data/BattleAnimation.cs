using System.Collections.Generic;
using System.Linq;
using PokeCreator.Json;

namespace PokeCreator.Data
{
    /// <summary>
    /// A battle animation in Essentials' format (PkmnAnimations.rxdata): frames of cels drawn from a 192×192 sheet in
    /// Graphics/Animations. Named "Move:TACKLE", "OppMove:TACKLE" (played when the foe uses it) or "Common:StatUp".
    /// </summary>
    public class BattleAnimation : GameRecord
    {
        // Indexes into a cel (27 values, as in Essentials' AnimFrame)
        public const int X = 0, Y = 1, ZoomX = 2, Angle = 3, Mirror = 4, Blend = 5, Visible = 6, Pattern = 7, Opacity = 8,
                         ZoomY = 11, ColorR = 12, ColorG = 13, ColorB = 14, ColorA = 15, ToneR = 16, ToneG = 17, ToneB = 18, ToneGray = 19,
                         Locked = 20, Priority = 25, Focus = 26, CelLength = 27;
        // Focus values
        public const int FocusTarget = 1, FocusUser = 2, FocusUserAndTarget = 3, FocusScreen = 4;
        // Where the editor puts the user and the target (Essentials' FOCUSUSER / FOCUSTARGET)
        public const int UserX = 128, UserY = 224, TargetX = 384, TargetY = 96;
        public const int CelSize = 192, SheetColumns = 5;

        [Field("Animation sheet", Asset = "Graphics/Animations", Help = "Image of 192×192 cells, 5 per row.")] public string Graphic { get; set; }
        [Field("Hue", Help = "Hue shift for the sheet (0-359).")] public int Hue { get; set; }
        [Field("Default focus", Help = "1 target, 2 user, 3 user and target, 4 screen.")] public int Position { get; set; }
        /// <summary>Each frame is a list of cels (null = unused); cel 0 is the user's sprite, cel 1 the target's.</summary>
        public List<List<int[]>> Frames { get; set; }
        public List<AnimationTiming> Timings { get; set; }

        public BattleAnimation() { Frames = new List<List<int[]>>(); Timings = new List<AnimationTiming>(); Position = 1; }

        [JsonIgnore] public int FrameCount { get { return Frames.Count; } }
        public override string DisplayName { get { return Id + "  (" + Frames.Count + " frames)"; } }
    }

    /// <summary>Something that happens on a frame of an animation: a sound effect and/or a flash.</summary>
    public class AnimationTiming
    {
        public int Frame { get; set; }
        /// <summary>0 = sound/flash. (Essentials' background and fog changes are kept but not shown.)</summary>
        public int Type { get; set; }
        public string Sound { get; set; }
        public int Volume { get; set; }
        public int Pitch { get; set; }
        /// <summary>0 none, 1 target, 2 user, 3 screen.</summary>
        public int FlashScope { get; set; }
        public int[] FlashColor { get; set; }
        public int FlashDuration { get; set; }
        public AnimationTiming() { Volume = 100; Pitch = 100; FlashColor = new[] { 255, 255, 255, 255 }; FlashDuration = 5; }
    }

    public static class BattleAnimations
    {
        /// <summary>Essentials' default animations by type for moves without their own: physical, special, status, then the same for moves hitting several targets.</summary>
        static readonly Dictionary<string, string[]> TypeDefaults = new Dictionary<string, string[]>
        {
            { "NORMAL",   new[] { "TACKLE", "SONICBOOM", "DEFENSECURL", "EXPLOSION", "SWIFT", "TAILWHIP" } },
            { "FIGHTING", new[] { "MACHPUNCH", "AURASPHERE", "DETECT", null, null, null } },
            { "FLYING",   new[] { "WINGATTACK", "GUST", "ROOST", null, "AIRCUTTER", "FEATHERDANCE" } },
            { "POISON",   new[] { "POISONSTING", "SLUDGE", "ACIDARMOR", null, "ACID", "POISONPOWDER" } },
            { "GROUND",   new[] { "SANDTOMB", "MUDSLAP", null, "EARTHQUAKE", "EARTHPOWER", "MUDSPORT" } },
            { "ROCK",     new[] { "ROCKTHROW", "POWERGEM", "ROCKPOLISH", "ROCKSLIDE", null, "SANDSTORM" } },
            { "BUG",      new[] { "TWINEEDLE", "BUGBUZZ", "QUIVERDANCE", null, "STRUGGLEBUG", "STRINGSHOT" } },
            { "GHOST",    new[] { "LICK", "SHADOWBALL", "GRUDGE", null, null, "CONFUSERAY" } },
            { "STEEL",    new[] { "IRONHEAD", "MIRRORSHOT", "IRONDEFENSE", null, null, "METALSOUND" } },
            { "FIRE",     new[] { "FIREPUNCH", "EMBER", "SUNNYDAY", null, "INCINERATE", "WILLOWISP" } },
            { "WATER",    new[] { "CRABHAMMER", "WATERGUN", "AQUARING", null, "SURF", "WATERSPORT" } },
            { "GRASS",    new[] { "VINEWHIP", "MEGADRAIN", "COTTONGUARD", "RAZORLEAF", null, "SPORE" } },
            { "ELECTRIC", new[] { "THUNDERPUNCH", "THUNDERSHOCK", "CHARGE", null, "DISCHARGE", "THUNDERWAVE" } },
            { "PSYCHIC",  new[] { "ZENHEADBUTT", "CONFUSION", "CALMMIND", null, "SYNCHRONOISE", "MIRACLEEYE" } },
            { "ICE",      new[] { "ICEPUNCH", "ICEBEAM", "MIST", null, "POWDERSNOW", "HAIL" } },
            { "DRAGON",   new[] { "DRAGONCLAW", "DRAGONRAGE", "DRAGONDANCE", null, "TWISTER", null } },
            { "DARK",     new[] { "PURSUIT", "DARKPULSE", "HONECLAW", null, "SNARL", "EMBARGO" } },
            { "FAIRY",    new[] { "TACKLE", "FAIRYWIND", "MOONLIGHT", null, "SWIFT", "SWEETKISS" } },
        };

        /// <summary>
        /// The animation for a move: "OppMove:X" when the foe uses it (if there is one), else "Move:X", else the type's default.
        /// swapRoles = an OppMove animation: as in Essentials it is played with the user and target swapped, because it was
        /// drawn with the foe (the user) where the target normally is.
        /// </summary>
        public static BattleAnimation ForMove(IList<BattleAnimation> all, MoveData md, bool userIsFoe, out bool swapRoles, int hit = 0, bool baseIfNoHit = true)
        {
            swapRoles = false;
            if (all == null || all.Count == 0 || md == null) return null;
            BattleAnimation a;
            // Extra hits (Fury Attack hit 2...) and charging turns ("Fly charging") have their own animations
            if (hit > 0)
            {
                a = Find(all, md.Id + ":" + hit, userIsFoe, out swapRoles);
                if (a != null || !baseIfNoHit) return a;
            }
            a = Find(all, md.Id, userIsFoe, out swapRoles);
            if (a != null) return a;
            string[] defs;
            if (!TypeDefaults.TryGetValue(md.Type ?? "NORMAL", out defs)) defs = TypeDefaults["NORMAL"];
            int kind = md.Category == "Physical" ? 0 : md.Category == "Special" ? 1 : 2;
            string tg = md.Target ?? "";
            if (tg == "AllNearFoes" || tg == "AllNearOthers" || tg == "AllFoes" || tg == "AllBattlers" || tg == "BothSides" || tg == "FoeSide") kind += 3;
            // As in Essentials: the type's default for this kind of move, then for the single-target kind, then Tackle
            if (defs[kind] != null && (a = Find(all, defs[kind], userIsFoe, out swapRoles)) != null) return a;
            if (kind >= 3 && defs[kind - 3] != null && (a = Find(all, defs[kind - 3], userIsFoe, out swapRoles)) != null) return a;
            return Find(all, "TACKLE", userIsFoe, out swapRoles);
        }

        static BattleAnimation Find(IList<BattleAnimation> all, string moveId, bool userIsFoe, out bool swapRoles)
        {
            swapRoles = false;
            if (userIsFoe)
            {
                var opp = all.FirstOrDefault(x => x.Id == "OppMove:" + moveId);
                if (opp != null) { swapRoles = true; return opp; }
            }
            return all.FirstOrDefault(x => x.Id == "Move:" + moveId);
        }

        public static BattleAnimation Common(IList<BattleAnimation> all, string name)
        {
            return all == null ? null : all.FirstOrDefault(x => x.Id == "Common:" + name);
        }
    }
}
