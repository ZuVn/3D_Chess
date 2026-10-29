using UnityEngine;

namespace ChessGame
{
    public enum Sound { Move, Capture, Castle, Check, GameOver, Click, Illegal, Promote }

    /// <summary>Small synthesised sound set (no audio files to license or go missing).</summary>
    public sealed class Sfx : MonoBehaviour
    {
        AudioSource _src;
        AudioClip[] _clips;
        public float Volume = 0.8f;

        const int Rate = 44100;

        void Awake()
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0;
            _clips = new[]
            {
                Make("move", 0.12f, t => Knock(t, 520f, 38f) + 0.35f * Noise(t, 90f)),
                Make("capture", 0.2f, t => Knock(t, 380f, 30f) + 0.8f * Knock(t - 0.055f, 300f, 26f) + 0.4f * Noise(t, 60f)),
                Make("castle", 0.24f, t => Knock(t, 520f, 38f) + Knock(t - 0.11f, 470f, 38f)),
                Make("check", 0.45f, t => Bell(t, 880f, 7f) * 0.6f + Bell(t, 1320f, 9f) * 0.3f),
                Make("gameover", 1.2f, t => 0.35f * (Bell(t, 523.25f, 2.5f) + Bell(t - 0.12f, 659.25f, 2.5f) + Bell(t - 0.24f, 783.99f, 2.2f))),
                Make("click", 0.05f, t => Bell(t, 1800f, 90f) * 0.4f),
                Make("illegal", 0.16f, t => 0.25f * Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 140f * t)) * Mathf.Exp(-t * 18f)),
                Make("promote", 0.6f, t => 0.4f * (Bell(t, 659.25f, 5f) + Bell(t - 0.09f, 987.77f, 5f))),
            };
        }

        static float Knock(float t, float f, float decay) =>
            t < 0 ? 0 : Mathf.Sin(2 * Mathf.PI * f * t * (1 - 0.3f * t)) * Mathf.Exp(-t * decay) * 0.8f;

        static float Bell(float t, float f, float decay) => t < 0 ? 0 : Mathf.Sin(2 * Mathf.PI * f * t) * Mathf.Exp(-t * decay);

        static uint _seed = 12345;
        static float Noise(float t, float decay)
        {
            if (t < 0) return 0;
            _seed = _seed * 1664525u + 1013904223u;
            return ((_seed >> 9) / (float)(1 << 23) * 2f - 1f) * Mathf.Exp(-t * decay);
        }

        static AudioClip Make(string name, float seconds, System.Func<float, float> f)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float fade = Mathf.Clamp01((seconds - t) / 0.02f); // avoid a click at the end
                data[i] = Mathf.Clamp(f(t) * fade, -1f, 1f) * 0.7f;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public void Play(Sound s)
        {
            if (_clips == null || Volume <= 0) return;
            _src.PlayOneShot(_clips[(int)s], Volume);
        }
    }
}
