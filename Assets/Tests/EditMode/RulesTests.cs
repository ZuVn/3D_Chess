using System;
using System.Linq;
using Chess;
using NUnit.Framework;

namespace Chess.Tests
{
    public class PerftTests
    {
        // Reference counts from https://www.chessprogramming.org/Perft_Results
        [TestCase(Position.StartFen, 1, 20)]
        [TestCase(Position.StartFen, 2, 400)]
        [TestCase(Position.StartFen, 3, 8902)]
        [TestCase(Position.StartFen, 4, 197281)]
        [TestCase("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 1, 48)]
        [TestCase("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 2, 2039)]
        [TestCase("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 3, 97862)]
        [TestCase("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 4, 43238)]
        [TestCase("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 5, 674624)]
        [TestCase("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 3, 9467)]
        [TestCase("r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1", 3, 9467)]
        [TestCase("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 3, 62379)]
        [TestCase("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 3, 89890)]
        public void Perft(string fen, int depth, long expected)
        {
            Assert.AreEqual(expected, new Position(fen).Perft(depth));
        }

        [Test]
        public void MakeUnmakeRestoresPositionAndKey()
        {
            var p = new Position("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
            string fen = p.ToFen();
            ulong key = p.Key;
            foreach (var m in p.LegalMoves())
            {
                p.MakeMove(m);
                // Incremental key must equal a key computed from scratch.
                Assert.AreEqual(new Position(p.ToFen()).Key, p.Key, m.ToUci());
                p.UnmakeMove();
                Assert.AreEqual(fen, p.ToFen());
                Assert.AreEqual(key, p.Key);
            }
        }
    }

    public class RuleTests
    {
        static bool Has(Game g, string uci) => g.LegalMoves.Any(m => m.ToUci() == uci);

        [Test]
        public void FenRoundTrip()
        {
            foreach (var fen in new[]
            {
                Position.StartFen,
                "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
                "rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3",
                "8/8/8/8/8/8/6k1/4K3 b - - 37 80",
            })
                Assert.AreEqual(fen, new Position(fen).ToFen());
        }

        [TestCase("")]
        [TestCase("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP w KQkq - 0 1")]          // 7 ranks
        [TestCase("rnbqkbnr/pppppppp/9/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")] // 9 squares
        [TestCase("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")] // bad colour
        [TestCase("rnbq1bnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQ - 0 1")]   // no black king
        [TestCase("4k3/8/8/8/8/8/8/P3K3 w - - 0 1")]                           // pawn on first rank
        [TestCase("4k2R/8/8/8/8/8/8/4K3 w - - 0 1")]                           // side not to move in check
        public void InvalidFenRejected(string fen)
        {
            Assert.Throws<FormatException>(() => new Position(fen));
        }

        [Test]
        public void PinnedPieceCannotMove()
        {
            var g = new Game("4k3/4r3/8/8/8/8/4N3/4K3 w - - 0 1");
            Assert.IsFalse(g.LegalMoves.Any(m => m.From == Sq.Parse("e2")));
        }

        [Test]
        public void DoubleCheckOnlyKingMoves()
        {
            // Rook e8 and bishop b4 both check the king on e1.
            var g = new Game("4r1k1/8/8/8/1b6/8/3N4/4K2R w K - 0 1");
            Assert.IsTrue(g.Position.InCheck);
            Assert.IsTrue(g.LegalMoves.All(m => m.From == Sq.Parse("e1")));
            Assert.IsFalse(Has(g, "e1g1"), "cannot castle out of check");
        }

        [Test]
        public void KingCannotCaptureDefendedPiece()
        {
            var g = new Game("4k3/8/8/8/8/2n5/4q3/4K3 w - - 0 1");
            Assert.IsFalse(Has(g, "e1e2"));
            Assert.AreEqual(GameResult.BlackWins, g.Result);
            Assert.AreEqual(ResultReason.Checkmate, g.Reason);
        }

        [Test]
        public void CastlingRules()
        {
            var g = new Game("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            Assert.IsTrue(Has(g, "e1g1"));
            Assert.IsTrue(Has(g, "e1c1"));

            // f1 attacked: no king-side castle; b1 attacked does not stop queen-side.
            g = new Game("r3k2r/8/8/8/8/8/1q3r2/R3K2R w KQkq - 0 1");
            Assert.IsFalse(Has(g, "e1g1"));

            g = new Game("r3k2r/8/8/8/8/8/8/Rr2K2R w KQkq - 0 1");
            Assert.IsFalse(Has(g, "e1c1"), "b1 occupied");

            g = new Game("1r2k2r/8/8/8/8/8/8/R3K2R w KQk - 0 1");
            Assert.IsTrue(Has(g, "e1c1"), "b1 attacked is fine");

            g = new Game("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            g.TryMakeUci("h1h2"); g.TryMakeUci("a8a7"); g.TryMakeUci("h2h1"); g.TryMakeUci("a7a8");
            Assert.IsFalse(Has(g, "e1g1"), "rook moved");
            Assert.IsTrue(Has(g, "e1c1"));
            Assert.IsFalse(Has(g, "e8c8"));

            g = new Game("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            Assert.IsTrue(g.TryMakeUci("e1g1"));
            Assert.AreEqual("r3k2r/8/8/8/8/8/8/R4RK1 b kq - 1 1", g.Position.ToFen());
            Assert.AreEqual("O-O", g.History[0].San);
            Assert.IsTrue(g.TryMakeUci("e8c8"));
            Assert.AreEqual("2kr3r/8/8/8/8/8/8/R4RK1 w - - 2 2", g.Position.ToFen());
            Assert.AreEqual("O-O-O", g.History[1].San);
        }

        [Test]
        public void CapturingRookRemovesCastlingRight()
        {
            var g = new Game("r3k2r/8/8/8/8/8/6B1/R3K2R w KQkq - 0 1");
            Assert.IsTrue(g.TryMakeUci("g2a8"));
            Assert.IsFalse(Has(g, "e8c8"));
            Assert.IsTrue(Has(g, "e8g8"));
        }

        [Test]
        public void EnPassant()
        {
            var g = new Game("rnbqkbnr/pppppppp/8/4P3/8/8/PPPP1PPP/RNBQKBNR b KQkq - 0 2");
            Assert.IsTrue(g.TryMakeUci("d7d5"));
            Assert.AreEqual(Sq.Parse("d6"), g.Position.EpSquare);
            Assert.IsTrue(g.TryMakeUci("e5d6"));
            Assert.AreEqual(0, g.Position.Board[Sq.Parse("d5")]);
            Assert.AreEqual("exd6", g.History[1].San);
            Assert.AreEqual(Piece.Make(Side.Black, PieceType.Pawn), g.History[1].CapturedPiece);
            g.Undo();
            Assert.AreEqual(Piece.Make(Side.Black, PieceType.Pawn), g.Position.Board[Sq.Parse("d5")]);
            Assert.AreEqual(Piece.Make(Side.White, PieceType.Pawn), g.Position.Board[Sq.Parse("e5")]);
        }

        [Test]
        public void EnPassantThatExposesKingIsIllegal()
        {
            // Capturing exd6 e.p. would remove both pawns from rank 5 and expose the king on a5 to the rook on h5.
            var g = new Game("8/8/8/K2pP2r/8/8/8/4k3 w - d6 0 1");
            Assert.IsFalse(Has(g, "e5d6"));
        }

        [Test]
        public void EnPassantOnlyImmediately()
        {
            var g = new Game("4k3/3p4/8/4P3/8/8/8/4K3 b - - 0 1");
            g.TryMakeUci("d7d5");
            Assert.IsTrue(Has(g, "e5d6"));
            g.TryMakeUci("e1e2"); g.TryMakeUci("e8e7");
            Assert.IsFalse(Has(g, "e5d6"));
        }

        [Test]
        public void PromotionAllPieces()
        {
            var g = new Game("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1");
            foreach (var p in "qrbn") Assert.IsTrue(Has(g, "b7b8" + p));
            Assert.IsFalse(g.TryMakeUci("b7b8"), "promotion piece required");
            Assert.IsTrue(g.TryMakeUci("b7b8n"));
            Assert.AreEqual(Piece.Make(Side.White, PieceType.Knight), g.Position.Board[Sq.Parse("b8")]);
            Assert.AreEqual("b8=N", g.History[0].San);
            g.Undo();
            Assert.AreEqual(Piece.Make(Side.White, PieceType.Pawn), g.Position.Board[Sq.Parse("b7")]);
            Assert.IsTrue(g.TryMakeUci("b7b8q"));
            Assert.AreEqual("b8=Q+", g.History[0].San);
        }

        [Test]
        public void PromotionWithCapture()
        {
            var g = new Game("2r1k3/1P6/8/8/8/8/8/4K3 w - - 0 1");
            Assert.IsTrue(g.TryMakeUci("b7c8r"));
            Assert.AreEqual("bxc8=R+", g.History[0].San);
        }

        [Test]
        public void FoolsMateIsCheckmate()
        {
            var g = new Game();
            foreach (var m in new[] { "f2f3", "e7e5", "g2g4", "d8h4" }) Assert.IsTrue(g.TryMakeUci(m));
            Assert.AreEqual(GameResult.BlackWins, g.Result);
            Assert.AreEqual(ResultReason.Checkmate, g.Reason);
            Assert.AreEqual("Qh4#", g.History[3].San);
            Assert.IsFalse(g.TryMakeUci("a2a3"), "no moves after game over");
            g.Undo();
            Assert.AreEqual(GameResult.Ongoing, g.Result);
        }

        [Test]
        public void Stalemate()
        {
            var g = new Game("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
            Assert.AreEqual(GameResult.Draw, g.Result);
            Assert.AreEqual(ResultReason.Stalemate, g.Reason);
        }

        [TestCase("8/8/8/4k3/8/8/8/4K3 w - - 0 1", true)]
        [TestCase("8/8/8/4k3/8/8/8/4KN2 w - - 0 1", true)]
        [TestCase("8/8/8/4k3/8/8/8/4KB2 w - - 0 1", true)]
        [TestCase("8/8/8/3bk3/8/8/8/4KB2 w - - 0 1", true)]    // both bishops on light squares
        [TestCase("8/8/8/2b1k3/8/8/8/4KB2 w - - 0 1", false)]  // opposite colours
        [TestCase("8/8/8/4k3/8/8/8/3NKN2 w - - 0 1", false)]
        [TestCase("8/8/8/4k3/8/8/5P2/4K3 w - - 0 1", false)]
        public void InsufficientMaterial(string fen, bool expected)
        {
            Assert.AreEqual(expected, Game.IsInsufficientMaterial(new Position(fen)));
            var g = new Game(fen);
            Assert.AreEqual(expected, g.Reason == ResultReason.InsufficientMaterial);
        }

        [Test]
        public void ThreefoldRepetition()
        {
            var g = new Game();
            var cycle = new[] { "g1f3", "g8f6", "f3g1", "f6g8" };
            foreach (var m in cycle) g.TryMakeUci(m);
            Assert.AreEqual(GameResult.Ongoing, g.Result);
            Assert.AreEqual(2, g.RepetitionCount());
            for (int i = 0; i < 3; i++) g.TryMakeUci(cycle[i]);
            Assert.AreEqual(GameResult.Ongoing, g.Result);
            g.TryMakeUci(cycle[3]);
            Assert.AreEqual(GameResult.Draw, g.Result);
            Assert.AreEqual(ResultReason.ThreefoldRepetition, g.Reason);
            g.Undo();
            Assert.AreEqual(GameResult.Ongoing, g.Result, "undo restores repetition tracking");
        }

        [Test]
        public void RepetitionIgnoresUncapturableEpSquare()
        {
            // After 1.e4 the ep square e3 is set but no black pawn can take, so the position
            // must hash equal to the same placement reached without a double push.
            var a = new Position("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1");
            var b = new Position("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1");
            Assert.AreEqual(a.Key, b.Key);
            var c = new Position("rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1");
            var d = new Position("rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1");
            Assert.AreNotEqual(c.Key, d.Key);
        }

        [Test]
        public void FiftyMoveRule()
        {
            var g = new Game("4k3/8/8/8/8/8/8/R3K3 w - - 99 80");
            Assert.AreEqual(GameResult.Ongoing, g.Result);
            g.TryMakeUci("a1a2");
            Assert.AreEqual(ResultReason.FiftyMoveRule, g.Reason);
        }

        [Test]
        public void CheckmateTakesPrecedenceOverFiftyMoves()
        {
            var g = new Game("7k/8/6K1/8/8/8/8/R7 w - - 99 80");
            Assert.IsTrue(g.TryMakeUci("a1a8"));
            Assert.AreEqual(ResultReason.Checkmate, g.Reason);
        }

        [Test]
        public void SanDisambiguation()
        {
            var g = new Game("4k3/8/8/8/8/8/8/R4RK1 w - - 0 1");
            g.TryMakeUci("a1d1");
            Assert.AreEqual("Rad1", g.History[0].San);

            g = new Game("4k3/8/8/R7/8/8/8/R3K3 w - - 0 1");
            g.TryMakeUci("a1a3");
            Assert.AreEqual("R1a3", g.History[0].San);

            g = new Game("k7/8/8/8/8/2Q1Q3/8/2Q1K3 w - - 0 1");
            g.TryMakeUci("c3d2");
            Assert.AreEqual("Qc3d2", g.History[0].San);
        }

        [Test]
        public void UndoRestoresEverything()
        {
            var g = new Game("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
            string start = g.Position.ToFen();
            var rnd = new Random(7);
            int played = 0;
            for (; played < 40 && !g.IsOver; played++)
                g.TryMakeMove(g.LegalMoves[rnd.Next(g.LegalMoves.Count)]);
            while (g.Undo()) { }
            Assert.AreEqual(start, g.Position.ToFen());
            Assert.AreEqual(1, g.KeyHistory.Count);
        }

        [Test]
        public void ReplayFromUciList()
        {
            var g = Game.FromUciMoves(Position.StartFen, new[] { "e2e4", "e7e5", "g1f3" });
            Assert.AreEqual("rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2", g.Position.ToFen());
            Assert.Throws<FormatException>(() => Game.FromUciMoves(Position.StartFen, new[] { "e2e5" }));
        }

        [Test]
        public void PgnExport()
        {
            var g = Game.FromUciMoves(Position.StartFen, new[] { "f2f3", "e7e5", "g2g4", "d8h4" });
            string pgn = g.ToPgn("A", "B", date: new DateTime(2026, 1, 2));
            StringAssert.Contains("[Result \"0-1\"]", pgn);
            StringAssert.Contains("1. f3 e5 2. g4 Qh4# 0-1", pgn);
            StringAssert.Contains("[Date \"2026.01.02\"]", pgn);
        }

        [Test]
        public void ResignAndAgreement()
        {
            var g = new Game();
            g.Resign(Side.White);
            Assert.AreEqual(GameResult.BlackWins, g.Result);
            Assert.IsFalse(g.TryMakeUci("e2e4"));
            g = new Game();
            g.TryMakeUci("e2e4");
            g.AgreeDraw();
            Assert.AreEqual(ResultReason.Agreement, g.Reason);
        }
    }
}
