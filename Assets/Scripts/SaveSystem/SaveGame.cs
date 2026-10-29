using System;
using System.IO;
using Chess;
using UnityEngine;

namespace ChessGame
{
    /// <summary>An unfinished game: start FEN plus UCI moves, replayed through the rules engine on load.</summary>
    [Serializable]
    public class SavedGame
    {
        public int Version = 1;
        public int Mode;          // GameMode
        public int HumanColor;    // Side
        public int Difficulty;    // Difficulty
        public int ProThinkMs;
        public int ClockBase, ClockIncrement;   // seconds; 0 = no clock
        public long WhiteMs = -1, BlackMs = -1;
        public string StartFen;
        public string[] Moves;
        public string SavedAt;

        static string FilePath => Path.Combine(Storage.Dir, "savegame.json");

        /// <summary>Returns a validated save or null (missing, corrupt, or containing illegal moves).</summary>
        public static SavedGame Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var s = JsonUtility.FromJson<SavedGame>(File.ReadAllText(FilePath));
                return s != null && s.IsValid() ? s : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Ignoring unreadable save: {e.Message}");
                return null;
            }
        }

        bool IsValid()
        {
            if (Version != 1 || Mode is < 0 or > 1 || HumanColor is < 0 or > 1 || Difficulty is < 0 or > 2) return false;
            try { var g = ToGame(); return !g.IsOver; }
            catch (FormatException) { return false; }
        }

        public Game ToGame() => Game.FromUciMoves(StartFen, Moves ?? Array.Empty<string>());

        public void Save()
        {
            SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            SafeFile.WriteAllText(FilePath, JsonUtility.ToJson(this), json =>
            {
                var s = JsonUtility.FromJson<SavedGame>(json);
                return s != null && s.IsValid();
            });
        }

        public static void Delete()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e) { Debug.LogWarning($"Could not delete save: {e.Message}"); }
        }
    }
}
