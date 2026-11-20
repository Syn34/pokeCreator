using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Battles;
using PokeCreator.Data;
using PokeCreator.Pkmn;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.World;

namespace PokeCreator.Runtime.UI
{
    /// <summary>How a battle was set up, and what to do when it ends.</summary>
    public class BattleSetup
    {
        public bool Wild = true;
        public TrainerData Trainer;
        public TrainerTypeData TrainerType;
        /// <summary>The second trainer of a two-trainer double battle (or null).</summary>
        public TrainerData Trainer2;
        public TrainerTypeData TrainerType2;
        public string Backdrop = "field";
        public bool CanLose;               // losing doesn't black out (e.g. a rival battle you may lose)
        public Action<BattleOutcome> OnEnd;
        /// <summary>Battle music instead of the usual wild / trainer music (roaming Pokémon).</summary>
        public string Bgm;
        /// <summary>A Safari Zone battle (Ball / Bait / Rock / Run).</summary>
        public bool Safari;
        /// <summary>A Bug-Catching Contest battle: the Bag throws a Sport Ball; the catch is kept by the contest.</summary>
        public bool BugContest;
        /// <summary>A battle facility round (Battle Tower): no items from the Bag, no prize money.</summary>
        public bool Facility;
        /// <summary>A link battle: no Bag, no running, no prize money; LinkUpdate runs every frame (network).</summary>
        public bool Link;
        public Action LinkUpdate;
        public string LinkOpponent;
    }

    /// <summary>
    /// The battle screen: backdrop, sprites, databoxes, the Fight / Bag / Pokémon / Run menu, and playback of the
    /// engine's events (messages, HP bars, fainting, EXP, catching).
    /// </summary>
    public class BattleScene : Scene
    {
        readonly Battle _b;
        readonly BattleSetup _setup;
        readonly Queue<BattleEvent> _queue = new Queue<BattleEvent>();
        enum Mode { Intro, Events, Command, Fight, Target, Busy, Ending, Done }
        Mode _mode = Mode.Intro;
        int _cmd, _move, _anim, _target;
        BattleEvent _current;
        // Display state per field position: index = side * 2 + slot
        readonly double[] _shownHp = new double[6];
        readonly int[] _shownParty = { -1, -1, -1, -1, -1, -1 };
        readonly bool[] _visible = new bool[6];
        readonly int[] _faintDrop = new int[6];
        readonly int[] _blink = new int[6];
        readonly double[] _shownExp = new double[3];
        // Simple move effects: the user lunges, a burst in the move type's colour hits the target, arrows on stat changes
        readonly int[] _lunge = new int[6];
        int _burstKey = -1, _burstFrames, _statKey = -1, _statFrames, _statDir;
        Color _burstColor = new Color(248, 248, 248);
        string _lastCategory = "Physical";
        // Essentials battle animations (Data/animations.json)
        BattleAnimPlayer _player;
        string _briefText;
        bool _megaOn;
        int _formFlash; // Mega Evolve the Pokémon choosing its move (toggled with Shift in the Fight menu)
        bool _animThisMove;
        bool AnimsOn { get { return Game.Project.Animations.Count > 0 && Game.Options.BattleEffects; } }
        bool _trainerVisible, _ballVisible, _ballCaught;
        int _ballShakes, _ballSlot;
        int _introSlide = 120;
        readonly List<Pokemon> _party;
        // Commands chosen this turn (double battles: one per Pokémon)
        int _cmdSlot;
        readonly List<BattleCommand> _pending = new List<BattleCommand>();
        List<Battler> _targets = new List<Battler>();

        public BattleScene(Game g, Battle battle, BattleSetup setup) : base(g)
        {
            Opaque = true; _b = battle; _setup = setup; _party = battle.Parties[0];
            battle.PlayerHasMegaRing = Battle.MegaRings.Any(r => g.State.ItemCount(r) > 0);
            battle.BagHas = r => g.State.ItemCount(r) > 0;
            _trainerVisible = !setup.Wild;
            foreach (var e in battle.Start()) _queue.Enqueue(e);
            // Seen in the Pokédex as soon as it appears
            foreach (var pk in battle.Parties[1]) if (setup.Wild) g.State.Seen.Add(pk.Species);
            string bgm = !string.IsNullOrEmpty(setup.Bgm) ? setup.Bgm : setup.Wild ? g.Project.Settings.WildBattleBGM : (setup.TrainerType != null && !string.IsNullOrEmpty(setup.TrainerType.BattleBGM) ? setup.TrainerType.BattleBGM : g.Project.Settings.TrainerBattleBGM);
            if (string.IsNullOrEmpty(bgm)) bgm = setup.Wild ? "Battle wild" : "Battle trainer";
            g.Audio.PlayBgm(bgm);
        }

        static int Key(int side, int slot) { return side * 3 + Math.Max(0, Math.Min(2, slot)); }
        /// <summary>The field slot showing this party member (or 0).</summary>
        int SlotOf(int side, int party) { for (int s = 0; s < 3; s++) if (_shownParty[Key(side, s)] == party) return s; return 0; }
        Battler Me { get { return _b.Field[0][_cmdSlot] ?? _b.Live(0).FirstOrDefault() ?? _b.Active[0]; } }

        // ------------------------------------------------------------------ update
        /// <summary>Link battles: the turn's events haven't arrived yet (waiting for the other player).</summary>
        public bool ExternalWait;

        /// <summary>Link battles: events that arrived later (the turn, or the other player's switch).</summary>
        public void Inject(List<BattleEvent> events)
        {
            ExternalWait = false;
            foreach (var e in events) _queue.Enqueue(e);
            if (_mode != Mode.Ending) _mode = Mode.Events;
        }

        protected override void Update()
        {
            if (_setup.LinkUpdate != null) _setup.LinkUpdate();
            if (_introSlide > 0) { _introSlide -= 6; return; }
            switch (_mode)
            {
                case Mode.Intro: _mode = Mode.Events; break;
                case Mode.Events: PlayEvents(); break;
                case Mode.Command: CommandMenu(); break;
                case Mode.Fight: FightMenu(); break;
                case Mode.Target: TargetMenu(); break;
                case Mode.Busy: break;
                case Mode.Ending: break;
            }
        }

        void PlayEvents()
        {
            if (_current != null && !StepAnimation()) return;
            _current = null;
            while (_queue.Count > 0)
            {
                var e = _queue.Dequeue();
                if (Begin(e)) { _current = e; _anim = 0; return; }
                if (Game.Message.Busy || _mode != Mode.Events) return;
            }
            // All played: what next?
            if (ExternalWait) return;
            if (_b.Outcome != BattleOutcome.Ongoing) { EndBattle(); return; }
            if (_b.NeedPlayerSwitch) { ChooseReplacement(); return; }
            if (_b.PendingFoeSendOut) { AskShift(); return; }
            StartCommands();
        }

        void StartCommands()
        {
            _briefText = null;
            _pending.Clear();
            var live = _b.PlayerControlled();
            if (live.Count == 0 && _b.Live(0).Count > 0) { Run(_b.DoTurn(new List<BattleCommand>())); return; } // only the partner is left
            _cmdSlot = live.Count > 0 ? live[0].Slot : 0;
            _mode = Mode.Command;
            if (_cmd < 0 || _cmd > 3) _cmd = 0;
        }

        /// <summary>Records the command for the Pokémon being commanded; asks the next one, or runs the turn.</summary>
        void Commit(BattleCommand c)
        {
            c.Slot = _cmdSlot;
            if (c.Kind == BattleCommandKind.Fight && _megaOn && _b.CanMegaEvolve(Me)) c.Mega = true;
            _megaOn = false;
            _pending.RemoveAll(x => x.Slot == _cmdSlot);
            _pending.Add(c);
            var next = _b.PlayerControlled().FirstOrDefault(x => x.Slot > _cmdSlot);
            if (next != null && c.Kind != BattleCommandKind.Run) { _cmdSlot = next.Slot; _cmd = 0; _move = 0; _mode = Mode.Command; return; }
            Run(_b.DoTurn(new List<BattleCommand>(_pending)));
        }

