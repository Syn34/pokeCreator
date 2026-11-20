using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Pkmn;

namespace PokeCreator.Runtime.World
{
    public class StorageBox
    {
        public string Name { get; set; }
        public int Wallpaper { get; set; }
        /// <summary>30 places; null = empty.</summary>
        public List<Pokemon> Slots { get; set; }
        public StorageBox() { Slots = new List<Pokemon>(); }
        public void Fix() { if (Slots == null) Slots = new List<Pokemon>(); while (Slots.Count < PokemonStorage.BoxSize) Slots.Add(null); }
        public int Count { get { return Slots.Count(p => p != null); } }
    }

    /// <summary>
    /// The PC's Pokémon boxes (as Essentials): a number of boxes of 30 places, each with a name and wallpaper.
    /// New Pokémon go into the current box, or the next one with room.
    /// </summary>
    public class PokemonStorage : IEnumerable<Pokemon>
    {
        public const int BoxSize = 30;
        public static int DefaultBoxes = 40;
        public List<StorageBox> Boxes { get; private set; }
        public int Current;

        public PokemonStorage() { Boxes = new List<StorageBox>(); Ensure(DefaultBoxes); }

        public void Ensure(int n)
        {
            while (Boxes.Count < n) Boxes.Add(new StorageBox { Name = "Box " + (Boxes.Count + 1), Wallpaper = Boxes.Count % 16 });
            foreach (var b in Boxes) b.Fix();
            if (Current < 0 || Current >= Boxes.Count) Current = 0;
        }

        public int Count { get { return Boxes.Sum(b => b.Count); } }
        public int Capacity { get { return Boxes.Count * BoxSize; } }
        public bool Full { get { return Count >= Capacity; } }

        public Pokemon Get(int box, int slot) { return box >= 0 && box < Boxes.Count && slot >= 0 && slot < BoxSize ? Boxes[box].Slots[slot] : null; }
        public void Set(int box, int slot, Pokemon pk) { if (box >= 0 && box < Boxes.Count && slot >= 0 && slot < BoxSize) Boxes[box].Slots[slot] = pk; }

        /// <summary>Stores a Pokémon in the current box or the next with room; returns the box number (or -1 when every box is full).</summary>
        public int TryAdd(Pokemon pk)
        {
            for (int k = 0; k < Boxes.Count; k++)
            {
                int b = (Current + k) % Boxes.Count;
                int i = Boxes[b].Slots.IndexOf(null);
                if (i >= 0) { Boxes[b].Slots[i] = pk; return b; }
            }
            return -1;
        }

        /// <summary>Stores a Pokémon; if every box is full, another box is added so nothing is lost.</summary>
        public int Add(Pokemon pk)
        {
            int b = TryAdd(pk);
            if (b >= 0) return b;
            Ensure(Boxes.Count + 1);
            Boxes[Boxes.Count - 1].Slots[0] = pk;
            return Boxes.Count - 1;
        }

        public bool Remove(Pokemon pk)
        {
            foreach (var b in Boxes) { int i = b.Slots.IndexOf(pk); if (i >= 0) { b.Slots[i] = null; return true; } }
            return false;
        }

        public bool Contains(Pokemon pk) { return Boxes.Any(b => b.Slots.Contains(pk)); }

        public IEnumerator<Pokemon> GetEnumerator() { return Boxes.SelectMany(b => b.Slots).Where(p => p != null).ToList().GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        /// <summary>Saves from before boxes: a flat list fills the boxes in order.</summary>
        public static PokemonStorage FromList(List<Pokemon> flat)
        {
            var s = new PokemonStorage();
            if (flat != null) foreach (var pk in flat.Where(p => p != null)) s.Add(pk);
            return s;
        }

        public static PokemonStorage FromBoxes(List<StorageBox> boxes, int current)
        {
            var s = new PokemonStorage();
            if (boxes != null && boxes.Count > 0) { s.Boxes.Clear(); s.Boxes.AddRange(boxes); }
            s.Current = current;
            s.Ensure(Math.Max(DefaultBoxes, s.Boxes.Count));
            return s;
        }
    }
}
