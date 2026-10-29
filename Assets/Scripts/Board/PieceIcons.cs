using System.Collections.Generic;
using Chess;
using UnityEngine;

namespace ChessGame
{
    /// <summary>
    /// Flat side-view icons rasterised from the same outlines as the 3D meshes and tinted with the current
    /// piece theme, so 2D tokens, captured-piece trays and the promotion picker match the 3D set.
    /// </summary>
    public static class PieceIcons
    {
        const int Size = 128, Super = 2, M = Size * Super;
        // Piece units shown in the icon (same scale for every piece so relative sizes are kept).
        const float MinX = -0.86f, MaxX = 0.86f, MinY = -0.06f, MaxY = 1.66f;

        static readonly Sprite[] Cache = new Sprite[16];
        static PieceTheme _theme = Themes.Pieces[0];

        /// <summary>Switch colours; icons are rebuilt lazily. Returns true if anything changed.</summary>
        public static bool SetTheme(PieceTheme theme)
        {
            if (theme == _theme) return false;
            _theme = theme;
            for (int i = 0; i < Cache.Length; i++)
            {
                if (Cache[i] == null) continue;
                Object.Destroy(Cache[i].texture);
                Object.Destroy(Cache[i]);
                Cache[i] = null;
            }
            return true;
        }

        public static Sprite Get(int piece)
        {
            if (Cache[piece] == null) Cache[piece] = Build(Piece.Type(piece), Piece.Color(piece));
            return Cache[piece];
        }

        static Vector2 ToUnits(int x, int y) =>
            new Vector2(Mathf.Lerp(MinX, MaxX, (x + 0.5f) / M), Mathf.Lerp(MinY, MaxY, (y + 0.5f) / M));

        static int RowOf(float units) => Mathf.RoundToInt((units - MinY) / (MaxY - MinY) * M - 0.5f);

