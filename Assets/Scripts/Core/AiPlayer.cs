using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chess;
using Chess.AI;
using UnityEngine;

namespace ChessGame
{
    public enum Difficulty { Easy, Hard, Professional }

    public sealed class AiMove
    {
        public string Uci;
        /// <summary>Evaluation from White's point of view, centipawns (null if unknown).</summary>
        public int? EvalWhite;
        /// <summary>Signed mate distance from White's point of view (+3 = White mates in 3).</summary>
        public int? MateWhite;
        public int Depth;
        public long Ms;
        public string Engine;
    }

    /// <summary>
    /// Runs the computer's move search off the main thread. Easy/Hard use the built-in searcher;
    /// Professional uses Stockfish over UCI and falls back (clearly labelled) to the built-in engine.
    /// Searches are serialised: a new request waits for a cancelled one to unwind.
    /// </summary>
    public sealed class AiPlayer : IDisposable
    {
        readonly Searcher _searcher = new Searcher(20);
        readonly object _searchLock = new object();
        readonly string _enginePath;
        UciEngine _sf;
        Task _sfStart;
        bool _sfFailed;
        volatile bool _clearPending;
        Task _last = Task.CompletedTask;

        // Live search info for the HUD, written by the worker thread.
        volatile string _live;
        public string LiveInfo => _live;

        public AiPlayer()
        {
            _enginePath = FindStockfish();
        }

        public bool StockfishFound => _enginePath != null;
        public bool StockfishReady => _sf != null && !_sfFailed;

        public string ProfessionalLabel => _enginePath == null || _sfFailed
            ? "Built-in engine (Stockfish unavailable, reduced strength)"
            : "Stockfish 19";

        public static string FindStockfish()
        {
            string exe = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                ? "stockfish.exe" : "stockfish";
            string os = exe.EndsWith(".exe") ? "windows" : "linux";
            var candidates = new[]
            {
                Path.Combine(Application.streamingAssetsPath, "Stockfish", exe),
                Path.Combine(Application.dataPath, "..", "ThirdParty", "Stockfish", os, exe), // editor / source checkout
            };
            foreach (var c in candidates)
                if (File.Exists(c))
                {
                    if (os == "linux") EnsureExecutable(c);
                    return Path.GetFullPath(c);
                }
            return null;
        }

        static void EnsureExecutable(string path)
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("chmod", $"+x \"{path}\"")
                    { UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit(2000);
            }
            catch (Exception e) { Debug.LogWarning($"chmod failed for Stockfish: {e.Message}"); }
        }

        /// <summary>Start Stockfish in the background so the first Professional move does not pay the startup cost.</summary>
        public void WarmUpStockfish()
        {
            if (_enginePath == null || _sf != null || _sfFailed) return;
            _sf = new UciEngine(_enginePath);
            int threads = Mathf.Clamp(SystemInfo.processorCount / 2, 1, 8);
            _sfStart = _sf.StartAsync(threads, 128);
        }

        public void NewGame()
        {
            _clearPending = true; // cleared by the worker before the next search, never on the main thread
            try { if (_sf != null && _sfStart != null && _sfStart.IsCompleted && !_sfStart.IsFaulted) _sf.NewGame(); }
            catch (Exception e) { Debug.LogWarning(e.Message); }
        }

        /// <param name="budgetMs">Clock-derived cap on thinking time (null = the difficulty's normal limit).</param>
        public Task<AiMove> ThinkAsync(Game game, Difficulty difficulty, int proThinkMs, int? budgetMs, CancellationToken ct)
        {
            // Snapshot everything on the main thread; the worker never touches the live Game.
            var pos = game.Position.Clone();
            var keys = game.KeyHistory.ToArray();
            string startFen = game.StartFen;
            var moves = game.History.Select(h => h.Move.ToUci()).ToArray();
            var prev = _last;
            _live = null;
            if (budgetMs is int cap) proThinkMs = Math.Min(proThinkMs, cap);

            var task = Task.Run(async () =>
            {
                try { await prev.ConfigureAwait(false); } catch { /* previous search already reported */ }
                ct.ThrowIfCancellationRequested();
                if (difficulty == Difficulty.Professional && _sf != null && !_sfFailed)
                {
                    try { return await StockfishMove(pos, startFen, moves, proThinkMs, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Stockfish failed ({e.Message}); falling back to the built-in engine.");
                        _sfFailed = true;
                        try { _sf.Dispose(); } catch { /* ignore */ }
                    }
                }
                return BuiltIn(pos, keys, difficulty, proThinkMs, budgetMs, ct);
            });
            _last = task;
            return task;
        }

        AiMove BuiltIn(Position pos, ulong[] keys, Difficulty d, int proMs, int? budgetMs, CancellationToken ct)
        {
            var lim = d switch
            {
                Difficulty.Easy => new SearchLimits { MaxDepth = 2, TimeMs = 800, FullEval = false, RandomMarginCp = 40, SloppyChance = 0.15 },
                Difficulty.Hard => new SearchLimits { MaxDepth = 64, TimeMs = 1500, FullEval = true },
                _ => new SearchLimits { MaxDepth = 64, TimeMs = proMs, FullEval = true },
            };
            if (budgetMs is int cap) lim.TimeMs = Math.Min(lim.TimeMs, cap);
            Side stm = pos.SideToMove;
            SearchResult r;
            lock (_searchLock)
            {
                if (_clearPending) { _clearPending = false; _searcher.Clear(); }
                r = _searcher.Search(pos, keys, lim, ct, it => _live = $"depth {it.Depth}");
            }
            ct.ThrowIfCancellationRequested();
            int sign = stm == Side.White ? 1 : -1;
            return new AiMove
            {
                Uci = r.Best.ToUci(),
                EvalWhite = r.IsMate ? (int?)null : r.Score * sign,
                MateWhite = r.IsMate ? r.MateInMoves * sign : (int?)null,
                Depth = r.Depth,
                Ms = r.ElapsedMs,
                Engine = d == Difficulty.Professional ? "Built-in (fallback)" : "Built-in",
            };
        }

        async Task<AiMove> StockfishMove(Position pos, string startFen, string[] moves, int ms, CancellationToken ct)
        {
            await _sfStart.ConfigureAwait(false);
            Side stm = pos.SideToMove;
            int sign = stm == Side.White ? 1 : -1;
            var r = await _sf.GoAsync(startFen, moves, ms, 0, ct,
                info => _live = info.IsMate ? $"depth {info.Depth}, mate {info.Score}" : $"depth {info.Depth}").ConfigureAwait(false);
            // Never trust an external process blindly: the move must be legal in our own position.
            if (pos.ParseUci(r.BestMove).IsNone) throw new InvalidOperationException($"illegal engine move '{r.BestMove}'");
            return new AiMove
            {
                Uci = r.BestMove,
                EvalWhite = r.IsMate ? (int?)null : r.Score * sign,
                MateWhite = r.IsMate ? r.Score * sign : (int?)null,
                Depth = r.Depth,
                Ms = r.ElapsedMs,
                Engine = _sf.Name,
            };
        }

        public void Dispose()
        {
            try { _sf?.Dispose(); } catch { /* ignore */ }
            _sf = null;
        }
    }
}
