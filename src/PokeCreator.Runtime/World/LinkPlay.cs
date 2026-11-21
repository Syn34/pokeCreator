using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Battles;
using PokeCreator.Pkmn;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.UI;

namespace PokeCreator.Runtime.World
{
    /// <summary>Connecting two games: host or join, then swap a "hello" (name, game, party, seed).</summary>
    public class LinkConnectScene : Scene
    {
        readonly LinkSession _s;
        readonly LinkMessage _hello;
        readonly Action<LinkSession, LinkMessage> _ready;
        readonly Action _cancel;
        readonly string _status;
        bool _sent;

        public LinkConnectScene(Game g, LinkSession s, LinkMessage hello, string status, Action<LinkSession, LinkMessage> ready, Action cancel) : base(g)
        {
            _s = s; _hello = hello; _status = status; _ready = ready; _cancel = cancel;
        }

        void Fail(string why)
        {
            Close(); _s.Dispose();
            Game.Message.Show(why, null, -1, _ => _cancel());
        }

        protected override void Update()
        {
            if (Input.Trigger(Button.Cancel)) { Cancel(); Close(); _s.Dispose(); _cancel(); return; }
            if (_s.Closed) { Fail("The link couldn't be made." + (string.IsNullOrEmpty(_s.Error) ? "" : "\\n(" + _s.Error + ")")); return; }
            if (_s.Connected && !_sent) { _s.Send(_hello); _sent = true; }
            var m = _s.Poll();
            if (m == null || m.Type != "hello") return;
            if (m.Game != _hello.Game) { Fail("The other player is playing a different game."); return; }
            if (m.Mode != _hello.Mode) { Fail("The other player chose " + (m.Mode == "trade" ? "trading" : "a battle") + "."); return; }
            Close();
            _ready(_s, m);
        }

        public override void Draw(Gfx g)
        {
            MessageWindow.DrawBox(g, 4, g.Height - 100, g.Width - 8, 96);
            var lines = _status.Split('\n');
            for (int i = 0; i < lines.Length && i < 2; i++) Ui.Text(g, 24, g.Height - 86 + i * 32, lines[i]);
        }
    }

    /// <summary>
    /// Link battles and trades (pbLinkBattle / pbLinkTrade). Both games run the same battle from the host's point of
    /// view, with the same seed and both players' commands each turn, so they stay in step; the joining player sees it
    /// from the other side. Teams fight as healed copies, so nothing changes in either party.
    /// </summary>
    public static class LinkPlay
    {
        /// <summary>pbLinkBattle / pbLinkTrade: asks whether to host or join.</summary>
        public static void Begin(Game g, string mode, bool dbl)
        {
            if (g.State.Party.Count(Battle.Able) == 0) { g.Message.Show("You need a Pokémon that can battle."); return; }
            g.Message.Show(mode == "trade" ? "Trade with another player over a link." : "Battle another player over a link.", new List<string> { "Host", "Join", "Cancel" }, 2, c =>
            {
                if (c == 0) Host(g, mode, dbl);
                else if (c == 1)
                {
                    g.ScriptHolds++;
                    g.PushScene(new NameEntryScene(g, "The host's address (e.g. 192.168.1.20)", g.State.Extras.LastLinkAddress ?? "", 40, addr =>
                    {
                        g.ScriptHolds = Math.Max(0, g.ScriptHolds - 1);
                        if (!string.IsNullOrWhiteSpace(addr)) Join(g, mode, addr);
                    }));
                }
            });
        }

        static LinkMessage Hello(Game g, string mode, bool dbl)
        {
            return new LinkMessage
            {
                Type = "hello", Mode = mode, Name = g.State.PlayerName, Game = g.Project.Settings.Title ?? "",
                Seed = g.Rng.Next(int.MaxValue), Double = dbl,
                Party = mode == "battle" ? g.State.Party.Where(p => !p.IsEgg).ToList() : new List<Pokemon>(),
            };
        }

