using System;
using System.Collections.Generic;
using System.Linq;
using Chess;
using UnityEngine;
using UnityEngine.UI;

namespace ChessGame
{
    /// <summary>Main menu, new-game setup, settings, help, credits, pause and confirmation dialogs.</summary>
    public sealed class Menus : MonoBehaviour
    {
        App _app;
        GameObject _main, _aiSetup, _localSetup, _settings, _howTo, _credits, _pause, _confirm;
        Button _continue;
        Text _continueInfo, _engineStatus, _difficultyInfo, _confirmTitle, _confirmText;
        Action _confirmYes;
        Action<bool> _setRotateSettings, _setRotateLocal;
        readonly Stack<GameObject> _history = new Stack<GameObject>();
        GameObject _current;

        public bool AnyOpen => _current != null || _confirm.activeSelf;

        public void Build(App app)
        {
            _app = app;
            BuildMain();
            BuildAiSetup();
            BuildLocalSetup();
            BuildSettings();
            _howTo = TextScreen("How to Play", HowToText);
            _credits = TextScreen("Credits & Licenses", CreditsText);
            BuildPause();
            BuildConfirm();
            foreach (var g in new[] { _main, _aiSetup, _localSetup, _settings, _howTo, _credits, _pause, _confirm }) Ui.Show(g, false, 0);
        }

        // ------------------------------------------------------------------ navigation

        void Open(GameObject screen, bool remember = true)
        {
            if (_current != null)
            {
                if (remember) _history.Push(_current);
                Ui.Show(_current, false);
            }
            _current = screen;
            // Auto-rotate can also be toggled in game, so refresh the switches that show it.
            _setRotateSettings?.Invoke(_app.Settings.AutoRotate);
            _setRotateLocal?.Invoke(_app.Settings.AutoRotate);
            Ui.Show(screen, true);
        }

        public void Back()
        {
            if (_confirm.activeSelf) { CloseConfirm(); return; }
            if (_current == null) return;
            if (_history.Count > 0) Open(_history.Pop(), false);
            else if (_current != _main) CloseAll();
        }

        public void CloseAll()
        {
            if (_current != null) Ui.Show(_current, false);
            _current = null;
            _history.Clear();
        }

        public void ShowMain()
        {
            _history.Clear();
            RefreshContinue();
            Open(_main, false);
        }

        public void ShowPause() { _history.Clear(); Open(_pause, false); }
        public void ShowSettings() => Open(_settings);
        public void ShowHowTo() => Open(_howTo);

        public void Confirm(string title, string text, Action yes)
        {
            _confirmTitle.text = title;
            _confirmText.text = text;
            _confirmYes = yes;
            Ui.Show(_confirm, true);
        }

        void CloseConfirm() => Ui.Show(_confirm, false);

        // ------------------------------------------------------------------ screens

        static GameObject Screen(Transform parent, string name, bool dim)
        {
            var rt = Ui.Rect(parent, name).Stretch();
            if (dim)
            {
                var img = rt.gameObject.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0.8f);
            }
            return rt.gameObject;
        }

