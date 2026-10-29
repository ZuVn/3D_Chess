using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Chess;
using UnityEngine;
using UnityEngine.Profiling;

namespace ChessGame
{
    /// <summary>
    /// Command-line driven self test for real builds:
    ///   -benchmark &lt;seconds&gt; -benchmark-out &lt;file&gt;   plays a game vs the Hard AI while orbiting the camera,
    ///                                                toggling 2D/3D, and records frame times and memory.
    ///   -shots &lt;dir&gt;                                    captures screenshots of the main screens.
    /// Optional: -quality 0|1|2, -vsync 0|1, -fpslimit N (0 = unlimited) override settings for the run only.
    /// </summary>
    public sealed class Benchmark : MonoBehaviour
    {
        App _app;

        public static void Attach(App app)
        {
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("-benchmark") && !args.Contains("-shots")) return;
            var b = app.gameObject.AddComponent<Benchmark>();
            b._app = app;
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        IEnumerator Start()
        {
            // Never persist benchmark/screenshot overrides into the player's real settings or autosave.
            Storage.RootOverride = Path.Combine(Application.temporaryCachePath, "selftest");
            if (int.TryParse(Arg("-fpslimit"), out int fl)) _app.Settings.FrameRateLimit = fl;
            if (int.TryParse(Arg("-quality"), out int q)) _app.Settings.Quality = Mathf.Clamp(q, 0, 2);
            if (int.TryParse(Arg("-vsync"), out int vs)) _app.Settings.VSync = vs != 0;
            _app.ApplySettings();
            yield return new WaitForSeconds(1.5f);

            string shots = Arg("-shots");
            if (shots != null) yield return Shots(shots);
            if (float.TryParse(Arg("-benchmark"), NumberStyles.Float, CultureInfo.InvariantCulture, out float secs))
                yield return Run(secs, Arg("-benchmark-out") ?? Path.Combine(Application.persistentDataPath, "benchmark.txt"));
            Application.Quit();
        }

        IEnumerator Shot(string dir, string name)
        {
            yield return new WaitForSeconds(0.9f);
            yield return new WaitForEndOfFrame();
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return null;
        }

        void PlayUci(params string[] moves)
        {
            foreach (var u in moves)
            {
                var m = _app.Session.Game.Position.ParseUci(u);
                if (!m.IsNone) _app.Session.TryHumanMove(m);
            }
        }

        IEnumerator Shots(string dir)
        {
            yield return Shot(dir, "01-main-menu");

            _app.StartNewGame(GameMode.Local, Side.White, Difficulty.Easy, 0, null, new TimeControl(300, 3));
            _app.Settings.AutoRotate = false;
            PlayUci("e2e4", "e7e5", "g1f3", "b8c6", "f1c4", "g8f6");
            yield return new WaitForSeconds(1f);
            yield return Shot(dir, "02-game-3d");

            // Selection with legal-move markers.
            _app.Board.SetHighlights(_app.Session.Game.History.Last().Move, Sq.Parse("f3"), _app.Session.Game.LegalMoves, true, Sq.None, Sq.None, Sq.None);
            yield return Shot(dir, "03-selection");
            _app.RefreshHighlights();

            _app.ToggleView();
            yield return new WaitForSeconds(1f);
            yield return Shot(dir, "04-game-2d");
            _app.Rig.RotateBy(180);
            yield return new WaitForSeconds(1f);
            yield return Shot(dir, "04b-2d-rotated");
            _app.Settings.PieceTheme = 3;
            _app.ApplySettings();
            yield return Shot(dir, "04c-2d-gold-silver");
            _app.Settings.PieceTheme = 1;
            _app.ApplySettings();
            yield return Shot(dir, "04d-2d-wood");
            _app.Settings.PieceTheme = 0;
            _app.ApplySettings();
            _app.ResetCamera();
            _app.ToggleView();
            yield return new WaitForSeconds(1f);

            _app.Rig.RotateBy(180);
            yield return new WaitForSeconds(1f);
            yield return Shot(dir, "05-black-side");
            _app.ResetCamera();

            int[] themes = { 1, 2, 3 };
            foreach (int t in themes)
            {
                _app.Settings.BoardTheme = t;
                _app.Settings.PieceTheme = t;
                _app.Settings.Environment = t % Themes.Environments.Length;
                _app.ApplySettings();
                yield return Shot(dir, $"06-theme-{t}");
            }
            _app.Settings.BoardTheme = _app.Settings.PieceTheme = _app.Settings.Environment = 0;
            _app.ApplySettings();

            // Castling and capture, then Scholar's-mate style finish for the game-over dialog.
            PlayUci("e1g1", "f6e4", "f3e5", "c6e5");
            yield return new WaitForSeconds(1.5f);
            yield return Shot(dir, "07-castled-captures");

            _app.Session.Restart();
            PlayUci("e2e4", "e7e5", "d1h5", "b8c6", "f1c4", "g8f6", "h5f7");
            yield return new WaitForSeconds(2.5f);
            yield return Shot(dir, "08-checkmate");
            _app.Hud.HideGameOver();

            _app.Session.Start(GameMode.Local, Side.White, Difficulty.Easy, 0, new Game("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1"));
            yield return new WaitForSeconds(1f);
            _app.Hud.AskPromotion(Side.White, _ => { });
            yield return Shot(dir, "09-promotion");
            _app.Hud.CancelPromotion();

            _app.OpenSettings();
            yield return Shot(dir, "10-settings");
            _app.Menus.CloseAll();
            _app.ReturnToMenu();
            _app.Menus.Back();
            yield return new WaitForSeconds(0.5f);
        }

