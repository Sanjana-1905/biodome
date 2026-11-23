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
    /// Builds the experiment garden: five glass-and-steel climate pods with new Monstera under "BioDome_ExperimentGarden".
    /// Only that root is created or replaced. Refuses to run on any scene other than BioDome_Working. The original plants are only
    /// read (a checksum of their transforms is compared before and after). Menu: BioDome > Experiment > Build Experiment Garden.
    /// </summary>
    public static class ExperimentGardenBuilder
    {
        public const string RootName = "BioDome_ExperimentGarden";
        private const string WorkingScene = "Assets/Scenes/BioDome_Working.unity";
        private const string MatFolder = "Assets/Art/Materials/Experiment";
        private const int PlantsPerPod = 6;

        // The research wing: the rear end of the expanded greenhouse. The five pods stand on an arc around the research plaza
        // (angle in degrees in the XZ plane, 0 = straight towards the rear wall) and face it.
        public static readonly Vector2 CourtCentre = ConservatoryExpansion.ResearchPlaza;
        private const float PodRadius = 9.0f;
        private const float FloorY = 0.04f;

        private struct PodSpec
        {
            public ExperimentalCondition Condition;
            public string Title, Subtitle;
            public Color Accent;
            public float Angle;
            public Vector2 Size;     // pod footprint: width (local x) and depth (local z) in metres
            public Vector2 Centre => CourtCentre + new Vector2(Mathf.Cos(Angle * Mathf.Deg2Rad), Mathf.Sin(Angle * Mathf.Deg2Rad)) * PodRadius;
            public Vector3 Outward => new Vector3(Mathf.Cos(Angle * Mathf.Deg2Rad), 0f, Mathf.Sin(Angle * Mathf.Deg2Rad));
        }

        private static readonly PodSpec[] Specs =
        {
            new PodSpec { Condition = ExperimentalCondition.Control,     Title = "CONTROL",       Subtitle = "Ideal conditions",     Accent = new Color(0.35f, 0.85f, 0.45f), Angle = 0f, Size = new Vector2(3.0f, 2.6f) },
            new PodSpec { Condition = ExperimentalCondition.HeatStress,  Title = "HEAT STRESS",   Subtitle = "+10 °C",               Accent = new Color(1.00f, 0.45f, 0.20f), Angle = 35f, Size = new Vector2(3.0f, 2.6f) },
            new PodSpec { Condition = ExperimentalCondition.LowHumidity, Title = "LOW HUMIDITY",  Subtitle = "−30 % humidity",       Accent = new Color(0.30f, 0.75f, 1.00f), Angle = 70f, Size = new Vector2(3.0f, 2.6f) },
            new PodSpec { Condition = ExperimentalCondition.LowLight,    Title = "LOW LIGHT",     Subtitle = "Shaded, 40 % light",   Accent = new Color(0.62f, 0.45f, 1.00f), Angle = -70f, Size = new Vector2(3.0f, 2.6f) },
            new PodSpec { Condition = ExperimentalCondition.WaterStress, Title = "WATER STRESS",  Subtitle = "Reduced water supply", Accent = new Color(0.95f, 0.80f, 0.25f), Angle = -35f, Size = new Vector2(3.0f, 2.6f) },
        };

        /// <summary>Pod centres, outward directions and accents (for the navigation shortcuts).</summary>
        public static IEnumerable<(string label, ExperimentalCondition condition, Vector3 centre, Vector3 outward, Color accent)> PodPlacements()
        {
            foreach (PodSpec p in Specs) yield return (p.Title, p.Condition, new Vector3(p.Centre.x, FloorY, p.Centre.y), p.Outward, p.Accent);
        }

        [MenuItem("BioDome/Experiment/Build Experiment Garden")]
        public static void BuildFromMenu()
        {
            string result = Build();
            Debug.Log("BioDome experiment garden:\n" + result);
        }

        public static void BuildBatch()
        {
            int exit = 0;
            try
            {
                EditorSceneManager.OpenScene(WorkingScene, OpenSceneMode.Single);
                string result = Build();
                Debug.Log("EXPERIMENTGARDEN\n" + result);
                if (result.Contains("ERROR")) exit = 1;
            }
            catch (System.Exception e) { Debug.LogError("EXPERIMENTGARDEN EXCEPTION " + e); exit = 2; }
            EditorApplication.Exit(exit);
        }

        public static string Build() => Build(true);

        public static string Build(bool save)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != WorkingScene) return $"ERROR: the open scene is '{scene.path}'. Open {WorkingScene} first; no other scene is ever modified.";

            var log = new StringBuilder();
            GameObject plantsRoot = GameObject.Find("BioDome_Plants");
            if (plantsRoot == null) return "ERROR: BioDome_Plants (the original plants) was not found.";
            int originalsBefore = plantsRoot.GetComponentsInChildren<PlantController>(true).Length;
            long checksumBefore = Checksum(plantsRoot);

            PlantKit kit = PlantKit.Create(out string kitError);
            if (kit == null) return "ERROR: " + kitError;

            var environment = Object.FindFirstObjectByType<EnvironmentController>();
            var manager = Object.FindFirstObjectByType<GreenhouseManager>();
            if (environment == null || manager == null) return "ERROR: EnvironmentController / GreenhouseManager not found in the scene.";

            // Replace only our own previous build.
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            System.IO.Directory.CreateDirectory(MatFolder);

            var root = new GameObject(RootName);
            var garden = root.AddComponent<ExperimentGarden>();
            var pods = new List<ExperimentPod>();
            var irrigations = new List<IrrigationController>();

            Material steel = Mat("BioDome_Steel"), glass = Mat("BioDome_Glass"), soil = Mat("BioDome_Soil"), timber = Mat("BioDome_Timber"), pavers = Mat("BioDome_Pavers");
            if (steel == null || glass == null || soil == null || timber == null || pavers == null) { Object.DestroyImmediate(root); return "ERROR: a required BioDome material is missing."; }

            BuildResearchWing(root.transform, steel, timber, pavers);

            Material podGlass = PlantKit.TransparentMaterial($"{MatFolder}/Pod_Glass.mat", new Color(0.84f, 0.94f, 0.97f, 0.09f), "Universal Render Pipeline/Lit", null, false);
            Material screenMat = PlantKit.LitMaterial($"{MatFolder}/Pod_Screen.mat", new Color(0.09f, 0.11f, 0.12f), 0.65f, 0.3f);
            Material screenGlow = PlantKit.LitMaterial($"{MatFolder}/Pod_ScreenGlow.mat", new Color(0.05f, 0.12f, 0.14f), 0.7f, 0f, new Color(0.10f, 0.45f, 0.5f));

            foreach (PodSpec spec in Specs)
            {
                var profile = AssetDatabase.LoadAssetAtPath<ZoneProfile>(ZoneProfileAssetBuilder.PathFor(spec.Condition));
                if (profile == null) { Object.DestroyImmediate(root); return $"ERROR: zone profile asset for {spec.Condition} is missing."; }

                float floor = FloorY;
                log.AppendLine($"  {spec.Title}: centre {spec.Centre}, angle {spec.Angle}");
                Material accent = AccentMaterial(spec.Condition, spec.Accent, steel);

                var pod = new GameObject("Pod_" + spec.Condition).transform;
                pod.SetParent(root.transform, false);
                pod.position = new Vector3(spec.Centre.x, floor, spec.Centre.y);
                pod.rotation = Quaternion.LookRotation(spec.Outward, Vector3.up); // local -z (the glass front, the sign) faces the plaza

                BuildShell(pod, spec.Size, steel, podGlass, soil, timber, pavers, accent);
                BuildSignature(pod, spec, steel, accent);
                BuildEquipment(pod, spec, steel, accent, screenMat, screenGlow);

                // Plants: identical layout, variants, scale and heading in every pod, pre-grown to 60 % so the pods look established
                // while the growth comparison still means something. Every pod starts from exactly the same state.
                Transform plantsGroup = new GameObject("Plants").transform;
                plantsGroup.SetParent(pod, false);
                var plantList = new List<PlantController>();
                float bedTop = 0.30f;
                Vector2 bed = spec.Size - new Vector2(0.7f, 0.7f);
                for (int i = 0; i < PlantsPerPod; i++)
                {
                    int col = i % 3, row = i / 3;
                    float px = (col - 1) * (bed.x / 3.0f);
                    float pz = (row - 0.5f) * (bed.y / 2.2f);
                    PlantController controller = kit.Spawn("Monstera", plantsGroup, new Vector3(px, bedTop, pz), (i * 61) % 360, 1f, 0.6f, i % 4);
                    controller.gameObject.name = $"Monstera_{spec.Condition}_{i + 1:00}";
                    var so = new SerializedObject(controller);
                    so.FindProperty("zoneProfile").objectReferenceValue = profile;
                    so.FindProperty("waterShareWeight").floatValue = profile.WaterShareWeight;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    plantList.Add(controller);
                }

                // Each pod has its own irrigation controller with identical settings, so pods cannot affect one another.
                var irrObject = new GameObject("Irrigation");
                irrObject.transform.SetParent(pod, false);
                var irrigation = irrObject.AddComponent<IrrigationController>();
                var irrSo = new SerializedObject(irrigation);
                irrSo.FindProperty("reservoirCapacityLitres").floatValue = 200f;
                irrSo.FindProperty("startingWaterLitres").floatValue = 200f;
                irrSo.FindProperty("mainsSupplyLitresPerHour").floatValue = 1f;
                irrSo.FindProperty("flowRateLitresPerHour").floatValue = 0.72f; // about 0.12 L/h per plant
                irrSo.FindProperty("mode").enumValueIndex = (int)IrrigationController.Mode.Auto;
                irrSo.FindProperty("startBelowMoisture").floatValue = 0.40f;
                irrSo.FindProperty("stopAboveMoisture").floatValue = 0.70f;
                irrSo.ApplyModifiedPropertiesWithoutUndo();
                irrigations.Add(irrigation);

                // Sign with live indicator.
                TMP_Text indicator = BuildSign(pod, spec, steel, screenMat, accent, floor);

                var podComponent = pod.gameObject.AddComponent<ExperimentPod>();
                var podSo = new SerializedObject(podComponent);
                podSo.FindProperty("displayName").stringValue = profile.DisplayName;
                podSo.FindProperty("description").stringValue = spec.Subtitle;
                podSo.FindProperty("accent").colorValue = spec.Accent;
                podSo.FindProperty("profile").objectReferenceValue = profile;
                podSo.FindProperty("irrigation").objectReferenceValue = irrigation;
                podSo.FindProperty("indicatorText").objectReferenceValue = indicator;
                SerializedProperty list = podSo.FindProperty("plants");
                list.arraySize = plantList.Count;
                for (int i = 0; i < plantList.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = plantList[i];
                podSo.ApplyModifiedPropertiesWithoutUndo();
                pods.Add(podComponent);
            }

            // Garden + comparison UI.
            var gardenSo = new SerializedObject(garden);
            gardenSo.FindProperty("environment").objectReferenceValue = environment;
            SerializedProperty podList = gardenSo.FindProperty("pods");
            podList.arraySize = pods.Count;
            for (int i = 0; i < pods.Count; i++) podList.GetArrayElementAtIndex(i).objectReferenceValue = pods[i];
            gardenSo.ApplyModifiedPropertiesWithoutUndo();

            var panelObject = new GameObject("ComparisonUI");
            panelObject.transform.SetParent(root.transform, false);
            var panel = panelObject.AddComponent<ExperimentComparisonPanel>();
            var panelSo = new SerializedObject(panel);
            panelSo.FindProperty("garden").objectReferenceValue = garden;
            panelSo.ApplyModifiedPropertiesWithoutUndo();

            // Tell the manager to tick the pod irrigation controllers (the only change to an existing object: one list, empty before).
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

            // Safety: originals untouched.
            long checksumAfter = Checksum(plantsRoot);
            int originalsAfter = plantsRoot.GetComponentsInChildren<PlantController>(true).Length;
            if (checksumAfter != checksumBefore || originalsAfter != originalsBefore)
            {
                Object.DestroyImmediate(root);
                return $"ERROR: original plants changed ({originalsBefore}->{originalsAfter}, checksum {checksumBefore}->{checksumAfter}). Build discarded, scene not saved.";
            }

            if (save) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            log.Insert(0, $"Built {pods.Count} pods with {pods.Count * PlantsPerPod} new Monstera under {RootName}. Originals: {originalsAfter} plants under BioDome_Plants, transform checksum unchanged ({checksumAfter}). {(save ? " Scene saved." : "")}\n");
            return log.ToString();
        }

        // --- Geometry -----------------------------------------------------------------

        private static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/{name}.mat");

        private static Material AccentMaterial(ExperimentalCondition c, Color color, Material like)
        {
            string path = $"{MatFolder}/Pod_{c}_Accent.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(like.shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = like.shader;
            m.SetColor("_BaseColor", color * 0.8f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.2f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.6f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            m.SetColor("_EmissionColor", color * 0.9f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // decor must not block plant picking
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static void BuildShell(Transform pod, Vector2 size, Material steel, Material glass, Material soil, Material timber, Material pavers, Material accent)
        {
            var shell = new GameObject("Structure").transform;
            shell.SetParent(pod, false);
            float w = size.x, d = size.y;

            Prim(PrimitiveType.Cube, "Plinth", shell, new Vector3(0f, 0.04f, 0f), new Vector3(w, 0.08f, d), pavers);
            // Glowing accent inlay set into the plinth, all round.
            Prim(PrimitiveType.Cube, "Inlay_N", shell, new Vector3(0f, 0.083f, d / 2f - 0.05f), new Vector3(w - 0.12f, 0.012f, 0.03f), accent);
            Prim(PrimitiveType.Cube, "Inlay_S", shell, new Vector3(0f, 0.083f, -d / 2f + 0.05f), new Vector3(w - 0.12f, 0.012f, 0.03f), accent);
            Prim(PrimitiveType.Cube, "Inlay_E", shell, new Vector3(w / 2f - 0.05f, 0.083f, 0f), new Vector3(0.03f, 0.012f, d - 0.12f), accent);
            Prim(PrimitiveType.Cube, "Inlay_W", shell, new Vector3(-w / 2f + 0.05f, 0.083f, 0f), new Vector3(0.03f, 0.012f, d - 0.12f), accent);

            Vector2 bed = size - new Vector2(0.7f, 0.7f);
            Prim(PrimitiveType.Cube, "SoilBed", shell, new Vector3(0f, 0.19f, 0f), new Vector3(bed.x, 0.22f, bed.y), soil);
            Prim(PrimitiveType.Cube, "Edge_N", shell, new Vector3(0f, 0.20f, bed.y / 2f + 0.03f), new Vector3(bed.x + 0.12f, 0.26f, 0.06f), timber);
            Prim(PrimitiveType.Cube, "Edge_S", shell, new Vector3(0f, 0.20f, -bed.y / 2f - 0.03f), new Vector3(bed.x + 0.12f, 0.26f, 0.06f), timber);
            Prim(PrimitiveType.Cube, "Edge_E", shell, new Vector3(bed.x / 2f + 0.03f, 0.20f, 0f), new Vector3(0.06f, 0.26f, bed.y), timber);
            Prim(PrimitiveType.Cube, "Edge_W", shell, new Vector3(-bed.x / 2f - 0.03f, 0.20f, 0f), new Vector3(0.06f, 0.26f, bed.y), timber);

            // Chamber: four slim steel posts, rails at the bottom and top, glass on all four sides and a glass lid on a light steel frame.
            const float h = 1.75f, y0 = 0.08f;
            float hx = w / 2f - 0.04f, hz = d / 2f - 0.04f;
            Vector2[] corners = { new Vector2(hx, hz), new Vector2(-hx, hz), new Vector2(hx, -hz), new Vector2(-hx, -hz) };
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Cylinder, "Post_" + i, shell, new Vector3(corners[i].x, y0 + h / 2f, corners[i].y), new Vector3(0.05f, h / 2f, 0.05f), steel);

            float gx = w - 0.08f, gz = d - 0.08f;
            foreach (float y in new[] { y0 + 0.025f, y0 + h - 0.025f })
            {
                string tag = y.ToString("0.0");
                Prim(PrimitiveType.Cube, "RailN_" + tag, shell, new Vector3(0f, y, hz), new Vector3(gx, 0.05f, 0.05f), steel);
                Prim(PrimitiveType.Cube, "RailS_" + tag, shell, new Vector3(0f, y, -hz), new Vector3(gx, 0.05f, 0.05f), steel);
                Prim(PrimitiveType.Cube, "RailE_" + tag, shell, new Vector3(hx, y, 0f), new Vector3(0.05f, 0.05f, gz), steel);
                Prim(PrimitiveType.Cube, "RailW_" + tag, shell, new Vector3(-hx, y, 0f), new Vector3(0.05f, 0.05f, gz), steel);
            }
            // Mid-height mullions give the open frame a slim, technical look (the chambers have no glass, so the plants are fully visible).
            foreach (float x in new[] { -w / 6f, w / 6f })
            {
                Prim(PrimitiveType.Cube, "MullionN", shell, new Vector3(x, y0 + h / 2f, hz), new Vector3(0.025f, h - 0.1f, 0.025f), steel);
                Prim(PrimitiveType.Cube, "MullionS", shell, new Vector3(x, y0 + h / 2f, -hz), new Vector3(0.025f, h - 0.1f, 0.025f), steel);
            }
            float gh = h - 0.12f, gy = y0 + h / 2f;
            Prim(PrimitiveType.Cube, "LidBarX", shell, new Vector3(0f, y0 + h + 0.015f, 0f), new Vector3(gx, 0.03f, 0.03f), steel);
            Prim(PrimitiveType.Cube, "LidBarZ", shell, new Vector3(0f, y0 + h + 0.015f, 0f), new Vector3(0.03f, 0.03f, gz), steel);

            float ay = y0 + h - 0.11f;
            Prim(PrimitiveType.Cube, "Accent_N", shell, new Vector3(0f, ay, hz + 0.03f), new Vector3(gx, 0.03f, 0.018f), accent);
            Prim(PrimitiveType.Cube, "Accent_S", shell, new Vector3(0f, ay, -hz - 0.03f), new Vector3(gx, 0.03f, 0.018f), accent);
            Prim(PrimitiveType.Cube, "Accent_E", shell, new Vector3(hx + 0.03f, ay, 0f), new Vector3(0.018f, 0.03f, gz), accent);
            Prim(PrimitiveType.Cube, "Accent_W", shell, new Vector3(-hx - 0.03f, ay, 0f), new Vector3(0.018f, 0.03f, gz), accent);
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Sphere, "Cap_" + i, shell, new Vector3(corners[i].x, y0 + h + 0.04f, corners[i].y), Vector3.one * 0.08f, accent);
        }

        // Scientific equipment shared by every pod: a drip irrigation line, a climate sensor and a small control cabinet with a status display.
        private static void BuildEquipment(Transform pod, PodSpec spec, Material steel, Material accent, Material screen, Material screenGlow)
        {
            var eq = new GameObject("Equipment").transform;
            eq.SetParent(pod, false);
            float w = spec.Size.x, d = spec.Size.y;
            float hz = d / 2f - 0.04f;
            bool dry = spec.Condition == ExperimentalCondition.WaterStress;

            // Drip line along the back of the bed, with emitters hanging towards the plants (fewer, and a red valve, on the water-stress pod).
            float pipeLen = w - 0.9f;
            var pipe = Prim(PrimitiveType.Cylinder, "DripLine", eq, new Vector3(0f, 0.62f, hz - 0.2f), new Vector3(0.028f, pipeLen / 2f, 0.028f), steel);
            pipe.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            int emitters = dry ? 2 : 5;
            for (int i = 0; i < emitters; i++)
            {
                float x = emitters == 1 ? 0f : Mathf.Lerp(-pipeLen / 2f + 0.25f, pipeLen / 2f - 0.25f, i / (float)(emitters - 1));
                Prim(PrimitiveType.Cylinder, "Drop_" + i, eq, new Vector3(x, 0.52f, hz - 0.2f), new Vector3(0.008f, 0.1f, 0.008f), steel);
                Prim(PrimitiveType.Sphere, "Emitter_" + i, eq, new Vector3(x, 0.41f, hz - 0.2f), Vector3.one * 0.03f, accent);
            }
            Prim(PrimitiveType.Cube, "Valve", eq, new Vector3(pipeLen / 2f + 0.06f, 0.62f, hz - 0.2f), new Vector3(0.1f, 0.1f, 0.1f), steel);
            Prim(PrimitiveType.Sphere, "ValveKnob", eq, new Vector3(pipeLen / 2f + 0.06f, 0.7f, hz - 0.2f), Vector3.one * 0.05f, dry ? ScreenAccent(Color.red) : accent);

            // Climate sensor on a slim stalk near the front-left of the bed: housing and a status LED.
            float sx = -w / 2f + 0.5f, sz = -d / 2f + 0.52f;
            Prim(PrimitiveType.Cylinder, "SensorStalk", eq, new Vector3(sx, 0.62f, sz), new Vector3(0.016f, 0.42f, 0.016f), steel);
            Prim(PrimitiveType.Cube, "SensorHead", eq, new Vector3(sx, 1.05f, sz), new Vector3(0.1f, 0.07f, 0.07f), steel);
            Prim(PrimitiveType.Sphere, "SensorLed", eq, new Vector3(sx, 1.1f, sz - 0.03f), Vector3.one * 0.022f, accent);

            // Control cabinet outside the glass, front-right: pedestal, cabinet, a small glowing display and three status LEDs.
            float cx = w / 2f + 0.3f, cz = -d / 2f + 0.15f;
            Prim(PrimitiveType.Cube, "CabinetBase", eq, new Vector3(cx, 0.04f, cz), new Vector3(0.4f, 0.08f, 0.3f), steel);
            Prim(PrimitiveType.Cube, "Cabinet", eq, new Vector3(cx, 0.48f, cz), new Vector3(0.34f, 0.8f, 0.22f), screen);
            Prim(PrimitiveType.Cube, "CabinetDisplay", eq, new Vector3(cx, 0.68f, cz - 0.115f), new Vector3(0.26f, 0.18f, 0.01f), screenGlow);
            for (int i = 0; i < 3; i++) Prim(PrimitiveType.Sphere, "Led_" + i, eq, new Vector3(cx - 0.08f + i * 0.08f, 0.5f, cz - 0.115f), Vector3.one * 0.02f, accent);
            var pipeToCabinet = Prim(PrimitiveType.Cylinder, "FeedPipe", eq, new Vector3(cx, 0.1f, cz + 0.4f), new Vector3(0.02f, 0.3f, 0.02f), steel);
            pipeToCabinet.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        private static Material ScreenAccent(Color c)
        {
            return PlantKit.LitMaterial($"{MatFolder}/Pod_Alert.mat", c * 0.7f, 0.5f, 0f, c * 1.2f);
        }

        // One recognisable prop per condition so each pod reads at a glance.
        private static void BuildSignature(Transform pod, PodSpec spec, Material steel, Material accent)
        {
            var props = new GameObject("Signature").transform;
            props.SetParent(pod, false);
            float w = spec.Size.x, d = spec.Size.y;
            float hz = d / 2f - 0.04f;

            switch (spec.Condition)
            {
                case ExperimentalCondition.Control:
                    // Sensor mast with a green beacon.
                    Prim(PrimitiveType.Cylinder, "Mast", props, new Vector3(w / 2f - 0.2f, 1.0f, hz - 0.2f), new Vector3(0.04f, 0.9f, 0.04f), steel);
                    Prim(PrimitiveType.Sphere, "Beacon", props, new Vector3(w / 2f - 0.2f, 1.96f, hz - 0.2f), Vector3.one * 0.14f, accent);
                    break;
                case ExperimentalCondition.HeatStress:
                    // Two glowing heater pipes along the back wall plus a warm lamp bar overhead.
                    for (int i = 0; i < 2; i++)
                    {
                        var pipe = Prim(PrimitiveType.Cylinder, "HeaterPipe_" + i, props, new Vector3(0f, 0.38f + i * 0.14f, hz - 0.14f), new Vector3(0.05f, (w - 0.5f) / 2f, 0.05f), accent);
                        pipe.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    }
                    Prim(PrimitiveType.Cube, "LampBar", props, new Vector3(0f, 1.68f, 0f), new Vector3(w - 0.6f, 0.05f, 0.12f), accent);
                    // Wall thermometer: a slim column with a glowing red-orange scale.
                    Prim(PrimitiveType.Cube, "ThermoBody", props, new Vector3(-w / 2f + 0.18f, 1.0f, hz - 0.1f), new Vector3(0.06f, 1.1f, 0.03f), steel);
                    Prim(PrimitiveType.Cube, "ThermoScale", props, new Vector3(-w / 2f + 0.18f, 1.15f, hz - 0.12f), new Vector3(0.025f, 0.8f, 0.015f), accent);
                    break;
                case ExperimentalCondition.LowHumidity:
                    // Dehumidifier cabinet with grille slats.
                    Prim(PrimitiveType.Cube, "Dehumidifier", props, new Vector3(w / 2f - 0.28f, 0.55f, hz - 0.22f), new Vector3(0.36f, 0.95f, 0.30f), steel);
                    for (int i = 0; i < 5; i++)
                        Prim(PrimitiveType.Cube, "Grille_" + i, props, new Vector3(w / 2f - 0.28f, 0.30f + i * 0.14f, hz - 0.065f), new Vector3(0.28f, 0.03f, 0.02f), accent);
                    break;
                case ExperimentalCondition.LowLight:
                    // Shade louvres across the top: dark slats with gaps so the plants stay visible from above.
                    for (int i = 0; i < 9; i++)
                    {
                        float z = -hz + 0.12f + i * ((2f * hz - 0.24f) / 8f);
                        Prim(PrimitiveType.Cube, "Louvre_" + i, props, new Vector3(0f, 1.92f, z), new Vector3(w - 0.1f, 0.025f, 0.15f), ShadeMaterial(steel));
                    }
                    break;
                case ExperimentalCondition.WaterStress:
                    // Small supply tank and a drip line with a few emitters.
                    Prim(PrimitiveType.Cylinder, "Tank", props, new Vector3(w / 2f - 0.26f, 0.5f, hz - 0.26f), new Vector3(0.38f, 0.42f, 0.38f), steel);
                    Prim(PrimitiveType.Cylinder, "TankBand", props, new Vector3(w / 2f - 0.26f, 0.62f, hz - 0.26f), new Vector3(0.40f, 0.03f, 0.40f), accent);
                    var line = Prim(PrimitiveType.Cylinder, "DripLine", props, new Vector3(0f, 0.55f, hz - 0.12f), new Vector3(0.02f, (w - 0.9f) / 2f, 0.02f), steel);
                    line.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    for (int i = 0; i < 4; i++)
                        Prim(PrimitiveType.Sphere, "Emitter_" + i, props, new Vector3(-0.8f + i * 0.5f, 0.50f, hz - 0.12f), Vector3.one * 0.06f, accent);
                    break;
            }
        }

        private static Material shadeMat;
        private static Material ShadeMaterial(Material like)
        {
            if (shadeMat != null) return shadeMat;
            string path = $"{MatFolder}/Pod_Shade.mat";
            shadeMat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (shadeMat == null) { shadeMat = new Material(like.shader); AssetDatabase.CreateAsset(shadeMat, path); }
            shadeMat.shader = like.shader;
            shadeMat.SetColor("_BaseColor", new Color(0.12f, 0.13f, 0.16f));
            EditorUtility.SetDirty(shadeMat);
            return shadeMat;
        }

        private static TMP_Text BuildSign(Transform pod, PodSpec spec, Material steel, Material timber, Material accent, float floor)
        {
            // A modest sign on two short posts, in front of the glass (on the plaza side), set to one side so it never hides the plants.
            // It turns to face the camera at runtime.
            var sign = new GameObject("Sign").transform;
            sign.SetParent(pod, false);
            sign.localPosition = new Vector3(-(spec.Size.x * 0.5f - 0.2f), 0f, -(spec.Size.y / 2f + 0.55f)); // left of the entrance, clear of the bottom-right panel
            sign.gameObject.AddComponent<FaceCameraYaw>();

            const float w = 1.45f, h = 0.5f, y = 0.72f;
            Prim(PrimitiveType.Cylinder, "PostL", sign, new Vector3(-w / 2f + 0.1f, y / 2f, 0f), new Vector3(0.04f, y / 2f, 0.04f), steel);
            Prim(PrimitiveType.Cylinder, "PostR", sign, new Vector3(w / 2f - 0.1f, y / 2f, 0f), new Vector3(0.04f, y / 2f, 0.04f), steel);
            Prim(PrimitiveType.Cube, "Board", sign, new Vector3(0f, y + h / 2f - 0.05f, 0f), new Vector3(w, h, 0.04f), timber);
            Prim(PrimitiveType.Cube, "AccentBar", sign, new Vector3(0f, y - 0.05f + 0.012f, -0.025f), new Vector3(w, 0.04f, 0.015f), accent);

            var board = new GameObject("Text").transform;
            board.SetParent(sign, false);
            board.localPosition = new Vector3(0f, y + h / 2f - 0.05f, -0.03f);
            board.localRotation = Quaternion.identity;

            Text(board, "Title", spec.Title, 1.25f, Color.white, FontStyles.Bold, new Vector3(0f, 0.12f, 0f), new Vector2(w - 0.1f, 0.2f));
            Text(board, "Subtitle", spec.Subtitle, 0.8f, spec.Accent, FontStyles.Normal, new Vector3(0f, -0.03f, 0f), new Vector2(w - 0.1f, 0.14f));
            return Text(board, "Indicator", "31.5°C · 56% RH · light 83%", 0.52f, new Color(0.9f, 0.95f, 0.9f), FontStyles.Normal, new Vector3(0f, -0.15f, 0f), new Vector2(w - 0.1f, 0.1f));
        }

        // The research wing: paved spokes and a ring path linking the plaza to every pod, and a pergola gate across the main path.
        private static void BuildResearchWing(Transform root, Material steel, Material timber, Material pavers)
        {
            Material stone = Mat("BioDome_Stone");
            var wing = new GameObject("ResearchWing").transform;
            wing.SetParent(root, false);
            wing.position = new Vector3(CourtCentre.x, 0f, CourtCentre.y);

            Ring(wing, "RingPath", 5.9f, 1.0f, 0.04f, 48, pavers);
            foreach (PodSpec spec in Specs)
            {
                Vector3 o = spec.Outward;
                var spoke = Prim(PrimitiveType.Cube, "Spoke_" + spec.Condition, wing, o * 5.6f + Vector3.up * 0.04f, new Vector3(1.2f, 0.04f, 3.6f), pavers);
                spoke.transform.localRotation = Quaternion.LookRotation(o, Vector3.up);
            }

            // Pergola gate across the main path, signed for visitors walking east from the fountain.
            float gx = 31.0f - CourtCentre.x;
            Prim(PrimitiveType.Cylinder, "GatePostN", wing, new Vector3(gx, 1.3f, 2.25f), new Vector3(0.16f, 1.3f, 0.16f), steel);
            Prim(PrimitiveType.Cylinder, "GatePostS", wing, new Vector3(gx, 1.3f, -2.25f), new Vector3(0.16f, 1.3f, 0.16f), steel);
            Prim(PrimitiveType.Cube, "GateBeam", wing, new Vector3(gx, 2.55f, 0f), new Vector3(0.22f, 0.34f, 4.9f), timber);
            Prim(PrimitiveType.Cube, "GateSlat", wing, new Vector3(gx, 2.80f, 0f), new Vector3(0.12f, 0.06f, 4.9f), steel);
            var title = Text(wing, "GateTitle", "CLIMATE RESEARCH WING", 2.4f, Color.white, FontStyles.Bold, new Vector3(gx - 0.13f, 2.57f, 0f), new Vector2(4.6f, 0.4f));
            title.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // faces west, towards the fountain
            var sub = Text(wing, "GateSubtitle", "Monstera · five conditions · one control", 1.2f, new Color(0.8f, 0.95f, 0.8f), FontStyles.Normal, new Vector3(gx - 0.13f, 2.37f, 0f), new Vector2(4.6f, 0.25f));
            sub.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // A few natural stones between the pods and the rear wall.
            var rng = new System.Random(4242);
            for (int i = 0; i < 7; i++)
            {
                float a = Mathf.Lerp(-95f, 95f, i / 6f) * Mathf.Deg2Rad + ((float)rng.NextDouble() - 0.5f) * 0.12f;
                float r = 10.6f + (float)rng.NextDouble() * 0.6f;
                float sc = 0.3f + (float)rng.NextDouble() * 0.35f;
                Vector3 p = new Vector3(Mathf.Cos(a) * r, 0.08f, Mathf.Sin(a) * r);
                if (CourtCentre.x + p.x > 47.2 || Mathf.Abs(p.z) > 10.6f) continue;
                var rock = Prim(PrimitiveType.Sphere, "Rock_" + i, wing, p, new Vector3(sc * 1.5f, sc * 0.8f, sc * 1.2f), stone);
                rock.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            }
        }

        // A ring of box segments (a circular path or curb).
        private static void Ring(Transform parent, string name, float radius, float width, float height, int segments, Material material)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            float length = 2f * Mathf.PI * radius / segments * 1.1f;
            for (int i = 0; i < segments; i++)
            {
                float a = i * 2f * Mathf.PI / segments;
                Vector3 p = new Vector3(Mathf.Cos(a) * radius, height / 2f + 0.03f, Mathf.Sin(a) * radius);
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                var seg = Prim(PrimitiveType.Cube, name + "_" + i, group, p, new Vector3(width, height, length), material);
                seg.transform.localRotation = Quaternion.LookRotation(tangent, Vector3.up);
            }
        }

        private static TextMeshPro Text(Transform parent, string name, string text, float size, Color color, FontStyles style, Vector3 localPos, Vector2 rect)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshPro));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tmp = go.GetComponent<TextMeshPro>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = rect;
            return tmp;
        }

        // --- Helpers --------------------------------------------------------------------

        /// <summary>Highest flat floor surface under the point (paths, pavers, stones), found from renderer bounds; 0.1 if none.</summary>
        private static float FloorHeight(Vector2 xz)
        {
            float best = float.NegativeInfinity;
            foreach (Renderer r in Object.FindObjectsByType<Renderer>())
            {
                string rootName = r.transform.root.name;
                if (rootName == RootName || rootName == "BioDome_Plants" || rootName == "BioDome_PlantAdditions") continue;
                Bounds b = r.bounds;
                if (b.size.y > 0.35f || b.max.y < 0.02f) continue; // only thin, floor-like surfaces
                if (xz.x < b.min.x || xz.x > b.max.x || xz.y < b.min.z || xz.y > b.max.z) continue;
                if (b.size.x > 100f) continue; // the huge outside ground
                best = Mathf.Max(best, b.max.y);
            }
            return float.IsNegativeInfinity(best) ? 0.1f : best;
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
