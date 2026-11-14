using System;
using System.IO;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Command-line composition check, meant to run on a scratch COPY of the project: it opens a scene, runs the lush plant plan,
    /// grows every plant to a chosen growth in memory and photographs the greenhouse from a few cameras. It never saves the scene.
    /// Unity -batchmode -projectPath COPY -executeMethod BioDome.EditorTools.PlantCompositionBatch.Run -compOut DIR [-compGrowth 1] [-compScene PATH]
    /// </summary>
    public static class PlantCompositionBatch
    {
        private const int Width = 1600;
        private const int Height = 900;

        private struct View
        {
            public string Name;
            public Vector3 Position, Target;
            public float Fov;
        }

        public static void Run()
        {
            string outDir = Arg("-compOut") ?? "PlantCompositions";
            string scenePath = Arg("-compScene") ?? "Assets/Scenes/BioDome_Main.unity";
            float growth = float.TryParse(Arg("-compGrowth"), out float g) ? g : 1f;
            int exitCode = 0;

            try
            {
                Directory.CreateDirectory(outDir);
                EditorSceneManager.OpenScene(scenePath);

                PlantSpeciesAssetBuilder.CreateAssets();
                PlantAdditionPlan plan = PlantSpeciesAssetBuilder.LoadOrCreateLushPlan();

                PlantAdditionBuilder.Report dry = PlantAdditionBuilder.Run(plan, new PlantAdditionBuilder.Options { DryRun = true });
                Debug.Log("COMPOSITION DRY RUN\n" + dry.Text);
                PlantAdditionBuilder.Report real = PlantAdditionBuilder.Run(plan, new PlantAdditionBuilder.Options());
                Debug.Log("COMPOSITION REAL RUN\n" + real.Text);
                if (real.Error != null || !real.OriginalsUnchanged) exitCode = 3;

                // A second run with the default options must add nothing: species already present are skipped.
                PlantAdditionBuilder.Report again = PlantAdditionBuilder.Run(plan, new PlantAdditionBuilder.Options());
                Debug.Log($"COMPOSITION RERUN: added {again.Added} (expected 0), originals unchanged: {again.OriginalsUnchanged}");

                int plants = 0;
                foreach (PlantGenerator generator in UnityEngine.Object.FindObjectsByType<PlantGenerator>())
                {
                    generator.SetGrowth(growth); // in memory only; the scene is never saved
                    plants++;
                }
                Debug.Log($"COMPOSITION: {plants} plants grown to {growth:0.00} for the photographs");

                var views = new[]
                {
                    new View { Name = "overview_west", Position = new Vector3(-19f, 8.5f, 0f), Target = new Vector3(-2f, 0.8f, 0f), Fov = 62f },
                    new View { Name = "overview_high", Position = new Vector3(-3f, 17f, -9f), Target = new Vector3(-3f, 0.5f, 3f), Fov = 60f },
                    new View { Name = "bed_N2_1", Position = new Vector3(-11.5f, 2.6f, 3.6f), Target = new Vector3(-11.5f, 0.9f, 8.4f), Fov = 62f },
                    new View { Name = "bed_N1_2_banana", Position = new Vector3(-3f, 2.4f, 0.4f), Target = new Vector3(-3f, 0.9f, 4.2f), Fov = 62f },
                    new View { Name = "border_N", Position = new Vector3(-6f, 2.0f, 6.5f), Target = new Vector3(-6f, 0.9f, 11.6f), Fov = 70f },
                };
                foreach (View view in views) Photograph(view, Path.Combine(outDir, view.Name + ".png"));
            }
            catch (Exception e)
            {
                Debug.LogError("COMPOSITION FAILED: " + e);
                exitCode = 2;
            }
            EditorApplication.Exit(exitCode);
        }

        private static void Photograph(View view, string path)
        {
            var go = new GameObject("CompositionCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = view.Fov;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 250f;
            camera.transform.position = view.Position;
            camera.transform.rotation = Quaternion.LookRotation(view.Target - view.Position, Vector3.up);

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(go);
        }

        private static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
