using System.Collections.Generic;
using System.Text;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Experiment;
using BioDome.Irrigation;
using BioDome.Plants;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Builds the collection wing under "BioDome_CollectionGardens": two garden rooms (x 19.5 - 33) with curved loop paths, a crescent of oval beds around each loop,
    /// benches in the loop centres, and side planters along both walls of the new section. Every bed is a layered composition of mature, simulated plants
    /// (tall feature plants, mid-height companions, accents and low cover that spills over the edge). Only this root is replaced; refuses to run outside
    /// BioDome_Working; originals are checksummed before and after. Menu: BioDome > Collection > Build Collection Beds.
    /// </summary>
    public static class CollectionGardenBuilder
    {
        public const string RootName = "BioDome_CollectionGardens";
        private const string WorkingScene = "Assets/Scenes/BioDome_Working.unity";

        private struct BedSpec
        {
            public string Name, Recipe;
            public Vector2 Centre, Size; // x,z and width(x), depth(z) of the oval bed
            public bool StoneEdge;
        }

        public const float RoomX = 26f;
        public const float RoomZ = 6.6f;
        private const float CrescentA = 5.0f, CrescentB = 2.8f;
        private const float LoopA = 3.0f, LoopB = 0.95f, LoopWidth = 1.2f;

        private static BedSpec Bed(string name, int side, float angle, float w, float d, bool stone, string recipe)
        {
            float t = angle * Mathf.Deg2Rad;
            var centre = new Vector2(RoomX + CrescentA * Mathf.Cos(t), side * (RoomZ + CrescentB * Mathf.Sin(t)));
            return new BedSpec { Name = name, Centre = centre, Size = new Vector2(w, d), StoneEdge = stone, Recipe = recipe };
        }

        private static readonly BedSpec[] Beds =
        {
            // North room: the giants.
            Bed("Banana",           1, 20f,  2.8f, 2.4f, false, "Banana:3@0-0.5, Calathea:4@0.45-0.85, BostonFern:2@0.5-0.85, Anthurium:2@0.55-0.85, Moss:6@0.75-1.12c, CreepingGroundCover:7@0.7-1.15c"),
            Bed("Bird of Paradise", 1, 65f,  2.6f, 2.2f, true,  "BirdOfParadise:3@0-0.5, Calathea:3@0.45-0.85, Anthurium:3@0.5-0.85, Orchid:2@0.6-0.85, Moss:5@0.75-1.12c, CreepingGroundCover:6@0.7-1.15c"),
            Bed("Areca Palm",       1, 115f, 2.6f, 2.2f, false, "ArecaPalm:2@0-0.45, Philodendron:2@0.45-0.8, BostonFern:3@0.5-0.88, Anthurium:2@0.6-0.85, CreepingGroundCover:8@0.7-1.15c, Moss:4@0.75-1.1c"),
            Bed("Monstera",         1, 160f, 2.4f, 2.0f, true,  "Monstera:2@0-0.45, Philodendron:2@0.5-0.85, Calathea:3@0.5-0.88, BostonFern:2@0.6-0.9, CreepingGroundCover:7@0.7-1.15c"),
            Bed("Philodendron",     1, 215f, 2.2f, 1.9f, false, "Philodendron:3@0-0.55, Anthurium:3@0.4-0.85, BostonFern:2@0.55-0.9, Moss:5@0.75-1.12c, CreepingGroundCover:5@0.7-1.15c"),
            Bed("Boston Fern",      1, 325f, 2.0f, 1.8f, true,  "BostonFern:4@0-0.6, Calathea:3@0.45-0.85, Orchid:2@0.55-0.85, Moss:5@0.75-1.12c, CreepingGroundCover:4@0.7-1.1c"),
            // South room: understory and ground covers.
            Bed("Calathea",         -1, 20f,  2.2f, 1.9f, true,  "Calathea:5@0-0.6, BostonFern:2@0.5-0.88, Anthurium:2@0.55-0.85, CreepingGroundCover:7@0.7-1.15c, Moss:4@0.75-1.1c"),
            Bed("Anthurium",        -1, 70f,  2.0f, 1.8f, false, "Anthurium:5@0-0.6, Calathea:2@0.5-0.85, Orchid:2@0.55-0.85, Moss:5@0.75-1.12c, CreepingGroundCover:4@0.7-1.1c"),
            Bed("Orchid",           -1, 125f, 1.9f, 1.7f, true,  "Orchid:4@0-0.6, BostonFern:2@0.5-0.88, Calathea:2@0.55-0.85, Moss:6@0.75-1.12c, CreepingGroundCover:3@0.7-1.1c"),
            Bed("Moss",             -1, 215f, 1.9f, 1.7f, false, "Moss:7@0-0.9c, BostonFern:3@0-0.6, Orchid:2@0.5-0.85, CreepingGroundCover:5@0.7-1.15c"),
            Bed("Ground Cover",     -1, 325f, 2.0f, 1.8f, true,  "CreepingGroundCover:10@0-1.1c, Anthurium:3@0-0.55, Calathea:3@0.45-0.85, Moss:3@0.7-1.1c"),
        };

        public static IEnumerable<(string name, Vector3 centre)> BedCentres()
        {
            foreach (BedSpec b in Beds) yield return (b.Name, new Vector3(b.Centre.x, 0.12f, b.Centre.y));
        }

        [MenuItem("BioDome/Collection/Build Collection Beds")]
        public static void BuildFromMenu() => Debug.Log("BioDome collection beds:\n" + Build());

        public static void BuildBatch()
        {
            int exit = 0;
            try
            {
                EditorSceneManager.OpenScene(WorkingScene, OpenSceneMode.Single);
                string result = Build();
                Debug.Log("COLLECTIONGARDEN\n" + result);
                if (result.Contains("ERROR")) exit = 1;
            }
            catch (System.Exception e) { Debug.LogError("COLLECTIONGARDEN EXCEPTION " + e); exit = 2; }
            EditorApplication.Exit(exit);
        }

        public static string Build() => Build(true);

        public static string Build(bool save)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != WorkingScene) return $"ERROR: the open scene is '{scene.path}'. Open {WorkingScene} first.";

            var log = new StringBuilder();
            GameObject plantsRoot = GameObject.Find("BioDome_Plants");
            if (plantsRoot == null) return "ERROR: BioDome_Plants not found.";
            int originalsBefore = plantsRoot.GetComponentsInChildren<PlantController>(true).Length;
            long checksumBefore = Checksum(plantsRoot);

            PlantKit kit = PlantKit.Create(out string error);
            if (kit == null) return "ERROR: " + error;
            var manager = Object.FindFirstObjectByType<GreenhouseManager>();
            if (manager == null) return "ERROR: GreenhouseManager not found.";

            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);

            Material timber = PlantKit.Mat("BioDome_Timber"), soil = PlantKit.Mat("BioDome_Soil"), stone = PlantKit.Mat("BioDome_Stone"), steel = PlantKit.Mat("BioDome_Steel"), pavers = PlantKit.Mat("BioDome_Pavers");
            if (timber == null || soil == null || stone == null || steel == null || pavers == null) return "ERROR: a required BioDome material is missing.";

            var root = new GameObject(RootName);
            BuildRoomPaths(root.transform, pavers, stone, timber, steel);
            var irrigations = new List<IrrigationController>();
            int total = 0;

            for (int bi = 0; bi < Beds.Length; bi++)
            {
                BedSpec spec = Beds[bi];
                var bed = new GameObject("Bed_" + spec.Name.Replace(" ", "")).transform;
                bed.SetParent(root.transform, false);
                bed.position = new Vector3(spec.Centre.x, 0.03f, spec.Centre.y);

                BuildBedStructure(bed, spec, timber, soil, stone, bi);

                var plantsGroup = new GameObject("Plants").transform;
                plantsGroup.SetParent(bed, false);
                var plants = new List<PlantController>();
                var rng = new System.Random(7000 + bi * 131);
                float a = spec.Size.x / 2f - 0.2f, b = spec.Size.y / 2f - 0.2f;
                int n = kit.Compose(plantsGroup, rng, plants, Vector2.zero, a, b, spec.Recipe, 0.28f, 1f);
                total += n;
                log.AppendLine($"  {spec.Name}: {n} plants");

                irrigations.Add(PlantKit.AddIrrigation(bed.gameObject, plants));
                BuildLabel(bed, spec, timber, steel);
            }

            BuildSidePlanters(root.transform, kit, timber, soil, irrigations, log, ref total);

            // Manager ticks these irrigation controllers too (keeps any already listed, e.g. the experiment pods').
            var managerSo = new SerializedObject(manager);
            SerializedProperty extra = managerSo.FindProperty("extraIrrigations");
            var keep = new List<Object>();
            for (int i = 0; i < extra.arraySize; i++)
            {
                Object o = extra.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o != null) keep.Add(o);
            }
            foreach (IrrigationController c in irrigations) keep.Add(c);
            extra.arraySize = keep.Count;
            for (int i = 0; i < keep.Count; i++) extra.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            long checksumAfter = Checksum(plantsRoot);
            int originalsAfter = plantsRoot.GetComponentsInChildren<PlantController>(true).Length;
            if (checksumAfter != checksumBefore || originalsAfter != originalsBefore)
            {
                Object.DestroyImmediate(root);
                return $"ERROR: original plants changed ({originalsBefore}->{originalsAfter}). Build discarded, scene not saved.";
            }

            if (save) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            log.Insert(0, $"Built {Beds.Length} collection beds and 2 side planters: {total} mature plants. Originals: {originalsAfter} plants, checksum unchanged ({checksumAfter}).{(save ? " Scene saved." : "")}\n");
            return log.ToString();
        }

        // --- Side planters -----------------------------------------------------------

        private static void BuildSidePlanters(Transform root, PlantKit kit, Material timber, Material soil, List<IrrigationController> irrigations, StringBuilder log, ref int total)
        {
            var bordersRoot = new GameObject("SidePlanters").transform;
            bordersRoot.SetParent(root, false);
            // A rhythm of tall, mid and low plants, varied between the two sides so nothing repeats exactly.
            string[][] tall = { new[] { "Banana", "ArecaPalm", "BirdOfParadise", "Banana", "ArecaPalm" }, new[] { "BirdOfParadise", "Banana", "ArecaPalm", "BirdOfParadise", "Banana" } };
            string[][] mid = { new[] { "Philodendron", "Calathea", "Anthurium", "Monstera" }, new[] { "Calathea", "Monstera", "Philodendron", "Anthurium" } };
            string[] filler = { "BostonFern", "Moss", "CreepingGroundCover", "CreepingGroundCover", "Calathea", "Moss" };
            const float x0 = 19.8f, x1 = 47.0f, zc = 11.55f, depth = 1.1f;

            for (int side = -1; side <= 1; side += 2)
            {
                int k = side > 0 ? 0 : 1;
                var strip = new GameObject(side > 0 ? "Planter_North" : "Planter_South").transform;
                strip.SetParent(bordersRoot, false);
                strip.position = new Vector3((x0 + x1) * 0.5f, 0.03f, side * zc);
                float len = x1 - x0;
                PlantKit.Prim(PrimitiveType.Cube, "Soil", strip, new Vector3(0f, 0.14f, 0f), new Vector3(len, 0.28f, depth), soil);
                PlantKit.Prim(PrimitiveType.Cube, "EdgeInner", strip, new Vector3(0f, 0.17f, -side * (depth / 2f + 0.035f)), new Vector3(len + 0.1f, 0.34f, 0.07f), timber);
                PlantKit.Prim(PrimitiveType.Cube, "EdgeEndW", strip, new Vector3(-len / 2f - 0.035f, 0.17f, 0f), new Vector3(0.07f, 0.34f, depth), timber);
                PlantKit.Prim(PrimitiveType.Cube, "EdgeEndE", strip, new Vector3(len / 2f + 0.035f, 0.17f, 0f), new Vector3(0.07f, 0.34f, depth), timber);

                var rng = new System.Random(5100 + side * 17);
                var plants = new List<PlantController>();
                var group = new GameObject("Plants").transform;
                group.SetParent(strip, false);
                float Jit(float amount) => ((float)rng.NextDouble() - 0.5f) * amount;
                void Spawn(string label, float lx, float lz) =>
                    plants.Add(kit.Spawn(label, group, new Vector3(lx, 0.28f, lz), (float)rng.NextDouble() * 360f, 0.92f + (float)rng.NextDouble() * 0.2f, 1f, rng.Next(4)));

                int t = 0;
                for (float lx = -len / 2f + 1.3f + (side > 0 ? 0f : 1.4f); lx < len / 2f - 0.8f; lx += 3.4f + Jit(0.5f), t++)
                    Spawn(tall[k][t % tall[k].Length], lx, side * 0.2f + Jit(0.1f));
                int m = 0;
                for (float lx = -len / 2f + 3.1f + (side > 0 ? 0f : 0.9f); lx < len / 2f - 0.8f; lx += 3.4f + Jit(0.4f), m++)
                    Spawn(mid[k][m % mid[k].Length], lx, -side * 0.05f + Jit(0.15f));
                int f = 0;
                for (float lx = -len / 2f + 0.6f; lx < len / 2f - 0.4f; lx += 0.95f, f++)
                    Spawn(filler[(f + k * 2) % filler.Length], lx + Jit(0.25f), -side * 0.34f + Jit(0.12f));

                total += plants.Count;
                log.AppendLine($"  Side planter {(side > 0 ? "north" : "south")}: {plants.Count} plants");
                irrigations.Add(PlantKit.AddIrrigation(strip.gameObject, plants));
            }
        }

        // --- Structure ---------------------------------------------------------------

        private static void BuildBedStructure(Transform bed, BedSpec spec, Material timber, Material soil, Material stone, int index)
        {
            var s = new GameObject("Structure").transform;
            s.SetParent(bed, false);
            float a = spec.Size.x / 2f, b = spec.Size.y / 2f;

            PlantKit.Prim(PrimitiveType.Cylinder, "Soil", s, new Vector3(0f, 0.15f, 0f), new Vector3(spec.Size.x - 0.06f, 0.15f, spec.Size.y - 0.06f), soil);

            var rng = new System.Random(900 + index);
            if (!spec.StoneEdge)
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * (a + b) / 0.38f), 18, 44);
                float length = Mathf.PI * (a + b) / n * 1.14f;
                for (int i = 0; i < n; i++)
                {
                    float t = i * 2f * Mathf.PI / n;
                    Vector3 p = new Vector3(a * Mathf.Cos(t), 0.19f, b * Mathf.Sin(t));
                    Vector3 tangent = new Vector3(-a * Mathf.Sin(t), 0f, b * Mathf.Cos(t)).normalized;
                    var board = PlantKit.Prim(PrimitiveType.Cube, "Edge_" + i, s, p, new Vector3(0.07f, 0.38f, length), timber);
                    board.transform.localRotation = Quaternion.LookRotation(tangent, Vector3.up);
                }
            }
            else
            {
                int n = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * (a + b) / 0.42f), 14, 36);
                for (int i = 0; i < n; i++)
                {
                    float t = i * 2f * Mathf.PI / n + (float)rng.NextDouble() * 0.05f;
                    float sc = 0.26f + (float)rng.NextDouble() * 0.10f;
                    Vector3 p = new Vector3((a + 0.02f) * Mathf.Cos(t), 0.17f, (b + 0.02f) * Mathf.Sin(t));
                    var rock = PlantKit.Prim(PrimitiveType.Sphere, "Rock_" + i, s, p, new Vector3(sc * 1.25f, sc * 1.1f, sc), stone);
                    rock.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                }
            }
            for (int i = 0; i < 2; i++)
            {
                float t = (float)rng.NextDouble() * 2f * Mathf.PI, r = 0.5f + (float)rng.NextDouble() * 0.3f;
                float sc = 0.11f + (float)rng.NextDouble() * 0.10f;
                var rock = PlantKit.Prim(PrimitiveType.Sphere, "Stone_" + i, s, new Vector3(a * r * Mathf.Cos(t), 0.30f, b * r * Mathf.Sin(t)), new Vector3(sc * 1.4f, sc, sc * 1.1f), stone);
                rock.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            }
        }

        private static void BuildLabel(Transform bed, BedSpec spec, Material timber, Material steel)
        {
            var sign = new GameObject("Label").transform;
            sign.SetParent(bed, false);
            float dz = spec.Size.y / 2f + 0.22f;
            sign.localPosition = new Vector3(0f, 0f, spec.Centre.y > 0f ? -dz : dz);
            sign.gameObject.AddComponent<FaceCameraYaw>();
            PlantKit.Prim(PrimitiveType.Cylinder, "Stake", sign, new Vector3(0f, 0.25f, 0f), new Vector3(0.03f, 0.25f, 0.03f), steel);
            PlantKit.Prim(PrimitiveType.Cube, "Plaque", sign, new Vector3(0f, 0.5f, 0f), new Vector3(0.82f, 0.2f, 0.03f), timber);
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshPro));
            go.transform.SetParent(sign, false);
            go.transform.localPosition = new Vector3(0f, 0.5f, -0.025f);
            var tmp = go.GetComponent<TextMeshPro>();
            tmp.text = spec.Name.ToUpperInvariant();
            tmp.fontSize = 1.15f; tmp.color = new Color(0.95f, 0.97f, 0.9f); tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center; tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(0.85f, 0.2f);
        }

        /// <summary>Loop paths, connectors and a bench in the middle of each room.</summary>
        private static void BuildRoomPaths(Transform root, Material pavers, Material stone, Material timber, Material steel)
        {
            var group = new GameObject("RoomPaths").transform;
            group.SetParent(root, false);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 centre = new Vector3(RoomX, 0f, side * RoomZ);
                var room = new GameObject(side > 0 ? "NorthRoomPath" : "SouthRoomPath").transform;
                room.SetParent(group, false);
                room.position = centre;

                int n = 40;
                float length = Mathf.PI * (LoopA + LoopB) / n * 1.18f;
                for (int i = 0; i < n; i++)
                {
                    float t = i * 2f * Mathf.PI / n;
                    Vector3 p = new Vector3(LoopA * Mathf.Cos(t), 0.045f, LoopB * Mathf.Sin(t));
                    Vector3 tangent = new Vector3(-LoopA * Mathf.Sin(t), 0f, LoopB * Mathf.Cos(t)).normalized;
                    var seg = PlantKit.Prim(PrimitiveType.Cube, "Loop_" + i, room, p, new Vector3(LoopWidth, 0.04f, length), pavers);
                    seg.transform.localRotation = Quaternion.LookRotation(tangent, Vector3.up);
                }
                float z0 = ConservatoryExpansion.MainPathHalf + 0.2f, z1 = RoomZ - LoopB - LoopWidth * 0.4f;
                var link = PlantKit.Prim(PrimitiveType.Cube, side > 0 ? "ConnectorN" : "ConnectorS", group, new Vector3(RoomX, 0.045f, side * (z0 + z1) * 0.5f), new Vector3(1.4f, 0.04f, z1 - z0), pavers);
                // A bench in the loop, facing the main path.
                PlantKit.Bench(room, "LoopBench", new Vector3(0f, 0.03f, 0f), side > 0 ? 0f : 180f, timber, steel);
            }
        }

        private static long Checksum(GameObject root)
        {
            long h = 17;
            foreach (PlantController p in root.GetComponentsInChildren<PlantController>(true))
            {
                Transform t = p.transform;
                h = h * 31 + p.name.GetHashCode();
                h = h * 31 + Mathf.RoundToInt(t.position.x * 1000f);
                h = h * 31 + Mathf.RoundToInt(t.position.y * 1000f);
                h = h * 31 + Mathf.RoundToInt(t.position.z * 1000f);
                h = h * 31 + Mathf.RoundToInt(t.eulerAngles.y * 100f);
                h = h * 31 + Mathf.RoundToInt(t.localScale.x * 1000f);
            }
            return h;
        }
    }
}
