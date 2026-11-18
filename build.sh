#!/usr/bin/env bash
# Builds PokeCreator with Mono's C# compiler (Linux/macOS). On Windows use build.cmd or open PokeCreator.sln.
set -e
cd "$(dirname "$0")"
OUT=bin
mkdir -p "$OUT"
MCS="mcs -langversion:5 -nowarn:1591,0168,0219 -debug-"
echo "== Core";    $MCS -target:library -out:$OUT/PokeCreator.Core.dll   -recurse:'src/PokeCreator.Core/*.cs' -r:System.Core.dll
echo "== Import";  $MCS -target:library -out:$OUT/PokeCreator.Import.dll -recurse:'src/PokeCreator.Import/*.cs' -r:$OUT/PokeCreator.Core.dll -r:System.Core.dll
echo "== CLI";     $MCS -target:exe     -out:$OUT/PokeCreatorImport.exe  src/PokeCreator.Tools/ImportCli.cs -r:$OUT/PokeCreator.Core.dll -r:$OUT/PokeCreator.Import.dll
if [ -d src/PokeCreator.Runtime ]; then
  echo "== Runtime"; $MCS -target:winexe -platform:x64 -unsafe -out:$OUT/Game.exe -recurse:'src/PokeCreator.Runtime/*.cs' -r:$OUT/PokeCreator.Core.dll -r:System.Core.dll
fi
if [ -d src/PokeCreator.Studio ]; then
  echo "== Studio";  $MCS -target:winexe -out:$OUT/PokeCreatorStudio.exe -recurse:'src/PokeCreator.Studio/*.cs' -r:$OUT/PokeCreator.Core.dll -r:$OUT/PokeCreator.Import.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Data.dll
fi
cp src/PokeCreator.Runtime/Game.exe.config "$OUT"/ 2>/dev/null || true
if [ -d lib/win-x64 ]; then cp lib/win-x64/*.dll "$OUT"/ 2>/dev/null || true; fi
echo "Build OK -> $OUT/"
