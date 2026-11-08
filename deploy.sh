#!/usr/bin/env bash
# Build and install the forked OTH core, the accessory editor and the car mods (Tacoma 3rd gen, Baja Bug, and a
# private 2024 Tacoma in src/Mods/Tacoma if that folder is there) into the game
# (BepInEx/plugins/Salochi).
# The game must be closed. Settings, each from the environment, else ./deploy.local (KEY=value lines), else the default:
#   OTH_GAME     the game folder (default: E:\SteamLibrary\steamapps\common\over the hill)
#   OTH_TOOLKIT  the oth-car-toolkit checkout with the conversion tools (default: ../oth-car-toolkit)
#   OTH_BUG      the Baja Bug's build folder (default: <toolkit>/build/bajabug-3dd, made by profiles/bajabug_3dd_prep.py +
#                convert_materials.py; build/bajabug-cc: the earlier CC BY Beetle)
#   OTH_TACOMA3  the 3rd-gen Tacoma's build folder (default: <toolkit>/build/tacoma3, made by refine.py + convert_materials.py)
set -euo pipefail
cd "$(dirname "$0")"
setting() { local v="${!1:-}"; [ -z "$v" ] && [ -f deploy.local ] && v="$(sed -n "s/^$1=//p" deploy.local | head -1)"; echo "${v:-$2}"; }
GAME="$(setting OTH_GAME "/e/SteamLibrary/steamapps/common/over the hill")"
TOOLKIT="$(setting OTH_TOOLKIT ../oth-car-toolkit)"
BUG="$(setting OTH_BUG "$TOOLKIT/build/bajabug-3dd")"
T3="$(setting OTH_TACOMA3 "$TOOLKIT/build/tacoma3")"
PLUG="$GAME/BepInEx/plugins"
[ -d "$PLUG" ] || { echo "No BepInEx/plugins in $GAME (set OTH_GAME, or GAME in deploy.local)."; exit 1; }
[ -f "$TOOLKIT/tools/make_icon.py" ] || { echo "No toolkit at $TOOLKIT (set OTH_TOOLKIT)."; exit 1; }
if tasklist | grep -qi "over the hill.exe"; then echo "Close Over The Hill first."; exit 1; fi
MODS="Accessories Tacoma3 BajaBug"
if [ -d src/Mods/Tacoma ]; then   # the private 2024 Tacoma (not in the public repo)
  MODS="$MODS Tacoma"
  python "$TOOLKIT/tools/make_icon.py" build/tacoma.mesh build/tacoma_icon.png --tex build/tacoma --seams 0F,0-1,1R
  cp build/tacoma.mesh build/tacoma_*.png src/Mods/Tacoma/Assets/
fi
# garage icons, drawn from the built meshes (the Bug drives on the Brick's 0.40 m wheels)
python "$TOOLKIT/tools/make_icon.py" "$T3/tacoma3.mesh" "$T3/tacoma3_icon.png" --tex "$T3/tacoma3" --seams 0F,0-1,1R
python "$TOOLKIT/tools/make_icon.py" "$BUG/bajabug.mesh" "$BUG/bajabug_icon.png" --tex "$BUG/bajabug" --tyre 0.40 --seams 0F,0-1
cp "$T3/tacoma3.mesh" "$T3"/tacoma3_{albedo_base,zones,mask,lights,ao,icon}.png src/Mods/Tacoma3/Assets/
cp "$BUG/bajabug.mesh" "$BUG"/bajabug_{albedo_base,zones,mask,lights,ao,icon}.png src/Mods/BajaBug/Assets/
for m in $MODS; do
  dotnet build "src/Mods/$m/OTH.$m.csproj" -c Release -p:NoDeploy=true -v q -nologo
done
# Into BepInEx/plugins/Salochi, the folder the Salochi Launcher and Core's in-game installer use. Copies an older
# deploy left directly in plugins/ (or the original OverTheHillPlus folder) would load twice: moved aside once.
OURS="$PLUG/Salochi"
mkdir -p "$OURS"
MOVED="$GAME/BepInEx/salochi-moved/deploy-$(date +%Y%m%d-%H%M%S)"
for f in "$PLUG"/OTH.{Core,Accessories,Tacoma,Tacoma3,BajaBug}.dll "$PLUG"/OverTheHillMods/OTH.Core.dll; do
  [ -f "$f" ] && { mkdir -p "$MOVED"; mv "$f" "$MOVED/"; echo "Moved aside: $f"; }
done
cp src/Core/bin/Release/net6.0/OTH.Core.dll "$OURS/OTH.Core.dll"
for m in $MODS; do
  cp "src/Mods/$m/bin/Release/net6.0/OTH.$m.dll" "$OURS/OTH.$m.dll"
done
echo "Installed in plugins/Salochi: $(ls "$OURS")"