        static RectTransform Card(Transform parent, float w, float h, string title)
        {
            var card = Ui.Panel(parent, "Card", Ui.PanelBg).rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            Ui.VList(card, 14, 36);
            var t = Ui.Label(card, title, 40, Ui.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.Size(56);
            var line = Ui.Panel(card, "Rule", Ui.Accent, false);
            line.Size(3);
            return card;
        }

        void BuildMain()
        {
            _main = Screen(transform, "Main Menu", false);
            // Soft dark band on the left keeps the menu readable over the orbiting board.
            var band = Ui.Panel(_main.transform, "Band", new Color(0.03f, 0.035f, 0.045f, 0.95f), false).rectTransform;
            band.anchorMin = Vector2.zero; band.anchorMax = new Vector2(0, 1); band.pivot = new Vector2(0, 0.5f);
            band.sizeDelta = new Vector2(620, 0);

            var col = Ui.Rect(_main.transform, "Column").Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(420, 760));
            Ui.VList(col, 14, 0, TextAnchor.MiddleLeft);
            var title = Ui.Label(col, "ROYAL GAMBIT", 64, Ui.Accent, TextAnchor.LowerLeft, FontStyle.Bold);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.Size(84);
            Ui.Label(col, "3D & 2D Chess", 26, Ui.TextDim).Size(40);
            Ui.Rect(col, "Gap").Size(24);

            _continue = Ui.Button(col, "Continue Game", () => _app.ContinueSavedGame(), 24, accent: true);
            _continue.Size(62);
            _continueInfo = Ui.Label(col, "", 18, Ui.TextDim);
            _continueInfo.Size(24);
            Ui.Button(col, "Play vs Computer", () => Open(_aiSetup), 24).Size(62);
            Ui.Button(col, "Local Two-Player", () => Open(_localSetup), 24).Size(62);
            Ui.Button(col, "Settings", () => Open(_settings), 24).Size(62);
            Ui.Button(col, "How to Play", () => Open(_howTo), 24).Size(62);
            Ui.Button(col, "Credits & Licenses", () => Open(_credits), 24).Size(62);
            Ui.Button(col, "Quit", () => _app.Quit(), 24).Size(62);

            var ver = Ui.Label(_main.transform, $"v{Application.version}", 18, Ui.TextDim, TextAnchor.LowerRight);
            ver.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 16), new Vector2(400, 30));
        }

        void RefreshContinue()
        {
            var save = SavedGame.Load();
            _continue.gameObject.SetActive(save != null);
            _continueInfo.gameObject.SetActive(save != null);
            if (save != null)
                _continueInfo.text = $"{(save.Mode == 0 ? "vs Computer" : "Two-player")}, {save.Moves?.Length ?? 0} moves, saved {save.SavedAt}";
        }

        static readonly string[] DifficultyNames = { "Easy", "Hard", "Professional" };

        void BuildAiSetup()
        {
            _aiSetup = Screen(transform, "AI Setup", true);
            var card = Card(_aiSetup.transform, 860, 700, "Play vs Computer");
            var s = _app.Settings;
            Ui.Cycler(card, "Play as", new[] { "White", "Black", "Random" }, s.HumanColor, i => s.HumanColor = i);
            Ui.Cycler(card, "Time control", Settings.TimeControlNames, s.TimeControl, i => s.TimeControl = i);
            Ui.Cycler(card, "Difficulty", DifficultyNames, s.AiDifficulty, i => { s.AiDifficulty = i; RefreshAiInfo(); });
            _difficultyInfo = Ui.Label(card, "", 19, Ui.TextDim);
            _difficultyInfo.Size(78);
            Ui.Cycler(card, "Professional think time", Settings.ProThinkOptions.Select(ms => $"{ms / 1000} s per move").ToArray(),
                Array.IndexOf(Settings.ProThinkOptions, s.ProThinkMs), i => s.ProThinkMs = Settings.ProThinkOptions[i]);
            _engineStatus = Ui.Label(card, "", 19, Ui.TextDim);
            _engineStatus.Size(52);
            Ui.Rect(card, "Spacer").Size(flexH: 1);
            var buttons = Ui.Rect(card, "Buttons");
            Ui.HList(buttons, 16, 0, TextAnchor.MiddleRight);
            buttons.Size(60);
            Ui.Button(buttons, "Back", Back).Size(prefWidth: 180);
            Ui.Button(buttons, "Start Game", () =>
            {
                var side = s.HumanColor == 2 ? (UnityEngine.Random.value < 0.5f ? Side.White : Side.Black) : (Side)s.HumanColor;
                _app.StartNewGame(GameMode.VsAI, side, (Difficulty)s.AiDifficulty, s.ProThinkMs, null, Settings.TimeControls[s.TimeControl]);
            }, 22, accent: true).Size(prefWidth: 240);
            RefreshAiInfo();
        }

        void BuildLocalSetup()
        {
            _localSetup = Screen(transform, "Local Setup", true);
            var card = Card(_localSetup.transform, 860, 420, "Local Two-Player");
            var s = _app.Settings;
            Ui.Cycler(card, "Time control", Settings.TimeControlNames, s.TimeControl, i => s.TimeControl = i);
            _setRotateLocal = Ui.Switch(card, "Auto-rotate board after each move", s.AutoRotate, v => s.AutoRotate = v);
            Ui.Rect(card, "Spacer").Size(flexH: 1);
            var buttons = Ui.Rect(card, "Buttons");
            Ui.HList(buttons, 16, 0, TextAnchor.MiddleRight);
            buttons.Size(60);
            Ui.Button(buttons, "Back", Back).Size(prefWidth: 180);
            Ui.Button(buttons, "Start Game", () =>
                _app.StartNewGame(GameMode.Local, Side.White, Difficulty.Easy, 0, null, Settings.TimeControls[s.TimeControl]), 22, accent: true)
                .Size(prefWidth: 240);
        }

        void RefreshAiInfo()
        {
            int d = _app.Settings.AiDifficulty;
            _difficultyInfo.text = d switch
            {
                0 => "Shallow 2-ply search with material and piece-square evaluation. Replies almost instantly and sometimes picks a reasonable but imperfect move.",
                1 => "Iterative-deepening alpha-beta (about 1.5 s per move) with quiescence search, transposition table, king safety and pawn-structure evaluation.",
                _ => "Stockfish 19 at full strength with the selected thinking time per move.",
            };
            _engineStatus.text = _app.Session.Ai.StockfishFound
                ? "Engine: Stockfish 19 found."
                : "Engine: Stockfish not found. Professional will use the built-in engine at reduced strength.";
            _engineStatus.color = _app.Session.Ai.StockfishFound ? Ui.TextDim : Ui.Danger;
        }

        GameObject TextScreen(string title, string body)
        {
            var screen = Screen(transform, title, true);
            var card = Card(screen.transform, 1000, 820, title);
            var scroll = Ui.Scroll(card, out var content);
            scroll.Size(flexH: 1);
            var t = Ui.Label(content, body, 21, Ui.Text, TextAnchor.UpperLeft);
            t.lineSpacing = 1.15f;
            t.supportRichText = true;
            var fit = t.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var back = Ui.Button(card, "Back", Back);
            back.Size(58);
            return screen;
        }

        void BuildPause()
        {
            _pause = Screen(transform, "Pause", true);
            var card = Card(_pause.transform, 520, 560, "Paused");
            Ui.Button(card, "Resume", CloseAll, 24, accent: true).Size(60);
            Ui.Button(card, "Settings", () => Open(_settings), 24).Size(60);
            Ui.Button(card, "How to Play", () => Open(_howTo), 24).Size(60);
            Ui.Button(card, "Main Menu", () => _app.ReturnToMenu(), 24).Size(60);
            Ui.Button(card, "Quit to Desktop", () => _app.Quit(), 24).Size(60);
        }

        void BuildConfirm()
        {
            _confirm = Screen(transform, "Confirm", true);
            var card = Card(_confirm.transform, 640, 320, "");
            _confirmTitle = card.GetComponentInChildren<Text>();
            _confirmText = Ui.Label(card, "", 22, Ui.TextDim);
            _confirmText.Size(80);
            var buttons = Ui.Rect(card, "Buttons");
            Ui.HList(buttons, 16, 0, TextAnchor.MiddleRight);
            buttons.Size(58);
            Ui.Button(buttons, "Cancel", CloseConfirm).Size(prefWidth: 180);
            Ui.Button(buttons, "Confirm", () => { CloseConfirm(); _confirmYes?.Invoke(); }, 22, accent: true).Size(prefWidth: 180);
        }

        // ------------------------------------------------------------------ settings

        void BuildSettings()
        {
            _settings = Screen(transform, "Settings", true);
            var card = Card(_settings.transform, 1040, 900, "Settings");
            var scroll = Ui.Scroll(card, out var c, 8);
            scroll.Size(flexH: 1);
            var s = _app.Settings;
            void Changed() { _app.ApplySettings(); _app.SaveSettings(); }
            void Section(string name)
            {
                var l = Ui.Label(c, name.ToUpperInvariant(), 18, Ui.Accent, TextAnchor.LowerLeft, FontStyle.Bold);
                l.Size(44);
            }

            Section("Display");
            var res = UnityEngine.Screen.resolutions.Select(r => (r.width, r.height)).Distinct().OrderBy(r => r.width * r.height).ToList();
            var resNames = new List<string> { "Current window size" };
            resNames.AddRange(res.Select(r => $"{r.width} × {r.height}"));
            int resIdx = res.FindIndex(r => r.width == s.ResolutionWidth && r.height == s.ResolutionHeight) + 1;
            Ui.Cycler(c, "Resolution", resNames, resIdx, i =>
            {
                if (i == 0) s.ResolutionWidth = s.ResolutionHeight = 0;
                else { s.ResolutionWidth = res[i - 1].width; s.ResolutionHeight = res[i - 1].height; }
                Changed();
            });
            Ui.Cycler(c, "Display mode", new[] { "Windowed", "Borderless fullscreen", "Exclusive fullscreen" }, s.DisplayMode, i => { s.DisplayMode = i; Changed(); });
            Ui.Switch(c, "VSync", s.VSync, v => { s.VSync = v; Changed(); });
            Ui.Cycler(c, "Frame rate limit (VSync off)", Settings.FrameRateOptions.Select(f => f == 0 ? "Unlimited" : $"{f} FPS").ToArray(),
                Array.IndexOf(Settings.FrameRateOptions, s.FrameRateLimit), i => { s.FrameRateLimit = Settings.FrameRateOptions[i]; Changed(); });
            Ui.Cycler(c, "Graphics quality", new[] { "Low", "Medium", "High" }, s.Quality, i => { s.Quality = i; Changed(); });
            Ui.Switch(c, "Show FPS counter", s.ShowFps, v => { s.ShowFps = v; Changed(); });

            Section("Audio");
            Ui.Slider(c, "Master volume", 0, 1, s.MasterVolume, v => { s.MasterVolume = v; Changed(); }, v => $"{Mathf.RoundToInt(v * 100)}%");
            Ui.Slider(c, "Sound effects", 0, 1, s.SfxVolume, v => { s.SfxVolume = v; Changed(); }, v => $"{Mathf.RoundToInt(v * 100)}%");
            Ui.Switch(c, "Mute", s.Muted, v => { s.Muted = v; Changed(); });

            Section("Camera & animation");
            Ui.Slider(c, "Camera sensitivity", 0.2f, 3f, s.CameraSensitivity, v => { s.CameraSensitivity = v; Changed(); }, v => $"{v:0.0}×");
            Ui.Cycler(c, "Default view", new[] { "3D", "2D top-down" }, s.Default2D ? 1 : 0, i => { s.Default2D = i == 1; Changed(); });
            _setRotateSettings = Ui.Switch(c, "Auto-rotate board (two-player)", s.AutoRotate, v => _app.SetAutoRotate(v));
            Ui.Switch(c, "Piece animations", s.Animations, v => { s.Animations = v; Changed(); });
            Ui.Slider(c, "Animation speed", 0.5f, 2f, s.AnimationSpeed, v => { s.AnimationSpeed = v; Changed(); }, v => $"{v:0.0}×");

            Section("Board & pieces");
            Ui.Cycler(c, "Board theme", Themes.Boards.Select(t => t.Name).ToArray(), s.BoardTheme, i => { s.BoardTheme = i; Changed(); });
            Ui.Cycler(c, "Piece theme", Themes.Pieces.Select(t => t.Name).ToArray(), s.PieceTheme, i => { s.PieceTheme = i; Changed(); });
            Ui.Cycler(c, "Environment", Themes.Environments.Select(t => t.Name).ToArray(), s.Environment, i => { s.Environment = i; Changed(); });

            Section("Gameplay aids");
            Ui.Switch(c, "Show legal moves", s.ShowLegalMoves, v => { s.ShowLegalMoves = v; Changed(); });
            Ui.Switch(c, "Board coordinates", s.ShowCoordinates, v => { s.ShowCoordinates = v; Changed(); });
            Ui.Switch(c, "Move history panel", s.ShowMoveHistory, v => { s.ShowMoveHistory = v; Changed(); });
            Ui.Switch(c, "Engine evaluation", s.ShowEval, v => { s.ShowEval = v; Changed(); });

            Ui.Button(card, "Back", Back).Size(58);
        }

        // ------------------------------------------------------------------ texts

        const string HowToText =
