# Chess Rules Engine

Scope: board representation, move generation/validation, game results, notation, undo, clock rules.
Source: `Assets/Scripts/Chess/` — `Types.cs`, `Position.cs`, `Game.cs`, `Clock.cs` (assembly `Chess.Core`, no UnityEngine).
Status: implemented and tested (see [testing.md](testing.md)).

## Representation

- Squares `0..63`, **a1 = 0, h1 = 7, a8 = 56** (`Sq.Make(file, rank) = rank * 8 + file`). `Sq.None = -1`.
- Piece codes (`Piece`): `0` empty, `PieceType` value (1 Pawn … 6 King) for White, `type | 8` for Black.
- `Move` is a readonly struct: `From`, `To`, `Promotion`, `Flags` (`Capture`, `DoublePush`, `EnPassant`,
  `CastleKing`, `CastleQueen`). `Move.SameAs` compares from/to/promotion only (flags are derived).
- `Position`: mailbox `int[64]`, side to move, castling bits (`WhiteKingSide=1, WhiteQueenSide=2, BlackKingSide=4,
  BlackQueenSide=8`), en passant square, halfmove clock, fullmove number, king squares, incremental Zobrist `Key`
  (deterministic xorshift seed, so keys are stable across runs).

## Move generation and legality

- `GeneratePseudo` produces pseudo-legal moves (precomputed knight/king targets and sliding rays);
  `capturesOnly` adds queen promotions too (used by quiescence search).
- `LegalMoves()` / `IsLegal()` = make the move, reject if own king is attacked, unmake. This covers pins,
  discovered checks, double check and en passant that exposes the king.
- Castling is generated only if: rights present, squares between king and rook empty, king not in check, and
  the transit/destination squares not attacked. Rights are removed when king/rook move or a rook is captured
  (`CastleMask`).
- En passant square is set only after a double push; FEN import drops an ep square with no pawn behind it.
- Promotion generates Q/R/B/N; a promotion move without a piece is rejected by `Game.TryMakeMove`.
- `MakeNullMove`/`UnmakeNullMove` exist for search only.

## Game (the API gameplay code must use)

- `Game.TryMakeMove(Move)` accepts a move only if it matches an entry in `LegalMoves`; it records SAN, mover,
  captured piece (`CapturedPiece`, including en passant pawns) and the new Zobrist key, then recomputes the result.
- `Game.TryMakeUci("e7e8q")`, `Game.FromUciMoves(fen, moves)` (throws `FormatException` on an illegal move).
- `Game.Undo()` unmakes one ply, pops history and key, and **recomputes** the result (also clears resign/agreement/flag).
- Never call `Position.MakeMove` from gameplay code; it skips legality and history.

## Results (`GameResult`, `ResultReason`)

Computed after each move, in this order: checkmate / stalemate (no legal moves), insufficient material,
threefold repetition, fifty-move rule. So checkmate on the 100th halfmove wins (tested).

| Rule | Implementation |
|---|---|
| Insufficient material | K v K, K+single minor v K, any number of bishops all on one square colour (`IsInsufficientMaterial`). |
| Threefold repetition | Count of equal Zobrist keys over the whole game ≥ 3; applied **automatically** (not claimed). |
| Fifty-move rule | `Halfmove >= 100`; applied **automatically**. |
| Resignation / agreement | `Resign(Side)`, `AgreeDraw()`. |
| Timeout | `Flag(Side loser)`: loss, or draw if the winner has only a king (`TimeoutVsInsufficientMaterial`). |

Known deviations (documented, not bugs to "fix" silently): repetition/fifty-move are automatic; the ep part of
the hash ignores the case where the only ep capture is illegal (pinned pawn); timeout-draw only checks the
lone-king case, not every "no possible mate" position. Fivefold/75-move rules are subsumed by the automatic rules.

## Notation

- FEN: `Position(string fen)` / `SetFen` validate field count, 8 ranks of 8 squares, one king each, no pawns on
  ranks 1/8, side not to move not in check; invalid castling rights are dropped. `ToFen()` round-trips.
- SAN: generated on move (disambiguation by file, rank, or both; `+` / `#`; `O-O`, `O-O-O`; `=Q`).
- PGN: **export only** (`Game.ToPgn`, with `SetUp`/`FEN` tags for non-standard starts and `Termination`).
  PGN import and SAN parsing are **not implemented**.

## Clock (`Clock.cs`)

`ChessClock` is driven by explicit milliseconds (deterministic): does not run before the first ply, Fischer
increment added to the mover in `OnMove`, per-ply snapshots for `OnUndo`, `Restore` for saved games.
Wiring to real time lives in `GameSession` (see [state_management.md](state_management.md)).

## Invariants

- Make/unmake restores the exact position and Zobrist key (tested for every legal move of Kiwipete).
- `KeyHistory[0]` is the start position; `KeyHistory.Count == History.Count + 1`.
- `LegalMoves` is always the list for the current position (refreshed in `TryMakeMove`/`Undo`).

## Validation

```bash
~/Unity/Hub/Editor/6000.6.7f1/Editor/Data/DotNetSdk/dotnet test Tools/CoreTests   # fast loop
```
`Assets/Tests/EditMode/RulesTests.cs` holds perft (start position to depth 4, Kiwipete to 3, position 3 to 5,
positions 4/4-mirrored/5/6 to 3; reference counts from chessprogramming.org), FEN validation, pins, double check,
castling, en passant (incl. exposing the king), promotion, mate/stalemate, insufficient material, repetition,
fifty-move, SAN disambiguation, undo, PGN. `ClockTests.cs` covers the clock and `Flag`. Any rule change needs a
new test with a concrete FEN. Run the EditMode command in [testing.md](testing.md) as well.

Related: [ai_engine.md](ai_engine.md) (search uses `Position` directly), [state_management.md](state_management.md).
