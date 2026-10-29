using Chess;
using NUnit.Framework;

namespace Chess.Tests
{
    public class ClockTests
    {
        [Test]
        public void DoesNotRunBeforeFirstMoveThenAddsIncrement()
        {
            var c = new ChessClock(new TimeControl(60, 2));
            Assert.IsFalse(c.Tick(Side.White, 0, 10_000));
            Assert.AreEqual(60_000, c.RemainingMs(Side.White));
            c.OnMove(Side.White);
            Assert.AreEqual(62_000, c.RemainingMs(Side.White));
            c.Tick(Side.Black, 1, 5_000);
            Assert.AreEqual(55_000, c.RemainingMs(Side.Black));
        }

        [Test]
        public void FlagsAtZeroAndUndoRestores()
        {
            var c = new ChessClock(new TimeControl(1, 0));
            c.OnMove(Side.White);
            Assert.IsFalse(c.Tick(Side.Black, 1, 400));
            Assert.IsTrue(c.Tick(Side.Black, 1, 700));
            Assert.AreEqual(0, c.RemainingMs(Side.Black));
            c.OnUndo();
            Assert.AreEqual(1000, c.RemainingMs(Side.Black));
            Assert.AreEqual(1000, c.RemainingMs(Side.White));
        }

        [Test]
        public void DisabledClockNeverFlags()
        {
            var c = new ChessClock(new TimeControl(0, 0));
            c.OnMove(Side.White);
            Assert.IsFalse(c.Tick(Side.Black, 1, 1_000_000));
        }

        [Test]
        public void TimeoutWinsUnlessOpponentHasBareKing()
        {
            var g = new Game();
            g.Flag(Side.White);
            Assert.AreEqual(GameResult.BlackWins, g.Result);
            Assert.AreEqual(ResultReason.Timeout, g.Reason);

            g = new Game("4k3/8/8/8/8/8/8/R3K3 w - - 0 1"); // White flags, Black has only a king
            g.Flag(Side.White);
            Assert.AreEqual(GameResult.Draw, g.Result);
            Assert.AreEqual(ResultReason.TimeoutVsInsufficientMaterial, g.Reason);
        }
    }
}
