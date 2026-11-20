#!/usr/bin/env bash
# Builds PokeCreator with Mono's C# compiler (Linux/macOS). On Windows use build.cmd or open PokeCreator.sln.
set -e
cd "$(dirname "$0")"
OUT=bin
mkdir -p "$OUT"
# Compile against the real .NET Framework 4.8 reference assemblies (not Mono's own library,
# which has extra methods that don't exist on Windows and would crash at runtime).
API=/usr/lib/mono/4.8-api
MCS="mcs -langversion:5 -nowarn:1591,0168,0219 -debug- -nostdlib -r:$API/mscorlib.dll -r:$API/System.dll -lib:$API"
echo "== Core";    $MCS -target:library -out:$OUT/PokeCreator.Core.dll   -recurse:'src/PokeCreator.Core/*.cs' -r:$API/System.Core.dll
echo "== Import";  $MCS -target:library -out:$OUT/PokeCreator.Import.dll -recurse:'src/PokeCreator.Import/*.cs' -r:$OUT/PokeCreator.Core.dll -r:$API/System.Core.dll
echo "== CLI";     $MCS -target:exe     -out:$OUT/PokeCreatorImport.exe  src/PokeCreator.Tools/ImportCli.cs -r:$OUT/PokeCreator.Core.dll -r:$OUT/PokeCreator.Import.dll
if [ -d src/PokeCreator.Runtime ]; then
  echo "== Runtime"; $MCS -target:winexe -platform:x64 -unsafe -out:$OUT/Game.exe -recurse:'src/PokeCreator.Runtime/*.cs' -r:$OUT/PokeCreator.Core.dll -r:$API/System.Core.dll
fi
if [ -d src/PokeCreator.Studio ]; then
  echo "== Studio";  $MCS -target:winexe -out:$OUT/PokeCreatorStudio.exe -recurse:'src/PokeCreator.Studio/*.cs' -r:$OUT/PokeCreator.Core.dll -r:$OUT/PokeCreator.Import.dll -r:$API/System.Core.dll -r:$API/System.Drawing.dll -r:$API/System.Windows.Forms.dll -r:$API/System.Data.dll -r:$API/System.IO.Compression.dll -r:$API/System.IO.Compression.FileSystem.dll
fi
cp src/PokeCreator.Runtime/Game.exe.config "$OUT"/ 2>/dev/null || true
if [ -d data/base ]; then mkdir -p "$OUT/BaseData/Tilesets" && cp data/base/*.json "$OUT/BaseData/" && cp data/base/Tilesets/README.txt "$OUT/BaseData/Tilesets/"; fi
# Default fonts for projects that have none (the font files themselves are supplied by the user, not in git)
if [ -d data/base/Fonts ]; then mkdir -p "$OUT/BaseData/Fonts" && cp data/base/Fonts/* "$OUT/BaseData/Fonts/"; fi
if [ -d lib/win-x64 ]; then cp lib/win-x64/*.dll "$OUT"/ 2>/dev/null || true; fi
# Library licenses ship with every built game (Build Game copies bin/licenses)
if [ -d lib/win-x64/licenses ]; then mkdir -p "$OUT/licenses"; cp -r lib/win-x64/licenses/. "$OUT/licenses/"; fi
echo "Build OK -> $OUT/"
