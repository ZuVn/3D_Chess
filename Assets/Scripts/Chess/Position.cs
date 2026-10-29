using System;
using System.Collections.Generic;
using System.Text;

namespace Chess
{
    /// <summary>
    /// Authoritative board state with make/unmake, legal move generation and FEN I/O.
    /// Mailbox (int[64]) with precomputed rays; fast enough for perft and the built-in search.
    /// </summary>
    public sealed class Position
    {
        public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        public const int WhiteKingSide = 1, WhiteQueenSide = 2, BlackKingSide = 4, BlackQueenSide = 8;

        public readonly int[] Board = new int[64];
        public Side SideToMove { get; private set; }
        public int Castling { get; private set; }
        public int EpSquare { get; private set; } = Sq.None;
        public int Halfmove { get; private set; }
        public int Fullmove { get; private set; } = 1;
        public ulong Key { get; private set; }

        readonly int[] _king = new int[2];
        ulong _epKeyPart;

        struct Undo
        {
            public Move Move;
            public int Moved, Captured, Castling, Ep, Halfmove, Fullmove;
            public ulong Key, EpKeyPart;
        }

        Undo[] _stack = new Undo[256];
        int _sp;

        public Position() : this(StartFen) { }
        public Position(string fen) { SetFen(fen); }

        public Position Clone() => new Position(ToFen());

        public int KingSquare(Side s) => _king[(int)s];
        public int PieceAt(int sq) => Board[sq];
        public bool InCheck => IsAttacked(_king[(int)SideToMove], SideToMove.Other());

        // ------------------------------------------------------------------ tables

        static readonly int[][] KnightTargets = new int[64][];
        static readonly int[][] KingTargets = new int[64][];
        static readonly int[][][] Rays = new int[64][][]; // [sq][dir] ; dirs 0-3 orthogonal, 4-7 diagonal
        static readonly int[] CastleMask = new int[64];

        static readonly ulong[,] ZPiece = new ulong[16, 64];
        static readonly ulong[] ZCastle = new ulong[16];
        static readonly ulong[] ZEpFile = new ulong[8];
        static readonly ulong ZSide;

        static Position()
        {
            int[] df = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int[] dr = { 1, -1, 0, 0, 1, 1, -1, -1 };
            for (int sq = 0; sq < 64; sq++)
            {
                int f = Sq.File(sq), r = Sq.Rank(sq);
                var kn = new List<int>();
                foreach (var (a, b) in new[] { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) })
                    if (f + a is >= 0 and < 8 && r + b is >= 0 and < 8) kn.Add(Sq.Make(f + a, r + b));
                KnightTargets[sq] = kn.ToArray();

                var kg = new List<int>();
                Rays[sq] = new int[8][];
                for (int d = 0; d < 8; d++)
                {
                    if (f + df[d] is >= 0 and < 8 && r + dr[d] is >= 0 and < 8) kg.Add(Sq.Make(f + df[d], r + dr[d]));
                    var ray = new List<int>();
                    for (int k = 1; ; k++)
                    {
                        int nf = f + df[d] * k, nr = r + dr[d] * k;
                        if (nf is < 0 or >= 8 || nr is < 0 or >= 8) break;
                        ray.Add(Sq.Make(nf, nr));
                    }
                    Rays[sq][d] = ray.ToArray();
                }
                KingTargets[sq] = kg.ToArray();
                CastleMask[sq] = 15;
            }
            CastleMask[0] &= ~WhiteQueenSide;
            CastleMask[7] &= ~WhiteKingSide;
            CastleMask[4] &= ~(WhiteKingSide | WhiteQueenSide);
            CastleMask[56] &= ~BlackQueenSide;
            CastleMask[63] &= ~BlackKingSide;
            CastleMask[60] &= ~(BlackKingSide | BlackQueenSide);