        static Action<LinkSession, LinkMessage> Ready(Game g, string mode, LinkMessage hello, Action finish)
        {
            return (s, them) => { if (mode == "trade") Trade(g, s, them, finish); else Fight(g, s, hello, them, finish); };
        }

        /// <summary>pbLinkHost(mode): waits for another player to join ("battle", "double" or "trade").</summary>
        public static void Host(Game g, string mode, bool dbl)
        {
            if (mode == "double") { mode = "battle"; dbl = true; }
            g.ScriptHolds++;
            Action finish = () => g.ScriptHolds = Math.Max(0, g.ScriptHolds - 1);
            var hello = Hello(g, mode, dbl);
            var s = LinkSession.Host(LinkSession.DefaultPort);
            var addrs = LinkSession.LocalAddresses();
            g.PushScene(new LinkConnectScene(g, s, hello, "Waiting for another player...\nYour address: " + (addrs.Count > 0 ? string.Join(", ", addrs.Take(2)) : "?") + "  (port " + LinkSession.DefaultPort + ")", Ready(g, mode, hello, finish), finish));
        }

        /// <summary>pbLinkJoin(mode, address): connects to a host ("192.168.1.20" or "host:port").</summary>
        public static void Join(Game g, string mode, string addr)
        {
            if (mode == "double") mode = "battle";
            g.ScriptHolds++;
            Action finish = () => g.ScriptHolds = Math.Max(0, g.ScriptHolds - 1);
            g.State.Extras.LastLinkAddress = addr.Trim();
            string host = addr.Trim(); int port = LinkSession.DefaultPort;
            int colon = host.LastIndexOf(':'); int pp;
            if (colon > 0 && int.TryParse(host.Substring(colon + 1), out pp)) { port = pp; host = host.Substring(0, colon); }
            var hello = Hello(g, mode, false);
            var s = LinkSession.Join(host, port);
            g.PushScene(new LinkConnectScene(g, s, hello, "Connecting to " + host + "...", Ready(g, mode, hello, finish), finish));
        }

        static List<Pokemon> Copy(List<Pokemon> party, Game g)
        {
            var list = party.Where(p => !p.IsEgg).Select(p => Json.Json.Deserialize<Pokemon>(Json.Json.Write(p))).ToList();
            foreach (var p in list) p.Heal(g.Project);
            return list;
        }

