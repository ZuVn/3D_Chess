# Project Overview

Royal Gambit Chess is a Unity desktop chess game for Windows x86-64 and Linux x86-64 with a 3D board and a
2D top-down (orthographic) view of the same game. It supports Player vs AI (Easy, Hard: built-in engine;
Professional: Stockfish 19 over UCI) and local two-player with chess clocks, undo, autosave and themes.
Implemented and tested on Linux; the Windows build compiles but has not been run on native Windows (see
[build_and_release.md](.claude/docs/build_and_release.md)). No CI and no git commits exist yet.

# Tech Stack

| Item | Verified value | Source |
|---|---|---|
| Unity Editor | 6000.6.7f1 | `ProjectSettings/ProjectVersion.txt` |
| C# / runtime | C# 9, .NET Standard 2.1 API level, Mono backend (no IL2CPP override) | generated `*.csproj`, `ProjectSettings/ProjectSettings.asset` |
| Render pipeline | URP 17.6.0 (`Assets/Settings/PC_RPAsset.asset`) | `Packages/manifest.json` |
| Input | Input System 1.20.1, `activeInputHandler: 1` (new system only) | manifest, ProjectSettings |
| UI | uGUI 2.6.0, built in code with legacy `UnityEngine.UI.Text` (no TextMeshPro, no UXML) | `Assets/Scripts/UI/` |
| Chess rules | Own engine, `Assets/Scripts/Chess/` (no UnityEngine reference) | `Chess.Core.asmdef` |
| AI | Own alpha-beta searcher + Stockfish 19 (external process, GPLv3) | `Assets/Scripts/AI/`, `ThirdParty/Stockfish/` |
| Tests | Unity Test Framework 1.8.0 (NUnit); same rules/AI tests also via `dotnet test` | `Assets/Tests/`, `Tools/CoreTests/` |
| Builds | StandaloneLinux64 (Vulkan, then OpenGLCore), StandaloneWindows64 (default APIs) | `Assets/Editor/BuildScript.cs`, ProjectSettings |

All art and audio are generated at runtime by project code; there are no imported models, prefabs or sound files.

# Dev Commands

Prerequisite: Unity Hub with editor 6000.6.7f1 plus *Linux Build Support (Mono)* and *Windows Build Support (Mono)*.
Commands assume the editor at `~/Unity/Hub/Editor/6000.6.7f1/Editor/Unity` (the path on the reference machine; adjust locally).

```bash
UNITY=~/Unity/Hub/Editor/6000.6.7f1/Editor/Unity
Tools/fetch_stockfish.sh       # once: Stockfish binaries are git-ignored (needs curl, unzip)
~/Unity/Hub/Editor/6000.6.7f1/Editor/Data/DotNetSdk/dotnet test Tools/CoreTests   # rules+AI+clock, no Unity
$UNITY -batchmode -projectPath . -runTests -testPlatform EditMode -testResults $PWD/Logs/editmode.xml
$UNITY -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults $PWD/Logs/playmode.xml
$UNITY -batchmode -quit -projectPath . -executeMethod ChessGame.EditorTools.BuildScript.BuildLinux    # -> Builds/Linux/
$UNITY -batchmode -quit -projectPath . -executeMethod ChessGame.EditorTools.BuildScript.BuildWindows  # -> Builds/Windows/
```

- Close any Unity editor that has this project open before batch-mode tests or builds (the project is locked).

- **Open / run in editor:** open the folder in Unity Hub, open `Assets/Scenes/Main.unity` (contains a single `App`
  object), press Play. If materials or the scene are missing, run menu **Chess > Setup Project**.
  (Interactive Play in the editor GUI is pending verification; PlayMode tests and builds start `App` the same way.)
- **Editor menu equivalents:** **Chess > Build Linux x86-64**, **Chess > Build Windows x86-64**.
- **Self-test a build:** `./RoyalGambit.x86_64 -shots <dir>` (screenshots) or `-benchmark 60 -benchmark-out f.txt`
  (frame-time report). Details in [testing.md](.claude/docs/testing.md) and [performance.md](.claude/docs/performance.md).
- `-testPlatform StandaloneLinux64` did not return results in this environment; use EditMode/PlayMode above.

# Core Logic Summary

- `Chess.Game` (owned by `ChessGame.GameSession`) is the single authoritative game state: position, history,
  repetition keys, result. Nothing else stores the position.
