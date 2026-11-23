using System;
using System.Collections.Generic;
using System.Linq;
using PokeCreator.Data;
using PokeCreator.Maps;
using PokeCreator.Runtime.Platform;
using PokeCreator.Runtime.Script;

namespace PokeCreator.Runtime.World
{
    /// <summary>A map event: picks the active page from its conditions and runs its commands.</summary>
    public class GameEvent : Character
    {
        public readonly MapEvent Data;
        public readonly GameMap Owner;
        public EventPage Page;
        public bool Erased;
        /// <summary>Showing a planted berry's sprite instead of the page's.</summary>
        public bool BerryOverride;
        public int Trigger = -1;
        public List<EventCommand> List;
        public bool Starting;
        public Interpreter ParallelInterpreter;
        int _lockedDirection;
        bool _locked;

        public int Id { get { return Data.Id; } }
        public string Name { get { return Data.Name; } }
        public override GameMap Map { get { return Owner; } }

        public GameEvent(Game game, GameMap owner, MapEvent data) : base(game)
        {
            Data = data; Owner = owner;
            MoveTo(data.X, data.Y);
            Refresh();
        }

        bool ConditionsMet(EventPage p)
        {
            var c = p.Condition; var s = Game.State;
            if (c.Switch1Valid && !Game.SwitchOn(c.Switch1Id, Owner.Id, Id)) return false;
            if (c.Switch2Valid && !Game.SwitchOn(c.Switch2Id, Owner.Id, Id)) return false;
            if (c.VariableValid && s.GetVar(c.VariableId) < c.VariableValue) return false;
            if (c.SelfSwitchValid && !s.GetSelf(Owner.Id, Id, c.SelfSwitchCh)) return false;
            return true;
        }

        public void Refresh()
        {
            EventPage newPage = null;
            if (!Erased)
                for (int i = Data.Pages.Count - 1; i >= 0; i--)
                    if (ConditionsMet(Data.Pages[i])) { newPage = Data.Pages[i]; break; }
            if (newPage == Page) return;
            Page = newPage;
            Starting = false;
            if (Page == null)
            {
                TileId = 0; CharacterName = ""; Through = true; Trigger = -1; List = null; ParallelInterpreter = null;
                return;
            }
            var g = Page.Graphic;
            TileId = g.TileId; CharacterName = g.CharacterName ?? "";
            if (Direction != g.Direction || !_locked) { Direction = g.Direction == 0 ? 2 : g.Direction; }
            OriginalPattern = Pattern = g.Pattern;
            Opacity = g.Opacity;
            MoveSpeed = Page.MoveSpeed; MoveFrequency = Page.MoveFrequency;
            WalkAnime = Page.WalkAnime; StepAnime = Page.StepAnime; DirectionFix = Page.DirectionFix;
            Through = Page.Through; AlwaysOnTop = Page.AlwaysOnTop;
            Trigger = Page.Trigger; List = Page.List;
            SetCustomRoute(Page.MoveType == 3 ? Page.MoveRoute : null);
            ParallelInterpreter = Trigger == Triggers.Parallel ? new Interpreter(Game, Owner.Id, Id, List) : null;
            if (Trigger == Triggers.Autorun) Start();
            BerryOverride = false;
            Berries.ApplyGraphic(Game, this);
        }

        public bool HasCommands { get { return List != null && List.Any(c => c.Code != 0); } }

        public void Start() { if (HasCommands) Starting = true; }

        public void Erase() { Erased = true; Refresh(); }

        /// <summary>
        /// Events with no sprite (or Through) are stepped on rather than bumped into, unless their tile is
        /// blocked (a sign on a wall, a bookshelf...): then the player triggers them from in front, as in RPG Maker XP.
        /// </summary>
        public bool OverTrigger { get { return (CharacterName.Length == 0 || Through) && Owner.TilePassable(X, Y, 0, this); } }

        public void Lock()
        {
            if (_locked) return;
            _lockedDirection = Direction;
            TurnToward(Game.Player);
            _locked = true;
        }

        public void Unlock()
        {
            if (!_locked) return;
            _locked = false;
            if (!DirectionFix && Page != null && Page.MoveType == 0) Direction = _lockedDirection;
        }

        protected override bool CanMoveAutonomously()
        {
            return Page != null && !_locked && !Starting && !Moving && !Jumping && !Game.MessageBusy;
        }