        IEnumerator Run(float seconds, string outPath)
        {
            var difficulty = int.TryParse(Arg("-difficulty"), out int dl) ? (Difficulty)Mathf.Clamp(dl, 0, 2) : Difficulty.Hard;
            _app.StartNewGame(GameMode.VsAI, Side.White, difficulty, 1000);
            var human = new Chess.AI.Searcher(16, seed: 7);
            int frames = 0, capacity = Mathf.CeilToInt(seconds * 1000);
            var dts = new float[capacity];
            long monoStart = Profiler.GetMonoUsedSizeLong();
            long totalStart = Profiler.GetTotalAllocatedMemoryLong();
            int gcStart = GC.CollectionCount(0);
            int moves = 0, games = 1, aiMoves = 0;
            AiMove lastAi = null;
            float t = 0, nextToggle = 8f;
            yield return new WaitForSeconds(1f);

            while (t < seconds && frames < capacity)
            {
                float dt = Time.unscaledDeltaTime;
                dts[frames++] = dt;
                t += dt;
                _app.Rig.Orbit(new Vector2(60f * dt, 0));
                if (t > nextToggle) { _app.ToggleView(); nextToggle += 8f; }

                var s = _app.Session;
                if (s.LastAi != null && s.LastAi != lastAi) { lastAi = s.LastAi; aiMoves++; }
                if (s.Game.IsOver)
                {
                    _app.Hud.HideGameOver();
                    s.Restart();
                    games++;
                }
                else if (s.IsHumanTurn && !_app.Board.IsAnimating)
                {
                    var r = human.Search(s.Game.Position.Clone(), s.Game.KeyHistory,
                        new Chess.AI.SearchLimits { MaxDepth = 1, TimeMs = 50, FullEval = false, RandomMarginCp = 60 });
                    if (s.TryHumanMove(r.Best)) moves++;
                }
                yield return null;
            }

            var sorted = dts.Take(frames).OrderBy(x => x).ToArray();
            float avg = sorted.Average();
            float P(float p) => sorted[Mathf.Clamp((int)(p * (sorted.Length - 1)), 0, sorted.Length - 1)];
            float onePctLowFps = 1f / sorted.Skip((int)(sorted.Length * 0.99f)).DefaultIfEmpty(sorted.Last()).Average();
            var sb = new StringBuilder();
            sb.AppendLine($"date: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"version: {Application.version} ({Application.platform}, {(Debug.isDebugBuild ? "development" : "release")})");
            sb.AppendLine($"resolution: {UnityEngine.Screen.width}x{UnityEngine.Screen.height} {UnityEngine.Screen.fullScreenMode}");
            sb.AppendLine($"quality: {new[] { "Low", "Medium", "High" }[_app.Settings.Quality]}, vsync: {_app.Settings.VSync}, targetFrameRate: {Application.targetFrameRate}");
            sb.AppendLine($"cpu: {SystemInfo.processorType} ({SystemInfo.processorCount} threads)");
            sb.AppendLine($"gpu: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsMemorySize} MB)");
            sb.AppendLine($"os: {SystemInfo.operatingSystem}, ram: {SystemInfo.systemMemorySize} MB");
            sb.AppendLine($"duration: {t:0.0} s, frames: {frames}, games: {games}, human moves: {moves}, AI moves: {aiMoves}");
            sb.AppendLine($"AI: {difficulty}, engine: {lastAi?.Engine ?? "-"}, last AI think: {lastAi?.Ms ?? 0} ms, depth {lastAi?.Depth ?? 0}");
            sb.AppendLine($"avg fps: {1f / avg:0.0} (avg frame {avg * 1000:0.00} ms)");
            sb.AppendLine($"median frame: {P(0.5f) * 1000:0.00} ms, p95: {P(0.95f) * 1000:0.00} ms, p99: {P(0.99f) * 1000:0.00} ms, max: {sorted.Last() * 1000:0.00} ms");
            sb.AppendLine($"1% low fps: {onePctLowFps:0.0}");
            sb.AppendLine($"frames > 16.7 ms: {sorted.Count(x => x > 1f / 60f)}, > 33.3 ms: {sorted.Count(x => x > 1f / 30f)}");
            sb.AppendLine($"mono heap: {monoStart / 1048576f:0.0} MB -> {Profiler.GetMonoUsedSizeLong() / 1048576f:0.0} MB");
            sb.AppendLine($"total allocated: {totalStart / 1048576f:0.0} MB -> {Profiler.GetTotalAllocatedMemoryLong() / 1048576f:0.0} MB");
            sb.AppendLine($"gen0 GCs during run: {GC.CollectionCount(0) - gcStart}");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("Benchmark results:\n" + sb);
        }
    }
}