        // ------------------------------------------------------------------ battle
        static void Fight(Game g, LinkSession s, LinkMessage me, LinkMessage them, Action finish)
        {
            bool host = s.IsHost;
            var mine = Copy(me.Party, g);
            var theirs = Copy(them.Party ?? new List<Pokemon>(), g);
            if (theirs.Count(Battle.Able) == 0) { s.Dispose(); g.Message.Show("The other player has no Pokémon that can battle.", null, -1, _ => finish()); return; }
            int seed = host ? me.Seed : them.Seed;
            bool dbl = host ? me.Double : them.Double;
            var hostParty = host ? mine : theirs;
            var guestParty = host ? theirs : mine;
            var master = new Battle(g.Project, hostParty, guestParty, false, new Random(seed), dbl)
            {
                PlayerName = g.State.PlayerName, TrainerName = them.Name ?? "Player", TrainerTypeName = "",
                NoExp = true, TrainerSkill = 0, SwitchStyle = false, FlipNames = !host, PlayerHasMegaRing = true,
            };
            List<BattleCommand> myCmds = null, theirCmds = null, guestCmds = null;
            master.ExternalCommand = f => (guestCmds ?? new List<BattleCommand>()).FirstOrDefault(c => c.Slot == f.Slot) ?? BattleCommand.Fight(-1);

            var real = g.State.Party;
            g.State.Party = mine; // switching screens show the battle copies
            Battle view = master;
            BattleScene scene = null;
            Action sync = () => { };
            if (!host)
            {
                view = new Battle(g.Project, guestParty, hostParty, false, new Random(0), dbl) { PlayerName = g.State.PlayerName, TrainerName = them.Name, TrainerTypeName = "" };
                sync = () =>
                {
                    for (int i = 0; i < 3; i++) { view.Field[0][i] = master.Field[1][i]; view.Field[1][i] = master.Field[0][i]; }
                    view.Weather = master.Weather; view.WeatherTurns = master.WeatherTurns; view.Terrain = master.Terrain;
                    view.SetOutcome(Battle.FlipOutcome(master.Outcome));
                };
                view.StartOverride = () => { var ev = master.Start(); sync(); return ev.Select(Battle.Flip).ToList(); };
            }
            Action tryTurn = () =>
            {
                if (myCmds == null || theirCmds == null || scene == null) return;
                var hostCmds = host ? myCmds : theirCmds;
                guestCmds = host ? theirCmds : myCmds;
                var saved = master.TurnOverride; master.TurnOverride = null;
                var ev = master.DoTurn(hostCmds);
                master.TurnOverride = saved;
                myCmds = theirCmds = null;
                sync();
                scene.Inject(host ? ev : ev.Select(Battle.Flip).ToList());
            };
            Func<List<BattleCommand>, List<BattleEvent>> localTurn = cmds =>
            {
                myCmds = cmds.Select(c => Battle.DecodeCommand(Battle.EncodeCommand(c))).ToList();
                s.Send(new LinkMessage { Type = "cmd", Commands = myCmds.Select(Battle.EncodeCommand).ToList() });
                scene.ExternalWait = true;
                tryTurn();
                return new List<BattleEvent>();
            };
            if (host)
            {
                master.TurnOverride = localTurn;
                master.SwitchAfterFaintOverride = (slot, party) =>
                {
                    var saved = master.SwitchAfterFaintOverride; master.SwitchAfterFaintOverride = null;
                    var ev = master.SwitchAfterFaint(slot, party);
                    master.SwitchAfterFaintOverride = saved;
                    s.Send(new LinkMessage { Type = "switch", Slot = slot, Index = party });
                    return ev;
                };
            }
            else view.TurnOverride = localTurn;

            bool lost = false;
            var setup = new BattleSetup
            {
                Wild = false, Link = true, CanLose = true, LinkOpponent = them.Name, Backdrop = "field",
                LinkUpdate = () =>
                {
                    if (scene == null) return;
                    LinkMessage m;
                    while ((m = s.Poll()) != null)
                    {
                        if (m.Type == "cmd") { theirCmds = (m.Commands ?? new List<List<int>>()).Select(Battle.DecodeCommand).ToList(); tryTurn(); }
                        else if (m.Type == "switch" && !host) { var ev = master.SwitchAfterFaint(m.Slot, m.Index); sync(); scene.Inject(ev.Select(Battle.Flip).ToList()); }
                        else if (m.Type == "bye" && master.Outcome == BattleOutcome.Ongoing) s.Dispose();
                    }
                    if (s.Closed && !lost && master.Outcome == BattleOutcome.Ongoing)
                    {
                        lost = true;
                        master.SetOutcome(BattleOutcome.Escaped); view.SetOutcome(BattleOutcome.Escaped);
                        scene.Inject(new List<BattleEvent> { new BattleEvent { Kind = BattleEventKind.Message, Text = "The link with " + them.Name + " was lost." } });
                    }
                },
                OnEnd = o =>
                {
                    g.State.Party = real;
                    s.Send(new LinkMessage { Type = "bye" });
                    s.Dispose();
                    string result = o == BattleOutcome.Win ? "You beat " + them.Name + "!" : o == BattleOutcome.Loss ? them.Name + " won the battle!" : "The battle ended.";
                    g.Message.Show(result, null, -1, _ => finish());
                },
            };
            g.Audio.PlaySe("Encounter");
            scene = new BattleScene(g, view, setup);
            g.PushScene(scene);
        }

