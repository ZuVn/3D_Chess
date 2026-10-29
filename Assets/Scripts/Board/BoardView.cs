using System.Collections.Generic;
using Chess;
using UnityEngine;
using UnityEngine.UI;

namespace ChessGame
{
    /// <summary>
    /// Presentation of the board: geometry, piece objects, highlights and animation.
    /// It never decides legality; it mirrors whatever <see cref="Sync"/> is given. Logical squares are fixed in
    /// world space (a1 at -3.5,-3.5; White at -Z); only the camera rotates, so picking is never affected.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        public const float TableY = -0.35f;
        const float LiftHeight = 0.28f;

        public static Vector3 SquareCenter(int sq) => new Vector3(Sq.File(sq) - 3.5f, 0, Sq.Rank(sq) - 3.5f);

        public static int SquareAt(Vector3 world)
        {
            int f = Mathf.FloorToInt(world.x + 4f), r = Mathf.FloorToInt(world.z + 4f);
            return f is >= 0 and < 8 && r is >= 0 and < 8 ? Sq.Make(f, r) : Sq.None;
        }

        sealed class PieceView
        {
            public int Code;
            public GameObject Go;
            public Transform Tr, MeshTr;
            public MeshRenderer Mesh;
            public SpriteRenderer Token;
            public Collider Col;
            // movement tween
            public Vector3 From, To;
            public float T = 1, Dur, Delay, Arc;
            // scale tween (spawn / despawn / tray size)
            public float Scale = 1, ScaleFrom = 1, ScaleTo = 1, ST = 1, SDur, SDelay;
            public bool Despawning, Dragged;
            public bool Busy => T < 1 || ST < 1;
        }

        readonly PieceView[] _board = new PieceView[64];
        readonly List<PieceView>[] _trays = { new List<PieceView>(), new List<PieceView>() }; // [capturer side]
        readonly List<PieceView> _all = new List<PieceView>();
        readonly Dictionary<int, Stack<PieceView>> _pool = new Dictionary<int, Stack<PieceView>>();
        readonly Dictionary<Collider, PieceView> _byCollider = new Dictionary<Collider, PieceView>();

        Material _lightMat, _darkMat, _frameMat, _inlayMat, _tableMat, _whiteMat, _blackMat;
        Material _litTemplate, _markerTemplate;
        Transform _piecesRoot, _markersRoot;
        Light _sun;
        ReflectionProbe _probe;
        Canvas _coordCanvas;
        readonly List<Text> _coordLabels = new List<Text>();
        float _labelYaw = float.NaN;

        float _flat;     // 0 = 3D, 1 = flat 2D tokens
        public float AnimSpeed = 1f;
        public bool AnimationsEnabled = true;

        public bool IsAnimating
        {
            get
            {
                foreach (var v in _all) if (v.Busy) return true;
                return false;
            }
        }

        // ------------------------------------------------------------------ construction

        public void Init(Light sun)
        {
            _sun = sun;
            _litTemplate = Resources.Load<Material>("Materials/ChessLit");
            _markerTemplate = Resources.Load<Material>("Materials/ChessMarker");
            if (_litTemplate == null || _markerTemplate == null)
                Debug.LogError("Material templates missing from Resources/Materials. Run Chess > Setup Project.");

            _lightMat = new Material(_litTemplate);
            _darkMat = new Material(_litTemplate);
            _frameMat = new Material(_litTemplate);
            _inlayMat = new Material(_litTemplate);
            _tableMat = new Material(_litTemplate);
            _whiteMat = new Material(_litTemplate);
            _blackMat = new Material(_litTemplate);

            BuildSquares();
            Child("Frame", BoxMesh(new Vector3(9.3f, 0.35f, 9.3f), 0.3f), _frameMat, new Vector3(0, -0.175f - 0.002f, 0));
            Child("Inlay", BoxMesh(new Vector3(8.12f, 0.01f, 8.12f), 1f), _inlayMat, new Vector3(0, -0.006f, 0));
            var table = Child("Table", BoxMesh(new Vector3(160f, 0.1f, 160f), 0.12f), _tableMat, new Vector3(0, TableY - 0.05f, 0));
            table.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _piecesRoot = new GameObject("Pieces").transform;
            _piecesRoot.SetParent(transform, false);
            _markersRoot = new GameObject("Markers").transform;
            _markersRoot.SetParent(transform, false);
            BuildCoordinates();

            var probeGo = new GameObject("Reflection Probe");
            probeGo.transform.SetParent(transform, false);
            probeGo.transform.localPosition = new Vector3(0, 2f, 0);
            _probe = probeGo.AddComponent<ReflectionProbe>();
            _probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            _probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            _probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            _probe.size = new Vector3(40, 20, 40);
            _probe.resolution = 128;
            _probe.importance = 1;
        }

