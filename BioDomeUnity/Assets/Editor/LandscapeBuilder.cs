using System.Collections.Generic;
using System.Text;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Irrigation;
using BioDome.Plants;
using BioDome.Procedural;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BioDome.EditorTools
{
    /// <summary>
    /// The living landscape between and around the beds, under "BioDome_Landscape": planted clusters around the fountain plaza, in the entrance pockets,
    /// along the main path and around the research wing; climbing vines on the wall pillars; benches and resting places; lilies on the fountain.
    /// All plants are simulated, mature and on their own irrigation groups. Does not save the scene (ConservatorySetup does, after its checks).
    /// </summary>
    public static class LandscapeBuilder
    {
        public const string RootName = "BioDome_Landscape";

        private struct Cluster
        {
            public string Group, Recipe;
            public Vector2 Centre;
            public float Radius;
            public int Rocks;
        }

        private static Cluster C(string group, float x, float z, float r, int rocks, string recipe) =>
            new Cluster { Group = group, Centre = new Vector2(x, z), Radius = r, Rocks = rocks, Recipe = recipe };

        private static List<Cluster> BuildClusterList()
        {
            var list = new List<Cluster>();

            // Fountain plaza surround: six compositions outside the plaza kerb, leaving the benches, the west path and the east path clear.
            list.Add(C("Plaza", 17.0f, 6.0f, 1.4f, 2, "ArecaPalm:1@0-0.3, BirdOfParadise:1@0.2-0.55, Calathea:3@0.4-0.8, BostonFern:3@0.4-0.85, CreepingGroundCover:6@0.7-1.15c, Moss:3@0.7-1.1c"));
            list.Add(C("Plaza", 13.8f, 6.8f, 1.5f, 3, "Banana:2@0-0.45, Philodendron:1@0.4-0.7, Anthurium:3@0.4-0.8, Orchid:2@0.5-0.8, Moss:5@0.7-1.1c, CreepingGroundCover:5@0.7-1.15c"));
            list.Add(C("Plaza", 10.6f, 6.0f, 1.4f, 2, "BirdOfParadise:2@0-0.4, Calathea:3@0.4-0.8, BostonFern:3@0.4-0.85, CreepingGroundCover:6@0.7-1.15c"));
            list.Add(C("Plaza", 17.0f, -6.0f, 1.4f, 2, "BirdOfParadise:1@0-0.3, ArecaPalm:1@0.3-0.6, Anthurium:3@0.4-0.8, Calathea:2@0.4-0.8, Moss:4@0.7-1.1c, CreepingGroundCover:5@0.7-1.15c"));
            list.Add(C("Plaza", 13.8f, -6.8f, 1.5f, 3, "ArecaPalm:2@0-0.5, BostonFern:3@0.4-0.85, Orchid:2@0.5-0.8, Calathea:2@0.5-0.8, CreepingGroundCover:7@0.7-1.15c"));
            list.Add(C("Plaza", 10.6f, -6.0f, 1.4f, 2, "Banana:2@0-0.45, Philodendron:2@0.4-0.75, BostonFern:2@0.5-0.85, Moss:5@0.7-1.1c, CreepingGroundCover:4@0.7-1.15c"));

            // Entrance pockets: arriving visitors walk into greenery.
            list.Add(C("Foyer", -17.4f, 8.6f, 1.7f, 3, "ArecaPalm:2@0-0.4, BirdOfParadise:1@0.3-0.6, Monstera:1@0.45-0.75, Calathea:3@0.45-0.85, BostonFern:3@0.5-0.88, Anthurium:2@0.55-0.85, CreepingGroundCover:8@0.7-1.15c, Moss:4@0.75-1.1c"));
            list.Add(C("Foyer", -17.5f, 4.9f, 1.2f, 2, "Banana:2@0-0.45, Philodendron:1@0.4-0.7, Calathea:3@0.45-0.85, Orchid:2@0.55-0.85, Moss:5@0.7-1.1c, CreepingGroundCover:5@0.7-1.15c"));
            list.Add(C("Foyer", -17.4f, -8.6f, 1.7f, 3, "BirdOfParadise:2@0-0.4, Banana:1@0.3-0.6, Philodendron:2@0.45-0.8, BostonFern:3@0.5-0.88, Anthurium:3@0.5-0.85, CreepingGroundCover:8@0.7-1.15c, Moss:4@0.75-1.1c"));
            list.Add(C("Foyer", -17.5f, -4.9f, 1.2f, 2, "ArecaPalm:2@0-0.45, Calathea:3@0.45-0.85, Anthurium:2@0.5-0.85, BostonFern:2@0.55-0.88, Moss:5@0.7-1.1c, CreepingGroundCover:5@0.7-1.15c"));

            // Low borders along the main path between the plaza and the research wing (a gap at x = 26 keeps the room connectors open).
            string[] low =
            {
                "CreepingGroundCover:5@0-1.1c, Moss:2@0.2-1.0c, BostonFern:1@0-0.4",
                "Moss:3@0-1.0c, CreepingGroundCover:4@0.3-1.1c, Calathea:1@0-0.4",
                "CreepingGroundCover:4@0-1.1c, Orchid:1@0-0.4, Moss:2@0.4-1.0c",
            };
            float[] xs = { 20.2f, 22.6f, 24.2f, 28.0f, 29.6f, 31.0f };
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < xs.Length; i++)
                    list.Add(C("PathBorder", xs[i] + side * 0.15f, side * 2.6f, 0.85f, i % 2, low[(i + (side > 0 ? 0 : 1)) % low.Length]));

            // Around the research wing: soft planting between the pods and at the rear corners.
            string[] ends =
            {
                "BostonFern:2@0-0.5, Calathea:2@0.3-0.8, CreepingGroundCover:5@0.5-1.15c, Moss:2@0.6-1.1c",
                "BirdOfParadise:1@0-0.3, Calathea:2@0.3-0.8, BostonFern:2@0.4-0.85, CreepingGroundCover:5@0.6-1.15c",
            };
            float pr = 9.55f;
            float[] angles = { 17.5f, 52.5f, 87.5f, -17.5f, -52.5f, -87.5f };
            for (int i = 0; i < angles.Length; i++)
            {
                float t = angles[i] * Mathf.Deg2Rad;
                list.Add(C("Research", ConservatoryExpansion.ResearchPlaza.x + pr * Mathf.Cos(t), pr * Mathf.Sin(t), 0.9f, 1, ends[i % 2]));
            }
            list.Add(C("Research", 46.4f, 9.6f, 1.25f, 2, "Banana:2@0-0.45, ArecaPalm:1@0.3-0.6, Calathea:2@0.45-0.85, BostonFern:2@0.5-0.88, CreepingGroundCover:6@0.7-1.15c"));
            list.Add(C("Research", 46.4f, -9.6f, 1.25f, 2, "ArecaPalm:2@0-0.45, BirdOfParadise:1@0.3-0.6, Anthurium:2@0.45-0.85, BostonFern:2@0.5-0.88, Moss:4@0.7-1.1c, CreepingGroundCover:3@0.7-1.15c"));
            return list;
        }

        public static string Build()
        {
            var log = new StringBuilder();
            PlantKit kit = PlantKit.Create(out string error);
            if (kit == null) return "ERROR: " + error;
            var manager = Object.FindFirstObjectByType<GreenhouseManager>();
            if (manager == null) return "ERROR: GreenhouseManager not found.";

            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(RootName);

            Material stone = PlantKit.Mat("BioDome_Stone"), timber = PlantKit.Mat("BioDome_Timber"), steel = PlantKit.Mat("BioDome_Steel");
            var irrigations = new List<IrrigationController>();

            // --- Planted clusters --------------------------------------------------
            var groups = new Dictionary<string, (GameObject go, List<PlantController> plants)>();
            var rng = new System.Random(8800);
            int clusterIndex = 0;
            foreach (Cluster c in BuildClusterList())
            {
                if (!groups.TryGetValue(c.Group, out var g))
                {
                    var go = new GameObject("Group_" + c.Group);
                    go.transform.SetParent(root.transform, false);
                    g = (go, new List<PlantController>());
                    groups[c.Group] = g;
                }
                var cluster = new GameObject($"Cluster_{c.Group}_{++clusterIndex:00}").transform;
                cluster.SetParent(g.go.transform, false);
                cluster.position = new Vector3(c.Centre.x, 0.03f, c.Centre.y);

                // A low mound of soil so the planting sits in the floor instead of on it.
                var soil = PlantKit.Prim(PrimitiveType.Cylinder, "Mound", cluster, new Vector3(0f, 0.04f, 0f), new Vector3(c.Radius * 2f * 1.05f, 0.04f, c.Radius * 2f * 1.05f), PlantKit.Mat("BioDome_Soil"));
                soil.name = "Mound";

                var plantsParent = new GameObject("Plants").transform;
                plantsParent.SetParent(cluster, false);
                int n = kit.Compose(plantsParent, rng, g.plants, Vector2.zero, c.Radius, c.Radius, c.Recipe, 0.06f, 1f);

                for (int i = 0; i < c.Rocks; i++)
                {
                    float t = (float)rng.NextDouble() * Mathf.PI * 2f, r = c.Radius * (0.75f + (float)rng.NextDouble() * 0.35f);
                    float sc = 0.2f + (float)rng.NextDouble() * 0.28f;
                    var rock = PlantKit.Prim(PrimitiveType.Sphere, "Rock_" + i, cluster, new Vector3(Mathf.Cos(t) * r, 0.05f, Mathf.Sin(t) * r), new Vector3(sc * 1.5f, sc * 0.85f, sc * 1.2f), stone);
                    rock.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                }
                log.AppendLine($"  {cluster.name}: {n} plants");
            }
            foreach (var kv in groups)
                irrigations.Add(PlantKit.AddIrrigation(kv.Value.go, kv.Value.plants));

            // --- Benches and resting places -------------------------------------------
            var furniture = new GameObject("Furniture").transform;
            furniture.SetParent(root.transform, false);
            PlantKit.Bench(furniture, "Bench_Foyer_N", new Vector3(-16.4f, 0.03f, 2.45f), 0f, timber, steel);
            PlantKit.Bench(furniture, "Bench_Foyer_S", new Vector3(-16.4f, 0.03f, -2.45f), 180f, timber, steel);
            PlantKit.Bench(furniture, "Bench_Path_N", new Vector3(22.0f, 0.03f, 2.35f), 0f, timber, steel);
            PlantKit.Bench(furniture, "Bench_Path_S", new Vector3(22.0f, 0.03f, -2.35f), 180f, timber, steel);
            PlantKit.Bench(furniture, "Bench_Research_N", new Vector3(33.4f, 0.03f, 3.0f), -90f, timber, steel);
            PlantKit.Bench(furniture, "Bench_Research_S", new Vector3(33.4f, 0.03f, -3.0f), -90f, timber, steel);

            // --- Fountain: lilies ----------------------------------------------------------
            BuildLilies(root.transform);

            // --- Climbing vines ----------------------------------------------------------
            log.AppendLine("  " + BuildVines(root.transform));

            // Manager ticks the new irrigation groups (keeps any already listed).
            var managerSo = new SerializedObject(manager);
            SerializedProperty extra = managerSo.FindProperty("extraIrrigations");
            var keep = new List<Object>();
            for (int i = 0; i < extra.arraySize; i++)
            {
                Object o = extra.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o != null) keep.Add(o);
            }
            foreach (IrrigationController irr in irrigations) keep.Add(irr);
            extra.arraySize = keep.Count;
            for (int i = 0; i < keep.Count; i++) extra.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            log.Insert(0, $"  landscape: {kit.Spawned} mature plants in {clusterIndex} clusters, 6 benches, fountain lilies, 16 vines; {kit.Triangles / 1000} k triangles of planting\n");
            return log.ToString();
        }

        // --- Lilies ------------------------------------------------------------------

        private static void BuildLilies(Transform root)
        {
            var group = new GameObject("FountainLilies").transform;
            group.SetParent(root, false);
            Material pad = PlantKit.LitMaterial("Assets/Art/Materials/Experiment/Lily_Pad.mat", new Color(0.18f, 0.45f, 0.2f), 0.5f, 0f, null, true);
            Material flower = PlantKit.LitMaterial("Assets/Art/Materials/Experiment/Lily_Flower.mat", new Color(0.95f, 0.62f, 0.78f), 0.4f, 0f, new Color(0.35f, 0.12f, 0.2f));
            var rng = new System.Random(77);
            for (int i = 0; i < 9; i++)
            {
                float t = (i / 9f) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.4f;
                float r = 1.0f + (float)rng.NextDouble() * 0.55f;
                float sc = 0.34f + (float)rng.NextDouble() * 0.22f;
                Vector3 p = new Vector3(ConservatoryExpansion.PlazaX + Mathf.Cos(t) * r, 0.435f, Mathf.Sin(t) * r);
                var go = PlantKit.Prim(PrimitiveType.Cylinder, "Pad_" + i, group, p, new Vector3(sc, 0.006f, sc * (0.9f + (float)rng.NextDouble() * 0.2f)), pad);
                go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                if (i % 3 == 0) PlantKit.Prim(PrimitiveType.Sphere, "Flower_" + i, group, p + new Vector3(0.03f, 0.04f, 0f), new Vector3(0.11f, 0.08f, 0.11f), flower);
            }
        }

        // --- Vines -------------------------------------------------------------------

        private static string BuildVines(Transform root)
        {
            Material[] leaves =
            {
                PlantKit.LitMaterial("Assets/Art/Materials/Experiment/Vine_Leaf_A.mat", new Color(0.20f, 0.50f, 0.18f), 0.35f, 0f, null, true),
                PlantKit.LitMaterial("Assets/Art/Materials/Experiment/Vine_Leaf_B.mat", new Color(0.32f, 0.60f, 0.20f), 0.35f, 0f, null, true),
                PlantKit.LitMaterial("Assets/Art/Materials/Experiment/Vine_Leaf_C.mat", new Color(0.14f, 0.38f, 0.16f), 0.35f, 0f, null, true),
                PlantKit.LitMaterial("Assets/Art/Materials/Experiment/Vine_Stem.mat", new Color(0.28f, 0.2f, 0.12f), 0.15f),
            };
            var d = new MeshData();
            var rng = new System.Random(31337);
            int leafCount = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k < 8; k++)
                {
                    float x = -16f + 8f * k + (side > 0 ? 0f : 4f * (k == 7 ? 0 : 1)) ; // staggered so the two walls differ
                    if (x > 44.5f) x = 44.5f;
                    float zWall = side * 12.12f;
                    float phase = (float)rng.NextDouble() * 6.28f;
                    float topY = 4.8f + (float)rng.NextDouble() * 1.4f;
                    int steps = 26;
                    Vector3 prev = default;
                    for (int s = 0; s <= steps; s++)
                    {
                        float t = s / (float)steps;
                        float y = Mathf.Lerp(0.5f, topY, t);
                        Vector3 p = new Vector3(x + 0.22f * Mathf.Sin(t * 7f + phase), y, zWall - side * (0.05f + 0.05f * Mathf.Sin(t * 5f + phase)));
                        if (s > 0) MeshShapes.AddTube(d, prev, p, 0.022f, 5, 3, false);
                        prev = p;
                        // leaves, denser towards the middle height
                        float density = 0.5f + Mathf.Sin(t * Mathf.PI) * 1.4f;
                        int n = Mathf.RoundToInt(density * 3f);
                        for (int i = 0; i < n; i++)
                        {
                            Vector3 lp = p + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.55f, ((float)rng.NextDouble() - 0.5f) * 0.18f, -side * (float)rng.NextDouble() * 0.12f);
                            AddLeaf(d, lp, side, rng, (float)rng.NextDouble() < 0.5f ? 0 : ((float)rng.NextDouble() < 0.5f ? 1 : 2));
                            leafCount++;
                        }
                    }
                    // a side branch along the wall transom, left and right
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        float len = 1.2f + (float)rng.NextDouble() * 1.4f;
                        Vector3 bprev = new Vector3(x, 3.6f + (float)rng.NextDouble() * 0.5f, zWall - side * 0.06f);
                        for (int s = 1; s <= 10; s++)
                        {
                            float t = s / 10f;
                            Vector3 p = new Vector3(x + dir * len * t, bprev.y - 0.05f * t + 0.08f * Mathf.Sin(t * 9f + phase), zWall - side * (0.06f + 0.04f * Mathf.Sin(t * 6f)));
                            MeshShapes.AddTube(d, bprev, p, 0.016f, 4, 3, false);
                            bprev = p;
                            for (int i = 0; i < 3; i++)
                            {
                                Vector3 lp = p + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.22f, -(float)rng.NextDouble() * 0.28f, -side * (float)rng.NextDouble() * 0.1f);
                                AddLeaf(d, lp, side, rng, rng.Next(3));
                                leafCount++;
                            }
                        }
                    }
                }
            }
            GreenhouseAssetFactory.EnsureFolder(GreenhouseAssetFactory.MeshFolder);
            string path = $"{GreenhouseAssetFactory.MeshFolder}/Landscape_Vines.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh();
            mesh.name = "Landscape_Vines";
            d.ApplyTo(mesh);
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);

            var go = new GameObject("ClimbingVines");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var count = Mathf.Max(1, mesh.subMeshCount);
            var used = new Material[count];
            for (int i = 0; i < count; i++) used[i] = leaves[Mathf.Min(i, leaves.Length - 1)];
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = used;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return $"vines: {leafCount} leaves in one mesh ({mesh.vertexCount} vertices)";
        }

        private static void AddLeaf(MeshData d, Vector3 p, int side, System.Random rng, int submesh)
        {
            float len = 0.17f + (float)rng.NextDouble() * 0.17f;
            float wid = len * (0.55f + (float)rng.NextDouble() * 0.2f);
            float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
            Vector3 along = new Vector3(Mathf.Cos(yaw), -0.35f + (float)rng.NextDouble() * 0.3f, Mathf.Sin(yaw) * 0.4f).normalized;
            Vector3 across = Vector3.Cross(along, new Vector3(0f, 0f, -side)).normalized * wid * 0.5f;
            Vector3 normal = Vector3.Cross(across, along).normalized;
            Vector3 tip = p + along * len, mid = p + along * len * 0.4f;
            int a = d.AddVertex(p, normal, new Vector2(0.5f, 0f));
            int b = d.AddVertex(mid - across, normal, new Vector2(0f, 0.4f));
            int c = d.AddVertex(tip, normal, new Vector2(0.5f, 1f));
            int e = d.AddVertex(mid + across, normal, new Vector2(1f, 0.4f));
            d.AddQuad(a, b, c, e, submesh);
        }
    }
}
