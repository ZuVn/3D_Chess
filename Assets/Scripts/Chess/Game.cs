using System;
using System.Collections.Generic;
using System.Text;

namespace Chess
{
    public enum GameResult { Ongoing, WhiteWins, BlackWins, Draw }

    public enum ResultReason
    {
        None, Checkmate, Resignation, Stalemate, InsufficientMaterial, ThreefoldRepetition, FiftyMoveRule, Agreement,
        Timeout, TimeoutVsInsufficientMaterial,
    }

    public sealed class MoveRecord
    {
        public Move Move;
        public string San;
        public Side Side;
        public int MovedPiece;
        public int CapturedPiece; // 0 if none
    }

    /// <summary>
    /// A game: position + history + result. The only object presentation code should mutate.
    /// Threefold repetition and the fifty-move rule end the game automatically (as most online
    /// servers do) rather than waiting for a claim.
    /// </summary>
    public sealed class Game
    {
        public string StartFen { get; }
        public Position Position { get; }
        public IReadOnlyList<MoveRecord> History => _history;
        public IReadOnlyList<Move> LegalMoves => _legal;
        public GameResult Result { get; private set; }
        public ResultReason Reason { get; private set; }
        public bool IsOver => Result != GameResult.Ongoing;
        public Side SideToMove => Position.SideToMove;

        /// <summary>Zobrist keys of every position reached, index 0 = start position.</summary>
        public IReadOnlyList<ulong> KeyHistory => _keys;

        readonly List<MoveRecord> _history = new List<MoveRecord>();
        readonly List<ulong> _keys = new List<ulong>();
        List<Move> _legal;

        public Game(string fen = Position.StartFen)
        {
            Position = new Position(fen);
            StartFen = Position.ToFen();
            _keys.Add(Position.Key);
            Refresh();
        }

        public static Game FromUciMoves(string startFen, IEnumerable<string> moves)
        {
            var g = new Game(startFen);
            foreach (var u in moves)
                if (!g.TryMakeUci(u)) throw new FormatException($"Illegal move '{u}' in move list.");
            return g;
        }

        public bool TryMakeUci(string uci)
        {
            var m = Position.ParseUci(uci);
            return !m.IsNone && TryMakeMove(m);
        }

        /// <summary>Validate and play a move given by from/to/promotion. Flags are taken from the legal list.</summary>
        public bool TryMakeMove(Move candidate)
        {
            if (IsOver) return false;
            Move m = Move.None;
            foreach (var l in _legal)
                if (l.SameAs(candidate)) { m = l; break; }
            if (m.IsNone) return false;

            var rec = new MoveRecord
            {
                Move = m,
                San = ToSanNoSuffix(m),
                Side = Position.SideToMove,
                MovedPiece = Position.Board[m.From],
                CapturedPiece = (m.Flags & MoveFlags.EnPassant) != 0
                    ? Piece.Make(Position.SideToMove.Other(), PieceType.Pawn)
                    : Position.Board[m.To],
            };
            Position.MakeMove(m);
            _history.Add(rec);
            _keys.Add(Position.Key);
            Refresh();
            if (Position.InCheck) rec.San += Reason == ResultReason.Checkmate ? "#" : "+";
            return true;
        }

        public bool CanUndo => _history.Count > 0;

        /// <summary>Take back the last move. Also clears a game-over state reached by moves (mate, draws, resign).</summary>
        public bool Undo()
        {
            if (_history.Count == 0) return false;
            Position.UnmakeMove();
            _history.RemoveAt(_history.Count - 1);
            _keys.RemoveAt(_keys.Count - 1);
            Refresh();
            return true;
        }

        public void Resign(Side loser)
        {
            if (IsOver) return;
            Result = loser == Side.White ? GameResult.BlackWins : GameResult.WhiteWins;
            Reason = ResultReason.Resignation;
        }

        /// <summary>
        /// The loser's clock ran out. A draw instead if the opponent has only a king left and so can never mate.
        /// ponytail: only the lone-king case; FIDE's "no possible mate" covers rarer piece sets too.
        /// </summary>
        public void Flag(Side loser)
        {
            if (IsOver) return;
            Side winner = loser.Other();
            bool winnerHasMaterial = false;
            for (int sq = 0; sq < 64 && !winnerHasMaterial; sq++)
            {
                int p = Position.Board[sq];
                winnerHasMaterial = p != 0 && Piece.Color(p) == winner && Piece.Type(p) != PieceType.King;
            }
            if (!winnerHasMaterial) { Result = GameResult.Draw; Reason = ResultReason.TimeoutVsInsufficientMaterial; return; }
            Result = winner == Side.White ? GameResult.WhiteWins : GameResult.BlackWins;
            Reason = ResultReason.Timeout;
        }

        public void AgreeDraw()
        {
            if (IsOver) return;
            Result = GameResult.Draw;
            Reason = ResultReason.Agreement;
        }

        public int RepetitionCount()
        {
            ulong k = Position.Key;
            int c = 0;
            foreach (var x in _keys) if (x == k) c++;
            return c;
        }