        protected override void UpdateSelfMovement()
        {
            int threshold = (int)((40 - MoveFrequency * 2) * (6 - MoveFrequency) * 1.5);
            if (StopCount <= threshold) return;
            switch (Page.MoveType)
            {
                case 1: MoveRandom(); break;
                case 2:
                    {
                        int dx = Math.Abs(X - Game.Player.X), dy = Math.Abs(Y - Game.Player.Y);
                        if (dx + dy >= 20) MoveRandom();
                        else if (Game.Rng.Next(6) < 4) MoveToward(Game.Player); else MoveRandom();
                        break;
                    }
                case 3: UpdateRoute(); break;
            }
        }

        protected override void OnBump(int x, int y)
        {
            // Event touch: this event walked into the player
            if (Trigger == Triggers.EventTouch && Game.Player.X == x && Game.Player.Y == y && !Game.InterpreterRunning) Start();
        }

        public override void Update()
        {
            base.Update();
            if (ParallelInterpreter != null && !Erased) ParallelInterpreter.Update();
        }
    }

    /// <summary>The player character, controlled with the keyboard.</summary>
    public class Player : Character
    {
        public PlayerCharacter Look;
        bool _wasMoving;
        GameMap _pendingNeighbor;
        int _pendingOffX, _pendingOffY;

        public Player(Game game) : base(game) { MoveSpeed = 4; }

        public string WalkCharset
        {
            get
            {
                if (Look == null && Game.State != null && Game.State.PlayerCharacter < 0) return ""; // not chosen yet (Essentials intro)
                return Look != null && !string.IsNullOrEmpty(Look.WalkCharset) ? Look.WalkCharset : "trainer_POKEMONTRAINER_Red";
            }
        }
        public string RunCharset { get { return Look != null && !string.IsNullOrEmpty(Look.RunCharset) ? Look.RunCharset : WalkCharset; } }
        public string SurfCharset { get { return Look != null && !string.IsNullOrEmpty(Look.SurfCharset) ? Look.SurfCharset : WalkCharset; } }
        public string CycleCharset { get { return Look != null && !string.IsNullOrEmpty(Look.CycleCharset) ? Look.CycleCharset : WalkCharset; } }

        /// <summary>On the bicycle (from the Bag, or maps that are "Always on bicycle").</summary>
        public bool Cycling;
        bool _debugThrough;

        /// <summary>F5: use the registered key item (or choose between several).</summary>
        void UseRegisteredItem()
        {
            // The Ready menu (as Essentials): registered items, then the party's field moves
            var reg = (Game.State.Extras.Registered ?? new List<string>()).Where(id => Game.State.ItemCount(id) > 0).ToList();
            var moves = new List<KeyValuePair<string, PokeCreator.Pkmn.Pokemon>>();
            foreach (var pk in Game.State.Party.Where(p => !p.IsEgg))
                foreach (var m in FieldMoves.FieldMovesOf(pk))
                    if (!moves.Any(x => x.Key == m) && (m == "FLY" || m == "FLASH" || m == "DIG" || m == "TELEPORT" || m == "SWEETSCENT" || m == "SURF" || m == "DIVE")) moves.Add(new KeyValuePair<string, PokeCreator.Pkmn.Pokemon>(m, pk));
            if (reg.Count == 0 && moves.Count == 0) { Game.Message.Show("An item in the Bag can be registered to this key for instant use."); return; }
            if (reg.Count == 1 && moves.Count == 0) { PokeCreator.Runtime.UI.ItemFlow.UseFromBag(Game, reg[0], null); return; }
            var names = reg.Select(id => { var it = Game.Project.Items.FirstOrDefault(i => i.Id == id); return it != null ? it.Name : id; }).ToList();
            names.AddRange(moves.Select(m => PokeCreator.Pkmn.ItemEffects.MoveName(Game.Project, m.Key) + " (" + m.Value.Name(Game.Project) + ")"));
            names.Add("Cancel");
            Game.Message.Show("Use what?", names, names.Count - 1, k =>
            {
                if (k >= 0 && k < reg.Count) PokeCreator.Runtime.UI.ItemFlow.UseFromBag(Game, reg[k], null);
                else if (k >= reg.Count && k < reg.Count + moves.Count) { var m = moves[k - reg.Count]; FieldMoves.UseFromParty(Game, m.Value, m.Key); }
            });
        }
        /// <summary>Underwater after Dive.</summary>
        public bool Diving;
        public string DiveCharset { get { return Look != null && !string.IsNullOrEmpty(Look.DiveCharset) ? Look.DiveCharset : SurfCharset; } }
        bool _sliding;
        int _faceHold; // frames to keep facing the following Pokémon before walking into it
        bool _runOn;

