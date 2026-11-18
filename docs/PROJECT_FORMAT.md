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
    tilesets.json           tileset images + per-tile passability / priority / terrain tag
    mapinfos.json           map tree (names, parents, order)
    map_connections.json    maps that join at their edges
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

Codes follow RPG Maker XP (101 text, 102 choices, 111 conditional branch, 121 switches, 122 variables, 123 self switch, 201 transfer, 209 move route, 241/250 audio, 355 script, ...). The Studio's event editor shows them as readable text.

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
}
```
