# Roadmap

PokeCreator's goal: everything Pokémon Essentials does, without RPG Maker XP, and easier to use.

## ✅ v0.1: Foundation (done)
- Core data model, JSON project format, Essentials importer (PBS + maps + events + tilesets + assets)
- SDL2 game runtime: maps, autotiles, layering, movement, ledges, connections, events, interpreter, messages, choices, transfers, audio, day/night tint
- Essentials script-call compatibility layer; C# project scripts
- Studio: project tree, generated database editors, map editor, event editor, tileset editor, asset browser, script/text editor, import wizard, playtest

## v0.2: Core gameplay loop
- [ ] Pokémon instances (stats, IVs/EVs, natures, abilities, moves, EXP, evolution)
- [ ] Party and bag data; give/take Pokémon and items from events
- [ ] Pause menu: Party, Bag, Pokédex, Trainer Card, Save, Options
- [ ] Save/load (JSON save files) and a title screen with Continue / New Game
- [ ] Intro sequence support (choose character, name entry)

## v0.3: Battles
- [ ] Single wild battles: scene, HP bars, move selection, damage formula, type matchups, catching
- [ ] Trainer battles from `trainers.json`, AI by skill level, prize money, lose text
- [ ] Move effects via function codes, starting with the most common Essentials codes
- [ ] Status conditions, stat stages, weather, abilities and held items (incremental)
- [ ] Double battles

## v0.4: World features
- [ ] Field moves: Surf, Cut, Strength, Rock Smash, Fly, Waterfall, Dive
- [ ] Pokémon Centers, Poké Marts, PC storage, Day Care
- [ ] Trainer line-of-sight (`Trainer(n)` events), rematches
- [ ] Bicycle, running shoes toggle, fishing, Repel, Itemfinder
- [ ] Weather and fog/panorama effects, flash/darkness maps, Essentials text codes (colours, speed, windowskins)

## v0.5: Studio power features
- [ ] Visual map connections editor (drag maps next to each other)
- [ ] World map overview of all maps
- [ ] Move-route editor and full command palette in the event editor
- [ ] Validation panel: missing graphics, broken references (e.g. a move that doesn't exist)
- [ ] Common events, troops/battle setup screens
- [ ] Animation editor for battle animations
- [ ] Export/distribute: one-click "Build game" to a folder players can run

## Later
- Plugin system for sharing scripts between projects
- Localization (message tables)
- Online trading/battling hooks