        /// <summary>Climbs (up = true) or slides down a waterfall while surfing.</summary>
        public void ClimbWaterfall(bool up)
        {
            int d = up ? 8 : 2;
            if (up) Turn(8);
            var map = Game.Map;
            int n = 0, x = X, y = Y;
            while (true)
            {
                y += up ? -1 : 1;
                if (!map.Valid(x, y)) break;
                int tag = map.TerrainTag(x, y);
                n++;
                if (tag != GameMap.TagWaterfall && tag != GameMap.TagWaterfallCrest) break;
            }
            if (n == 0) return;
            var route = new PokeCreator.Maps.MoveRoute { Repeat = false };
            route.List.Add(new PokeCreator.Maps.EventCommand(37, 0));                 // Through ON
            route.List.Add(new PokeCreator.Maps.EventCommand(29, 0, up ? 3 : 4));     // slower going up
            for (int i = 0; i < n; i++) route.List.Add(new PokeCreator.Maps.EventCommand(up ? 4 : 1, 0));
            route.List.Add(new PokeCreator.Maps.EventCommand(38, 0));                 // Through OFF
            route.List.Add(new PokeCreator.Maps.EventCommand(29, 0, 4));
            route.List.Add(new PokeCreator.Maps.EventCommand(0, 0));
            ForceMoveRoute(route);
        }

        /// <summary>On the water (Surf). Moving onto land ends it.</summary>
        public bool Surfing;

        public void StartSurfing()
        {
            int fx = X + Dx(Direction), fy = Y + Dy(Direction);
            Surfing = true; Cycling = false;
            string bgm = Game.Project.Settings.SurfBGM;
            if (!string.IsNullOrEmpty(bgm)) Game.Audio.PlayBgm(bgm);
            Jump(Dx(Direction), Dy(Direction));
        }

        public override void Update()
        {
            bool canControl = !Game.InterpreterRunning && !Game.MessageBusy && !Game.Transferring && !RouteForced;
            // Ice: keep sliding until something stops the player
            if (_sliding && !Moving && !Jumping)
            {
                if (Game.Map.TerrainTag(X, Y) != GameMap.TagIce || Surfing || !Move(Direction, false)) _sliding = false;
                canControl = false;
            }
            if (canControl && !Moving && !Jumping)
            {
                int d = Input.Dir4;
                if (Game.Options != null && Game.Options.RunToggle && Input.Trigger(Button.Run)) _runOn = !_runOn;
                bool runKey = Game.Options != null && Game.Options.RunToggle ? _runOn : Input.Press(Button.Run);
                bool run = runKey && !Cycling && (!Game.Project.Settings.RunningShoesRequired || Game.State.Flags.Contains("has_running_shoes"));
                MoveSpeed = run || Cycling ? 5 : 4;
                SpeedScale = Cycling ? 1.5 : 1;
                // Walking toward the following Pokémon first turns to face it (so it can be talked to); again, and you swap places
                bool atPokemon = d != 0 && Followers.PokemonAt(X + Dx(d), Y + Dy(d));
                if (atPokemon && d != Direction) { Turn(d); _faceHold = 8; }
                else if (atPokemon && _faceHold > 0) _faceHold--;
                else if (d != 0) Move(d);
                if (d == 0) _faceHold = 0;
            }
            bool running = MoveSpeed >= 5 && !Cycling && (Moving || Input.Dir4 != 0);
            if (Diving) { SpeedScale = 1; MoveSpeed = 4; CharacterName = Game.Project.FindAsset("Graphics/Characters", DiveCharset) != null ? DiveCharset : WalkCharset; }
            else if (Cycling && !Surfing) CharacterName = Game.Project.FindAsset("Graphics/Characters", CycleCharset) != null ? CycleCharset : WalkCharset;
            else if (Surfing) { SpeedScale = 1; MoveSpeed = 4; CharacterName = Game.Project.FindAsset("Graphics/Characters", SurfCharset) != null ? SurfCharset : WalkCharset; }
            else CharacterName = running && Game.Project.FindAsset("Graphics/Characters", RunCharset) != null ? RunCharset : WalkCharset;

            base.Update();

            if (_wasMoving && !Moving && !Jumping) OnStepFinished();
            _wasMoving = Moving || Jumping;

            if (canControl && !Moving && !Jumping && Input.Trigger(Button.Action)) CheckActionTrigger();
            if (canControl && !Moving && !Jumping && Input.Trigger(Button.Menu) && !Game.State.Flags.Contains("menu_disabled")) Game.PushScene(new PokeCreator.Runtime.UI.PauseMenu(Game));
            if (canControl && !Moving && !Jumping && Game.DebugMode && Input.KeyTrigger(Scancode.F9)) PokeCreator.Runtime.UI.DebugMenu.Open(Game);
            if (canControl && !Moving && !Jumping && Input.KeyTrigger(Scancode.F5)) UseRegisteredItem();
            if (Game.DebugMode)
            {
                bool ctrl = Input.KeyPress(Scancode.LCtrl) || Input.KeyPress(228);
                if (ctrl && !Through) { Through = true; _debugThrough = true; }
                else if (!ctrl && _debugThrough) { Through = false; _debugThrough = false; }
            }
        }

