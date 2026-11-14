using System;
using System.Collections.Generic;
using System.IO;
using BioDome.Plants;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Command-line screenshot tool, used to check plant geometry without opening the Editor window:
    /// Unity -batchmode -projectPath P -executeMethod BioDome.EditorTools.PlantPreviewBatch.Run
    ///       -previewOut DIR [-previewSpecies A,B] [-previewVariant N] [-previewDetail N]
    /// Writes one contact sheet per species (12%, 25%, 50%, 75%, 100%) and logs triangle counts. Changes no asset or scene.
    /// </summary>
    public static class PlantPreviewBatch
    {
        private const int CellWidth = 340;
        private const int CellHeight = 400;

        public static void Run()
        {
            string outDir = Arg("-previewOut") ?? "PlantPreviews";
            string speciesArg = Arg("-previewSpecies");
            int variant = int.TryParse(Arg("-previewVariant"), out int v) ? v : 0;
            int detail = int.TryParse(Arg("-previewDetail"), out int d) ? d : 1;
            var only = speciesArg == null ? null : new HashSet<string>(speciesArg.Split(','));

            bool useCodeDefaults = Arg("-previewDefaults") != null;
            float yaw = float.TryParse(Arg("-previewYaw"), out float y) ? y : 28f;

            Directory.CreateDirectory(outDir);
            int exitCode = 0;
            try
            {
                // Development helper (used on a scratch COPY of the project): create the species assets first.
                if (Arg("-previewCreate") != null) PlantSpeciesAssetBuilder.CreateAssets();

                foreach (GameObject prefab in PlantPreviewWindow.FindSpeciesPrefabs())
                {
                    if (only != null && !only.Contains(prefab.name)) continue;

                    float[] stages = PlantPreviewWindow.Stages;
                    var sheet = new Texture2D(CellWidth * stages.Length, CellHeight, TextureFormat.RGBA32, false);
                    var line = new System.Text.StringBuilder($"PREVIEW {prefab.name} variant {variant} detail {detail}:");

                    for (int s = 0; s < stages.Length; s++)
                    {
                        PlantPreviewRenderer.Result r = PlantPreviewRenderer.Render(prefab, stages[s], variant, CellWidth, CellHeight, detail, yaw, useCodeDefaults);
                        if (r == null || r.Image == null) { line.Append($" [g{stages[s]:0.00} FAILED]"); exitCode = 1; continue; }

                        sheet.SetPixels(s * CellWidth, 0, CellWidth, CellHeight, r.Image.GetPixels());
                        line.Append($" [g{stages[s]:0.00}: {r.Triangles} tris, {r.Vertices} verts, {r.Size.x:0.00}x{r.Size.y:0.00}x{r.Size.z:0.00} m]");
                        UnityEngine.Object.DestroyImmediate(r.Image);
                    }

                    sheet.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, $"{prefab.name}_v{variant}_y{Mathf.RoundToInt(yaw)}.png"), sheet.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(sheet);
                    Debug.Log(line.ToString());
                }
            }
            catch (Exception e)
            {
                Debug.LogError("PREVIEW FAILED: " + e);
                exitCode = 2;
            }
            EditorApplication.Exit(exitCode);
        }

        /// <summary>Value after a command-line flag, "" for a flag with no value, or null if the flag is absent.</summary>
        private static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs(); // fully qualified: BioDome.Environment is a project namespace
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != name) continue;
                return i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "";
            }
            return null;
        }
    }
}
