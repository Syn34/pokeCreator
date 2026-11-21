# PokeCreator

**A standalone engine and editor for making Pokémon fan games.** No RPG Maker XP, no Ruby, no hidden script files. Everything lives in readable files that the editor opens for you.

PokeCreator does what Pokémon Essentials does, built to be easier to use:

| Pokémon Essentials | PokeCreator |
|---|---|
| Needs RPG Maker XP for maps and events | **PokeCreator Studio** has its own map, event and tileset editors |
| Game data in PBS text files that must be "compiled" | Forms for every Pokémon, move, item, trainer and encounter, saved as JSON instantly |
| ~1 MB of Ruby packed into the binary `Scripts.rxdata` | Engine written in C#; your own scripts are plain `.cs` files in `Scripts/`, editable in the Studio |
| Files scattered across `PBS/`, `Data/`, `Graphics/` with no overview | One project tree: Database, Maps, Scripts, Graphics & Audio, and **All Project Files** |
| Playtest through RMXP | **F5** to playtest, **Shift+F5** to start on the map you're editing |

Already have an Essentials game? **File → Import Pokémon Essentials Project** converts it: PBS data, maps, events, tilesets, switch/variable names, graphics and audio. Your Essentials folder isn't changed.

## What works today (v0.3)

**Studio (editor)**
- **New Project** asks for a project name and a location (paste a path or click Browse). The project is created in a new folder with the project's name inside that location, e.g. `Documents\PokeCreator Projects\My Game`
- Project explorer for every data table, map, script, asset folder and file, with an icon for everything (houses for towns, signposts for routes, caves, Pokémon Centers, gyms, Poké Balls...) and colour-coded indent guides like a code editor. Right-click a map → **Change Icon…** to choose your own
- **View → Appearance**: light/dark mode (Ctrl+Shift+D), accent colours, rainbow / per-section / plain tree guides, icons on/off, text size
- Database editors generated from the data model: dropdowns for types/moves/abilities/items, grids for level-up moves, evolutions, EVs, trainer parties and encounter slots, sprite previews
- **Pokémon, trainer type and item images**: entries missing any image are highlighted and tagged "missing details"; under the search bar is a count of what's missing (front, back, front shiny, back shiny, icon) and a Show filter. The details pane shows the item icon, trainer picture, or the Pokémon's front, back, shiny and icon sprites, marking missing ones. **Import** (next to New / Duplicate / Delete) takes images named after IDs (`YOUNGSTER.png`, `CHARIZARD_1.png`): a matching entry gets the image, any other name becomes a new entry (a new form is copied from its species)
- Map editor: tileset palette, 3 tile layers + events layer, pencil / rectangle / flood fill / eraser, right-click eyedropper, automatic autotile shapes, dim-other-layers, grid, passability overlay, undo/redo, zoom, map properties (size, tileset, music, Pokémon settings)
- Event editor: pages, conditions, sprite picker with preview, triggers and movement, readable command list, quick buttons for the common commands, plus quick templates for NPCs, doors and item balls
- **Add Command…** palette grouped like RPG Maker's: messages (text, choices, input number, comments), flow (conditional branch, loop, break, exit, labels and jumps, call common event, wait, erase), switches and variables, player and party (give Pokémon, give item, money, heal), map and movement (warp, set event location, move route, wait for moves, transparency), screen (tone presets, flash) and audio (BGM, fade out, ME, SE)
- **Move route editor**: who moves (player, this event, any event on the map), buttons for every move, turn and setting step (jump, wait, speed, frequency, switches, opacity, graphic, sound, script), repeat / skip if blocked / wait until done. Also used for an event page's **custom movement** (Edit Custom Route…)
- **Battle Animations** (Database → Battle Animations): every animation listed by name with search; the preview plays it over two Pokémon of your choice at the game's speed. Step through frames, add / copy / delete frames, drag cels on the preview, pick a cel's picture by clicking the animation sheet, set its zoom, angle, opacity, blend, layer and focus, and add sounds and flashes on any frame. **Import from Essentials…** brings the animations, sheets and sounds into a project made before animations were imported
- **Common events** (Database → Common Events): events that aren't on a map. Call them from any event with Call Common Event, or set them to run on their own while a switch is ON (autorun or parallel). Imported from Essentials' CommonEvents.rxdata
- **Map copy and paste**: the Select tool (drag an area, Ctrl+C / Ctrl+X / Delete, Ctrl+A for the whole map) copies all three layers; Ctrl+V shows the tiles under the mouse and each click pastes them, on this map or another (Esc stops). Right-drag with the Pencil picks an area of the current layer as the brush. On the Events layer, Ctrl+C / Ctrl+X / Ctrl+V copy, cut and paste events, also between maps
- **Switches & Variables** (Game Settings): search, rename and set how many; each shows how many times it's used and a list of every place (page conditions, Control Switches/Variables, conditional branches, move routes, scripts, common events). Double-click one to open that event on the right page. Wherever an event needs a switch or variable you pick it by name, and can rename it or add more right there
- **Problems panel** (toolbar button or Ctrl+Shift+P) lists what would break or look wrong in the game: missing graphics and audio, warps to maps that don't exist or outside the map, calls to missing common events or labels, unknown Pokémon / items / moves / trainers in the database and in event scripts, and script compile errors with file and line. Double-click one to go straight to the map event, record or script line. The toolbar shows the error count
- **Fonts**: every project uses the default FireRed font (Power Green from Essentials, kept in `BaseData/Fonts`). To use your own, add a .ttf to the project's Fonts folder, tick **Use my own font** in Game Settings and pick it as the Message font
- **Test Battle** (Game menu, Ctrl+F5): choose your team (Pokémon and levels, or a trainer from the database) and an opponent (a trainer or up to three wild Pokémon), single, double or triple, optionally with healing items and Poké Balls; the game opens straight into that battle and closes when it ends
- **Build Game** (Ctrl+B): makes a folder players can run without the Studio (the game program named after your game, your data, graphics, audio, fonts and scripts, library licenses, a How to play.txt) plus a .zip to send them. It checks for problems first
- **Nested maps**: drag a map onto another map to put it inside, onto the top or bottom edge of a row to reorder, or onto "Maps" for the top level (or use Move Up / Move Down / Move Inside Another Map… / Move to Top Level on the right-click menu). The tree draws ├ └ │ lines for each level
- **World Map** (Ctrl+M): every outdoor map laid out by its connections as live thumbnails. Drag maps so their edges touch to connect them, pull them apart to disconnect, nudge with the arrow keys, drop unplaced maps in from the side list, and right-click empty space to create a new connected map. Writes the same `map_connections.json` the game uses
- **Tileset folders**: a tileset is a folder of images, one per area (Outdoor, Cave, Poké Center, Mart...). Maps using it can paint from every area, picked from a list above the palette. **Game → Tilesets**: New Tileset Folder (import a whole folder), Add Area Image, Replace Image, Refresh Folder (picks up images you drop in), autotiles, and per-area passability, priority, terrain tags, bush and counter flags. 16×16 images are doubled and wide images re-arranged to 8 tiles across automatically. Existing single-image tilesets keep working
- **Default tilesets**: every new project starts with the tileset folders in `BaseData\Tilesets` next to the Studio (e.g. your FireRed tileset). Fill it with **Make Default for New Projects** in the Tilesets editor, or copy a folder of images in by hand. See `BaseData\Tilesets\README.txt`
- **Complete built-in database**: all 1025 Pokémon (#1–1025, with regional and alternate forms), 921 moves, 316 abilities and 715 items. New projects start with it, imports top up what Essentials lacks, and **Database → Add Missing Pokémon & Data** adds anything missing to an existing project without touching your edits
- Asset browser with thumbnails and drag-and-drop import
- **Script editor** like Visual Studio (drawn line by line, so even species.json at 3.5 MB opens instantly, scrolls smoothly and is coloured): line numbers (click or drag them to select lines), Visual Studio's C# colours in light and dark mode (keywords, control keywords, classes/structs/interfaces/enums from .NET and the engine, methods, strings and escapes, numbers, comments and XML doc tags, `#region`), recolouring as you type, Tab = 4 spaces, Enter keeps the indent, Ctrl+/ toggles comments, find (Ctrl+F / F3) and a line/column readout. JSON files get key/value colours
- Game settings, player characters, switches & variables

**Game runtime (`Game.exe`, SDL2)**
- Tile maps with RMXP autotiles, animated tiles and correct above/below layering
- Walking/running with RMXP passability and ledges
- Seamless walking between connected maps (Essentials-style map connections)
- Events: pages and conditions, action/touch/autorun/parallel triggers, move routes, NPC wandering
- RMXP-compatible event commands: text with Essentials text codes, choices, Input Number, conditional branches, loops, labels, switches, variables, self switches, money, transfers with fades, audio (with Memorize/Restore BGM), screen tone/flash/shake, **pictures** (Show/Move/Rotate/Erase Picture, so Essentials' intro with Professor Oak plays), Change Text Options (position, frameless text), Return to Title
- Common events: Call Common Event (and `pbCommonEvent`), autorun and parallel common events while their switch is ON
- **Battles**: wild battles from grass and events, trainer battles from the database (`TrainerBattle.start` / `pbTrainerBattle`, `WildBattle.start` / `pbWildBattle`). Essentials-style screen with battlebacks, databoxes, HP and EXP bars; Fight / Bag / Pokémon / Run; switching; Potions and Poké Balls in battle; catching with ball shakes; EXP, level ups, move learning and evolution afterwards; prize money and the trainer's defeat line; blacking out returns you to the last Pokémon Center (`pbSetPokemonCenter`). A Conditional Branch around `TrainerBattle.start` runs its commands only if the player won, as in Essentials. The Studio palette has Trainer Battle and Wild Battle commands
- **Double battles**: one trainer sending out two Pokémon (`setBattleRule("double")`), two trainers at once (`TrainerBattle.start(:CAMPER, "Jeff", :PICNICKER, "Susie")`, `pbDoubleTrainerBattle`) and double wild battles (`WildBattle.start(:PIDGEY, 5, :RATTATA, 5)`). You choose a move and target for each Pokémon; moves like Surf and Earthquake hit several Pokémon (0.75× damage), each trainer sends out their own replacements, and prizes from both trainers add up. The Studio palette has Double Battle commands
- **Abilities and battle weather**: about 200 abilities (Intimidate, Trace, Prankster, Magic Bounce, Parental Bond, Disguise, Protosynthesis, Levitate, Blaze/Torrent/Overgrow, Static, Sturdy, Swift Swim, Drizzle, Flash Fire, Volt Absorb, Wonder Guard, Speed Boost, Guts, Huge Power, Technician, Mold Breaker...), rain, sun, sandstorm and hail (from Rain Dance & co., abilities, or the map's weather), weather-dependent moves (Solar Beam, Thunder, Blizzard, Weather Ball, Moonlight), and common held items (Choice items, Life Orb, Focus Sash, type-boosting items, Rocky Helmet, Eviolite...)
- **Triple battles**: `setBattleRule("triple")` before a trainer battle, or `WildBattle.start` with three Pokémon. Single-target and spread moves only reach Pokémon next to the user, as in the games. The Studio palette has Triple Battle commands
- **Mega Evolution**: with a Mega Ring (or Mega Bracelet / Key Stone...) in the Bag, a Pokémon holding its Mega Stone (or knowing Rayquaza's Mega move) can Mega Evolve once per battle: press Shift in the Fight menu. Trainers' Pokémon Mega Evolve too. Forms come from the species' Mega forms (Essentials' pokemon_forms MegaStone / MegaMove / MegaMessage) and revert after the battle
- **Move effects**: all of Essentials' 921 moves, all 316 abilities and the battle held items (berries, herbs, Weakness Policy, Eject Button, gems...) work as in the games, including Reflect / Light Screen / Aurora Veil, Tailwind, Trick Room, Safeguard, Mist, Gravity, the four terrains, entry hazards (Stealth Rock, Spikes, Toxic Spikes, Sticky Web) and Rapid Spin / Defog, Taunt, Encore, Disable, Substitute, Leech Seed, Curse, Perish Song, Destiny Bond, Attract, Wish, Aqua Ring, Ingrain, Baton Pass, U-turn / Volt Switch, Roar / Whirlwind / Dragon Tail, Metronome, Sleep Talk, Outrage and Rollout, Counter / Mirror Coat, Sucker Punch, Future Sight, Knock Off, Thief, Brick Break, Smack Down, Foul Play, Psyshock, and moves whose power or type depends on the situation (Low Kick, Heavy Slam, Gyro Ball, Electro Ball, Stored Power, Hex, Facade, Brine, Return, Hidden Power, Judgment, Terrain Pulse...). The Fight menu refuses moves that Taunt, Disable or Encore block
- **Battle animations**: Essentials' move animations (`Data/PkmnAnimations.rxdata` and `Graphics/Animations`) are imported and play in battle as in Essentials, with their sounds, flashes, zoom, rotation, additive blending and hue. The foe uses `OppMove:` versions when they exist, multi-hit moves animate every hit, two-turn moves show their charging animation, moves without their own use their type's default, and common animations play for stat changes, status conditions, confusion, attraction, weather, Leech Seed, binding and shiny Pokémon. Players can turn them off with the "Battle Effects" option
- **Bag items work**: Potions, status heals, Revives, Ethers/Elixirs, PP Up, Rare Candy, Exp. Candy, vitamins, feathers, mints, Repels (wild Pokémon weaker than your lead stay away), TMs/HMs, and evolution stones. Give and take held items from the Bag or the party
- **Forms**: Pokémon get their random, seasonal or nature-based forms when created (Unown letters, Deerling seasons, Flabébé flowers, Vivillon patterns, Toxtricity, Tatsugiri, Squawkabilly...), and Arceus, Silvally, Genesect, Giratina, Zacian, Zamazenta and Ogerpon change form with their held item. The Gen 9 forms missing from the built-in data (and Ogerpon's masks) are added automatically; Palafin turns into its Hero Form after switching out
- **Evolution** by level, stone, happiness, time of day, held item, known move, party member and more (Essentials' methods that don't need a battle), with the evolution screen (B cancels a level evolution) and "forget a move?" when learning a fifth move
- **Pokédex** (seen and caught, list and entry pages) in the pause menu once `$player.has_pokedex = true`; **Options** (text speed, music and sound volume); **name entry** for `pbTrainerName` / `pbEnterPlayerName`, and nicknames when receiving a Pokémon
- **Field moves**: Surf (press A at the water with a Surf user in the party; water encounters while surfing), Cut trees, Rock Smash rocks (with Rock Smash encounters), Strength boulders, Headbutt trees, fishing with the Old/Good/Super Rod from the Bag, and berry plants (`pbPickBerry`). Essentials' events for these (`pbCut`, `pbRockSmash`, `pbSmashThisEvent`, `pbPushThisBoulder`, `pbHeadbutt`...) work unchanged, and the Studio has them in the palette (Field Moves) and as right-click templates (New Field Object). Pokémon that know a field move (Cut, Fly, Surf, Strength, Rock Smash, Headbutt, Flash, Waterfall, Dive, Teleport, Dig, Sweet Scent) list it in the party menu. **Waterfall** climbs and descends waterfalls, **Dive** goes down to a map's "Dive map ID" and back up, **dark maps** show only a circle around the player until Flash is used, and **ice** makes the player slide
- **Panoramas and fogs** (RPG Maker XP): a tileset can have a panorama behind the map (scrolling at half speed) and a fog over it (opacity, blend, zoom, drift, hue), set in the Studio's tileset editor (Panorama & Fog…) or by event commands (Change Fog, Change Panorama, Change Fog Opacity)
- **Pokégear and rematches**: the pause menu shows the Pokégear once `$player.has_pokegear` is set, with the Town Map and the Phone. Trainers registered after a battle (`pbPhoneRegisterBattle`, `Phone.add`) call now and then when they want a rematch and then battle with their next version from the database (`pbPhoneReadyToBattle?`, `pbPhoneBattleCount` / `Phone.variant`, `pbPhoneIncrement`); people registered with a common event run it when called. The Studio's New Trainer template sets this up when the trainer has rematch versions
- **Berry planting**: talk to soft soil (an event named BerryPlant or calling `pbBerryPlant`) to plant a berry from the Bag; it sprouts, grows, flowers and fruits over real hours (Essentials' growth times), shown with the `berrytree_<BERRY>` sprites. Watering with the Wailmer Pail / Sprayduck / Squirt Bottle / Sprinklotad (talking to the plant or from the Bag) gives more berries; ripe berries fall and the plant regrows up to 9 times. Plants are saved with the game
- **Itemfinder / Dowsing Machine**: points the player toward the nearest hidden item (events named HiddenItem). The Studio map editor's New Field Object menu adds Hidden Items and Empty Berry Soil
- **Town Map and Fly**: the region map from your town map data (Graphics/UI/Town Map), with the player's position; Fly goes to any place with a fly point the player has visited. **Bicycle** (from the Bag; faster, its own sprite and music, only on outdoor/bike maps), **Escape Rope** and Dig (back to the cave entrance set by `pbSetEscapePoint`), Teleport (last Pokémon Center)
- **Day Care and Eggs**: Essentials' `DayCare` calls work, so the example Day Care events run unchanged. Pokémon gain EXP as you walk, compatible pairs make Eggs (baby species, inherited IVs, Everstone, Destiny Knot, egg moves), and Eggs hatch after their hatch steps (faster with Flame Body). Variables can hold names for `\v[n]`
- **Ending**: Hall of Fame (`pbHallOfFameEntry`: shows the team, records it and saves; `pbHallOfFamePC` views past entries) and scrolling credits (`$scene = Scene_Credits.new`, text and music in Game Settings)
- Poké Marts (`pbPokemonMart`), the Pokémon Center PC (Pokémon storage boxes and item storage) and trainers who spot the player and walk up
- Essentials compatibility layer for common script calls (`pbReceiveItem`, `pbItemBall`, `setTempSwitchOn`, `$game_switches[..]`, `TrainerBattle.start`, ...)
- C# project scripts compiled at startup: `[ScriptCommand("name")]` methods become event commands, `[ScriptInit]` methods run at startup and can hook `GameEvents` (steps, map changes, battle ends, frames)
- Link battles and trades between two players over a LAN or the internet (pbLinkBattle, pbLinkTrade)
- Partner trainers, roaming Pokémon, Safari Zone, Bug-Catching Contest, Pokérus, Poké Radar, Battle Tower, Mystery Gift and Shadow Pokémon
- Plugins (Game → Plugins…): share scripts, graphics, sounds and database records between projects as folders or .zip files
- Translations (Game → Translations…): every message and database name in a per-language file with a grid editor; players pick the language in Options
- Location signs, day/night tint on outdoor maps, BGM/SE/ME through SDL_mixer
- **Weather**: rain, storm (with lightning), heavy rain, snow, blizzard, sandstorm, sun and fog, from a map's Weather property (e.g. `Rain,50`) or the Set Weather command
- **Pokémon:** real individual Pokémon with official stat formulas, 25 natures, IVs/EVs, all six EXP growth curves, gender ratios, abilities, shininess and level-up movesets. Events that give Pokémon (`pbAddPokemon`) create them, and healing commands restore HP and PP
- **Title screen** (uses your `Graphics/Titles/title.png`) with Continue / New Game / Quit
- **Pause menu** (Esc/X): Pokémon party (switch order), 3-page summary (info, stats with nature colours, moves), Bag with 8 pockets, trainer card, Save, Quit to Title
- **Saving and loading** to `%AppData%\PokeCreator\Saves\<game title>\Game.json` (previous save kept as a backup)

**Newer additions**
- **Pokémon Storage System**: 40 boxes (configurable) with names and wallpapers, Organize Boxes with the party beside the box, pick up / swap / withdraw / store, held items, markings; Purify Chamber for Shadow Pokémon; Mail (write, read, PC mailbox)
- **Summary** with Info, Memo (where met, characteristic), Stats, Moves (details and reordering) and Ribbons pages; level-up stat window; **Pokédex** with regional dexes, search / sort, Area (town map) and Forms pages
- **Battle Frontier**: Battle Tower, Palace (Pokémon act by nature), Arena (judged after 3 turns), Factory (rentals and swaps), Essentials' `pbBattleChallenge` calls, battle recording and playback
- **Smarter trainer AI** (damage estimates, immunities, status checks, healing, set-up, switching), `setBattleRule` options (weather, terrain, backdrop, no running, no EXP / money...)
- **Random dungeons**, play statistics (`$stats`, Trainer Card back), seasons and time events, Shiny / Oval Charm, overworld ability effects (Stench, Synchronize, Static...), White / Black Flute, Pokégear Jukebox, form-changing items (Gracidea, Reveal Glass, Rotom Catalog, DNA Splicers...)
- **Event scripts** run Ruby control flow (if / case / while / each / blocks / ternaries), and the Problems panel lists script calls the game doesn't understand
- Grass rustle, landing dust, jump shadows, tall-grass bush depth, water reflections; BGS ambient sounds and music effects that pause the BGM; game controllers, key rebinding, screen size / full screen (Alt+Enter); F5 Ready menu; F9 debug menu in playtests

**Sprites for Pokémon #899–1025 aren't included** (PokeCreator ships no Nintendo assets): add them to `Graphics/Pokemon/Front`, `Back`, `Icons` and so on, named like the others (e.g. `SPRIGATITO.png`).

 See [docs/ROADMAP.md](docs/ROADMAP.md).

## Quick start (Windows)

1. Build it (see below) or download a release zip, and unzip anywhere.
2. Run **`PokeCreatorStudio.exe`**.
3. Click **Import a Pokémon Essentials project**, pick your Essentials folder (the one with `Game.exe` and `PBS`), choose where the new project should go, then click **Import**.
4. Click maps and database entries in the project pane to edit them. **Ctrl+S** saves everything.
5. Press **F5** to play. Controls: arrows/WASD move, Shift runs, Z/Enter/Space interacts, X/Esc cancels.

Requires Windows 10/11 64-bit (uses the built-in .NET Framework 4.8; nothing to install).

## Building

**Windows, no Visual Studio:** double-click `build.cmd`. It uses the C# compiler that comes with Windows and writes everything to `bin\`.

**Visual Studio 2022:** open `PokeCreator.sln` and build. Output goes to `bin\`.

**Linux/macOS (Mono):** `./build.sh`. Needs `mono-complete`; to run the game, install SDL2, SDL2_image, SDL2_ttf and SDL2_mixer.

```
src/
  PokeCreator.Core      data model, JSON, project format, RMXP .rxdata reader
  PokeCreator.Import    Pokémon Essentials -> PokeCreator converter
  PokeCreator.Tools     PokeCreatorImport.exe (command-line importer)
  PokeCreator.Runtime   Game.exe (SDL2 engine)
  PokeCreator.Studio    PokeCreatorStudio.exe (editor)
lib/win-x64             SDL2 DLLs bundled with Game.exe
tests/                  self-checks (mono bin/CoreTests.exe <project>)
docs/                   architecture, project format, roadmap
```

Command-line import: `PokeCreatorImport.exe "C:\path\to\Essentials" "C:\path\to\NewProject"`

Playtest options: `Game.exe <project> [--map ID --x X --y Y --dir 2|4|6|8] [--scale 2] [--no-encounters]`

## Documentation

- [Architecture](docs/ARCHITECTURE.md): how the pieces fit together
- [Project format](docs/PROJECT_FORMAT.md): every file in a PokeCreator project
- [Roadmap](docs/ROADMAP.md): what's next

## Credits

Pokémon is © Nintendo / Creatures Inc. / GAME FREAK inc. PokeCreator is a non-commercial fan tool and contains no Nintendo assets; graphics come from your own project. Import support targets Pokémon Essentials v21 (by Maruno and contributors). Rendering, input and audio use [SDL2](https://libsdl.org) (zlib license).
