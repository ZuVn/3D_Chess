# AI Engine

Scope: difficulty levels, built-in search/evaluation, Stockfish UCI integration, threading, cancellation, licensing.
Source: `Assets/Scripts/AI/Searcher.cs`, `Evaluator.cs`, `UciEngine.cs` (assembly `Chess.AI`, no UnityEngine);
orchestration in `Assets/Scripts/Core/AiPlayer.cs` and `GameSession.cs`.
Status: implemented. Easy/Hard verified in-game and in tests; Professional (Stockfish) verified in the Linux
build only — not verified on native Windows (Stockfish crashes under Wine; see [build_and_release.md](build_and_release.md)).

## Difficulty levels (`AiPlayer.BuiltIn` / `ThinkAsync`)

| Level | Engine | Limits |
|---|---|---|
| Easy | `Searcher`, basic eval (material + PST) | depth 2, 800 ms; every root move scored exactly, random pick within 40 cp of best, 15% chance of a 150 cp margin; never discards a forced mate |
| Hard | `Searcher`, full eval | iterative deepening to depth 64, 1500 ms |
| Professional | Stockfish 19 (`UciEngine`), `go movetime` = selected think time (1/2/5/10 s) | falls back to `Searcher` with the same time if Stockfish is missing or fails, labelled "Built-in engine (Stockfish unavailable, reduced strength)" in the UI |

With a chess clock, `GameSession.AiBudgetMs()` caps every level: `remaining/30 + 0.8 × increment − 450 ms`,
at least 100 ms and at most a quarter of the remaining time.

Measured strength (`Tools/Match`, 8 games each): Hard 8–0 Easy; Stockfish (300 ms/move) 8–0 Hard. No Elo claims.

## Built-in search (`Searcher`)

Negamax alpha-beta with iterative deepening, PVS, transposition table (`1 << 20` entries, mate scores
ply-adjusted), quiescence search (captures + queen promotions, delta pruning), null-move pruning (not in check,
side has non-pawn material), late-move reductions, check extension, mate-distance pruning, repetition and
fifty-move detection inside the tree (using game key history). Ordering: TT move, MVV-LVA captures/promotions,
two killers per ply, history heuristic. Abort checks every 1024 nodes (time limit or `CancellationToken`);
an aborted iteration keeps the previous best move. Not thread-safe: one search per instance; the caller passes a
`Position` clone, which is restored on return.

## Evaluation (`Evaluator.Evaluate`)

Tapered middlegame/endgame score from the side to move's view. Basic: material (P100 N320 B330 R500 Q900) +
Michniewski "Simplified Evaluation Function" piece-square tables (separate king MG/EG tables). Full adds bishop
pair, doubled/isolated/passed pawns, rook on (semi-)open file, slider mobility, king pawn shelter. Uses
`stackalloc` (no per-node allocation).

## Stockfish integration (`UciEngine`)

- Path lookup (`AiPlayer.FindStockfish`, main thread): `StreamingAssets/Stockfish/stockfish[.exe]`, then
  `<project>/ThirdParty/Stockfish/<linux|windows>/` for the editor. Linux: `chmod +x` attempted.
- `StartAsync`: `uci`→`uciok`, `Threads = clamp(cpu/2, 1, 8)`, `Hash = 128`, `isready`→`readyok`, 10 s timeout.
  Started early by `WarmUpStockfish()` when a Professional game begins.
- `GoAsync`: `position fen <start> moves <uci...>` then `go movetime N`; parses `info ... score cp|mate`
  (side-to-move view, converted to White's view in `AiPlayer`); deadline = movetime + 5 s, then the process is
  killed and `TimeoutException` raised. Cancellation sends `stop`, drains `bestmove` (≤ 2 s) and throws
  `OperationCanceledException`. One search at a time (`SemaphoreSlim`).
- Every engine reply is re-validated: `pos.ParseUci(bestmove)` must be a legal move or the reply is rejected.
- Any engine error marks Stockfish failed for the session, disposes the process, and the same request is
  answered by the built-in searcher.

## Threading, cancellation, stale results

- `AiPlayer.ThinkAsync` snapshots position clone, key history, start FEN and UCI move list **on the main thread**,
  then runs on `Task.Run`. Requests are serialised: each waits for the previous task (cancelled ones unwind fast).
- `GameSession.Tick` starts a search when it is the AI's turn, polls `IsCompleted` (never blocks), and applies a
  result only when the board is idle and ≥ 0.45 s have passed. `CancelAi()` (undo, restart, resign, new game,
  menu) cancels the token and drops the task reference, so a late result is never seen.
- Before applying: the UCI move is parsed against the **live** position and `IsAiTurn` rechecked; otherwise it is
  logged and discarded. Faulted tasks show a notice and are retried next frame.
- Transposition-table clearing for a new game happens on the worker (`_clearPending`), not the main thread.

## Licensing and distribution

Stockfish 19 is GPLv3, shipped **unmodified as a separate executable** with `COPYING.txt`, `AUTHORS.txt` and
`SOURCE.txt` (source link to tag `sf_19`) copied by `BuildScript` next to it. Binaries are fetched with
`Tools/fetch_stockfish.sh` and are git-ignored (~99 MB each). Do not link Stockfish code into the game.

## Validation

- `Assets/Tests/EditMode/AITests.cs`: mate in 1/2, wins a hanging queen, avoids stalemating, Easy never plays
  illegal moves over 60 plies and never misses mate-in-1, cancellation returns within 1.5 s, search restores the position.
- PlayMode: `AiRespondsAndUndoTakesBackBothMoves`, `RestartCancelsRunningSearch` (no stale move applied).
- Strength: `dotnet run -c Release --project Tools/Match -- 8 ThirdParty/Stockfish/linux/stockfish`.
- Stockfish in a build: `./RoyalGambit.x86_64 -benchmark 60 -difficulty 2 -benchmark-out f.txt` reports the engine name
  and depth (see [performance.md](performance.md)).

Related: [chess_rules.md](chess_rules.md), [state_management.md](state_management.md).
