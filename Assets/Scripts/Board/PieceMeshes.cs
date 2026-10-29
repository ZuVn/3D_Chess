using System;
using System.Collections.Generic;
using Chess;
using UnityEngine;

namespace ChessGame
{
    /// <summary>
    /// Original Staunton-style piece geometry: lathe profiles (radius, height) for the round pieces,
    /// an extruded head silhouette for the knight. Units: one board square = 1. Pieces face +Z.
    /// The same outlines drive the 2D icons (<see cref="PieceIcons"/>), so both views always match.
    /// </summary>
    public static class PieceShapes
    {
        static List<Vector2> P(params float[] xy)
        {
            var l = new List<Vector2>();
            for (int i = 0; i < xy.Length; i += 2) l.Add(new Vector2(xy[i], xy[i + 1]));
            return l;
        }

        /// <summary>Repeated point = hard edge in the lathe normals.</summary>
        static void Hard(List<Vector2> l, float r, float y) { l.Add(new Vector2(r, y)); l.Add(new Vector2(r, y)); }

        static void Arc(List<Vector2> l, float cy, float rx, float ry, float a0, float a1, int steps)
        {
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad;
                l.Add(new Vector2(Mathf.Max(0, rx * Mathf.Cos(a)), cy + ry * Mathf.Sin(a)));
            }
        }

        static List<Vector2> Base(float r0)
        {
            var l = P(0, 0);
            Hard(l, r0, 0);
            l.Add(new Vector2(r0, 0.06f));
            l.Add(new Vector2(r0 - 0.03f, 0.09f));
            Hard(l, r0 - 0.06f, 0.11f);
            l.Add(new Vector2(r0 - 0.06f, 0.14f));
            return l;
        }

        public static List<Vector2> Profile(PieceType t)
        {
            List<Vector2> l;
            switch (t)
            {
                case PieceType.Pawn:
                    l = Base(0.34f);
                    l.AddRange(P(0.24f, 0.18f, 0.18f, 0.24f, 0.14f, 0.32f, 0.12f, 0.42f, 0.11f, 0.48f, 0.19f, 0.50f, 0.20f, 0.53f, 0.19f, 0.56f, 0.11f, 0.575f));
                    Arc(l, 0.72f, 0.165f, 0.165f, -60, 90, 12);
                    return l;
                case PieceType.Rook:
                    l = Base(0.38f);
                    l.AddRange(P(0.29f, 0.19f, 0.26f, 0.25f, 0.24f, 0.45f, 0.235f, 0.68f, 0.25f, 0.73f));
                    Hard(l, 0.29f, 0.77f);
                    l.Add(new Vector2(0.29f, 0.80f));
                    Hard(l, 0.31f, 0.82f);
                    Hard(l, 0.31f, 1.00f);
                    Hard(l, 0.22f, 1.00f);
                    Hard(l, 0.22f, 0.88f);
                    l.Add(new Vector2(0, 0.88f));
                    return l;
                case PieceType.Knight:
                    l = Base(0.38f);
                    l.AddRange(P(0.30f, 0.18f));
                    Hard(l, 0.29f, 0.22f);
                    l.Add(new Vector2(0, 0.22f));
                    return l;
                case PieceType.Bishop:
                    l = Base(0.36f);
                    l.AddRange(P(0.25f, 0.19f, 0.19f, 0.27f, 0.15f, 0.40f, 0.12f, 0.55f, 0.11f, 0.66f, 0.20f, 0.69f, 0.21f, 0.72f, 0.19f, 0.75f, 0.12f, 0.77f));
                    Arc(l, 0.96f, 0.17f, 0.21f, -62, 78, 14);
                    Arc(l, 1.215f, 0.05f, 0.05f, -70, 90, 8);
                    return l;
                case PieceType.Queen:
                    l = Base(0.38f);
                    l.AddRange(P(0.27f, 0.19f, 0.20f, 0.28f, 0.16f, 0.42f, 0.13f, 0.60f, 0.12f, 0.78f, 0.22f, 0.82f, 0.23f, 0.85f, 0.20f, 0.88f,
                        0.14f, 0.90f, 0.15f, 0.98f, 0.19f, 1.10f, 0.25f, 1.22f));
                    Hard(l, 0.265f, 1.27f);
                    l.AddRange(P(0.20f, 1.255f, 0.14f, 1.27f, 0.09f, 1.31f));
                    Arc(l, 1.375f, 0.07f, 0.07f, -45, 90, 8);
                    return l;
                case PieceType.King:
                    l = Base(0.40f);
                    l.AddRange(P(0.29f, 0.19f, 0.22f, 0.28f, 0.17f, 0.42f, 0.14f, 0.62f, 0.13f, 0.82f, 0.23f, 0.86f, 0.24f, 0.89f, 0.21f, 0.92f,
                        0.15f, 0.94f, 0.16f, 1.02f, 0.20f, 1.14f, 0.24f, 1.24f));
                    Hard(l, 0.25f, 1.28f);
                    l.AddRange(P(0.19f, 1.32f, 0.10f, 1.36f, 0f, 1.37f));
                    return l;
            }
            throw new ArgumentOutOfRangeException(nameof(t));
        }

