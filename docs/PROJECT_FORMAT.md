# Project format

A PokeCreator project is an ordinary folder. Every file is readable and diff-friendly, so projects work well with Git.

```
MyGame/
  project.json              name, author, engine version
  Data/
    settings.json           game title, new-game start position, screen size, font, default music
    players.json            playable characters (trainer type + overworld sprites)
    system.json             switch and variable names (index = ID)
    types.json              Pokémon types and matchups
    abilities.json
    moves.json
    items.json
    species.json            every Pokémon; alternate forms are separate entries (e.g. CHARIZARD_1)
    trainer_types.json
    trainers.json           trainer parties
    encounters.json         wild encounter tables per map
    town_maps.json          region map points
    regional_dexes.json
    tilesets.json           tilesets: folder of area images, autotiles, per-tile passability / priority / terrain tag
    mapinfos.json           map tree (names, parents, order)
    map_connections.json    maps that join at their edges (edited in the World Map)
    common_events.json      events that aren't on a map: Id, Name, Trigger (0 called only, 1 autorun, 2 parallel), SwitchId, List
    world.json              Studio only: where each map sits on the World Map ({"5": [x, y]} in tiles)
    Maps/
      Map001.json …         one file per map: size, tileset, music, Pokémon settings,
                            tile layers, events
  Graphics/                 same layout as Essentials (Characters, Tilesets, Autotiles, Pokemon, ...)
  Audio/                    BGM, BGS, ME, SE
  Fonts/
  Scripts/                  your C# scripts (compiled when the game starts)
```

## Conventions

- **IDs** are internal names in capitals (`PIKACHU`, `POTION`, `THUNDERBOLT`). Data refers to other data by ID.
- **Defaults are omitted.** A field that's missing from a JSON entry has its default value (see the classes in `src/PokeCreator.Core/Data/GameData.cs`).
- **Assets are referenced by name without extension**, e.g. `"CharacterName": "NPC 01"` → `Graphics/Characters/NPC 01.png`.

## Maps

```json
{
  "Id": 2, "Width": 32, "Height": 21, "TilesetId": 1,
  "AutoplayBgm": true, "Bgm": { "Name": "Lappet Town", "Volume": 100, "Pitch": 100 },
  "Properties": { "Name": "Lappet Town", "Outdoor": true, "ShowArea": true, "BattleBack": "field" },
  "Layers": [ [[384, 385, ...], ...], [...], [...] ],
  "Events": [ { "Id": 3, "Name": "NPC", "X": 12, "Y": 7, "Pages": [ ... ] } ]
}
```

- `Layers[z][y][x]` holds tile IDs: `0` empty, `48–383` autotiles (`id / 48` = autotile slot 1–7, `id % 48` = shape), `384+` tiles from the tileset image (`id - 384` = index, 8 tiles per row).
- Tileset tables (`Passages`, `Priorities`, `TerrainTags`) are indexed by tile ID. Passage bits: `1` down, `2` left, `4` right, `8` up (blocked), `0x40` bush, `0x80` counter.

## Events

Each page has `Condition`, `Graphic`, movement settings, `Trigger` (0 action button, 1 player touch, 2 event touch, 3 autorun, 4 parallel) and `List`, the commands:

```json
{ "Code": 101, "Params": ["Hello, \\PN!"] }
{ "Code": 111, "Params": [12, "pbItemBall(:POTION)"] }
{ "Code": 201, "Params": [0, 3, 7, 5, 8, 0] }
```

Codes follow RPG Maker XP (101 text, 102 choices, 111 conditional branch, 117 call common event, 121 switches, 122 variables, 123 self switch, 201 transfer, 209 move route, 241/250 audio, 355 script, ...). The Studio's event editor shows them as readable text.

