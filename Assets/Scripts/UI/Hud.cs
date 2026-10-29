using System;
using System.Collections.Generic;
using System.Text;
using Chess;
using UnityEngine;
using UnityEngine.UI;

namespace ChessGame
{
    /// <summary>In-game interface. Rebuilt from the session only when something changes (no per-frame layout work).</summary>
    public sealed class Hud : MonoBehaviour
    {
        App _app;
        GameSession S => _app.Session;

        Text _status, _subStatus, _toast, _fps, _aiTitle, _aiThinking, _aiEval, _viewLabel;
        RectTransform _left, _right, _movesContent;
        ScrollRect _movesScroll;
        Button _undo, _restart, _resign, _draw, _replay, _export;
        readonly PlayerCard[] _cards = new PlayerCard[2];
        GameObject _aiCard, _localCard, _promotion, _gameOver;
        Action<bool> _setRotateSwitch;
        Text _overTitle, _overText;
        readonly List<Text> _moveRows = new List<Text>();
        CanvasGroup _toastGroup;
        float _toastT, _fpsAccum, _thinkRefresh;
        int _fpsFrames;
        Action<PieceType> _promotionPick;

        public const float SidePanelWidth = 340f;
        public bool ModalOpen => _promotion.activeSelf || _gameOver.activeSelf;

        sealed class PlayerCard
        {
            public Image Bg, Turn;
            public Text Name, TurnText, Material, Clock;
            public Image ClockBg;
            public int ClockShown = int.MinValue;
            public Image[] Icons;
        }

        public void Build(App app)
        {
            _app = app;
            BuildTopBar();
            BuildLeft();
            BuildRight();
            BuildToastAndFps();
            BuildPromotion();
            BuildGameOver();
        }

        // ------------------------------------------------------------------ layout

        void BuildTopBar()
        {
            var bar = Ui.Panel(transform, "Top Bar", new Color(0.05f, 0.055f, 0.07f, 0.97f), false).rectTransform;
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(0.5f, 1);
            bar.sizeDelta = new Vector2(0, 76);

            var left = Ui.Rect(bar, "Left").Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(360, 52));
            Ui.HList(left, 10);
            Ui.Button(left, "Menu", () => _app.OpenPause(), 20).Size(prefWidth: 110);
            Ui.Button(left, "Settings", () => _app.OpenSettings(), 20).Size(prefWidth: 130);