        /// <summary>Starts showing an event. Returns true if it animates over several frames.</summary>
        bool Begin(BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.Message:
                    // "X used Y!" closes by itself and stays in the box while the move's animation plays (as in Essentials)
                    if (AnimsOn && _queue.Count > 0 && _queue.Peek().Kind == BattleEventKind.MoveUsed) { _briefText = e.Text; Game.Message.Show(e.Text + "\\wtnp[6]"); }
                    else { _briefText = null; Game.Message.Show(e.Text); }
                    return false;
                case BattleEventKind.SendOut:
                    {
                        if (e.Side == 1) _trainerVisible = false;
                        int k = Key(e.Side, e.Slot);
                        _shownParty[k] = e.Party; _visible[k] = true; _faintDrop[k] = 0;
                        _shownHp[k] = _b.Parties[e.Side][e.Party].HP;
                        if (e.Side == 0) _shownExp[Math.Min(2, e.Slot)] = ExpFraction(_party[e.Party]);
                        Game.Audio.PlaySe("Battle recall");
                        Game.Audio.PlaySe("Cries/" + _b.Parties[e.Side][e.Party].Species);
                        return true;
                    }
                case BattleEventKind.Withdraw: _visible[Key(e.Side, e.Slot)] = false; Game.Audio.PlaySe("Battle recall"); return true;
                case BattleEventKind.Hp: return true;
                case BattleEventKind.Hit:
                    Game.Audio.PlaySe(e.Value > 100 ? "Battle damage super" : e.Value < 100 ? "Battle damage weak" : "Battle damage normal");
                    _blink[Key(e.Side, SlotOf(e.Side, e.Party))] = 16;
                    if (!_animThisMove) { _burstKey = Key(e.Side, SlotOf(e.Side, e.Party)); _burstFrames = 18; }
                    return true;
                case BattleEventKind.MoveUsed:
                    {
                        var md = Game.Project.Moves.FirstOrDefault(m => m.Id == e.Text);
                        _burstColor = TypeColor(md != null ? md.Type : "NORMAL");
                        _lastCategory = md != null ? md.Category ?? "Physical" : "Physical";
                        _animThisMove = false;
                        if (AnimsOn) return false; // the move's animation plays when it hits (MoveAnim)
                        if (_lastCategory == "Physical") { _lunge[Key(e.Side, SlotOf(e.Side, e.Party))] = 12; return true; }
                        return false;
                    }
                case BattleEventKind.MoveAnim:
                    {
                        if (!AnimsOn) return false;
                        string moveId = e.Text ?? ""; int hit = 0;
                        int colon = moveId.IndexOf(':');
                        if (colon > 0) { int.TryParse(moveId.Substring(colon + 1), out hit); moveId = moveId.Substring(0, colon); }
                        var md = Game.Project.Moves.FirstOrDefault(m => m.Id == moveId);
                        bool swap;
                        // Charging turns only animate when the move has a charging animation; extra hits fall back to the move's own
                        var anim = BattleAnimations.ForMove(Game.Project.Animations, md, e.Side == 1, out swap, hit, !e.Flag);
                        if (anim == null) return false;
                        var user = Battler(e.Side, e.Slot); var target = Battler(e.Value, e.Value2);
                        if (user == null) return false;
                        if (target == null) target = user;
                        _player = swap ? new BattleAnimPlayer(Game, anim, target, user) : new BattleAnimPlayer(Game, anim, user, target);
                        _animThisMove = true;
                        return true;
                    }
                case BattleEventKind.CommonAnim: return StartCommon(e.Text, e.Side, e.Slot);
                case BattleEventKind.FormChange:
                    Game.Audio.PlaySe("Battle recall");
                    _blink[Key(e.Side, e.Slot)] = 0;
                    if (StartCommon("MegaEvolution", e.Side, e.Slot)) return true;
                    _formFlash = 24;
                    return true;
                case BattleEventKind.Status:
                    if (!string.IsNullOrEmpty(e.Text)) StartCommon(StatusAnimName(e.Text), e.Side, SlotOf(e.Side, e.Party));
                    return _player != null;
                case BattleEventKind.Faint: Game.Audio.PlaySe("Pkmn faint"); return true;
                case BattleEventKind.StatChange:
                    Game.Audio.PlaySe(e.Value > 0 ? "Battle stat up" : "Battle stat down");
                    if (StartCommon(e.Value > 0 ? "StatUp" : "StatDown", e.Side, SlotOf(e.Side, e.Party))) return true;
                    _statKey = Key(e.Side, SlotOf(e.Side, e.Party)); _statFrames = 24; _statDir = e.Value > 0 ? 1 : -1;
                    return true;
                case BattleEventKind.Exp:
                    if (_shownParty[Key(0, SlotOf(0, e.Party))] != e.Party) return false;
                    Game.Audio.PlaySe("Pkmn exp gain"); return true;
                case BattleEventKind.LevelUp:
                    {
                        var pk = _party[e.Party];
                        int slot = SlotOf(0, e.Party);
                        if (_shownParty[Key(0, slot)] == e.Party) { _shownHp[Key(0, slot)] = pk.HP; _shownExp[slot] = 0; }
                        Game.Audio.PlayMe("Pkmn level up");
                        _mode = Mode.Busy;
                        Game.Message.Show(e.Text, null, -1, _ =>
                            ItemFlow.LearnMoves(Game, pk, new Queue<string>(e.Moves ?? new List<string>()), () => _mode = Mode.Events));
                        return false;
                    }
                case BattleEventKind.Catch:
                    {
                        if (e.Value < 0) return false; // the trainer blocked it, or two targets
                        var foe = _b.Live(1).FirstOrDefault();
                        _ballSlot = foe != null ? foe.Slot : 0;
                        _ballVisible = true; _ballCaught = e.Flag; _ballShakes = e.Value;
                        Game.Audio.PlaySe("Battle throw");
                        return true;
                    }
                case BattleEventKind.Flee: Game.Audio.PlaySe("Battle flee"); return false;
                default: return false;
            }
        }