        // ------------------------------------------------------------------ trade
        static void Trade(Game g, LinkSession s, LinkMessage them, Action finish)
        {
            int myIndex = -1; Pokemon offered = null, mineOffer = null;
            bool myAccept = false, theirAccept = false, decided = false, done = false;
            Action end = null;
            end = () => { if (done) return; done = true; s.Send(new LinkMessage { Type = "bye" }); s.Dispose(); finish(); };
            Action check = null;
            Action ask = () =>
            {
                g.Message.Show(them.Name + " offers " + offered.Name(g.Project) + " (Lv. " + offered.Level + ") for your " + mineOffer.Name(g.Project) + ". Trade?", new List<string> { "Yes", "No" }, 1, c =>
                {
                    myAccept = c == 0; decided = true;
                    s.Send(new LinkMessage { Type = "accept", Accept = myAccept });
                    if (!myAccept) { g.Message.Show("You cancelled the trade.", null, -1, _ => end()); return; }
                    g.Message.Show("Waiting for " + them.Name + "...");
                    check();
                });
            };
            check = () =>
            {
                if (done || !decided || !myAccept) return;
                if (!theirAccept) return;
                // Both agreed: swap
                int i = g.State.Party.IndexOf(mineOffer);
                if (i < 0) { end(); return; }
                var got = offered;
                g.State.Party[i] = got;
                g.State.Register(got);
                g.PushScene(new TradeScene(g, mineOffer, got, them.Name, () =>
                {
                    var to = Growth.CheckTradeEvolution(g.Project, got, mineOffer);
                    if (to != null) g.PushScene(new EvolutionScene(g, got, to, false, null));
                    end();
                }));
            };
            // A little scene that only listens to the link while the player chooses
            var listener = new LinkListenScene(g, () =>
            {
                LinkMessage m;
                while ((m = s.Poll()) != null)
                {
                    if (m.Type == "offer" && m.Party != null && m.Party.Count > 0) { offered = m.Party[0]; if (mineOffer != null && !decided) ask(); }
                    else if (m.Type == "accept") { theirAccept = m.Accept; if (!m.Accept) { g.CloseAllScenes(); g.Message.Show(them.Name + " cancelled the trade.", null, -1, _ => end()); return true; } check(); }
                    else if (m.Type == "bye") { if (!done) { g.Message.Show("The link was closed.", null, -1, _ => end()); } return true; }
                }
                if (s.Closed && !done) { g.Message.Show("The link was lost.", null, -1, _ => end()); return true; }
                return done;
            });
            g.PushScene(listener);
            g.PushScene(new PartyScene(g, "Choose a Pokémon to trade with " + them.Name + ".", null, (i, close) =>
            {
                var pk = g.State.Party[i];
                if (pk.IsEgg) { g.Message.Show("Eggs can't be traded here."); return; }
                if (g.State.Party.Count(p => Battle.Able(p) && p != pk) == 0) { g.Message.Show("That's your last Pokémon that can battle!"); return; }
                close();
                myIndex = i; mineOffer = pk;
                s.Send(new LinkMessage { Type = "offer", Party = new List<Pokemon> { pk } });
                if (offered != null) ask();
                else g.Message.Show("Waiting for " + them.Name + " to choose...");
            }, () => { s.Send(new LinkMessage { Type = "accept", Accept = false }); end(); }));
        }
    }

    /// <summary>Runs a check every frame underneath other screens (link messages during a trade); closes when it returns true.</summary>
    public class LinkListenScene : Scene
    {
        readonly Func<bool> _tick;
        public LinkListenScene(Game g, Func<bool> tick) : base(g) { _tick = tick; }
        public void Tick2() { if (_tick()) Close(); }
        protected override void Update() { if (_tick()) Close(); }
        public override void Draw(Gfx g) { }
    }
}
