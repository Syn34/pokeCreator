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
        public string Backdrop = "field";
        public bool CanLose;               // losing doesn't black out (e.g. a rival battle you may lose)
        public Action<BattleOutcome> OnEnd;
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
        enum Mode { Intro, Events, Command, Fight, Busy, Ending, Done }
        Mode _mode = Mode.Intro;
        int _cmd, _move, _anim;
        BattleEvent _current;
        readonly double[] _shownHp = new double[2];
        readonly int[] _shownParty = { -1, -1 };     // which party member each side's databox/sprite shows
        readonly bool[] _visible = new bool[2];
        readonly int[] _faintDrop = new int[2];
        readonly int[] _blink = new int[2];
        double _shownExp;
        bool _trainerVisible, _ballVisible, _ballCaught;
        int _ballShakes;
        int _introSlide = 120;
        readonly List<Pokemon> _party;

        public BattleScene(Game g, Battle battle, BattleSetup setup) : base(g)
        {
            Opaque = true; _b = battle; _setup = setup; _party = battle.Parties[0];
            _trainerVisible = !setup.Wild;
            foreach (var e in battle.Start()) _queue.Enqueue(e);
            // Seen in the Pokédex as soon as it appears
            foreach (var pk in battle.Parties[1]) if (setup.Wild) g.State.Seen.Add(pk.Species);
            string bgm = setup.Wild ? g.Project.Settings.WildBattleBGM : (setup.TrainerType != null && !string.IsNullOrEmpty(setup.TrainerType.BattleBGM) ? setup.TrainerType.BattleBGM : g.Project.Settings.TrainerBattleBGM);
            if (string.IsNullOrEmpty(bgm)) bgm = setup.Wild ? "Battle wild" : "Battle trainer";
            g.Audio.PlayBgm(bgm);
        }

        Battler Me { get { return _b.Active[0]; } }

        // ------------------------------------------------------------------ update
        protected override void Update()
        {
            if (_introSlide > 0) { _introSlide -= 6; return; }
            switch (_mode)
            {
                case Mode.Intro: _mode = Mode.Events; break;
                case Mode.Events: PlayEvents(); break;
                case Mode.Command: CommandMenu(); break;
                case Mode.Fight: FightMenu(); break;
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
            if (_b.Outcome != BattleOutcome.Ongoing) { EndBattle(); return; }
            if (_b.NeedPlayerSwitch) { ChooseReplacement(); return; }
            _mode = Mode.Command;
            if (_cmd < 0 || _cmd > 3) _cmd = 0;
        }

        /// <summary>Starts showing an event. Returns true if it animates over several frames.</summary>
        bool Begin(BattleEvent e)
        {
            switch (e.Kind)
            {
                case BattleEventKind.Message: Game.Message.Show(e.Text); return false;
                case BattleEventKind.SendOut:
                    if (e.Side == 1) _trainerVisible = false;
                    _shownParty[e.Side] = e.Party; _visible[e.Side] = true; _faintDrop[e.Side] = 0;
                    _shownHp[e.Side] = _b.Parties[e.Side][e.Party].HP;
                    if (e.Side == 0) _shownExp = ExpFraction(_party[e.Party]);
                    Game.Audio.PlaySe("Battle recall");
                    Game.Audio.PlaySe("Cries/" + _b.Parties[e.Side][e.Party].Species);
                    return true;
                case BattleEventKind.Withdraw: _visible[e.Side] = false; Game.Audio.PlaySe("Battle recall"); return true;
                case BattleEventKind.Hp: return true;
                case BattleEventKind.Hit:
                    Game.Audio.PlaySe(e.Value > 100 ? "Battle damage super" : e.Value < 100 ? "Battle damage weak" : "Battle damage normal");
                    _blink[e.Side] = 16; return true;
                case BattleEventKind.Faint: Game.Audio.PlaySe("Pkmn faint"); return true;
                case BattleEventKind.StatChange: Game.Audio.PlaySe(e.Value > 0 ? "Battle stat up" : "Battle stat down"); return true;
                case BattleEventKind.Exp:
                    if (e.Party != _shownParty[0]) return false;
                    Game.Audio.PlaySe("Pkmn exp gain"); return true;
                case BattleEventKind.LevelUp:
                    {
                        var pk = _party[e.Party];
                        if (e.Party == _shownParty[0]) { _shownHp[0] = pk.HP; _shownExp = 0; }
                        Game.Audio.PlayMe("Pkmn level up");
                        _mode = Mode.Busy;
                        Game.Message.Show(e.Text, null, -1, _ =>
                            ItemFlow.LearnMoves(Game, pk, new Queue<string>(e.Moves ?? new List<string>()), () => _mode = Mode.Events));
                        return false;
                    }
                case BattleEventKind.Catch:
                    if (e.Value < 0) return false; // the trainer blocked it
                    _ballVisible = true; _ballCaught = e.Flag; _ballShakes = e.Value;
                    Game.Audio.PlaySe("Battle throw");
                    return true;
                case BattleEventKind.Flee: Game.Audio.PlaySe("Battle flee"); return false;
                default: return false;
            }
        }

        /// <summary>Advances the current animation; true when finished.</summary>
        bool StepAnimation()
        {
            var e = _current;
            _anim++;
            switch (e.Kind)
            {
                case BattleEventKind.Hp:
                    {
                        if (e.Party != _shownParty[e.Side]) return true;
                        double target = e.Value2;
                        int max = Math.Max(1, _b.Parties[e.Side][e.Party].TotalHP(Game.Project));
                        double step = Math.Max(1, max / 48.0);
                        if (Math.Abs(_shownHp[e.Side] - target) <= step) { _shownHp[e.Side] = target; return true; }
                        _shownHp[e.Side] += Math.Sign(target - _shownHp[e.Side]) * step;
                        return false;
                    }
                case BattleEventKind.Hit: return _anim >= 16;
                case BattleEventKind.Faint:
                    _faintDrop[e.Side] = _anim * 8;
                    if (_anim >= 20) { _visible[e.Side] = false; return true; }
                    return false;
                case BattleEventKind.SendOut: return _anim >= 12;
                case BattleEventKind.Withdraw: return _anim >= 10;
                case BattleEventKind.StatChange: return _anim >= 14;
                case BattleEventKind.Exp:
                    {
                        var pk = _party[e.Party];
                        double target = ExpFractionAt(pk, e.Value2);
                        if (_shownExp >= target - 0.01 || _anim > 90) { _shownExp = target; return true; }
                        _shownExp = Math.Min(target, _shownExp + 0.02);
                        return false;
                    }
                case BattleEventKind.Catch:
                    {
                        // 0-30 throw, then 30 frames per shake, then burst or click
                        int end = 30 + _ballShakes * 30 + 20;
                        if (_anim == 30) _visible[1] = false;
                        if (_anim > 30 && (_anim - 30) % 30 == 0 && (_anim - 30) / 30 <= _ballShakes) Game.Audio.PlaySe("Battle ball shake");
                        if (_anim >= end)
                        {
                            if (_ballCaught) { Game.Audio.PlaySe("Battle catch click"); Game.Audio.PlayMe("Battle capture success"); }
                            else { _ballVisible = false; _visible[1] = true; Game.Audio.PlaySe("Battle recall"); }
                            return true;
                        }
                        return false;
                    }
                default: return true;
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

        void CommandMenu()
        {
            if (Input.Repeat(Button.Right) && _cmd % 2 == 0) { _cmd++; Cursor(); }
            if (Input.Repeat(Button.Left) && _cmd % 2 == 1) { _cmd--; Cursor(); }
            if (Input.Repeat(Button.Down) && _cmd < 2) { _cmd += 2; Cursor(); }
            if (Input.Repeat(Button.Up) && _cmd >= 2) { _cmd -= 2; Cursor(); }
            if (Input.Trigger(Button.Cancel) && _cmd != 3) { _cmd = 3; Cursor(); return; }
            if (!Input.Trigger(Button.Action)) return;
            Decide();
            switch (_cmd)
            {
                case 0:
                    if (Me.Charging != null || Me.MustRecharge || !_b.HasUsableMove(Me)) { Run(_b.DoTurn(BattleCommand.Fight(-1))); return; }
                    _mode = Mode.Fight; if (_move >= Me.Pk.Moves.Count) _move = 0; break;
                case 1: OpenBag(); break;
                case 2: OpenParty(); break;
                case 3: Run(_b.DoTurn(BattleCommand.Run())); break;
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
            if (!Input.Trigger(Button.Action)) return;
            if (Me.Pk.Moves[_move].PP <= 0) { Buzz(); Game.Message.Show("There's no PP left for this move!"); return; }
            Decide();
            Run(_b.DoTurn(BattleCommand.Fight(_move)));
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
            _mode = Mode.Busy;
            Game.PushScene(new BagScene(Game, id =>
            {
                var item = Game.Project.Items.FirstOrDefault(i => i.Id == id);
                if (item == null || !BattleItem(item)) { Game.Message.Show("That can't be used now.", null, -1, _ => _mode = Mode.Command); return; }
                if (item.Pocket == 3)
                {
                    if (!_setup.Wild) { Run(_b.DoTurn(BattleCommand.Ball(id))); return; }  // blocked, but still uses the turn
                    Game.State.AddItem(id, -1);
                    Run(_b.DoTurn(BattleCommand.Ball(id)));
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
                    Run(_b.DoTurn(BattleCommand.UseItem(id, i)));
                }, () => _mode = Mode.Command));
            }, BattleItem, () => _mode = Mode.Command));
        }

        void OpenParty()
        {
            _mode = Mode.Busy;
            Game.PushScene(new PartyScene(Game, "Choose a Pokémon.", null, (i, close) =>
            {
                var pk = _party[i];
                if (i == Me.PartyIndex) { Game.Message.Show(pk.Name(Game.Project) + " is already in battle!"); return; }
                if (!Battle.Able(pk)) { Game.Message.Show(pk.IsEgg ? "An Egg can't battle!" : pk.Name(Game.Project) + " has no energy left to battle!"); return; }
                close();
                Run(_b.DoTurn(BattleCommand.Switch(i)));
            }, () => _mode = Mode.Command));
        }

        void ChooseReplacement()
        {
            _mode = Mode.Busy;
            Game.PushScene(new PartyScene(Game, "Choose a Pokémon to send out.", null, (i, close) =>
            {
                var pk = _party[i];
                if (!Battle.Able(pk)) { Game.Message.Show(pk.IsEgg ? "An Egg can't battle!" : pk.Name(Game.Project) + " has no energy left to battle!"); return; }
                close();
                Run(_b.SwitchAfterFaint(i));
            }, () => ChooseReplacement())); // must choose one
        }

        // ------------------------------------------------------------------ ending
        void EndBattle()
        {
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
                steps.Add(next =>
                {
                    if (Game.State.Party.Count < GameState.MaxParty) { Game.State.Party.Add(pk); next(); }
                    else { Game.State.Storage.Add(pk); Game.Message.Show(pk.Name(Game.Project) + " was sent to the PC.", null, -1, _ => next()); }
                });
            }
            else if (o == BattleOutcome.Loss && !_setup.CanLose)
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
            return baseMoney * level;
        }

        int LossMoney()
        {
            // Gen 4+: highest level × a factor that grows with badges (no badges yet: 8)
            int level = _party.Count > 0 ? _party.Max(p => p.Level) : 1;
            return level * 8;
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
        public override void Draw(Gfx g)
        {
            DrawBackdrop(g);
            int slide = Math.Max(0, _introSlide);
            // Opponent
            int foeX = 384 + slide * 2, foeBottom = 200;
            if (_trainerVisible && _setup.TrainerType != null)
            {
                var tex = Game.Gfx.Load(Game.Project.FindAsset("Graphics/Trainers", _setup.TrainerType.Id));
                if (tex != null) g.Draw(tex, 0, 0, tex.Width, tex.Height, foeX - tex.Width / 2, foeBottom - tex.Height);
            }
            if (_visible[1] && _shownParty[1] >= 0 && !(_blink[1] > 0 && (_blink[1]-- / 2) % 2 == 0))
                DrawPokemon(g, _b.Parties[1][_shownParty[1]], false, foeX, foeBottom + _faintDrop[1]);
            if (_ballVisible) DrawBall(g, foeX, foeBottom);
            // Player
            int meX = 128 - slide * 2, meBottom = 288;
            if (_visible[0] && _shownParty[0] >= 0 && !(_blink[0] > 0 && (_blink[0]-- / 2) % 2 == 0))
                DrawPokemon(g, _party[_shownParty[0]], true, meX, meBottom + _faintDrop[0]);
            // Databoxes
            if (_visible[1] && _shownParty[1] >= 0) DrawFoeBox(g, _b.Parties[1][_shownParty[1]]);
            if (_visible[0] && _shownParty[0] >= 0) DrawPlayerBox(g, _party[_shownParty[0]]);
            // Bottom box
            if (!Game.Message.Busy)
            {
                if (_mode == Mode.Command) DrawCommands(g);
                else if (_mode == Mode.Fight) DrawFight(g);
                else MessageWindow.DrawBox(g, 4, g.Height - 100, g.Width - 8, 96);
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

        void DrawPokemon(Gfx g, Pokemon pk, bool back, int cx, int bottom)
        {
            string folder = back ? (pk.Shiny ? "Graphics/Pokemon/Back shiny" : "Graphics/Pokemon/Back") : (pk.Shiny ? "Graphics/Pokemon/Front shiny" : "Graphics/Pokemon/Front");
            string id = pk.Form > 0 ? pk.Species + "_" + pk.Form : pk.Species;
            var path = Game.Project.FindAsset(folder, id) ?? Game.Project.FindAsset(back ? "Graphics/Pokemon/Back" : "Graphics/Pokemon/Front", pk.Species);
            var tex = Game.Gfx.Load(path);
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

        void DrawFoeBox(Gfx g, Pokemon pk)
        {
            int x = 16, y = 30, w = 236, h = 62;
            Ui.Panel(g, x, y, w, h, new Color(248, 248, 232), new Color(64, 80, 72));
            Ui.Text(g, x + 12, y + 2, pk.Name(Game.Project));
            Ui.GenderSymbol(g, x + 12 + g.TextWidth(pk.Name(Game.Project)) + 4, y + 2, pk.Gender);
            Ui.TextRight(g, x + w - 12, y + 2, "Lv" + pk.Level);
            if (_setup.Wild && Game.State.Caught.Contains(pk.Species)) { g.FillRect(x + w - 26, y + 38, 10, 10, new Color(232, 56, 48)); g.FillRect(x + w - 26, y + 42, 10, 2, new Color(40, 40, 48)); }
            string st = Ui.StatusAbbrev(pk.Status);
            if (st != "") g.DrawText(x + 12, y + 30, st, new Color(232, 64, 48), Ui.DarkShadow);
            g.DrawText(x + 72, y + 30, "HP", new Color(248, 176, 0), Ui.DarkShadow);
            Ui.HpBar(g, x + 104, y + 42, 100, (int)Math.Round(_shownHp[1]), pk.TotalHP(Game.Project));
        }

        void DrawPlayerBox(Gfx g, Pokemon pk)
        {
            int x = 262, y = 192, w = 240, h = 88;
            Ui.Panel(g, x, y, w, h, new Color(248, 248, 232), new Color(64, 80, 72));
            Ui.Text(g, x + 16, y + 2, pk.Name(Game.Project));
            Ui.GenderSymbol(g, x + 16 + g.TextWidth(pk.Name(Game.Project)) + 4, y + 2, pk.Gender);
            Ui.TextRight(g, x + w - 12, y + 2, "Lv" + pk.Level);
            string st = Ui.StatusAbbrev(pk.Status);
            if (st != "") g.DrawText(x + 16, y + 28, st, new Color(232, 64, 48), Ui.DarkShadow);
            g.DrawText(x + 76, y + 28, "HP", new Color(248, 176, 0), Ui.DarkShadow);
            int max = pk.TotalHP(Game.Project);
            Ui.HpBar(g, x + 108, y + 40, 112, (int)Math.Round(_shownHp[0]), max);
            Ui.TextRight(g, x + w - 16, y + 46, (int)Math.Round(_shownHp[0]) + "/" + max);
            Ui.ExpBar(g, x + 40, y + h - 10, w - 60, _shownExp);
        }

        void DrawCommands(Gfx g)
        {
            int by = g.Height - 100;
            MessageWindow.DrawBox(g, 4, by, 260, 96);
            Ui.Text(g, 24, by + 14, "What will");
            Ui.Text(g, 24, by + 46, Me.Pk.Name(Game.Project) + " do?");
            Ui.Panel(g, 268, by, g.Width - 272, 96, new Color(248, 248, 248), new Color(96, 88, 120));
            for (int i = 0; i < 4; i++)
            {
                int cx = 296 + (i % 2) * 110, cy = by + 14 + (i / 2) * 36;
                if (i == _cmd) Ui.Arrow(g, cx - 18, cy + 6);
                Ui.Text(g, cx, cy, Commands[i]);
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