        GameMap TargetMap(int nx, int ny, out int lx, out int ly)
        {
            GameMap m;
            if (Game.Map.Resolve(nx, ny, out m, out lx, out ly)) return m;
            return null;
        }

        public override bool Move(int d, bool turn = true)
        {
            if (turn) Turn(d);
            int nx = X + Dx(d), ny = Y + Dy(d);
            var map = Game.Map;

            if (Through) { Followers.PlayerMoved(Game, X, Y); X = nx; Y = ny; StopCount = 0; return true; }

            int lx, ly;
            var target = TargetMap(nx, ny, out lx, out ly);
            if (target == null) return false;

            // Ledges: jump over in the direction of travel
            if (target.TerrainTag(lx, ly) == GameMap.TagLedge && map.Valid(nx + Dx(d), ny + Dy(d)))
            {
                int jx = nx + Dx(d), jy = ny + Dy(d);
                if (map.TilePassable(jx, jy, 10 - d, null) && !map.EventsAt(jx, jy).Any(e => !e.OverTrigger))
                {
                    Game.Audio.PlaySe("Player jump");
                    Jump(Dx(d) * 2, Dy(d) * 2);
                    return true;
                }
            }

            bool water = Array.IndexOf(GameMap.WaterTags, target.TerrainTag(lx, ly)) >= 0;
            bool ok;
            if (Surfing && water) ok = map.SurfPassable(X, Y, d) && target.SurfPassable(lx, ly, 10 - d);
            else ok = (Surfing ? map.SurfPassable(X, Y, d) : map.TilePassable(X, Y, d, null)) && target.TilePassable(lx, ly, 10 - d, null);
            if (ok && water && !Surfing) ok = false; // needs Surf
            // Going down a waterfall while surfing
            if (Surfing && d == 2 && target == map && target.TerrainTag(lx, ly) == GameMap.TagWaterfall) { ClimbWaterfall(false); return true; }
            if (ok && Surfing && !water && target == map && !map.EventsAt(nx, ny).Any(e => !e.Through && e.CharacterName.Length > 0))
            {
                // Hop back onto land
                Surfing = false;
                Game.RestoreMapBgm();
                Jump(Dx(d), Dy(d));
                return true;
            }
            if (ok && target == map)
            {
                foreach (var e in map.EventsAt(nx, ny))
                    if (!e.Through && e.CharacterName.Length > 0) { ok = false; break; }
            }
            if (!ok)
            {
                if (target == map) CheckTouchTrigger(nx, ny);
                Game.Audio.PlaySe("Player bump");
                return false;
            }
            Followers.PlayerMoved(Game, X, Y);
            X = nx; Y = ny;
            StopCount = 0;
            if (target != map)
            {
                var n = map.Neighbors.First(nb => nb.Map == target);
                _pendingNeighbor = target; _pendingOffX = n.OffsetX; _pendingOffY = n.OffsetY;
            }
            return true;
        }

