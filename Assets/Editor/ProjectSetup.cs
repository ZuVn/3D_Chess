using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChessGame.EditorTools
{
    /// <summary>
    /// Creates the generated project assets: material templates (so URP shader variants ship in builds)
    /// and the single bootstrap scene. Idempotent: run from the menu or with
    /// -executeMethod ChessGame.EditorTools.ProjectSetup.Run
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        const string MatDir = "Assets/Resources/Materials";

        [MenuItem("Chess/Setup Project")]
        public static void Run()
        {
            Directory.CreateDirectory(MatDir);
            Directory.CreateDirectory("Assets/Scenes");

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (lit == null || unlit == null) throw new System.Exception("URP shaders not found; is the URP package imported?");

            var litMat = LoadOrCreate($"{MatDir}/ChessLit.mat", lit);
            litMat.SetColor("_BaseColor", Color.white);
            litMat.SetFloat("_Smoothness", 0.5f);
            EditorUtility.SetDirty(litMat);

            var marker = LoadOrCreate($"{MatDir}/ChessMarker.mat", unlit);
            // Alpha-blended, no depth write, double sided.
            marker.SetFloat("_Surface", 1);
            marker.SetFloat("_Blend", 0);
            marker.SetFloat("_Cull", (float)CullMode.Off);
            marker.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            marker.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            marker.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            marker.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            marker.SetFloat("_ZWrite", 0);
            marker.SetOverrideTag("RenderType", "Transparent");
            marker.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            marker.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(marker);
            AssetDatabase.SaveAssets();

            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("App").AddComponent<App>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "Zn";
            PlayerSettings.productName = "Royal Gambit Chess";
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            // Vulkan first on Linux: on hybrid laptops it selects the discrete GPU, OpenGL stays as fallback.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLCore });
            PlayerSettings.SetApiCompatibilityLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            AssetDatabase.SaveAssets();
            Debug.Log("Project setup complete.");
        }

        static Material LoadOrCreate(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = shader;
            return m;
        }
    }
}