@"<b>Moving pieces</b>
• Click a piece to select it; legal destinations are marked with dots (empty squares) or rings (captures). Click a destination to move.
• Or drag a piece and drop it on a destination. Dropping it anywhere else returns it to its square.
• Click the selected piece again, or an empty square, to deselect.
• When a pawn reaches the last rank you choose the promotion piece (Queen, Rook, Bishop or Knight). Esc cancels the move.

<b>Camera</b>
• Right-drag (or middle-drag), or left-drag on an empty part of the view, to orbit around the board.
• Mouse wheel zooms. The 2D/3D button (or V) switches between the 3D view and the top-down 2D view.
• Q / E rotate the board view by 90°, F flips it 180°, R resets the camera, P returns to the current player's side.
• Rotating the view never changes the position; it only changes where you look from.

<b>Other keys</b>
• T: turn auto-rotate on/off during a two-player game (also a switch in the left panel).
• Esc: pause menu / cancel selection (the clocks stop while a menu is open).   Ctrl+Z: undo.   F11: toggle fullscreen.

<b>Rules</b>
Standard FIDE chess rules are enforced: castling (not out of, through or into check; king and rook unmoved), en passant (only immediately after the double step), promotion, check, checkmate and stalemate.
Draws: stalemate, insufficient material (e.g. king vs king, king and minor piece vs king, bishops all on one colour), threefold repetition and the fifty-move rule are applied automatically. A draw can also be agreed (two-player) or offered to the computer, which accepts only when it judges its own position clearly worse.