        /// <summary>Advances the current animation; true when finished.</summary>
        bool StepAnimation()
        {
            var e = _current;
            _anim++;
            if (_player != null)
            {
                _player.Update();
                if (!_player.Done) return false;
                _player = null;
                return true;
            }
            switch (e.Kind)
            {
                case BattleEventKind.Hp:
                    {
                        int k = Key(e.Side, SlotOf(e.Side, e.Party));
                        if (e.Party != _shownParty[k]) return true;
                        double target = e.Value2;
                        int max = Math.Max(1, _b.Parties[e.Side][e.Party].TotalHP(Game.Project));
                        double step = Math.Max(1, max / 48.0);
                        if (Math.Abs(_shownHp[k] - target) <= step) { _shownHp[k] = target; return true; }
                        _shownHp[k] += Math.Sign(target - _shownHp[k]) * step;
                        return false;
                    }
                case BattleEventKind.Hit: return _anim >= 16;
                case BattleEventKind.MoveUsed: return _anim >= 12;
                case BattleEventKind.Faint:
                    {
                        int k = Key(e.Side, e.Slot);
                        _faintDrop[k] = _anim * 8;
                        if (_anim >= 20) { _visible[k] = false; return true; }
                        return false;
                    }
                case BattleEventKind.FormChange: return _anim >= 24;
                case BattleEventKind.SendOut:
                    if (_anim == 12 && _b.Parties[e.Side][e.Party].Shiny && StartCommon("Shiny", e.Side, e.Slot)) return false;
                    return _anim >= 12;
                case BattleEventKind.Withdraw: return _anim >= 10;
                case BattleEventKind.StatChange: return _anim >= 24;
                case BattleEventKind.Exp:
                    {
                        var pk = _party[e.Party];
                        int slot = SlotOf(0, e.Party);
                        double target = ExpFractionAt(pk, e.Value2);
                        if (_shownExp[slot] >= target - 0.01 || _anim > 90) { _shownExp[slot] = target; return true; }
                        _shownExp[slot] = Math.Min(target, _shownExp[slot] + 0.02);
                        return false;
                    }
                case BattleEventKind.Catch:
                    {
                        // 0-30 throw, then 30 frames per shake, then burst or click
                        int end = 30 + _ballShakes * 30 + 20;
                        int k = Key(1, _ballSlot);
                        if (_anim == 30) _visible[k] = false;
                        if (_anim > 30 && (_anim - 30) % 30 == 0 && (_anim - 30) / 30 <= _ballShakes) Game.Audio.PlaySe("Battle ball shake");
                        if (_anim >= end)
                        {
                            if (_ballCaught) { Game.Audio.PlaySe("Battle catch click"); Game.Audio.PlayMe("Battle capture success"); }
                            else { _ballVisible = false; _visible[k] = true; Game.Audio.PlaySe("Battle recall"); }
                            return true;
                        }
                        return false;
                    }
                default: return true;
            }
        }

        // ------------------------------------------------------------------ battle animations
        /// <summary>A battler as the animation player sees it (sprite centre and texture), or null if nobody stands there.</summary>
        AnimBattler Battler(int side, int slot)
        {
            if (side < 0 || side > 1) return null;
            int k = Key(side, slot);
            if (_shownParty[k] < 0) return null;
            var pk = _b.Parties[side][_shownParty[k]];
            int x, bottom; Pos(side, slot, out x, out bottom);
            int fw;
            var tex = BattlerTexture(pk, side == 0, out fw);
            int h = tex != null ? tex.Height : 96;
            return new AnimBattler { Key = k, X = x, Y = bottom - h / 2.0, Tex = tex, FrameW = tex != null ? fw : 0, FrameH = h };
        }

        /// <summary>Starts "Common:name" on a Pokémon (side -1 = the field, e.g. weather). False if there is no such animation.</summary>
        bool StartCommon(string name, int side, int slot)
        {
            if (!AnimsOn || string.IsNullOrEmpty(name)) return false;
            var anim = BattleAnimations.Common(Game.Project.Animations, name);
            if (anim == null) return false;
            AnimBattler user, target;
            if (side < 0)
            {
                user = Battler(0, 0) ?? Battler(0, 1); target = Battler(1, 0) ?? Battler(1, 1);
                if (user == null) user = target;
                if (user == null) return false;
            }
            else { user = Battler(side, slot); target = user; if (user == null) return false; }
            _player = new BattleAnimPlayer(Game, anim, user, target);
            return true;
        }

        /// <summary>Autotest: plays a move's (or "Common:X") animation now, from userSide's first Pokémon.</summary>
        public string DebugAnimation(string what, int userSide, int targetSlot)
        {
            if (what.StartsWith("Common:")) return StartCommon(what.Substring(7), userSide, 0) ? "common" : "none";
            var md = Game.Project.Moves.FirstOrDefault(m => m.Id == what);
            bool swap;
            var anim = BattleAnimations.ForMove(Game.Project.Animations, md, userSide == 1, out swap);
            if (anim == null) return "none";
            var user = Battler(userSide, 0); var target = Battler(1 - userSide, targetSlot) ?? Battler(1 - userSide, 0);
            if (user == null) return "no user";
            _player = swap ? new BattleAnimPlayer(Game, anim, target, user) : new BattleAnimPlayer(Game, anim, user, target);
            _current = new BattleEvent { Kind = BattleEventKind.MoveAnim }; _anim = 0; _mode = Mode.Events;
            return anim.Id + (swap ? " (swapped)" : "");
        }

        static string StatusAnimName(string status)
        {
            switch ((status ?? "").ToUpperInvariant())
            {
                case "SLEEP": return "Sleep"; case "POISON": return "Poison"; case "BURN": return "Burn";
                case "PARALYSIS": return "Paralysis"; case "FROZEN": return "Frozen"; default: return null;
            }
        }

        double ExpFraction(Pokemon pk) { return ExpFractionAt(pk, pk.Exp); }

        double ExpFractionAt(Pokemon pk, int exp)
        {
            int lo = pk.ExpForThisLevel(Game.Project), hi = pk.ExpForNextLevel(Game.Project);
            if (pk.Level >= GrowthRates.MaxLevel || hi <= lo) return 0;
            return Math.Max(0, Math.Min(1, (double)(exp - lo) / (hi - lo)));
        }

        void Run(List<BattleEvent> events)
        {
            foreach (var e in events) _queue.Enqueue(e);
            _mode = Mode.Events;
        }

        // ------------------------------------------------------------------ menus
        static readonly string[] Commands = { "FIGHT", "BAG", "POKéMON", "RUN" };
        static readonly string[] SafariCommands = { "BALL", "BAIT", "ROCK", "RUN" };

        void SafariMenu()
        {
            if (Input.Repeat(Button.Right) && _cmd % 2 == 0) { _cmd++; Cursor(); }
            if (Input.Repeat(Button.Left) && _cmd % 2 == 1) { _cmd--; Cursor(); }
            if (Input.Repeat(Button.Down) && _cmd < 2) { _cmd += 2; Cursor(); }
            if (Input.Repeat(Button.Up) && _cmd >= 2) { _cmd -= 2; Cursor(); }
            if (Input.Trigger(Button.Cancel) && _cmd != 3) { _cmd = 3; Cursor(); return; }
            if (!Input.Trigger(Button.Action)) return;
            Decide();
            var s = World.Safari.State(Game);
            if (_cmd == 0)
            {
                if (s.Balls <= 0) { Game.Message.Show("You have no Safari Balls left!"); return; }
                s.Balls--;
                Run(_b.SafariTurn(Battle.SafariAction.Ball, s.Balls));
                return;
            }
            Run(_b.SafariTurn(_cmd == 1 ? Battle.SafariAction.Bait : _cmd == 2 ? Battle.SafariAction.Rock : Battle.SafariAction.Run, s.Balls));
        }

        void CommandMenu()
        {
            if (_b.Safari) { SafariMenu(); return; }
            // Battle Palace: the Pokémon decide for themselves
            if (_b.FacilityRule == "palace") { Commit(BattleCommand.Fight(-1)); return; }
            if (Input.Repeat(Button.Right) && _cmd % 2 == 0) { _cmd++; Cursor(); }
            if (Input.Repeat(Button.Left) && _cmd % 2 == 1) { _cmd--; Cursor(); }
            if (Input.Repeat(Button.Down) && _cmd < 2) { _cmd += 2; Cursor(); }
            if (Input.Repeat(Button.Up) && _cmd >= 2) { _cmd -= 2; Cursor(); }
            if (Input.Trigger(Button.Cancel))
            {
                // Double battles: back to the first Pokémon's command
                var prev = _b.PlayerControlled().LastOrDefault(x => x.Slot < _cmdSlot);
                if (prev != null) { Cancel(); _pending.RemoveAll(x => x.Slot == prev.Slot); _cmdSlot = prev.Slot; return; }
                if (_cmd != 3) { _cmd = 3; Cursor(); }
                return;
            }
            if (!Input.Trigger(Button.Action)) return;
            Decide();
            switch (_cmd)
            {
                case 0:
                    if (Me.Charging != null || Me.MustRecharge || Me.LockedMove != null || !_b.HasUsableMove(Me)) { Commit(BattleCommand.Fight(-1)); return; }
                    _mode = Mode.Fight; if (_move >= Me.Pk.Moves.Count) _move = 0; break;
                case 1: OpenBag(); break;
                case 2:
                    if (_b.FacilityRule == "arena") { Game.Message.Show("Pokémon can't be switched in the Battle Arena!"); return; }
                    OpenParty(); break;
                case 3:
                    if (_b.FacilityRule != null) { Game.Message.Show("There's no running from here!"); return; }
                    if (_setup.Link) { Game.Message.Show("There's no running from a link battle!"); return; }
                    if (_cmdSlot != (_b.PlayerControlled().FirstOrDefault() ?? Me).Slot) { Game.Message.Show("Only the first Pokémon can choose to run."); return; }
                    Commit(BattleCommand.Run()); break;
            }
        }

