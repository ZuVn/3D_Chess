using System;
using System.IO;
using UnityEngine;

namespace ChessGame
{
    /// <summary>Persistent user preferences, stored as JSON in Application.persistentDataPath.</summary>
    [Serializable]
    public class Settings
    {
        public int Version = 1;

        // Display
        public int ResolutionWidth;   // 0 = leave as is
        public int ResolutionHeight;
        public int DisplayMode;       // 0 windowed, 1 borderless fullscreen, 2 exclusive fullscreen
        public bool VSync = true;
        public int FrameRateLimit = 60; // used when VSync is off; 0 = unlimited
        public int Quality = 1;         // 0 low, 1 medium, 2 high
        public bool ShowFps;

        // Audio
        public float MasterVolume = 0.8f;
        public float SfxVolume = 0.8f;
        public bool Muted;

        // Camera / presentation
        public float CameraSensitivity = 1f;
        public bool Default2D;
        public bool AutoRotate = true;
        public bool Animations = true;
        public float AnimationSpeed = 1f;
        public int BoardTheme;
        public int PieceTheme;
        public int Environment;

        // Gameplay aids
        public bool ShowLegalMoves = true;
        public bool ShowCoordinates = true;
        public bool ShowMoveHistory = true;
        public bool ShowEval = true;

        // Last used new-game options
        public int AiDifficulty = 1;   // 0 easy, 1 hard, 2 professional
        public int HumanColor;         // 0 white, 1 black, 2 random
        public int ProThinkMs = 2000;
        public int TimeControl = 4;    // index into TimeControls

        public static readonly int[] FrameRateOptions = { 30, 60, 75, 120, 144, 165, 240, 0 };
        public static readonly int[] ProThinkOptions = { 1000, 2000, 5000, 10000 };
        public static readonly Chess.TimeControl[] TimeControls =
        {
            new Chess.TimeControl(0, 0), new Chess.TimeControl(60, 0), new Chess.TimeControl(180, 2), new Chess.TimeControl(300, 0),
            new Chess.TimeControl(600, 0), new Chess.TimeControl(900, 10), new Chess.TimeControl(1800, 0),
        };
        public static readonly string[] TimeControlNames =
            { "No clock", "1 + 0  Bullet", "3 + 2  Blitz", "5 + 0  Blitz", "10 + 0  Rapid", "15 + 10  Rapid", "30 + 0  Classical" };

        static string FilePath => Path.Combine(Storage.Dir, "settings.json");

        public void Validate()
        {
            DisplayMode = Mathf.Clamp(DisplayMode, 0, 2);
            if (ResolutionWidth < 640 || ResolutionHeight < 480 || ResolutionWidth > 16384 || ResolutionHeight > 16384)
                ResolutionWidth = ResolutionHeight = 0;
            if (Array.IndexOf(FrameRateOptions, FrameRateLimit) < 0) FrameRateLimit = 60;
            Quality = Mathf.Clamp(Quality, 0, 2);
            MasterVolume = Clamp01(MasterVolume, 0.8f);
            SfxVolume = Clamp01(SfxVolume, 0.8f);
            CameraSensitivity = float.IsNaN(CameraSensitivity) ? 1f : Mathf.Clamp(CameraSensitivity, 0.2f, 3f);
            AnimationSpeed = float.IsNaN(AnimationSpeed) ? 1f : Mathf.Clamp(AnimationSpeed, 0.5f, 2f);
            BoardTheme = Mathf.Clamp(BoardTheme, 0, Themes.Boards.Length - 1);
            PieceTheme = Mathf.Clamp(PieceTheme, 0, Themes.Pieces.Length - 1);
            Environment = Mathf.Clamp(Environment, 0, Themes.Environments.Length - 1);
            AiDifficulty = Mathf.Clamp(AiDifficulty, 0, 2);
            HumanColor = Mathf.Clamp(HumanColor, 0, 2);
            if (Array.IndexOf(ProThinkOptions, ProThinkMs) < 0) ProThinkMs = 2000;
            TimeControl = Mathf.Clamp(TimeControl, 0, TimeControls.Length - 1);
        }

        static float Clamp01(float v, float fallback) => float.IsNaN(v) ? fallback : Mathf.Clamp01(v);

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonUtility.FromJson<Settings>(File.ReadAllText(FilePath));
                    if (s != null) { s.Validate(); return s; }
                }
            }
            catch (Exception e) { Debug.LogWarning($"Settings file unreadable, using defaults: {e.Message}"); }
            // First run: integrated / low-memory GPUs start on Low so the default stays smooth.
            var d = new Settings { Quality = SystemInfo.graphicsMemorySize > 0 && SystemInfo.graphicsMemorySize < 2048 ? 0 : 1 };
            d.Validate();
            return d;
        }

        public void Save()
        {
            Validate();
            SafeFile.WriteAllText(FilePath, JsonUtility.ToJson(this, true), json => JsonUtility.FromJson<Settings>(json) != null);
        }

        /// <summary>Applies display and frame pacing options (quality and audio are applied by their owners).</summary>
        public void ApplyDisplay()
        {
            var mode = DisplayMode switch
            {
                1 => FullScreenMode.FullScreenWindow,
                2 => FullScreenMode.ExclusiveFullScreen,
                _ => FullScreenMode.Windowed,
            };
            if (ResolutionWidth > 0) Screen.SetResolution(ResolutionWidth, ResolutionHeight, mode);
            else if (Screen.fullScreenMode != mode)
            {
                if (mode == FullScreenMode.Windowed) Screen.SetResolution(1600, 900, mode);
                else Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, mode);
            }
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync ? -1 : (FrameRateLimit == 0 ? -1 : FrameRateLimit);
        }
    }

    /// <summary>Where settings and saves live. Tests point this at a temporary folder.</summary>
    public static class Storage
    {
        public static string RootOverride;
        public static string Dir => RootOverride ?? Application.persistentDataPath;
    }

    /// <summary>Write-to-temp, validate, then replace: an interrupted or bad write never clobbers the previous file.</summary>
    public static class SafeFile
    {
        public static bool WriteAllText(string path, string contents, Func<string, bool> validate)
        {
            string tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(tmp, contents);
                if (!validate(File.ReadAllText(tmp))) throw new InvalidDataException("validation failed");
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not save {Path.GetFileName(path)}: {e.Message}");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
                return false;
            }
        }
    }
}
