# Roadmap

PokeCreator's goal: everything Pokémon Essentials does, without RPG Maker XP, and easier to use.

## ✅ v0.1: Foundation (done)
- Core data model, JSON project format, Essentials importer (PBS + maps + events + tilesets + assets)
- SDL2 game runtime: maps, autotiles, layering, movement, ledges, connections, events, interpreter, messages, choices, transfers, audio, day/night tint
- Essentials script-call compatibility layer; C# project scripts
- Studio: project tree, generated database editors, map editor, event editor, tileset editor, asset browser, script/text editor, import wizard, playtest

## v0.2: Core gameplay loop (in progress)
- [x] Pokémon instances (stats, IVs/EVs, natures, abilities, moves, EXP curves)
- [x] Party and bag data; give Pokémon and items from events; healing
- [x] Pause menu: Party, Summary, Bag, Trainer Card, Save, Quit to Title
- [x] Save/load (JSON save files) and a title screen with Continue / New Game
- [x] Studio: closable document tabs, dark mode, Appearance tab, tree icons
- [x] Complete built-in database: all 1025 Pokémon with forms, moves, abilities and items; Add Missing Data for existing projects
- [x] World Map: drag-to-connect map connections, new connected maps, unplaced-map list
- [x] Tileset and autotile image import with 16×16 and wide-sheet conversion
- [x] Tileset folders: one image per area, maps paint from all of them; default tilesets for new projects
- [x] Script editor with line numbers and Visual Studio colours
- [x] Using items from the Bag (Potions, status heals, Revives, Ethers, Rare Candy, Exp. Candy, vitamins, mints, Repels, TMs), giving and taking held items
- [x] Evolution by level (Rare Candy / Exp. Candy), stone, happiness, held item, known move and more, with the evolution screen and move learning
- [x] Pokédex (seen/caught, list and entries), Options menu (text speed, volumes)
- [x] Name entry (pbTrainerName / pbEnterPlayerName) and nicknames when receiving a Pokémon
- [x] Intro sequence: choosing the player character (pbChangePlayer)

## v0.3: Battles
- [x] Single wild battles: scene, HP bars, move selection, damage formula, type matchups, catching
- [x] Trainer battles from `trainers.json`, AI by skill level, prize money, lose text, blacking out
- [x] Move effects read from Essentials function code names (stat changes, status, confusion, flinch, multi-hit, recoil, draining, healing, fixed damage, OHKO, two-turn, Protect, binding...)
- [x] Status conditions and stat stages; Leftovers and healing berries
- [x] Battle weather, abilities, held items
- [x] Screens (Reflect, Light Screen, Aurora Veil), Tailwind, Safeguard, Mist, Trick Room, Gravity, terrains, entry hazards (Stealth Rock, Spikes, Toxic Spikes, Sticky Web) with Rapid Spin / Defog
- [x] Taunt, Encore, Disable, Substitute, Leech Seed, Curse, Perish Song, Destiny Bond, Attract, Wish, Baton Pass, U-turn / Volt Switch, Roar / Dragon Tail in trainer battles, Metronome, Sleep Talk, Outrage / Rollout locks, Counter, Future Sight, Knock Off, variable power and type (Low Kick, Gyro Ball, Hex, Hidden Power...)
- [x] Rare move effects: type changes (Soak, Reflect Type, Conversion, Burn Up), Transform, Mimic, Sketch, Trick, Heal Block, Torment, Imprison, rooms, sports, Fling, Natural Gift, Beat Up, Present, Retaliate, Echoed Voice, Round, Fusion moves, Instruct, After You, Quash, Ally Switch, Beak Blast, Octolock...
- [x] All 316 abilities, including primal weather, Ruin abilities, Protean/Libero, Dancer, Mirror Armor, Opportunist, Emergency Exit, Tera Shell and the Legends Z-A Mega abilities
- [x] Held items in battle: every berry (HP, status, pinch, type-resist, Kee/Maranga/Jaboca/Rowap/Enigma/Custap/Micle), herbs, Weakness Policy, Air Balloon, Eject Button/Pack, Red Card, Black Sludge, orbs, gems, Quick Claw, Metronome, Loaded Dice, seeds, Room Service, Booster Energy, Utility Umbrella, Ring Target, Clear Amulet...
- [x] Form-changing abilities (Zen Mode, Schooling, Shields Down, Forecast, Flower Gift, Stance Change, Hunger Switch, Power Construct, Battle Bond, Disguise)
- [x] Pledge combinations (sea of fire, swamp, rainbow), Pursuit hitting a switching Pokémon
- [x] Battle animations for moves: Essentials' PkmnAnimations imported and played (Move / OppMove / Common, extra hits, charging turns, type defaults, sounds, flashes, hue), "Battle Effects" option
- [x] Double battles (one trainer, two trainers, wild pairs; targeting and spread moves)
- [x] Mega Evolution (Mega Ring, Mega Stones / Mega move, AI trainers, reverts after battle)
- [x] Triple battles (adjacency for targets and spread moves, setBattleRule("triple"), three-Pokémon WildBattle.start)