        void FightMenu()
        {
            int n = Me.Pk.Moves.Count;
            if (Input.Repeat(Button.Right) && _move % 2 == 0 && _move + 1 < n) { _move++; Cursor(); }
            if (Input.Repeat(Button.Left) && _move % 2 == 1) { _move--; Cursor(); }
            if (Input.Repeat(Button.Down) && _move + 2 < n) { _move += 2; Cursor(); }
            if (Input.Repeat(Button.Up) && _move >= 2) { _move -= 2; Cursor(); }
            if (Input.Trigger(Button.Cancel)) { Cancel(); _mode = Mode.Command; return; }
            // Shift: Mega Evolution on/off (one Pokémon per battle)
            if (Input.Trigger(Button.Run) && _b.CanMegaEvolve(Me) && !_pending.Any(x => x.Mega)) { _megaOn = !_megaOn; Decide(); }
            if (!Input.Trigger(Button.Action)) return;
            if (Me.Pk.Moves[_move].PP <= 0) { Buzz(); Game.Message.Show("There's no PP left for this move!"); return; }
            var why = _b.CannotSelectReason(Me, _move);
            if (why != null) { Buzz(); Game.Message.Show(why); return; }
            Decide();
            // Double battles: pick a target for single-target moves
            var md = Game.Project.Moves.FirstOrDefault(x => x.Id == Me.Pk.Moves[_move].Id);
            string tg = md != null ? md.Target ?? "NearOther" : "NearOther";
            if (_b.IsDouble && (tg == "NearOther" || tg == "NearFoe" || tg == "Other" || tg == "Foe"))
            {
                bool any = tg == "Other" || tg == "Foe";
                _targets = _b.Live(1).Where(f => any || _b.Near(f, Me)).ToList();
                if (tg == "NearOther" || tg == "Other") _targets.AddRange(_b.Live(0).Where(a => a != Me && (any || _b.Near(a, Me))));
                if (_targets.Count > 1)
                {
                    _target = Math.Max(0, _targets.FindIndex(t => t.Side == 1 && t.Slot == _cmdSlot));
                    _mode = Mode.Target;
                    return;
                }
            }
            Commit(BattleCommand.Fight(_move));
        }

        void TargetMenu()
        {
            if (Input.Repeat(Button.Right) || Input.Repeat(Button.Down)) { _target = (_target + 1) % _targets.Count; Cursor(); }
            if (Input.Repeat(Button.Left) || Input.Repeat(Button.Up)) { _target = (_target + _targets.Count - 1) % _targets.Count; Cursor(); }
            if (Input.Trigger(Button.Cancel)) { Cancel(); _mode = Mode.Fight; return; }
            if (!Input.Trigger(Button.Action)) return;
            Decide();
            var t = _targets[_target];
            var c = BattleCommand.Fight(_move);
            c.TargetSide = t.Side; c.TargetSlot = t.Slot;
            Commit(c);
        }

        /// <summary>Items usable in battle: Poké Balls and medicine.</summary>
        bool BattleItem(ItemData it)
        {
            if (it.Pocket == 3) return true;
            var k = ItemEffects.Info(Game.Project, it).Kind;
            return k == ItemKind.HealHP || k == ItemKind.FullRestore || k == ItemKind.CureStatus || k == ItemKind.Revive;
        }

        void OpenBag()
        {
            if (_setup.Facility || _setup.Link) { Game.Message.Show("Items can't be used here."); return; }
            if (_setup.BugContest)
            {
                var cs = World.BugContest.State(Game);
                if (cs.Balls <= 0) { Game.Message.Show("You have no Sport Balls left!"); return; }
                if (_b.Live(1).Count != 1) { Game.Message.Show("It's no good! It's impossible to aim when there's more than one Pokémon!"); return; }
                cs.Balls--;
                Commit(BattleCommand.Ball("SPORTBALL"));
                return;
            }
            _mode = Mode.Busy;
            Game.PushScene(new BagScene(Game, id =>
            {
                var item = Game.Project.Items.FirstOrDefault(i => i.Id == id);
                if (item == null || !BattleItem(item)) { Game.Message.Show("That can't be used now.", null, -1, _ => _mode = Mode.Command); return; }
                if (item.Pocket == 3)
                {
                    if (_setup.Wild && _b.Live(1).Count == 1) Game.State.AddItem(id, -1);
                    Commit(BattleCommand.Ball(id));
                    return;
                }
                var info = ItemEffects.Info(Game.Project, item);
                var ctx = ItemFlow.Context(Game);
                Game.PushScene(new PartyScene(Game, "Use on which Pokémon?", null, (i, close) =>
                {
                    // Try it on a copy first so the item isn't wasted
                    var test = Json.Json.Deserialize<Pokemon>(Json.Json.Write(_party[i]));
                    if (!ItemEffects.Use(Game.Project, test, item, info, -1, ctx).Used) { Game.Message.Show("It won't have any effect."); return; }
                    close();
                    Game.State.AddItem(id, -1);
                    Commit(BattleCommand.UseItem(id, i));
                }, () => _mode = Mode.Command));
            }, BattleItem, () => _mode = Mode.Command));
        }

        bool OnField(int party) { return _b.OnField.Any(x => x.Side == 0 && x.PartyIndex == party && !x.Pk.Fainted); }

        void OpenParty()
        {
            _mode = Mode.Busy;
            Game.PushScene(new PartyScene(Game, "Choose a Pokémon.", null, (i, close) =>
            {
                var pk = _party[i];
                if (OnField(i)) { Game.Message.Show(pk.Name(Game.Project) + " is already in battle!"); return; }
                if (_pending.Any(c => c.Kind == BattleCommandKind.Switch && c.Index == i)) { Game.Message.Show(pk.Name(Game.Project) + " is already going into battle!"); return; }
                if (!Battle.Able(pk)) { Game.Message.Show(pk.IsEgg ? "An Egg can't battle!" : pk.Name(Game.Project) + " has no energy left to battle!"); return; }
                close();
                Commit(BattleCommand.Switch(i));
            }, () => _mode = Mode.Command));
        }

        /// <summary>Switch battle style: "X is about to send in Y. Will you change Pokémon?"</summary>
        void AskShift()
        {
            _mode = Mode.Busy;
            string text = _b.PendingFoeTrainer + " is about to send in " + _b.PendingFoePokemon.Name(Game.Project) + ".\\nWill \\PN change Pokémon?";
            Game.Message.Show(text, new List<string> { "Yes", "No" }, 1, c =>
            {
                if (c != 0) { Run(_b.FinishFoeSendOut(-1)); return; }
                Game.PushScene(new PartyScene(Game, "Choose a Pokémon.", null, (i, close) =>
                {
                    var pk = _party[i];
                    if (OnField(i)) { Game.Message.Show(pk.Name(Game.Project) + " is already in battle!"); return; }
                    if (!Battle.Able(pk)) { Game.Message.Show(pk.IsEgg ? "An Egg can't battle!" : pk.Name(Game.Project) + " has no energy left to battle!"); return; }
                    close();
                    Run(_b.FinishFoeSendOut(i));
                }, () => Run(_b.FinishFoeSendOut(-1))));
            });
        }

