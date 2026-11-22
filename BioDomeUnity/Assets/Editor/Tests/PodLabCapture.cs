using System.IO;
using BioDome.Diagnostics;
using BioDome.Experiment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Game-view screenshot run for the Pod Lab and plant condition colours (needs the Editor window, not -batchmode).
    /// Menu: BioDome > Tests > Capture Pod Lab Screenshots. Command line: -executeMethod BioDome.EditorTools.Tests.PodLabCapture.Run
    /// Output: Docs/Screenshots/PodLab (next to the Unity project folder).
    /// </summary>
    public static class PodLabCapture
    {
        private static string outDir;

        [MenuItem("BioDome/Tests/Capture Pod Lab Screenshots")]
        public static void RunFromMenu() { outDir = OutPath(); Begin(); }

        public static void Run()
        {
            outDir = OutPath();
            EditorSceneManager.OpenScene("Assets/Scenes/BioDome_Working.unity");
            Begin();
        }

        private static string OutPath() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Docs", "Screenshots", "PodLab"));

        private static void Begin()
        {
            EditorApplication.update -= WaitForScene;
            EditorApplication.update += WaitForScene;
            EditorApplication.isPlaying = true;
        }

        private static void WaitForScene()
        {
            if (!EditorApplication.isPlaying || Object.FindAnyObjectByType<ExperimentGarden>() == null) return;
            EditorApplication.update -= WaitForScene;
            new GameObject("PodLabCaptureRunner").AddComponent<PodLabCaptureRunner>().OutDir = outDir;
        }
    }
}
