using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Pkmn;

namespace PokeCreator.Battles
{
    /// <summary>Mega Evolution, as in Essentials: once per side per battle, for a Pokémon holding its Mega Stone (or knowing its Mega move).</summary>
    public partial class Battle
    {
        /// <summary>Key items that let the player Mega Evolve (Essentials' Settings::MEGA_RINGS).</summary>
        public static readonly string[] MegaRings = { "MEGARING", "MEGABRACELET", "MEGACUFF", "MEGACHARM", "KEYSTONE" };
        /// <summary>Set by the game: whether the player has a Mega Ring in the Bag.</summary>
        public bool PlayerHasMegaRing;
        readonly bool[] _megaUsed = new bool[2];
        readonly Dictionary<Pokemon, int> _formBeforeMega = new Dictionary<Pokemon, int>();

        /// <summary>The Mega form this Pokémon can turn into, or null.</summary>
        public SpeciesData MegaForm(Pokemon pk)
        {
            if (pk == null || pk.IsEgg) return null;
            var cur = pk.Data(P);
            if (cur != null && (!string.IsNullOrEmpty(cur.MegaStone) || !string.IsNullOrEmpty(cur.MegaMove)) && pk.Form > 0) return null; // already Mega
            string prefix = pk.Species + "_";
            return P.Species.FirstOrDefault(s => s.Id.StartsWith(prefix) &&
                ((!string.IsNullOrEmpty(s.MegaStone) && s.MegaStone == pk.Item) || (!string.IsNullOrEmpty(s.MegaMove) && pk.Moves.Any(m => m.Id == s.MegaMove))));
        }

        public bool CanMegaEvolve(Battler b)
        {
            if (b == null || b.Pk.Fainted || _megaUsed[b.Side]) return false;
            if (b.Side == 0 && !PlayerHasMegaRing) return false;
            if (b.Side == 1 && IsWild) return false;
            return MegaForm(b.Pk) != null;
        }

        void MegaEvolve(Battler b)
        {
            var form = MegaForm(b.Pk);
            if (form == null || !CanMegaEvolve(b)) return;
            _megaUsed[b.Side] = true;
            string owner = b.Side == 0 ? PlayerName : TrainerLabel(b.PartyIndex);
            var stone = P.Items.FirstOrDefault(i => i.Id == b.Pk.Item);
            if (form.MegaMessage == 1) Say(owner + "'s fervent wish has reached " + b.Pk.Name(P) + "!");
            else Say(Name(b) + "'s " + (stone != null ? stone.Name : "Mega Stone") + " is reacting to " + owner + "'s " + (b.Side == 0 ? MegaRingName() : "Mega Ring") + "!");
            if (!_formBeforeMega.ContainsKey(b.Pk)) _formBeforeMega[b.Pk] = b.Pk.Form;
            string oldName = b.Pk.Name(P);
            int f; b.Pk.Form = int.TryParse(form.Id.Substring(b.Pk.Species.Length + 1), out f) ? f : 1;
            _ev.Add(new BattleEvent { Kind = BattleEventKind.FormChange, Side = b.Side, Party = b.PartyIndex, Slot = b.Slot, Text = "Mega" });
            Say(Name(b) + " has Mega Evolved into " + (string.IsNullOrEmpty(form.FormName) ? "Mega " + oldName : form.FormName) + "!");
            b.AbilityOverride = null;
            SwitchInAbility(b);
        }

        string MegaRingName()
        {
            foreach (var r in MegaRings) { var it = P.Items.FirstOrDefault(i => i.Id == r); if (it != null && (BagHas == null || BagHas(r))) return it.Name; }
            return "Mega Ring";
        }

        /// <summary>Optional: lets the battle check the player's Bag (for the Mega Ring's name).</summary>
        public Func<string, bool> BagHas;

        /// <summary>The AI Mega Evolves as soon as it can.</summary>
        bool AIWantsMega(Battler b) { return CanMegaEvolve(b); }

        /// <summary>Changes a battler's form for the rest of the battle (form-changing abilities); reverted at the end.</summary>
        bool ChangeForm(Battler b, int form, string message)
        {
            if (b == null || b.Pk.Fainted || b.Pk.Form == form) return false;
            if (P.Species.FirstOrDefault(s => s.Id == b.Pk.Species + (form > 0 ? "_" + form : "")) == null && form > 0) return false;
            if (!_formBeforeMega.ContainsKey(b.Pk)) _formBeforeMega[b.Pk] = b.Pk.Form;
            int oldMax = b.Pk.TotalHP(P);
            b.Pk.Form = form;
            int newMax = b.Pk.TotalHP(P);
            if (newMax != oldMax) { int f = b.Pk.HP; b.Pk.HP = Math.Max(1, Math.Min(newMax, b.Pk.HP + newMax - oldMax)); Hp(b, f); }
            Splash(b);
            _ev.Add(new BattleEvent { Kind = BattleEventKind.FormChange, Side = b.Side, Party = b.PartyIndex, Slot = b.Slot, Text = "Form" });
            if (!string.IsNullOrEmpty(message)) Say(message);
            return true;
        }

        /// <summary>Mega Pokémon go back to their normal form when the battle ends.</summary>
        public void RevertForms()
        {
            foreach (var kv in _formBeforeMega) kv.Key.Form = kv.Value;
            _formBeforeMega.Clear();
            RestoreItems();
        }
    }
}