        static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        static Sprite Build(PieceType t, Side side)
        {
            var mask = new bool[M * M];
            var radius = new float[M]; // lathe radius per row (for cylindrical shading), -1 = none

            var prof = PieceShapes.Profile(t);
            for (int y = 0; y < M; y++)
            {
                float py = ToUnits(0, y).y, r = -1;
                for (int i = 0; i < prof.Count - 1; i++)
                {
                    var a = prof[i]; var b = prof[i + 1];
                    float lo = Mathf.Min(a.y, b.y), hi = Mathf.Max(a.y, b.y);
                    if (py < lo || py > hi) continue;
                    float rr = hi - lo < 1e-6f ? Mathf.Max(a.x, b.x) : Mathf.Lerp(a.x, b.x, (py - a.y) / (b.y - a.y));
                    r = Mathf.Max(r, rr);
                }
                radius[y] = r;
                if (r < 0) continue;
                for (int x = 0; x < M; x++) if (Mathf.Abs(ToUnits(x, y).x) <= r) mask[y * M + x] = true;
            }

            var detail = new bool[M * M]; // inner lines drawn in the outline colour
            for (int y = 0; y < M; y++)
                for (int x = 0; x < M; x++)
                {
                    var p = ToUnits(x, y);
                    int i = y * M + x;
                    switch (t)
                    {
                        case PieceType.Knight:
                            // Facing left on screen, like printed diagrams.
                            var q = new Vector2(-p.x, p.y);
                            if (InPolygon(q, PieceShapes.KnightHead)) mask[i] = true;
                            if ((q - new Vector2(0.17f, 0.93f)).sqrMagnitude < 0.032f * 0.032f) detail[i] = true;              // eye
                            if (Mathf.Abs(q.x + 0.15f - (q.y - 0.55f) * 0.12f) < 0.012f && q.y > 0.3f && q.y < 0.95f) detail[i] = true; // mane
                            break;
                        case PieceType.King:
                            foreach (var (c, s) in PieceShapes.KingCross)
                                if (Mathf.Abs(p.x - c.x) <= s.x / 2 && Mathf.Abs(p.y - c.y) <= s.y / 2) mask[i] = true;
                            break;
                        case PieceType.Queen:
                            for (int k = -2; k <= 2; k++)
                            {
                                var c = new Vector2(k * 0.13f, 1.33f + (2 - Mathf.Abs(k)) * 0.025f);
                                if ((p - c).sqrMagnitude < 0.045f * 0.045f) mask[i] = true;
                                if (Mathf.Abs(p.x - c.x) < 0.03f && p.y > 1.24f && p.y < c.y) mask[i] = true;
                            }
                            break;
                        case PieceType.Rook:
                            if (p.y > 0.91f && Mathf.Abs(p.x) > 0.06f && Mathf.Abs(p.x) < 0.17f) mask[i] = false;
                            break;
                        case PieceType.Bishop:
                            if (p.y > 0.93f && p.y < 1.08f && Mathf.Abs(p.x - 0.03f - (p.y - 1.0f) * 0.9f) < 0.022f) mask[i] = false;
                            break;
                    }
                }

            // Ridge lines at the profile's collars (local radius maxima), like the turned rings on a real piece.
            var ridges = new List<int>();
            for (int i = 1; i < prof.Count - 1; i++)
                if (prof[i].y > 0.05f && prof[i].x > prof[i - 1].x - 1e-4f && prof[i].x > prof[i + 1].x + 1e-4f && prof[i].x > 0.15f)
                    ridges.Add(RowOf(prof[i].y));
            foreach (int ry in ridges)
                for (int y = ry - 2; y <= ry + 2; y++)
                {
                    if (y < 0 || y >= M || radius[y] < 0) continue;
                    for (int x = 0; x < M; x++)
                        if (Mathf.Abs(ToUnits(x, y).x) <= radius[y] - 0.05f) detail[y * M + x] = true;
                }

            bool In(int x, int y) => x >= 0 && y >= 0 && x < M && y < M && mask[y * M + x];

            Color fill = side == Side.White ? _theme.White : _theme.Black;
            bool lightFill = Luminance(fill) > 0.42f;
            Color line = lightFill ? fill * 0.22f : Color.Lerp(fill, Color.white, 0.72f);
            line.a = 1;
            float gloss = Mathf.Clamp01(_theme.Smoothness) * (0.35f + 0.65f * _theme.Metallic + 0.3f);

            const int w = 6, sx = 6, sy = -7; // outline width, drop-shadow offset (super-sampled px)
            var px2 = new Color[M * M];
            for (int y = 0; y < M; y++)
                for (int x = 0; x < M; x++)
                {
                    int i = y * M + x;
                    if (!mask[i])
                    {
                        // Soft drop shadow so the token reads as an object resting on the board.
                        px2[i] = In(x - sx, y - sy) ? new Color(0, 0, 0, 0.3f) : Color.clear;
                        continue;
                    }
                    bool edge = !In(x - w, y) || !In(x + w, y) || !In(x, y - w) || !In(x, y + w)
                                || !In(x - 4, y - 4) || !In(x + 4, y - 4) || !In(x - 4, y + 4) || !In(x + 4, y + 4);
                    if (edge || detail[i]) { px2[i] = line; continue; }

                    var p = ToUnits(x, y);
                    // Cylindrical shading across the turned parts, light from the upper left.
                    float r = radius[y] > 0.02f ? radius[y] : 0.3f;
                    float u = Mathf.Clamp(p.x / r, -1f, 1f);
                    float shade = 1.06f - 0.28f * u * u + 0.07f * -u + 0.08f * (p.y / MaxY);
                    float spec = gloss * Mathf.Exp(-Mathf.Pow((u + 0.42f) / 0.14f, 2));
                    if (_theme.Surface == Surface.Wood)
                        shade *= 0.93f + 0.07f * Mathf.Sin(p.y * 70f + Mathf.PerlinNoise(p.x * 6f, p.y * 3f) * 6f);
                    var c = fill * shade + Color.white * spec * 0.5f;
                    c.a = 1;
                    px2[i] = c;
                }

            // Box-filter downsample with premultiplied colour so edges stay clean.
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    Color acc = Color.clear;
                    for (int yy = 0; yy < Super; yy++)
                        for (int xx = 0; xx < Super; xx++)
                        {
                            var c = px2[(y * Super + yy) * M + x * Super + xx];
                            acc += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                        }
                    acc /= Super * Super;
                    px[y * Size + x] = acc.a > 0 ? new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, acc.a) : Color.clear;
                }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = $"{side} {t} icon" };
            tex.SetPixels(px);
            tex.Apply(true, true);
            // Pivot at the icon centre; 128 px per unit = one board square.
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
        }

        static bool InPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }
    }
}
