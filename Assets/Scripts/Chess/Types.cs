using System;

namespace Chess
{
    public enum Side : byte { White = 0, Black = 1 }

    public enum PieceType : byte { None = 0, Pawn = 1, Knight = 2, Bishop = 3, Rook = 4, Queen = 5, King = 6 }

    /// <summary>Piece codes: 0 = empty, (type) for White, (type | 8) for Black.</summary>
    public static class Piece
    {
        public const int Empty = 0;
        public const int BlackBit = 8;

        public static int Make(Side side, PieceType type) => (int)type | (side == Side.Black ? BlackBit : 0);
        public static PieceType Type(int piece) => (PieceType)(piece & 7);
        public static Side Color(int piece) => (piece & BlackBit) != 0 ? Side.Black : Side.White;

        public static char ToFenChar(int piece)
        {
            char c = " pnbrqk"[(int)Type(piece)];
            return Color(piece) == Side.White ? char.ToUpperInvariant(c) : c;
        }

        public static int FromFenChar(char c)
        {
            int t = "pnbrqk".IndexOf(char.ToLowerInvariant(c));
            if (t < 0) return -1;
            return Make(char.IsUpper(c) ? Side.White : Side.Black, (PieceType)(t + 1));
        }

        public static char TypeLetter(PieceType t) => " PNBRQK"[(int)t];
    }

    public static class SideExt
    {
        public static Side Other(this Side s) => s == Side.White ? Side.Black : Side.White;
    }

    /// <summary>Squares are 0..63, a1 = 0, h1 = 7, a8 = 56.</summary>
    public static class Sq
    {
        public const int None = -1;
        public static int File(int sq) => sq & 7;
        public static int Rank(int sq) => sq >> 3;
        public static int Make(int file, int rank) => rank * 8 + file;
        public static string Name(int sq) => sq < 0 ? "-" : $"{(char)('a' + File(sq))}{(char)('1' + Rank(sq))}";

        public static int Parse(string s)
        {
            if (s == null || s.Length != 2) return None;
            int f = s[0] - 'a', r = s[1] - '1';
            return f is >= 0 and < 8 && r is >= 0 and < 8 ? Make(f, r) : None;
        }

        public static bool IsLight(int sq) => ((File(sq) + Rank(sq)) & 1) == 1;
    }

    [Flags]
    public enum MoveFlags : byte
    {
        None = 0,
        Capture = 1,
        DoublePush = 2,
        EnPassant = 4,
        CastleKing = 8,
        CastleQueen = 16,
    }

    public readonly struct Move : IEquatable<Move>
    {
        public readonly byte From;
        public readonly byte To;
        public readonly PieceType Promotion;
        public readonly MoveFlags Flags;

        public Move(int from, int to, MoveFlags flags = MoveFlags.None, PieceType promotion = PieceType.None)
        {
            From = (byte)from;
            To = (byte)to;
            Flags = flags;
            Promotion = promotion;
        }

        public static readonly Move None = default;
        public bool IsNone => From == To;
        public bool IsCapture => (Flags & (MoveFlags.Capture | MoveFlags.EnPassant)) != 0;
        public bool IsCastle => (Flags & (MoveFlags.CastleKing | MoveFlags.CastleQueen)) != 0;
        public bool IsPromotion => Promotion != PieceType.None;

        /// <summary>Same from/to/promotion (flags are derived from the position).</summary>
        public bool SameAs(Move o) => From == o.From && To == o.To && Promotion == o.Promotion;

        public string ToUci()
        {
            if (IsNone) return "0000";
            string s = Sq.Name(From) + Sq.Name(To);
            return IsPromotion ? s + char.ToLowerInvariant(Piece.TypeLetter(Promotion)) : s;
        }

        public bool Equals(Move o) => From == o.From && To == o.To && Promotion == o.Promotion && Flags == o.Flags;
        public override bool Equals(object obj) => obj is Move m && Equals(m);
        public override int GetHashCode() => From | (To << 6) | ((int)Promotion << 12) | ((int)Flags << 16);
        public override string ToString() => ToUci();
    }
}