A move route (209, or a page's `MoveRoute` when `MoveType` is 3) is `{ "Repeat": false, "Skippable": false, "List": [ {"Code": 1}, {"Code": 14, "Params": [0, -1]}, {} ] }`; step codes follow RPG Maker XP (1-4 move down/left/right/up, 14 jump, 15 wait, 16-19 face, 29 speed, 41 graphic, 45 script...). The 509 lines after a 209 only repeat its steps for display. Screen tone (223) takes `[{"_class": "Tone", "Values": [r, g, b, gray]}, frames]`.

## Built games

**Game → Build Game** copies the game program (renamed after the game, with its `.config`), `PokeCreator.Core.dll`, the SDL libraries, `licenses/`, `project.json` and the `Data`, `Graphics`, `Audio`, `Fonts`, `Scripts`, `Languages` and `Plugins` folders. The game program finds `project.json` next to itself, so the folder runs as it is. Saves go to `%AppData%\PokeCreator\Saves\<game title>`.

## Scripts

```csharp
using System.Collections.Generic;
using PokeCreator.Runtime;
using PokeCreator.Runtime.Script;

public static class Rewards
{
    // In an event:  Script: giveStarterKit()
    [ScriptCommand("giveStarterKit")]
    public static object GiveStarterKit(Game game, Interpreter ctx, List<object> args)
    {
        game.State.AddItem("POTION", 5);
        game.Message.Show("\\PN received 5 Potions!");
        return true;
    }

    // Runs once at startup, after every script and plugin is loaded
    [ScriptInit]
    public static void Setup(Game game)
    {
        GameEvents.Step += g => { /* every step on the map */ };
        GameEvents.MapChanged += g => { /* arrived on g.Map */ };
        GameEvents.BattleEnded += (g, outcome) => { /* Win, Loss, Escaped, Caught */ };
        GameEvents.GameStarted += g => { /* new game or loaded save */ };
        GameEvents.Frame += g => { /* every frame on the map */ };
        GameEvents.WildPokemonCreated += (g, pk) => { /* change the wild Pokémon before the battle */ };
        GameEvents.BattleStarting += (g, battle) => { /* battle fields: weather, rules... */ };
        GameEvents.GameSaved += g => { /* after saving */ };
    }
}
```

## Plugins

`Plugins/<Name>/` folders hold things to share between projects (**Game → Plugins…** installs, switches on/off, exports a plugin as a .zip, or makes one from some of your scripts):

```
Plugins/MyPlugin/
  plugin.json         { "Name": "MyPlugin", "Version": "1.0", "Author": "...", "Description": "...", "Requires": ["OtherPlugin"], "Disabled": false }
  Scripts/*.cs        compiled together with the project's Scripts ([ScriptCommand], [ScriptInit], GameEvents)
  Graphics/ Audio/ Fonts/   used when the project has no file of that name
  Data/items.json     database records (any table file name: items.json, moves.json, species.json...) added to
                      the game's database, replacing entries with the same ID; the project's own files are untouched
```

Plugins load after the plugins they require; a missing requirement shows in the Problems panel.

## Translations

`Languages/<Name>.json` (made by **Game → Translations…**):

```json
{ "Language": "Español", "Texts": { "Hello! Welcome to the world of Pokémon!": "¡Hola! ¡Bienvenido al mundo Pokémon!", "Potion": "Poción" } }
```

Keys are the original texts: event messages (a Show Text command with its continuation lines, joined by spaces), choices, `pbMessage("...")` texts, map names and database names and descriptions. Empty translations fall back to the original. The player picks the language in Options.

## Map connections

`map_connections.json` uses Essentials' meaning. Each entry joins one edge of `Map1` to one edge of `Map2`:

```json
{ "Map1": 47, "Edge1": "East", "Offset1": 8, "Map2": 52, "Edge2": "West" }
```

Take the point `Offset1` tiles along Map1's edge and the point `Offset2` tiles along Map2's edge (offsets run left-to-right on North/South edges, top-to-bottom on East/West edges); the maps are placed so those two points meet. Above, Map 52's top-left corner sits 8 tiles below Map 47's top-right corner. Missing offsets are 0. The World Map writes these for you.

## Default tilesets

`BaseData/Tilesets/<Folder>/` next to the Studio: images (one per area), an optional `Autotiles/` folder and an optional `tileset.json` (written by **Make Default for New Projects**, holds passability). `BaseData/Tilesets/default.txt` names the folder new maps use. New projects get a copy of every folder.

## Built-in database

`BaseData/*.json` next to the Studio (from `data/base` in the repo) holds every Pokémon, move, ability, item, type and trainer type. New projects are created from it, and **Database → Add Missing Pokémon & Data** merges in records whose ID the project doesn't have yet. Existing records are never changed. See `tools/basedata/README.md` for how it's generated.

## Tilesets and tile IDs

A tileset is either one image (`TilesetName`, as imported from Essentials) or a **folder** (`Folder`) in `Graphics/Tilesets` holding one image per area. Every image is a *sheet* with a fixed slot number; the first sheet is slot 0 (`TilesetName`), the rest are listed in `Sheets`:

```json
{ "Id": 1, "Name": "FireRed", "Folder": "FireRed", "TilesetName": "FireRed/Outdoor",
  "Sheets": [ { "Name": "FireRed/Cave", "Slot": 1, "Passages": [...] },
              { "Name": "FireRed/Pokemon Center", "Slot": 2, "Passages": [...] } ] }
```

Tile IDs in map layers follow RPG Maker XP: 0 is empty, 48–383 are autotiles (48 per autotile), and sheet tiles are `384 + slot × 32768 + row × 8 + column`. Slot 0 therefore uses exactly RMXP's IDs. Slots are never reused or renumbered, so adding, removing or re-ordering images never changes existing maps. A deleted image's slot stays reserved. Passability, priority and terrain tags for slot 0 (and autotiles) are in the tileset's own lists, indexed by tile ID; other sheets keep theirs in their entry, indexed by `row × 8 + column`.

New images dropped into a tileset's folder are added as new sheets when the project is opened or **Refresh Folder** is clicked. Don't rename sheet images after maps use them (the name is how the sheet is found).
