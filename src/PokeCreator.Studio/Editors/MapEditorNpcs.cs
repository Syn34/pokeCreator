using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PokeCreator.Data;
using PokeCreator.Maps;

namespace PokeCreator.Studio.Editors
{
    /// <summary>Ready-made service NPCs and Pokémon events for the map's right-click menu.</summary>
    public partial class MapCanvas
    {
        /// <summary>Builds the "New Pokémon Event / Service NPC" sub-menu.</summary>
        ToolStripMenuItem NpcMenu(Point cell)
        {
            var m = new ToolStripMenuItem("New Service NPC / Pokémon");
            m.DropDownItems.Add("Starter Pokémon choice (three Poké Balls)…", null, (s, a) => QuickStarters(cell));
            m.DropDownItems.Add("Gift Pokémon (given once)…", null, (s, a) => QuickGift(cell));
            m.DropDownItems.Add("Static Pokémon (battle once, e.g. a legendary)…", null, (s, a) => QuickStatic(cell));
            m.DropDownItems.Add("In-game Trade…", null, (s, a) => QuickTrade(cell));
            m.DropDownItems.Add(new ToolStripSeparator());
            m.DropDownItems.Add("Move Relearner", null, (s, a) => QuickSimple(cell, "Move Relearner", TemplateSprite("NPC 05"), MoveRelearnerList()));
            m.DropDownItems.Add("Move Deleter", null, (s, a) => QuickSimple(cell, "Move Deleter", TemplateSprite("NPC 01"), MoveDeleterList()));
            m.DropDownItems.Add("Name Rater", null, (s, a) => QuickSimple(cell, "Name Rater", TemplateSprite("NPC 02"), NameRaterList()));
            return m;
        }

        string PickSpecies(string title)
        {
            var list = _p.Species.Where(s => s.Form == 0).ToList();
            if (list.Count == 0) { MessageBox.Show(FindForm(), "There are no Pokémon in the database yet.", title); return null; }
            int k = ChoiceDialog.Ask(FindForm(), title, list.Select(s => s.Name + "  (" + s.Id + ")").ToArray(), true);
            return k < 0 ? null : list[k].Id;
        }

        static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }

        /// <summary>A second page shown once self switch A is on (after the gift / battle / trade).</summary>
        static EventPage DonePage(string sprite, string text)
        {
            var p2 = new EventPage();
            p2.Condition.SelfSwitchValid = true; p2.Condition.SelfSwitchCh = "A";
            p2.Graphic.CharacterName = sprite;
            p2.Trigger = Triggers.Action;
            p2.List = text == null ? new List<EventCommand> { new EventCommand(0, 0) }
                : new List<EventCommand> { new EventCommand(101, 0, text), new EventCommand(0, 0) };
            return p2;
        }

        /// <summary>The first unnamed switch (named for its new use), or 0 if all are taken.</summary>
        int FreeSwitch(string name)
        {
            var sw = _p.System.Switches;
            for (int i = 1; i < sw.Count; i++) if (string.IsNullOrEmpty(sw[i])) { sw[i] = name; return i; }
            if (sw.Count == 0) sw.Add("");
            sw.Add(name); return sw.Count - 1;
        }

