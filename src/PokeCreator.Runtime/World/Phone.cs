using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.UI;

namespace PokeCreator.Runtime.World
{
    /// <summary>Someone in the Pokégear's phone list: a trainer (for rematches) or a person who runs a common event when called.</summary>
    public class PhoneContact
    {
        public string TrainerType { get; set; }
        public string Name { get; set; }
        public int MapId { get; set; }
        public int EventId { get; set; }
        /// <summary>Which version of the trainer to battle next (TrainerBattle.start(type, name, version)).</summary>
        public int Version { get; set; }
        public bool RematchReady { get; set; }
        /// <summary>For people who aren't trainers: the common event run when you call them.</summary>
        public int CommonEvent { get; set; }
        public bool IsTrainer { get { return !string.IsNullOrEmpty(TrainerType); } }
    }

    /// <summary>
    /// The Pokégear phone and trainer rematches. Trainers registered after a battle call now and then (every 600-1200
    /// steps one of them) when they're ready to battle again, using the next version of their team; talking to them
    /// then starts the rematch. Script names follow Essentials (Phone.add, Phone.can_rematch?, Phone.variant,
    /// pbPhoneRegisterBattle, pbPhoneBattleCount, pbPhoneReadyToBattle?...).
    /// </summary>
    public static class Phone
    {
        static readonly Random Rng = new Random();
        static int _stepsToCall = -1;

        public static PhoneContact Find(Game g, string type, string name)
        {
            return g.State.Phone.FirstOrDefault(c => c.TrainerType == type && c.Name == name);
        }

        /// <summary>The highest version of this trainer in the database.</summary>
        public static int MaxVersion(Game g, string type, string name)
        {
            var vs = g.Project.Trainers.Where(t => t.TrainerType == type && t.Name == name).Select(t => t.Version).ToList();
            return vs.Count == 0 ? 0 : vs.Max();
        }

        public static bool CanAdd(Game g, string type, string name)
        {
            return g.State.Flags.Contains("has_pokegear") && Find(g, type, name) == null && (string.IsNullOrEmpty(type) || MaxVersion(g, type, name) > 0);
        }

        public static void AddTrainer(Game g, int mapId, int eventId, string type, string name, int startVersion)
        {
            if (Find(g, type, name) != null) return;
            g.State.Phone.Add(new PhoneContact { TrainerType = type, Name = name, MapId = mapId, EventId = eventId, Version = Math.Max(1, startVersion + 1) });
        }

        public static void AddPerson(Game g, int mapId, int eventId, string name, int commonEvent)
        {
            if (g.State.Phone.Any(c => !c.IsTrainer && c.Name == name)) return;
            g.State.Phone.Add(new PhoneContact { Name = name, MapId = mapId, EventId = eventId, CommonEvent = commonEvent });
        }

        public static bool ReadyToBattle(Game g, string type, string name) { var c = Find(g, type, name); return c != null && c.RematchReady; }

        /// <summary>The version to battle (0 if not registered).</summary>
        public static int Variant(Game g, string type, string name)
        {
            var c = Find(g, type, name);
            return c == null ? 0 : Math.Min(c.Version, MaxVersion(g, type, name));
        }

        /// <summary>After a rematch: next time the trainer uses their next team.</summary>
        public static void Rematched(Game g, string type, string name)
        {
            var c = Find(g, type, name);
            if (c == null) return;
            c.RematchReady = false;
            c.Version = Math.Min(c.Version + 1, Math.Max(1, MaxVersion(g, type, name)));
        }

        public static string ContactLabel(Game g, PhoneContact c)
        {
            if (!c.IsTrainer) return c.Name;
            var tt = g.Project.TrainerTypes.FirstOrDefault(t => t.Id == c.TrainerType);
            return (tt != null ? tt.Name + " " : "") + c.Name;
        }

        static string MapName(Game g, int mapId)
        {
            var mi = g.Project.GetMapInfo(mapId);
            return mi != null ? g.Message.Expand(mi.Name) : "somewhere";
        }

        /// <summary>Every step: now and then a registered trainer calls, ready for a rematch.</summary>
        public static void OnStep(Game g)
        {
            if (!g.State.Flags.Contains("has_pokegear")) return;
            var waiting = g.State.Phone.Where(x => x.IsTrainer && !x.RematchReady && x.Version <= MaxVersion(g, x.TrainerType, x.Name)).ToList();
            if (waiting.Count == 0) return;
            if (_stepsToCall < 0) _stepsToCall = 600 + Rng.Next(600);
            if (--_stepsToCall > 0) return;
            _stepsToCall = -1;
            if (g.MessageBusy || g.InterpreterRunning) return;
            var c = waiting[Rng.Next(waiting.Count)];
            c.RematchReady = true;
            g.Audio.PlaySe("Phone call");
            g.Message.Show("......\\n......", null, -1, _ =>
                g.Message.Show(ContactLabel(g, c) + ": Hi, it's " + c.Name + "! My Pokémon are a lot stronger now. Come and battle me again at " + MapName(g, c.MapId) + "!"));
        }

