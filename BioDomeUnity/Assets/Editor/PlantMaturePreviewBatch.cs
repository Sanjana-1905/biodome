using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Command-line check of the mature-growth preview, meant for a scratch COPY of the project. It opens a scene, records what every plant
    /// looks like in the saved data, switches the preview on, photographs several angles, switches it off, and verifies that nothing changed
    /// (no plant value, no mesh reference, scene not dirty, scene file bytes identical). The scene is never saved.
    /// Unity -batchmode -projectPath COPY -executeMethod BioDome.EditorTools.PlantMaturePreviewBatch.Run -matOut DIR [-matScene PATH]
    /// </summary>
    public static class PlantMaturePreviewBatch
    {
        public static void Run()
        {
            string outDir = Arg("-matOut") ?? "MaturePreview";
            string scenePath = Arg("-matScene") ?? "Assets/BioDome-Diverse.unity";
            int exit = 0;
            try
            {
                Directory.CreateDirectory(outDir);
                var scene = EditorSceneManager.OpenScene(scenePath);
                string fileHashBefore = Hash(scenePath);

                string before = Snapshot();
                bool dirtyBefore = scene.isDirty;
                Debug.Log($"MATUREPREVIEW scene '{scene.name}' opened. dirty={dirtyBefore}. snapshot={before.GetHashCode()}");

                // Angles with the preview OFF (to show what the greenhouse normally looks like) ...
                List<PlantMaturePreview.ViewPreset> all = PlantMaturePreview.BuildViews();
                var chosen = new List<PlantMaturePreview.ViewPreset>();
                foreach (var v in all)
                    if (v.Name.StartsWith("Overview") || v.Name == "Bed Bed_N2_1" || v.Name == "Bed Bed_N1_2" || v.Name == "Bed Bed_S1_1" || v.Name == "Bed Border_N")
                        chosen.Add(v);

                var normal = new List<PlantMaturePreview.ViewPreset>();
                foreach (var v in chosen)
                    if (v.Name == "Bed Bed_N2_1" || v.Name.StartsWith("Overview from the west")) { var c = v; c.Name = "OFF_" + v.Name; normal.Add(c); }
                PlantMaturePreview.SaveScreenshots(outDir, normal);

                // ... then ON.
                bool on = PlantMaturePreview.SetActive(true);
                Debug.Log($"MATUREPREVIEW on={on} active={PlantMaturePreview.Active} plants={PlantMaturePreview.PlantCount} meshes={PlantMaturePreview.MeshCount} tris={PlantMaturePreview.TriangleCount} sceneUntouched={PlantMaturePreview.SceneUntouched}");

                string during = Snapshot();
                bool dirtyDuring = scene.isDirty;
                int shots = PlantMaturePreview.SaveScreenshots(outDir, chosen);
                Debug.Log($"MATUREPREVIEW wrote {shots} preview PNGs");

                PlantMaturePreview.SetActive(false);
                string after = Snapshot();
                bool dirtyAfter = scene.isDirty;
                string fileHashAfter = Hash(scenePath);

                bool same = before == during && before == after;
                bool clean = dirtyBefore == dirtyDuring && dirtyBefore == dirtyAfter;
                bool freed = PlantMaturePreview.MeshCount == 0 && !PlantMaturePreview.Active;
                bool fileSame = fileHashBefore == fileHashAfter;
                Debug.Log($"MATUREPREVIEW CHECKS plantDataUnchanged={same} sceneDirtyUnchanged={clean} (before={dirtyBefore} during={dirtyDuring} after={dirtyAfter}) " +
                          $"tempMeshesFreed={freed} sceneFileBytesIdentical={fileSame}");
                if (!(same && clean && freed && fileSame && on)) exit = 3;
            }
            catch (Exception e)
            {
                Debug.LogError("MATUREPREVIEW FAILED: " + e);
                exit = 2;
            }
            EditorApplication.Exit(exit);
        }

        /// <summary>Everything the preview must not change: every plant's saved growth, seed, size, mesh references and transform.</summary>
        private static string Snapshot()
        {
            var parts = new List<string>();
            foreach (PlantGenerator g in UnityEngine.Object.FindObjectsByType<PlantGenerator>())
            {
                var filter = g.Renderer != null ? g.Renderer.GetComponent<MeshFilter>() : null;
                Transform t = g.transform;
                parts.Add($"{g.name}|{g.Growth01:F4}|{g.Seed}|{(g.BuiltMesh != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(g.BuiltMesh) : 0)}|" +
                          $"{(filter != null && filter.sharedMesh != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(filter.sharedMesh) : 0)}|{t.position:F3}|{t.localScale:F3}|{t.eulerAngles.y:F2}");
            }
            parts.Sort(string.CompareOrdinal);
            return string.Join("\n", parts);
        }

        private static string Hash(string path)
        {
            using (var sha = SHA1.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        private static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
