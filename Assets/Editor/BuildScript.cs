using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace ChessGame.EditorTools
{
    /// <summary>
    /// Command-line builds:
    ///   Unity -batchmode -quit -projectPath . -executeMethod ChessGame.EditorTools.BuildScript.BuildLinux
    ///   Unity -batchmode -quit -projectPath . -executeMethod ChessGame.EditorTools.BuildScript.BuildWindows
    /// Output goes to Builds/&lt;Platform&gt;/. Stockfish (if present in ThirdParty/) is copied next to the game data.
    /// </summary>
    public static class BuildScript
    {
        const string Exe = "RoyalGambit";

        [MenuItem("Chess/Build Linux x86-64")]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Linux", Exe + ".x86_64", "linux", "stockfish");

        [MenuItem("Chess/Build Windows x86-64")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Windows", Exe + ".exe", "windows", "stockfish.exe");

        public static void BuildAll()
        {
            BuildLinux();
            BuildWindows();
        }

        static void Build(BuildTarget target, string folder, string exeName, string sfOs, string sfName)
        {
            ProjectSetup.Run();
            string outDir = Path.GetFullPath(Path.Combine("Builds", folder));
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = Path.Combine(outDir, exeName),
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"{folder} build failed: {report.summary.result}, {report.summary.totalErrors} errors");
                if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // Unity's debug-symbol folder must not be distributed.
            string symbols = Path.Combine(outDir, Exe + "_BackUpThisFolder_ButDontShipItWithYourGame");
            if (Directory.Exists(symbols)) Directory.Delete(symbols, true);

            string data = Path.Combine(outDir, Exe + "_Data");
            string sfDir = Path.Combine(data, "StreamingAssets", "Stockfish");
            string src = Path.Combine("ThirdParty", "Stockfish");
            bool sf = File.Exists(Path.Combine(src, sfOs, sfName));
            if (sf)
            {
                Directory.CreateDirectory(sfDir);
                File.Copy(Path.Combine(src, sfOs, sfName), Path.Combine(sfDir, sfName), true);
                File.Copy(Path.Combine(src, "COPYING.txt"), Path.Combine(sfDir, "COPYING.txt"), true);
                File.Copy(Path.Combine(src, "AUTHORS"), Path.Combine(sfDir, "AUTHORS.txt"), true);
                File.Copy(Path.Combine(src, "SOURCE.txt"), Path.Combine(sfDir, "SOURCE.txt"), true);
                if (sfOs == "linux") Chmod(Path.Combine(sfDir, sfName));
            }
            else Debug.LogWarning($"Stockfish not found in {src}/{sfOs}; Professional will fall back to the built-in engine. Run Tools/fetch_stockfish.sh.");

            File.Copy("THIRD_PARTY_NOTICES.md", Path.Combine(outDir, "THIRD_PARTY_NOTICES.md"), true);
            File.WriteAllText(Path.Combine(outDir, "BUILD_INFO.txt"),
                $"Royal Gambit Chess {PlayerSettings.bundleVersion}\n" +
                $"Platform: {folder} x86-64 ({target})\n" +
                $"Built: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC with Unity {UnityEngine.Application.unityVersion}\n" +
                $"Stockfish included: {(sf ? "yes (Stockfish 19, GPLv3, see " + Exe + "_Data/StreamingAssets/Stockfish/)" : "no")}\n" +
                $"Run: {(target == BuildTarget.StandaloneLinux64 ? "./" + exeName : exeName)}\n");
            if (target == BuildTarget.StandaloneLinux64) Chmod(Path.Combine(outDir, exeName));
            Debug.Log($"{folder} build succeeded: {outDir} ({report.summary.totalSize / 1048576} MB)");
        }

        static void Chmod(string path)
        {
            if (Environment.OSVersion.Platform != PlatformID.Unix) return;
            using var p = Process.Start(new ProcessStartInfo("chmod", $"+x \"{path}\"") { UseShellExecute = false });
            p?.WaitForExit();
        }
    }
}
