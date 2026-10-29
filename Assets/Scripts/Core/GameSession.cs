using System;
using System.Threading;
using System.Threading.Tasks;
using Chess;
using UnityEngine;

namespace ChessGame
{
    public enum GameMode { VsAI, Local }

    /// <summary>
    /// Owns the authoritative <see cref="Game"/> and turn flow (human input, AI turns, undo, resign, draws).
    /// Presentation subscribes to events; nothing here depends on rendering.
    /// </summary>
    public sealed class GameSession : IDisposable
    {
        public Game Game { get; private set; } = new Game();
        public GameMode Mode { get; private set; }
        public Side HumanSide { get; private set; }
        public Difficulty Difficulty { get; private set; }
        public int ProThinkMs { get; private set; } = 2000;
        public ChessClock Clock { get; private set; } = new ChessClock(default);
        public readonly AiPlayer Ai = new AiPlayer();

        /// <summary>Board changed. Arg: last move to animate (Move.None = no animation).</summary>
        public event Action<Move> PositionChanged;
        public event Action<MoveRecord> MovePlayed;
        public event Action GameEnded;
        public event Action<string> Notice;

        Task<AiMove> _aiTask;
        CancellationTokenSource _cts;
        float _aiStarted;
        double _clockCarryMs;
        public AiMove LastAi { get; private set; }
        public const float MinAiThinkSeconds = 0.45f;

        public bool AiThinking => _aiTask != null;
        public float AiElapsed => _aiTask != null ? Time.realtimeSinceStartup - _aiStarted : 0;
        public bool IsAiTurn => Mode == GameMode.VsAI && !Game.IsOver && Game.SideToMove != HumanSide;
        public bool IsHumanTurn => !Game.IsOver && (Mode == GameMode.Local || Game.SideToMove == HumanSide);

        public void Start(GameMode mode, Side human, Difficulty difficulty, int proThinkMs, Game game = null,
            TimeControl timeControl = default, long whiteMs = -1, long blackMs = -1)
        {
            CancelAi();
            Mode = mode;
            HumanSide = human;
            Difficulty = difficulty;
            ProThinkMs = proThinkMs;
            Game = game ?? new Game();
            Clock = new ChessClock(timeControl);
            if (whiteMs >= 0 && blackMs >= 0) Clock.Restore(whiteMs, blackMs, Game.History.Count);
            LastAi = null;
            Ai.NewGame();
            if (mode == GameMode.VsAI && difficulty == Difficulty.Professional) Ai.WarmUpStockfish();
            PositionChanged?.Invoke(Move.None);
            if (Game.IsOver) GameEnded?.Invoke();
        }

        public void Restart() => Start(Mode, HumanSide, Difficulty, ProThinkMs, new Game(Game.StartFen), Clock.Control);

        public bool TryHumanMove(Move m)
        {
            Side mover = Game.SideToMove;
            if (!IsHumanTurn || !Game.TryMakeMove(m)) return false;
            Clock.OnMove(mover);
            AfterMove();
            return true;
        }

        void AfterMove()
        {
            var rec = Game.History[Game.History.Count - 1];
            PositionChanged?.Invoke(rec.Move);
            MovePlayed?.Invoke(rec);
            if (Game.IsOver) GameEnded?.Invoke();
        }

        public bool CanUndo
        {
            get
            {
                if (Game.History.Count == 0) return false;
                if (Mode == GameMode.Local) return true;
                // Need at least one human move to take back.
                foreach (var h in Game.History) if (h.Side == HumanSide) return true;
                return false;
            }
        }

        /// <summary>Local: one ply. Vs AI: back to the human's previous turn (cancelling any search).</summary>
        public void Undo()
        {
            if (!CanUndo) return;
            CancelAi();
            if (Mode == GameMode.Local) { Game.Undo(); Clock.OnUndo(); }
            else
            {
                do { Game.Undo(); Clock.OnUndo(); }
                while (Game.History.Count > 0 && Game.SideToMove != HumanSide);
                if (Game.SideToMove != HumanSide) return; // AI moved first and nothing else to undo
            }
            PositionChanged?.Invoke(Move.None);
        }

