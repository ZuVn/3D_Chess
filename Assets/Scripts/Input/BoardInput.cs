using System;
using Chess;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace ChessGame
{
    /// <summary>
    /// Single owner of pointer input over the 3D view, so camera orbit, piece selection, dragging and UI
    /// never compete: a gesture is classified once (on press) and keeps that meaning until release.
    /// Left press on an own piece → select / drag. Left press elsewhere → click (deselect) or orbit after
    /// a few pixels. Right/middle drag always orbits. Presses that start over UI are ignored entirely.
    /// </summary>
    public sealed class BoardInput : MonoBehaviour
    {
        enum Gesture { None, PieceArmed, Dragging, EmptyArmed, Orbit }

        public BoardView Board;
        public CameraRig Rig;
        public Func<Game> GetGame;
        /// <summary>True when the local player may move the side to move (and no modal is open).</summary>
        public Func<bool> CanMove;
        /// <summary>Called with a from/to pair; returns true if the move was played (handles promotion choice).</summary>
        public Func<int, int, bool> TryMove;
        public Action<int> Illegal;
        public Action SelectionChanged;
        /// <summary>Re-sync the view to the (unchanged) position, e.g. to return a dropped piece.</summary>
        public Action Resync;

        public int Selected { get; private set; } = Sq.None;
        public int Hover { get; private set; } = Sq.None;
        public int DragTarget { get; private set; } = Sq.None;
        public bool IsDragging => _gesture == Gesture.Dragging;
        public bool InputLocked; // e.g. menus

        Gesture _gesture;
        Vector2 _pressPos, _lastPos;
        bool _wasSelected;
        const float DragThreshold = 6f;

        public void ClearSelection()
        {
            if (_gesture == Gesture.Dragging) CancelDrag();
            _gesture = Gesture.None;
            if (Selected != Sq.None || DragTarget != Sq.None) { Selected = Sq.None; DragTarget = Sq.None; SelectionChanged?.Invoke(); }
        }

        void CancelDrag()
        {
            // Returning the piece is the view's job: re-syncing to the unchanged position slides it home.
            _gesture = Gesture.None;
            DragTarget = Sq.None;
            SelectionChanged?.Invoke();
        }

        bool OwnPiece(int sq)
        {
            var g = GetGame();
            int p = sq >= 0 ? g.Position.Board[sq] : 0;
            return p != 0 && Piece.Color(p) == g.SideToMove;
        }

        bool IsTarget(int from, int to)
        {
            if (from < 0 || to < 0) return false;
            foreach (var m in GetGame().LegalMoves) if (m.From == from && m.To == to) return true;
            return false;
        }

        static bool OverUi(Vector2 pos)
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        void Update()
        {
            var pointer = Pointer.current;
            var mouse = Mouse.current;
            if (pointer == null || Rig == null) return;
            Vector2 pos = pointer.position.ReadValue();

            // Scroll zoom (not over UI panels, e.g. the move list).
            if (mouse != null && !InputLocked)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !OverUi(pos)) Rig.Zoom(Mathf.Sign(scroll) * Mathf.Min(3f, Mathf.Abs(scroll) / 120f + 0.5f));
            }

            // Right / middle drag: orbit only.
            if (mouse != null && (mouse.rightButton.isPressed || mouse.middleButton.isPressed))
            {
                if ((mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)) _lastPos = pos;
                else if (_gesture != Gesture.Dragging) Rig.Orbit(pos - _lastPos);
                _lastPos = pos;
            }

            bool canMove = !InputLocked && CanMove() && !Rig.IsTransitioning;
            var ray = Rig.Cam.ScreenPointToRay(pos);

            if (pointer.press.wasPressedThisFrame)
            {
                if (InputLocked || OverUi(pos)) { _gesture = Gesture.None; return; }
                _pressPos = _lastPos = pos;
                int sq = Board.Pick(ray, out _);
                if (canMove && Selected >= 0 && sq != Selected && IsTarget(Selected, sq))
                {
                    // Click-to-move.
                    int from = Selected;
                    ClearSelection();
                    TryMove(from, sq);
                    _gesture = Gesture.None;
                }
                else if (canMove && OwnPiece(sq))
                {
                    _wasSelected = Selected == sq;
                    Selected = sq;
                    _gesture = Gesture.PieceArmed;
                    SelectionChanged?.Invoke();
                }
                else _gesture = Gesture.EmptyArmed;
            }
            else if (pointer.press.isPressed)
            {
                switch (_gesture)
                {
                    case Gesture.PieceArmed when (pos - _pressPos).magnitude > DragThreshold:
                        if (!canMove) { _gesture = Gesture.None; break; }
                        _gesture = Gesture.Dragging;
                        Board.BeginDrag(Selected);
                        goto case Gesture.Dragging;
                    case Gesture.Dragging:
                        if (!canMove) { ClearSelection(); Resync(); break; }
                        int over = Board.DragTo(Selected, ray);
                        int target = IsTarget(Selected, over) ? over : Sq.None;
                        if (target != DragTarget) { DragTarget = target; SelectionChanged?.Invoke(); }
                        break;
                    case Gesture.EmptyArmed when (pos - _pressPos).magnitude > DragThreshold:
                        _gesture = Gesture.Orbit;
                        Rig.Orbit(pos - _pressPos);
                        break;
                    case Gesture.Orbit:
                        Rig.Orbit(pos - _lastPos);
                        break;
                }
                _lastPos = pos;
            }
            else if (pointer.press.wasReleasedThisFrame)
            {
                var g = _gesture;
                _gesture = Gesture.None;
                if (g == Gesture.Dragging)
                {
                    int from = Selected;
                    int to = Board.DragTo(from, ray);
                    DragTarget = Sq.None;
                    if (canMove && IsTarget(from, to))
                    {
                        ClearSelection();
                        if (!TryMove(from, to)) Resync();
                    }
                    else
                    {
                        if (to >= 0 && to != from) Illegal?.Invoke(to);
                        Resync(); // piece slides back to its square, stays selected
                        SelectionChanged?.Invoke();
                    }
                }
                else if (g == Gesture.PieceArmed && _wasSelected) ClearSelection(); // second click on the same piece
                else if (g == Gesture.EmptyArmed && Selected >= 0) ClearSelection();
            }

            // Hover feedback (only for things that can be clicked).
            int hover = Sq.None;
            if (canMove && _gesture == Gesture.None && !OverUi(pos))
            {
                int sq = Board.Pick(ray, out _);
                if (OwnPiece(sq) || IsTarget(Selected, sq)) hover = sq;
            }
            if (hover != Hover) { Hover = hover; SelectionChanged?.Invoke(); }
        }
    }
}
