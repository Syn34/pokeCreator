using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeCreator.Runtime.World
{
    public class FollowerInfo
    {
        public string Name { get; set; }
        public string CharacterName { get; set; }
        /// <summary>Common event run when the player talks to the follower (0 = none).</summary>
        public int CommonEvent { get; set; }
    }

    /// <summary>A character walking behind the player (Essentials' Followers / dependent events).</summary>
    public class FollowerCharacter : Character
    {
        public readonly FollowerInfo Info;
        public bool Hidden;
        public FollowerCharacter(Game g, FollowerInfo info) : base(g) { Info = info; CharacterName = info.CharacterName ?? ""; Through = true; }

        /// <summary>Takes one step to (x, y) if it's next to it, else appears there.</summary>
        public void StepTo(int x, int y)
        {
            int dx = x - X, dy = y - Y;
            if (Math.Abs(dx) + Math.Abs(dy) == 1)
            {
                int d = dx > 0 ? 6 : dx < 0 ? 4 : dy > 0 ? 2 : 8;
                Move(d);
            }
            else if (dx != 0 || dy != 0) { MoveTo(x, y); }
        }
    }

    /// <summary>
    /// NPCs that follow the player (Followers.add / remove / clear, pbAddDependency2): each walks where the one in front
    /// was; they go through doors with the player and come back out on the other side. Talking to one runs its common
    /// event. Saved with the game.
    /// </summary>
    public static class Followers
    {
        public static readonly List<FollowerCharacter> Characters = new List<FollowerCharacter>();

        static List<FollowerInfo> Infos(Game g)
        {
            if (g.State.Extras.Followers == null) g.State.Extras.Followers = new List<FollowerInfo>();
            return g.State.Extras.Followers;
        }

        /// <summary>Rebuilds the follower characters on the player's tile (new map, loaded game).</summary>
        public static void Rebuild(Game g)
        {
            Characters.Clear(); _pokemon = null; _pokemonKey = null;
            if (g.State == null || g.Player == null) return;
            foreach (var info in Infos(g))
            {
                var f = new FollowerCharacter(g, info);
                f.MoveTo(g.Player.X, g.Player.Y); f.Direction = g.Player.Direction;
                f.Hidden = true; // appears behind the player on its first step
                Characters.Add(f);
            }
        }

        public static void Add(Game g, GameEvent ev, string name, int commonEvent)
        {
            if (ev == null) return;
            var infos = Infos(g);
            infos.RemoveAll(i => i.Name == name);
            var info = new FollowerInfo { Name = string.IsNullOrEmpty(name) ? "Follower" + (infos.Count + 1) : name, CharacterName = ev.CharacterName, CommonEvent = commonEvent };
            infos.Add(info);
            var f = new FollowerCharacter(g, info);
            f.MoveTo(ev.X, ev.Y); f.Direction = ev.Direction;
            Characters.Add(f);
            ev.Erased = true; // the event becomes the follower
        }

        public static void Remove(Game g, string name)
        {
            Infos(g).RemoveAll(i => i.Name == name);
            Characters.RemoveAll(c => c.Info.Name == name);
        }

        public static void Clear(Game g) { Infos(g).Clear(); Characters.RemoveAll(c => c != _pokemon); }

        // ------------------------------------------------------------------ following Pokémon
        static FollowerCharacter _pokemon;
        static string _pokemonKey;

        /// <summary>Game Settings → Following Pokémon, unless the player or a script turned it off.</summary>
        public static bool PokemonEnabled(Game g) { return g.Project.Settings.FollowingPokemon && !g.State.Flags.Contains("following_pokemon_off"); }

        /// <summary>The overworld sprite of a Pokémon: Graphics/Characters/Followers (shiny) / SPECIES[_form].</summary>
        public static string PokemonSprite(Game g, PokeCreator.Pkmn.Pokemon pk)
        {
            string id = pk.Form > 0 ? pk.Species + "_" + pk.Form : pk.Species;
            foreach (var folder in pk.Shiny ? new[] { "Followers shiny", "Followers" } : new[] { "Followers" })
            {
                if (g.Project.FindAsset("Graphics/Characters/" + folder, id) != null) return folder + "/" + id;
                if (pk.Form > 0 && g.Project.FindAsset("Graphics/Characters/" + folder, pk.Species) != null) return folder + "/" + pk.Species;
            }
            return null;
        }

        /// <summary>Keeps the first healthy party Pokémon walking right behind the player (when it has an overworld sprite).</summary>
        public static void SyncPokemon(Game g)
        {
            if (g.State == null || g.Player == null || g.Project == null) return;
            var lead = PokemonEnabled(g) ? g.State.Party.FirstOrDefault(p => !p.IsEgg && !p.Fainted) : null;
            string sprite = lead != null ? PokemonSprite(g, lead) : null;
            if (sprite == null)
            {
                if (_pokemon != null) { Characters.Remove(_pokemon); _pokemon = null; _pokemonKey = null; }
                return;
            }
            if (_pokemon == null || !Characters.Contains(_pokemon))
            {
                _pokemon = new FollowerCharacter(g, new FollowerInfo { Name = "\u0001pokemon", CharacterName = sprite });
                _pokemon.MoveTo(g.Player.X, g.Player.Y); _pokemon.Direction = g.Player.Direction; _pokemon.Hidden = true;
                Characters.Insert(0, _pokemon);
            }
            if (_pokemonKey != sprite) { _pokemon.CharacterName = sprite; _pokemonKey = sprite; }
            // Not while surfing, diving or cycling
            if (g.Player.Surfing || g.Player.Diving || g.Player.Cycling) _pokemon.Hidden = true;
            _pokemon.StepAnime = true;
        }

        public static bool PokemonAt(int x, int y) { return _pokemon != null && !_pokemon.Hidden && Characters.Contains(_pokemon) && _pokemon.X == x && _pokemon.Y == y; }

        /// <summary>Talking to the following Pokémon: its cry and how it feels.</summary>
        static bool TalkToPokemon(Game g)
        {
            var lead = g.State.Party.FirstOrDefault(p => !p.IsEgg && !p.Fainted);
            if (lead == null) return false;
            _pokemon.TurnToward(g.Player);
            g.Audio.PlayCry(lead);
            string n = lead.Name(g.Project);
            int h = lead.Happiness;
            string text = h >= 250 ? n + " is very happy to be with you!" : h >= 200 ? n + " seems happy." : h >= 150 ? n + " is feeling fine."
                        : h >= 100 ? n + " is looking at you." : h >= 60 ? n + " is following along." : h >= 30 ? n + " doesn't seem to care much." : n + " is ignoring you...";
            if (lead.Status == "POISON" || lead.Status == "TOXIC") text = n + " is shivering with the effects of being poisoned.";
            else if (lead.HP < lead.TotalHP(g.Project) / 4) text = n + " looks tired.";
            g.Message.Show(text);
            return true;
        }

        /// <summary>The player stepped away from (fromX, fromY): the line moves up one place.</summary>
        public static void PlayerMoved(Game g, int fromX, int fromY)
        {
            int tx = fromX, ty = fromY;
            foreach (var f in Characters)
            {
                int ox = f.X, oy = f.Y;
                f.MoveSpeed = g.Player.MoveSpeed; f.SpeedScale = g.Player.SpeedScale;
                if (f.Hidden) { f.MoveTo(tx, ty); f.Hidden = false; }
                else f.StepTo(tx, ty);
                tx = ox; ty = oy;
            }
        }

        public static void Update() { foreach (var f in Characters) f.Update(); }

        public static IEnumerable<Character> Visible { get { return Characters.Where(c => !c.Hidden).Cast<Character>(); } }

        /// <summary>Talking to a follower: its common event.</summary>
        public static bool TalkAt(Game g, int x, int y)
        {
            if (_pokemon != null && !_pokemon.Hidden && _pokemon.X == x && _pokemon.Y == y && Characters.Contains(_pokemon)) return TalkToPokemon(g);
            var f = Characters.FirstOrDefault(c => !c.Hidden && c.X == x && c.Y == y && c.Info.CommonEvent > 0);
            if (f == null) return false;
            var ce = g.Project.CommonEvents.FirstOrDefault(c => c.Id == f.Info.CommonEvent);
            if (ce == null) return false;
            f.TurnToward(g.Player);
            g.RunCommonEvent(ce);
            return true;
        }

        /// <summary>Doors: followers walk into the player (follow_into_door), vanish (hide_followers), then reappear on the player after the transfer (put_followers_on_player).</summary>
        public static void IntoDoor(Game g) { foreach (var f in Characters) f.StepTo(g.Player.X, g.Player.Y); }
        public static void Hide() { foreach (var f in Characters) f.Hidden = true; }
        public static void PutOnPlayer(Game g) { foreach (var f in Characters) { f.MoveTo(g.Player.X, g.Player.Y); f.Direction = g.Player.Direction; f.Hidden = false; } }
    }
}
