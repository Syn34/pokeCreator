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
- [ ] Remaining abilities and items, about 85 rarer move effects (Pledges, Fling, Pursuit, type-changing moves...)
- [x] Battle animations for moves: Essentials' PkmnAnimations imported and played (Move / OppMove / Common, extra hits, charging turns, type defaults, sounds, flashes, hue), "Battle Effects" option
- [x] Double battles (one trainer, two trainers, wild pairs; targeting and spread moves)

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
- [ ] Gen 9 cosmetic forms (Tatsugiri, Squawkabilly, Ogerpon masks...) and battle function codes for new moves
- [x] Move-route editor and full command palette in the event editor
- [x] Problems panel: missing graphics and audio, broken references, script errors
- [x] Common events
- [ ] Troops/battle setup screens
- [x] Animation editor for battle animations (preview, frames, cels, sheet palette, sounds, import from Essentials)
- [x] Export/distribute: one-click "Build game" to a folder (and .zip) players can run

## Later
- Plugin system for sharing scripts between projects
- Localization (message tables)
- Online trading/battling hooks