<b>Clocks</b>
Choose a time control when starting a game (e.g. 3 + 2 = three minutes each plus two seconds added after every move). A clock starts after White's first move. Running out of time loses, unless the opponent has only a king left, which is a draw.

<b>Modes</b>
• Play vs Computer: choose your colour and Easy, Hard or Professional (Stockfish). Undo takes back your last move and the computer's reply.
• Local Two-Player: two people share the board. With auto-rotate enabled the view turns to the side to move after each move; you can still orbit freely.
• An unfinished game is saved automatically and can be continued from the main menu.";

        const string CreditsText =
@"<b>Royal Gambit</b>
Game design, programming and procedural art: original work created for this project.
Chess pieces, board textures, icons and sound effects are generated procedurally at runtime; no third-party art or audio assets are used.

<b>Stockfish 19</b> (Professional difficulty)
Copyright © The Stockfish developers (see AUTHORS in the Stockfish folder).
Stockfish is free software licensed under the GNU General Public License version 3. It is shipped unmodified as a separate program and communicates with this game only through the UCI text protocol.
The complete corresponding source code is available at https://github.com/official-stockfish/Stockfish/tree/sf_19 and in the release archive from https://github.com/official-stockfish/Stockfish/releases/tag/sf_19. The full licence text is included with the game as Stockfish/COPYING.txt.

<b>Unity</b>
Made with Unity. Unity and the Unity logo are trademarks of Unity Technologies. Uses the Universal Render Pipeline, Input System and uGUI packages under the Unity Companion License.

<b>Built-in engine</b>
Easy and Hard levels use an original alpha-beta search. Its piece-square tables follow Tomasz Michniewski's public ""Simplified Evaluation Function"".";
    }
}