        void ChooseReplacement()
        {
            _mode = Mode.Busy;
            int slot = _b.PlayerSlotsToFill[0];
            Game.PushScene(new PartyScene(Game, "Choose a Pokémon to send out.", null, (i, close) =>
            {
                var pk = _party[i];
                if (OnField(i)) { Game.Message.Show(pk.Name(Game.Project) + " is already in battle!"); return; }
                if (!Battle.Able(pk)) { Game.Message.Show(pk.IsEgg ? "An Egg can't battle!" : pk.Name(Game.Project) + " has no energy left to battle!"); return; }
                close();
                Run(_b.SwitchAfterFaint(slot, i));
            }, () => ChooseReplacement())); // must choose one
        }

        // ------------------------------------------------------------------ ending
        void EndBattle()
        {
            _b.RevertForms();
            // Pickup / Honey Gather: Pokémon holding nothing may find an item after the battle
            if (!_setup.Link && (_b.Outcome == BattleOutcome.Win || _b.Outcome == BattleOutcome.Escaped || _b.Outcome == BattleOutcome.Caught))
                Battle.PostBattlePickup(Game.Project, _party, new Random());
            _mode = Mode.Ending;
            var o = _b.Outcome;
            var steps = new List<Action<Action>>();
            if (o == BattleOutcome.Win && !_setup.Wild)
            {
                string victory = _setup.TrainerType != null && !string.IsNullOrEmpty(_setup.TrainerType.VictoryBGM) ? _setup.TrainerType.VictoryBGM : Game.Project.Settings.TrainerVictoryBGM;
                Game.Audio.PlayBgm(string.IsNullOrEmpty(victory) ? "Battle victory trainer" : victory);
                _trainerVisible = true;
                var tr = _setup.Trainer;
                if (tr != null && !string.IsNullOrEmpty(tr.LoseText)) steps.Add(next => Game.Message.Show(tr.LoseText, null, -1, _ => next()));
                var tr2 = _setup.Trainer2;
                if (tr2 != null && !string.IsNullOrEmpty(tr2.LoseText)) steps.Add(next => Game.Message.Show(tr2.LoseText, null, -1, _ => next()));
                int money = Prize();
                if (money > 0) steps.Add(next => { Game.State.Money += money; Game.Message.Show("\\PN got $" + money + " for winning!", null, -1, _ => next()); });
            }
            else if (o == BattleOutcome.Win)
            {
                string victory = Game.Project.Settings.WildVictoryBGM;
                Game.Audio.PlayBgm(string.IsNullOrEmpty(victory) ? "Battle victory wild" : victory);
            }
            else if (o == BattleOutcome.Caught && _b.CaughtPokemon != null)
            {
                var pk = _b.CaughtPokemon;
                pk.OTName = Game.State.PlayerName; pk.OTId = Game.State.TrainerId; pk.MetMap = Game.Map != null ? Game.Map.Id : 0; pk.MetLevel = pk.Level;
                bool newEntry = !Game.State.Caught.Contains(pk.Species);
                Game.State.Register(pk);
                if (newEntry && Game.State.HasPokedex) steps.Add(next => Game.Message.Show(pk.Name(Game.Project) + "'s data was added to the Pokédex.", null, -1, _ => next()));
                steps.Add(next => Game.Message.Show("Would you like to give a nickname to " + pk.Name(Game.Project) + "?", new List<string> { "Yes", "No" }, 1, c =>
                {
                    if (c == 0) { Game.AskName(pk.Name(Game.Project) + "'s nickname?", "", 12, n => pk.Nickname = n, null); }
                    next();
                }));
                if (_setup.BugContest) steps.Add(next => World.BugContest.OnCaught(Game, pk, next));
                else steps.Add(next =>
                {
                    if (Game.State.Party.Count < GameState.MaxParty) { Game.State.Party.Add(pk); next(); }
                    else { Game.State.Storage.Add(pk); Game.Message.Show(pk.Name(Game.Project) + " was sent to the PC.", null, -1, _ => next()); }
                });
            }
            foreach (var sn in _b.Snagged)
            {
                var pk = sn;
                pk.OTName = Game.State.PlayerName; pk.OTId = Game.State.TrainerId; pk.MetMap = Game.Map != null ? Game.Map.Id : 0; pk.MetLevel = pk.Level;
                pk.Heal(Game.Project);
                Game.State.Register(pk);
                steps.Add(next =>
                {
                    if (Game.State.Party.Count < GameState.MaxParty) { Game.State.Party.Add(pk); Game.Message.Show(pk.Name(Game.Project) + " joined your team!", null, -1, _ => next()); }
                    else { Game.State.Storage.Add(pk); Game.Message.Show(pk.Name(Game.Project) + " was sent to the PC.", null, -1, _ => next()); }
                });
            }
            if (o == BattleOutcome.Loss && !_setup.CanLose)
            {
                int lost = Math.Min(Game.State.Money, LossMoney());
                steps.Add(next =>
                {
                    Game.State.Money -= lost;
                    string text = _setup.Wild ? "\\PN panicked and lost $" + lost + "..." : "\\PN gave $" + lost + " to the winner...";
                    Game.Message.Show(text + "\\n\\PN blacked out!", null, -1, _ => next());
                });
            }
            // Evolution for Pokémon that levelled up (and are still standing)
            foreach (int i in _b.LeveledUp.OrderBy(x => x))
            {
                var pk = i < _party.Count ? _party[i] : null;
                if (pk == null || pk.Fainted || o == BattleOutcome.Loss) continue;
                steps.Add(next =>
                {
                    string to = Growth.CheckLevelEvolution(Game.Project, pk, ItemFlow.Context(Game));
                    if (to == null) { next(); return; }
                    Game.PushScene(new EvolutionScene(Game, pk, to, true, next));
                });
            }
            RunSteps(steps, 0, Finish);
        }

        static void RunSteps(List<Action<Action>> steps, int i, Action done)
        {
            if (i >= steps.Count) { done(); return; }
            steps[i](() => RunSteps(steps, i + 1, done));
        }

        int Prize()
        {
            var tr = _setup.Trainer;
            int baseMoney = _setup.TrainerType != null ? _setup.TrainerType.BaseMoney : 30;
            int level = tr != null && tr.Pokemon.Count > 0 ? tr.Pokemon.Max(p => p.Level) : 5;
            int total = baseMoney * level;
            var tr2 = _setup.Trainer2;
            if (tr2 != null && tr2.Pokemon.Count > 0) total += (_setup.TrainerType2 != null ? _setup.TrainerType2.BaseMoney : 30) * tr2.Pokemon.Max(p => p.Level);
            return total;
        }

        int LossMoney()
        {
            // Gen 4+: highest level × a factor that grows with the number of badges
            int level = _party.Count > 0 ? _party.Max(p => p.Level) : 1;
            int[] factor = { 8, 16, 24, 36, 48, 64, 80, 100, 120 };
            return level * factor[Math.Min(8, Game.State.Badges.Count)];
        }

        void Finish()
        {
            _mode = Mode.Done;
            Close();
            var o = _b.Outcome;
            if (o == BattleOutcome.Loss && !_setup.CanLose) Game.Whiteout();
            else Game.RestoreMapBgm();
            if (_setup.OnEnd != null) _setup.OnEnd(o);
        }

