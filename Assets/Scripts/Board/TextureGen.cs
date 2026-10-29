using UnityEngine;

namespace ChessGame
{
    /// <summary>Procedural, tileable-enough textures so the project ships without third-party art.</summary>
    public static class TextureGen
    {
        /// <summary>Greyscale detail texture (multiplied with the material colour). White = 1.</summary>
        public static Texture2D Detail(Surface kind, int seed, int size = 256)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color32[size * size];
            float ox = seed * 17.31f, oy = seed * 9.73f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float g = kind switch
                    {
                        Surface.Wood => Wood(u, v, ox, oy),
                        Surface.Marble => Marble(u, v, ox, oy),
                        _ => 1f - 0.04f * Mathf.PerlinNoise(ox + u * 40f, oy + v * 40f),
                    };
                    byte b = (byte)(Mathf.Clamp01(g) * 255f);
                    px[y * size + x] = new Color32(b, b, b, 255);
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static float Fbm(float x, float y)
        {
            float s = 0, a = 0.5f;
            for (int i = 0; i < 4; i++) { s += a * Mathf.PerlinNoise(x, y); x *= 2.03f; y *= 2.03f; a *= 0.5f; }
            return s;
        }

        static float Wood(float u, float v, float ox, float oy)
        {
            // Long grain along v with gentle waviness, plus fine pores.
            float warp = Fbm(ox + u * 3f, oy + v * 0.6f) * 2.5f;
            float rings = Mathf.Sin((u * 26f + warp * 6f) * Mathf.PI);
            float grain = 0.82f + 0.12f * rings * rings;
            float pores = Mathf.PerlinNoise(ox + u * 220f, oy + v * 12f);
            return grain - 0.08f * pores + 0.06f * Fbm(ox + u * 8f, oy + v * 2f);
        }

        static float Marble(float u, float v, float ox, float oy)
        {
            float t = Fbm(ox + u * 4f, oy + v * 4f) * 6f;
            float vein = Mathf.Abs(Mathf.Sin((u * 3f + v * 2f + t) * Mathf.PI));
            vein = Mathf.Pow(vein, 0.25f);
            return 0.78f + 0.22f * vein - 0.05f * Fbm(ox + u * 30f, oy + v * 30f);
        }

        /// <summary>Alpha mask shapes used for square highlights.</summary>
        public enum Mark { Fill, Dot, Ring, Frame }

        public static Texture2D Marker(Mark kind, int size = 128)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float aa = 1.5f / size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size - 0.5f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float edge = 0.5f - Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
                    float a = kind switch
                    {
                        Mark.Fill => Mathf.Clamp01(edge / aa),
                        Mark.Dot => Mathf.Clamp01((0.16f - r) / aa),
                        Mark.Ring => Mathf.Clamp01((0.47f - r) / aa) * Mathf.Clamp01((r - 0.37f) / aa),
                        // Inner border of the square, so the outline reads as "this square" even without colour.
                        _ => Mathf.Clamp01(edge / aa) * Mathf.Clamp01((0.07f - edge) / aa),
                    };
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Rounded rectangle for 9-sliced UI panels.</summary>
        public static Sprite RoundedRect(int radius = 12)
        {
            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - c) - (c - radius), 0);
                    float dy = Mathf.Max(Mathf.Abs(y + 0.5f - c) - (c - radius), 0);
                    float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0,
                SpriteMeshType.FullRect, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
        }
    }
}
