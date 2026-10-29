# Testing

Scope: automated tests, how to run them, coverage and gaps, build smoke tests, bug reporting.
Source: `Assets/Tests/EditMode/` (`RulesTests.cs`, `AITests.cs`, `ClockTests.cs`; asmdef `Chess.Tests.EditMode`),
`Assets/Tests/PlayMode/GameplayTests.cs` (asmdef `Chess.Tests.PlayMode`), `Tools/CoreTests/CoreTests.csproj`,
`Tools/Match/`, `Assets/Scripts/Core/Benchmark.cs`.
Status: last full run on 2026-10-29 — .NET 60/60, EditMode 60/60, PlayMode 12/12 (re-run before claiming this again).

## Commands

```bash
# Fast: rules + AI + clock (same EditMode sources, NUnit via NuGet, no Unity)
~/Unity/Hub/Editor/6000.6.7f1/Editor/Data/DotNetSdk/dotnet test Tools/CoreTests

# Unity (close editors on this project first; results are NUnit XML)
UNITY=~/Unity/Hub/Editor/6000.6.7f1/Editor/Unity
$UNITY -batchmode -projectPath . -runTests -testPlatform EditMode -testResults $PWD/Logs/editmode.xml
$UNITY -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults $PWD/Logs/playmode.xml
# Single test: add  -testFilter "PlayerMovesThroughInputRouting"
```
Exit code 0 = all passed, 2 = failures, 1 = compile error/abort (check `-logFile`). `-testPlatform StandaloneLinux64`
built a test player but returned no results here — not a supported route yet.

## What is covered

- **Rules (EditMode)**: perft on 6 standard positions (chessprogramming.org counts), make/unmake + Zobrist
  consistency, FEN round-trip and rejection, pins, double check, king captures into defended squares, castling
  restrictions and right loss, en passant (incl. exposing the king, only immediately), all promotions, mate,
  stalemate, insufficient material, threefold (incl. ep hashing), fifty-move vs mate precedence, SAN, undo, PGN,
  resign/agreement.
- **Clock (EditMode)**: start rule, increment, flag at zero, undo, disabled clock, timeout vs bare king.
- **AI (EditMode)**: tactics (mate 1/2, hanging queen, no stalemate when winning), Easy legality over 60 plies,
  cancellation latency, position restored after search.
- **Gameplay (PlayMode, real `App`)**: move via input routing, illegal moves rejected, picking at 5 yaws in 3D and 2D,
  2D/3D switch keeps the position, auto-rotate (on, and toggled mid-game) does not affect rules, AI reply + undo,
  restart cancels a running search (no stale move), castling/en passant/promotion/capture views match the game
  after animation (and after undo), promotion waits for a choice, clock flag-fall and pause in menus,
  settings/autosave persistence and corrupt-file recovery.

PlayMode tests set `Storage.RootOverride` to a temp folder and destroy only the objects they created
(destroying every root object also kills the test runner and hangs the run).

## Gaps (not automated)

Real mouse/touch drag events (tests call the same `TryMove` path), window resizing, audio, every theme combination,
Stockfish failure/timeout paths, save files from older versions, native Windows. Strength is checked manually
with `Tools/Match`.

## Build smoke tests

```bash
cd Builds/Linux
./RoyalGambit.x86_64 -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -shots /tmp/shots   # screenshots
./RoyalGambit.x86_64 -benchmark 60 -benchmark-out bench.txt -difficulty 2                           # play vs Stockfish
grep -c Exception ~/.config/unity3d/Zn/"Royal Gambit Chess"/Player.log   # when run without -logFile
```
Inspect the PNGs (menu, 3D, 2D, rotated 2D, themes, castling/captures, checkmate dialog, promotion, settings).
Windows: only `wine RoyalGambit.exe -benchmark 25` has been run (see [build_and_release.md](build_and_release.md)).

## Regression and bug reports

1. Reproduce with a FEN (`Position.ToFen()`/PGN export) or a UCI move list (`Game.FromUciMoves`).
2. Add a failing test first: rules/AI/clock → EditMode; flow, view sync, input or camera → PlayMode using the
   `Local(fen)` helper and `AssertViewMatchesGame()`.
3. Fix, run all three commands above, and attach `Player.log` plus the build's `BUILD_INFO.txt` for build-only issues.

Related: [chess_rules.md](chess_rules.md), [camera_and_input.md](camera_and_input.md), [performance.md](performance.md).
