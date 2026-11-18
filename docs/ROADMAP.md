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
- [ ] Using items from the Bag (Potions, Repels...), giving held items
- [ ] Evolution on level up (needs battles for EXP)
- [ ] Pokédex (seen/caught), Options menu
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
