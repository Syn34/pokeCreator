using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Pkmn;
using PokeCreator.Runtime.Platform;

namespace PokeCreator.Runtime.UI
{
    /// <summary>
    /// A screen shown on top of the map (menus, party, bag...) or instead of it (title).
    /// Scenes are stacked: only the top one receives input; the map is frozen underneath.
    /// </summary>
    public abstract class Scene
    {
        protected readonly Game Game;
        /// <summary>True if the scene covers the whole screen (the map isn't drawn behind it).</summary>
        public bool Opaque { get; protected set; }
        public bool Closed { get; private set; }
        protected int Frame;

        protected Scene(Game g) { Game = g; }

        public void Close() { Closed = true; OnClose(); var cb = OnClosed; OnClosed = null; if (cb != null) cb(); }
        /// <summary>Runs once when the scene closes (scripts waiting for a screen to finish).</summary>
        public Action OnClosed;
        protected virtual void OnClose() { }

        public void Tick() { Frame++; Update(); }
        protected abstract void Update();
        public abstract void Draw(Gfx g);

        protected void Cursor() { Game.Audio.PlaySe("GUI sel cursor"); }
        protected void Decide() { Game.Audio.PlaySe("GUI sel decision"); }
        protected void Cancel() { Game.Audio.PlaySe("GUI sel cancel"); }
        protected void Buzz() { Game.Audio.PlaySe("GUI sel buzzer"); }
    }

    /// <summary>Shared drawing helpers for menus.</summary>
    public static class Ui
    {
        public static readonly Color Dark = new Color(64, 64, 72);
        public static readonly Color DarkShadow = new Color(176, 176, 184);
        public static readonly Color Light = new Color(248, 248, 248);
        public static readonly Color LightShadow = new Color(80, 80, 96);
        public static readonly Color Male = new Color(24, 112, 216);
        public static readonly Color Female = new Color(232, 32, 16);

        public static void Box(Gfx g, int x, int y, int w, int h) { MessageWindow.DrawBox(g, x, y, w, h); }

        public static void Text(Gfx g, int x, int y, string s) { g.DrawText(x, y, s, Dark, DarkShadow); }
        public static void TextRight(Gfx g, int right, int y, string s) { g.DrawText(right - g.TextWidth(s), y, s, Dark, DarkShadow); }
        public static void TextCenter(Gfx g, int cx, int y, string s) { g.DrawText(cx - g.TextWidth(s) / 2, y, s, Dark, DarkShadow); }
        public static void LightText(Gfx g, int x, int y, string s) { g.DrawText(x, y, s, Light, LightShadow); }
        public static void LightTextRight(Gfx g, int right, int y, string s) { g.DrawText(right - g.TextWidth(s), y, s, Light, LightShadow); }

        /// <summary>Selection arrow (▶) pointing right at (x, y).</summary>
        public static void Arrow(Gfx g, int x, int y, Color? c = null)
        {
            if (Skin.Arrow(g, x, y)) return;
            var col = c ?? Dark;
            for (int r = 0; r < 8; r++) g.FillRect(x + r, y + r, 1, 16 - r * 2, col);
        }

        public static void Gradient(Gfx g, int x, int y, int w, int h, Color top, Color bottom)
        {
            for (int i = 0; i < h; i += 2)
            {
                double t = (double)i / Math.Max(1, h);
                g.FillRect(x, y + i, w, 2, new Color((byte)(top.R + (bottom.R - top.R) * t), (byte)(top.G + (bottom.G - top.G) * t), (byte)(top.B + (bottom.B - top.B) * t)));
            }
        }

        /// <summary>Rounded-looking panel (corner pixels trimmed).</summary>
        public static void Panel(Gfx g, int x, int y, int w, int h, Color fill, Color border)
        {
            g.FillRect(x + 2, y, w - 4, h, border);
            g.FillRect(x, y + 2, w, h - 4, border);
            g.FillRect(x + 1, y + 1, w - 2, h - 2, border);
            g.FillRect(x + 3, y + 2, w - 6, h - 4, fill);
            g.FillRect(x + 2, y + 3, w - 4, h - 6, fill);
        }

