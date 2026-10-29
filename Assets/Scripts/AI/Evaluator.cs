namespace Chess.AI
{
    /// <summary>
    /// Tapered static evaluation in centipawns from the side to move's point of view.
    /// Basic mode = material + piece-square tables. Full mode adds pawn structure, king shelter,
    /// bishop pair, rook files and slider mobility.
    /// Tables: Tomasz Michniewski's "Simplified Evaluation Function" (chessprogramming.org).
    /// </summary>
    public static class Evaluator
    {
        public static readonly int[] Value = { 0, 100, 320, 330, 500, 900, 0 };
        static readonly int[] PhaseWeight = { 0, 0, 1, 1, 2, 4, 0 };

        // Written rank 8 first, white's point of view.
        static readonly int[] PawnT =
        {
             0,  0,  0,  0,  0,  0,  0,  0,
            50, 50, 50, 50, 50, 50, 50, 50,
            10, 10, 20, 30, 30, 20, 10, 10,
             5,  5, 10, 25, 25, 10,  5,  5,
             0,  0,  0, 20, 20,  0,  0,  0,
             5, -5,-10,  0,  0,-10, -5,  5,
             5, 10, 10,-20,-20, 10, 10,  5,
             0,  0,  0,  0,  0,  0,  0,  0,
        };
        static readonly int[] KnightT =
        {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50,
        };
        static readonly int[] BishopT =
        {
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -20,-10,-10,-10,-10,-10,-10,-20,
        };
        static readonly int[] RookT =
        {
             0,  0,  0,  0,  0,  0,  0,  0,
             5, 10, 10, 10, 10, 10, 10,  5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
             0,  0,  0,  5,  5,  0,  0,  0,
        };
        static readonly int[] QueenT =
        {
            -20,-10,-10, -5, -5,-10,-10,-20,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -10,  0,  5,  5,  5,  5,  0,-10,
             -5,  0,  5,  5,  5,  5,  0, -5,
              0,  0,  5,  5,  5,  5,  0, -5,
            -10,  5,  5,  5,  5,  5,  0,-10,
            -10,  0,  5,  0,  0,  0,  0,-10,
            -20,-10,-10, -5, -5,-10,-10,-20,
        };
        static readonly int[] KingMgT =
        {
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -10,-20,-20,-20,-20,-20,-20,-10,
             20, 20,  0,  0,  0,  0, 20, 20,
             20, 30, 10,  0,  0, 10, 30, 20,
        };
        static readonly int[] KingEgT =
        {
            -50,-40,-30,-20,-20,-30,-40,-50,
            -30,-20,-10,  0,  0,-10,-20,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 30, 40, 40, 30,-10,-30,
            -30,-10, 20, 30, 30, 20,-10,-30,
            -30,-30,  0,  0,  0,  0,-30,-30,
            -50,-30,-30,-30,-30,-30,-30,-50,
        };
        static readonly int[][] Tables = { null, PawnT, KnightT, BishopT, RookT, QueenT, KingMgT };
        static readonly int[] PassedBonus = { 0, 5, 10, 20, 35, 60, 100, 0 };

        static int TableIndex(int sq, Side s) => s == Side.White ? (7 - Sq.Rank(sq)) * 8 + Sq.File(sq) : sq;

        public static int Evaluate(Position p, bool full)
        {
            var b = p.Board;
            int mg = 0, eg = 0, phase = 0;
            // pawn files per side for structure terms
            System.Span<int> pawnFiles = stackalloc int[16]; // [side * 8 + file]
            int bishops0 = 0, bishops1 = 0;

            for (int sq = 0; sq < 64; sq++)
            {
                int pc = b[sq];
                if (pc == 0) continue;
                var t = Piece.Type(pc);
                var s = Piece.Color(pc);
                int sign = s == Side.White ? 1 : -1;
                int idx = TableIndex(sq, s);
                phase += PhaseWeight[(int)t];
                if (t == PieceType.King)
                {
                    mg += sign * KingMgT[idx];
                    eg += sign * KingEgT[idx];
                    continue;
                }
                int v = Value[(int)t] + Tables[(int)t][idx];
                mg += sign * v;
                eg += sign * (t == PieceType.Pawn ? v + 10 : v);
                if (t == PieceType.Pawn) pawnFiles[(int)s * 8 + Sq.File(sq)]++;
                if (t == PieceType.Bishop) { if (s == Side.White) bishops0++; else bishops1++; }
            }

            if (full)
            {
                if (bishops0 >= 2) { mg += 30; eg += 40; }
                if (bishops1 >= 2) { mg -= 30; eg -= 40; }

                for (int sq = 0; sq < 64; sq++)
                {
                    int pc = b[sq];
                    if (pc == 0) continue;
                    var t = Piece.Type(pc);
                    var s = Piece.Color(pc);
                    int si = (int)s, oi = 1 - si, sign = s == Side.White ? 1 : -1;
                    int f = Sq.File(sq);
                    switch (t)
                    {
                        case PieceType.Pawn:
                        {
                            if (pawnFiles[si * 8 + f] > 1) { mg -= sign * 10; eg -= sign * 20; }
                            bool isolated = (f == 0 || pawnFiles[si * 8 + f - 1] == 0) && (f == 7 || pawnFiles[si * 8 + f + 1] == 0);
                            if (isolated) { mg -= sign * 12; eg -= sign * 15; }
                            if (IsPassed(b, sq, s))
                            {
                                int rel = s == Side.White ? Sq.Rank(sq) : 7 - Sq.Rank(sq);
                                mg += sign * PassedBonus[rel] / 2;
                                eg += sign * PassedBonus[rel];
                            }
                            break;
                        }
                        case PieceType.Rook:
                            if (pawnFiles[si * 8 + f] == 0) { int bonus = pawnFiles[oi * 8 + f] == 0 ? 20 : 10; mg += sign * bonus; eg += sign * bonus / 2; }
                            mg += sign * 2 * Mobility(b, sq, 0, 4); eg += sign * 3 * Mobility(b, sq, 0, 4);
                            break;
                        case PieceType.Bishop:
                            { int m = Mobility(b, sq, 4, 8); mg += sign * 4 * m; eg += sign * 4 * m; }
                            break;
                        case PieceType.Queen:
                            { int m = Mobility(b, sq, 0, 8); mg += sign * m; eg += sign * 2 * m; }
                            break;
                        case PieceType.King:
                            mg += sign * KingShelter(b, sq, s);
                            break;
                    }
                }
            }

            if (phase > 24) phase = 24;
            int score = (mg * phase + eg * (24 - phase)) / 24;
            return p.SideToMove == Side.White ? score : -score;
        }

        static readonly int[] Df = { 0, 0, 1, -1, 1, -1, 1, -1 };
        static readonly int[] Dr = { 1, -1, 0, 0, 1, 1, -1, -1 };

        static int Mobility(int[] b, int sq, int d0, int d1)
        {
            int n = 0, f0 = Sq.File(sq), r0 = Sq.Rank(sq);
            for (int d = d0; d < d1; d++)
                for (int f = f0 + Df[d], r = r0 + Dr[d]; f is >= 0 and < 8 && r is >= 0 and < 8; f += Df[d], r += Dr[d])
                {
                    n++;
                    if (b[Sq.Make(f, r)] != 0) break;
                }
            return n;
        }

        static bool IsPassed(int[] b, int sq, Side s)
        {
            int enemyPawn = Piece.Make(s.Other(), PieceType.Pawn);
            int f = Sq.File(sq), dir = s == Side.White ? 1 : -1;
            for (int r = Sq.Rank(sq) + dir; r is >= 0 and < 8; r += dir)
                for (int df = -1; df <= 1; df++)
                {
                    int ff = f + df;
                    if (ff is >= 0 and < 8 && b[Sq.Make(ff, r)] == enemyPawn) return false;
                }
            return true;
        }

        /// <summary>Pawn shield in front of a castled-looking king (middlegame only).</summary>
        static int KingShelter(int[] b, int sq, Side s)
        {
            int rank = Sq.Rank(sq), rel = s == Side.White ? rank : 7 - rank;
            if (rel > 1) return -20;
            int own = Piece.Make(s, PieceType.Pawn), dir = s == Side.White ? 1 : -1, score = 0;
            for (int df = -1; df <= 1; df++)
            {
                int f = Sq.File(sq) + df;
                if (f is < 0 or >= 8) continue;
                int r1 = rank + dir, r2 = rank + 2 * dir;
                if (r1 is >= 0 and < 8 && b[Sq.Make(f, r1)] == own) score += 12;
                else if (r2 is >= 0 and < 8 && b[Sq.Make(f, r2)] == own) score += 6;
                else score -= 15;
            }
            return score;
        }
    }
}
