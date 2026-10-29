using System.Collections;
using System.Collections.Generic;
using System.IO;
using Chess;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ChessGame.Tests
{
    /// <summary>Integration tests: the real App (view, camera, input routing, AI) without a human at the keyboard.</summary>
    public class GameplayTests
    {
        App _app;
        string _dir;
        HashSet<GameObject> _before;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "chess-tests-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            Storage.RootOverride = _dir; // never touch the player's real settings or save
            _before = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
            _app = new GameObject("App").AddComponent<App>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _app.Session.Dispose();
            // Only what the App created: the test runner lives in the same scene.
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (!_before.Contains(go)) Object.Destroy(go);
            yield return null;
            Storage.RootOverride = null;
            try { Directory.Delete(_dir, true); } catch { /* ignore */ }
        }

        IEnumerator Idle(float timeout = 5f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            yield return null;
            while ((_app.Board.IsAnimating || _app.Rig.IsTransitioning) && Time.realtimeSinceStartup < end) yield return null;
        }

        void AssertViewMatchesGame()
        {
            var p = _app.Session.Game.Position;
            for (int sq = 0; sq < 64; sq++)
            {
                Assert.AreEqual(p.Board[sq], _app.Board.ViewCodeAt(sq), $"view differs on {Sq.Name(sq)}");
                if (p.Board[sq] != 0)
                    Assert.Less(Vector3.Distance(_app.Board.ViewPositionAt(sq), BoardView.SquareCenter(sq)), 0.01f, $"piece not settled on {Sq.Name(sq)}");
            }
        }

        void Local(string fen = Position.StartFen) =>
            _app.StartNewGame(GameMode.Local, Side.White, Difficulty.Easy, 0, new Game(fen));

        [UnityTest]
        public IEnumerator PlayerMovesThroughInputRouting()
        {
            Local();
            _app.Settings.AutoRotate = false;
            Assert.IsTrue(_app.Input.TryMove(Sq.Parse("e2"), Sq.Parse("e4")));
            yield return Idle();
            Assert.AreEqual(1, _app.Session.Game.History.Count);
            AssertViewMatchesGame();
        }

        [UnityTest]
        public IEnumerator IllegalMovesAreRejected()
        {
            Local();
            Assert.IsFalse(_app.Input.TryMove(Sq.Parse("e2"), Sq.Parse("e5")));
            Assert.IsFalse(_app.Input.TryMove(Sq.Parse("e7"), Sq.Parse("e5")), "not Black's turn");
            yield return Idle();
            Assert.AreEqual(0, _app.Session.Game.History.Count);
            AssertViewMatchesGame();
        }

        /// <summary>Screen point a player would click: the board for empty squares, the piece body otherwise.</summary>
        Vector3 ClickPoint(int sq)
        {
            int p = _app.Session.Game.Position.Board[sq];
            float h = p == 0 ? 0.01f : PieceShapes.Height(Piece.Type(p)) * 0.6f;
            return _app.Rig.Cam.WorldToScreenPoint(BoardView.SquareCenter(sq) + Vector3.up * h);
        }

        [UnityTest]
        public IEnumerator PickingIsCorrectFromEveryAngleAndIn2D()
        {
            // Sparse position so no piece hides another from any angle.
            Local("4k3/8/8/3q4/8/2N5/8/4K3 w - - 0 1");
            yield return Idle();
            int[] squares = { Sq.Parse("e4"), Sq.Parse("d5"), Sq.Parse("c3"), Sq.Parse("e1"), Sq.Parse("e8"), Sq.Parse("h8"), Sq.Parse("a1"), Sq.Parse("g6") };
            foreach (bool twoD in new[] { false, true })
                foreach (float yaw in new[] { 0f, 90f, 180f, 270f, 37f })
                {
                    _app.Rig.SnapTo(yaw, twoD);
                    yield return null;
                    foreach (int sq in squares)
                    {
                        int picked = _app.Board.Pick(_app.Rig.Cam.ScreenPointToRay(ClickPoint(sq)), out _);
                        Assert.AreEqual(Sq.Name(sq), Sq.Name(picked), $"yaw {yaw}, 2D {twoD}");
                    }
                }
        }

        [UnityTest]
        public IEnumerator SwitchingViewsPreservesPosition()
        {
            Local();
            foreach (var m in new[] { "e2e4", "e7e5", "g1f3" }) _app.Session.Game.TryMakeUci(m);
            string fen = _app.Session.Game.Position.ToFen();
            _app.ToggleView();
            yield return new WaitForSecondsRealtime(1f);
            Assert.IsTrue(_app.Rig.Is2D);
            Assert.AreEqual(1f, _app.Rig.Flatness, 1e-3f);
            Assert.IsTrue(_app.Rig.Cam.orthographic);
            Assert.AreEqual(fen, _app.Session.Game.Position.ToFen());
            _app.ToggleView();
            yield return new WaitForSecondsRealtime(1f);
            Assert.IsFalse(_app.Rig.Cam.orthographic);
            Assert.AreEqual(fen, _app.Session.Game.Position.ToFen());
        }

        [UnityTest]
        public IEnumerator AutoRotateTurnsViewWithoutTouchingRules()
        {
            Local();
            _app.Settings.AutoRotate = true;
            Assert.IsTrue(_app.Input.TryMove(Sq.Parse("e2"), Sq.Parse("e4")));
            yield return Idle();
            yield return Idle();
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(_app.Rig.Yaw, 180f)), 1.5f);
            Assert.AreEqual(20, _app.Session.Game.LegalMoves.Count);
            Assert.AreEqual(Sq.Parse("e7"), _app.Board.Pick(_app.Rig.Cam.ScreenPointToRay(ClickPoint(Sq.Parse("e7"))), out _));
            Assert.IsTrue(_app.Input.TryMove(Sq.Parse("e7"), Sq.Parse("e5")));
        }

        [UnityTest]
        public IEnumerator AutoRotateCanBeToggledMidGame()
        {
            Local();
            _app.SetAutoRotate(false);
            _app.Session.TryHumanMove(_app.Session.Game.Position.ParseUci("e2e4"));
            yield return Idle();
            yield return Idle();
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(_app.Rig.Yaw, 0f)), 1.5f, "off: view stays on White's side");

            _app.SetAutoRotate(true);
            yield return Idle();
            yield return Idle();
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(_app.Rig.Yaw, 180f)), 1.5f, "on: turns to the side to move immediately");
            Assert.AreEqual(Side.Black, _app.Session.Game.SideToMove);
            Assert.AreEqual(20, _app.Session.Game.LegalMoves.Count);

            _app.SetAutoRotate(false);
            _app.Session.TryHumanMove(_app.Session.Game.Position.ParseUci("e7e5"));
            yield return Idle();
            yield return Idle();
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(_app.Rig.Yaw, 180f)), 1.5f, "off again: no rotation after Black's move");
        }

        [UnityTest]
        public IEnumerator AiRespondsAndUndoTakesBackBothMoves()
        {
            _app.StartNewGame(GameMode.VsAI, Side.White, Difficulty.Easy, 0);
            Assert.IsTrue(_app.Session.TryHumanMove(_app.Session.Game.Position.ParseUci("e2e4")));
            float end = Time.realtimeSinceStartup + 15f;
            while (_app.Session.Game.History.Count < 2 && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(2, _app.Session.Game.History.Count, "AI did not reply");
            Assert.AreEqual(Side.White, _app.Session.Game.SideToMove);
            _app.Undo();
            yield return Idle();
            Assert.AreEqual(0, _app.Session.Game.History.Count);
            AssertViewMatchesGame();
        }

        [UnityTest]
        public IEnumerator RestartCancelsRunningSearch()
        {
            _app.StartNewGame(GameMode.VsAI, Side.Black, Difficulty.Hard, 0);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.IsTrue(_app.Session.AiThinking);
            _app.Session.Restart();
            Assert.AreEqual(0, _app.Session.Game.History.Count);
            float end = Time.realtimeSinceStartup + 15f;
            while (_app.Session.Game.History.Count < 1 && Time.realtimeSinceStartup < end) yield return null;
            yield return new WaitForSecondsRealtime(2.5f);
            Assert.AreEqual(1, _app.Session.Game.History.Count, "a stale search result was applied");
            Assert.AreEqual(Side.Black, _app.Session.Game.SideToMove);
        }

        [UnityTest]
        public IEnumerator SpecialMovesAnimateToTheRightSquares()
        {
            Local("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            _app.Settings.AutoRotate = false;
            yield return Idle();
            _app.Session.TryHumanMove(new Move(Sq.Parse("e1"), Sq.Parse("g1")));
            yield return Idle();
            AssertViewMatchesGame();
            _app.Undo();
            yield return Idle();
            AssertViewMatchesGame();

            Local("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
            yield return Idle();
            _app.Session.TryHumanMove(new Move(Sq.Parse("e5"), Sq.Parse("d6")));
            yield return Idle();
            AssertViewMatchesGame();
            Assert.AreEqual(1, _app.Board.TrayCount(Side.White));

            Local("2r1k3/1P6/8/8/8/8/8/4K3 w - - 0 1");
            yield return Idle();
            _app.Session.TryHumanMove(new Move(Sq.Parse("b7"), Sq.Parse("c8"), MoveFlags.None, PieceType.Knight));
            yield return Idle();
            AssertViewMatchesGame();
            Assert.AreEqual(Piece.Make(Side.White, PieceType.Knight), _app.Board.ViewCodeAt(Sq.Parse("c8")));
            _app.Undo();
            yield return Idle();
            AssertViewMatchesGame();
            Assert.AreEqual(0, _app.Board.TrayCount(Side.White));
        }

        [UnityTest]
        public IEnumerator PromotionWaitsForChoice()
        {
            Local("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1");
            yield return Idle();
            Assert.IsTrue(_app.Input.TryMove(Sq.Parse("b7"), Sq.Parse("b8")));
            Assert.AreEqual(0, _app.Session.Game.History.Count, "no move before a piece is chosen");
            Assert.IsTrue(_app.Hud.ModalOpen);
            _app.Hud.CancelPromotion();
            yield return Idle();
            Assert.AreEqual(0, _app.Session.Game.History.Count);
            AssertViewMatchesGame();
        }

        [UnityTest]
        public IEnumerator ClockRunsOutAndPausesInMenus()
        {
            _app.StartNewGame(GameMode.Local, Side.White, Difficulty.Easy, 0, null, new TimeControl(2, 0));
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(2000, _app.Session.Clock.RemainingMs(Side.White), "no clock before the first move");
            _app.Session.TryHumanMove(_app.Session.Game.Position.ParseUci("e2e4"));

            _app.OpenPause();
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.AreEqual(2000, _app.Session.Clock.RemainingMs(Side.Black), 50, "clock stops while the pause menu is open");
            _app.Menus.CloseAll();

            float end = Time.realtimeSinceStartup + 5f;
            while (!_app.Session.Game.IsOver && Time.realtimeSinceStartup < end) yield return null;
            Assert.AreEqual(ResultReason.Timeout, _app.Session.Game.Reason);
            Assert.AreEqual(GameResult.WhiteWins, _app.Session.Game.Result);
        }

        [UnityTest]
        public IEnumerator SettingsAndSavePersist()
        {
            var s = Settings.Load();
            s.BoardTheme = 2;
            s.CameraSensitivity = 2.5f;
            s.Save();
            var r = Settings.Load();
            Assert.AreEqual(2, r.BoardTheme);
            Assert.AreEqual(2.5f, r.CameraSensitivity, 1e-4f);

            File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
            Assert.AreEqual(0, Settings.Load().BoardTheme, "corrupt file falls back to defaults");

            Local();
            _app.Session.TryHumanMove(_app.Session.Game.Position.ParseUci("d2d4"));
            yield return null;
            var save = SavedGame.Load();
            Assert.IsNotNull(save);
            CollectionAssert.AreEqual(new[] { "d2d4" }, save.Moves);

            File.WriteAllText(Path.Combine(_dir, "savegame.json"), "{\"Version\":1,\"StartFen\":\"x\",\"Moves\":[\"e2e5\"]}");
            Assert.IsNull(SavedGame.Load(), "invalid save is ignored");
        }
    }
}