        void OnStepFinished()
        {
            if (_pendingNeighbor != null)
            {
                Game.EnterNeighbor(_pendingNeighbor, _pendingOffX, _pendingOffY);
                _pendingNeighbor = null;
            }
            Game.State.Steps++;
            DayCare.OnStep(Game);
            World.Phone.OnStep(Game);
            Safari.OnStep(Game);
            GameStats.OnStep(Game);
            if (StepEffects.OnStep(Game)) return;
            foreach (var spk in Game.State.Party) if (spk.ShadowPokemon) PokeCreator.Pkmn.Shadow.AdjustHeart(spk, -1);
            if (Game.State.Extras.PurifyChamber != null) foreach (var cpk in Game.State.Extras.PurifyChamber) PokeCreator.Pkmn.Shadow.AdjustHeart(cpk, -2);
            if ((Game.State.Steps & 63) == 0) World.Pokerus.DailyCheck(Game);
            Script.GameEvents.RaiseStep(Game);
            if (Game.Map.TerrainTag(X, Y) == GameMap.TagIce && !Surfing && !RouteForced) _sliding = true;
            // Player touch / event touch on the tile we stepped onto
            foreach (var e in Game.Map.EventsAt(X, Y).ToList())
                if ((e.Trigger == Triggers.PlayerTouch || e.Trigger == Triggers.EventTouch) && e.OverTrigger && !Game.InterpreterRunning) { e.Start(); return; }
            if (!Game.InterpreterRunning && Game.CheckTrainerSight()) return;
            if (PokeRadar.OnStep(Game)) return;
            Game.Encounters.OnStep(X, Y);
        }

        void CheckTouchTrigger(int x, int y)
        {
            if (Game.InterpreterRunning) return;
            foreach (var e in Game.Map.EventsAt(x, y))
                if ((e.Trigger == Triggers.PlayerTouch || e.Trigger == Triggers.EventTouch) && !e.OverTrigger) { e.Start(); return; }
        }

        void CheckActionTrigger()
        {
            if (Followers.TalkAt(Game, X + Dx(Direction), Y + Dy(Direction))) return;
            var map = Game.Map;
            foreach (var e in map.EventsAt(X, Y))
                if (e.Trigger == Triggers.Action && e.OverTrigger) { e.Start(); return; }
            int fx = X + Dx(Direction), fy = Y + Dy(Direction);
            // As RPG Maker XP: talking to an event in front also starts Player Touch / Event Touch pages (trainers)
            foreach (var e in map.EventsAt(fx, fy))
                if ((e.Trigger == Triggers.Action || e.Trigger == Triggers.PlayerTouch || e.Trigger == Triggers.EventTouch) && !e.OverTrigger) { e.Start(); return; }
            foreach (var e in map.EventsAt(fx, fy))
                if (FieldMoves.IsBoulder(e) && e.CharacterName.Length > 0) { FieldMoves.Strength(Game, null); return; }
            if (map.IsCounter(fx, fy))
            {
                fx += Dx(Direction); fy += Dy(Direction);
                foreach (var e in map.EventsAt(fx, fy))
                    if (e.Trigger == Triggers.Action && !e.OverTrigger) { e.Start(); return; }
                return;
            }
            // Dive (deep water / underwater) and Waterfall
            if ((Surfing || Diving) && FieldMoves.TryDive(Game)) return;
            if (Surfing && map.TerrainTag(fx, fy) == GameMap.TagWaterfall) { FieldMoves.OfferWaterfall(Game); return; }
            // Facing water: offer Surf
            if (!Surfing && !Diving && FieldMoves.IsWater(Game, fx, fy) && map.SurfPassable(fx, fy, 10 - Direction)
                && !map.EventsAt(fx, fy).Any(e => !e.Through && e.CharacterName.Length > 0)) FieldMoves.OfferSurf(Game);
        }
    }

    /// <summary>Wild encounters from the project's encounter tables.</summary>
    public class EncounterSystem
    {
        readonly Game _game;
        public bool Enabled = true;
        int _cooldown;
        public EncounterSystem(Game g) { _game = g; }