        public static void HpBar(Gfx g, int x, int y, int w, int hp, int max)
        {
            if (Skin.HpBar(g, x, y - 1, w, hp, max)) return;
            g.FillRect(x - 2, y - 2, w + 4, 10, new Color(72, 72, 80));
            g.FillRect(x, y, w, 6, new Color(40, 40, 48));
            double f = max <= 0 ? 0 : Math.Max(0, Math.Min(1, (double)hp / max));
            var c = f > 0.5 ? new Color(24, 192, 32) : f > 0.2 ? new Color(248, 184, 0) : new Color(248, 64, 48);
            g.FillRect(x, y, (int)Math.Ceiling(w * f), 6, c);
            g.FillRect(x, y, (int)Math.Ceiling(w * f), 2, new Color((byte)Math.Min(255, c.R + 60), (byte)Math.Min(255, c.G + 60), (byte)Math.Min(255, c.B + 60)));
        }

        public static void ExpBar(Gfx g, int x, int y, int w, double f)
        {
            if (Skin.ExpBar(g, x, y, w, f)) return;
            g.FillRect(x - 1, y - 1, w + 2, 6, new Color(72, 72, 80));
            g.FillRect(x, y, w, 4, new Color(40, 40, 48));
            g.FillRect(x, y, (int)(w * Math.Max(0, Math.Min(1, f))), 4, new Color(64, 200, 248));
        }

        static readonly Dictionary<string, Color> TypeColors = new Dictionary<string, Color>
        {
            { "NORMAL", new Color(168,168,120) }, { "FIRE", new Color(240,128,48) }, { "WATER", new Color(104,144,240) }, { "GRASS", new Color(120,200,80) },
            { "ELECTRIC", new Color(248,208,48) }, { "ICE", new Color(152,216,216) }, { "FIGHTING", new Color(192,48,40) }, { "POISON", new Color(160,64,160) },
            { "GROUND", new Color(224,192,104) }, { "FLYING", new Color(168,144,240) }, { "PSYCHIC", new Color(248,88,136) }, { "BUG", new Color(168,184,32) },
            { "ROCK", new Color(184,160,56) }, { "GHOST", new Color(112,88,152) }, { "DRAGON", new Color(112,56,248) }, { "DARK", new Color(112,88,72) },
            { "STEEL", new Color(184,184,208) }, { "FAIRY", new Color(238,153,172) },
        };

        public static Color TypeColor(string type) { Color c; return TypeColors.TryGetValue((type ?? "").ToUpperInvariant(), out c) ? c : new Color(104, 160, 144); }

        public const int BadgeWidth = 84;

        /// <summary>Coloured type label.</summary>
        public static void TypeBadge(Gfx g, Game game, int x, int y, string type)
        {
            var t0 = game.Project.Types.FirstOrDefault(tt => tt.Id == type);
            // Essentials' type icons (Graphics/UI/types: one 64×28 row per type, at the type's icon position)
            var icons = g.Load(game.Project.FindAsset("Graphics/UI", "types"));
            if (icons != null && t0 != null && icons.Width >= 64)
            {
                int rowH = 28, row = t0.IconPosition;
                if ((row + 1) * rowH <= icons.Height) { g.Draw(icons, 0, row * rowH, 64, rowH, x + (BadgeWidth - 64) / 2, y); return; }
            }
            var c = TypeColor(type);
            Panel(g, x, y, BadgeWidth, 28, c, new Color((byte)(c.R * 2 / 3), (byte)(c.G * 2 / 3), (byte)(c.B * 2 / 3)));
            var t = game.Project.Types.FirstOrDefault(tt => tt.Id == type);
            string name = t != null && !string.IsNullOrEmpty(t.Name) ? t.Name : (type ?? "");
            g.DrawText(x + BadgeWidth / 2 - g.TextWidth(name) / 2, y - 1, name, Light, new Color(40, 40, 48));
        }

