using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Chess.AI
{
    public sealed class SearchLimits
    {
        public int MaxDepth = 64;
        public int TimeMs = 2000;
        public bool FullEval = true;
        /// <summary>When > 0 every root move gets an exact score and one within this margin of the best is picked at random.</summary>
        public int RandomMarginCp;
        /// <summary>Chance of widening the random margin to <see cref="SloppyMarginCp"/> (a "reasonable but imperfect" move).</summary>
        public double SloppyChance;
        public int SloppyMarginCp = 150;
    }

    public sealed class SearchResult
    {
        public Move Best;
        /// <summary>Score from the side to move's point of view, centipawns (mate scores are ±(Mate - plies)).</summary>
        public int Score;
        public int Depth;
        public long Nodes;
        public long ElapsedMs;
        public bool IsMate => Math.Abs(Score) >= Searcher.Mate - 1000;
        public int MateInMoves => (Searcher.Mate - Math.Abs(Score) + 1) / 2 * Math.Sign(Score);
    }

    /// <summary>
    /// Negamax alpha-beta with iterative deepening, transposition table, quiescence search, null-move
    /// pruning, late-move reductions, PVS, check extension, and TT/MVV-LVA/killer/history ordering.
    /// Not thread-safe: one search at a time per instance. The position passed in is mutated during search
    /// and restored afterwards, so callers should hand in a clone.
    /// </summary>
    public sealed class Searcher
    {
        public const int Mate = 100000;
        const int Inf = 1000000;
        const int MaxPly = 128;

        struct TtEntry { public ulong Key; public Move Move; public int Score; public sbyte Depth; public byte Flag; }
        const byte Exact = 1, Lower = 2, Upper = 3;
        readonly TtEntry[] _tt;
        readonly int _ttMask;

        readonly Move[][] _moves = new Move[MaxPly][];
        readonly int[][] _scores = new int[MaxPly][];
        readonly Move[,] _killers = new Move[MaxPly, 2];
        readonly int[,] _history = new int[64, 64];
        readonly ulong[] _keys = new ulong[1024 + MaxPly];
        int _keyCount;

        Position _pos;
        SearchLimits _lim;
        CancellationToken _ct;
        Stopwatch _sw;
        long _nodes;
        bool _abort;
        readonly Random _rng;

        public Searcher(int ttSizeLog2 = 20, int seed = 0)
        {
            _tt = new TtEntry[1 << ttSizeLog2];
            _ttMask = (1 << ttSizeLog2) - 1;
            for (int i = 0; i < MaxPly; i++) { _moves[i] = new Move[256]; _scores[i] = new int[256]; }
            _rng = seed == 0 ? new Random() : new Random(seed);
        }

        public void Clear()
        {
            Array.Clear(_tt, 0, _tt.Length);
            Array.Clear(_history, 0, _history.Length);
        }

        /// <param name="history">Keys of earlier game positions (for repetition detection), oldest first, may include the root.</param>
        /// <param name="onIteration">Called after each completed depth (on the search thread).</param>
        public SearchResult Search(Position pos, IReadOnlyList<ulong> history, SearchLimits limits, CancellationToken ct = default,
            Action<SearchResult> onIteration = null)
        {
            _pos = pos; _lim = limits; _ct = ct; _nodes = 0; _abort = false;
            _sw = Stopwatch.StartNew();
            Array.Clear(_killers, 0, _killers.Length);
            for (int i = 0; i < 64; i++) for (int j = 0; j < 64; j++) _history[i, j] /= 8;

            _keyCount = 0;
            if (history != null)
            {
                int start = Math.Max(0, history.Count - 1024);
                for (int i = start; i < history.Count; i++) _keys[_keyCount++] = history[i];
            }
            if (_keyCount == 0 || _keys[_keyCount - 1] != pos.Key) _keys[_keyCount++] = pos.Key;

            var legal = pos.LegalMoves();
            var result = new SearchResult();
            if (legal.Count == 0) return result;
            result.Best = legal[0];
            if (legal.Count == 1 && limits.RandomMarginCp <= 0) { result.Depth = 1; return result; }

            if (limits.RandomMarginCp > 0) return RandomizedSearch(legal, result);

            var rootMoves = legal.ToArray();
            for (int depth = 1; depth <= limits.MaxDepth; depth++)
            {
                int alpha = -Inf, beta = Inf, best = -Inf;
                Move bestMove = Move.None;
                for (int i = 0; i < rootMoves.Length; i++)
                {
                    var m = rootMoves[i];
                    _pos.MakeMove(m);
                    PushKey();
                    int score;
                    if (i == 0) score = -Negamax(depth - 1, -beta, -alpha, 1, true);
                    else
                    {
                        score = -Negamax(depth - 1, -alpha - 1, -alpha, 1, true);
                        if (score > alpha && !_abort) score = -Negamax(depth - 1, -beta, -alpha, 1, true);
                    }
                    _keyCount--;
                    _pos.UnmakeMove();
                    if (_abort) break;
                    if (score > best) { best = score; bestMove = m; }
                    if (score > alpha) alpha = score;
                }
                if (_abort && bestMove.IsNone) break; // partial iteration with nothing better: keep previous
                if (!bestMove.IsNone)
                {
                    result.Best = bestMove;
                    result.Score = best;
                    result.Depth = depth;
                }
                result.Nodes = _nodes;
                result.ElapsedMs = _sw.ElapsedMilliseconds;
                if (_abort) break;
                onIteration?.Invoke(result);

                // Search the best move first next iteration.
                int bi = Array.IndexOf(rootMoves, bestMove);
                if (bi > 0) (rootMoves[0], rootMoves[bi]) = (rootMoves[bi], rootMoves[0]);
                if (Math.Abs(best) >= Mate - depth) break; // forced mate found
                // Don't start an iteration we are unlikely to finish.
                if (_sw.ElapsedMilliseconds > limits.TimeMs / 2) break;
            }
            result.Nodes = _nodes;
            result.ElapsedMs = _sw.ElapsedMilliseconds;
            return result;
        }

        /// <summary>Easy mode: exact score for each root move at a shallow depth, then a random pick among good ones.</summary>
        SearchResult RandomizedSearch(List<Move> legal, SearchResult result)
        {
            int depth = Math.Max(1, _lim.MaxDepth);
            var scores = new int[legal.Count];
            for (int i = 0; i < scores.Length; i++) scores[i] = -Inf;
            int best = -Inf;
            for (int i = 0; i < legal.Count; i++)
            {
                _pos.MakeMove(legal[i]);
                PushKey();
                scores[i] = -Negamax(depth - 1, -Inf, Inf, 1, false);
                _keyCount--;
                _pos.UnmakeMove();
                if (_abort) { scores[i] = -Inf; if (best == -Inf) best = scores[0] = 0; break; }
                if (scores[i] > best) best = scores[i];
            }
            int margin = _rng.NextDouble() < _lim.SloppyChance ? _lim.SloppyMarginCp : _lim.RandomMarginCp;
            // Never throw away a forced mate or walk into one just for variety.
            if (Math.Abs(best) >= Mate - 1000) margin = 0;
            var pool = new List<int>();
            for (int i = 0; i < legal.Count; i++)
                if (scores[i] == best || (scores[i] >= best - margin && Math.Abs(scores[i]) < Mate - 1000)) pool.Add(i);
            int pick = pool[_rng.Next(pool.Count)];
            result.Best = legal[pick];
            result.Score = scores[pick];
            result.Depth = depth;
            result.Nodes = _nodes;
            result.ElapsedMs = _sw.ElapsedMilliseconds;
            return result;
        }

        void PushKey() => _keys[_keyCount++] = _pos.Key;

        bool IsRepetition()
        {
            // Only positions since the last irreversible move can repeat; same side to move = every 2nd entry.
            int limit = Math.Max(0, _keyCount - 1 - _pos.Halfmove);
            for (int i = _keyCount - 3; i >= limit; i -= 2)
                if (_keys[i] == _pos.Key) return true;
            return false;
        }

        void CheckLimits()
        {
            if (_ct.IsCancellationRequested || _sw.ElapsedMilliseconds >= _lim.TimeMs) _abort = true;
        }

        int Eval() => Evaluator.Evaluate(_pos, _lim.FullEval);

        int Negamax(int depth, int alpha, int beta, int ply, bool allowNull)
        {
            if ((++_nodes & 1023) == 0) CheckLimits();
            if (_abort) return 0;
            if (_pos.Halfmove >= 100 || IsRepetition()) return 0;
            if (ply >= MaxPly - 1) return Eval();

            bool inCheck = _pos.InCheck;
            if (inCheck) depth++;
            if (depth <= 0) return Quiesce(alpha, beta, ply);

            // Mate distance pruning.
            alpha = Math.Max(alpha, -Mate + ply);
            beta = Math.Min(beta, Mate - ply - 1);
            if (alpha >= beta) return alpha;

            ref var e = ref _tt[(int)_pos.Key & _ttMask];
            Move ttMove = Move.None;
            if (e.Key == _pos.Key)
            {
                ttMove = e.Move;
                if (e.Depth >= depth)
                {
                    int s = FromTt(e.Score, ply);
                    if (e.Flag == Exact || (e.Flag == Lower && s >= beta) || (e.Flag == Upper && s <= alpha)) return s;
                }
            }

            bool pvNode = beta - alpha > 1;
            if (allowNull && !inCheck && !pvNode && depth >= 3 && HasPieces(_pos.SideToMove) && Eval() >= beta)
            {
                _pos.MakeNullMove();
                PushKey();
                int r = depth >= 6 ? 3 : 2;
                int s = -Negamax(depth - 1 - r, -beta, -beta + 1, ply + 1, false);
                _keyCount--;
                _pos.UnmakeNullMove();
                if (_abort) return 0;
                if (s >= beta) return beta;
            }

            var moves = _moves[ply];
            var scores = _scores[ply];
            int n = _pos.GeneratePseudo(moves);
            ScoreMoves(moves, scores, n, ttMove, ply);

            int origAlpha = alpha, best = -Inf, legal = 0;
            Move bestMove = Move.None;
            Side us = _pos.SideToMove;
            for (int i = 0; i < n; i++)
            {
                PickNext(moves, scores, i, n);
                var m = moves[i];
                _pos.MakeMove(m);
                if (_pos.IsAttacked(_pos.KingSquare(us), us.Other())) { _pos.UnmakeMove(); continue; }
                legal++;
                PushKey();
                bool quiet = !m.IsCapture && !m.IsPromotion;
                int score;
                if (legal == 1) score = -Negamax(depth - 1, -beta, -alpha, ply + 1, true);
                else
                {
                    int reduction = 0;
                    if (depth >= 3 && legal > 3 && quiet && !inCheck && !_pos.InCheck)
                        reduction = legal > 8 ? 2 : 1;
                    score = -Negamax(depth - 1 - reduction, -alpha - 1, -alpha, ply + 1, true);
                    if (score > alpha && reduction > 0) score = -Negamax(depth - 1, -alpha - 1, -alpha, ply + 1, true);
                    if (score > alpha && score < beta) score = -Negamax(depth - 1, -beta, -alpha, ply + 1, true);
                }
                _keyCount--;
                _pos.UnmakeMove();
                if (_abort) return 0;

                if (score > best) { best = score; bestMove = m; }
                if (score > alpha) alpha = score;
                if (alpha >= beta)
                {
                    if (quiet)
                    {
                        if (!_killers[ply, 0].Equals(m)) { _killers[ply, 1] = _killers[ply, 0]; _killers[ply, 0] = m; }
                        _history[m.From, m.To] += depth * depth;
                    }
                    break;
                }
            }

            if (legal == 0) return inCheck ? -Mate + ply : 0;

            byte flag = best <= origAlpha ? Upper : best >= beta ? Lower : Exact;
            if (e.Key != _pos.Key || depth >= e.Depth || flag == Exact)
            {
                e.Key = _pos.Key; e.Move = bestMove; e.Score = ToTt(best, ply); e.Depth = (sbyte)depth; e.Flag = flag;
            }
            return best;
        }

        int Quiesce(int alpha, int beta, int ply)
        {
            if ((++_nodes & 1023) == 0) CheckLimits();
            if (_abort) return 0;
            int stand = Eval();
            if (ply >= MaxPly - 1) return stand;
            if (stand >= beta) return stand;
            if (stand > alpha) alpha = stand;

            var moves = _moves[ply];
            var scores = _scores[ply];
            int n = _pos.GeneratePseudo(moves, capturesOnly: true);
            ScoreMoves(moves, scores, n, Move.None, ply);
            Side us = _pos.SideToMove;
            int best = stand;
            for (int i = 0; i < n; i++)
            {
                PickNext(moves, scores, i, n);
                var m = moves[i];
                // Delta pruning: even winning this piece cannot lift us to alpha.
                if (!m.IsPromotion && stand + CapturedValue(m) + 200 < alpha) continue;
                _pos.MakeMove(m);
                if (_pos.IsAttacked(_pos.KingSquare(us), us.Other())) { _pos.UnmakeMove(); continue; }
                int score = -Quiesce(-beta, -alpha, ply + 1);
                _pos.UnmakeMove();
                if (_abort) return 0;
                if (score > best) best = score;
                if (score > alpha) alpha = score;
                if (alpha >= beta) break;
            }
            return best;
        }

        int CapturedValue(Move m) =>
            (m.Flags & MoveFlags.EnPassant) != 0 ? 100 : Evaluator.Value[(int)Piece.Type(_pos.Board[m.To])];

        void ScoreMoves(Move[] moves, int[] scores, int n, Move ttMove, int ply)
        {
            for (int i = 0; i < n; i++)
            {
                var m = moves[i];
                int s;
                if (m.Equals(ttMove)) s = 10_000_000;
                else if (m.IsCapture || m.IsPromotion)
                    s = 1_000_000 + CapturedValue(m) * 10 - Evaluator.Value[(int)Piece.Type(_pos.Board[m.From])] / 10
                        + (m.Promotion == PieceType.Queen ? 9000 : 0);
                else if (m.Equals(_killers[ply, 0])) s = 900_000;
                else if (m.Equals(_killers[ply, 1])) s = 800_000;
                else s = Math.Min(_history[m.From, m.To], 700_000);
                scores[i] = s;
            }
        }

        static void PickNext(Move[] moves, int[] scores, int i, int n)
        {
            int bi = i;
            for (int j = i + 1; j < n; j++) if (scores[j] > scores[bi]) bi = j;
            if (bi == i) return;
            (moves[i], moves[bi]) = (moves[bi], moves[i]);
            (scores[i], scores[bi]) = (scores[bi], scores[i]);
        }

        bool HasPieces(Side s)
        {
            for (int sq = 0; sq < 64; sq++)
            {
                int p = _pos.Board[sq];
                if (p != 0 && Piece.Color(p) == s && Piece.Type(p) is not (PieceType.Pawn or PieceType.King)) return true;
            }
            return false;
        }

        static int ToTt(int s, int ply) => s >= Mate - 1000 ? s + ply : s <= -Mate + 1000 ? s - ply : s;
        static int FromTt(int s, int ply) => s >= Mate - 1000 ? s - ply : s <= -Mate + 1000 ? s + ply : s;
    }
}