        public void OnStep(int x, int y)
        {
            if (_game.State.RepelSteps > 0 && --_game.State.RepelSteps == 0)
            {
                StepEffects.RepelWoreOff(_game);
                return;
            }
            if (!Enabled || _game.State.Flags.Contains("encounter_disabled")) return;
            if (_cooldown > 0) { _cooldown--; return; }
            var map = _game.Map;
            int tag = map.TerrainTag(x, y);
            string method;
            if (_game.Player.Surfing && Array.IndexOf(GameMap.WaterTags, tag) >= 0) method = "Water";
            else if (GameMap.IsGrassTag(tag)) method = "Land";
            else if (map.Tileset != null && _game.Project.Encounters.Any(en => en.Map == map.Id && en.Methods.Any(mt => mt.Type.StartsWith("Cave")))) method = "Cave";
            else return;
            var table = _game.EncounterTable(map.Id);
            if (table == null) return;
            int hour = DateTime.Now.Hour;
            bool night = hour < 6 || hour >= 20, morning = hour >= 5 && hour < 10, afternoon = hour >= 14 && hour < 17, evening = hour >= 17 && hour < 20;
            string part = morning ? "Morning" : afternoon ? "Afternoon" : evening ? "Evening" : null;
            var m = (part != null ? table.Methods.FirstOrDefault(mm => mm.Type == method + part && mm.Slots.Count > 0) : null)
                 ?? table.Methods.FirstOrDefault(mm => mm.Type == method + (night ? "Night" : "Day") && mm.Slots.Count > 0) ?? table.Methods.FirstOrDefault(mm => mm.Type == method);
            if (method == "Land" && BugContest.InProgress(_game)) m = table.Methods.FirstOrDefault(mm => mm.Type == "BugContest" && mm.Slots.Count > 0) ?? m;
            if (m == null || m.Slots.Count == 0) return;
            int density = m.Density > 0 ? m.Density : 25;
            if (_game.Rng.NextDouble() * 180 >= density * EncounterModifiers.RateFactor(_game)) return;
            int total = m.Slots.Sum(s => s.Chance), roll = _game.Rng.Next(Math.Max(1, total));
            EncounterSlot slot = m.Slots[0];
            foreach (var s in m.Slots) { if (roll < s.Chance) { slot = s; break; } roll -= s.Chance; }
            slot = EncounterModifiers.PickSlot(_game, m, slot);
            int level = _game.Rng.Next(slot.MinLevel, Math.Max(slot.MinLevel, slot.MaxLevel) + 1);
            level = EncounterModifiers.AdjustLevel(_game, slot, level);
            if (EncounterModifiers.Scared(_game, level)) return;
            // Repel: no wild Pokémon weaker than the first healthy party member
            if (_game.State.RepelSteps > 0)
            {
                var lead = _game.State.Party.FirstOrDefault(pk => !pk.Fainted && !pk.IsEgg);
                if (lead != null && level < lead.Level) return;
            }
            var sp = _game.Project.Species.FirstOrDefault(s => s.Id == slot.Species);
            _cooldown = 4;
            if (sp == null || !_game.PartyCanBattle) return;
            if (Roaming.TryEncounter(_game, method)) return;
            // Tall grass: 30% of encounters are double battles when two party Pokémon can fight (as Essentials)
            if (tag == GameMap.TagTallGrass && !_game.Project.Settings.NoTallGrassDoubles && _game.State.Extras.Partner == null
                && _game.State.Party.Count(p => !p.IsEgg && !p.Fainted) > 1 && !Safari.InZone(_game) && !BugContest.InProgress(_game) && _game.Rng.Next(100) < 30)
            {
                int roll2 = _game.Rng.Next(Math.Max(1, total));
                EncounterSlot slot2 = m.Slots[0];
                foreach (var s in m.Slots) { if (roll2 < s.Chance) { slot2 = s; break; } roll2 -= s.Chance; }
                int level2 = EncounterModifiers.AdjustLevel(_game, slot2, _game.Rng.Next(slot2.MinLevel, Math.Max(slot2.MinLevel, slot2.MaxLevel) + 1));
                if (_game.Project.Species.Any(s => s.Id == slot2.Species))
                {
                    _game.StartDoubleWildBattle(slot.Species, level, slot2.Species, level2, null);
                    return;
                }
            }
            _game.StartWildEncounter(slot.Species, level);
        }
    }
}