            var mid = Ui.Rect(bar, "Status").Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 70));
            _status = Ui.Label(mid, "", 30, Ui.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            _status.rectTransform.Stretch(0, 4, 0, 26);
            _subStatus = Ui.Label(mid, "", 18, Ui.TextDim, TextAnchor.LowerCenter);
            _subStatus.rectTransform.Stretch(0, 40, 0, 4);

            var right = Ui.Rect(bar, "View").Place(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(640, 52));
            Ui.HList(right, 8, 0, TextAnchor.MiddleRight);
            var view = Ui.Button(right, "2D View", () => _app.ToggleView(), 19, accent: true);
            view.Size(prefWidth: 120);
            _viewLabel = Ui.LabelOf(view);
            Ui.Button(right, "Rotate 90°", () => _app.Rig.RotateBy(90), 19).Size(prefWidth: 124);
            Ui.Button(right, "Flip", () => _app.Rig.RotateBy(180), 19).Size(prefWidth: 80);
            Ui.Button(right, "Player View", () => _app.PlayerView(), 19).Size(prefWidth: 140);
            Ui.Button(right, "Reset", () => _app.ResetCamera(), 19).Size(prefWidth: 100);
        }

        void BuildLeft()
        {
            _left = Ui.Rect(transform, "Left Panel");
            _left.anchorMin = new Vector2(0, 0); _left.anchorMax = new Vector2(0, 1); _left.pivot = new Vector2(0, 1);
            _left.offsetMin = new Vector2(16, 16); _left.offsetMax = new Vector2(16 + SidePanelWidth, -92);
            Ui.VList(_left, 12, 0, TextAnchor.UpperLeft);
            _cards[1] = Card(_left, Side.Black);
            _cards[0] = Card(_left, Side.White);

            var ai = Ui.Panel(_left, "AI", Ui.CardBg);
            _aiCard = ai.gameObject;
            Ui.VList(ai, 6, 16, TextAnchor.UpperLeft);
            _aiTitle = Ui.Label(ai.transform, "", 20, Ui.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            _aiTitle.Size(54);
            _aiThinking = Ui.Label(ai.transform, "", 19, Ui.Accent);
            _aiThinking.Size(28);
            _aiEval = Ui.Label(ai.transform, "", 19, Ui.TextDim);
            _aiEval.Size(52);

            // Two-player only: auto-rotation can be switched at any time during the game.
            var local = Ui.Panel(_left, "Local Options", Ui.CardBg);
            _localCard = local.gameObject;
            local.Size(78, flexH: 0); // rows report flexible height; keep the card compact
            Ui.VList(local, 0, 12, TextAnchor.MiddleLeft);
            _setRotateSwitch = Ui.Switch(local.transform, "Auto-rotate board (T)", _app.Settings.AutoRotate, v => _app.SetAutoRotate(v), 96);
        }

        PlayerCard Card(Transform parent, Side side)
        {
            var c = new PlayerCard();
            c.Bg = Ui.Panel(parent, $"{side} Card", Ui.CardBg);
            c.Bg.Size(150);
            var rt = c.Bg.rectTransform;
            c.Turn = Ui.Panel(rt, "Turn Bar", Ui.Accent, false);
            c.Turn.rectTransform.anchorMin = Vector2.zero; c.Turn.rectTransform.anchorMax = new Vector2(0, 1);
            c.Turn.rectTransform.pivot = new Vector2(0, 0.5f);
            c.Turn.rectTransform.sizeDelta = new Vector2(6, 0);

            var swatch = Ui.Panel(rt, "Swatch", side == Side.White ? new Color(0.95f, 0.93f, 0.88f) : new Color(0.12f, 0.12f, 0.13f));
            swatch.sprite = Ui.RoundSmall;
            swatch.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -18), new Vector2(26, 26));
            var outline = swatch.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1, 1, 1, 0.35f);

            c.Name = Ui.Label(rt, "", 22, Ui.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            c.Name.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(58, -14), new Vector2(150, 34));
            c.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
            c.TurnText = Ui.Label(rt, "TO MOVE", 15, Ui.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            c.TurnText.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(58, -42), new Vector2(120, 22));

            c.ClockBg = Ui.Panel(rt, "Clock", new Color(0.06f, 0.065f, 0.08f, 1f));
            c.ClockBg.sprite = Ui.RoundSmall;
            c.ClockBg.rectTransform.Place(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -12), new Vector2(132, 50));
            c.Clock = Ui.Label(c.ClockBg.transform, "", 32, Ui.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            c.Clock.rectTransform.Stretch();

            var icons = Ui.Rect(rt, "Captured").Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 12), new Vector2(300, 66));
            c.Icons = new Image[15];
            for (int i = 0; i < 15; i++)
            {
                var img = Ui.Rect(icons, "Icon").gameObject.AddComponent<Image>();
                img.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2((i % 8) * 30 - 6, i < 8 ? 26 : -8), new Vector2(48, 48));
                img.preserveAspect = true;
                img.raycastTarget = false;
                img.gameObject.SetActive(false);
                c.Icons[i] = img;
            }
            c.Material = Ui.Label(rt, "", 18, Ui.TextDim, TextAnchor.MiddleRight);
            c.Material.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 12), new Vector2(60, 30));
            return c;
        }

        void BuildRight()
        {
            _right = Ui.Rect(transform, "Right Panel");
            _right.anchorMin = new Vector2(1, 0); _right.anchorMax = new Vector2(1, 1); _right.pivot = new Vector2(1, 1);
            _right.offsetMin = new Vector2(-16 - SidePanelWidth, 16); _right.offsetMax = new Vector2(-16, -92);
            var bg = _right.gameObject.AddComponent<Image>();
            bg.sprite = Ui.Round; bg.type = Image.Type.Sliced; bg.color = Ui.CardBg;
            Ui.VList(_right, 10, 16);

            Ui.Label(_right, "MOVES", 18, Ui.Accent, TextAnchor.MiddleLeft, FontStyle.Bold).Size(28);
            _movesScroll = Ui.Scroll(_right, out _movesContent, 2);
            _movesScroll.Size(flexH: 1);

            var grid = Ui.Rect(_right, "Actions");
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(148, 50);
            g.spacing = new Vector2(12, 10);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
            grid.Size(50 * 3 + 20);
            _undo = Ui.Button(grid, "Undo", () => _app.Undo(), 20);
            _restart = Ui.Button(grid, "Restart", () => _app.ConfirmRestart(), 20);
            _resign = Ui.Button(grid, "Resign", () => _app.ConfirmResign(), 20);
            _draw = Ui.Button(grid, "Offer Draw", () => _app.OfferDraw(), 20);
            _replay = Ui.Button(grid, "Replay Last", () => _app.ReplayLastMove(), 20);
            _export = Ui.Button(grid, "Export PGN", () => _app.ExportPgn(), 20);
        }

        void BuildToastAndFps()
        {
            var t = Ui.Panel(transform, "Toast", new Color(0.05f, 0.05f, 0.06f, 0.97f));
            t.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -96), new Vector2(760, 54));
            t.raycastTarget = false;
            _toastGroup = t.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.alpha = 0;
            _toastGroup.blocksRaycasts = false;
            _toast = Ui.Label(t.transform, "", 22, Ui.Text, TextAnchor.MiddleCenter);
            _toast.rectTransform.Stretch(12, 0, 12, 0);

            _fps = Ui.Label(transform, "", 18, new Color(0.6f, 1f, 0.6f), TextAnchor.LowerRight);
            _fps.rectTransform.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16 - SidePanelWidth - 16, 8), new Vector2(300, 28));
        }

        void BuildPromotion()
        {
            _promotion = Modal("Promotion", 620, 300, "Promote pawn to", out var card);
            var row = Ui.Rect(card, "Choices");
            Ui.HList(row, 14, 0, TextAnchor.MiddleCenter);
            row.Size(150);
            foreach (var t in new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight })
            {
                var b = Ui.Button(row, "", () => { Ui.Show(_promotion, false); _promotionPick?.Invoke(t); }, 18);
                b.Size(prefWidth: 124);
                var icon = Ui.Rect(b.transform, "Icon").gameObject.AddComponent<Image>();
                icon.name = "Icon";
                icon.rectTransform.Stretch(14, 8, 14, 34);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                var label = Ui.LabelOf(b);
                label.text = t.ToString();
                label.alignment = TextAnchor.LowerCenter;
                label.rectTransform.Stretch(4, 4, 4, 8);
            }
            Ui.Button(card, "Cancel (Esc)", () => { Ui.Show(_promotion, false); _promotionPick?.Invoke(PieceType.None); }).Size(50);
        }

        void BuildGameOver()
        {
            _gameOver = Modal("Game Over", 640, 400, "", out var card);
            _overTitle = card.GetComponentInChildren<Text>();
            _overText = Ui.Label(card, "", 24, Ui.TextDim);
            _overText.Size(70);
            var row = Ui.Rect(card, "Buttons");
            var g = row.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(276, 54);
            g.spacing = new Vector2(16, 12);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
            row.Size(120);
            Ui.Button(row, "New Game", () => { Ui.Show(_gameOver, false); _app.Session.Restart(); }, 21, accent: true);
            Ui.Button(row, "Review Board", () => Ui.Show(_gameOver, false), 21);
            Ui.Button(row, "Export PGN", () => _app.ExportPgn(), 21);
            Ui.Button(row, "Main Menu", () => { Ui.Show(_gameOver, false); _app.ReturnToMenu(); }, 21);
        }

        GameObject Modal(string name, float w, float h, string title, out RectTransform card)
        {
            var root = Ui.Rect(transform, name).Stretch();
            root.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);
            card = Ui.Panel(root, "Card", Ui.PanelBg).rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            Ui.VList(card, 14, 30);
            Ui.Label(card, title, 34, Ui.Text, TextAnchor.MiddleLeft, FontStyle.Bold).Size(48);
            Ui.Show(root.gameObject, false, 0);
            return root.gameObject;
        }

        // ------------------------------------------------------------------ behaviour

        public void SetVisible(bool on)
        {
            gameObject.SetActive(on);
            if (!on) { Ui.Show(_promotion, false, 0); Ui.Show(_gameOver, false, 0); }
        }

        public void AskPromotion(Side side, Action<PieceType> pick)
        {
            foreach (var img in _promotion.GetComponentsInChildren<Image>(true))
            {
                if (img.name != "Icon") continue;
                var label = img.transform.parent.GetComponentInChildren<Text>().text;
                var t = (PieceType)Enum.Parse(typeof(PieceType), label);
                img.sprite = PieceIcons.Get(Piece.Make(side, t));
            }
            _promotionPick = pick;
            Ui.Show(_promotion, true);
        }

        public void CancelPromotion()
        {
            if (!_promotion.activeSelf) return;
            Ui.Show(_promotion, false);
            _promotionPick?.Invoke(PieceType.None);
        }

        public void ShowGameOver()
        {
            var g = S.Game;
            string winner = g.Result == GameResult.WhiteWins ? "White" : "Black";
            string loser = g.Result == GameResult.WhiteWins ? "Black" : "White";
            bool vsAi = S.Mode == GameMode.VsAI;
            bool humanWon = vsAi && (g.Result == GameResult.WhiteWins) == (S.HumanSide == Side.White);
            string outcome = g.Result == GameResult.Draw ? "Draw" : vsAi ? (humanWon ? "You win!" : "The computer wins") : $"{winner} wins";
            (_overTitle.text, _overText.text) = g.Reason switch
            {
                ResultReason.Checkmate => ("Checkmate", outcome),
                ResultReason.Resignation => ($"{loser} resigned", outcome),
                ResultReason.Timeout => ($"{loser} ran out of time", outcome),
                ResultReason.TimeoutVsInsufficientMaterial => ("Draw", "Time ran out, but the opponent has only a king left and cannot win."),
                ResultReason.Stalemate => ("Stalemate", "Draw: the side to move has no legal move but is not in check."),
                ResultReason.InsufficientMaterial => ("Draw", "Insufficient material: neither side can checkmate."),
                ResultReason.ThreefoldRepetition => ("Draw", "The same position occurred three times."),
                ResultReason.FiftyMoveRule => ("Draw", "Fifty moves by each side without a capture or pawn move."),
                ResultReason.Agreement => ("Draw", "Draw agreed."),
                _ => ("Game over", outcome),
            };
            Ui.Show(_gameOver, true, 0.3f);
        }

        public void HideGameOver() => Ui.Show(_gameOver, false, 0);

        public void Toast(string text, float seconds = 2.6f)
        {
            _toast.text = text;
            _toastT = seconds;
        }

        static string SideName(Side s) => s == Side.White ? "White" : "Black";

        /// <summary>Refresh everything derived from the game state.</summary>
        public void Refresh()
        {
            var g = S.Game;
            var stm = g.SideToMove;
            bool vsAi = S.Mode == GameMode.VsAI;

            if (g.IsOver)
            {
                _status.text = g.Result == GameResult.Draw ? "Draw" : $"{(g.Result == GameResult.WhiteWins ? "White" : "Black")} wins";
                _subStatus.text = g.Reason switch
                {
                    ResultReason.Checkmate => "by checkmate", ResultReason.Resignation => "by resignation",
                    ResultReason.Timeout => "on time", ResultReason.TimeoutVsInsufficientMaterial => "timeout vs insufficient material",
                    ResultReason.Stalemate => "stalemate", ResultReason.InsufficientMaterial => "insufficient material",
                    ResultReason.ThreefoldRepetition => "threefold repetition", ResultReason.FiftyMoveRule => "fifty-move rule",
                    ResultReason.Agreement => "by agreement", _ => "",
                };
                _status.color = Ui.Accent;
            }
            else
            {
                bool check = g.Position.InCheck;
                string who = vsAi ? (stm == S.HumanSide ? "Your move" : "Computer's move") : $"{SideName(stm)} to move";
                _status.text = check ? $"Check! {who}" : who;
                _status.color = check ? Ui.Danger : Ui.Text;
                string tc = S.Clock.Control.Enabled ? $" · {S.Clock.Control}" : "";
                _subStatus.text = (vsAi ? $"You play {SideName(S.HumanSide)} · {S.Difficulty}" : "Local two-player") + tc;
            }

            for (int s = 0; s < 2; s++)
            {
                var side = (Side)s;
                var c = _cards[s];
                c.Name.text = vsAi ? (side == S.HumanSide ? $"You ({SideName(side)})" : $"Computer ({SideName(side)})") : SideName(side);
                bool active = !g.IsOver && stm == side;
                c.Turn.enabled = active;
                c.TurnText.enabled = active;
                c.Bg.color = active ? Color.Lerp(Ui.CardBg, Ui.AccentDark, 0.25f) : Ui.CardBg;
            }

            // Captured pieces (pieces taken BY each side) and material balance.
            var taken = new[] { new List<int>(), new List<int>() };
            foreach (var h in g.History) if (h.CapturedPiece != 0) taken[(int)h.Side].Add(h.CapturedPiece);
            int material = 0;
            for (int sq = 0; sq < 64; sq++)
            {
                int p = g.Position.Board[sq];
                if (p == 0) continue;
                int v = Chess.AI.Evaluator.Value[(int)Piece.Type(p)] / 100;
                material += Piece.Color(p) == Side.White ? v : -v;
            }
            for (int s = 0; s < 2; s++)
            {
                taken[s].Sort((a, b) => Piece.Type(b).CompareTo(Piece.Type(a)));
                var c = _cards[s];
                for (int i = 0; i < c.Icons.Length; i++)
                {
                    bool on = i < taken[s].Count;
                    c.Icons[i].gameObject.SetActive(on);
                    if (on) c.Icons[i].sprite = PieceIcons.Get(taken[s][i]);
                }
                int adv = s == 0 ? material : -material;
                c.Material.text = adv > 0 ? $"+{adv}" : "";
            }

            _aiCard.SetActive(vsAi);
            _localCard.SetActive(!vsAi);
            _setRotateSwitch(_app.Settings.AutoRotate);
            if (vsAi)
            {
                string engine = S.Difficulty == Difficulty.Professional ? S.Ai.ProfessionalLabel : "Built-in engine";
                _aiTitle.text = $"Computer: {S.Difficulty}\n<size=16><color=#A0A8B4>{engine}</color></size>";
                _aiTitle.supportRichText = true;
                RefreshEval();
            }

            RefreshMoves();
            RefreshButtons();
        }

        void RefreshEval()
        {
            var r = S.LastAi;
            if (!_app.Settings.ShowEval || r == null) { _aiEval.text = r == null ? "" : $"Last move: {r.Ms / 1000f:0.0} s"; return; }
            string eval = r.MateWhite is int m ? $"Mate in {Mathf.Abs(m)} for {(m > 0 ? "White" : "Black")}"
                : r.EvalWhite is int cp ? $"Evaluation: {(cp >= 0 ? "+" : "")}{cp / 100f:0.00} (White's view)" : "";
            _aiEval.text = $"{eval}\nLast move: {r.Ms / 1000f:0.0} s, depth {r.Depth}";
        }

        void RefreshButtons()
        {
            var g = S.Game;
            Ui.SetEnabled(_undo, S.CanUndo);
            Ui.SetEnabled(_resign, !g.IsOver);
            Ui.SetEnabled(_draw, !g.IsOver);
            Ui.SetEnabled(_replay, g.History.Count > 0);
            Ui.SetEnabled(_restart, g.History.Count > 0 || g.IsOver);
            Ui.SetEnabled(_export, g.History.Count > 0);
        }

        void RefreshMoves()
        {
            var h = S.Game.History;
            _right.gameObject.SetActive(true);
            _movesScroll.gameObject.SetActive(_app.Settings.ShowMoveHistory);
            var start = new Position(S.Game.StartFen);
            int no = start.Fullmove;
            bool blackFirst = start.SideToMove == Side.Black;
            var lines = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < h.Count; i++)
            {
                bool white = (i % 2 == 0) != blackFirst;
                if (white || i == 0)
                {
                    sb.Clear();
                    sb.Append($"{no,3}.  ");
                    if (!white) sb.Append("…        ");
                }
                sb.Append(h[i].San.PadRight(9));
                if (!white || i == h.Count - 1) { lines.Add(sb.ToString()); }
                if (!white) no++;
            }
            while (_moveRows.Count < lines.Count)
            {
                var t = Ui.Label(_movesContent, "", 21, Ui.Text);
                t.Size(30);
                _moveRows.Add(t);
            }
            for (int i = 0; i < _moveRows.Count; i++)
            {
                bool on = i < lines.Count;
                _moveRows[i].gameObject.SetActive(on);
                if (on)
                {
                    _moveRows[i].text = lines[i];
                    _moveRows[i].color = i == lines.Count - 1 ? Ui.Accent : Ui.Text;
                }
            }
            Canvas.ForceUpdateCanvases();
            _movesScroll.verticalNormalizedPosition = 0;
        }

        static readonly Color ClockIdle = new Color(0.06f, 0.065f, 0.08f, 1f);

        /// <summary>Clock faces; text is rebuilt only when the shown value changes (no per-frame allocations).</summary>
        void UpdateClocks()
        {
            var clock = S.Clock;
            bool on = clock.Control.Enabled;
            var g = S.Game;
            for (int s = 0; s < 2; s++)
            {
                var c = _cards[s];
                if (c.ClockBg.gameObject.activeSelf != on) c.ClockBg.gameObject.SetActive(on);
                if (!on) continue;
                long ms = clock.RemainingMs((Side)s);
                bool active = !g.IsOver && g.SideToMove == (Side)s && g.History.Count > 0;
                bool low = ms < 20_000;
                // Tenths under 10 s, otherwise whole seconds; the sign carries the active/low state into the cache key.
                int shown = (ms < 10_000 ? (int)(ms / 100) : (int)(ms / 1000) * 10 + 100_000) * (active ? 1 : -1) + (low ? 7 : 0);
                if (shown == c.ClockShown) continue;
                c.ClockShown = shown;
                long secs = (ms + 999) / 1000;
                c.Clock.text = ms < 10_000 ? $"{ms / 1000}.{ms / 100 % 10}"
                    : secs >= 3600 ? $"{secs / 3600}:{secs / 60 % 60:00}:{secs % 60:00}" : $"{secs / 60:00}:{secs % 60:00}";
                c.ClockBg.color = active ? (low ? Ui.Danger : Ui.Accent) : ClockIdle;
                c.Clock.color = active ? new Color(0.08f, 0.07f, 0.05f) : low ? Ui.Danger : Ui.TextDim;
            }
        }

        public void SetViewButton(bool is2D) => _viewLabel.text = is2D ? "3D View" : "2D View";

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_toastT > 0)
            {
                _toastT -= dt;
                _toastGroup.alpha = Mathf.Clamp01(Mathf.Min(_toastT / 0.4f, 1f));
            }
            else if (_toastGroup.alpha > 0) _toastGroup.alpha = 0;

            if (S.AiThinking)
            {
                _thinkRefresh -= dt;
                if (_thinkRefresh <= 0)
                {
                    _thinkRefresh = 0.1f;
                    int dots = (int)(Time.unscaledTime * 3) % 4;
                    string live = S.Ai.LiveInfo;
                    _aiThinking.text = $"Thinking{new string('.', dots)}{new string(' ', 3 - dots)}  {S.AiElapsed:0.0} s{(live != null ? $"  ({live})" : "")}";
                }
            }
            else if (_aiThinking.text.Length > 0) _aiThinking.text = "";

            UpdateClocks();

            _fps.gameObject.SetActive(_app.Settings.ShowFps);
            if (_app.Settings.ShowFps)
            {
                _fpsAccum += dt;
                _fpsFrames++;
                if (_fpsAccum >= 0.5f)
                {
                    _fps.text = $"{_fpsFrames / _fpsAccum:0} FPS  {_fpsAccum / _fpsFrames * 1000f:0.0} ms";
                    _fpsAccum = 0;
                    _fpsFrames = 0;
                }
            }
        }
    }
}
