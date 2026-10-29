# Build and Release

Scope: Unity install requirements, build scripts, outputs, runtime files, Stockfish distribution, platform status.
Source: `Assets/Editor/BuildScript.cs`, `Assets/Editor/ProjectSetup.cs`, `Tools/fetch_stockfish.sh`,
`ThirdParty/Stockfish/`, `THIRD_PARTY_NOTICES.md`, `ProjectSettings/ProjectSettings.asset`.
Status: Linux build verified on the reference machine; Windows build produced but **not run on native Windows**.
No CI workflows exist.

## Requirements

- Unity 6000.6.7f1 with modules *Linux Build Support (Mono)* and *Windows Build Support (Mono)*
  (install via Hub, e.g. `unityhub --headless install-modules --version 6000.6.7f1 -m windows-mono`).
- Stockfish binaries (optional; Professional falls back without them): `Tools/fetch_stockfish.sh` downloads the
  official Stockfish 19 Linux and Windows x86-64 builds into `ThirdParty/Stockfish/{linux,windows}/`
  (git-ignored). `COPYING.txt`, `AUTHORS`, `SOURCE.txt` in `ThirdParty/Stockfish/` are tracked.

## Building

```bash
UNITY=~/Unity/Hub/Editor/6000.6.7f1/Editor/Unity   # local install path
$UNITY -batchmode -quit -projectPath . -executeMethod ChessGame.EditorTools.BuildScript.BuildLinux
$UNITY -batchmode -quit -projectPath . -executeMethod ChessGame.EditorTools.BuildScript.BuildWindows
$UNITY -batchmode -quit -projectPath . -executeMethod ChessGame.EditorTools.BuildScript.BuildAll
```
Editor menu: **Chess > Build Linux x86-64 / Build Windows x86-64**. Close any editor that has the project open.
`Build()` steps: run `ProjectSetup.Run()` (materials, scene, player settings, Linux APIs Vulkan + OpenGLCore,
.NET Standard API level) → delete and recreate `Builds/<Platform>/` → `BuildPipeline.BuildPlayer` (Mono, release)
→ delete `RoyalGambit_BackUpThisFolder_ButDontShipItWithYourGame` → copy Stockfish + licence files into
`RoyalGambit_Data/StreamingAssets/Stockfish/` → copy `THIRD_PARTY_NOTICES.md` → write `BUILD_INFO.txt`
(version, platform, UTC build time, Unity version, Stockfish yes/no) → `chmod +x` (Linux, when building on Unix).
Batch mode exits with code 1 if the build fails. First builds compile URP shaders for several minutes.

## Outputs

```
Builds/Linux/    RoyalGambit.x86_64, UnityPlayer.so, libdecor-*.so, RoyalGambit_Data/, BUILD_INFO.txt, THIRD_PARTY_NOTICES.md
Builds/Windows/  RoyalGambit.exe, UnityPlayer.dll, UnityCrashHandler64.exe, MonoBleedingEdge/, D3D12/, dstorage*.dll,
                 RoyalGambit_Data/, BUILD_INFO.txt, THIRD_PARTY_NOTICES.md
```
Each folder is ~200 MB (~100 MB game, ~99 MB Stockfish). Ship the whole folder. `Builds/` is git-ignored.
Version: `bundleVersion` in ProjectSettings (currently `1.0.0`), shown in the main menu and `BUILD_INFO.txt`.
No archive/AppImage packaging step exists yet (pending).

## Running

- Linux: `cd Builds/Linux && ./RoyalGambit.x86_64`. If "Permission denied": `chmod +x RoyalGambit.x86_64
  RoyalGambit_Data/StreamingAssets/Stockfish/stockfish` (the game also attempts the Stockfish chmod itself).
  Graphics problems: `-force-glcore` or `-force-vulkan`; hybrid GPUs: PRIME offload variables (see README).
  Logs: `~/.config/unity3d/Zn/Royal Gambit Chess/Player.log`.
- Windows: run `RoyalGambit.exe`; data in `%USERPROFILE%\AppData\LocalLow\Zn\Royal Gambit Chess\`.

## Platform validation status

| Check | Linux | Windows |
|---|---|---|
| Builds | yes | yes (cross-built on Linux) |
| Launches, plays, screenshots | yes (native) | Wine only (D3D11 via Wine) |
| Stockfish in game | yes (depth 19 at 1 s) | **no**: Stockfish 19's exe aborts under Wine (`std::out_of_range`); fallback engine used and labelled |
| Benchmark | yes ([performance.md](performance.md)) | not measured |

Before a release, run on real Windows 10/11: start a Professional game (HUD must show "Stockfish 19"), play,
undo, save/continue, toggle 2D/3D, change resolution/fullscreen.

## Licensing

Stockfish is GPLv3: keep it an unmodified separate executable, ship `COPYING.txt`, `AUTHORS.txt` and `SOURCE.txt`
(source location for tag `sf_19`) with it, keep `THIRD_PARTY_NOTICES.md` and the in-game Credits text
(`Menus.CreditsText`) accurate. Unity runtime/packages are under Unity's terms. All other assets are generated
by project code.

Related: [testing.md](testing.md), [performance.md](performance.md).
