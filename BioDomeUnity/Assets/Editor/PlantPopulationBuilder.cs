using System.Collections.Generic;
using System.Text;
using BioDome.CameraSystem;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Irrigation;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Populates the greenhouse's PlantingZones from a PlantPopulationSettings asset.
    /// Every plant starts at the same growth (one cohort). Variation comes only from controlled seed variants, rotation,
    /// small size changes and jittered positions. Plant counts follow bed size, mature canopy width and spacing.
    /// Re-running replaces the previous plants (single "BioDome_Plants" root), so nothing is duplicated.
    /// </summary>
    public static class PlantPopulationBuilder
    {
        public const string RootName = "BioDome_Plants";
        public const string SettingsPath = "Assets/ScriptableObjects/Settings/PlantPopulationSettings.asset";

        private const string PlantDataPath = "Assets/ScriptableObjects/Plants/Monstera.asset";
        private const string SpeciesPrefabPath = "Assets/Prefabs/Plants/Monstera.prefab";
        private const string ModelFolder = "Assets/Art/Models/Plants";
        private const string ClimatePath = "Assets/ScriptableObjects/Environment/DefaultClimate.asset";

        // Small, fixed, narrow variations between variants (never large enough to suggest different ages).
        private static readonly int[] LeafCountDelta = { -1, 0, 1, 0, -1, 1, 0, 1 };
        private static readonly float[] HeightFactor = { 0.97f, 1.00f, 1.03f, 0.99f, 1.02f, 0.98f, 1.01f, 1.00f };
        private static readonly float[] WidthFactor = { 1.00f, 0.98f, 1.03f, 1.00f, 0.97f, 1.02f, 1.00f, 0.99f };

        private struct Slot
        {
            public PlantingZone Zone;
            public PlantSpeciesEntry Species;
            public Vector3 Local;
            public float Spacing;
        }

        // --- Settings -------------------------------------------------------------

        public static PlantPopulationSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PlantPopulationSettings>(SettingsPath);
            if (settings == null)
            {
                GreenhouseAssetFactory.EnsureFolder(GreenhouseAssetFactory.SettingsFolder);
                settings = ScriptableObject.CreateInstance<PlantPopulationSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            bool changed = false;
            if (settings.species.Count == 0)
            {
                EnsureMonsteraSpecies(settings.initialGrowth01, out GameObject prefab, out PlantData data);
                settings.species.Add(new PlantSpeciesEntry { label = "Monstera", prefab = prefab, data = data, variantCount = 4, baseSeed = 101 });
                changed = true;
            }
            if (settings.zoneRules.Count == 0)
            {
                // Border planters first so their prefix wins; both use the first species until more exist.
                settings.zoneRules.Add(new ZoneRule { zonePrefix = "Border_", speciesIndex = 0 });
                settings.zoneRules.Add(new ZoneRule { zonePrefix = "Bed_", speciesIndex = 0 });
                changed = true;
            }
            if (changed)
            {
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
            return settings;
        }

        /// <summary>Creates the Monstera PlantData asset and species prefab if they do not exist yet.</summary>
        private static void EnsureMonsteraSpecies(float initialGrowth, out GameObject prefab, out PlantData data)
        {
            GreenhouseAssetFactory.EnsureFolder("Assets/ScriptableObjects/Plants");
            GreenhouseAssetFactory.EnsureFolder("Assets/Prefabs/Plants");
            GreenhouseAssetFactory.EnsureFolder(ModelFolder);

            data = AssetDatabase.LoadAssetAtPath<PlantData>(PlantDataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<PlantData>();
                var so = new SerializedObject(data);
                so.FindProperty("displayName").stringValue = "Swiss Cheese Plant";
                so.FindProperty("scientificName").stringValue = "Monstera deliciosa";
                so.FindProperty("description").stringValue = "A climbing aroid with large, glossy leaves that develop splits and holes as they mature.";
                so.FindProperty("speciesId").stringValue = "monstera";
                so.FindProperty("botanicalZone").stringValue = "Tropical foliage";
                so.FindProperty("matureCanopyWidth").floatValue = 1.7f;
                so.FindProperty("matureHeight").floatValue = 1.4f;
                so.FindProperty("daysToMaturity").floatValue = 30f;
                so.FindProperty("idealTemperatureCelsius").vector2Value = new Vector2(18f, 29f);
                so.FindProperty("idealHumidity01").vector2Value = new Vector2(0.5f, 0.85f);
                so.FindProperty("idealLight01").vector2Value = new Vector2(0.25f, 0.75f);
                so.FindProperty("idealSoilMoisture01").vector2Value = new Vector2(0.35f, 0.75f);
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(data, PlantDataPath);
            }

            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpeciesPrefabPath);
            if (prefab != null) return;

            PlantAssetFactory.EnsureMaterials(false, out Material leaf, out Material stem);
            var go = new GameObject("Monstera");
            try
            {
                var generator = go.AddComponent<MonsteraGenerator>();
                generator.ApplyDefaultSettings();
                generator.Configure(1.4f, 1.4f, 9, 0.55f, 0.47f, initialGrowth, 101);
                generator.SetMaterials(leaf, stem);

                Mesh baseMesh = LoadOrCreateMeshAsset($"{ModelFolder}/Monstera_Base_Mesh.asset");
                generator.Rebuild(true, baseMesh);
                EditorUtility.SetDirty(baseMesh);

                var controller = go.AddComponent<PlantController>();
                var controllerObject = new SerializedObject(controller);
                controllerObject.FindProperty("data").objectReferenceValue = data;
                controllerObject.ApplyModifiedPropertiesWithoutUndo();

                prefab = PrefabUtility.SaveAsPrefabAsset(go, SpeciesPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // --- Populate -------------------------------------------------------------

        public static void Populate(PlantPopulationSettings settings)
        {
            PlantingZone[] found = Object.FindObjectsByType<PlantingZone>();
            if (found.Length == 0)
            {
                EditorUtility.DisplayDialog("No planting zones",
                    "The open scene has no PlantingZone objects. Open Assets/Scenes/BioDome_Main.unity (or run BioDome > Build Greenhouse Scene) first.", "OK");
                return;
            }

            var zones = new List<PlantingZone>(found);
            zones.Sort((a, b) => string.CompareOrdinal(a.ZoneName, b.ZoneName)); // stable order for repeatable results

            // Every species used by a rule must be fully set up.
            foreach (PlantingZone zone in zones)
            {
                PlantSpeciesEntry species = settings.ResolveSpecies(zone.ZoneName);
                if (species != null && (species.prefab == null || species.data == null || species.prefab.GetComponent<PlantGenerator>() == null))
                {
                    EditorUtility.DisplayDialog("Species not set up",
                        $"The species for zone '{zone.ZoneName}' needs a prefab with a PlantGenerator and a PlantData asset. Check the Plant Population Settings.", "OK");
                    return;
                }
            }

            EnsureSimulation(settings);
            ClearExisting();

            // Layout: widen spacing automatically if the plant count would exceed the cap.
            float spacingScale = 1f;
            List<Slot> slots = LayoutAll(zones, settings, spacingScale);
            int guard = 0;
            while (slots.Count > settings.maxPlantCount && guard++ < 60)
            {
                spacingScale *= 1.04f;
                slots = LayoutAll(zones, settings, spacingScale);
            }
            bool capped = spacingScale > 1f;

            // Variant meshes (one set per species) built once at the shared initial growth.
            var variants = new Dictionary<PlantSpeciesEntry, Mesh[]>();
            foreach (Slot slot in slots)
            {
                if (!variants.ContainsKey(slot.Species)) variants.Add(slot.Species, BuildVariantMeshes(slot.Species, settings));
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Populate Planting Zones");
            var groups = new Dictionary<PlantingZone, Transform>();
            var rng = new System.Random(settings.seed + 1);
            var placed = new List<(GameObject go, Slot slot)>();

            foreach (Slot slot in slots)
            {
                if (!groups.TryGetValue(slot.Zone, out Transform group))
                {
                    var groupObject = new GameObject($"Plants_{slot.Zone.ZoneName}");
                    groupObject.transform.SetParent(root.transform, false);
                    group = groupObject.transform;
                    groups.Add(slot.Zone, group);
                }

                Mesh[] meshes = variants[slot.Species];
                int variant = rng.Next(meshes.Length);

                var plant = (GameObject)PrefabUtility.InstantiatePrefab(slot.Species.prefab, group);
                plant.name = $"{slot.Species.label}_{placed.Count + 1:000}";

                var generator = plant.GetComponent<PlantGenerator>();
                ApplyVariant(generator, variant, settings, slot.Species);
                generator.DisplayMesh(meshes[variant], settings.initialGrowth01);

                float yaw = settings.randomRotation ? (float)rng.NextDouble() * 360f : 0f;
                float scale = 1f + ((float)rng.NextDouble() * 2f - 1f) * settings.sizeVariation;
                plant.transform.SetPositionAndRotation(slot.Zone.transform.TransformPoint(slot.Local), Quaternion.Euler(0f, yaw, 0f));
                plant.transform.localScale = Vector3.one * scale;

                placed.Add((plant, slot));
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            AssetDatabase.SaveAssets();

            Debug.Log(BuildReport(settings, placed, zones.Count, spacingScale, capped));
        }

        // --- Layout ---------------------------------------------------------------

        private static List<Slot> LayoutAll(List<PlantingZone> zones, PlantPopulationSettings s, float spacingScale)
        {
            var result = new List<Slot>();
            var rng = new System.Random(s.seed);

            foreach (PlantingZone zone in zones)
            {
                PlantSpeciesEntry species = s.ResolveSpecies(zone.ZoneName);
                if (species == null || species.data == null) continue;

                float canopy = species.data.MatureCanopyWidth;
                float spacing = s.ComputeSpacing(canopy) * spacingScale;
                float margin = s.edgeMarginFactor * canopy;
                float jitter = Mathf.Min(s.positionJitter, 0.15f) * spacing;

                foreach (Vector2 p in LayoutZone(zone.Size, spacing, margin, jitter, s.staggerRows, rng))
                {
                    result.Add(new Slot { Zone = zone, Species = species, Local = new Vector3(p.x, 0f, p.y), Spacing = spacing });
                }
            }
            return result;
        }

        /// <summary>
        /// Staggered (hexagonal) grid inside a rectangle, centred, kept <paramref name="margin"/> from every edge.
        /// Returns points in the zone's local XZ plane. If the rectangle is narrower than the margins allow,
        /// plants run along the centre line.
        /// </summary>
        public static List<Vector2> LayoutZone(Vector2 size, float spacing, float margin, float jitter, bool stagger, System.Random rng)
        {
            var points = new List<Vector2>();
            float usableX = Mathf.Max(0f, size.x - 2f * margin);
            float usableZ = Mathf.Max(0f, size.y - 2f * margin);

            float rowStep = stagger ? spacing * 0.866f : spacing;
            int rows = Mathf.Max(1, Mathf.FloorToInt(usableZ / rowStep) + 1);
            int evenCount = Mathf.Max(1, Mathf.FloorToInt(usableX / spacing) + 1);
            float x0 = -(evenCount - 1) * spacing * 0.5f;
            float z0 = -(rows - 1) * rowStep * 0.5f;

            for (int r = 0; r < rows; r++)
            {
                float z = z0 + r * rowStep;
                bool odd = stagger && r % 2 == 1;
                float start = odd ? x0 + spacing * 0.5f : x0;

                for (float x = start; x <= usableX * 0.5f + 1e-4f; x += spacing)
                {
                    float jx = ((float)rng.NextDouble() * 2f - 1f) * jitter;
                    float jz = ((float)rng.NextDouble() * 2f - 1f) * jitter;
                    points.Add(new Vector2(
                        Mathf.Clamp(x + jx, -usableX * 0.5f, usableX * 0.5f),
                        Mathf.Clamp(z + jz, -usableZ * 0.5f, usableZ * 0.5f)));
                }
            }
            return points;
        }

        // --- Variants -------------------------------------------------------------

        internal static void ApplyVariant(PlantGenerator generator, int index, PlantPopulationSettings s, PlantSpeciesEntry species)
        {
            int i = index % LeafCountDelta.Length;
            generator.Vary(species.baseSeed + index * 17, LeafCountDelta[i], HeightFactor[i], WidthFactor[i], s.initialGrowth01, s.meshDetail);
        }

        /// <summary>Builds (or refreshes in place) the initial-growth mesh assets for each variant of a species.</summary>
        internal static Mesh[] BuildVariantMeshes(PlantSpeciesEntry species, PlantPopulationSettings s)
        {
            GreenhouseAssetFactory.EnsureFolder(ModelFolder);
            int count = Mathf.Clamp(species.variantCount, 1, 8);
            var meshes = new Mesh[count];

            for (int i = 0; i < count; i++)
            {
                var temp = (GameObject)PrefabUtility.InstantiatePrefab(species.prefab);
                try
                {
                    var generator = temp.GetComponent<PlantGenerator>();
                    ApplyVariant(generator, i, s, species);

                    Mesh asset = LoadOrCreateMeshAsset($"{ModelFolder}/{species.prefab.name}_Initial_V{i + 1}_Mesh.asset");
                    generator.Rebuild(true, asset);
                    EditorUtility.SetDirty(asset);
                    meshes[i] = asset;
                }
                finally
                {
                    Object.DestroyImmediate(temp);
                }
            }
            return meshes;
        }

        internal static Mesh LoadOrCreateMeshAsset(string path)
        {
            string expectedName = System.IO.Path.GetFileNameWithoutExtension(path);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
            {
                if (mesh.name != expectedName) mesh.name = expectedName;
                return mesh;
            }

            mesh = new Mesh { name = expectedName };
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // --- Simulation objects ---------------------------------------------------

        /// <summary>
        /// Makes sure the scene has a GreenhouseManager, EnvironmentController and IrrigationController so the plants
        /// actually grow, and keeps the manager's initial growth equal to the population settings.
        /// </summary>
        private static void EnsureSimulation(PlantPopulationSettings settings)
        {
            var manager = Object.FindAnyObjectByType<GreenhouseManager>();
            if (manager == null)
            {
                var go = new GameObject("Simulation");
                Undo.RegisterCreatedObjectUndo(go, "Add Simulation");
                var environment = go.AddComponent<EnvironmentController>();
                var irrigation = go.AddComponent<IrrigationController>();
                manager = go.AddComponent<GreenhouseManager>();

                var envObject = new SerializedObject(environment);
                envObject.FindProperty("climateProfile").objectReferenceValue = LoadOrCreateClimate();
                GameObject sun = GameObject.Find("Sun");
                if (sun != null) envObject.FindProperty("sunLight").objectReferenceValue = sun.GetComponent<Light>();
                envObject.ApplyModifiedPropertiesWithoutUndo();

                // A dense planting needs far more water than the default tank holds, so connect a mains supply.
                var irrObject = new SerializedObject(irrigation);
                SetFloat(irrObject, "reservoirCapacityLitres", 2000f);
                SetFloat(irrObject, "startingWaterLitres", 2000f);
                SetFloat(irrObject, "flowRateLitresPerHour", 150f);
                SetFloat(irrObject, "mainsSupplyLitresPerHour", 40f);
                irrObject.ApplyModifiedPropertiesWithoutUndo();

                var managerObject = new SerializedObject(manager);
                managerObject.FindProperty("environment").objectReferenceValue = environment;
                managerObject.FindProperty("irrigation").objectReferenceValue = irrigation;
                managerObject.FindProperty("cameraController").objectReferenceValue = Object.FindAnyObjectByType<CameraController>();
                managerObject.ApplyModifiedPropertiesWithoutUndo();
            }

            var sync = new SerializedObject(manager);
            SetFloat(sync, "initialGrowth01", settings.initialGrowth01);
            sync.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ClimateProfile LoadOrCreateClimate()
        {
            string[] guids = AssetDatabase.FindAssets("t:ClimateProfile");
            if (guids.Length > 0) return AssetDatabase.LoadAssetAtPath<ClimateProfile>(AssetDatabase.GUIDToAssetPath(guids[0]));

            GreenhouseAssetFactory.EnsureFolder("Assets/ScriptableObjects/Environment");
            var profile = ScriptableObject.CreateInstance<ClimateProfile>();
            AssetDatabase.CreateAsset(profile, ClimatePath);
            return profile;
        }

        private static void SetFloat(SerializedObject so, string property, float value)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p != null) p.floatValue = value;
            else Debug.LogWarning($"BioDome: property '{property}' not found on {so.targetObject.GetType().Name}.");
        }

        // --- Clear and report -----------------------------------------------------

        public static bool ClearExisting()
        {
            bool removed = false;
            GameObject existing;
            while ((existing = GameObject.Find(RootName)) != null)
            {
                Undo.DestroyObjectImmediate(existing);
                removed = true;
            }
            return removed;
        }

        /// <summary>Checks what was just placed and returns a Console report with counts per botanical zone.</summary>
        private static string BuildReport(PlantPopulationSettings s, List<(GameObject go, Slot slot)> placed, int zoneCount, float spacingScale, bool capped)
        {
            var text = new StringBuilder();
            text.AppendLine($"BioDome: placed {placed.Count} plants on {zoneCount} planting zones under '{RootName}' (density {s.densityPreset}, x{s.plantDensity:0.00}).");
            if (capped) text.AppendLine($"  [WARN] Plant cap of {s.maxPlantCount} reached: spacing was widened by x{spacingScale:0.00}.");

            // Counts by botanical zone and by planting zone.
            var byBotanical = new SortedDictionary<string, int>();
            var byZone = new SortedDictionary<string, int>();
            foreach (var entry in placed)
            {
                string botanical = string.IsNullOrEmpty(entry.slot.Species.data.BotanicalZone) ? entry.slot.Species.label : entry.slot.Species.data.BotanicalZone;
                byBotanical[botanical] = byBotanical.TryGetValue(botanical, out int b) ? b + 1 : 1;
                string zone = entry.slot.Zone.ZoneName;
                byZone[zone] = byZone.TryGetValue(zone, out int z) ? z + 1 : 1;
            }
            text.AppendLine("  Plants by botanical zone:");
            foreach (var pair in byBotanical) text.AppendLine($"    {pair.Key}: {pair.Value}");
            text.AppendLine("  Plants by planting zone:");
            foreach (var pair in byZone) text.AppendLine($"    {pair.Key}: {pair.Value}");

            // Verification.
            int outside = 0, floating = 0, wrongSpecies = 0;
            float minGrowth = float.MaxValue, maxGrowth = float.MinValue;
            float closest = float.MaxValue, closestRatio = float.MaxValue;
            var positions = new Dictionary<PlantingZone, List<(Vector3 pos, float spacing)>>();

            foreach (var entry in placed)
            {
                Vector3 position = entry.go.transform.position;
                if (!entry.slot.Zone.Contains(position)) outside++;
                if (Mathf.Abs(position.y - entry.slot.Zone.transform.position.y) > 0.01f) floating++;

                var controller = entry.go.GetComponent<PlantController>();
                if (controller == null || controller.Data != entry.slot.Species.data) wrongSpecies++;

                var generator = entry.go.GetComponent<PlantGenerator>();
                minGrowth = Mathf.Min(minGrowth, generator.Growth01);
                maxGrowth = Mathf.Max(maxGrowth, generator.Growth01);

                if (!positions.TryGetValue(entry.slot.Zone, out var list))
                {
                    list = new List<(Vector3, float)>();
                    positions.Add(entry.slot.Zone, list);
                }
                list.Add((position, entry.slot.Spacing));
            }
            foreach (var list in positions.Values)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        float d = Vector3.Distance(list[i].pos, list[j].pos);
                        closest = Mathf.Min(closest, d);
                        closestRatio = Mathf.Min(closestRatio, d / list[i].spacing);
                    }
                }
            }

            int rootCount = 0;
            foreach (GameObject go in Object.FindObjectsByType<GameObject>())
                if (go.name == RootName && go.transform.parent == null) rootCount++;

            text.AppendLine("  Checks:");
            text.AppendLine(Line(rootCount == 1, $"One '{RootName}' root (no duplicates from re-running)."));
            text.AppendLine(Line(Mathf.Approximately(minGrowth, maxGrowth) && placed.Count > 0,
                $"Initial growth identical for all plants ({minGrowth * 100f:0.#}% to {maxGrowth * 100f:0.#}%)."));
            text.AppendLine(Line(outside == 0, $"{outside} plants outside their planting zone."));
            text.AppendLine(Line(floating == 0, $"{floating} plants not resting on the soil surface."));
            text.AppendLine(Line(wrongSpecies == 0, $"{wrongSpecies} plants with the wrong species configuration."));
            text.AppendLine(Line(closestRatio >= 0.7f || placed.Count < 2,
                placed.Count < 2 ? "Overlap check skipped." : $"Closest neighbours {closest:0.00} m apart ({closestRatio * 100f:0}% of the target spacing)."));
            text.AppendLine("  Plants stay inside the bed soil areas with a canopy-based margin, so paths and cross paths remain clear.");
            text.AppendLine("  Save the scene (Ctrl+S) to keep the plants.");
            return text.ToString();
        }

        private static string Line(bool ok, string message)
        {
            return (ok ? "    [PASS] " : "    [FAIL] ") + message;
        }
    }
}
