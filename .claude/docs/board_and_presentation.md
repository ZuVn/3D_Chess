# Board and Presentation

Scope: board geometry and coordinates, procedural pieces and 2D icons, materials/themes, highlights, move
animation and view/state synchronisation.
Source: `Assets/Scripts/Board/` — `BoardView.cs`, `PieceMeshes.cs` (contains `PieceShapes` + `PieceMeshes`),
`PieceIcons.cs`, `TextureGen.cs`, `Themes.cs`; materials `Assets/Resources/Materials/ChessLit.mat`, `ChessMarker.mat`.
Status: implemented. No prefabs or imported art: everything is generated at runtime.

## Coordinates and world layout

- One square = 1 world unit. `BoardView.SquareCenter(sq) = (file − 3.5, 0, rank − 3.5)`; a1 at (−3.5, 0, −3.5),
  White at −Z. `SquareAt(world)` is the inverse (floor of x+4, z+4). Board surface y = 0; table at `TableY = −0.35`.
- Frame 9.3 × 9.3 (border to ±4.65), inlay line under the squares, large table box. Coordinates are a world-space
  uGUI canvas with labels on all four sides; label rotation snaps to the nearest 90° of the camera yaw.
- Captured pieces sit in trays beside the board (`TraySlot`: White's captures at x ≈ +5.3, Black's at x ≈ −5.3,
  two columns of 8, scale 0.62).

## Pieces

- `PieceShapes` defines lathe profiles (radius, height) per type, the knight head polygon and king cross boxes;
  heights 0.89 (pawn) … 1.64 (king), base radii 0.34–0.40. Pivot at the base centre; pieces face +Z.
- `PieceMeshes.Get(type)` builds and caches one mesh per type (lathe with hard-edge duplicates, rook crenels,
  bishop slit and queen crown via vertex modifiers, extruded knight head, king cross). Triangles are wound with an
  explicit facing check (`Builder.Tri`).
- Each piece object: root (with `CapsuleCollider` for picking) → `Mesh` child (black pieces rotated 180°; knights
  additionally −90° so the profile faces the owner's left) → `Token` child (`SpriteRenderer`, 2D icon).
- `PieceIcons.Get(code)`: 128 px side-view icons rasterised from the **same** profiles, tinted with the active
  `PieceTheme` (fill, contrasting outline, cylindrical shading, gloss, wood grain, ridge lines, knight eye/mane,
  drop shadow). Cache is rebuilt when `SetTheme` changes the theme; used by 2D tokens, HUD trays and promotion picker.
- 2D mode (`SetPresentation(flatness, yaw)`): mesh Y scale → 0.03 and hidden near 1, tokens fade in; token rotation
  is fixed (`TokenRotation`, Euler 90,0,0) — tokens stay locked to the board when the view rotates (by request), so
  they appear upside down from Black's side.

## Materials and themes

- Runtime materials are `new Material(template)` from `Resources/Materials` (URP Lit / URP Unlit transparent).
  The templates exist so URP shader variants are included in builds; create them with **Chess > Setup Project**.
  Do not switch shader keywords at runtime (variants may be stripped); change colours/textures/floats only.
- `Themes`: 4 boards (Classic Wood, Dark Modern, Light Minimalist, Marble), 4 piece sets (Ivory & Ebony,
  Boxwood & Rosewood, Modern Matte, Gold & Silver), 3 environments (Study, Night, Daylight). Detail textures are
  procedural (`TextureGen.Detail`, cached per surface/seed). `ApplyTheme` also sets camera background, trilight
  ambient, sun colour and re-renders the realtime reflection probe once.

## Highlights (`SetHighlights`, `FlashIllegal`)

Flat quads with marker textures, pooled per kind, each kind with its own material and render queue:
last move (fill), selected (frame), legal move (dot), capture (ring), check (red ring on king), hover (frame),
drag target (white frame), illegal (red fill, fades 0.45 s). Shapes differ, so meaning does not rely on colour only.
`App.RefreshHighlights` rebuilds the set only on changes (selection, hover square, position).

## Synchronisation and animation (`BoardView.Sync`)

`Sync(position, history, historyCount, lastMove, animate)` makes the view match a position:
1. Keep views already on a square with the right piece code; keep tray views at the right index.
2. For every other occupied square take the **nearest free view with the same code**, else spawn (scale-in).
3. Fill trays from `MoveRecord.CapturedPiece` the same way; leftover views scale out and return to a pool.
4. Tween everything to its target: mover arcs (knights higher), castling rook follows with a delay, captured piece
   waits ~80% of the move then flies to the tray, promotion pawn walks to the last rank then fades while the new
   piece scales in. Dragged pieces snap back/forward quickly without an arc.

This one routine handles normal moves, captures, castling, en passant, promotion, undo, restart, save-restore and
"Replay Last" (sync to previous position without animation, then animate to current). Views never feed back into
`Game`. `IsAnimating` gates AI application, game-over dialog and auto-rotate. `Animations` off or speed (0.5–2×)
come from settings.

## Rendering notes

Shared meshes and materials (SRP batcher friendly), squares merged into two meshes, table/squares do not cast
shadows, one directional light, reflection probe rendered once per theme change. See [performance.md](performance.md).

## Validation

- PlayMode `SpecialMovesAnimateToTheRightSquares` (castling + undo, en passant, capture-promotion + undo: every
  square's view code and settled position match `Game`), `PlayerMovesThroughInputRouting`.
- Visual review: `./RoyalGambit.x86_64 -shots <dir>` produces menu, 3D, selection, 2D, rotated 2D, three piece-theme
  2D shots, black side, three board themes, castling/captures, checkmate, promotion, settings.

Related: [camera_and_input.md](camera_and_input.md), [state_management.md](state_management.md).