        // ------------------------------------------------------------------ drawing
        /// <summary>Where a Pokémon stands: centre x and the bottom of its sprite.</summary>
        void Pos(int side, int slot, out int x, out int bottom)
        {
            int slide = Math.Max(0, _introSlide);
            if (!_b.IsDouble) { if (side == 1) { x = 384 + slide * 2; bottom = 200; } else { x = 128 - slide * 2; bottom = 288; } return; }
            if (_b.IsTriple)
            {
                if (side == 1) { x = 300 + slot * 82 + slide * 2; bottom = 198 - slot * 6; }
                else { x = 56 + slot * 92 - slide * 2; bottom = 288; }
                return;
            }
            if (side == 1) { x = (slot == 0 ? 330 : 440) + slide * 2; bottom = slot == 0 ? 196 : 188; }
            else { x = (slot == 0 ? 92 : 212) - slide * 2; bottom = 288; }
        }

        public override void Draw(Gfx g)
        {
            DrawBackdrop(g);
            int tx, tb; Pos(1, 0, out tx, out tb);
            if (_b.IsDouble) tx = 384 + Math.Max(0, _introSlide) * 2;
            if (_trainerVisible && _setup.TrainerType != null)
            {
                bool two = _setup.TrainerType2 != null;
                var tex = Game.Gfx.Load(Game.Project.FindAsset("Graphics/Trainers", _setup.TrainerType.Id));
                if (tex != null) g.Draw(tex, 0, 0, tex.Width, tex.Height, (two ? tx - 56 : tx) - tex.Width / 2, 200 - tex.Height);
                if (two)
                {
                    var tex2 = Game.Gfx.Load(Game.Project.FindAsset("Graphics/Trainers", _setup.TrainerType2.Id));
                    if (tex2 != null) g.Draw(tex2, 0, 0, tex2.Width, tex2.Height, tx + 56 - tex2.Width / 2, 200 - tex2.Height);
                }
            }
            if (_player != null) _player.Draw(g, true);
            // Opponents (back row first), then the player's Pokémon
            for (int side = 1; side >= 0; side--)
                for (int slot = _b.SlotsPerSide - 1; slot >= 0; slot--)
                {
                    int k = Key(side, slot);
                    if (!_visible[k] || _shownParty[k] < 0) continue;
                    int x, bottom; Pos(side, slot, out x, out bottom);
                    if (_blink[k] > 0 && (_blink[k]-- / 2) % 2 == 0) continue;
                    var pk = _b.Parties[side][_shownParty[k]];
                    // Lunge toward the other side
                    if (_lunge[k] > 0) { int t = _lunge[k]--; int off = (int)(Math.Sin((12 - t) / 12.0 * Math.PI) * 24); x += side == 0 ? off : -off; bottom += side == 0 ? -off / 3 : off / 3; }
                    var st = _player != null ? _player.BattlerState(k) : null;
                    if (st != null) { DrawPokemonAnimated(g, pk, side == 0, st); continue; }
                    DrawPokemon(g, pk, side == 0, x, bottom + _faintDrop[k]);
                    if (_mode == Mode.Target && _targets.Count > _target && _targets[_target].Side == side && _targets[_target].Slot == slot && (Frame / 8) % 2 == 0)
                        Ellipse(g, x, bottom - 2, 30, 6, new Color(248, 232, 80));
                }
            if (_ballVisible) { int bx, bb; Pos(1, _ballSlot, out bx, out bb); DrawBall(g, bx, bb); }
            DrawEffects(g);
            if (_player != null) { _player.Draw(g, false); _player.DrawScreenFlash(g); }
            if (_formFlash > 0) { g.FillRect(0, 0, g.Width, 288, new Color(255, 255, 255, (byte)Math.Min(255, _formFlash * 10))); _formFlash--; }
            // Databoxes
            for (int slot = 0; slot < _b.SlotsPerSide; slot++)
            {
                int kf = Key(1, slot), km = Key(0, slot);
                if (_visible[kf] && _shownParty[kf] >= 0) DrawFoeBox(g, _b.Parties[1][_shownParty[kf]], kf, slot);
                if (_visible[km] && _shownParty[km] >= 0) DrawPlayerBox(g, _party[_shownParty[km]], km, slot);
            }
            // Bottom box
            if (!Game.Message.Busy)
            {
                if (_mode == Mode.Command) DrawCommands(g);
                if (ExternalWait && !Game.Message.Busy)
                {
                    MessageWindow.DrawBox(g, 4, g.Height - 100, g.Width - 8, 96);
                    Ui.Text(g, 24, g.Height - 86, "Waiting for " + (_setup.LinkOpponent ?? "the other player") + "...");
                }
                else if (_mode == Mode.Fight) DrawFight(g);
                else if (_mode == Mode.Target) DrawTarget(g);
                else
                {
                    MessageWindow.DrawBox(g, 4, g.Height - 100, g.Width - 8, 96);
                    if (_briefText != null && _mode == Mode.Events) g.DrawText(26, g.Height - 86, _briefText, Color.TextBase, Color.TextShadow);
                }
            }
        }

        static Color TypeColor(string type)
        {
            switch (type)
            {
                case "FIRE": return new Color(240, 128, 48); case "WATER": return new Color(104, 144, 240); case "ELECTRIC": return new Color(248, 208, 48);
                case "GRASS": return new Color(120, 200, 80); case "ICE": return new Color(152, 216, 216); case "FIGHTING": return new Color(192, 48, 40);
                case "POISON": return new Color(160, 64, 160); case "GROUND": return new Color(224, 192, 104); case "FLYING": return new Color(168, 144, 240);
                case "PSYCHIC": return new Color(248, 88, 136); case "BUG": return new Color(168, 184, 32); case "ROCK": return new Color(184, 160, 56);
                case "GHOST": return new Color(112, 88, 152); case "DRAGON": return new Color(112, 56, 248); case "DARK": return new Color(112, 88, 72);
                case "STEEL": return new Color(184, 184, 208); case "FAIRY": return new Color(238, 153, 172); default: return new Color(232, 232, 216);
            }
        }

        /// <summary>Hit bursts and stat arrows over the battlers.</summary>
        void DrawEffects(Gfx g)
        {
            if (_burstFrames > 0 && _burstKey >= 0)
            {
                int x, bottom; Pos(_burstKey / 2, _burstKey % 2, out x, out bottom);
                int cy = bottom - 56, t = 18 - _burstFrames;
                var rng = new Random(_burstKey * 31 + 7);
                byte a = (byte)Math.Max(0, 255 - t * 12);
                var c = new Color(_burstColor.R, _burstColor.G, _burstColor.B, a);
                int n = _lastCategory == "Special" ? 18 : 12;
                for (int i = 0; i < n; i++)
                {
                    double ang = rng.NextDouble() * Math.PI * 2, sp = 3 + rng.NextDouble() * 4;
                    int px = x + (int)(Math.Cos(ang) * sp * t), py = cy + (int)(Math.Sin(ang) * sp * t);
                    int sz = _lastCategory == "Special" ? 11 : 8;
                    g.FillRect(px - sz / 2, py - sz / 2, sz, sz, c);
                    g.FillRect(px - sz / 4, py - sz / 4, sz / 2, sz / 2, new Color(255, 255, 255, a));
                }
                if (t < 4) g.FillRect(x - 24 + t * 2, cy - 24 + t * 2, 48 - t * 4, 48 - t * 4, new Color(255, 255, 255, (byte)(160 - t * 40)));
                _burstFrames--;
            }
            if (_statFrames > 0 && _statKey >= 0)
            {
                int x, bottom; Pos(_statKey / 2, _statKey % 2, out x, out bottom);
                int t = 24 - _statFrames;
                var c = _statDir > 0 ? new Color(248, 136, 48, 200) : new Color(72, 120, 232, 200);
                for (int i = 0; i < 5; i++)
                {
                    int ax = x - 48 + i * 24, ay = bottom - 30 - ((t * 4 + i * 20) % 90) * _statDir;
                    if (_statDir < 0) ay = bottom - 120 + ((t * 4 + i * 20) % 90);
                    for (int r = 0; r < 8; r++)
                    {
                        int w = _statDir > 0 ? r * 2 + 2 : (8 - r) * 2;
                        g.FillRect(ax - w / 2, ay + r, w, 1, c);
                    }
                    g.FillRect(ax - 3, ay + (_statDir > 0 ? 8 : -8), 6, 8, c);
                }
                _statFrames--;
            }
        }

