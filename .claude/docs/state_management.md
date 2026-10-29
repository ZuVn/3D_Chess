# State Management

Scope: ownership of game state, turn flow, history/undo, captures, restart/resign/game over, clocks,
settings and save/load, async cleanup.
Source: `Assets/Scripts/Core/GameSession.cs`, `App.cs`, `AiPlayer.cs`; `Assets/Scripts/Settings/Settings.cs`
(`Settings`, `Storage`, `SafeFile`); `Assets/Scripts/SaveSystem/SaveGame.cs` (`SavedGame`); `Assets/Scripts/Chess/Clock.cs`.
Status: implemented.

## Ownership

| State | Owner | Notes |
|---|---|---|
| Position, history, result | `GameSession.Game` (`Chess.Game`) | Only mutated through `GameSession`. |
| Clock | `GameSession.Clock` (`ChessClock`) | Recreated in `Start`. |
| Mode, human side, difficulty, think time | `GameSession` properties | Set in `Start`. |
| Captured pieces | Derived from `MoveRecord.CapturedPiece` | No separate list; HUD and trays recompute. |
| Selection / hover / drag target | `BoardInput` | Cleared on every position change. |
| Settings | `App.Settings` | Single instance. |

## Turn flow (`GameSession`)

- `Start(mode, human, difficulty, proMs, game?, timeControl, whiteMs, blackMs)` cancels AI, sets everything,
  raises `PositionChanged(Move.None)` (and `GameEnded` if the loaded game is already over).
- `TryHumanMove(m)`: only on the human's turn; `Game.TryMakeMove`, `Clock.OnMove`, then `PositionChanged(move)`,
  `MovePlayed`, and `GameEnded` if over.
- `Tick(viewIdle, clockPaused, dt)` every frame from `App.Update` (only while in a game): advances the clock
  (paused while any menu/dialog is open; sub-millisecond remainder carried so high FPS does not slow it), flags the
  side to move at zero, starts AI searches, applies finished ones (see [ai_engine.md](ai_engine.md)).
- `Undo()`: local = one ply; vs AI = back to the human's previous turn (undoes the AI reply too); always
  `CancelAi()` first and `Clock.OnUndo()` per ply. `CanUndo` requires a human move to exist.
- `Restart()` = `Start` with the same settings, start FEN and time control.
- `Resign()`: side to move (local) or the human (vs AI). `OfferDraw()`: local → agreement (App asks for
  confirmation); vs AI → accepted only if the AI's last eval ≤ −150 cp for itself (or it is being mated) after ≥ 10 plies.

## App-level flow (`App`)

- Main menu ↔ game (`InGame`). `StartNewGame`, `ContinueSavedGame`, `ReturnToMenu` (saves first), `Quit`.
- `OnPositionChanged`: cancel pending promotion, clear selection, `BoardView.Sync`, highlights, `Hud.Refresh`,
  autosave, set auto-rotate flag (two-player, if enabled).
- `GameEnded`: delete autosave; game-over sound + dialog once `!Board.IsAnimating`.
- Promotion: `_promoFrom/_promoTo` pending while the picker is open; input locked; Esc cancels.

## Persistence

- Location: `Storage.Dir` = `Application.persistentDataPath` (Linux: `~/.config/unity3d/Zn/Royal Gambit Chess/`;
  Windows: `%USERPROFILE%\AppData\LocalLow\Zn\Royal Gambit Chess\`) unless `Storage.RootOverride` is set
  (PlayMode tests and `-shots`/`-benchmark` use a temp folder so they never touch real data).
- `settings.json` (`Settings`, `JsonUtility`): display, quality, audio, camera, themes, aids, last new-game options,
  time control index. `Validate()` clamps every field; unreadable files fall back to defaults (first run picks Low
  quality on GPUs reporting < 2 GB). Saved after every settings change.
- `savegame.json` (`SavedGame`): mode, human colour, difficulty, think time, start FEN, UCI move list, clock
  base/increment and remaining ms. `Load()` replays the moves through `Game.FromUciMoves`; invalid, illegal or
  finished saves return null. Written after every position change of an unfinished game; deleted on game over.
- `SafeFile.WriteAllText`: write `.tmp`, re-read and validate, then `File.Replace`/`Move` — a bad write never
  clobbers the previous file.
- PGN export: `games/game-<timestamp>.pgn` under the data folder plus clipboard.

## Invariants

- Every state change ends in exactly one `PositionChanged` so views can resync from scratch.
- No async callback mutates state: AI results are pulled and validated in `Tick` on the main thread.
- `CancelAi()` before any operation that changes the position outside normal play (undo, restart, resign, start, menu).
- Clock snapshots: one per ply (`OnMove`/`OnUndo`); restored saves seed snapshots with current times
  (undo past the resume point keeps those times — known limitation).

## Validation

PlayMode: `AiRespondsAndUndoTakesBackBothMoves`, `RestartCancelsRunningSearch`, `ClockRunsOutAndPausesInMenus`,
`SettingsAndSavePersist` (including corrupt settings and illegal save). EditMode: `ClockTests`, `RulesTests.UndoRestoresEverything`.
Commands: [testing.md](testing.md).

Related: [architecture.md](architecture.md), [chess_rules.md](chess_rules.md).
