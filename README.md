# Salochi Cars

The source of the Salochi mods for **Over The Hill**: Salochi Core (our fork of
[OverTheHillPlus](https://github.com/legomaster188/OverTheHillPlus-QoL)), the Accessory Editor and the car mods.
Players don't need anything here: the [Salochi Launcher](https://github.com/Salochi/oth-salochi-launcher) installs the
released builds, and checks every download against its signed mod list. This repo is so anyone can see what those
DLLs do, and build them.

| | |
|---|---|
| `src/Core` | **Salochi Core** (`OTH.Core`), our fork of the OTH+ core: car ids appended to the network key and mod id lists, two hooks the 2026-10-30 game update removed made string-based, save sidecar polling (a modded car keeps its paint, parts and toolset), garage/menu/lobby displays that match the driven car, paint presets, the in-game installer, and car framework additions: donor wheels and axles (`KeepDonorWheels`, `KeepDonorAxles`), `WheelColliderLift`, multi-piece body colliders with an optional per-car floor (`BodyColliderLift`), extra garage upgrades, `CarObjects` (body-built event, a car's objects in the scene). |
| `src/Mods/Accessories` | **Accessory Editor** (`OTH.Accessories`): the F8 panel for every framework car: stack accessories, move / turn / size each, wheel size and offset, synced to other players over the mod network. A car adds its own tabs with `AccessoryTabs` (soft dependency). |
| `src/Mods/Tacoma3` | **Tacoma (3rd gen)** (`OTH.Tacoma3`): a 3rd-gen Tacoma TRD on the Burro: stock 18" six-split-spoke rims on 32" M/T tyres, garage tyre sizes 33" / 35" on beadlocks. Model: "Toyota Tacoma TRD" by LagzDesign, CC BY 4.0. |
| `src/Mods/BajaBug` | **Baja Bug** (`OTH.BajaBug`): lifted, the flat four out back in a tube basket, a front bar with rally lamps, on the Brick's wheels and axles. Model: "VW Beetle" by 3DDomino, CC BY 4.0. |
| `src/Mods/FourRunner` | **4Runner (4th gen)** (`OTH.FourRunner`): a lifted 2006-2009 Limited on 33" M/T tyres on the Burro, on its own wheels and the Burro's axles. Model: "Toyota 4Runner" by sadiqminhas, CC BY 4.0. |
| `tests/` | Regression checks for the logic that runs without the game (`dotnet run --project tests/<Name>/<Name>.csproj -c Release`). |

The game fixes (Winch Fix, Shift Fix) live in [oth-salochi-fixes](https://github.com/Salochi/oth-salochi-fixes), and
the model conversion tools in [oth-car-toolkit](https://github.com/Salochi/oth-car-toolkit).

## Building

Needs the game with BepInEx 6 IL2CPP build 788 (the references come from its `BepInEx/core` and `BepInEx/interop`
folders; set `-p:GameDir=<path>` if the game isn't in `E:\SteamLibrary\steamapps\common\over the hill`).

```bash
dotnet build src/Core/OTH.Core.csproj -c Release -p:NoDeploy=true          # build one, without installing
./deploy.sh   # game closed: draws the icons, builds Core + the editor + the cars, installs them in BepInEx/plugins/Salochi
```

`deploy.sh` reads the game folder from `OTH_GAME` (or a `GAME=` line in `deploy.local`) and the toolkit from
`OTH_TOOLKIT` (default `../oth-car-toolkit`).

The cars' `.mesh` and textures in `src/Mods/*/Assets` are already converted. To rebuild them from the source models
(downloaded from Sketchfab into the toolkit's `input/`):

```bash
T=../oth-car-toolkit
blender -b --factory-startup --python $T/tools/refine.py -- $T/profiles/tacoma3.py $T/input/tacoma3/toyota_tacoma_trd.glb $T/build/tacoma3/refined.glb
blender -b --factory-startup --python $T/tools/convert_materials.py -- $T/profiles/tacoma3.py $T/build/tacoma3/refined.blend $T/build/tacoma3 ref/hilo22.mesh tacoma3
blender -b --factory-startup --python $T/profiles/bajabug_3dd_prep.py -- $T/input/beetle-3ddomino/vw_beetle.glb $T/build/bajabug-3dd/prepped.blend
blender -b --factory-startup --python $T/tools/convert_materials.py -- $T/profiles/bajabug_3dd.py $T/build/bajabug-3dd/prepped.blend $T/build/bajabug-3dd ref/aro.mesh bajabug
blender -b --factory-startup --python $T/profiles/4runner4_prep.py -- $T/input/4runner4/toyota_4runner.glb $T/build/4runner4/prepped.blend
blender -b --factory-startup --python $T/tools/convert_materials.py -- $T/profiles/4runner4.py $T/build/4runner4/prepped.blend $T/build/4runner4 ref/hilo22.mesh 4runner4
```

`ref/` holds the OTH+ cars whose accessory anchors seed the new cars' slots (`hilo22.mesh`, `aro.mesh`). They come
from OverTheHillPlus's cars, so they aren't in this repo.

## Licences

The code is MIT ([LICENSE](LICENSE)). Salochi Core is a fork of OverTheHillPlus by legomaster188, MIT
([LICENSE-OverTheHillPlus](LICENSE-OverTheHillPlus)). The car models are CC BY 4.0, adapted (converted, re-rigged on
the game's cars and retextured): "Toyota Tacoma TRD" by LagzDesign, "VW Beetle" by 3DDomino and "Toyota 4Runner" by
sadiqminhas, all on Sketchfab.
The mods carry those credits in game.

Over The Hill is made by Funselektor Labs. This is an unofficial fan project.
