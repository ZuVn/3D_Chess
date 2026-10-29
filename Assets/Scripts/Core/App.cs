using System;
using System.IO;
using System.Linq;
using Chess;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ChessGame
{
    /// <summary>
    /// Composition root: builds the scene at startup and routes between session, view, input and UI.
    /// The only scene object is this component; everything else is created here.
    /// </summary>
    public sealed class App : MonoBehaviour
    {
        public Settings Settings { get; private set; }
        public GameSession Session { get; private set; }
        public BoardView Board { get; private set; }
        public CameraRig Rig { get; private set; }
        public BoardInput Input { get; private set; }
        public Sfx Sfx { get; private set; }
        public Hud Hud => _hud;
        public Menus Menus => _menus;
        public bool InGame { get; private set; }

        Hud _hud;
        Menus _menus;
        Canvas _hudCanvas;
        Light _sun;
        UniversalRenderPipelineAsset _urp;
        UniversalAdditionalCameraData _camData;
        ScriptableRendererFeature _ssao;
        (int w, int h, int mode, bool vsync, int fps) _appliedDisplay = (-1, -1, -1, false, -1);

        bool _awaitingGameOver, _awaitingAutoRotate;
        Vector2Int _screenSize;
        bool _wasInGame;
        int _promoFrom = Sq.None, _promoTo = Sq.None;

        void Awake()
        {
            Settings = Settings.Load();

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            _camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.AddComponent<AudioListener>();
            Rig = camGo.AddComponent<CameraRig>();

            var sunGo = new GameObject("Sun");
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.85f;
            _sun.shadowNormalBias = 0.3f;
            _sun.shadowBias = 0.03f;

            BuildPostProcessing();

            Board = new GameObject("Board").AddComponent<BoardView>();
            Board.Init(_sun);

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            Sfx = gameObject.AddComponent<Sfx>();
            Ui.ClickSound = () => Sfx.Play(Sound.Click);

            Session = new GameSession();
            Session.PositionChanged += OnPositionChanged;
            Session.MovePlayed += OnMovePlayed;
            Session.GameEnded += () => { _awaitingGameOver = true; SavedGame.Delete(); };
            Session.Notice += t => _hud.Toast(t);

            _hudCanvas = Ui.Canvas("HUD", 0);
            _hud = _hudCanvas.gameObject.AddComponent<Hud>();
            _hud.Build(this);
            var menuCanvas = Ui.Canvas("Menus", 10);
            _menus = menuCanvas.gameObject.AddComponent<Menus>();
            _menus.Build(this);

            Input = gameObject.AddComponent<BoardInput>();
            Input.Board = Board;
            Input.Rig = Rig;
            Input.GetGame = () => Session.Game;
            Input.CanMove = () => InGame && Session.IsHumanTurn;
            Input.TryMove = TryHumanMove;
            Input.Illegal = sq => { Board.FlashIllegal(sq); Sfx.Play(Sound.Illegal); };
            Input.SelectionChanged = RefreshHighlights;
            Input.Resync = () => SyncBoard(Move.None);

            ApplySettings();
            SyncBoard(Move.None);
            Rig.SnapTo(-30f, false);
            ShowMainMenu();
            Benchmark.Attach(this);
        }

        void BuildPostProcessing()
        {
            var go = new GameObject("Post Processing");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.25f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.45f);
            vol.sharedProfile = profile;
        }

        // ------------------------------------------------------------------ flow

        public void ShowMainMenu()
        {
            InGame = false;
            Session.CancelAi();
            _hud.SetVisible(false);
            Input.ClearSelection();
            Rig.Set2D(false);
            _menus.ShowMain();
            RefreshHighlights();
        }

        public void StartNewGame(GameMode mode, Side human, Difficulty difficulty, int proMs, Game game = null,
            TimeControl timeControl = default, long whiteMs = -1, long blackMs = -1)
        {
            Settings.Save();
            _menus.CloseAll();
            _hud.SetVisible(true);
            _hud.HideGameOver();
            _awaitingGameOver = false;
            InGame = true;
            Session.Start(mode, human, difficulty, proMs, game, timeControl, whiteMs, blackMs);
            Rig.Set2D(Settings.Default2D);
            Rig.ResetView(PlayerYaw);
        }

        public void ContinueSavedGame()
        {
            var save = SavedGame.Load();
            if (save == null) { _menus.ShowMain(); return; }
            StartNewGame((GameMode)save.Mode, (Side)save.HumanColor, (Difficulty)save.Difficulty, save.ProThinkMs, save.ToGame(),
                new TimeControl(save.ClockBase, save.ClockIncrement), save.WhiteMs, save.BlackMs);
        }

        public void ReturnToMenu()
        {
            SaveGame();
            ShowMainMenu();
        }

        public void Quit()
        {
            SaveGame();
            Settings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnApplicationQuit()
        {
            SaveGame();
            Session?.Dispose();
        }

        void SaveGame()
        {
            if (!InGame || Session.Game.IsOver || Session.Game.History.Count == 0) return;
            new SavedGame
            {
                Mode = (int)Session.Mode,
                HumanColor = (int)Session.HumanSide,
                Difficulty = (int)Session.Difficulty,
                ProThinkMs = Session.ProThinkMs,
                StartFen = Session.Game.StartFen,
                Moves = Session.Game.History.Select(h => h.Move.ToUci()).ToArray(),
                ClockBase = Session.Clock.Control.BaseSeconds,
                ClockIncrement = Session.Clock.Control.IncrementSeconds,
                WhiteMs = Session.Clock.RemainingMs(Side.White),
                BlackMs = Session.Clock.RemainingMs(Side.Black),
            }.Save();
        }

        public void OpenPause() => _menus.ShowPause();
        public void OpenSettings() => _menus.ShowSettings();

        // ------------------------------------------------------------------ game events

        void OnPositionChanged(Move move)
        {
            CancelPromotion();
            Input.ClearSelection();
            SyncBoard(move);
            _hud.Refresh();
            if (move.IsNone) _hud.HideGameOver();
            if (InGame) SaveGame();
            if (Session.Mode == GameMode.Local && Settings.AutoRotate && !move.IsNone && !Session.Game.IsOver) _awaitingAutoRotate = true;
        }

        void SyncBoard(Move animateMove)
        {
            var g = Session.Game;
            Board.Sync(g.Position, g.History, g.History.Count, animateMove, true);
            RefreshHighlights();
        }

        void OnMovePlayed(MoveRecord rec)
        {
            var g = Session.Game;
            if (g.IsOver && g.Reason == ResultReason.Checkmate) return; // game-over sound follows
            if (g.Position.InCheck) SfxPlay(Sound.Check);
            else if (rec.Move.IsPromotion) SfxPlay(Sound.Promote);
            else if (rec.Move.IsCastle) SfxPlay(Sound.Castle);
            else SfxPlay(rec.CapturedPiece != 0 ? Sound.Capture : Sound.Move);
        }

        void SfxPlay(Sound s) => Sfx.Play(s);

        bool TryHumanMove(int from, int to)
        {
            var candidates = Session.Game.LegalMoves.Where(m => m.From == from && m.To == to).ToList();
            if (candidates.Count == 0) return false;
            if (candidates.Count == 1) return Session.TryHumanMove(candidates[0]);
            // Promotion: wait for a choice; the pawn returns to its square meanwhile.
            _promoFrom = from;
            _promoTo = to;
            SyncBoard(Move.None);
            _hud.AskPromotion(Session.Game.SideToMove, t =>
            {
                int f = _promoFrom, tt = _promoTo;
                _promoFrom = _promoTo = Sq.None;
                if (t != PieceType.None) Session.TryHumanMove(new Move(f, tt, MoveFlags.None, t));
            });
            return true;
        }

        void CancelPromotion()
        {
            if (_promoFrom == Sq.None) return;
            _hud.CancelPromotion();
            _promoFrom = _promoTo = Sq.None;
        }

        public void RefreshHighlights()
        {
            var g = Session.Game;
            var last = g.History.Count > 0 ? g.History[g.History.Count - 1].Move : Move.None;
            int check = g.Position.InCheck ? g.Position.KingSquare(g.SideToMove) : Sq.None;
            Board.SetHighlights(InGame ? last : Move.None, Input.Selected, g.LegalMoves, Settings.ShowLegalMoves, InGame ? check : Sq.None,
                Input.Hover, Input.DragTarget);
        }

        // ------------------------------------------------------------------ commands

        float PlayerYaw => (Session.Mode == GameMode.Local ? Session.Game.SideToMove : Session.HumanSide) == Side.White ? 0f : 180f;

        public void ToggleView() => Rig.Toggle2D();

        /// <summary>Two-player auto-rotation, switchable mid-game. Turning it on faces the side to move right away.</summary>
        public void SetAutoRotate(bool on)
        {
            Settings.AutoRotate = on;
            SaveSettings();
            _hud.Refresh();
            if (InGame && Session.Mode == GameMode.Local)
            {
                _hud.Toast(on ? "Auto-rotate on: the board turns to the side to move" : "Auto-rotate off");
                if (on && !Session.Game.IsOver) _awaitingAutoRotate = true; // waits for any move animation to finish
            }
        }
        public void PlayerView() => Rig.LookFrom(PlayerYaw);
        public void ResetCamera() => Rig.ResetView(PlayerYaw);

        public void Undo()
        {
            if (!Session.CanUndo) return;
            Session.Undo();
            if (Session.Mode == GameMode.Local && Settings.AutoRotate) _awaitingAutoRotate = true;
        }

        public void ConfirmRestart() => _menus.Confirm("Restart game?", "The current game will be lost.", () => Session.Restart());

        public void ConfirmResign()
        {
            string who = Session.Mode == GameMode.Local ? (Session.Game.SideToMove == Side.White ? "White" : "Black") : "You";
            _menus.Confirm("Resign?", $"{who} will lose this game.", () => Session.Resign());
        }

        public void OfferDraw()
        {
            if (Session.Mode == GameMode.Local)
            {
                string other = Session.Game.SideToMove == Side.White ? "Black" : "White";
                _menus.Confirm("Draw offer", $"{other}, do you accept a draw?", () => Session.OfferDraw());
            }
            else Session.OfferDraw();
        }

        public void ReplayLastMove()
        {
            var g = Session.Game;
            if (g.History.Count == 0 || Board.IsAnimating) return;
            Input.ClearSelection();
            // Rebuild the previous position by replaying all but the last move (the live game is untouched).
            var before = Game.FromUciMoves(g.StartFen, g.History.Take(g.History.Count - 1).Select(h => h.Move.ToUci()));
            Board.Sync(before.Position, g.History, g.History.Count - 1, Move.None, false);
            Board.Sync(g.Position, g.History, g.History.Count, g.History[g.History.Count - 1].Move, true);
        }

        public void ExportPgn()
        {
            var g = Session.Game;
            string white = Session.Mode == GameMode.VsAI ? (Session.HumanSide == Side.White ? "Player" : $"Computer ({Session.Difficulty})") : "White";
            string black = Session.Mode == GameMode.VsAI ? (Session.HumanSide == Side.Black ? "Player" : $"Computer ({Session.Difficulty})") : "Black";
            string pgn = g.ToPgn(white, black, "Royal Gambit game");
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "games");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"game-{DateTime.Now:yyyyMMdd-HHmmss}.pgn");
                File.WriteAllText(path, pgn);
                GUIUtility.systemCopyBuffer = pgn;
                _hud.Toast($"PGN copied to clipboard and saved to {path}", 5f);
            }
            catch (Exception e) { _hud.Toast($"Could not save PGN: {e.Message}", 4f); }
        }

        // ------------------------------------------------------------------ settings

        public void SaveSettings() => Settings.Save();

        public void ApplySettings()
        {
            var s = Settings;
            var disp = (s.ResolutionWidth, s.ResolutionHeight, s.DisplayMode, s.VSync, s.FrameRateLimit);
            if (disp != _appliedDisplay)
            {
                // Window size/mode only when they actually changed, so other settings never flicker the window.
                bool windowChanged = disp.ResolutionWidth != _appliedDisplay.w || disp.ResolutionHeight != _appliedDisplay.h || disp.DisplayMode != _appliedDisplay.mode;
                if (windowChanged && _appliedDisplay.w >= 0) s.ApplyDisplay();
                else if (_appliedDisplay.w < 0 && (s.ResolutionWidth > 0 || s.DisplayMode != 0)) s.ApplyDisplay();
                QualitySettings.vSyncCount = s.VSync ? 1 : 0;
                Application.targetFrameRate = s.VSync ? -1 : (s.FrameRateLimit == 0 ? -1 : s.FrameRateLimit);
                _appliedDisplay = disp;
            }
            ApplyQuality(s.Quality);
            AudioListener.volume = s.Muted ? 0 : s.MasterVolume;
            Sfx.Volume = s.SfxVolume;
            Rig.Sensitivity = s.CameraSensitivity;
            Board.AnimationsEnabled = s.Animations;
            Board.AnimSpeed = s.AnimationSpeed;
            Board.ShowCoordinates(s.ShowCoordinates);
            Board.ApplyTheme(Themes.Boards[s.BoardTheme], Themes.Pieces[s.PieceTheme], Themes.Environments[s.Environment], Rig.Cam);
            if (InGame) _hud.Refresh();
            RefreshHighlights();
        }

        void ApplyQuality(int q)
        {
            if (_urp == null)
            {
                // Work on a runtime copy so editor play sessions never modify the project asset.
                var src = (QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline)
                    as UniversalRenderPipelineAsset;
                if (src == null) { Debug.LogError("URP asset not configured."); return; }
                _urp = Instantiate(src);
                QualitySettings.renderPipeline = _urp;
                foreach (var data in Resources.FindObjectsOfTypeAll<UniversalRendererData>())
                    foreach (var f in data.rendererFeatures)
                        if (f != null && f.name.Contains("ScreenSpaceAmbientOcclusion")) _ssao = f;
            }
            _urp.msaaSampleCount = q == 0 ? 1 : q == 1 ? 4 : 8;
            _urp.renderScale = q == 0 ? 0.75f : 1f; // Low: weak iGPUs are fill-rate bound; UI stays full resolution
            _urp.shadowDistance = q == 0 ? 22f : 32f;
            _urp.shadowCascadeCount = q == 2 ? 2 : 1;
            _urp.mainLightShadowmapResolution = q == 0 ? 1024 : q == 1 ? 2048 : 4096;
            _urp.supportsHDR = q > 0;
            _ssao?.SetActive(q == 2);
            _sun.shadows = q == 0 ? LightShadows.None : LightShadows.Soft;
            _camData.renderPostProcessing = q > 0;
            _camData.antialiasing = AntialiasingMode.None;
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            HandleKeys();

            if (InGame)
            {
                // The clock stops while a menu or dialog is open (pause, settings, confirmations).
                Session.Tick(!Board.IsAnimating, _menus.AnyOpen, Time.unscaledDeltaTime);
                if (_awaitingAutoRotate && !Board.IsAnimating)
                {
                    _awaitingAutoRotate = false;
                    Rig.LookFrom(PlayerYaw);
                }
                if (_awaitingGameOver && !Board.IsAnimating)
                {
                    _awaitingGameOver = false;
                    SfxPlay(Sound.GameOver);
                    _hud.Refresh();
                    _hud.ShowGameOver();
                }
            }
            else Rig.Idle(4f);

            bool modal = _menus.AnyOpen || _hud.ModalOpen || _promoFrom != Sq.None;
            Input.InputLocked = !InGame || modal;
            float panel = InGame ? (Hud.SidePanelWidth + 24f) * _hudCanvas.scaleFactor / Mathf.Max(1, UnityEngine.Screen.width) : 0f;
            Rig.SidePanelFraction = panel;
            var size = new Vector2Int(UnityEngine.Screen.width, UnityEngine.Screen.height);
            if (size != _screenSize || InGame != _wasInGame) { _screenSize = size; _wasInGame = InGame; Rig.Refit(); }
            Board.SetPresentation(Rig.Flatness, Rig.Yaw);
            _hud.SetViewButton(Rig.Is2D);
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (_promoFrom != Sq.None) CancelPromotion();
                else if (_menus.AnyOpen) _menus.Back();
                else if (InGame && Input.Selected != Sq.None) Input.ClearSelection();
                else if (InGame) OpenPause();
            }
            if (kb.f11Key.wasPressedThisFrame)
            {
                Settings.DisplayMode = Settings.DisplayMode == 0 ? 1 : 0;
                ApplySettings();
                SaveSettings();
            }
            if (!InGame || _menus.AnyOpen || _hud.ModalOpen) return;
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (ctrl && kb.zKey.wasPressedThisFrame) Undo();
            if (kb.vKey.wasPressedThisFrame) ToggleView();
            if (kb.qKey.wasPressedThisFrame) Rig.RotateBy(-90);
            if (kb.eKey.wasPressedThisFrame) Rig.RotateBy(90);
            if (kb.fKey.wasPressedThisFrame) Rig.RotateBy(180);
            if (kb.rKey.wasPressedThisFrame) ResetCamera();
            if (kb.pKey.wasPressedThisFrame) PlayerView();
            if (kb.tKey.wasPressedThisFrame && Session.Mode == GameMode.Local) SetAutoRotate(!Settings.AutoRotate);
        }
    }
}