        void DrawBackdrop(Gfx g)
        {
            string bd = string.IsNullOrEmpty(_setup.Backdrop) ? "field" : _setup.Backdrop;
            var bg = Game.Gfx.Load(Game.Project.FindAsset("Graphics/Battlebacks", bd + "_bg"));
            if (bg != null) g.Draw(bg, 0, 0, bg.Width, bg.Height, 0, 0);
            else Ui.Gradient(g, 0, 0, g.Width, 288, new Color(160, 216, 248), new Color(232, 248, 200));
            g.FillRect(0, 288, g.Width, g.Height - 288, new Color(40, 48, 56));
            var b1 = Game.Gfx.Load(Game.Project.FindAsset("Graphics/Battlebacks", bd + "_base1"));
            if (b1 != null) g.Draw(b1, 0, 0, b1.Width, b1.Height, 384 - b1.Width / 2, 176 - b1.Height / 2);
            else Ellipse(g, 384, 192, 120, 22, new Color(150, 200, 110));
            var b0 = Game.Gfx.Load(Game.Project.FindAsset("Graphics/Battlebacks", bd + "_base0"));
            if (b0 != null) g.Draw(b0, 0, 0, b0.Width, b0.Height, 128 - b0.Width / 2, 288 - b0.Height);
            else Ellipse(g, 128, 280, 140, 22, new Color(150, 200, 110));
        }

        static void Ellipse(Gfx g, int cx, int cy, int rx, int ry, Color c)
        {
            for (int y = -ry; y <= ry; y++)
            {
                int w = (int)(rx * Math.Sqrt(1 - (double)y * y / (ry * ry)));
                g.FillRect(cx - w, cy + y, w * 2, 1, c);
            }
        }

        Texture BattlerTexture(Pokemon pk, bool back, out int frameW)
        {
            string folder = back ? (pk.Shiny ? "Graphics/Pokemon/Back shiny" : "Graphics/Pokemon/Back") : (pk.Shiny ? "Graphics/Pokemon/Front shiny" : "Graphics/Pokemon/Front");
            string id = pk.Form > 0 ? pk.Species + "_" + pk.Form : pk.Species;
            var path = Game.Project.FindAsset(folder, id) ?? Game.Project.FindAsset(back ? "Graphics/Pokemon/Back" : "Graphics/Pokemon/Front", pk.Species);
            var tex = Game.Gfx.Load(path);
            frameW = tex == null ? 0 : tex.Width > tex.Height * 3 / 2 ? tex.Height : tex.Width; // animated strips: first frame
            return tex;
        }

        /// <summary>A Pokémon moved, zoomed, rotated or tinted by a battle animation.</summary>
        void DrawPokemonAnimated(Gfx g, Pokemon pk, bool back, AnimSpriteState st)
        {
            if (!st.Visible) return;
            int fw;
            var tex = BattlerTexture(pk, back, out fw);
            if (tex == null) { Ui.PokemonFront(g, Game, pk, (int)st.X, (int)st.Y + 48, 160); return; }
            g.DrawEx(tex, 0, 0, fw, tex.Height, st.X, st.Y, st.ZoomX, st.ZoomY, st.Angle, st.Mirror, st.Opacity, st.Blend, st.Color);
        }

        void DrawPokemon(Gfx g, Pokemon pk, bool back, int cx, int bottom)
        {
            int fwUnused;
            var tex = BattlerTexture(pk, back, out fwUnused);
            if (tex == null)
            {
                if (back) { Ui.PokemonFront(g, Game, pk, cx, bottom, 160); return; } // no back sprite: show the front one
                Ui.PokemonFront(g, Game, pk, cx, bottom, 160);
                return;
            }
            int fw = tex.Width > tex.Height * 3 / 2 ? tex.Height : tex.Width; // animated strips: first frame
            // Clip what falls below the battle area (fainting slides down out of view)
            int h = Math.Min(tex.Height, Math.Max(0, 288 - (bottom - tex.Height)));
            if (h <= 0) return;
            if (pk.ShadowPokemon) { g.DrawEx(tex, 0, 0, fw, h, cx, bottom - tex.Height + h / 2.0, 1, 1, 0, false, 255, 0, new Color(112, 40, 160, 120)); return; }
            g.Draw(tex, 0, 0, fw, h, cx - fw / 2, bottom - tex.Height);
        }

        void DrawBall(Gfx g, int foeX, int foeBottom)
        {
            int x, y;
            if (_current != null && _current.Kind == BattleEventKind.Catch && _anim < 30)
            {
                double t = _anim / 30.0;
                x = (int)(100 + (foeX - 100) * t); y = (int)(260 - 260 * t + 330 * t * t - 120 * t);
            }
            else
            {
                x = foeX; y = foeBottom - 16;
                if (_current != null && _current.Kind == BattleEventKind.Catch && _anim > 30)
                {
                    int phase = (_anim - 30) % 30;
                    if ((_anim - 30) / 30 < _ballShakes && phase < 12) x += (phase < 6 ? -1 : 1) * 4;
                }
            }
            var top = _ballCaught && (_current == null || _anim > 30 + _ballShakes * 30) ? new Color(160, 40, 40) : new Color(232, 48, 40);
            Ellipse(g, x, y, 10, 10, new Color(40, 40, 48));
            for (int yy = -9; yy <= 9; yy++)
            {
                int w = (int)(9 * Math.Sqrt(1 - yy * yy / 81.0));
                g.FillRect(x - w, y + yy, w * 2, 1, yy < 0 ? top : new Color(248, 248, 248));
            }
            g.FillRect(x - 9, y - 1, 18, 2, new Color(40, 40, 48));
            g.FillRect(x - 2, y - 2, 4, 4, new Color(248, 248, 248));
        }

        void DrawFoeBox(Gfx g, Pokemon pk, int k, int slot)
        {
            bool dbl = _b.IsDouble;
            if (_b.IsTriple)
            {
                // Triple battles: three slim boxes (name, level, HP bar)
                int tx2 = 8, ty2 = 4 + slot * 44, tw = 220, th = 42;
                Ui.Panel(g, tx2, ty2, tw, th, new Color(248, 248, 232), new Color(64, 80, 72));
                Ui.Text(g, tx2 + 10, ty2 - 5, pk.Name(Game.Project));
                Ui.TextRight(g, tx2 + tw - 10, ty2 - 5, "Lv" + pk.Level);
                string st3 = Ui.StatusAbbrev(pk.Status);
                if (st3 != "") g.DrawText(tx2 + 10, ty2 + 22, st3, new Color(232, 64, 48), Ui.DarkShadow);
                Ui.HpBar(g, tx2 + 70, ty2 + 34, 130, (int)Math.Round(_shownHp[k]), pk.TotalHP(Game.Project));
                return;
            }
            int x = 16, y = dbl ? 8 + slot * 54 : 30, w = 236, h = dbl ? 50 : 62;
            Ui.Panel(g, x, y, w, h, new Color(248, 248, 232), new Color(64, 80, 72));
            Ui.Text(g, x + 12, y + 2, pk.Name(Game.Project));
            Ui.GenderSymbol(g, x + 12 + g.TextWidth(pk.Name(Game.Project)) + 4, y + 2, pk.Gender);
            Ui.TextRight(g, x + w - 12, y + 2, "Lv" + pk.Level);
            int row = dbl ? 24 : 30;
            if (_setup.Wild && Game.State.Caught.Contains(pk.Species)) { g.FillRect(x + w - 26, y + row + 8, 10, 10, new Color(232, 56, 48)); g.FillRect(x + w - 26, y + row + 12, 10, 2, new Color(40, 40, 48)); }
            string st = Ui.StatusAbbrev(pk.Status);
            if (st != "") g.DrawText(x + 12, y + row, st, new Color(232, 64, 48), Ui.DarkShadow);
            g.DrawText(x + 72, y + row, "HP", new Color(248, 176, 0), Ui.DarkShadow);
            Ui.HpBar(g, x + 104, y + row + 12, 100, (int)Math.Round(_shownHp[k]), pk.TotalHP(Game.Project));
        }