- Every move goes through `Game.TryMakeMove`, which accepts only moves from the full legal-move list (king safety,
  castling, en passant, promotion included). Input, AI and save-replay all use this path.
- The 3D meshes and 2D tokens are the same `BoardView` piece objects; 2D mode only flattens meshes and shows
  tokens. There is one board and one state.
- The camera orbits a fixed board; camera mode, rotation and auto-rotation never change logical squares or the position.
- AI searches a snapshot on a worker thread; results are applied only if still current and legal in the live position.
- `BoardView.Sync` re-derives every visual (captures, castling rook, en passant, promotion, undo, trays) from the
  position after each change; animations are cosmetic and never feed back into state.
- Game over is computed by `Game` after each move (checkmate, stalemate, insufficient material, threefold,
  fifty-move) or set explicitly (resign, draw agreement, clock flag); `Undo` recomputes it.

Details: [chess_rules.md](.claude/docs/chess_rules.md), [ai_engine.md](.claude/docs/ai_engine.md),
[state_management.md](.claude/docs/state_management.md).

# Key Constraints

- Read the relevant source and the matching `.claude/docs/` file before editing; follow existing naming
  (namespaces `Chess`, `Chess.AI`, `ChessGame`) and the one-composition-root pattern (`App.Awake` builds the scene).
- Keep `Assets/Scripts/Chess` and `Assets/Scripts/AI` free of `UnityEngine` (`noEngineReferences: true`); they are
  also compiled by `Tools/CoreTests` and `Tools/Match`. Dependencies point Game -> AI -> Core only.
- Never duplicate chess state for 2D/3D or the HUD; read it from `Session.Game`.
- Never bypass legal-move validation (no direct `Position.MakeMove` from gameplay code; it is for search/tests).
- Never run search or engine I/O on the main thread; never block in `Update()` waiting for a task.
- Animations must not decide the position; after any state change call the existing sync path (`OnPositionChanged`).
- Picking must stay correct at every camera angle and in 2D: rotate the camera, never the board transform.
- No per-frame allocations or scene searches in `Update()`; rebuild UI only on change (see the HUD clock caching).
- Keep Windows and Linux paths working (e.g. `stockfish` vs `stockfish.exe`, `chmod` only on Linux).
- Change rules, clock or state transitions only with new/updated tests in `Assets/Tests/EditMode` (and PlayMode
  when gameplay flow changes); run both test commands above before claiming success.
- Do not claim 60 FPS, platform support or passing tests without a reproducible run; record results in
  [performance.md](.claude/docs/performance.md) or [testing.md](.claude/docs/testing.md).
- Do not add packages or change Unity/package versions without a written reason; `Packages/manifest.json` was
  deliberately trimmed of Unity's default extras (analytics, purchasing, etc.).
- Keep `THIRD_PARTY_NOTICES.md` and the Stockfish `COPYING.txt`/`AUTHORS`/`SOURCE.txt` shipping with builds;
  Stockfish must stay an unmodified separate executable (GPLv3).
- Self-test modes (`-shots`, `-benchmark`) and PlayMode tests must not write the player's real settings/saves
  (they use `Storage.RootOverride`).

# Additional Documentation

| Document | Read it when |
|---|---|
| [architecture.md](.claude/docs/architecture.md) | Adding a feature, a new module, or touching `App` / event flow / assembly references |
| [chess_rules.md](.claude/docs/chess_rules.md) | Changing move generation, draw/game-over rules, FEN/SAN/PGN, undo, or the clock rules |
| [ai_engine.md](.claude/docs/ai_engine.md) | Changing difficulty levels, search, evaluation, Stockfish/UCI, or AI threading |
| [camera_and_input.md](.claude/docs/camera_and_input.md) | Changing orbit/zoom/2D transition, rotation, selection, drag-and-drop or input routing |
| [board_and_presentation.md](.claude/docs/board_and_presentation.md) | Changing board/piece geometry, themes, 2D icons, highlights or move animations |
| [state_management.md](.claude/docs/state_management.md) | Changing turn flow, undo/restart/resign, clocks, settings or save/load |
| [performance.md](.claude/docs/performance.md) | Touching per-frame code, rendering, quality presets, or reporting FPS |
| [build_and_release.md](.claude/docs/build_and_release.md) | Building, packaging, platform issues, Stockfish distribution or licensing |
| [testing.md](.claude/docs/testing.md) | Writing or running tests, smoke-testing builds, or filing a reproducible bug |

User-facing setup, controls and measured results are in [README.md](README.md).