        /// <summary>Calling someone from the Pokégear.</summary>
        public static void Call(Game g, PhoneContact c)
        {
            if (!c.IsTrainer && c.CommonEvent > 0)
            {
                var ce = g.Project.GetCommonEvent(c.CommonEvent);
                if (ce != null) { g.CloseAllScenes(); g.RunCommonEvent(ce); return; }
            }
            string who = ContactLabel(g, c);
            if (!c.IsTrainer) { g.Message.Show(who + ": Hello! How are you doing?"); return; }
            if (c.RematchReady) { g.Message.Show(who + ": I'm waiting for you at " + MapName(g, c.MapId) + "! Let's battle!"); return; }
            string[] chat = { "My Pokémon and I are training hard. I'll call you when we're ready!", "How are your Pokémon doing? Mine are getting stronger!", "I've been battling a lot lately. Let's battle again soon!" };
            g.Message.Show(who + ": " + chat[Rng.Next(chat.Length)]);
        }
    }

    /// <summary>The Pokégear: Town Map and Phone.</summary>
    public class PokegearScene : Scene
    {
        CommandList _list;
        readonly List<string> _ids = new List<string>();

        public PokegearScene(Game g) : base(g)
        {
            var labels = new List<string>();
            _ids.Add("map"); labels.Add("Map");
            _ids.Add("phone"); labels.Add("Phone");
            _ids.Add("exit"); labels.Add("Exit");
            _list = new CommandList(labels);
            Opaque = true;
        }

        protected override void Update()
        {
            int r = _list.Update(this, Cursor);
            if (r == -2) { Cancel(); Close(); return; }
            if (r < 0) return;
            Decide();
            switch (_ids[r])
            {
                case "map": Game.PushScene(new TownMapScene(Game, false, null)); break;
                case "phone": Game.PushScene(new PhoneScene(Game)); break;
                default: Close(); break;
            }
        }

        public override void Draw(Platform.Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Platform.Color(64, 96, 168), new Platform.Color(24, 40, 96));
            Ui.Panel(g, 8, 8, g.Width - 16, 44, new Platform.Color(248, 248, 248), new Platform.Color(32, 48, 96));
            Ui.Text(g, 22, 14, "Pokégear");
            _list.Draw(g, g.Width / 2 - 90, 90);
        }
    }

    /// <summary>Phone list: call a registered contact.</summary>
    public class PhoneScene : Scene
    {
        int _index;

        public PhoneScene(Game g) : base(g) { Opaque = true; }

        protected override void Update()
        {
            var list = Game.State.Phone;
            if (Input.Trigger(Platform.Button.Cancel)) { Cancel(); Close(); return; }
            if (list.Count == 0) { if (Input.Trigger(Platform.Button.Action)) { Cancel(); Close(); } return; }
            if (Input.Repeat(Platform.Button.Down)) { _index = (_index + 1) % list.Count; Cursor(); }
            if (Input.Repeat(Platform.Button.Up)) { _index = (_index + list.Count - 1) % list.Count; Cursor(); }
            if (Input.Trigger(Platform.Button.Action))
            {
                Decide();
                var c = list[_index];
                Game.Message.Show("Calling " + Phone.ContactLabel(Game, c) + "...", null, -1, _ => Phone.Call(Game, c));
            }
        }

        public override void Draw(Platform.Gfx g)
        {
            Ui.Gradient(g, 0, 0, g.Width, g.Height, new Platform.Color(64, 96, 168), new Platform.Color(24, 40, 96));
            Ui.Panel(g, 8, 8, g.Width - 16, 44, new Platform.Color(248, 248, 248), new Platform.Color(32, 48, 96));
            Ui.Text(g, 22, 14, "Phone");
            Ui.Panel(g, 8, 60, g.Width - 16, 230, new Platform.Color(248, 248, 248), new Platform.Color(32, 48, 96));
            var list = Game.State.Phone;
            if (list.Count == 0) { Ui.Text(g, 30, 76, "No one is registered yet."); return; }
            int first = Math.Max(0, Math.Min(_index - 3, list.Count - 7));
            for (int i = first; i < Math.Min(list.Count, first + 7); i++)
            {
                int y = 70 + (i - first) * 30;
                if (i == _index) Ui.Arrow(g, 22, y + 8);
                var c = list[i];
                Ui.Text(g, 44, y, Phone.ContactLabel(Game, c));
                if (c.IsTrainer && c.RematchReady) Ui.TextRight(g, g.Width - 30, y, "Wants to battle!");
            }
        }
    }
}