        /// <summary>Knight head silhouette: x = forward, y = up.</summary>
        public static readonly Vector2[] KnightHead =
        {
            new Vector2(-0.24f, 0.20f), new Vector2(-0.26f, 0.45f), new Vector2(-0.24f, 0.68f), new Vector2(-0.18f, 0.88f),
            new Vector2(-0.08f, 1.02f), new Vector2(-0.03f, 1.14f), new Vector2(0.02f, 1.20f), new Vector2(0.06f, 1.06f),
            new Vector2(0.16f, 1.00f), new Vector2(0.28f, 0.88f), new Vector2(0.36f, 0.76f), new Vector2(0.37f, 0.66f),
            new Vector2(0.30f, 0.61f), new Vector2(0.17f, 0.62f), new Vector2(0.07f, 0.57f), new Vector2(0.12f, 0.42f),
            new Vector2(0.22f, 0.20f),
        };

        public static float KnightHalfThickness(float forward) => 0.16f - 0.06f * Mathf.InverseLerp(-0.26f, 0.37f, forward);

        /// <summary>King cross as (center, size) boxes.</summary>
        public static readonly (Vector3 c, Vector3 s)[] KingCross =
        {
            (new Vector3(0, 1.50f, 0), new Vector3(0.075f, 0.28f, 0.075f)),
            (new Vector3(0, 1.545f, 0), new Vector3(0.23f, 0.075f, 0.075f)),
        };

        public static float Height(PieceType t) => t switch
        {
            PieceType.Pawn => 0.89f, PieceType.Rook => 1.0f, PieceType.Knight => 1.2f,
            PieceType.Bishop => 1.27f, PieceType.Queen => 1.45f, _ => 1.64f,
        };

