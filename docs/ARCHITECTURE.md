# Architecture

PokeCreator has four parts that share one data model.

```
             ┌────────────────────────────┐
             │      PokeCreator.Core      │  data classes, JSON, Project, MapData,
             │  (no dependencies, .NET 4) │  RMXP Marshal reader
             └─────┬───────────┬──────────┘
                   │           │
   ┌───────────────┴──┐   ┌────┴────────────────┐
   │ PokeCreator.     │   │ PokeCreator.Runtime  │  Game.exe: SDL2 window, renderer,
   │ Import           │   │ (Game.exe)           │  world simulation, event interpreter
   │ Essentials → JSON│   └──────────────────────┘
   └───────┬──────────┘              ▲ launched for playtests
           │                         │
   ┌───────┴─────────────────────────┴──┐
   │ PokeCreator.Studio (WinForms)      │  project tree, database forms,
   │ PokeCreatorStudio.exe              │  map/event/tileset editors
   └────────────────────────────────────┘
```

## Design choices

**Plain .NET Framework 4.8, C# 5-compatible source.** Every Windows 10/11 PC already has the runtime, so the Studio and the game run without installing anything. Windows' built-in `csc.exe` can build the code (`build.cmd`), and Mono builds it on Linux/macOS.

**No third-party packages.** JSON, the Ruby Marshal reader and the SDL bindings are small files in this repository. The only native dependency is SDL2 (DLLs in `lib/win-x64`).

**Data is the source of truth, not code.** Pokémon, moves, items and the rest are typed C# classes in `Core/Data/GameData.cs`, saved as JSON. The `[Field]` attribute on each property carries the label, help text, group, dropdown source (`Ref = "moves"`) or asset folder. The Studio's `FormBuilder` builds every database form from those attributes, so **adding a field to a class automatically adds it to the editor and to the project files.**

**RMXP-compatible maps and events.** Map tiles use RMXP tile IDs (0 empty, 48–383 autotiles, 384+ tileset tiles), and event commands use RMXP command codes (101 Show Text, 111 Conditional Branch, 201 Transfer Player, ...). Imported Essentials maps work without conversion, and the event interpreter follows RGSS's well-documented behaviour. The Studio's event editor hides the codes behind readable text and dialogs.

**Essentials compatibility layer.** Essentials events call Ruby (`pbReceiveItem(:POTION)`, `setTempSwitchOn("A")`, ...). `Runtime/Script/EssentialsCompat.cs` contains a tiny expression evaluator and a table of handlers for the common calls. Unknown calls are logged once and ignored, so imported games keep running while coverage grows. New handlers are one line: `Register("name", (ctx, args) => ...)`.

**User scripts are C#.** Files in a project's `Scripts/` folder are compiled with CodeDom when the game starts (`ScriptLoader.cs`). Static methods marked `[ScriptCommand("name")]` become callable from event Script commands and conditions.

## Core (`src/PokeCreator.Core`)

| File | Purpose |
|---|---|
| `Json/Json.cs` | Reader/writer with reflection mapping. Values equal to the class defaults are omitted, so files stay short and reload exactly. |
| `Data/GameData.cs` | `TypeData`, `AbilityData`, `MoveData`, `ItemData`, `SpeciesData` (forms are separate entries `SPECIES_n`), `TrainerTypeData`, `TrainerData`, `EncounterData`, `GameSettings`, `PlayerCharacter`, `MapConnection`, `TownMapRegion`, `RegionalDex` |
| `Maps/MapData.cs` | `MapData` (3 tile layers + events), `MapEvent`/`EventPage`/`EventCommand`, `TilesetData`, `MapInfo`, `MapProperties` |
| `Project/Project.cs` | Loads/saves the project folder; `Tables` lists every database table for generic tooling; `FindAsset` resolves names like "NPC 01" to files |
| `Ruby/RubyMarshal.cs` | Reads Ruby Marshal 4.8 (`.rxdata`), including RGSS `Table`, `Color` and `Tone` |

## Runtime (`src/PokeCreator.Runtime`)

| Folder | Purpose |
|---|---|
| `Platform/` | SDL2 P/Invoke bindings (UTF-8 safe), `Gfx` (textures, text, screenshots), `Input` (buttons with press/trigger/repeat), `Audio` |
| `World/` | `GameState` (switches, variables, self switches, bag, money), `GameMap` (passability, terrain tags, connections), `Character` (movement and move routes, RMXP timing scaled to 60 FPS), `GameEvent`, `Player`, `EncounterSystem` |
| `Render/` | `MapRenderer`: chunk-free per-frame rendering of visible tiles; priority tiles and characters are depth-sorted together; connected maps are drawn around the current map |
| `Script/` | `Interpreter` (RMXP command set), `EssentialsCompat`, `ScriptLoader` |
| `UI/` | `MessageWindow` (typewriter, paging, choices, Essentials text codes) |
| `Game.cs` | Main loop, map setup, transfers/fades, camera, screen tone, placeholder battles, `--autotest` scripted input |

The 60 FPS loop runs `Input.Update` → events → player → map interpreter → message window → camera, then draws.

## Studio (`src/PokeCreator.Studio`)

| File | Purpose |
|---|---|
| `MainForm.cs` | Window, menus, project tree, document tabs, save-all, playtest launching, recent projects |
| `Controls/FormBuilder.cs` | Builds forms from `[Field]` metadata; `ObjectGrid` for flat lists, `ObjectListEditor` for nested lists; `MultiPickDialog` |
| `Controls/DatabaseEditor.cs` | Table editor (search, new/duplicate/delete/reorder, sprite preview), settings and switch/variable editors |
| `Editors/MapEditor.cs` | Map document, palette, `MapCanvas` (16×16-tile chunk cache per layer, so big maps stay fast) |
| `Editors/TileGraphics.cs` | GDI+ tile drawing and **automatic autotile shapes** (`PatternFor`/`FixAutotiles`) |
| `Editors/EventEditor.cs` | Event pages, readable command list (`CommandText`), command dialogs |
| `Editors/MiscEditors.cs` | Tileset editor, asset browser, text editor, import dialog |

## Testing

- `tests/CoreTests.cs`: autotile shape selection, JSON round trips, project save/load, importer output.
- `Game.exe --autotest script.txt` replays input (`hold down 30`, `tap action`, `shot file.png`, `log`), which is how map rendering, doors, dialogue, connections and scripts were verified.
