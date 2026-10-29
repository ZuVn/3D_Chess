using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Chess.AI
{
    public sealed class UciResult
    {
        public string BestMove;
        /// <summary>Last reported score from the side to move's view (centipawns), or mate distance if <see cref="IsMate"/>.</summary>
        public int Score;
        public bool IsMate;
        public int Depth;
        public long ElapsedMs;
    }

    /// <summary>
    /// Minimal UCI client for an external engine process (Stockfish). Every call that talks to the engine
    /// runs off the caller's thread; one search at a time. Throws <see cref="TimeoutException"/> or
    /// <see cref="InvalidOperationException"/> on engine failure so the caller can fall back or restart.
    /// </summary>
    public sealed class UciEngine : IDisposable
    {
        public string Name { get; private set; } = "UCI engine";
        public bool IsRunning => _proc != null && !_proc.HasExited;

        readonly string _path;
        Process _proc;
        readonly BlockingCollection<string> _lines = new BlockingCollection<string>();
        readonly SemaphoreSlim _busy = new SemaphoreSlim(1, 1);

        public UciEngine(string path) { _path = path; }

        public Task StartAsync(int threads = 1, int hashMb = 64, int timeoutMs = 10000) => Task.Run(() =>
        {
            var psi = new ProcessStartInfo(_path)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(_path) ?? "",
            };
            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (_, e) => { try { if (e.Data != null) _lines.Add(e.Data); } catch (InvalidOperationException) { /* disposed */ } };
            _proc.ErrorDataReceived += (_, __) => { };
            if (!_proc.Start()) throw new InvalidOperationException("Engine process failed to start.");
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();

            Send("uci");
            WaitFor(l => { if (l.StartsWith("id name ")) Name = l.Substring(8).Trim(); return l == "uciok"; }, timeoutMs);
            Send($"setoption name Threads value {threads}");
            Send($"setoption name Hash value {hashMb}");
            Send("isready");
            WaitFor(l => l == "readyok", timeoutMs);
        });

        public void NewGame()
        {
            if (!IsRunning) return;
            Send("ucinewgame");
        }

        /// <summary>
        /// Search the position. On cancellation sends "stop", drains the reply and throws OperationCanceledException.
        /// </summary>
        public Task<UciResult> GoAsync(string fen, IEnumerable<string> moves, int moveTimeMs, int depth, CancellationToken ct,
            Action<UciResult> onInfo = null) => Task.Run(() =>
        {
            if (!_busy.Wait(0)) throw new InvalidOperationException("Engine is already searching.");
            try
            {
                if (!IsRunning) throw new InvalidOperationException("Engine is not running.");
                while (_lines.TryTake(out _)) { } // drop stale output

                string movesPart = string.Join(" ", moves ?? Array.Empty<string>());
                Send(movesPart.Length > 0 ? $"position fen {fen} moves {movesPart}" : $"position fen {fen}");
                Send(depth > 0 ? $"go depth {depth}" : $"go movetime {Math.Max(10, moveTimeMs)}");

                var sw = Stopwatch.StartNew();
                var result = new UciResult();
                // Generous deadline: movetime plus slack, or a depth search capped at a minute.
                long deadline = depth > 0 ? 60000 : moveTimeMs + 5000;
                bool stopSent = false;
                while (true)
                {
                    if (ct.IsCancellationRequested && !stopSent) { Send("stop"); stopSent = true; deadline = sw.ElapsedMilliseconds + 2000; }
                    if (sw.ElapsedMilliseconds > deadline) { Kill(); throw new TimeoutException("Engine did not answer in time."); }
                    if (!_lines.TryTake(out var line, 50))
                    {
                        if (!IsRunning) throw new InvalidOperationException("Engine process exited.");
                        continue;
                    }
                    if (line.StartsWith("info ") && line.Contains(" score ")) { ParseInfo(line, result); result.ElapsedMs = sw.ElapsedMilliseconds; onInfo?.Invoke(result); }
                    else if (line.StartsWith("bestmove"))
                    {
                        if (stopSent) throw new OperationCanceledException(ct);
                        var parts = line.Split(' ');
                        if (parts.Length < 2 || parts[1] == "(none)") throw new InvalidOperationException("Engine returned no move.");
                        result.BestMove = parts[1];
                        result.ElapsedMs = sw.ElapsedMilliseconds;
                        return result;
                    }
                }
            }
            finally { _busy.Release(); }
        });

        static void ParseInfo(string line, UciResult r)
        {
            var t = line.Split(' ');
            for (int i = 0; i < t.Length - 1; i++)
            {
                if (t[i] == "depth" && int.TryParse(t[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int d)) r.Depth = d;
                if (t[i] == "score" && i + 2 < t.Length && int.TryParse(t[i + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                {
                    r.IsMate = t[i + 1] == "mate";
                    r.Score = v;
                }
            }
        }

        void Send(string cmd)
        {
            try { _proc.StandardInput.WriteLine(cmd); _proc.StandardInput.Flush(); }
            catch (Exception e) { throw new InvalidOperationException("Engine pipe closed.", e); }
        }

        void WaitFor(Func<string, bool> pred, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (_lines.TryTake(out var l, 50)) { if (pred(l)) return; }
                else if (!IsRunning) throw new InvalidOperationException("Engine process exited during startup.");
            }
            Kill();
            throw new TimeoutException("Engine did not respond during startup.");
        }

        void Kill()
        {
            try { if (IsRunning) _proc.Kill(); } catch { /* already gone */ }
        }

        public void Dispose()
        {
            if (_proc == null) return;
            try
            {
                if (IsRunning)
                {
                    Send("stop");
                    Send("quit");
                    if (!_proc.WaitForExit(500)) _proc.Kill();
                }
            }
            catch { Kill(); }
            _proc.Dispose();
            _proc = null;
            _lines.CompleteAdding();
        }
    }
}