        public static float Radius(PieceType t) => t switch
        {
            PieceType.Pawn => 0.34f, PieceType.Bishop => 0.36f, PieceType.King => 0.40f, _ => 0.38f,
        };
    }

    public static class PieceMeshes
    {
        static readonly Mesh[] Cache = new Mesh[7];

        public static Mesh Get(PieceType t)
        {
            if (Cache[(int)t] == null) Cache[(int)t] = Build(t);
            return Cache[(int)t];
        }

        sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();

            public int Add(Vector3 v, Vector3 n, Vector2 uv) { V.Add(v); N.Add(n); UV.Add(uv); return V.Count - 1; }

            /// <summary>Adds a triangle wound so that it faces <paramref name="facing"/> (Unity: front = Cross(b-a, c-a)).</summary>
            public void Tri(int a, int b, int c, Vector3 facing)
            {
                var n = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
                if (n.sqrMagnitude < 1e-14f) return;
                if (Vector3.Dot(n, facing) < 0) (b, c) = (c, b);
                T.Add(a); T.Add(b); T.Add(c);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(V);
                m.SetNormals(N);
                m.SetUVs(0, UV);
                m.SetTriangles(T, 0);
                m.RecalculateBounds();
                m.RecalculateTangents();
                m.UploadMeshData(true);
                return m;
            }
        }

        static Mesh Build(PieceType t)
        {
            var b = new Builder();
            var profile = PieceShapes.Profile(t);
            Func<int, float, Vector2, Vector2> mod = null;
            int segments = 40;
            switch (t)
            {
                case PieceType.Rook:
                    segments = 48;
                    // Six crenels cut into the top rim.
                    mod = (i, a, p) => p.y > 0.99f && Mathf.Repeat(a * Mathf.Rad2Deg + 15f, 60f) < 22f ? new Vector2(p.x, 0.90f) : p;
                    break;
                case PieceType.Bishop:
                    // Diagonal mitre slit at the front of the head.
                    mod = (i, a, p) =>
                    {
                        if (p.y < 0.93f || p.y > 1.13f) return p;
                        float centre = (p.y - 1.03f) * 260f;
                        float d = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, centre));
                        return d < 10f ? new Vector2(p.x * 0.45f, p.y) : p;
                    };
                    break;
                case PieceType.Queen:
                    // Eight crown points on the rim.
                    mod = (i, a, p) =>
                    {
                        if (Mathf.Abs(p.y - 1.27f) > 0.001f) return p;
                        float c = Mathf.Max(0, Mathf.Cos(a * 8f));
                        return new Vector2(p.x, p.y + 0.08f * c * c);
                    };
                    break;
            }
            Lathe(b, profile, segments, mod);
            if (t == PieceType.Knight) KnightHead(b);
            if (t == PieceType.King) foreach (var (c, s) in PieceShapes.KingCross) Box(b, c, s);
            return b.ToMesh(t.ToString());
        }

        static void Lathe(Builder b, List<Vector2> prof, int seg, Func<int, float, Vector2, Vector2> mod)
        {
            int n = prof.Count;
            // Profile normals (outward): average of adjacent non-degenerate segment normals.
            var pn = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 acc = Vector2.zero;
                if (i > 0) acc += SegNormal(prof[i - 1], prof[i]);
                if (i < n - 1) acc += SegNormal(prof[i], prof[i + 1]);
                pn[i] = acc.sqrMagnitude > 0 ? acc.normalized : Vector2.up;
            }
            float total = 0;
            var vcoord = new float[n];
            for (int i = 1; i < n; i++) { total += Vector2.Distance(prof[i - 1], prof[i]); vcoord[i] = total; }

            int start = b.V.Count;
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= seg; j++)
                {
                    float a = j / (float)seg * Mathf.PI * 2f;
                    var p = mod != null ? mod(i, a, prof[i]) : prof[i];
                    float s = Mathf.Sin(a), c = Mathf.Cos(a);
                    b.Add(new Vector3(p.x * s, p.y, p.x * c), new Vector3(pn[i].x * s, pn[i].y, pn[i].x * c),
                        new Vector2(j / (float)seg, vcoord[i] / total));
                }
            int row = seg + 1;
            for (int i = 0; i < n - 1; i++)
            {
                if ((prof[i + 1] - prof[i]).sqrMagnitude < 1e-10f) continue;
                for (int j = 0; j < seg; j++)
                {
                    int v00 = start + i * row + j, v01 = v00 + 1, v10 = v00 + row, v11 = v10 + 1;
                    var facing = b.N[v00] + b.N[v01] + b.N[v10] + b.N[v11];
                    b.Tri(v00, v10, v11, facing);
                    b.Tri(v00, v11, v01, facing);
                }
            }
        }

        static Vector2 SegNormal(Vector2 a, Vector2 b)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-10f) return Vector2.zero;
            return new Vector2(d.y, -d.x).normalized;
        }

        static void KnightHead(Builder b)
        {
            var poly = PieceShapes.KnightHead;
            int n = poly.Length;
            var tris = Triangulate(poly);

            // Side caps (polygon faces) at x = ±halfThickness(forward).
            for (int side = -1; side <= 1; side += 2)
            {
                int start = b.V.Count;
                const float slope = -0.06f / 0.63f; // d(halfThickness)/d(forward)
                var normal = new Vector3(side, 0, -side * slope).normalized;
                foreach (var p in poly)
                    b.Add(new Vector3(side * PieceShapes.KnightHalfThickness(p.x), p.y, p.x), normal, new Vector2(p.x + 0.5f, p.y));
                for (int k = 0; k < tris.Count; k += 3)
                    b.Tri(start + tris[k], start + tris[k + 1], start + tris[k + 2], normal);
            }

            // Walls around the outline, normals averaged at gentle corners.
            float area = 0;
            for (int i = 0; i < n; i++) { var p = poly[i]; var q = poly[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
            float orient = Mathf.Sign(area); // +1 = counter-clockwise
            Vector3 EdgeNormal(int i)
            {
                var d = poly[(i + 1) % n] - poly[i];
                var nn = new Vector2(d.y, -d.x).normalized * orient; // outward
                return new Vector3(0, nn.y, nn.x);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var en = EdgeNormal(i);
                var ni = Vector3.Dot(EdgeNormal((i + n - 1) % n), en) > 0.5f ? (EdgeNormal((i + n - 1) % n) + en).normalized : en;
                var nj = Vector3.Dot(EdgeNormal(j), en) > 0.5f ? (EdgeNormal(j) + en).normalized : en;
                var pi = poly[i]; var pj = poly[j];
                float hi = PieceShapes.KnightHalfThickness(pi.x), hj = PieceShapes.KnightHalfThickness(pj.x);
                int a = b.Add(new Vector3(-hi, pi.y, pi.x), ni, new Vector2(0, pi.y));
                int c = b.Add(new Vector3(hi, pi.y, pi.x), ni, new Vector2(1, pi.y));
                int d2 = b.Add(new Vector3(-hj, pj.y, pj.x), nj, new Vector2(0, pj.y));
                int e = b.Add(new Vector3(hj, pj.y, pj.x), nj, new Vector2(1, pj.y));
                b.Tri(a, c, e, en);
                b.Tri(a, e, d2, en);
            }

            // Mane ridge along the back of the neck.
            Box(b, new Vector3(0, 0.80f, -0.235f), new Vector3(0.07f, 0.42f, 0.06f));
        }

        static void Box(Builder b, Vector3 c, Vector3 s)
        {
            var h = s * 0.5f;
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var n in dirs)
            {
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                Vector3 fc = c + Vector3.Scale(n, h);
                Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
                int i0 = b.Add(fc - du - dv, n, new Vector2(0, 0));
                int i1 = b.Add(fc + du - dv, n, new Vector2(1, 0));
                int i2 = b.Add(fc + du + dv, n, new Vector2(1, 1));
                int i3 = b.Add(fc - du + dv, n, new Vector2(0, 1));
                b.Tri(i0, i1, i2, n);
                b.Tri(i0, i2, i3, n);
            }
        }

        /// <summary>Ear-clipping triangulation of a simple polygon (either winding).</summary>
        public static List<int> Triangulate(IReadOnlyList<Vector2> poly)
        {
            var idx = new List<int>();
            for (int i = 0; i < poly.Count; i++) idx.Add(i);
            float area = 0;
            for (int i = 0; i < poly.Count; i++) { var p = poly[i]; var q = poly[(i + 1) % poly.Count]; area += p.x * q.y - q.x * p.y; }
            float sign = Mathf.Sign(area);
            var result = new List<int>();
            int guard = 0;
            while (idx.Count > 3 && guard++ < 1000)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (cross * sign <= 0) continue; // reflex
                    bool inside = false;
                    foreach (int o in idx)
                    {
                        if (o == ia || o == ib || o == ic) continue;
                        if (InTriangle(poly[o], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    result.Add(ia); result.Add(ib); result.Add(ic);
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]); }
            return result;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