        public static void GenderSymbol(Gfx g, int x, int y, string gender)
        {
            if (Skin.Gender(g, x, y + 8, gender)) return;
            if (gender == "M") g.DrawText(x, y, "♂", Male, DarkShadow);
            else if (gender == "F") g.DrawText(x, y, "♀", Female, DarkShadow);
        }

        /// <summary>Party icon (2-frame 128x64 sheet), animated.</summary>
        public static void PokemonIcon(Gfx g, Game game, Pokemon pk, int x, int y, int frame)
        {
            string folder = pk.Shiny ? "Graphics/Pokemon/Icons shiny" : "Graphics/Pokemon/Icons";
            string id = pk.IsEgg ? "EGG" : (pk.Form > 0 ? pk.Species + "_" + pk.Form : pk.Species);
            var path = game.Project.FindAsset(folder, id) ?? game.Project.FindAsset("Graphics/Pokemon/Icons", pk.Species) ?? game.Project.FindAsset("Graphics/Pokemon/Icons", "000");
            var tex = game.Gfx.Load(path);
            if (tex == null) { g.FillRect(x + 16, y + 16, 32, 32, new Color(200, 200, 210)); return; }
            int fw = tex.Width >= tex.Height * 2 ? tex.Width / 2 : tex.Width;
            int f = fw < tex.Width ? (frame / 15) % 2 : 0;
            g.Draw(tex, f * fw, 0, fw, tex.Height, x, y);
        }

        /// <summary>
        /// A Pokémon's battle sprite: its animated sprite (Graphics/Pokemon/Animated/...) when the game uses them and it has one,
        /// else the still sprite. frameW is the width of one picture (animated sprites are horizontal strips of square pictures).
        /// </summary>
        public static Texture PokemonSprite(Game game, Pokemon pk, bool back, out int frameW)
        {
            string id = pk.Form > 0 ? pk.Species + "_" + pk.Form : pk.Species;
            string path = null;
            if (game.Project.Settings.UseAnimatedSprites)
            {
                string af = "Graphics/Pokemon/Animated/" + (back ? "Back" : "Front");
                path = (pk.Shiny ? game.Project.FindAsset(af + " shiny", id) : null) ?? game.Project.FindAsset(af, id)
                    ?? (pk.Form > 0 ? (pk.Shiny ? game.Project.FindAsset(af + " shiny", pk.Species) : null) ?? game.Project.FindAsset(af, pk.Species) : null);
                // A shiny Pokémon without a shiny animation keeps its still shiny sprite rather than the normal colours
                if (path != null && pk.Shiny && !path.Replace('\\', '/').Contains("shiny/") && game.Project.FindAsset(back ? "Graphics/Pokemon/Back shiny" : "Graphics/Pokemon/Front shiny", id) != null) path = null;
            }
            if (path == null)
            {
                string folder = back ? (pk.Shiny ? "Graphics/Pokemon/Back shiny" : "Graphics/Pokemon/Back") : (pk.Shiny ? "Graphics/Pokemon/Front shiny" : "Graphics/Pokemon/Front");
                path = game.Project.FindAsset(folder, id) ?? game.Project.FindAsset(back ? "Graphics/Pokemon/Back" : "Graphics/Pokemon/Front", pk.Species);
            }
            var tex = game.Gfx.Load(path);
            frameW = tex == null ? 0 : tex.Width > tex.Height * 3 / 2 ? tex.Height : tex.Width;
            return tex;
        }

        /// <summary>Left edge of the picture to show now from a sprite strip (0 for still sprites).</summary>
        public static int SpriteFrameX(Game game, Texture tex, int frameW)
        {
            if (tex == null || frameW <= 0 || tex.Width < frameW * 2) return 0;
            int speed = game.Project.Settings.AnimatedSpriteSpeed > 0 ? game.Project.Settings.AnimatedSpriteSpeed : 3;
            int frames = tex.Width / frameW;
            return (game.FrameCount / speed) % frames * frameW;
        }

