using System.Diagnostics;
using System.Threading;
using Chess.AI;
using NUnit.Framework;

namespace Chess.Tests
{
    public class AITests
    {
        static SearchResult Run(string fen, SearchLimits lim, CancellationToken ct = default)
        {
            var g = new Game(fen);
            var p = g.Position.Clone();
            var r = new Searcher(16, seed: 1).Search(p, g.KeyHistory, lim, ct);
            Assert.AreEqual(g.Position.ToFen(), p.ToFen(), "search must restore the position");
            return r;
        }

        [Test]
        public void FindsMateInOne()
        {
            var r = Run("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1", new SearchLimits { MaxDepth = 4, TimeMs = 5000 });
            Assert.AreEqual("d1d8", r.Best.ToUci());
            Assert.IsTrue(r.IsMate);
            Assert.AreEqual(1, r.MateInMoves);
        }

        [Test]
        public void FindsMateInTwo()
        {
            // 1.Rd8+ Rxd8 2.Rxd8#
            var r = Run("r5k1/5ppp/8/8/8/8/3R1PPP/3R2K1 w - - 0 1", new SearchLimits { MaxDepth = 6, TimeMs = 10000 });
            Assert.IsTrue(r.IsMate && r.MateInMoves <= 2, $"got {r.Best} score {r.Score}");
        }

        [Test]
        public void CapturesHangingQueen()
        {
            var r = Run("rnb1kbnr/pppp1ppp/8/4p1q1/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3", new SearchLimits { MaxDepth = 4, TimeMs = 5000 });
            Assert.AreEqual("f3g5", r.Best.ToUci());
        }

        [Test]
        public void EasyAlwaysPlaysLegalMovesAndAvoidsBlunderingMate()
        {
            var lim = new SearchLimits { MaxDepth = 2, TimeMs = 2000, FullEval = false, RandomMarginCp = 40, SloppyChance = 0.5 };
            for (int i = 0; i < 20; i++)
            {
                var r = Run("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1", lim);
                Assert.AreEqual("d1d8", r.Best.ToUci(), "never discards a mate in one");
            }
            var g = new Game();
            var s = new Searcher(16, seed: 3);
            for (int ply = 0; ply < 60 && !g.IsOver; ply++)
            {
                var r = s.Search(g.Position.Clone(), g.KeyHistory, lim);
                Assert.IsTrue(g.TryMakeMove(r.Best), $"illegal move {r.Best} in {g.Position.ToFen()}");
            }
        }

        [Test]
        public void CancellationStopsQuickly()
        {
            using var cts = new CancellationTokenSource(150);
            var sw = Stopwatch.StartNew();
            var r = Run(Position.StartFen, new SearchLimits { MaxDepth = 64, TimeMs = 60000 }, cts.Token);
            Assert.Less(sw.ElapsedMilliseconds, 1500);
            Assert.IsFalse(r.Best.IsNone);
        }

        [Test]
        public void AvoidsStalemateWhenWinning()
        {
            // Qc7?? / Qb6?? stalemate; any search should keep winning.
            var r = Run("k7/8/1K6/8/8/8/8/2Q5 w - - 0 1", new SearchLimits { MaxDepth = 5, TimeMs = 5000 });
            var g = new Game("k7/8/1K6/8/8/8/8/2Q5 w - - 0 1");
            g.TryMakeMove(r.Best);
            Assert.AreNotEqual(ResultReason.Stalemate, g.Reason);
        }
    }
}
