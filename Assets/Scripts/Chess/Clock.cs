using System;
using System.Collections.Generic;

namespace Chess
{
    public readonly struct TimeControl
    {
        public readonly int BaseSeconds, IncrementSeconds;
        public TimeControl(int baseSeconds, int incrementSeconds) { BaseSeconds = baseSeconds; IncrementSeconds = incrementSeconds; }
        public bool Enabled => BaseSeconds > 0;
        public override string ToString() => Enabled ? $"{BaseSeconds / 60}+{IncrementSeconds}" : "No clock";
    }

    /// <summary>
    /// Fischer clock driven by explicit elapsed time (deterministic, testable). Nothing runs until White's first
    /// move; the mover then gets the increment and the opponent's clock starts. Snapshots per ply make undo exact.
    /// </summary>
    public sealed class ChessClock
    {
        public TimeControl Control { get; }
        readonly long[] _ms = new long[2];
        readonly List<(long w, long b)> _snapshots = new List<(long, long)>(); // [ply] = times before that ply's move

        public ChessClock(TimeControl control)
        {
            Control = control;
            _ms[0] = _ms[1] = control.BaseSeconds * 1000L;
        }

        public long RemainingMs(Side s) => _ms[(int)s];

        /// <summary>Subtract time from the side to move. Returns true if that side has just run out.</summary>
        public bool Tick(Side toMove, int plyCount, long elapsedMs)
        {
            if (!Control.Enabled || plyCount == 0 || _ms[(int)toMove] <= 0) return false;
            _ms[(int)toMove] = Math.Max(0, _ms[(int)toMove] - elapsedMs);
            return _ms[(int)toMove] == 0;
        }

        public void OnMove(Side mover)
        {
            _snapshots.Add((_ms[0], _ms[1]));
            _ms[(int)mover] += Control.IncrementSeconds * 1000L;
        }

        public void OnUndo()
        {
            if (_snapshots.Count == 0) return;
            var (w, b) = _snapshots[_snapshots.Count - 1];
            _snapshots.RemoveAt(_snapshots.Count - 1);
            _ms[0] = w;
            _ms[1] = b;
        }

        /// <summary>Resume a saved game: current times known, earlier snapshots are not, so undo keeps them.</summary>
        public void Restore(long whiteMs, long blackMs, int plyCount)
        {
            _ms[0] = Math.Max(0, whiteMs);
            _ms[1] = Math.Max(0, blackMs);
            _snapshots.Clear();
            for (int i = 0; i < plyCount; i++) _snapshots.Add((_ms[0], _ms[1]));
        }
    }
}