## v0.4: World features
- [x] Field moves: Surf, Cut, Strength, Rock Smash, Headbutt; berry plants (picking)
- [x] Fly with the Town Map, Teleport, Dig, Sweet Scent; field moves in the party menu
- [x] Waterfall, Dive, Flash with dark maps, ice
- [x] Berry planting and growth (real time, watering, regrowth)
- [x] Pokémon Centers, Poké Marts, PC storage, Day Care and Eggs
- [x] Trainer line-of-sight (`Trainer(n)` events)
- [x] Trainer rematches through the Pokégear phone (trainer versions)
- [x] Fishing (Old/Good/Super Rod), Repel
- [x] Bicycle, Escape Rope, Town Map
- [x] Running shoes toggle
- [x] Itemfinder / Dowsing Machine and hidden items
- [x] Overworld weather (map Weather property and Set Weather command)
- [x] Essentials text codes: colours, pauses, speed, sounds, money window, positions, speaker names, alignment
- [x] Flash/darkness maps, windowskins and text codes
- [x] Fog/panorama images (tileset settings, Change Fog / Panorama / Fog Opacity commands)

## v0.5: Studio power features
- [x] Forms: random/seasonal/nature forms on creation (Unown, Deerling, Flabébé, Vivillon, Toxtricity...), held-item forms (Arceus, Silvally, Genesect, Giratina, Zacian, Zamazenta, Ogerpon), Gen 9 forms (Tatsugiri, Squawkabilly, Maushold, Dudunsparce, Gimmighoul, Poltchageist, Sinistcha, Palafin's Zero to Hero, Ogerpon's masks)
- [x] Move-route editor and full command palette in the event editor
- [x] Problems panel: missing graphics and audio, broken references, script errors
- [x] Common events
- [x] Battle setup / tester: Game → Test Battle… (your team or a database trainer's team vs a trainer or wild Pokémon, single/double/triple)
- [x] Animation editor for battle animations (preview, frames, cels, sheet palette, sounds, import from Essentials)
- [x] Export/distribute: one-click "Build game" to a folder (and .zip) players can run


## v0.6: Essentials extras
- [x] Partner trainer (pbRegisterPartner / pbDeregisterPartner): every battle becomes a double battle with the partner's Pokémon in the second slot, AI-controlled, healed after each battle
- [x] Roaming Pokémon (Game Settings → Roaming: species, level, switch, encounter kind, music, maps; HP carries over, flees each turn, gone once caught or defeated; pbResetAllRoamers)
- [x] Safari Zone (pbSafariState: steps and Safari Balls from Game Settings, "Safari Zone" maps, Ball / Bait / Rock / Run battles, back to the reception when time is up)
- [x] Bug-Catching Contest (pbBugContestState: one Pokémon, Sport Balls, timer, BugContest encounters, keep one catch, judging with Essentials' score formula and prizes from Game Settings)
- [x] Pokérus (catching it after battles, spreading in the party, wearing off by day, double EVs, PKRS on the Summary, pbPokerus? for Nurse Joy)
- [x] Poké Radar (shaking grass patches, PokeRadar encounter slots, chains of the same species raising the shiny chance, 50-step recharge)
- [x] Battle Tower (pbBattleTower(rounds, double, level): pick 3 or 4 Pokémon, copies at level 50, generated trainers that get stronger with the streak, healing between rounds, Battle Points, records; pbInChallenge?, pbBattleChallenge.wins / decision / record / streak)
- [x] Random dungeons: maps marked "Random dungeon" get a new rooms-and-corridors layout on every visit from their own tiles (or chosen floor/wall tiles); events are scattered into the rooms; $PokemonGlobal.dungeon_area picks cave or forest shapes; saving inside keeps the layout
- [x] Play statistics ($stats): steps, battles, catches, hatches, evolutions, money spent, Pokémon Center visits, time to badges / Hall of Fame; Trainer Card back shows the play record
- [x] Bug-Catching Contest judging calls (pbStartJudging, pbGetPlaceInfo, place, pbContestHeld?) and moved-event memory ($PokemonMap.addMovedEvent), $player.last_party
- [x] Ruby control flow in event scripts: if/elsif/else/unless, case/when, while/until, for…in, .each/.each_with_index/.times/loop do blocks, one-line { |x| } blocks (map, select, count, any?, sum, sort_by…), begin/rescue, break/next/return, statement modifiers, ternaries, ranges, script globals, rename_choice / hide_choice
- [x] Battle Palace (Pokémon choose moves by nature) and Battle Arena (no switching, judged after 3 turns) rules; Battle Factory rental choice and swaps
- [x] Battle recording: Frontier battles are recorded (seed, teams, decisions); pbRecordLastBattle keeps one, pbPlayLastBattle plays it back
- [x] Regional Pokédexes: unlocked dexes (pbUnlockDex / $player.pokedex.unlock / lock) are offered when opening the Pokédex, each in its own order with its own counts
- [x] More script calls: seasons and months (pbGetSeason, pbIsMonth…), time events (pbTimeEvent / pbTimeEventValid), party checks (pbHasSpecies?, pbHasType?, pbCheckMove, pbBalancedLevel…), pbChangeLevel, move tutor annotations, screen shake / flash, pbFadeOutIn blocks, multi-line { } blocks
- [x] Pokémon Storage System: 40 boxes of 30 places with names and wallpapers (Graphics/UI/Storage/box_N or built-in colours); Organize Boxes with the party beside the box, pick up / put down / swap anywhere, withdraw, store, summary, held items, markings, release, jump between boxes; $PokemonStorage calls; old saves move into the boxes
- [x] Mail: giving Mail asks for a message; party Mail → Read / Take (to the PC mailbox or erased); PC Mailbox (read, move to Bag, give); Graphics/UI/Mail backgrounds; pbStoreMail / pbMoveToMailbox / pbDisplayMail
- [x] Audio: BGS ambient loops (map autoplay, Play / Fade BGS), music effects pause and resume the BGM; battle music overrides (Change Battle BGM, nextBattleBGM, map battle music)
- [x] Event commands: button input, save / menu / encounter access, name input; Summary Memo & Ribbons pages, move details and reordering
- [x] Overworld ability and item effects on wild encounters (Stench, Cleanse Tag, Arena Trap, Sand Veil, Hustle, Intimidate, Static / Magnet Pull, Synchronize, Cute Charm…), White / Black Flute, Pokégear Jukebox (March / Lullaby / Oak / custom music)
- [x] Debug menu in playtests (F9): warp, switches, variables, heal, add Pokémon / items, fill Bag, money, wild / trainer battles, fill Pokédex, toggles, hatch eggs, PC; hold Ctrl to walk through walls
- [x] setBattleRule: cannotRun, noExp, noMoney, weather, terrain, backdrop / environment, noPartner, disablePokeBalls, switchStyle / setStyle, battleBGM (plus size, canLose, outcomeVar)
- [x] Game controllers (D-pad / stick, A, B, X/Y/Start, shoulders to run), screen size option and Alt+Enter full screen, Bag register (F5) / sort / toss amounts, Pokédex search
- [x] Overworld effects: grass rustling, landing dust, jump shadows, bush depth in tall grass, reflections on still water and puddles; Show Animation (1 grass, 2 dust, 3 "!")
- [x] Options → Controls: rebind keys for every button (saved with the options), reset to defaults
- [x] Smarter trainer AI (skill 32+): damage estimates as a share of HP, KOs and priority finishes, type-absorbing abilities, status moves only when they'd work, healing / setting up / hazards / Protect; skill 48+ switches out of hopeless match-ups; level-up stat window
- [x] Essentials Battle Frontier calls: pbBattleChallenge.set / start / pbEnd / pbCancel / pbAddWin / pbMatchOver? / setDecision / pbGoToStart / pbRest / pbGoOn / getMaxWins, tower/factory/palace/arena/cup rules, pbEntryScreen, pbHasEligible?, pbBattleChallengeBattle / Graphic / BeginSpeech, Factory rentals
- [x] Mystery Gift (Database → Mystery Gifts: Pokémon or items, open to everyone or unlocked by a code on the title screen; pbReceiveMysteryGift / pbNextMysteryGiftID for the delivery person)
- [x] Shadow Pokémon (trainer Pokémon with the Shadow flag, Shadow type and moves, Snag Machine catching from trainers, heart gauge opened by walking, battling and scents, EXP saved until purified at a Relic Stone)
- [x] NPC followers (Followers.add / remove / clear, pbAddDependency2: an event walks behind the player, through doors, talking runs its common event)
- [x] Ribbons, contest conditions, happiness changes, encounter table versions
- [x] Fishing reaction: after "Oh! A bite!" press Action in time to hook the Pokémon (Game Settings → Fishing hooks by itself to skip it)
- [x] Map right-click → New Service NPC / Pokémon: gift Pokémon, static (legendary) encounters, in-game trades, Move Relearner, Move Deleter, Name Rater; script assignment to any Pokémon expression (pbGetPokemon(1).name = …)
- [x] Trade animation; Continue panel with badges / Pokédex / party; Hall of Fame on the PC; Options → Send to Boxes (Manual asks which party Pokémon to send to the PC when catching with a full party); setBattleRule forceCatchIntoParty, noAnims / anims, inverseBattle
- [x] Exp. Share (held: holders split half of the EXP, as Essentials) and Exp. All (EXPALL key item, $player.has_exp_all or Game Settings: Pokémon that didn't fight get half)
- [x] Battle Bag: X items (+2, X Attack 2/3/6), Dire Hit, Guard Spec., Yellow / Red Flute, Poké Doll / Fluffy Tail / Poké Toy (escape), Ethers and Elixirs on a chosen move
- [x] Field items: Ability Capsule, Ability Patch, Bottle Cap / Gold Bottle Cap (Hyper Training, level from Game Settings; add items with IDs BOTTLECAP / GOLDBOTTLECAP), Sacred Ash
- [x] Two trainers who spot the player at once team up for a double battle (if the player has two Pokémon that can fight); both are marked beaten
- [x] Walking raises friendship (every 128 steps; Soothe Bell, Luxury Ball); optional field poison (Game Settings, Gen 1-4 style); "use another Repel?" when one wears off
- [x] Friendship changes as Essentials: levelling up, fainting (more against a much stronger foe), vitamins / feathers, TMs, X items, bitter medicine; EV-lowering berries (Pomeg, Kelpsy, Qualot, Hondew, Grepa, Tamato)
- [x] Options → Move Hints (battle move menu shows how effective a move is against a Pokémon already seen); Options list scrolls; per-map victory / capture music
- [x] Studio: Duplicate Map; Map Properties → shift contents (tiles, events, transfers inside the map and the player start move with them); party Item → Move
- [x] Remaining script calls: pbWait, pbSEPlay/BGMPlay/MEPlay, pbExclaim, pbTurnTowardEvent, pbMoveRoute, bridges (pbBridgeOn/Off with Bridge terrain tag 15), pbStartOver

## v0.7: Sharing
- [x] Translations (Game → Translations…: Languages/<name>.json with every message, choice, script message and database name; grid editor; refresh keeps existing translations; players choose in Options → Language; included in built games)
- [x] Plugin system (Plugins/<name>: plugin.json, scripts, assets and database records; requirements; install / export .zip / make from scripts in Game → Plugins…; [ScriptInit] and GameEvents hooks for scripts)
- [x] Link battles and trades with another player over the network (pbLinkBattle / pbLinkTrade / pbLinkHost / pbLinkJoin: host or join by address, both games run the same battle in step, single or double, healed copies; trades with trade evolutions); battle hooks for outside control (ExternalCommand, Start/Turn overrides, FlipNames)
- [x] Event editor: Battle Tower, link battle / trade, Mystery Gift, partner, Safari, Bug Contest and Relic Stone commands
