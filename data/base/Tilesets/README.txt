DEFAULT TILESETS FOR NEW PROJECTS
=================================

Every folder in here is a tileset that new PokeCreator projects start with.
The folder named in default.txt is used by the first map of a new project.

Put your FireRed tileset here, for example:

    BaseData\Tilesets\FireRed\Outdoor.png
    BaseData\Tilesets\FireRed\Cave.png
    BaseData\Tilesets\FireRed\Pokemon Center.png
    BaseData\Tilesets\FireRed\Poke Mart.png
    BaseData\Tilesets\FireRed\House.png
    BaseData\Tilesets\FireRed\Autotiles\Water.png     (optional, up to 7)

and write the word  FireRed  in default.txt.

- One image per area. Any map using the tileset can paint from all of them.
- 32x32 tiles 256 px wide (RPG Maker XP / Essentials layout) are used as they are.
  16x16 GBA-style images are doubled, and wider images are re-arranged to
  8 tiles across, automatically.
- Easiest way: in the Studio open Game > Tilesets, click "New Tileset Folder..."
  and pick your FireRed folder, set passability, then click
  "Make Default for New Projects". That fills this folder for you, including
  passability (tileset.json) and autotiles.

PokeCreator doesn't ship any Nintendo graphics, so this folder starts empty.