        public static void PokemonFront(Gfx g, Game game, Pokemon pk, int cx, int bottom, int maxSize)
        {
            int fw0;
            var tex = PokemonSprite(game, pk, false, out fw0);
            if (tex == null)
            {
                // No sprite yet: a grey placeholder so the screen isn't empty
                int sz = Math.Min(96, maxSize / 2);
                g.FillRect(cx - sz / 2, bottom - sz, sz, sz, new Color(200, 200, 208, 200));
                g.DrawText(cx - g.TextWidth("?") / 2, bottom - sz / 2 - g.LineHeight / 2, "?", new Color(96, 96, 104), new Color(232, 232, 240));
                return;
            }
            int fw = fw0;
            int scale = Math.Max(1, maxSize / Math.Max(fw, tex.Height));
            g.Draw(tex, SpriteFrameX(game, tex, fw), 0, fw, tex.Height, cx - fw * scale / 2, bottom - tex.Height * scale, fw * scale, tex.Height * scale);
        }

        public static void ItemIcon(Gfx g, Game game, string item, int x, int y)
        {
            var tex = game.Gfx.Load(game.Project.FindAsset("Graphics/Items", item) ?? game.Project.FindAsset("Graphics/Items", "000"));
            if (tex != null) g.Draw(tex, 0, 0, tex.Width, tex.Height, x, y);
        }

        public static string PlayTime(long frames)
        {
            long s = frames / 60;
            return (s / 3600) + ":" + ((s / 60) % 60).ToString("00");
        }

        /// <summary>Status (or FNT) as the custom icon, else as red text.</summary>
        public static void StatusMark(Gfx g, int x, int y, string status, bool fainted = false)
        {
            string code = Skin.StatusCode(status, fainted);
            if (code == "") return;
            if (Skin.Status(g, x, y + 6, code)) return;
            g.DrawText(x, y, code == "TOX" ? "PSN" : code, new Color(232, 64, 48), DarkShadow);
        }

        public static string StatusAbbrev(string status)
        {
            switch ((status ?? "").ToUpperInvariant()) { case "BURN": return "BRN"; case "POISON": return "PSN"; case "TOXIC": return "PSN"; case "PARALYSIS": return "PAR"; case "SLEEP": return "SLP"; case "FROZEN": return "FRZ"; }
            return "";
        }
    }

    /// <summary>A vertical command list in a box (used by the pause menu and small pop-up menus).</summary>
    public class CommandList
    {
        public List<string> Items;
        public int Index;
        public CommandList(IEnumerable<string> items) { Items = items.ToList(); }

        /// <summary>Returns the chosen index, -2 on cancel, -1 while still choosing.</summary>
        public int Update(Scene s, Action cursor)
        {
            if (Input.Repeat(Button.Down)) { Index = (Index + 1) % Items.Count; cursor(); }
            if (Input.Repeat(Button.Up)) { Index = (Index + Items.Count - 1) % Items.Count; cursor(); }
            if (Input.Trigger(Button.Action)) return Index;
            if (Input.Trigger(Button.Cancel)) return -2;
            return -1;
        }

        public int Width(Gfx g) { return Items.Max(i => g.TextWidth(i)) + 64; }
        public int Height(Gfx g) { return Items.Count * Math.Max(32, g.LineHeight + 4) + 24; }

        public void Draw(Gfx g, int x, int y)
        {
            int w = Width(g), h = Height(g), lh = Math.Max(32, g.LineHeight + 4);
            Ui.Box(g, x, y, w, h);
            for (int i = 0; i < Items.Count; i++)
            {
                if (i == Index) Ui.Arrow(g, x + 18, y + 14 + i * lh + 6);
                Ui.Text(g, x + 36, y + 12 + i * lh, Items[i]);
            }
        }
    }
}
