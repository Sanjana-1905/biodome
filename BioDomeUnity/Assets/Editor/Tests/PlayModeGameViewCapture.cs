using System.IO;
using BioDome.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Repeatable Play Mode capture of the visual states Stage 2 needs evidence for (healthy, wilted, recovering, flowering, fading, sprinkler delivering,
    /// controller on but delivering almost nothing). Uses the scene's own Game camera, UI and inspection panel and writes true Game-view PNGs.
    /// Menu: BioDome > Tests > Capture Stage 2 Game View Screenshots (interactive: watch it run in the Game view).
    /// Command line: -executeMethod BioDome.EditorTools.Tests.PlayModeGameViewCapture.Run -shotOut DIR   (graphics on: do not pass -nographics)
    /// </summary>
    public static class PlayModeGameViewCapture
    {
        private static string outDir;

        [MenuItem("BioDome/Tests/Capture Stage 2 Game View Screenshots")]
        public static void RunFromMenu()
        {
            outDir = Path.GetFullPath("Docs/Screenshots/Stage2_GameView");
            Begin();
        }

        public static void Run()
        {
            outDir = Path.GetFullPath(Arg("-shotOut") ?? "Stage2_GameView");
            EditorSceneManager.OpenScene("Assets/Scenes/BioDome_Working.unity");
            Begin();
        }

        private static void Begin()
        {
            EditorApplication.update -= WaitForScene;
            EditorApplication.update += WaitForScene;
            EditorApplication.isPlaying = true;
        }

        private static void WaitForScene()
        {
            if (!EditorApplication.isPlaying) return;
            var manager = Object.FindFirstObjectByType<GreenhouseManager>();
            if (manager == null || manager.GetComponent<GameViewCaptureRunner>() != null) return;
            EditorApplication.update -= WaitForScene;
            var host = new GameObject("GameViewCaptureRunner");
            host.AddComponent<GameViewCaptureRunner>().OutDir = outDir;
        }

        private static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