            // Deterministic Zobrist keys (xorshift64*), so hashes are stable between runs.
            ulong s = 0x9E3779B97F4A7C15UL;
            ulong Next() { s ^= s >> 12; s ^= s << 25; s ^= s >> 27; return s * 2685821657736338717UL; }
            for (int p = 0; p < 16; p++)
                for (int q = 0; q < 64; q++) ZPiece[p, q] = Next();
            for (int i = 0; i < 16; i++) ZCastle[i] = Next();
            for (int i = 0; i < 8; i++) ZEpFile[i] = Next();
            ZSide = Next();
        }

        // ------------------------------------------------------------------ FEN

        public void SetFen(string fen)
        {
            if (string.IsNullOrWhiteSpace(fen)) throw new FormatException("Empty FEN.");
            var parts = fen.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 || parts.Length > 6) throw new FormatException("FEN must have 4 to 6 fields.");

            var board = new int[64];
            var ranks = parts[0].Split('/');
            if (ranks.Length != 8) throw new FormatException("FEN board must have 8 ranks.");
            for (int i = 0; i < 8; i++)
            {
                int r = 7 - i, f = 0;
                foreach (char c in ranks[i])
                {
                    if (c is >= '1' and <= '8') { f += c - '0'; continue; }
                    int p = Piece.FromFenChar(c);
                    if (p < 0) throw new FormatException($"Invalid piece '{c}' in FEN.");
                    if (f >= 8) throw new FormatException($"Rank {r + 1} has too many squares.");
                    board[Sq.Make(f++, r)] = p;
                }
                if (f != 8) throw new FormatException($"Rank {r + 1} does not have 8 squares.");
            }

            Side stm = parts[1] switch
            {
                "w" => Side.White,
                "b" => Side.Black,
                _ => throw new FormatException("Active color must be 'w' or 'b'."),
            };

            int castling = 0;
            if (parts[2] != "-")
                foreach (char c in parts[2])
                    castling |= c switch
                    {
                        'K' => WhiteKingSide, 'Q' => WhiteQueenSide, 'k' => BlackKingSide, 'q' => BlackQueenSide,
                        _ => throw new FormatException($"Invalid castling flag '{c}'."),
                    };

            int ep = Sq.None;
            if (parts[3] != "-")
            {
                ep = Sq.Parse(parts[3]);
                if (ep < 0 || Sq.Rank(ep) != (stm == Side.White ? 5 : 2))
                    throw new FormatException("Invalid en passant square.");
            }

            int half = 0, full = 1;
            if (parts.Length > 4 && (!int.TryParse(parts[4], out half) || half < 0)) throw new FormatException("Invalid halfmove clock.");
            if (parts.Length > 5 && (!int.TryParse(parts[5], out full) || full < 1)) throw new FormatException("Invalid fullmove number.");

            int wk = -1, bk = -1;
            for (int sq = 0; sq < 64; sq++)
            {
                int p = board[sq];
                if (p == 0) continue;
                if (Piece.Type(p) == PieceType.King)
                {
                    if (Piece.Color(p) == Side.White) { if (wk >= 0) throw new FormatException("More than one white king."); wk = sq; }
                    else { if (bk >= 0) throw new FormatException("More than one black king."); bk = sq; }
                }
                if (Piece.Type(p) == PieceType.Pawn && (Sq.Rank(sq) == 0 || Sq.Rank(sq) == 7))
                    throw new FormatException("Pawns cannot stand on the first or last rank.");
            }
            if (wk < 0 || bk < 0) throw new FormatException("Each side needs exactly one king.");

            // Drop castling rights that the piece placement cannot support instead of rejecting the FEN.
            if (board[4] != Piece.Make(Side.White, PieceType.King)) castling &= ~(WhiteKingSide | WhiteQueenSide);
            if (board[7] != Piece.Make(Side.White, PieceType.Rook)) castling &= ~WhiteKingSide;
            if (board[0] != Piece.Make(Side.White, PieceType.Rook)) castling &= ~WhiteQueenSide;
            if (board[60] != Piece.Make(Side.Black, PieceType.King)) castling &= ~(BlackKingSide | BlackQueenSide);
            if (board[63] != Piece.Make(Side.Black, PieceType.Rook)) castling &= ~BlackKingSide;
            if (board[56] != Piece.Make(Side.Black, PieceType.Rook)) castling &= ~BlackQueenSide;