        public void Resign()
        {
            if (Game.IsOver) return;
            CancelAi();
            Game.Resign(Mode == GameMode.Local ? Game.SideToMove : HumanSide);
            GameEnded?.Invoke();
        }

        /// <summary>Vs AI: the engine accepts only when it judges itself clearly worse. Local: caller confirms first.</summary>
        public void OfferDraw()
        {
            if (Game.IsOver) return;
            if (Mode == GameMode.Local) { Game.AgreeDraw(); GameEnded?.Invoke(); return; }
            int? evalWhite = LastAi?.EvalWhite;
            int sign = HumanSide == Side.White ? -1 : 1; // AI's point of view
            bool aiLosing = (evalWhite.HasValue && evalWhite.Value * sign <= -150) || (LastAi?.MateWhite is int m && m * sign < 0);
            if (aiLosing && Game.History.Count >= 10)
            {
                CancelAi();
                Game.AgreeDraw();
                GameEnded?.Invoke();
            }
            else Notice?.Invoke("The computer declines the draw offer.");
        }

        /// <summary>Per-move thinking budget when a clock is running (null = use the difficulty's own limit).</summary>
        int? AiBudgetMs()
        {
            if (!Clock.Control.Enabled) return null;
            long left = Clock.RemainingMs(Game.SideToMove);
            long budget = left / 30 + Clock.Control.IncrementSeconds * 800L - (long)(MinAiThinkSeconds * 1000);
            return (int)Math.Max(100, Math.Min(budget, left / 4));
        }

        public void CancelAi()
        {
            if (_cts != null) { _cts.Cancel(); _cts.Dispose(); _cts = null; }
            _aiTask = null; // a cancelled task can still finish later; its result is simply ignored
        }

        /// <summary>Call every frame. Runs the clock, starts AI searches and applies finished ones once the board is idle.</summary>
        public void Tick(bool viewIdle, bool clockPaused, float deltaSeconds)
        {
            long elapsed = 0;
            if (!clockPaused)
            {
                // Keep sub-millisecond remainders so fast frame rates don't make the clock run slow.
                _clockCarryMs += deltaSeconds * 1000.0;
                elapsed = (long)_clockCarryMs;
                _clockCarryMs -= elapsed;
            }
            if (!Game.IsOver && elapsed > 0 && Clock.Tick(Game.SideToMove, Game.History.Count, elapsed))
            {
                CancelAi();
                Game.Flag(Game.SideToMove);
                PositionChanged?.Invoke(Move.None);
                GameEnded?.Invoke();
                return;
            }
            if (_aiTask == null)
            {
                if (IsAiTurn)
                {
                    _cts = new CancellationTokenSource();
                    _aiStarted = Time.realtimeSinceStartup;
                    _aiTask = Ai.ThinkAsync(Game, Difficulty, ProThinkMs, AiBudgetMs(), _cts.Token);
                }
                return;
            }
            if (!_aiTask.IsCompleted || !viewIdle || AiElapsed < MinAiThinkSeconds) return;

            var task = _aiTask;
            _aiTask = null;
            _cts?.Dispose();
            _cts = null;
            if (task.IsCanceled) return;
            if (task.IsFaulted)
            {
                Debug.LogError($"AI search failed: {task.Exception?.GetBaseException()}");
                Notice?.Invoke("The computer could not find a move; retrying.");
                return; // next Tick retries
            }
            var r = task.Result;
            var move = Game.Position.ParseUci(r.Uci);
            if (move.IsNone || !IsAiTurn)
            {
                Debug.LogError($"Discarding AI move '{r.Uci}' that is not legal in {Game.Position.ToFen()}");
                return;
            }
            LastAi = r;
            Side aiSide = Game.SideToMove;
            Game.TryMakeMove(move);
            Clock.OnMove(aiSide);
            AfterMove();
        }

        public void Dispose()
        {
            CancelAi();
            Ai.Dispose();
        }
    }
}