        /// <summary>Three Poké Balls in a row (from this tile to the right): each asks "Do you choose X?", gives it, and the choice is final.</summary>
        void QuickStarters(Point cell)
        {
            var picks = new List<string>();
            foreach (var label in new[] { "first", "second", "third" })
            {
                string sp = PickSpecies("Starter choice: the " + label + " Poké Ball's Pokémon?");
                if (sp == null) return;
                picks.Add(sp);
            }
            int lv = NumberDialog.Ask(FindForm(), "Starter level?", 5, 1);
            if (lv < 1) return;
            int sw = FreeSwitch("Starter chosen");
            int var = 0;
            var vars = _p.System.Variables;
            for (int i = 1; i < vars.Count && var == 0; i++) if (string.IsNullOrEmpty(vars[i])) { vars[i] = "Starter choice"; var = i; }
            if (var == 0) { vars.Add("Starter choice"); var = vars.Count - 1; }
            Json.Json.ToFile(_p.DataFile("system.json"), _p.System);
            for (int k = 0; k < 3; k++)
            {
                var at = new Point(cell.X + k, cell.Y);
                if (at.X >= _map.Width) break;
                string sp = picks[k];
                string name = _p.Species.Where(s => s.Id == sp).Select(s => s.Name).FirstOrDefault() ?? sp;
                var ev = NewEvent(at); ev.Name = "Starter " + sp;
                var pg = ev.Pages[0];
                pg.Graphic.CharacterName = "Object ball"; pg.Trigger = Triggers.Action;
                pg.List = new List<EventCommand>
                {
                    new EventCommand(355, 0, "Pokemon.play_cry(:" + sp + ")"),
                    new EventCommand(101, 0, "Do you choose " + name + "?"),
                    new EventCommand(102, 0, new List<object> { "Yes", "No" }, 2),
                    new EventCommand(402, 0, 0, "Yes"),
                    new EventCommand(111, 1, 12, "pbAddPokemon(:" + sp + ", " + lv + ")"),
                    new EventCommand(122, 2, var, var, 0, 0, k + 1),
                    new EventCommand(121, 2, sw, sw, 0),
                    new EventCommand(123, 2, "A", 0),
                    new EventCommand(0, 2),
                    new EventCommand(412, 1),
                    new EventCommand(0, 1),
                    new EventCommand(402, 0, 1, "No"),
                    new EventCommand(0, 1),
                    new EventCommand(404, 0),
                    new EventCommand(0, 0)
                };
                // After choosing: the chosen ball is gone, the others are the professor's
                var p2 = new EventPage();
                p2.Condition.Switch1Valid = true; p2.Condition.Switch1Id = sw;
                p2.Graphic.CharacterName = "Object ball"; p2.Trigger = Triggers.Action;
                p2.List = new List<EventCommand> { new EventCommand(101, 0, "That's a Poké Ball the Professor is keeping."), new EventCommand(0, 0) };
                ev.Pages.Add(p2);
                var p3 = new EventPage();
                p3.Condition.SelfSwitchValid = true; p3.Condition.SelfSwitchCh = "A";
                p3.List = new List<EventCommand> { new EventCommand(0, 0) };
                ev.Pages.Add(p3);
            }
            Invalidate();
        }

        void QuickGift(Point cell)
        {
            string sp = PickSpecies("Gift Pokémon: which Pokémon?");
            if (sp == null) return;
            int lv = NumberDialog.Ask(FindForm(), "At what level?", 5, 1);
            if (lv < 1) return;
            string say = TextDialog.Ask(FindForm(), "What does the giver say first?", "I'd like you to take care of this Pokémon.", true);
            if (say == null) return;
            var ev = NewEvent(cell); ev.Name = "Gift " + sp;
            var pg = ev.Pages[0];
            string sprite = TemplateSprite("NPC 03");
            pg.Graphic.CharacterName = sprite; pg.Trigger = Triggers.Action;
            pg.List = new List<EventCommand>
            {
                new EventCommand(101, 0, say),
                new EventCommand(111, 0, 12, "pbAddPokemon(:" + sp + ", " + lv + ")"),
                new EventCommand(123, 1, "A", 0),
                new EventCommand(0, 1),
                new EventCommand(411, 0),
                new EventCommand(101, 1, "Come back when you have room for it."),
                new EventCommand(0, 1),
                new EventCommand(412, 0),
                new EventCommand(0, 0)
            };
            ev.Pages.Add(DonePage(sprite, "Take good care of it!"));
            _ed.EditEvent(ev);
        }

