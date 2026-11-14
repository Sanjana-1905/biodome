using System;
using System.Collections.Generic;
using System.Text;
using BioDome.Plants;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Creates the data, prefab and mesh assets for every added species (Bird of Paradise, Banana, Areca Palm, Boston Fern,
    /// Calathea, Philodendron, Anthurium, Orchid, Moss and a creeping ground cover), the extra materials they need, and the
    /// "lush" preset plan that says which zones receive them.
    ///
    /// Safety: nothing is ever overwritten. Existing PlantData, prefabs, plans and materials are kept. The one exception is
    /// the Bird of Paradise and Banana PREFABS made earlier: if they still lack flower materials they are upgraded in place
    /// to the improved generators (their PlantData is untouched). The Monstera assets and the open scene are never touched.
    /// </summary>
    public static class PlantSpeciesAssetBuilder
    {
        public const string PlanPath = "Assets/ScriptableObjects/Settings/PlantAdditionPlan.asset";
        public const string LushPlanPath = "Assets/ScriptableObjects/Settings/PlantAdditionPlan_Lush.asset";
        private const string DataFolder = "Assets/ScriptableObjects/Plants";
        private const string PrefabFolder = "Assets/Prefabs/Plants";
        private const string ModelFolder = "Assets/Art/Models/Plants";

        /// <summary>Everything that differs between species. The simulation itself is identical for all of them.</summary>
        private sealed class Spec
        {
            public string AssetName;
            public string DisplayName, ScientificName, Description, SpeciesId, BotanicalZone;
            public float CanopyWidth, MatureHeight, DaysToMaturity;
            public Vector2 Temperature, Humidity, Light, SoilMoisture;
            public float TemperatureTolerance = 10f, HumidityTolerance = 0.4f;
            public float WaterCapacity, WaterUse;
            public float Spring, Summer, Autumn, Winter;
            public int Seed;

            public Func<GameObject, PlantGenerator> AddGenerator;
            public Action<PlantGenerator> Setup;
            public string LeafMaterial = "BioDome_Leaf";
            public string AccentMaterial, Accent2Material;
        }

        private static readonly Spec[] Specs =
        {
            new Spec
            {
                AssetName = "BirdOfParadise", DisplayName = "Bird of Paradise", ScientificName = "Strelitzia reginae",
                Description = "A clumping evergreen whose large, stiff, blue-green paddle leaves rise on long petioles in a flat fan.",
                SpeciesId = "bird_of_paradise", BotanicalZone = "Tropical flowering",
                CanopyWidth = 1.6f, MatureHeight = 1.8f, DaysToMaturity = 45f,
                Temperature = new Vector2(18f, 32f), Humidity = new Vector2(0.40f, 0.75f),
                Light = new Vector2(0.45f, 0.90f), SoilMoisture = new Vector2(0.30f, 0.70f),
                WaterCapacity = 6f, WaterUse = 0.045f,
                Spring = 1.0f, Summer = 1.2f, Autumn = 0.5f, Winter = 0.15f, Seed = 301,
                AddGenerator = go => go.AddComponent<BirdOfParadiseGenerator>(),
                AccentMaterial = PlantMaterialFactory.BirdSepal, Accent2Material = PlantMaterialFactory.BirdPetal
            },
            new Spec
            {
                AssetName = "Banana", DisplayName = "Banana Plant", ScientificName = "Musa acuminata",
                Description = "A fast, thirsty giant: a thick pseudostem crowned with enormous torn, arching paddle leaves.",
                SpeciesId = "banana", BotanicalZone = "Tropical canopy",
                CanopyWidth = 2.4f, MatureHeight = 2.8f, DaysToMaturity = 40f,
                Temperature = new Vector2(22f, 32f), Humidity = new Vector2(0.60f, 0.90f),
                Light = new Vector2(0.50f, 0.95f), SoilMoisture = new Vector2(0.50f, 0.85f),
                TemperatureTolerance = 8f,
                WaterCapacity = 12f, WaterUse = 0.14f,
                Spring = 1.1f, Summer = 1.3f, Autumn = 0.5f, Winter = 0.1f, Seed = 401,
                AddGenerator = go => go.AddComponent<BananaGenerator>(),
                AccentMaterial = PlantMaterialFactory.BananaBud
            },
            new Spec
            {
                AssetName = "ArecaPalm", DisplayName = "Areca Palm", ScientificName = "Dypsis lutescens",
                Description = "A clumping palm of slender canes topped with long, arching, feathery fronds.",
                SpeciesId = "areca_palm", BotanicalZone = "Tropical canopy",
                CanopyWidth = 1.8f, MatureHeight = 2.2f, DaysToMaturity = 60f,
                Temperature = new Vector2(18f, 30f), Humidity = new Vector2(0.40f, 0.80f),
                Light = new Vector2(0.40f, 0.85f), SoilMoisture = new Vector2(0.35f, 0.70f),
                WaterCapacity = 8f, WaterUse = 0.07f,
                Spring = 1.0f, Summer = 1.2f, Autumn = 0.5f, Winter = 0.15f, Seed = 501,
                AddGenerator = go => go.AddComponent<ArecaPalmGenerator>(),
                LeafMaterial = PlantMaterialFactory.Frond
            },
            new Spec
            {
                AssetName = "BostonFern", DisplayName = "Boston Fern", ScientificName = "Nephrolepis exaltata",
                Description = "A dense, rounded mound of long, arching, finely divided fronds that loves humid air.",
                SpeciesId = "boston_fern", BotanicalZone = "Understory",
                CanopyWidth = 0.95f, MatureHeight = 0.6f, DaysToMaturity = 25f,
                Temperature = new Vector2(16f, 27f), Humidity = new Vector2(0.60f, 0.95f),
                Light = new Vector2(0.15f, 0.55f), SoilMoisture = new Vector2(0.45f, 0.85f),
                WaterCapacity = 3f, WaterUse = 0.035f,
                Spring = 1.2f, Summer = 1.0f, Autumn = 0.7f, Winter = 0.3f, Seed = 601,
                AddGenerator = go => go.AddComponent<BostonFernGenerator>(),
                LeafMaterial = PlantMaterialFactory.Frond
            },
            new Spec
            {
                AssetName = "Calathea", DisplayName = "Calathea", ScientificName = "Calathea ornata",
                Description = "An upright rosette of broad, wavy, striped leaves. Fussy about humid air.",
                SpeciesId = "calathea", BotanicalZone = "Understory",
                CanopyWidth = 0.8f, MatureHeight = 0.7f, DaysToMaturity = 35f,
                Temperature = new Vector2(18f, 28f), Humidity = new Vector2(0.65f, 0.95f),
                Light = new Vector2(0.15f, 0.50f), SoilMoisture = new Vector2(0.45f, 0.80f),
                HumidityTolerance = 0.3f,
                WaterCapacity = 3f, WaterUse = 0.03f,
                Spring = 1.1f, Summer = 1.1f, Autumn = 0.6f, Winter = 0.25f, Seed = 701,
                AddGenerator = go => go.AddComponent<CalatheaGenerator>(),
                LeafMaterial = PlantMaterialFactory.Calathea
            },
            new Spec
            {
                AssetName = "Philodendron", DisplayName = "Heartleaf Philodendron", ScientificName = "Philodendron hederaceum",
                Description = "A tough, bushy plant of many vines carrying small glossy heart-shaped leaves.",
                SpeciesId = "philodendron", BotanicalZone = "Tropical foliage",
                CanopyWidth = 1.1f, MatureHeight = 0.9f, DaysToMaturity = 28f,
                Temperature = new Vector2(16f, 30f), Humidity = new Vector2(0.40f, 0.85f),
                Light = new Vector2(0.20f, 0.70f), SoilMoisture = new Vector2(0.30f, 0.75f),
                TemperatureTolerance = 12f,
                WaterCapacity = 4f, WaterUse = 0.04f,
                Spring = 1.2f, Summer = 1.1f, Autumn = 0.6f, Winter = 0.25f, Seed = 801,
                AddGenerator = go => go.AddComponent<PhilodendronGenerator>(),
                LeafMaterial = PlantMaterialFactory.LeafGlossy
            },
            new Spec
            {
                AssetName = "Anthurium", DisplayName = "Anthurium", ScientificName = "Anthurium andraeanum",
                Description = "Glossy heart-shaped leaves and waxy red spathes with a cream spadix.",
                SpeciesId = "anthurium", BotanicalZone = "Tropical flowering",
                CanopyWidth = 0.6f, MatureHeight = 0.6f, DaysToMaturity = 35f,
                Temperature = new Vector2(18f, 29f), Humidity = new Vector2(0.60f, 0.90f),
                Light = new Vector2(0.30f, 0.65f), SoilMoisture = new Vector2(0.40f, 0.75f),
                WaterCapacity = 2.5f, WaterUse = 0.03f,
                Spring = 1.1f, Summer = 1.1f, Autumn = 0.6f, Winter = 0.25f, Seed = 901,
                AddGenerator = go => go.AddComponent<AnthuriumGenerator>(),
                LeafMaterial = PlantMaterialFactory.LeafGlossy,
                AccentMaterial = PlantMaterialFactory.Spathe, Accent2Material = PlantMaterialFactory.Spadix
            },
            new Spec
            {
                AssetName = "Orchid", DisplayName = "Moth Orchid", ScientificName = "Phalaenopsis amabilis",
                Description = "Broad glossy leaves and arching spikes of flat, long-lasting blooms. Likes to dry out between drinks.",
                SpeciesId = "orchid", BotanicalZone = "Tropical flowering",
                CanopyWidth = 0.4f, MatureHeight = 0.5f, DaysToMaturity = 40f,
                Temperature = new Vector2(18f, 28f), Humidity = new Vector2(0.50f, 0.85f),
                Light = new Vector2(0.25f, 0.60f), SoilMoisture = new Vector2(0.20f, 0.50f),
                WaterCapacity = 1.2f, WaterUse = 0.015f,
                Spring = 0.9f, Summer = 1.0f, Autumn = 0.8f, Winter = 0.5f, Seed = 1001,
                AddGenerator = go => go.AddComponent<OrchidGenerator>(),
                LeafMaterial = PlantMaterialFactory.LeafGlossy,
                AccentMaterial = PlantMaterialFactory.OrchidPetal, Accent2Material = PlantMaterialFactory.OrchidLip
            },
            new Spec
            {
                AssetName = "Moss", DisplayName = "Cushion Moss", ScientificName = "Leucobryum glaucum",
                Description = "A dense, bumpy green cushion that carpets damp, shaded soil.",
                SpeciesId = "moss", BotanicalZone = "Ground cover",
                CanopyWidth = 0.9f, MatureHeight = 0.09f, DaysToMaturity = 20f,
                Temperature = new Vector2(8f, 25f), Humidity = new Vector2(0.70f, 1.00f),
                Light = new Vector2(0.05f, 0.50f), SoilMoisture = new Vector2(0.50f, 0.90f),
                TemperatureTolerance = 8f, HumidityTolerance = 0.3f,
                WaterCapacity = 1f, WaterUse = 0.01f,
                Spring = 1.2f, Summer = 0.8f, Autumn = 1.0f, Winter = 0.6f, Seed = 1101,
                AddGenerator = go => go.AddComponent<GroundCoverGenerator>(),
                Setup = g => ((GroundCoverGenerator)g).SetStyle(GroundCoverStyle.MossCushion),
                LeafMaterial = PlantMaterialFactory.Moss
            },
            new Spec
            {
                AssetName = "CreepingGroundCover", DisplayName = "Baby's Tears", ScientificName = "Soleirolia soleirolii",
                Description = "A low creeping carpet of wiry stems and tiny round leaves.",
                SpeciesId = "ground_cover", BotanicalZone = "Ground cover",
                CanopyWidth = 0.8f, MatureHeight = 0.06f, DaysToMaturity = 18f,
                Temperature = new Vector2(10f, 26f), Humidity = new Vector2(0.55f, 0.95f),
                Light = new Vector2(0.10f, 0.60f), SoilMoisture = new Vector2(0.45f, 0.85f),
                WaterCapacity = 1f, WaterUse = 0.012f,
                Spring = 1.2f, Summer = 1.0f, Autumn = 0.8f, Winter = 0.4f, Seed = 1201,
                AddGenerator = go => go.AddComponent<GroundCoverGenerator>(),
                Setup = g => ((GroundCoverGenerator)g).SetStyle(GroundCoverStyle.Creeping),
                LeafMaterial = PlantMaterialFactory.Moss
            }
        };

        [MenuItem("BioDome/Plants/Create Or Upgrade Species Assets")]
        public static void CreateAssets()
        {
            GreenhouseAssetFactory.EnsureFolder(DataFolder);
            GreenhouseAssetFactory.EnsureFolder(PrefabFolder);
            GreenhouseAssetFactory.EnsureFolder(ModelFolder);
            GreenhouseAssetFactory.EnsureFolder(GreenhouseAssetFactory.SettingsFolder);

            var log = new StringBuilder("BioDome: species assets\n");
            log.Append(PlantMaterialFactory.EnsureAll());
            PlantAssetFactory.EnsureMaterials(false, out Material leaf, out Material stem);

            float initialGrowth = LoadInitialGrowth();
            foreach (Spec spec in Specs)
            {
                PlantData data = EnsureData(spec, log);
                EnsurePrefab(spec, data, leaf, stem, initialGrowth, log);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(log.ToString());
        }

        [MenuItem("BioDome/Plants/Create Lush Preset Plan")]
        public static void CreateLushPlan()
        {
            CreateAssets(); // makes sure every species exists first
            var log = new StringBuilder();
            PlantAdditionPlan plan = EnsureLushPlan(log);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString(), plan);
            Selection.activeObject = plan;
        }

        public static PlantAdditionPlan LoadOrCreateLushPlan()
        {
            var plan = AssetDatabase.LoadAssetAtPath<PlantAdditionPlan>(LushPlanPath);
            if (plan != null) return plan;
            CreateAssets();
            return EnsureLushPlan(new StringBuilder());
        }

        private static float LoadInitialGrowth()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PlantPopulationSettings>(PlantPopulationBuilder.SettingsPath);
            return settings != null ? settings.initialGrowth01 : 0.12f;
        }

        // --- Data ---------------------------------------------------------------------

        private static PlantData EnsureData(Spec s, StringBuilder log)
        {
            string path = $"{DataFolder}/{s.AssetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<PlantData>(path);
            if (existing != null)
            {
                log.AppendLine($"  kept existing PlantData: {path}");
                return existing;
            }

            var data = ScriptableObject.CreateInstance<PlantData>();
            var so = new SerializedObject(data);
            so.FindProperty("displayName").stringValue = s.DisplayName;
            so.FindProperty("scientificName").stringValue = s.ScientificName;
            so.FindProperty("description").stringValue = s.Description;
            so.FindProperty("speciesId").stringValue = s.SpeciesId;
            so.FindProperty("botanicalZone").stringValue = s.BotanicalZone;
            so.FindProperty("matureCanopyWidth").floatValue = s.CanopyWidth;
            so.FindProperty("matureHeight").floatValue = s.MatureHeight;
            so.FindProperty("idealTemperatureCelsius").vector2Value = s.Temperature;
            so.FindProperty("idealHumidity01").vector2Value = s.Humidity;
            so.FindProperty("idealLight01").vector2Value = s.Light;
            so.FindProperty("idealSoilMoisture01").vector2Value = s.SoilMoisture;
            so.FindProperty("temperatureTolerance").floatValue = s.TemperatureTolerance;
            so.FindProperty("humidityTolerance").floatValue = s.HumidityTolerance;
            so.FindProperty("waterCapacityLitres").floatValue = s.WaterCapacity;
            so.FindProperty("waterUseLitresPerHour").floatValue = s.WaterUse;
            so.FindProperty("daysToMaturity").floatValue = s.DaysToMaturity;
            so.FindProperty("springGrowthMultiplier").floatValue = s.Spring;
            so.FindProperty("summerGrowthMultiplier").floatValue = s.Summer;
            so.FindProperty("autumnGrowthMultiplier").floatValue = s.Autumn;
            so.FindProperty("winterGrowthMultiplier").floatValue = s.Winter;
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(data, path);
            log.AppendLine($"  created PlantData: {path}");
            return data;
        }

        // --- Prefabs --------------------------------------------------------------------

        private static void EnsurePrefab(Spec s, PlantData data, Material leaf, Material stem, float initialGrowth, StringBuilder log)
        {
            string path = $"{PrefabFolder}/{s.AssetName}.prefab";
            string baseMeshPath = $"{ModelFolder}/{s.AssetName}_Base_Mesh.asset";
            Material leafMaterial = PlantMaterialFactory.Load(s.LeafMaterial) ?? leaf;
            Material accent = s.AccentMaterial != null ? PlantMaterialFactory.Load(s.AccentMaterial) : null;
            Material accent2 = s.Accent2Material != null ? PlantMaterialFactory.Load(s.Accent2Material) : null;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                UpgradeExistingPrefab(s, path, baseMeshPath, leafMaterial, stem, accent, accent2, initialGrowth, log);
                return;
            }

            var go = new GameObject(s.AssetName);
            try
            {
                PlantGenerator generator = s.AddGenerator(go);
                s.Setup?.Invoke(generator);
                generator.ApplyDefaultSettings();
                generator.SetGrowthAndSeed(initialGrowth, s.Seed);
                generator.SetMaterials(leafMaterial, stem);
                generator.SetAccentMaterials(accent, accent2);

                // Build straight into a mesh asset so the prefab keeps a real asset reference (same as the Monstera prefab).
                Mesh baseMesh = PlantPopulationBuilder.LoadOrCreateMeshAsset(baseMeshPath);
                generator.Rebuild(true, baseMesh);
                EditorUtility.SetDirty(baseMesh);

                var controller = go.AddComponent<PlantController>();
                var controllerObject = new SerializedObject(controller);
                controllerObject.FindProperty("data").objectReferenceValue = data;
                controllerObject.ApplyModifiedPropertiesWithoutUndo();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                log.AppendLine($"  created prefab: {path}");
                PlantMeshValidator.ValidateAndLog(prefab);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// The Bird of Paradise and Banana prefabs made earlier lack flower materials. Upgrade them to the improved generators in
        /// place (settings, materials and base mesh). A prefab that already has its accent material is left alone.
        /// </summary>
        private static void UpgradeExistingPrefab(Spec s, string path, string baseMeshPath, Material leafMaterial, Material stem,
            Material accent, Material accent2, float initialGrowth, StringBuilder log)
        {
            if (accent == null)
            {
                log.AppendLine($"  kept existing prefab: {path}");
                return;
            }

            bool upgraded = false;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var generator = root.GetComponent<PlantGenerator>();
                if (generator == null || generator.HasAccentMaterial)
                {
                    log.AppendLine($"  kept existing prefab (already up to date): {path}");
                    return;
                }

                generator.ApplyDefaultSettings();
                generator.SetGrowthAndSeed(initialGrowth, s.Seed);
                generator.SetMaterials(leafMaterial, stem);
                generator.SetAccentMaterials(accent, accent2);

                Mesh baseMesh = PlantPopulationBuilder.LoadOrCreateMeshAsset(baseMeshPath);
                generator.Rebuild(true, baseMesh);
                EditorUtility.SetDirty(baseMesh);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                upgraded = true;
                log.AppendLine($"  UPGRADED existing prefab to the improved generator (PlantData untouched): {path}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // Plants already placed in a scene show the stored initial-growth variant meshes; rebuild those (same seeds as when they were
            // placed) so they match the improved generator straight away.
            var settings = AssetDatabase.LoadAssetAtPath<PlantPopulationSettings>(PlantPopulationBuilder.SettingsPath);
            var upgradedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (upgraded && settings != null && upgradedPrefab != null)
            {
                PlantPopulationBuilder.BuildVariantMeshes(
                    new PlantSpeciesEntry { label = s.AssetName, prefab = upgradedPrefab, variantCount = 4, baseSeed = s.Seed }, settings);
                log.AppendLine($"  refreshed the 4 initial-growth variant meshes of {s.AssetName}");
            }
        }

        // --- Lush plan ----------------------------------------------------------------------

        private static PlantAdditionPlan EnsureLushPlan(StringBuilder log)
        {
            var existing = AssetDatabase.LoadAssetAtPath<PlantAdditionPlan>(LushPlanPath);
            if (existing != null)
            {
                log.AppendLine($"BioDome: kept existing lush plan: {LushPlanPath} (edit it to change the arrangement)");
                return existing;
            }

            var plan = ScriptableObject.CreateInstance<PlantAdditionPlan>();
            plan.seed = 33;
            plan.maxTotalAdded = 450;

            // Tall statement plants at the backs and corners of the back beds and along the wall borders.
            Add(plan, "BirdOfParadise", PlantLayer.Statement, PlacementBias.BackRow, 3, 0.9f, 1.5f, 0.7f, 0.4f, 0.4f, "Bed_N2_1", "Bed_N2_3", "Bed_S2_1", "Bed_S2_3");
            Add(plan, "Banana", PlantLayer.Statement, PlacementBias.BackRow, 2, 1.1f, 2.2f, 0.6f, 0.35f, 0.6f, "Bed_N1_2", "Bed_S1_2");
            Add(plan, "ArecaPalm", PlantLayer.Statement, PlacementBias.Corners, 2, 0.9f, 2.0f, 0.6f, 0.4f, 0.5f, "Bed_N2_*", "Bed_S2_*");
            Add(plan, "ArecaPalm", PlantLayer.Statement, PlacementBias.Anywhere, 5, 0.65f, 4.0f, 0.5f, 0.45f, 0.05f, "Border_N", "Border_S");

            // Medium foliage in the middle beds, layered.
            Add(plan, "Philodendron", PlantLayer.Mid, PlacementBias.Anywhere, 3, 0.8f, 1.6f, 0.6f, 0.5f, 0.4f, "Bed_N1_*", "Bed_S1_*", "Bed_N2_2", "Bed_S2_2");
            Add(plan, "Calathea", PlantLayer.Mid, PlacementBias.Center, 4, 0.6f, 0.9f, 0.5f, 0.5f, 0.4f, "Bed_N1_1", "Bed_S1_1", "Bed_N1_3", "Bed_S1_3", "Bed_N2_2", "Bed_S2_2");
            Add(plan, "Anthurium", PlantLayer.Low, PlacementBias.FrontRow, 4, 0.55f, 0.8f, 0.6f, 0.5f, 0.35f, "Bed_N1_*", "Bed_S1_*");

            // Small foliage toward the aisles, then ground cover filling the gaps.
            Add(plan, "Orchid", PlantLayer.Low, PlacementBias.FrontRow, 3, 0.5f, 0.7f, 0.5f, 0.5f, 0.3f, "Bed_N*", "Bed_S*");
            Add(plan, "BostonFern", PlantLayer.Low, PlacementBias.Edges, 5, 0.5f, 0.6f, 0.55f, 0.55f, 0.25f, "Bed_N*", "Bed_S*");
            Add(plan, "BostonFern", PlantLayer.Low, PlacementBias.Anywhere, 7, 0.5f, 1.0f, 0.55f, 0.55f, 0.05f, "Border_N", "Border_S");
            Add(plan, "Moss", PlantLayer.Ground, PlacementBias.Anywhere, 5, 0.3f, 0.5f, 1.0f, 0.85f, 0.3f, "Bed_N*", "Bed_S*");
            Add(plan, "Moss", PlantLayer.Ground, PlacementBias.Anywhere, 6, 0.3f, 0.7f, 1.0f, 0.85f, 0.05f, "Border_N", "Border_S");
            Add(plan, "CreepingGroundCover", PlantLayer.Ground, PlacementBias.Edges, 3, 0.3f, 0.6f, 1.0f, 0.85f, 0.3f, "Bed_N*", "Bed_S*");

            AssetDatabase.CreateAsset(plan, LushPlanPath);
            log.AppendLine($"BioDome: created lush preset plan: {LushPlanPath} ({plan.additions.Count} entries)");
            return plan;
        }

        private static void Add(PlantAdditionPlan plan, string assetName, PlantLayer layer, PlacementBias bias, int maxPerZone,
            float clearance, float spacing, float radiusScale, float overlap, float margin, params string[] zones)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{assetName}.prefab");
            var data = AssetDatabase.LoadAssetAtPath<PlantData>($"{DataFolder}/{assetName}.asset");

            int baseSeed = 101;
            foreach (Spec s in Specs) if (s.AssetName == assetName) baseSeed = s.Seed;

            plan.additions.Add(new PlantAdditionEntry
            {
                species = new PlantSpeciesEntry { label = assetName, prefab = prefab, data = data, variantCount = 4, baseSeed = baseSeed },
                zoneNames = new List<string>(zones),
                maxPerZone = maxPerZone,
                layer = layer,
                bias = bias,
                clearanceFromExisting = clearance,
                minSpacingBetweenNew = spacing,
                radiusScale = radiusScale,
                overlapAllowance = overlap,
                edgeMargin = margin,
                sizeVariation = layer == PlantLayer.Ground ? 0.15f : 0.1f
            });
        }
    }
}
