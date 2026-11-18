# Built-in database generator

`data/base/*.json` is PokeCreator's built-in database: every Pokémon (#1–1025 plus regional
and alternate forms), move, ability, item, type and trainer type. New projects start from it,
the Essentials importer adds what Essentials lacks, and **Database → Add Missing Pokémon & Data…**
tops up existing projects.

How it was made:

1. Import Pokémon Essentials v21.1 with `PokeCreatorImport.exe <Essentials> <out> --no-assets --no-base`
   (Gens 1–8, 898 species, 339 forms).
2. Download the PokeAPI CSVs (https://github.com/PokeAPI/pokeapi/tree/master/data/v2/csv).
3. `python3 build_base_data.py <out>/Data <pokeapi csv folder> ../../data/base`
   adds #899–1025, Hisuian and Paldean forms, their learnsets, evolutions, Pokédex entries,
   and the moves, abilities and items they need (Scarlet/Violet learnsets where available).
4. `PokeCreatorImport.exe --normalize ../../data/base` rewrites the files in PokeCreator's compact format.

Known gaps: new moves have `FunctionCode: None` (battle effects come with the battle system),
PokeAPI lacks some move flags for recent moves, and cosmetic Gen 9 forms (Tatsugiri colours,
Squawkabilly plumages, Ogerpon masks...) aren't included yet. No sprites are included: put
your own in Graphics/Pokemon.