        void Refresh()
        {
            _legal = Position.LegalMoves();
            Result = GameResult.Ongoing;
            Reason = ResultReason.None;
            if (_legal.Count == 0)
            {
                if (Position.InCheck)
                {
                    Result = Position.SideToMove == Side.White ? GameResult.BlackWins : GameResult.WhiteWins;
                    Reason = ResultReason.Checkmate;
                }
                else { Result = GameResult.Draw; Reason = ResultReason.Stalemate; }
            }
            else if (IsInsufficientMaterial(Position)) { Result = GameResult.Draw; Reason = ResultReason.InsufficientMaterial; }
            else if (RepetitionCount() >= 3) { Result = GameResult.Draw; Reason = ResultReason.ThreefoldRepetition; }
            else if (Position.Halfmove >= 100) { Result = GameResult.Draw; Reason = ResultReason.FiftyMoveRule; }
        }

        /// <summary>K v K, K+minor v K, and any number of bishops all on one square colour.</summary>
        public static bool IsInsufficientMaterial(Position p)
        {
            int knights = 0, bishops = 0, lightBishops = 0;
            for (int sq = 0; sq < 64; sq++)
            {
                int pc = p.Board[sq];
                switch (Piece.Type(pc))
                {
                    case PieceType.Pawn: case PieceType.Rook: case PieceType.Queen: return false;
                    case PieceType.Knight: knights++; break;
                    case PieceType.Bishop: bishops++; if (Sq.IsLight(sq)) lightBishops++; break;
                }
            }
            if (knights + bishops <= 1) return true;
            return knights == 0 && (lightBishops == 0 || lightBishops == bishops);
        }

        // ------------------------------------------------------------------ SAN / PGN

        string ToSanNoSuffix(Move m)
        {
            if ((m.Flags & MoveFlags.CastleKing) != 0) return "O-O";
            if ((m.Flags & MoveFlags.CastleQueen) != 0) return "O-O-O";
            var p = Position;
            var type = Piece.Type(p.Board[m.From]);
            var sb = new StringBuilder(8);
            if (type == PieceType.Pawn)
            {
                if (m.IsCapture) sb.Append((char)('a' + Sq.File(m.From))).Append('x');
                sb.Append(Sq.Name(m.To));
                if (m.IsPromotion) sb.Append('=').Append(Piece.TypeLetter(m.Promotion));
                return sb.ToString();
            }
            sb.Append(Piece.TypeLetter(type));
            bool ambiguous = false, sameFile = false, sameRank = false;
            foreach (var o in _legal)
            {
                if (o.To != m.To || o.From == m.From || Piece.Type(p.Board[o.From]) != type) continue;
                ambiguous = true;
                if (Sq.File(o.From) == Sq.File(m.From)) sameFile = true;
                if (Sq.Rank(o.From) == Sq.Rank(m.From)) sameRank = true;
            }
            if (ambiguous)
            {
                if (!sameFile) sb.Append((char)('a' + Sq.File(m.From)));
                else if (!sameRank) sb.Append((char)('1' + Sq.Rank(m.From)));
                else sb.Append(Sq.Name(m.From));
            }
            if (m.IsCapture) sb.Append('x');
            sb.Append(Sq.Name(m.To));
            return sb.ToString();
        }

        public string ResultString => Result switch
        {
            GameResult.WhiteWins => "1-0",
            GameResult.BlackWins => "0-1",
            GameResult.Draw => "1/2-1/2",
            _ => "*",
        };

        public string ToPgn(string white = "White", string black = "Black", string eventName = "Casual game", DateTime? date = null)
        {
            var sb = new StringBuilder();
            void Tag(string k, string v) => sb.Append('[').Append(k).Append(" \"").Append(v.Replace("\"", "'")).Append("\"]\n");
            Tag("Event", eventName);
            Tag("Site", "Local");
            Tag("Date", (date ?? DateTime.Now).ToString("yyyy.MM.dd"));
            Tag("Round", "-");
            Tag("White", white);
            Tag("Black", black);
            Tag("Result", ResultString);
            if (StartFen != Position.StartFen) { Tag("SetUp", "1"); Tag("FEN", StartFen); }
            if (Reason != ResultReason.None) Tag("Termination", Reason.ToString());
            sb.Append('\n');

            var start = new Position(StartFen);
            int moveNo = start.Fullmove;
            bool white2move = start.SideToMove == Side.White;
            int col = 0;
            for (int i = 0; i < _history.Count; i++)
            {
                string tok = white2move ? $"{moveNo}. {_history[i].San}" : (i == 0 ? $"{moveNo}... {_history[i].San}" : _history[i].San);
                if (!white2move) moveNo++;
                white2move = !white2move;
                if (col + tok.Length > 79) { sb.Append('\n'); col = 0; }
                else if (col > 0) { sb.Append(' '); col++; }
                sb.Append(tok);
                col += tok.Length;
            }
            sb.Append(col > 0 ? " " : "").Append(ResultString).Append('\n');
            return sb.ToString();
        }
    }
}
