# Camera and Input

Scope: orbit/zoom camera, orthographic 2D mode and transition, board rotation, pointer selection, drag-and-drop,
raycast picking, and arbitration between camera, pieces and UI.
Source: `Assets/Scripts/Camera/CameraRig.cs`, `Assets/Scripts/Input/BoardInput.cs`, `BoardView.Pick/RaySquare/DragTo`
(`Assets/Scripts/Board/BoardView.cs`), keyboard handling in `App.HandleKeys` (`Assets/Scripts/Core/App.cs`).
Status: implemented; picking verified by PlayMode tests at 5 yaw angles in 3D and 2D.

## Core rule

The board never moves: square centres are fixed in world space (`BoardView.SquareCenter`), White at −Z.
All "board rotation" is camera yaw. Therefore picking, logic and animations are independent of orientation.

## CameraRig

- Target: board centre (slightly below the surface in 3D). State: yaw, pitch, distance, ortho size, mode blend,
  each smoothed with `SmoothDamp` (yaw 0.06 s manual / 0.22 s automatic).
- Limits: pitch 18°–88°, distance 7.5–26, ortho size 3.2–9, FOV 40°.
- `Orbit(pixelDelta)`: 0.22°/px × `Sensitivity` (setting); pitch is ignored in 2D. `Zoom(steps)`: ×0.88 per step.
- 2D transition (`Set2D`/`Toggle2D`, 0.65 s, smoothstep): pitch → 90°, FOV narrows 40° → 8° while the camera pulls
  back to keep the framed size (dolly zoom), then switches to a true orthographic camera at the end. `Flatness`
  (0..1) drives the piece flattening/token fade in `BoardView.SetPresentation`. Reversible mid-transition.
- Rotation: `RotateBy(deg)` snaps the target to a multiple of 90°; `LookFrom(sideYaw)` uses the shortest path;
  `ResetView(sideYaw)` also resets pitch and fit; `Refit()` recomputes distance/ortho size (called by `App` on
  window resize or entering/leaving a game). `SidePanelFraction` (from the HUD width) keeps the board between panels.
- `IsTransitioning` is true during a 2D/3D blend or an automatic yaw move; board moves are blocked meanwhile.
- Auto-rotate (two-player): `App` sets `_awaitingAutoRotate` after a move (or when the user enables it mid-game)
  and calls `LookFrom(PlayerYaw)` once the board animation is idle. Toggle: HUD switch, Settings, or key **T**.

## BoardInput (single owner of pointer gestures)

A gesture is classified once, on press, and keeps that meaning until release:

| Press | Then | Result |
|---|---|---|
| Over UI (`EventSystem.IsPointerOverGameObject`) or `InputLocked` | — | ignored entirely |
| On a legal target of the selected piece | — | click-to-move (`TryMove`) |
| On an own piece (when `CanMove`) | release < 6 px | select (clicking the selected piece again deselects) |
| | drag ≥ 6 px | `BeginDrag`; piece follows pointer on a plane 0.28 above the board; drop on a legal target moves, elsewhere flashes red (if a different square) and slides back |
| Elsewhere | drag ≥ 6 px / release | orbit / deselect |
| Right or middle button | drag | orbit only (never selects) |
| Mouse wheel | not over UI | zoom |

- `CanMove` = in game and human's turn and not `Rig.IsTransitioning`; `InputLocked` = not in game, any menu,
  modal (promotion, game over) or pending promotion (set each frame by `App.Update`).
- Picking (`BoardView.Pick`): physics raycast against piece `CapsuleCollider`s first (logical square = index of that
  view in the board array), else ray–plane intersection at y = 0 → `SquareAt`. Captured/tray pieces have colliders off.
- Drop square = square under the dragged piece's base (not under the cursor), matching what the player sees.
- Promotion: `App.TryHumanMove` detects multiple candidate moves, re-syncs the pawn home, opens the picker; Esc cancels.
- Uses `Pointer.current` (mouse/touch/pen) and `Mouse.current` for right/middle/scroll. No pinch zoom.

## Keyboard (`App.HandleKeys`)

V toggle 2D/3D, Q/E rotate ±90°, F flip, R reset camera, P player view, T auto-rotate (two-player),
Ctrl+Z undo, Esc (cancel promotion → close menu → clear selection → pause), F11 windowed/borderless toggle.

## Invariants

- Never rotate or move the `Board` GameObject; only the camera.
- Input never mutates `Game` directly; it calls `App.TryHumanMove`, which validates through `GameSession`.
- A dragged piece is visual only until the move is accepted; on rejection `Resync` returns it.

## Validation

- PlayMode `PickingIsCorrectFromEveryAngleAndIn2D`: yaw 0/90/180/270/37 in 3D and 2D, empty and occupied squares
  (aims at the piece body, like a player). `SwitchingViewsPreservesPosition`, `AutoRotateTurnsViewWithoutTouchingRules`,
  `AutoRotateCanBeToggledMidGame`, `PromotionWaitsForChoice`.
- Manual (not automated): drag near board edges, orbit while a piece is selected, click through HUD panels,
  resize the window during a 2D transition. Note: when aiming at the board surface behind a tall piece the
  piece is correctly picked first; write tests that aim at what the player sees.

Related: [board_and_presentation.md](board_and_presentation.md), [testing.md](testing.md).