        void QuickStatic(Point cell)
        {
            string sp = PickSpecies("Static Pokémon: which Pokémon?");
            if (sp == null) return;
            int lv = NumberDialog.Ask(FindForm(), "At what level?", 50, 1);
            if (lv < 1) return;
            var ev = NewEvent(cell); ev.Name = "Static " + sp;
            var pg = ev.Pages[0];
            pg.Trigger = Triggers.Action;
            pg.List = new List<EventCommand>
            {
                new EventCommand(355, 0, "Pokemon.play_cry(:" + sp + ")"),
                new EventCommand(101, 0, "Gyaoo!"),
                // Variable 1 gets the outcome: 1 won, 2 lost, 3 ran, 4 caught
                new EventCommand(355, 0, "pbWildBattle(:" + sp + ", " + lv + ", 1)"),
                new EventCommand(111, 0, 12, "pbGet(1) == 1 || pbGet(1) == 4"),
                new EventCommand(123, 1, "A", 0),
                new EventCommand(0, 1),
                new EventCommand(412, 0),
                new EventCommand(0, 0)
            };
            ev.Pages.Add(DonePage("", null));
            _ed.EditEvent(ev);
        }

        void QuickTrade(Point cell)
        {
            string want = PickSpecies("In-game trade: which Pokémon does the NPC want?");
            if (want == null) return;
            string give = PickSpecies("…and which Pokémon do they give?");
            if (give == null) return;
            string nick = Prompt.Ask(FindForm(), "In-game trade", "Nickname of the Pokémon they give:", _p.Species.Where(s => s.Id == give).Select(s => s.Name).FirstOrDefault() ?? give);
            if (nick == null) return;
            string trainer = Prompt.Ask(FindForm(), "In-game trade", "Name of its original trainer:", "Mathew");
            if (trainer == null) return;
            string wantName = _p.Species.Where(s => s.Id == want).Select(s => s.Name).FirstOrDefault() ?? want;
            string giveName = _p.Species.Where(s => s.Id == give).Select(s => s.Name).FirstOrDefault() ?? give;
            var ev = NewEvent(cell); ev.Name = "Trade " + want + " for " + give;
            var pg = ev.Pages[0];
            string sprite = TemplateSprite("NPC 04");
            pg.Graphic.CharacterName = sprite; pg.Trigger = Triggers.Action;
            pg.List = new List<EventCommand>
            {
                new EventCommand(101, 0, "I'm looking for a " + wantName + ". Would you trade one for my " + giveName + "?"),
                new EventCommand(102, 0, new List<object> { "Yes", "No" }, 2),
                new EventCommand(402, 0, 0, "Yes"),
                new EventCommand(355, 1, "pbChoosePokemonForTrade(1, 3, :" + want + ")"),
                new EventCommand(111, 1, 12, "pbGet(1) >= 0"),
                new EventCommand(355, 2, "pbStartTrade(pbGet(1), :" + give + ", \"" + Esc(nick) + "\", \"" + Esc(trainer) + "\")"),
                new EventCommand(101, 2, "Thanks! I'll take good care of your " + wantName + "!"),
                new EventCommand(123, 2, "A", 0),
                new EventCommand(0, 2),
                new EventCommand(411, 1),
                new EventCommand(101, 2, "Oh, you don't want to? Too bad..."),
                new EventCommand(0, 2),
                new EventCommand(412, 1),
                new EventCommand(0, 1),
                new EventCommand(402, 0, 1, "No"),
                new EventCommand(101, 1, "Oh, you don't want to? Too bad..."),
                new EventCommand(0, 1),
                new EventCommand(404, 0),
                new EventCommand(0, 0)
            };
            ev.Pages.Add(DonePage(sprite, "How is my old " + giveName + " doing?"));
            _ed.EditEvent(ev);
        }

        static List<EventCommand> MoveRelearnerList()
        {
            return new List<EventCommand>
            {
                new EventCommand(101, 0, "I'm the Move Relearner. I can make your Pokémon remember a move it has forgotten. Shall I?"),
                new EventCommand(102, 0, new List<object> { "Yes", "No" }, 2),
                new EventCommand(402, 0, 0, "Yes"),
                new EventCommand(101, 1, "Which Pokémon needs tutoring?"),
                new EventCommand(355, 1, "pbChoosePokemon(1, 3, proc { |pkmn| pkmn.can_relearn_move? }, true)"),
                new EventCommand(111, 1, 12, "pbGet(1) >= 0"),
                new EventCommand(355, 2, "pbRelearnMoveScreen(pbGetPokemon(1))"),
                new EventCommand(0, 2),
                new EventCommand(412, 1),
                new EventCommand(0, 1),
                new EventCommand(402, 0, 1, "No"),
                new EventCommand(101, 1, "Come again any time."),
                new EventCommand(0, 1),
                new EventCommand(404, 0),
                new EventCommand(0, 0)
            };
        }

