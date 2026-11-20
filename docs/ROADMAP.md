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
- [x] Battle weather, ~90 common abilities, common held items
- [x] Screens (Reflect, Light Screen, Aurora Veil), Tailwind, Safeguard, Mist, Trick Room, Gravity, terrains, entry hazards (Stealth Rock, Spikes, Toxic Spikes, Sticky Web) with Rapid Spin / Defog
- [x] Taunt, Encore, Disable, Substitute, Leech Seed, Curse, Perish Song, Destiny Bond, Attract, Wish, Baton Pass, U-turn / Volt Switch, Roar / Dragon Tail in trainer battles, Metronome, Sleep Talk, Outrage / Rollout locks, Counter, Future Sight, Knock Off, variable power and type (Low Kick, Gyro Ball, Hex, Hidden Power...)
- [x] Rare move effects: type changes (Soak, Reflect Type, Conversion, Burn Up), Transform, Mimic, Sketch, Trick, Heal Block, Torment, Imprison, rooms, sports, Fling, Natural Gift, Beat Up, Present, Retaliate, Echoed Voice, Round, Fusion moves, Instruct, After You, Quash, Ally Switch, Beak Blast, Octolock...
- [x] About 240 of 316 abilities
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

- [x] Held items in battle: every berry (HP, status, pinch, type-resist, Kee/Maranga/Jaboca/Rowap/Enigma/Custap/Micle), herbs, Weakness Policy, Air Balloon, Eject Button/Pack, Red Card, Black Sludge, orbs, gems, Quick Claw, Metronome, Loaded Dice, seeds, Room Service, Booster Energy, Utility Umbrella, Ring Target, Clear Amulet...
- [x] Gluttony, Ripen, Cud Chew, Symbiosis, Pickup, Honey Gather, Ball Fetch, Stall, Quick Draw, Mycelium Might order

- [x] Every ability in the data (316): primal weather, Ruin abilities, Protean/Libero, Dancer, Mirror Armor, Opportunist, Emergency Exit, Tera Shell, Legends Z-A megas...

## Later
- Plugin system for sharing scripts between projects
- Localization (message tables)
- Online trading/battling hooks
