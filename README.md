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

## What works today (v0.1)

**Studio (editor)**
- Project explorer for every data table, map, script, asset folder and file
- Database editors generated from the data model: dropdowns for types/moves/abilities/items, grids for level-up moves, evolutions, EVs, trainer parties and encounter slots, sprite previews
- Map editor: tileset palette, 3 tile layers + events layer, pencil / rectangle / flood fill / eraser, right-click eyedropper, automatic autotile shapes, dim-other-layers, grid, passability overlay, undo/redo, zoom, map properties (size, tileset, music, Pokémon settings)
- Event editor: pages, conditions, sprite picker with preview, triggers and movement, readable command list, one-click Text / Choices / If / Switch / Self Switch / Variable / Warp / Sound / Music / Wait / Give Item / Script, plus quick templates for NPCs, doors and item balls
- Tileset editor: passability, priority, terrain tags, bush and counter flags
- Asset browser with thumbnails and drag-and-drop import; text/script editor with highlighting and find
- Game settings, player characters, switches & variables, map connections

**Game runtime (`Game.exe`, SDL2)**
- Tile maps with RMXP autotiles, animated tiles and correct above/below layering
- Walking/running with RMXP passability, ledges, water blocking (Surf not yet)
- Seamless walking between connected maps (Essentials-style map connections)
- Events: pages and conditions, action/touch/autorun/parallel triggers, move routes, NPC wandering
- RMXP-compatible event commands: text with Essentials text codes, choices, conditional branches, loops, labels, switches, variables, self switches, money, transfers with fades, audio, screen tone/flash
- Essentials compatibility layer for common script calls (`pbReceiveItem`, `pbItemBall`, `setTempSwitchOn`, `$game_switches[..]`, `TrainerBattle.start`, ...)
- C# project scripts compiled at startup: `[ScriptCommand("name")]` methods become event commands
- Location signs, day/night tint on outdoor maps, BGM/SE/ME through SDL_mixer

**Not yet:** battles (wild/trainer battles currently show a placeholder message), party/bag/Pokédex menus, saving/loading, title screen, Surf/Fly/field moves, shops and the PC. See [docs/ROADMAP.md](docs/ROADMAP.md).

## Quick start (Windows)

1. Build it (see below) or download a release zip, and unzip anywhere.
2. Run **`PokeCreatorStudio.exe`**.
3. Click **Import a Pokémon Essentials project**, pick your Essentials folder (the one with `Game.exe` and `PBS`), choose where the new project should go, then click **Import**.
4. Double-click maps and database entries to edit. **Ctrl+S** saves everything.
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
