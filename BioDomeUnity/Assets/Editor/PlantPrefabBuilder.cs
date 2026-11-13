using System.Collections.Generic;
using BioDome.Plants;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Menu commands to create a test Monstera in the open scene and to generate reusable Monstera prefabs.
    /// Meshes are saved as assets in Art/Models/Plants, prefabs in Prefabs/Plants, and every prefab is validated.
    /// </summary>
    public static class PlantPrefabBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs/Plants";
        private const string ModelFolder = "Assets/Art/Models/Plants";

        private struct Spec
        {
            public string Name;
            public float Height, Width, Growth, LeafLength, LeafWidth;
            public int LeafCount, Seed;
        }

        private static readonly Spec[] MonsteraSpecs =
        {
            new Spec { Name = "Monstera_Young",  Height = 1.0f, Width = 1.0f, Growth = 0.45f, LeafCount = 9, LeafLength = 0.45f, LeafWidth = 0.38f, Seed = 101 },
            new Spec { Name = "Monstera_Medium", Height = 1.2f, Width = 1.2f, Growth = 0.75f, LeafCount = 9, LeafLength = 0.50f, LeafWidth = 0.42f, Seed = 202 },
            new Spec { Name = "Monstera_Mature", Height = 1.4f, Width = 1.4f, Growth = 1.00f, LeafCount = 10, LeafLength = 0.55f, LeafWidth = 0.47f, Seed = 303 }
        };

        // --- Test plant -----------------------------------------------------------

        [MenuItem("BioDome/Plants/Create Test Monstera")]
        public static void CreateTestMonstera()
        {
            PlantAssetFactory.EnsureMaterials(false, out Material leaf, out Material stem);

            var go = new GameObject("Monstera_Test");
            Undo.RegisterCreatedObjectUndo(go, "Create Test Monstera");

            Vector3 position = Vector3.zero;
            if (SceneView.lastActiveSceneView != null)
            {
                position = SceneView.lastActiveSceneView.pivot;
                position.y = 0f;
            }
            go.transform.position = position;

            var generator = go.AddComponent<MonsteraGenerator>();
            generator.ApplyDefaultSettings();
            generator.SetMaterials(leaf, stem);
            generator.Rebuild(true);

            Selection.activeGameObject = go;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

            PlantMeshValidator.ValidateAndLog(go);
        }

        // --- Prefabs --------------------------------------------------------------

        [MenuItem("BioDome/Plants/Generate Monstera Prefabs")]
        public static void GenerateMonsteraPrefabs()
        {
            var existing = new List<string>();
            foreach (Spec spec in MonsteraSpecs)
            {
                string path = $"{PrefabFolder}/{spec.Name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) existing.Add(spec.Name);
            }
            if (existing.Count > 0 && !EditorUtility.DisplayDialog(
                    "Overwrite prefabs",
                    $"These prefabs already exist and will be regenerated:\n{string.Join("\n", existing)}\n\nChanges you made to them are lost.",
                    "Overwrite", "Cancel"))
            {
                return;
            }

            GreenhouseAssetFactory.EnsureFolder(PrefabFolder);
            GreenhouseAssetFactory.EnsureFolder(ModelFolder);
            PlantAssetFactory.EnsureMaterials(false, out Material leaf, out Material stem);

            int failures = 0;
            try
            {
                foreach (Spec spec in MonsteraSpecs)
                {
                    EditorUtility.DisplayProgressBar("BioDome", $"Generating {spec.Name}...", 0.5f);
                    GameObject prefab = BuildPrefab(spec, leaf, stem);
                    if (!PlantMeshValidator.ValidateAndLog(prefab)) failures++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"BioDome: generated {MonsteraSpecs.Length} Monstera prefabs in {PrefabFolder} ({failures} failed validation).");
        }

        private static GameObject BuildPrefab(Spec spec, Material leaf, Material stem)
        {
            var go = new GameObject(spec.Name);
            try
            {
                var generator = go.AddComponent<MonsteraGenerator>();
                generator.ApplyDefaultSettings();
                generator.Configure(spec.Height, spec.Width, spec.LeafCount, spec.LeafLength, spec.LeafWidth, spec.Growth, spec.Seed);
                generator.SetMaterials(leaf, stem);

                // Build straight into a mesh asset so the prefab keeps a real asset reference.
                Mesh asset = LoadOrCreateMeshAsset($"{ModelFolder}/{spec.Name}_Mesh.asset");
                generator.Rebuild(true, asset);
                EditorUtility.SetDirty(asset);

                string prefabPath = $"{PrefabFolder}/{spec.Name}.prefab";
                return PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static Mesh LoadOrCreateMeshAsset(string path)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            // The main object of an asset must be named after its file, or Unity logs a warning on import.
            string expectedName = System.IO.Path.GetFileNameWithoutExtension(path);
            if (mesh != null)
            {
                if (mesh.name != expectedName)
                {
                    mesh.name = expectedName;
                    EditorUtility.SetDirty(mesh);
                }
                return mesh;
            }

            mesh = new Mesh { name = expectedName };
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // --- Materials ------------------------------------------------------------

        [MenuItem("BioDome/Plants/Reset Plant Materials To Defaults")]
        public static void ResetPlantMaterials()
        {
            if (!EditorUtility.DisplayDialog(
                    "Reset plant materials",
                    "Restores the default leaf and stem materials and the leaf vein texture. Edits you made to them are overwritten.",
                    "Reset", "Cancel"))
            {
                return;
            }

            PlantAssetFactory.EnsureMaterials(true, out _, out _);
            AssetDatabase.SaveAssets();
            Debug.Log("BioDome: plant materials reset to defaults.");
        }
    }
}