            // Ep square must sit behind an enemy pawn that just double-pushed.
            if (ep >= 0)
            {
                int pawnSq = stm == Side.White ? ep - 8 : ep + 8;
                if (board[pawnSq] != Piece.Make(stm.Other(), PieceType.Pawn) || board[ep] != 0)
                    ep = Sq.None;
            }

            Array.Copy(board, Board, 64);
            SideToMove = stm;
            Castling = castling;
            EpSquare = ep;
            Halfmove = half;
            Fullmove = full;
            _king[0] = wk;
            _king[1] = bk;
            _sp = 0;

            if (IsAttacked(_king[(int)stm.Other()], stm))
                throw new FormatException("The side not to move is in check.");

            Key = ComputeKey();
        }

        public string ToFen()
        {
            var sb = new StringBuilder(90);
            for (int r = 7; r >= 0; r--)
            {
                int empty = 0;
                for (int f = 0; f < 8; f++)
                {
                    int p = Board[Sq.Make(f, r)];
                    if (p == 0) { empty++; continue; }
                    if (empty > 0) { sb.Append(empty); empty = 0; }
                    sb.Append(Piece.ToFenChar(p));
                }
                if (empty > 0) sb.Append(empty);
                if (r > 0) sb.Append('/');
            }
            sb.Append(SideToMove == Side.White ? " w " : " b ");
            if (Castling == 0) sb.Append('-');
            else
            {
                if ((Castling & WhiteKingSide) != 0) sb.Append('K');
                if ((Castling & WhiteQueenSide) != 0) sb.Append('Q');
                if ((Castling & BlackKingSide) != 0) sb.Append('k');
                if ((Castling & BlackQueenSide) != 0) sb.Append('q');
            }
            sb.Append(' ').Append(Sq.Name(EpSquare)).Append(' ').Append(Halfmove).Append(' ').Append(Fullmove);
            return sb.ToString();
        }

        ulong ComputeKey()
        {
            ulong k = 0;
            for (int sq = 0; sq < 64; sq++)
                if (Board[sq] != 0) k ^= ZPiece[Board[sq], sq];
            if (SideToMove == Side.Black) k ^= ZSide;
            k ^= ZCastle[Castling];
            _epKeyPart = EpKeyPart();
            return k ^ _epKeyPart;
        }

        // The ep square only distinguishes positions when a capture is actually available,
        // otherwise threefold repetition would miss positions that FIDE treats as identical.
        // ponytail: ignores the rare case where the only ep capture is illegal (pinned pawn).
        ulong EpKeyPart()
        {
            if (EpSquare < 0) return 0;
            int pawn = Piece.Make(SideToMove, PieceType.Pawn);
            int behind = SideToMove == Side.White ? EpSquare - 8 : EpSquare + 8;
            int f = Sq.File(EpSquare);
            bool capturable = (f > 0 && Board[behind - 1] == pawn) || (f < 7 && Board[behind + 1] == pawn);
            return capturable ? ZEpFile[f] : 0;
        }

        // ------------------------------------------------------------------ attacks

        public bool IsAttacked(int sq, Side by)
        {
            int f = Sq.File(sq);
            if (by == Side.White)
            {
                int wp = Piece.Make(Side.White, PieceType.Pawn);
                if (f > 0 && sq - 9 >= 0 && Board[sq - 9] == wp) return true;
                if (f < 7 && sq - 7 >= 0 && Board[sq - 7] == wp) return true;
            }
            else
            {
                int bp = Piece.Make(Side.Black, PieceType.Pawn);
                if (f > 0 && sq + 7 < 64 && Board[sq + 7] == bp) return true;
                if (f < 7 && sq + 9 < 64 && Board[sq + 9] == bp) return true;
            }

            int knight = Piece.Make(by, PieceType.Knight);
            foreach (int t in KnightTargets[sq]) if (Board[t] == knight) return true;
            int king = Piece.Make(by, PieceType.King);
            foreach (int t in KingTargets[sq]) if (Board[t] == king) return true;

            int queen = Piece.Make(by, PieceType.Queen);
            int rook = Piece.Make(by, PieceType.Rook);
            int bishop = Piece.Make(by, PieceType.Bishop);
            var rays = Rays[sq];
            for (int d = 0; d < 8; d++)
            {
                int slider = d < 4 ? rook : bishop;
                foreach (int t in rays[d])
                {
                    int p = Board[t];
                    if (p == 0) continue;
                    if (p == slider || p == queen) return true;
                    break;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ move generation

        /// <summary>Pseudo-legal moves into buffer (must hold 256). Returns count.</summary>
        public int GeneratePseudo(Move[] buf, bool capturesOnly = false)
        {
            int n = 0;
            Side us = SideToMove;
            int black = us == Side.Black ? Piece.BlackBit : 0;
            for (int from = 0; from < 64; from++)
            {
                int p = Board[from];
                if (p == 0 || (p & Piece.BlackBit) != black) continue;
                switch (Piece.Type(p))
                {
                    case PieceType.Pawn: n = GenPawn(from, us, buf, n, capturesOnly); break;
                    case PieceType.Knight: n = GenStep(from, KnightTargets[from], us, buf, n, capturesOnly); break;
                    case PieceType.King:
                        n = GenStep(from, KingTargets[from], us, buf, n, capturesOnly);
                        if (!capturesOnly) n = GenCastle(us, buf, n);
                        break;
                    case PieceType.Bishop: n = GenSlide(from, 4, 8, us, buf, n, capturesOnly); break;
                    case PieceType.Rook: n = GenSlide(from, 0, 4, us, buf, n, capturesOnly); break;
                    case PieceType.Queen: n = GenSlide(from, 0, 8, us, buf, n, capturesOnly); break;
                }
            }
            return n;
        }

        int GenStep(int from, int[] targets, Side us, Move[] buf, int n, bool capturesOnly)
        {
            foreach (int to in targets)
            {
                int q = Board[to];
                if (q == 0) { if (!capturesOnly) buf[n++] = new Move(from, to); }
                else if (Piece.Color(q) != us) buf[n++] = new Move(from, to, MoveFlags.Capture);
            }
            return n;
        }

        int GenSlide(int from, int d0, int d1, Side us, Move[] buf, int n, bool capturesOnly)
        {
            for (int d = d0; d < d1; d++)
                foreach (int to in Rays[from][d])
                {
                    int q = Board[to];
                    if (q == 0) { if (!capturesOnly) buf[n++] = new Move(from, to); continue; }
                    if (Piece.Color(q) != us) buf[n++] = new Move(from, to, MoveFlags.Capture);
                    break;
                }
            return n;
        }

        static int AddPawnMove(Move[] buf, int n, int from, int to, MoveFlags flags, bool promo, bool capturesOnly)
        {
            if (promo)
            {
                // Queen promotions count as "tactical" for quiescence; under-promotions only in full generation.
                buf[n++] = new Move(from, to, flags, PieceType.Queen);
                if (!capturesOnly)
                {
                    buf[n++] = new Move(from, to, flags, PieceType.Rook);
                    buf[n++] = new Move(from, to, flags, PieceType.Bishop);
                    buf[n++] = new Move(from, to, flags, PieceType.Knight);
                }
            }
            else buf[n++] = new Move(from, to, flags);
            return n;
        }

        int GenPawn(int from, Side us, Move[] buf, int n, bool capturesOnly)
        {
            int dir = us == Side.White ? 8 : -8;
            int startRank = us == Side.White ? 1 : 6;
            int lastRank = us == Side.White ? 7 : 0;
            int f = Sq.File(from);
            int one = from + dir;
            bool promo = Sq.Rank(one) == lastRank;

            if (Board[one] == 0)
            {
                if (!capturesOnly || promo) n = AddPawnMove(buf, n, from, one, MoveFlags.None, promo, capturesOnly);
                if (!capturesOnly && Sq.Rank(from) == startRank && Board[one + dir] == 0)
                    buf[n++] = new Move(from, one + dir, MoveFlags.DoublePush);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                if ((side < 0 && f == 0) || (side > 0 && f == 7)) continue;
                int to = one + side;
                int q = Board[to];
                if (q != 0 && Piece.Color(q) != us) n = AddPawnMove(buf, n, from, to, MoveFlags.Capture, promo, capturesOnly);
                else if (to == EpSquare) buf[n++] = new Move(from, to, MoveFlags.EnPassant);
            }
            return n;
        }

        int GenCastle(Side us, Move[] buf, int n)
        {
            int rank = us == Side.White ? 0 : 56;
            int ks = us == Side.White ? WhiteKingSide : BlackKingSide;
            int qs = us == Side.White ? WhiteQueenSide : BlackQueenSide;
            if ((Castling & (ks | qs)) == 0) return n;
            Side them = us.Other();
            int k = rank + 4;
            if (IsAttacked(k, them)) return n;
            if ((Castling & ks) != 0 && Board[rank + 5] == 0 && Board[rank + 6] == 0
                && !IsAttacked(rank + 5, them) && !IsAttacked(rank + 6, them))
                buf[n++] = new Move(k, rank + 6, MoveFlags.CastleKing);
            if ((Castling & qs) != 0 && Board[rank + 3] == 0 && Board[rank + 2] == 0 && Board[rank + 1] == 0
                && !IsAttacked(rank + 3, them) && !IsAttacked(rank + 2, them))
                buf[n++] = new Move(k, rank + 2, MoveFlags.CastleQueen);
            return n;
        }

        [ThreadStatic] static Move[] _scratch;

        /// <summary>Fully legal moves (pseudo-legal filtered by king safety).</summary>
        public List<Move> LegalMoves()
        {
            var list = new List<Move>(48);
            var buf = _scratch ??= new Move[256];
            int n = GeneratePseudo(buf);
            for (int i = 0; i < n; i++)
                if (IsLegal(buf[i])) list.Add(buf[i]);
            return list;
        }

        /// <summary>Is a pseudo-legal move legal (does not leave own king attacked)?</summary>
        public bool IsLegal(Move m)
        {
            Side us = SideToMove;
            MakeMove(m);
            bool ok = !IsAttacked(_king[(int)us], us.Other());
            UnmakeMove();
            return ok;
        }

        public bool HasLegalMove()
        {
            var buf = new Move[256];
            int n = GeneratePseudo(buf);
            for (int i = 0; i < n; i++)
                if (IsLegal(buf[i])) return true;
            return false;
        }

        // ------------------------------------------------------------------ make / unmake

        public void MakeMove(Move m)
        {
            if (_sp == _stack.Length) Array.Resize(ref _stack, _stack.Length * 2);
            int from = m.From, to = m.To;
            int moved = Board[from];
            Side us = SideToMove;
            ref var u = ref _stack[_sp++];
            u.Move = m; u.Moved = moved; u.Castling = Castling; u.Ep = EpSquare;
            u.Halfmove = Halfmove; u.Fullmove = Fullmove; u.Key = Key; u.EpKeyPart = _epKeyPart;

            ulong key = Key ^ _epKeyPart;
            int captured;
            if ((m.Flags & MoveFlags.EnPassant) != 0)
            {
                int capSq = us == Side.White ? to - 8 : to + 8;
                captured = Board[capSq];
                Board[capSq] = 0;
                key ^= ZPiece[captured, capSq];
            }
            else
            {
                captured = Board[to];
                if (captured != 0) key ^= ZPiece[captured, to];
            }
            u.Captured = captured;

            int placed = m.IsPromotion ? Piece.Make(us, m.Promotion) : moved;
            Board[from] = 0;
            Board[to] = placed;
            key ^= ZPiece[moved, from] ^ ZPiece[placed, to];

            if (Piece.Type(moved) == PieceType.King)
            {
                _king[(int)us] = to;
                if (m.IsCastle)
                {
                    int rank = us == Side.White ? 0 : 56;
                    int rf = (m.Flags & MoveFlags.CastleKing) != 0 ? rank + 7 : rank;
                    int rt = (m.Flags & MoveFlags.CastleKing) != 0 ? rank + 5 : rank + 3;
                    int rook = Board[rf];
                    Board[rf] = 0;
                    Board[rt] = rook;
                    key ^= ZPiece[rook, rf] ^ ZPiece[rook, rt];
                }
            }

            int newCastling = Castling & CastleMask[from] & CastleMask[to];
            key ^= ZCastle[Castling] ^ ZCastle[newCastling];
            Castling = newCastling;

            EpSquare = (m.Flags & MoveFlags.DoublePush) != 0 ? (from + to) / 2 : Sq.None;
            Halfmove = Piece.Type(moved) == PieceType.Pawn || captured != 0 ? 0 : Halfmove + 1;
            if (us == Side.Black) Fullmove++;
            SideToMove = us.Other();
            key ^= ZSide;
            _epKeyPart = EpKeyPart();
            Key = key ^ _epKeyPart;
        }

        public void UnmakeMove()
        {
            ref var u = ref _stack[--_sp];
            var m = u.Move;
            SideToMove = SideToMove.Other();
            Side us = SideToMove;
            int from = m.From, to = m.To;

            Board[from] = u.Moved;
            if ((m.Flags & MoveFlags.EnPassant) != 0)
            {
                Board[to] = 0;
                Board[us == Side.White ? to - 8 : to + 8] = u.Captured;
            }
            else Board[to] = u.Captured;

            if (Piece.Type(u.Moved) == PieceType.King)
            {
                _king[(int)us] = from;
                if (m.IsCastle)
                {
                    int rank = us == Side.White ? 0 : 56;
                    int rf = (m.Flags & MoveFlags.CastleKing) != 0 ? rank + 7 : rank;
                    int rt = (m.Flags & MoveFlags.CastleKing) != 0 ? rank + 5 : rank + 3;
                    Board[rf] = Board[rt];
                    Board[rt] = 0;
                }
            }

            Castling = u.Castling; EpSquare = u.Ep; Halfmove = u.Halfmove; Fullmove = u.Fullmove;
            Key = u.Key; _epKeyPart = u.EpKeyPart;
        }

        /// <summary>Pass the turn (search-only, never while in check).</summary>
        public void MakeNullMove()
        {
            if (_sp == _stack.Length) Array.Resize(ref _stack, _stack.Length * 2);
            ref var u = ref _stack[_sp++];
            u.Move = Move.None; u.Ep = EpSquare; u.Halfmove = Halfmove; u.Key = Key; u.EpKeyPart = _epKeyPart;
            u.Castling = Castling; u.Fullmove = Fullmove;
            EpSquare = Sq.None;
            Halfmove++;
            SideToMove = SideToMove.Other();
            _epKeyPart = 0;
            Key = u.Key ^ u.EpKeyPart ^ ZSide;
        }

        public void UnmakeNullMove()
        {
            ref var u = ref _stack[--_sp];
            SideToMove = SideToMove.Other();
            EpSquare = u.Ep; Halfmove = u.Halfmove; Key = u.Key; _epKeyPart = u.EpKeyPart;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Parse a UCI move ("e2e4", "e7e8q") into the matching legal move, or Move.None.</summary>
        public Move ParseUci(string uci)
        {
            if (uci == null || uci.Length < 4 || uci.Length > 5) return Move.None;
            int from = Sq.Parse(uci.Substring(0, 2)), to = Sq.Parse(uci.Substring(2, 2));
            var promo = PieceType.None;
            if (uci.Length == 5)
            {
                int t = "nbrq".IndexOf(uci[4]);
                if (t < 0) return Move.None;
                promo = (PieceType)(t + 2);
            }
            foreach (var m in LegalMoves())
                if (m.From == from && m.To == to && m.Promotion == promo) return m;
            return Move.None;
        }

        public long Perft(int depth)
        {
            if (depth == 0) return 1;
            var buf = new Move[256];
            int n = GeneratePseudo(buf);
            long total = 0;
            Side us = SideToMove;
            for (int i = 0; i < n; i++)
            {
                MakeMove(buf[i]);
                if (!IsAttacked(_king[(int)us], us.Other())) total += depth == 1 ? 1 : Perft(depth - 1);
                UnmakeMove();
            }
            return total;
        }
    }
}