        static List<EventCommand> MoveDeleterList()
        {
            return new List<EventCommand>
            {
                new EventCommand(101, 0, "Uh... Oh, yes, I'm the Move Deleter. I can make Pokémon forget their moves. Would you like me to?"),
                new EventCommand(102, 0, new List<object> { "Yes", "No" }, 2),
                new EventCommand(402, 0, 0, "Yes"),
                new EventCommand(101, 1, "Which Pokémon should forget a move?"),
                new EventCommand(355, 1, "pbChooseNonEggPokemon(1, 3)"),
                new EventCommand(111, 1, 12, "pbGet(1) >= 0"),
                new EventCommand(111, 2, 12, "pbGetPokemon(1).numMoves <= 1"),
                new EventCommand(101, 3, "\\v[3] seems to know only one move..."),
                new EventCommand(0, 3),
                new EventCommand(411, 2),
                new EventCommand(355, 3, "pbChooseMove(pbGetPokemon(1), 2, 4)"),
                new EventCommand(111, 3, 12, "pbGet(2) >= 0"),
                new EventCommand(355, 4, "pbGetPokemon(1).forget_move_at_index(pbGet(2))"),
                new EventCommand(101, 4, "Done! \\v[3] has forgotten \\v[4] completely."),
                new EventCommand(0, 4),
                new EventCommand(412, 3),
                new EventCommand(0, 3),
                new EventCommand(412, 2),
                new EventCommand(0, 2),
                new EventCommand(412, 1),
                new EventCommand(0, 1),
                new EventCommand(402, 0, 1, "No"),
                new EventCommand(101, 1, "Come again any time."),
                new EventCommand(0, 1),
                new EventCommand(404, 0),
                new EventCommand(0, 0)
            };
        }

        static List<EventCommand> NameRaterList()
        {
            return new List<EventCommand>
            {
                new EventCommand(101, 0, "Hello, I'm the Name Rater. Shall I rate your Pokémon's nickname?"),
                new EventCommand(102, 0, new List<object> { "Yes", "No" }, 2),
                new EventCommand(402, 0, 0, "Yes"),
                new EventCommand(101, 1, "Which Pokémon's nickname should I rate?"),
                new EventCommand(355, 1, "pbChooseNonEggPokemon(1, 3)"),
                new EventCommand(111, 1, 12, "pbGet(1) >= 0"),
                new EventCommand(111, 2, 12, "pbGetPokemon(1).foreign?($player)"),
                new EventCommand(101, 3, "Hmm... \\v[3] isn't your own Pokémon. I can't give it a new name."),
                new EventCommand(0, 3),
                new EventCommand(411, 2),
                new EventCommand(101, 3, "\\v[3], is it? That's a fine name. Would you like to give it a better one?"),
                new EventCommand(102, 3, new List<object> { "Yes", "No" }, 2),
                new EventCommand(402, 3, 0, "Yes"),
                new EventCommand(355, 4, "pbGetPokemon(1).name = pbEnterPokemonName(\"\\v[3]'s nickname?\", 0, Pokemon::MAX_NAME_SIZE, \"\", pbGetPokemon(1))"),
                new EventCommand(355, 4, "pbSet(3, pbGetPokemon(1).name)"),
                new EventCommand(101, 4, "OK! From now on, this Pokémon shall be known as \\v[3]. It's a better name than before!"),
                new EventCommand(0, 4),
                new EventCommand(402, 3, 1, "No"),
                new EventCommand(101, 4, "I see. Come back any time."),
                new EventCommand(0, 4),
                new EventCommand(404, 3),
                new EventCommand(0, 3),
                new EventCommand(412, 2),
                new EventCommand(0, 2),
                new EventCommand(412, 1),
                new EventCommand(0, 1),
                new EventCommand(402, 0, 1, "No"),
                new EventCommand(101, 1, "Come back any time."),
                new EventCommand(0, 1),
                new EventCommand(404, 0),
                new EventCommand(0, 0)
            };
        }
    }
}
