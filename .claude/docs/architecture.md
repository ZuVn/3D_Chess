# Architecture

Scope: module layout, assembly dependencies, scene composition, state ownership and event flow.
Status: implemented (verified against the repository, 2026-10-29).

## Assemblies and dependency direction

| Assembly (`.asmdef`) | Folder | References | Notes |
|---|---|---|---|
| `Chess.Core` | `Assets/Scripts/Chess/` | none | `noEngineReferences: true`. Rules, FEN/SAN/PGN, clock. |
| `Chess.AI` | `Assets/Scripts/AI/` | Chess.Core | `noEngineReferences: true`. Searcher, evaluator, UCI client. |
| `Chess.Game` | `Assets/Scripts/` (Core, Board, Camera, Input, UI, Audio, Settings, SaveSystem) | Chess.Core, Chess.AI, Unity.InputSystem, UnityEngine.UI, URP Core/Universal runtime | Everything Unity-facing. |
| `Chess.Editor` | `Assets/Editor/` | Chess.Game, URP runtime | Editor only: setup + build scripts. |
| `Chess.Tests.EditMode` | `Assets/Tests/EditMode/` | Chess.Core, Chess.AI, test runner | Editor only, `UNITY_INCLUDE_TESTS`. |
| `Chess.Tests.PlayMode` | `Assets/Tests/PlayMode/` | Chess.Core, Chess.AI, Chess.Game, test runner | `UNITY_INCLUDE_TESTS`. |

Direction is strictly `Chess.Game -> Chess.AI -> Chess.Core`. `Tools/CoreTests/CoreTests.csproj` and
`Tools/Match/Match.csproj` compile `Chess` + `AI` sources directly with the .NET SDK, so any `UnityEngine` usage
in those folders breaks both the asmdef and the tools.

## Scene and composition root

- One scene: `Assets/Scenes/Main.unity`, containing a single GameObject `App` with `ChessGame.App`
  (`Assets/Scripts/Core/App.cs`). It is listed in `ProjectSettings/EditorBuildSettings.asset`.
- `App.Awake` creates everything at runtime: camera + `CameraRig`, directional light, post-processing `Volume`,
  `BoardView`, `EventSystem` with `InputSystemUIInputModule`, `Sfx`, `GameSession`, HUD canvas (`Hud`), menu canvas
  (`Menus`), `BoardInput`, then applies settings and shows the main menu. `Benchmark.Attach` adds the self-test
  driver only when `-benchmark`/`-shots` is on the command line.
- There are **no prefabs** and no imported models. Generated assets: `Assets/Resources/Materials/ChessLit.mat`
  and `ChessMarker.mat` (URP Lit / Unlit-transparent templates), created by `Assets/Editor/ProjectSetup.cs`
  so the shader variants ship in builds. URP assets live in `Assets/Settings/`.

## Module responsibilities

| Folder | Key types | Responsibility |
|---|---|---|
| `Chess/` | `Position`, `Game`, `Move`, `ChessClock`, `TimeControl` | Authoritative rules and state. See [chess_rules.md](chess_rules.md). |
| `AI/` | `Searcher`, `Evaluator`, `UciEngine` | Move search, evaluation, Stockfish protocol. See [ai_engine.md](ai_engine.md). |
| `Core/` | `App`, `GameSession`, `AiPlayer`, `Benchmark` | Composition, turn flow, async AI orchestration, self-tests. See [state_management.md](state_management.md). |
| `Board/` | `BoardView`, `PieceShapes`, `PieceMeshes`, `PieceIcons`, `TextureGen`, `Themes` | Everything drawn on the board. See [board_and_presentation.md](board_and_presentation.md). |
| `Camera/` | `CameraRig` | Orbit/zoom/2D blend/rotation. See [camera_and_input.md](camera_and_input.md). |
| `Input/` | `BoardInput` | Pointer gestures over the 3D view. |
| `UI/` | `Ui`, `Fader`, `Menus`, `Hud` | uGUI built in code: menus, settings, HUD, dialogs. |
| `Audio/` | `Sfx` | Synthesised clips, one-shot playback. |
| `Settings/` | `Settings`, `Storage`, `SafeFile` | Preferences JSON, storage root, atomic writes. |
| `SaveSystem/` | `SavedGame` | Autosave of an unfinished game. |

## State ownership

- `GameSession.Game` (a `Chess.Game`) is the only chess state. `GameSession.Clock` is the only clock state.
- `BoardView`, `Hud`, `BoardInput` hold **derived** data only (piece objects, labels, selection square) and are
  rebuilt from `Session.Game` on change.
- `App.Settings` is the only settings instance; menus mutate it and call `App.ApplySettings()` / `SaveSettings()`.

## Event flow

```
BoardInput --TryMove(from,to)--> App.TryHumanMove --(promotion? Hud.AskPromotion)--> GameSession.TryHumanMove
GameSession.Tick (every frame from App.Update) --> clock, AiPlayer.ThinkAsync (worker) --> apply on main thread
GameSession events:
  PositionChanged(move) -> App.OnPositionChanged -> BoardView.Sync, highlights, Hud.Refresh, SaveGame, auto-rotate flag
  MovePlayed(record)    -> App.OnMovePlayed -> Sfx
  GameEnded             -> App: delete save, show game-over dialog once the board is idle
  Notice(text)          -> Hud.Toast
Menus/Hud buttons ----> public App methods (StartNewGame, Undo, ConfirmResign, ToggleView, SetAutoRotate, ...)
```

`App.Update` also computes input locking (menus/modals/promotion), feeds `CameraRig` state into
`BoardView.SetPresentation`, and refits the camera on window resize.

## Adding features without cycles

1. Rules or state that is not visual goes in `Chess/` (or `AI/`), with EditMode tests; no Unity types.
2. Turn-flow changes go in `GameSession`; expose events or methods, never let it reference UI/view types.
3. Visuals subscribe through `App` (the only class that knows every module). Views must not call each other
   except through `App` or plain data arguments.
4. UI widgets go through the `Ui` kit (`Ui.Button`, `Ui.Cycler`, `Ui.Switch`, `Ui.Slider`) for consistent style.
5. Never add a reference from `Chess.Core`/`Chess.AI` to `Chess.Game`.

## Validation

- Compile: run either Unity test command in [testing.md](testing.md) (batch mode aborts on compile errors).
- Dependency rule: `dotnet test Tools/CoreTests` must still build (proves Core/AI stay Unity-free).