        void DrawPlayerBox(Gfx g, Pokemon pk, int k, int slot)
        {
            bool dbl = _b.IsDouble;
            if (_b.IsTriple)
            {
                int tx2 = 290, ty2 = 166 + slot * 41, tw = 216, th = 40;
                Ui.Panel(g, tx2, ty2, tw, th, new Color(248, 248, 232), new Color(64, 80, 72));
                Ui.Text(g, tx2 + 10, ty2 - 5, pk.Name(Game.Project));
                Ui.TextRight(g, tx2 + tw - 10, ty2 - 5, "Lv" + pk.Level);
                Ui.HpBar(g, tx2 + 60, ty2 + 28, 140, (int)Math.Round(_shownHp[k]), pk.TotalHP(Game.Project));
                Ui.ExpBar(g, tx2 + 60, ty2 + th - 3, 140, _shownExp[Math.Min(2, slot)]);
                return;
            }
            int x = 262, y = dbl ? 196 + slot * 46 : 192, w = 240, h = dbl ? 44 : 88;
            Ui.Panel(g, x, y, w, h, new Color(248, 248, 232), new Color(64, 80, 72));
            Ui.Text(g, x + 16, y + 2, pk.Name(Game.Project));
            Ui.GenderSymbol(g, x + 16 + g.TextWidth(pk.Name(Game.Project)) + 4, y + 2, pk.Gender);
            Ui.TextRight(g, x + w - 12, y + 2, "Lv" + pk.Level);
            int max = pk.TotalHP(Game.Project);
            string st = Ui.StatusAbbrev(pk.Status);
            if (dbl)
            {
                // Compact box: no HP numbers (the bar shows how much is left)
                if (st != "") g.DrawText(x + w - 60, y + 22, st, new Color(232, 64, 48), Ui.DarkShadow);
                Ui.HpBar(g, x + 40, y + 30, 150, (int)Math.Round(_shownHp[k]), max);
                Ui.ExpBar(g, x + 40, y + h - 5, 150, _shownExp[Math.Min(2, slot)]);
                return;
            }
            if (st != "") g.DrawText(x + 16, y + 28, st, new Color(232, 64, 48), Ui.DarkShadow);
            g.DrawText(x + 76, y + 28, "HP", new Color(248, 176, 0), Ui.DarkShadow);
            Ui.HpBar(g, x + 108, y + 40, 112, (int)Math.Round(_shownHp[k]), max);
            Ui.TextRight(g, x + w - 16, y + 46, (int)Math.Round(_shownHp[k]) + "/" + max);
            Ui.ExpBar(g, x + 40, y + h - 10, w - 60, _shownExp[0]);
        }

        void DrawTarget(Gfx g)
        {
            int by = g.Height - 100;
            Ui.Panel(g, 4, by, g.Width - 8, 96, new Color(248, 248, 248), new Color(96, 88, 120));
            Ui.Text(g, 24, by + 10, "Choose a target:");
            for (int i = 0; i < _targets.Count; i++)
            {
                var t = _targets[i];
                int cx = 40 + (i % 3) * 160, cy = by + 44 + (i / 3) * 26;
                if (i == _target) Ui.Arrow(g, cx - 18, cy + 4);
                Ui.Text(g, cx, cy, (t.Side == 0 ? "Ally " : "") + t.Pk.Name(Game.Project));
            }
        }

        void DrawCommands(Gfx g)
        {
            int by = g.Height - 100;
            MessageWindow.DrawBox(g, 4, by, 260, 96);
            Ui.Text(g, 24, by + 14, "What will");
            Ui.Text(g, 24, by + 46, (_b.Safari ? Game.State.PlayerName : Me.Pk.Name(Game.Project)) + " do?");
            Ui.Panel(g, 268, by, g.Width - 272, 96, new Color(248, 248, 248), new Color(96, 88, 120));
            for (int i = 0; i < 4; i++)
            {
                int cx = 296 + (i % 2) * 110, cy = by + 14 + (i / 2) * 36;
                if (i == _cmd) Ui.Arrow(g, cx - 18, cy + 6);
                Ui.Text(g, cx, cy, _b.Safari ? SafariCommands[i] : _setup.BugContest && i == 1 ? "BALL x" + World.BugContest.State(Game).Balls : Commands[i]);
            }
            if (_b.Safari)
            {
                string balls = "Safari Balls: " + World.Safari.State(Game).Balls;
                int bw = g.TextWidth(balls) + 20;
                Ui.Panel(g, g.Width - bw - 4, by - 34, bw, 30, new Color(248, 248, 248), new Color(96, 88, 120));
                g.DrawText(g.Width - bw + 6, by - 32, balls, Ui.Dark, Ui.DarkShadow);
            }
        }

        void DrawFight(Gfx g)
        {
            int by = g.Height - 100;
            Ui.Panel(g, 4, by, 340, 96, new Color(248, 248, 248), new Color(96, 88, 120));
            var moves = Me.Pk.Moves;
            for (int i = 0; i < 4; i++)
            {
                int cx = 30 + (i % 2) * 160, cy = by + 14 + (i / 2) * 36;
                if (i == _move) Ui.Arrow(g, cx - 18, cy + 6);
                Ui.Text(g, cx, cy, i < moves.Count ? ItemEffects.MoveName(Game.Project, moves[i].Id) : "-");
            }
            Ui.Panel(g, 348, by, g.Width - 352, 96, new Color(248, 248, 248), new Color(96, 88, 120));
            if (_b.CanMegaEvolve(Me) && !_pending.Any(x => x.Mega))
            {
                string mega = _megaOn ? "MEGA EVOLVE: ON" : "Shift: Mega Evolve";
                int mw = g.TextWidth(mega) + 20;
                Ui.Panel(g, 4, by - 34, mw, 30, _megaOn ? new Color(248, 200, 96) : new Color(248, 248, 248), new Color(96, 88, 120));
                g.DrawText(14, by - 32, mega, Ui.Dark, Ui.DarkShadow);
            }
            if (_move < moves.Count)
            {
                var m = moves[_move];
                var md = Game.Project.Moves.FirstOrDefault(x => x.Id == m.Id);
                int total = md != null ? Pokemon.TotalPP(md, m.PPUps) : m.PP;
                var ppColor = m.PP == 0 ? new Color(232, 64, 48) : m.PP <= total / 4 ? new Color(232, 136, 40) : Ui.Dark;
                g.DrawText(362, by + 14, "PP", Ui.Dark, Ui.DarkShadow);
                g.DrawText(g.Width - 20 - g.TextWidth(m.PP + "/" + total), by + 14, m.PP + "/" + total, ppColor, Ui.DarkShadow);
                if (md != null)
                {
                    var t = Game.Project.Types.FirstOrDefault(x => x.Id == md.Type);
                    Ui.Text(g, 362, by + 50, "TYPE/" + (t != null && t.Name != null ? t.Name : md.Type));
                }
            }
        }
    }
}