        GameObject Child(string name, Mesh mesh, Material mat, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        void BuildSquares()
        {
            var rnd = new System.Random(42);
            for (int light = 0; light < 2; light++)
            {
                var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); var n = new List<Vector3>();
                for (int sq = 0; sq < 64; sq++)
                {
                    if (Sq.IsLight(sq) != (light == 1)) continue;
                    var c = SquareCenter(sq);
                    int i = v.Count;
                    v.Add(c + new Vector3(-0.5f, 0, -0.5f)); v.Add(c + new Vector3(-0.5f, 0, 0.5f));
                    v.Add(c + new Vector3(0.5f, 0, 0.5f)); v.Add(c + new Vector3(0.5f, 0, -0.5f));
                    for (int k = 0; k < 4; k++) n.Add(Vector3.up);
                    // Each square samples a different patch of the texture, alternating grain direction.
                    float ou = (float)rnd.NextDouble(), ov = (float)rnd.NextDouble();
                    bool rot = ((Sq.File(sq) + Sq.Rank(sq)) / 2 & 1) == 1;
                    const float s = 0.25f;
                    Vector2 P(float a, float b) => rot ? new Vector2(ou + b * s, ov + a * s) : new Vector2(ou + a * s, ov + b * s);
                    uv.Add(P(0, 0)); uv.Add(P(0, 1)); uv.Add(P(1, 1)); uv.Add(P(1, 0));
                    t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                }
                var m = new Mesh { name = light == 1 ? "LightSquares" : "DarkSquares" };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateTangents();
                var go = Child(m.name, m, light == 1 ? _lightMat : _darkMat, Vector3.zero);
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>Box with planar UVs proportional to size (so textures keep their scale).</summary>
        static Mesh BoxMesh(Vector3 size, float uvScale)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var h = size * 0.5f;
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                Vector3 u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(d, u);
                Vector3 fc = Vector3.Scale(d, h), du = Vector3.Scale(u, h), dw = Vector3.Scale(w, h);
                int i = v.Count;
                Vector3[] corners = { fc - du - dw, fc + du - dw, fc + du + dw, fc - du + dw };
                foreach (var c in corners)
                {
                    v.Add(c); n.Add(d);
                    uv.Add(new Vector2(Vector3.Dot(c, u), Vector3.Dot(c, w)) * uvScale);
                }
                // Unity front face = Cross(b - a, c - a) pointing along d.
                if (Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]), d) > 0)
                    t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else
                    t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            var m = new Mesh { name = "Box" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateTangents();
            return m;
        }

        void BuildCoordinates()
        {
            var go = new GameObject("Coordinates", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _coordCanvas = go.AddComponent<Canvas>();
            _coordCanvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(10, 10);
            rt.localPosition = new Vector3(0, 0.003f, 0);
            rt.localRotation = Quaternion.Euler(90, 0, 0);
            var font = Ui.DefaultFont;
            for (int i = 0; i < 8; i++)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    AddLabel(((char)('a' + i)).ToString(), new Vector2(i - 3.5f, side * 4.32f), font);
                    AddLabel(((char)('1' + i)).ToString(), new Vector2(side * 4.32f, i - 3.5f), font);
                }
            }
        }

        void AddLabel(string s, Vector2 pos, Font font)
        {
            var go = new GameObject(s, typeof(RectTransform));
            go.transform.SetParent(_coordCanvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(100, 100);
            rt.localScale = Vector3.one * 0.004f;
            rt.localPosition = new Vector3(pos.x, pos.y, 0);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = 72;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.text = s;
            t.raycastTarget = false;
            _coordLabels.Add(t);
        }

        public void ShowCoordinates(bool on) => _coordCanvas.gameObject.SetActive(on);

        // ------------------------------------------------------------------ themes

        readonly Dictionary<(Surface, int), Texture2D> _texCache = new Dictionary<(Surface, int), Texture2D>();

        Texture2D Tex(Surface s, int seed)
        {
            if (!_texCache.TryGetValue((s, seed), out var t)) _texCache[(s, seed)] = t = TextureGen.Detail(s, seed);
            return t;
        }

        static void SetLit(Material m, Color c, Texture2D tex, float smooth, float metal)
        {
            m.SetColor("_BaseColor", c);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
        }

        public void ApplyTheme(BoardTheme b, PieceTheme p, EnvironmentTheme e, Camera cam)
        {
            SetLit(_lightMat, b.Light, Tex(b.Squares, 1), b.Smoothness, 0);
            SetLit(_darkMat, b.Dark, Tex(b.Squares, 2), b.Smoothness, 0);
            SetLit(_frameMat, b.Frame, Tex(b.FrameSurface, 3), b.Smoothness * 0.9f, 0);
            SetLit(_inlayMat, Color.Lerp(b.Frame, b.Coordinates, 0.35f), Tex(Surface.Plain, 4), b.Smoothness, 0);
            SetLit(_whiteMat, p.White, Tex(p.Surface, 5), p.Smoothness, p.Metallic);
            if (PieceIcons.SetTheme(p))
            {
                // 2D tokens use the same colours as the 3D set.
                foreach (var v in _all) v.Token.sprite = PieceIcons.Get(v.Code);
                foreach (var st in _pool.Values) foreach (var v in st) v.Token.sprite = PieceIcons.Get(v.Code);
            }
            SetLit(_blackMat, p.Black, Tex(p.Surface, 6), p.Smoothness, p.Metallic);
            SetLit(_tableMat, e.Table, Tex(e.TableSurface, 7), 0.25f, 0);
            foreach (var l in _coordLabels) l.color = b.Coordinates;

            cam.backgroundColor = e.Background;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = e.Sky;
            RenderSettings.ambientEquatorColor = e.Equator;
            RenderSettings.ambientGroundColor = e.Ground;
            _sun.color = e.Sun;
            _sun.intensity = e.SunIntensity;
            RefreshReflections();
        }

        public void RefreshReflections()
        {
            if (_probe != null) _probe.RenderProbe();
        }

        // ------------------------------------------------------------------ pieces

        PieceView Spawn(int code)
        {
            if (_pool.TryGetValue(code, out var st) && st.Count > 0)
            {
                var r = st.Pop();
                r.Go.SetActive(true);
                r.Despawning = false; r.Dragged = false;
                _all.Add(r);
                return r;
            }
            var t = Piece.Type(code);
            var s = Piece.Color(code);
            var go = new GameObject($"{s} {t}");
            go.transform.SetParent(_piecesRoot, false);
            var meshGo = new GameObject("Mesh");
            meshGo.transform.SetParent(go.transform, false);
            // Knights face their owner's left, so the head's profile reads from the player's side of the board.
            float facing = t == PieceType.Knight ? -90f : 0f;
            meshGo.transform.localRotation = Quaternion.Euler(0, s == Side.Black ? 180f + facing : facing, 0);
            meshGo.AddComponent<MeshFilter>().sharedMesh = PieceMeshes.Get(t);
            var mr = meshGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = s == Side.White ? _whiteMat : _blackMat;

            var col = go.AddComponent<CapsuleCollider>();
            float h = PieceShapes.Height(t);
            col.center = new Vector3(0, h / 2, 0);
            col.height = h;
            col.radius = PieceShapes.Radius(t) * 0.92f;

            var tokenGo = new GameObject("Token");
            tokenGo.transform.SetParent(go.transform, false);
            tokenGo.transform.localPosition = new Vector3(0, 0.07f, 0);
            var sr = tokenGo.AddComponent<SpriteRenderer>();
            sr.sprite = PieceIcons.Get(code);
            sr.color = new Color(1, 1, 1, 0);

            var v = new PieceView { Code = code, Go = go, Tr = go.transform, MeshTr = meshGo.transform, Mesh = mr, Token = sr, Col = col };
            _byCollider[col] = v;
            _all.Add(v);
            ApplyFlat(v);
            return v;
        }

        void Recycle(PieceView v)
        {
            v.Go.SetActive(false);
            _all.Remove(v);
            if (!_pool.TryGetValue(v.Code, out var st)) _pool[v.Code] = st = new Stack<PieceView>();
            st.Push(v);
        }

        static Vector3 TraySlot(Side capturer, int index)
        {
            int col = index / 8, row = index % 8;
            return capturer == Side.White
                ? new Vector3(5.3f + col * 0.62f, TableY, -3.85f + row * 0.55f)
                : new Vector3(-5.3f - col * 0.62f, TableY, 3.85f - row * 0.55f);
        }

        const float TrayScale = 0.62f;

        /// <summary>
        /// Make the view match a position. Existing objects are reused by nearest match, so a normal move,
        /// castling, en passant, promotion, undo and captures all animate naturally from one routine.
        /// </summary>
        public void Sync(Position pos, IReadOnlyList<MoveRecord> history, int historyCount, Move lastMove, bool animate)
        {
            animate &= AnimationsEnabled;
            float speed = Mathf.Max(0.1f, AnimSpeed);
            float moveDur = 0.30f / speed;

            // Desired tray contents.
            var wantTray = new[] { new List<int>(), new List<int>() };
            for (int i = 0; i < historyCount; i++)
                if (history[i].CapturedPiece != 0) wantTray[(int)history[i].Side].Add(history[i].CapturedPiece);

            var free = new List<PieceView>();
            var newBoard = new PieceView[64];
            for (int sq = 0; sq < 64; sq++)
            {
                var v = _board[sq];
                if (v == null) continue;
                if (v.Code == pos.Board[sq]) newBoard[sq] = v;
                else free.Add(v);
            }
            var newTrays = new[] { new PieceView[wantTray[0].Count], new PieceView[wantTray[1].Count] };
            for (int s = 0; s < 2; s++)
                for (int i = 0; i < _trays[s].Count; i++)
                {
                    if (i < newTrays[s].Length && _trays[s][i].Code == wantTray[s][i]) newTrays[s][i] = _trays[s][i];
                    else free.Add(_trays[s][i]);
                }

            PieceView TakeNearest(int code, Vector3 target)
            {
                PieceView best = null;
                float bd = float.MaxValue;
                foreach (var f in free)
                {
                    if (f.Code != code) continue;
                    float d = (f.Tr.localPosition - target).sqrMagnitude;
                    if (d < bd) { bd = d; best = f; }
                }
                if (best != null) free.Remove(best);
                return best;
            }

            // Board squares first: the moving piece is matched before captured pieces head for the tray.
            var spawned = new HashSet<PieceView>();
            for (int sq = 0; sq < 64; sq++)
            {
                int code = pos.Board[sq];
                if (code == 0 || newBoard[sq] != null) continue;
                var v = TakeNearest(code, SquareCenter(sq));
                if (v == null) { v = Spawn(code); spawned.Add(v); }
                newBoard[sq] = v;
            }
            for (int s = 0; s < 2; s++)
                for (int i = 0; i < newTrays[s].Length; i++)
                {
                    if (newTrays[s][i] != null) continue;
                    var v = TakeNearest(wantTray[s][i], TraySlot((Side)s, i));
                    if (v == null) { v = Spawn(wantTray[s][i]); spawned.Add(v); }
                    newTrays[s][i] = v;
                }

            System.Array.Copy(newBoard, _board, 64);
            for (int s = 0; s < 2; s++) { _trays[s].Clear(); _trays[s].AddRange(newTrays[s]); }

            bool isPromo = lastMove.IsPromotion;
            for (int sq = 0; sq < 64; sq++)
            {
                var v = _board[sq];
                if (v == null) continue;
                v.Col.enabled = true;
                var target = SquareCenter(sq);
                if (spawned.Contains(v))
                {
                    v.Tr.localPosition = target;
                    v.T = 1;
                    // A promoted piece appears once the pawn has arrived.
                    StartScale(v, 0, 1, animate ? 0.25f / speed : 0, animate && isPromo && sq == lastMove.To ? moveDur : 0);
                    continue;
                }
                bool isMover = !lastMove.IsNone && sq == lastMove.To;
                float arc = v.Dragged ? 0 : Piece.Type(v.Code) == PieceType.Knight ? 0.55f : 0.22f;
                float dur = v.Dragged ? 0.12f / speed : moveDur;
                float delay = isMover || v.Dragged ? 0 : 0.06f / speed; // e.g. the castling rook follows the king
                v.Dragged = false;
                StartMove(v, target, animate ? dur : 0, animate ? delay : 0, arc);
                StartScale(v, v.Scale, 1, animate ? 0.2f / speed : 0, 0);
            }
            for (int s = 0; s < 2; s++)
                for (int i = 0; i < _trays[s].Count; i++)
                {
                    var v = _trays[s][i];
                    var slot = TraySlot((Side)s, i);
                    v.Col.enabled = false;
                    v.Dragged = false;
                    if (spawned.Contains(v)) { v.Tr.localPosition = slot; v.T = 1; StartScale(v, 0, TrayScale, animate ? 0.25f / speed : 0, 0); continue; }
                    // Wait for the capturing piece to land, then slide off to the tray.
                    float delay = animate ? moveDur * 0.8f : 0;
                    StartMove(v, slot, animate ? 0.38f / speed : 0, delay, 0.6f);
                    StartScale(v, v.Scale, TrayScale, animate ? 0.38f / speed : 0, delay);
                }
            foreach (var v in free)
            {
                v.Col.enabled = false;
                v.Dragged = false;
                v.Despawning = true;
                // Promotion: the pawn first walks onto the last rank, then fades away.
                bool promotingPawn = animate && isPromo && Piece.Type(v.Code) == PieceType.Pawn;
                if (promotingPawn) StartMove(v, SquareCenter(lastMove.To), moveDur, 0, 0.22f);
                StartScale(v, v.Scale, 0, animate ? 0.2f / speed : 0, promotingPawn ? moveDur : 0);
            }
            if (!animate) Update();
        }

        void StartMove(PieceView v, Vector3 to, float dur, float delay, float arc)
        {
            if (v.T < 1 && (v.To - to).sqrMagnitude < 1e-6f) return; // already heading there
            if ((v.Tr.localPosition - to).sqrMagnitude < 1e-6f) { v.T = 1; return; }
            v.From = v.Tr.localPosition;
            v.To = to;
            v.Dur = dur; v.Delay = delay; v.Arc = arc; v.T = 0;
        }

        static void StartScale(PieceView v, float from, float to, float dur, float delay)
        {
            if (Mathf.Approximately(from, to) && Mathf.Approximately(v.Scale, to)) { v.ST = 1; return; }
            v.ScaleFrom = from; v.ScaleTo = to; v.SDur = dur; v.SDelay = delay; v.ST = 0;
            v.Scale = from;
            v.Tr.localScale = Vector3.one * from;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _all.Count - 1; i >= 0; i--)
            {
                var v = _all[i];
                if (v.T < 1 && !v.Dragged)
                {
                    if (v.Delay > 0) v.Delay -= dt;
                    else
                    {
                        v.T = v.Dur <= 0 ? 1 : Mathf.Min(1, v.T + dt / v.Dur);
                        float e = EaseInOut(v.T);
                        var p = Vector3.Lerp(v.From, v.To, e);
                        p.y += v.Arc * 4f * v.T * (1 - v.T);
                        v.Tr.localPosition = p;
                    }
                }
                if (v.ST < 1)
                {
                    if (v.SDelay > 0) v.SDelay -= dt;
                    else
                    {
                        v.ST = v.SDur <= 0 ? 1 : Mathf.Min(1, v.ST + dt / v.SDur);
                        v.Scale = Mathf.Lerp(v.ScaleFrom, v.ScaleTo, EaseInOut(v.ST));
                        v.Tr.localScale = Vector3.one * v.Scale;
                    }
                }
                if (v.Despawning && v.ST >= 1 && v.T >= 1) Recycle(v);
            }
            UpdateMarkers(dt);
        }

        static float EaseInOut(float t) => t * t * (3f - 2f * t);

        // ------------------------------------------------------------------ dragging & picking

        /// <summary>Logical square of the piece under the ray, or the board square the ray hits.</summary>
        public int Pick(Ray ray, out bool hitPiece)
        {
            hitPiece = false;
            if (Physics.Raycast(ray, out var hit, 200f) && _byCollider.TryGetValue(hit.collider, out var v))
            {
                int sq = System.Array.IndexOf(_board, v);
                if (sq >= 0) { hitPiece = true; return sq; }
            }
            return RaySquare(ray, 0f);
        }

        public static int RaySquare(Ray ray, float planeY)
        {
            var plane = new Plane(Vector3.up, new Vector3(0, planeY, 0));
            return plane.Raycast(ray, out float d) ? SquareAt(ray.GetPoint(d)) : Sq.None;
        }

        /// <summary>Piece code shown on a square (0 = none). Used by tests to check view/state agreement.</summary>
        public int ViewCodeAt(int sq) => _board[sq] != null ? _board[sq].Code : 0;
        public Vector3 ViewPositionAt(int sq) => _board[sq] != null ? _board[sq].Tr.localPosition : Vector3.zero;
        public int TrayCount(Side capturer) => _trays[(int)capturer].Count;

        public void BeginDrag(int sq)
        {
            var v = _board[sq];
            if (v == null) return;
            v.Dragged = true;
            v.T = 1;
        }

        /// <summary>Moves the dragged piece so its base follows the pointer on a plane just above the board. Returns the square below it.</summary>
        public int DragTo(int sq, Ray ray)
        {
            var v = _board[sq];
            if (v == null) return Sq.None;
            var plane = new Plane(Vector3.up, new Vector3(0, LiftHeight * (1 - _flat), 0));
            if (!plane.Raycast(ray, out float d)) return SquareAt(v.Tr.localPosition);
            var p = ray.GetPoint(d);
            p.x = Mathf.Clamp(p.x, -4.6f, 4.6f);
            p.z = Mathf.Clamp(p.z, -4.6f, 4.6f);
            // Light smoothing removes jitter without feeling laggy.
            v.Tr.localPosition = Vector3.Lerp(v.Tr.localPosition, p, 1f - Mathf.Exp(-40f * Time.deltaTime));
            return SquareAt(v.Tr.localPosition);
        }

        // ------------------------------------------------------------------ 2D / 3D

        public void SetPresentation(float flat, float camYaw)
        {
            if (!Mathf.Approximately(flat, _flat))
            {
                _flat = flat;
                foreach (var v in _all) ApplyFlat(v);
                foreach (var v in _pool.Values) foreach (var p in v) ApplyFlat(p);
            }

            // Coordinates read upright for the current viewing side.
            float snapped = Mathf.Round(camYaw / 90f) * 90f;
            if (!Mathf.Approximately(snapped, _labelYaw))
            {
                _labelYaw = snapped;
                foreach (var l in _coordLabels) l.rectTransform.localRotation = Quaternion.Euler(0, 0, -snapped);
            }
            if (_sun != null) _sun.shadowStrength = Mathf.Lerp(0.85f, 0f, _flat);
        }

        // 2D tokens are fixed to the board (White's side at the bottom of each icon), like printed pieces on a real board.
        static readonly Quaternion TokenRotation = Quaternion.Euler(90, 0, 0);

        void ApplyFlat(PieceView v)
        {
            v.MeshTr.localScale = new Vector3(1, Mathf.Lerp(1f, 0.03f, _flat), 1);
            v.Mesh.enabled = _flat < 0.98f;
            float a = Mathf.Clamp01((_flat - 0.35f) / 0.6f);
            v.Token.color = new Color(1, 1, 1, a);
            v.Token.enabled = a > 0.001f;
            v.Token.transform.localRotation = TokenRotation;
        }

        // ------------------------------------------------------------------ highlights

        enum MarkKind { LastMove, Selected, Legal, Capture, Check, Hover, DragTarget, Illegal }

        Material[] _markMats;
        Mesh _quad;
        readonly List<MeshRenderer>[] _markPool = new List<MeshRenderer>[8];
        readonly int[] _markUsed = new int[8];
        MeshRenderer _illegal;
        float _illegalT;

        void EnsureMarkers()
        {
            if (_markMats != null) return;
            var fill = TextureGen.Marker(TextureGen.Mark.Fill);
            var frame = TextureGen.Marker(TextureGen.Mark.Frame);
            var dot = TextureGen.Marker(TextureGen.Mark.Dot);
            var ring = TextureGen.Marker(TextureGen.Mark.Ring);
            (Texture2D tex, Color col)[] defs =
            {
                (fill, Themes.LastMove), (frame, Themes.Selected), (dot, Themes.LegalMove), (ring, Themes.Capture),
                (ring, Themes.Check), (frame, Themes.Hover), (frame, new Color(1, 1, 1, 0.75f)), (fill, Themes.Illegal),
            };
            _markMats = new Material[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                var m = new Material(_markerTemplate);
                m.SetTexture("_BaseMap", defs[i].tex);
                m.SetColor("_BaseColor", defs[i].col);
                m.renderQueue = 3000 + i;
                _markMats[i] = m;
                _markPool[i] = new List<MeshRenderer>();
            }
            _quad = new Mesh { name = "MarkerQuad" };
            _quad.SetVertices(new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) });
            _quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            _quad.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            _quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            _illegal = NewMarker(MarkKind.Illegal);
            _illegal.gameObject.SetActive(false);
        }

        MeshRenderer NewMarker(MarkKind k)
        {
            var go = new GameObject(k.ToString());
            go.transform.SetParent(_markersRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _markMats[(int)k];
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        void Mark(MarkKind k, int sq)
        {
            if (sq < 0) return;
            var list = _markPool[(int)k];
            int i = _markUsed[(int)k]++;
            if (i == list.Count) list.Add(NewMarker(k));
            var mr = list[i];
            mr.gameObject.SetActive(true);
            mr.transform.localPosition = SquareCenter(sq) + new Vector3(0, 0.004f + 0.001f * (int)k, 0);
        }

        /// <summary>Rebuilds the static highlight set. Cheap; called only when something changes.</summary>
        public void SetHighlights(Move lastMove, int selected, IReadOnlyList<Move> legal, bool showLegal, int checkSq, int hover, int dragTarget)
        {
            EnsureMarkers();
            for (int k = 0; k < _markPool.Length; k++)
            {
                if (k == (int)MarkKind.Illegal) continue;
                _markUsed[k] = 0;
            }
            if (!lastMove.IsNone) { Mark(MarkKind.LastMove, lastMove.From); Mark(MarkKind.LastMove, lastMove.To); }
            if (checkSq >= 0) Mark(MarkKind.Check, checkSq);
            if (selected >= 0)
            {
                Mark(MarkKind.Selected, selected);
                if (showLegal && legal != null)
                {
                    int lastTo = -1;
                    foreach (var m in legal)
                    {
                        if (m.From != selected || m.To == lastTo) continue; // promotions share a square
                        lastTo = m.To;
                        Mark(m.IsCapture ? MarkKind.Capture : MarkKind.Legal, m.To);
                    }
                }
            }
            if (hover >= 0 && hover != selected) Mark(MarkKind.Hover, hover);
            if (dragTarget >= 0) Mark(MarkKind.DragTarget, dragTarget);
            for (int k = 0; k < _markPool.Length; k++)
            {
                if (k == (int)MarkKind.Illegal) continue;
                var list = _markPool[k];
                for (int i = _markUsed[k]; i < list.Count; i++) list[i].gameObject.SetActive(false);
            }
        }

        public void FlashIllegal(int sq)
        {
            EnsureMarkers();
            if (sq < 0) return;
            _illegal.transform.localPosition = SquareCenter(sq) + new Vector3(0, 0.012f, 0);
            _illegal.gameObject.SetActive(true);
            _illegalT = 0.45f;
        }

        void UpdateMarkers(float dt)
        {
            if (_illegalT <= 0) return;
            _illegalT -= dt;
            var c = Themes.Illegal;
            c.a *= Mathf.Clamp01(_illegalT / 0.45f);
            _markMats[(int)MarkKind.Illegal].SetColor("_BaseColor", c);
            if (_illegalT <= 0) _illegal.gameObject.SetActive(false);
        }
    }
}
