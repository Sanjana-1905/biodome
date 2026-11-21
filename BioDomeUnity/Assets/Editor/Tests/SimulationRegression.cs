using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Irrigation;
using BioDome.Plants;
using BioDome.Simulation;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Numeric regression test for the existing simulation. It records exact values from the current simulation code ("golden values")
    /// and later checks that the same inputs still give the same outputs, so changes for zones, experiments and irrigation can be proved
    /// not to alter existing plants.
    ///
    /// What it covers (all deterministic, no scene needed):
    ///  - PlantSimulation for every PlantData asset under six condition scenarios (ideal, hot, dry air, dark, drought, recovery, seasonal cycle).
    ///  - EnvironmentController: temperature, humidity and light for every season, many hours and offset combinations, plus clock and season rollover.
    ///  - IrrigationController together with environment and plants, stepped in the GreenhouseManager's own order
    ///    (environment.Advance, irrigation.Tick, then every plant): Auto with mains, Auto with a small reservoir, Manual and Off.
    ///
    /// Menu: BioDome > Tests. Command line: -executeMethod BioDome.EditorTools.Tests.SimulationRegression.RunBatch -goldenMode capture|compare.
    /// </summary>
    public static class SimulationRegression
    {
        public const string GoldenPath = "Assets/Tests/Golden/simulation_golden.json";

        /// <summary>The scientific growth model's own baseline (version 1). The original baseline above is never touched by it.</summary>
        public const string ScientificGoldenPath = "Assets/Tests/Golden/simulation_golden_scientific_v1.json";

        /// <summary>
        /// Growth model used while computing values. Original (default) reproduces the original golden baseline exactly;
        /// Scientific is used for the versioned scientific baseline. PlantSimulation.ActiveModel is restored afterwards.
        /// </summary>
        public static PlantSimulation.GrowthModel ModelUnderTest = PlantSimulation.GrowthModel.Original;

        /// <summary>
        /// true (default): the irrigation scenarios advance the simulation through the real GreenhouseManager.Step.
        /// false ("replica"): they use a hand-written copy of the manager's tick order. Both must reproduce the same golden values.
        /// </summary>
        public static bool UseManagerStep = true;
        private const string ClimatePath = "Assets/ScriptableObjects/Environment/DefaultClimate.asset";

        [Serializable] private class Entry { public string k; public string v; }
        [Serializable] private class GoldenFile { public string note; public List<Entry> entries = new List<Entry>(); }

        public sealed class Outcome
        {
            public bool Passed;
            public int Checked;
            public int Mismatches;
            public int Missing;
            public int NewKeys;
            public double MaxAbsDifference;
            public string Text;
        }

        // --- Menu and batch entry points -------------------------------------------------

        [MenuItem("BioDome/Tests/Run Simulation Regression")]
        public static void RunFromMenu()
        {
            Outcome outcome = Compare(GoldenPath);
            Debug.Log("BioDome regression:\n" + outcome.Text);
            EditorUtility.DisplayDialog("Simulation regression", outcome.Passed ? "PASSED\n\n" + outcome.Text : "FAILED\n\n" + outcome.Text, "OK");
        }

        [MenuItem("BioDome/Tests/Run Scientific Growth Regression (v1)")]
        public static void RunScientificFromMenu()
        {
            ModelUnderTest = PlantSimulation.GrowthModel.Scientific;
            try
            {
                Outcome outcome = Compare(ScientificGoldenPath);
                Debug.Log("BioDome scientific regression: " + outcome.Text);
            }
            finally { ModelUnderTest = PlantSimulation.GrowthModel.Original; }
        }

        [MenuItem("BioDome/Tests/Capture Simulation Golden Values")]
        public static void CaptureFromMenu()
        {
            if (File.Exists(GoldenPath) && !EditorUtility.DisplayDialog("Capture golden values",
                    "Golden values already exist. Capturing again REPLACES them with what the CURRENT code produces, which hides any change made since. " +
                    "Only do this if you intentionally changed simulation behaviour.", "Replace", "Cancel"))
                return;
            int count = Capture(GoldenPath);
            Debug.Log($"BioDome regression: captured {count} golden values to {GoldenPath}");
        }

        public static void RunBatch()
        {
            int exit = 0;
            try
            {
                string mode = Arg("-goldenMode") ?? "compare";
                string file = Arg("-goldenFile") ?? GoldenPath;
                string modelArg = Arg("-growthModel") ?? (file == GoldenPath ? "original" : "scientific");
                ModelUnderTest = modelArg == "scientific" ? PlantSimulation.GrowthModel.Scientific : PlantSimulation.GrowthModel.Original;
                Debug.Log("REGRESSION growth model: " + ModelUnderTest);
                UseManagerStep = (Arg("-goldenDriver") ?? "manager") != "replica";
                Debug.Log("REGRESSION driver: " + (UseManagerStep ? "GreenhouseManager.Step (real code)" : "replica of the manager's tick order"));
                if (mode == "capture")
                {
                    int count = Capture(file);
                    Debug.Log($"REGRESSION captured {count} values to {file}");
                }
                else
                {
                    Outcome outcome = Compare(file);
                    Debug.Log("REGRESSION " + (outcome.Passed ? "PASSED" : "FAILED") + "\n" + outcome.Text);
                    exit = outcome.Passed ? 0 : 1;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("REGRESSION ERROR: " + e);
                exit = 2;
            }
            EditorApplication.Exit(exit);
        }

        // --- Capture and compare -------------------------------------------------------------

        public static int Capture(string path)
        {
            List<KeyValuePair<string, string>> values = Compute();
            var file = new GoldenFile { note = "Exact values from the BioDome simulation. Generated by BioDome > Tests > Capture Simulation Golden Values." };
            foreach (var pair in values) file.entries.Add(new Entry { k = pair.Key, v = pair.Value });

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(file, true));
            AssetDatabase.Refresh();
            return values.Count;
        }

        public static Outcome Compare(string path)
        {
            var outcome = new Outcome();
            if (!File.Exists(path))
            {
                outcome.Text = $"No golden file at {path}. Run BioDome > Tests > Capture Simulation Golden Values first.";
                return outcome;
            }

            GoldenFile golden = JsonUtility.FromJson<GoldenFile>(File.ReadAllText(path));
            var expected = new Dictionary<string, string>();
            foreach (Entry e in golden.entries) expected[e.k] = e.v;

            var text = new StringBuilder();
            var seen = new HashSet<string>();
            int shown = 0;
            foreach (var pair in Compute())
            {
                seen.Add(pair.Key);
                if (!expected.TryGetValue(pair.Key, out string want)) { outcome.NewKeys++; continue; }

                outcome.Checked++;
                if (want == pair.Value) continue;

                double a = double.Parse(want, CultureInfo.InvariantCulture);
                double b = double.Parse(pair.Value, CultureInfo.InvariantCulture);
                double diff = Math.Abs(a - b);
                outcome.MaxAbsDifference = Math.Max(outcome.MaxAbsDifference, diff);
                if (diff <= 1e-6) continue; // float noise from a different CPU, not a behaviour change

                outcome.Mismatches++;
                if (shown++ < 15) text.AppendLine($"  MISMATCH {pair.Key}: expected {want}, got {pair.Value}");
            }
            foreach (string key in expected.Keys)
            {
                if (seen.Contains(key)) continue;
                outcome.Missing++;
                if (shown++ < 15) text.AppendLine($"  MISSING {key}");
            }

            outcome.Passed = outcome.Mismatches == 0 && outcome.Missing == 0 && outcome.Checked > 0;
            text.Insert(0, $"{outcome.Checked} values checked, {outcome.Mismatches} mismatches, {outcome.Missing} missing, {outcome.NewKeys} new (not compared), " +
                           $"max difference {outcome.MaxAbsDifference:E2}\n");
            outcome.Text = text.ToString();
            return outcome;
        }

        // --- The deterministic computation -----------------------------------------------------

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        public static List<KeyValuePair<string, string>> Compute()
        {
            PlantSimulation.GrowthModel previous = PlantSimulation.ActiveModel;
            PlantSimulation.ActiveModel = ModelUnderTest;
            try { return ComputeCore(); }
            finally { PlantSimulation.ActiveModel = previous; }
        }

        private static List<KeyValuePair<string, string>> ComputeCore()
        {
            var values = new List<KeyValuePair<string, string>>();
            void Put(string key, float value) => values.Add(new KeyValuePair<string, string>(key, F(value)));
            void PutInt(string key, int value) => values.Add(new KeyValuePair<string, string>(key, value.ToString(CultureInfo.InvariantCulture)));

            List<PlantData> species = LoadSpecies();
            ComputePlantSimulations(species, Put, PutInt);
            ComputeEnvironment(Put, PutInt);
            ComputeIrrigation(species, Put, PutInt);
            return values;
        }

        private static List<PlantData> LoadSpecies()
        {
            var list = new List<PlantData>();
            foreach (string guid in AssetDatabase.FindAssets("t:PlantData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<PlantData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null) list.Add(data);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        // 1. PlantSimulation alone ---------------------------------------------------------------

        private static void ComputePlantSimulations(List<PlantData> species, Action<string, float> put, Action<string, int> putInt)
        {
            const float dt = 0.1f;
            const int days = 60;
            string[] scenarios = { "ideal", "hot", "dry", "dark", "drought", "recover", "cycle" };

            foreach (PlantData data in species)
            {
                foreach (string scenario in scenarios)
                {
                    var sim = new PlantSimulation(data, 0.12f, 1f, 0.6f); // the greenhouse's own start values
                    int steps = Mathf.RoundToInt(days * 24f / dt);
                    for (int step = 0; step < steps; step++)
                    {
                        float hours = step * dt;
                        EnvironmentConditions env = ScenarioConditions(data, scenario, hours);

                        // "ideal", "hot", "dry" and "dark" keep the soil at a comfortable level, like working irrigation.
                        if (scenario != "drought" && !(scenario == "recover" && hours < 20f * 24f) && scenario != "cycle")
                        {
                            float target = 0.55f * data.WaterCapacityLitres;
                            float have = sim.Water01 * data.WaterCapacityLitres;
                            if (have < target) sim.AddWater(target - have);
                        }
                        if (scenario == "cycle" && step % 24 == 0) sim.AddWater(0.2f * data.WaterCapacityLitres); // a little water each 2.4 h

                        sim.Tick(dt, env);

                        if ((step + 1) % Mathf.RoundToInt(5f * 24f / dt) == 0)
                        {
                            string prefix = $"plant/{data.name}/{scenario}/d{(step + 1) / Mathf.RoundToInt(24f / dt)}";
                            put(prefix + "/growth", sim.Growth01);
                            put(prefix + "/health", sim.Health01);
                            put(prefix + "/water", sim.Water01);
                            put(prefix + "/stress", sim.Stress01);
                            put(prefix + "/age", sim.AgeHours);
                            putInt(prefix + "/state", (int)sim.State);
                        }
                    }
                }
            }
        }

        private static EnvironmentConditions ScenarioConditions(PlantData data, string scenario, float hours)
        {
            float tMid = (data.IdealTemperatureCelsius.x + data.IdealTemperatureCelsius.y) * 0.5f;
            float hMid = (data.IdealHumidity01.x + data.IdealHumidity01.y) * 0.5f;
            float lMid = (data.IdealLight01.x + data.IdealLight01.y) * 0.5f;
            float timeOfDay = Mathf.Repeat(hours, 24f);
            int day = Mathf.FloorToInt(hours / 24f);

            switch (scenario)
            {
                case "hot": return new EnvironmentConditions(data.IdealTemperatureCelsius.y + 10f, hMid, lMid, Season.Summer, timeOfDay, day);
                case "dry": return new EnvironmentConditions(tMid, Mathf.Max(0f, data.IdealHumidity01.x - 0.3f), lMid, Season.Spring, timeOfDay, day);
                case "dark": return new EnvironmentConditions(tMid, hMid, 0.1f, Season.Autumn, timeOfDay, day);
                case "cycle":
                {
                    float wave = Mathf.Sin((timeOfDay - 9f) / 24f * Mathf.PI * 2f);
                    float daylight = Mathf.Max(0f, Mathf.Sin((timeOfDay - 6f) / 12f * Mathf.PI));
                    var season = (Season)((day / 10) % 4);
                    return new EnvironmentConditions(16f + 7f * wave, Mathf.Clamp01(0.62f - 0.1f * wave), daylight * 0.85f, season, timeOfDay, day);
                }
                default: return new EnvironmentConditions(tMid, hMid, lMid, Season.Spring, timeOfDay, day);
            }
        }

        // 2. EnvironmentController ------------------------------------------------------------------

        private static EnvironmentController NewEnvironment(out GameObject owner)
        {
            owner = new GameObject("RegressionEnvironment") { hideFlags = HideFlags.HideAndDontSave };
            var env = owner.AddComponent<EnvironmentController>();
            var so = new SerializedObject(env);
            so.FindProperty("climateProfile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ClimateProfile>(ClimatePath);
            so.ApplyModifiedPropertiesWithoutUndo();
            return env;
        }

        private static void ComputeEnvironment(Action<string, float> put, Action<string, int> putInt)
        {
            EnvironmentController env = NewEnvironment(out GameObject owner);
            try
            {
                float[] temperatureOffsets = { 0f, 10f, -10f };
                float[] humidityOffsets = { 0f, -0.3f, 0.3f };
                float[] shades = { 0f, 0.7f };

                for (int season = 0; season < 4; season++)
                {
                    for (int tIndex = 0; tIndex < temperatureOffsets.Length; tIndex++)
                    {
                        env.SetSeason((Season)season);
                        env.SetTemperatureOffset(temperatureOffsets[tIndex]);
                        env.SetHumidityOffset(humidityOffsets[tIndex]);
                        env.SetShadeLevel(shades[tIndex % 2]);
                        for (float hour = 0f; hour < 24f; hour += 1.5f)
                        {
                            env.SetTimeOfDay(hour);
                            EnvironmentConditions c = env.Current;
                            string key = $"env/s{season}/o{tIndex}/h{hour:0.0}";
                            put(key + "/temp", c.TemperatureCelsius);
                            put(key + "/humidity", c.Humidity01);
                            put(key + "/light", c.Light01);
                        }
                    }
                }

                // The clock and the automatic season change.
                env.SetTemperatureOffset(0f);
                env.SetHumidityOffset(0f);
                env.SetShadeLevel(0f);
                env.SetSeason(Season.Spring);
                env.SetTimeOfDay(8f);
                const float dt = 0.05f;
                for (int step = 1; step <= 20 * 24 * 20; step++) // 20 simulated days
                {
                    env.Advance(dt);
                    if (step % 240 == 0)
                    {
                        EnvironmentConditions c = env.Current;
                        string key = $"env/clock/step{step}";
                        put(key + "/temp", c.TemperatureCelsius);
                        put(key + "/humidity", c.Humidity01);
                        put(key + "/light", c.Light01);
                        put(key + "/time", c.TimeOfDayHours);
                        putInt(key + "/day", c.DayCount);
                        putInt(key + "/season", (int)c.Season);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // 3. Irrigation together with environment and plants, in the GreenhouseManager's order ------------

        private struct IrrigationScenario
        {
            public string Name;
            public float Capacity, Start, Mains, Flow, Below, Above;
            public IrrigationController.Mode Mode;
        }

        private static void ComputeIrrigation(List<PlantData> species, Action<string, float> put, Action<string, int> putInt)
        {
            if (species.Count == 0) return;

            var scenarios = new[]
            {
                new IrrigationScenario { Name = "auto_mains", Capacity = 2000f, Start = 2000f, Mains = 40f, Flow = 150f, Below = 0.35f, Above = 0.75f, Mode = IrrigationController.Mode.Auto },
                new IrrigationScenario { Name = "auto_small", Capacity = 150f, Start = 150f, Mains = 0f, Flow = 150f, Below = 0.35f, Above = 0.75f, Mode = IrrigationController.Mode.Auto },
                new IrrigationScenario { Name = "auto_slow", Capacity = 600f, Start = 600f, Mains = 10f, Flow = 20f, Below = 0.4f, Above = 0.6f, Mode = IrrigationController.Mode.Auto },
                new IrrigationScenario { Name = "manual", Capacity = 500f, Start = 500f, Mains = 0f, Flow = 60f, Below = 0.35f, Above = 0.75f, Mode = IrrigationController.Mode.Manual },
                new IrrigationScenario { Name = "off", Capacity = 500f, Start = 500f, Mains = 40f, Flow = 150f, Below = 0.35f, Above = 0.75f, Mode = IrrigationController.Mode.Off }
            };

            const int plantCount = 24;
            const float dt = 0.05f;
            const int hours = 240;

            foreach (IrrigationScenario scenario in scenarios)
            {
                EnvironmentController env = NewEnvironment(out GameObject envOwner);
                var owners = new List<GameObject>();
                try
                {
                    env.SetSeason(Season.Spring);
                    env.SetTimeOfDay(8f);
                    env.SetTemperatureOffset(2f);

                    IrrigationController irrigation = NewIrrigation(scenario, out GameObject irrigationOwner);
                    owners.Add(irrigationOwner);

                    var plants = new List<PlantController>();
                    for (int i = 0; i < plantCount; i++)
                    {
                        var go = new GameObject("RegressionPlant" + i) { hideFlags = HideFlags.HideAndDontSave };
                        owners.Add(go);
                        var plant = go.AddComponent<PlantController>();
                        plant.Initialize(species[i % species.Count]);

                        // Different species and a head start of 0 to 40 hours give the plants different soil moisture.
                        float head = (i % 6) * 8f;
                        EnvironmentConditions warmup = env.Current;
                        for (float h = 0f; h < head; h += 0.5f) plant.Tick(0.5f, warmup);

                        plants.Add(plant);
                        irrigation.RegisterPlant(plant);
                    }

                    // The real manager, wired up the way the scene wires it (Awake, Start and Update do not run in Edit Mode, and Step needs none of them).
                    GreenhouseManager manager = null;
                    if (UseManagerStep)
                    {
                        var managerObject = new GameObject("RegressionManager") { hideFlags = HideFlags.HideAndDontSave };
                        owners.Add(managerObject);
                        manager = managerObject.AddComponent<GreenhouseManager>();
                        var managerSo = new SerializedObject(manager);
                        managerSo.FindProperty("environment").objectReferenceValue = env;
                        managerSo.FindProperty("irrigation").objectReferenceValue = irrigation;
                        managerSo.FindProperty("autoDiscoverPlants").boolValue = false;
                        SerializedProperty list = managerSo.FindProperty("plants");
                        list.arraySize = plants.Count;
                        for (int i = 0; i < plants.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = plants[i];
                        managerSo.ApplyModifiedPropertiesWithoutUndo();
                    }

                    int steps = Mathf.RoundToInt(hours / dt);
                    int perCheckpoint = Mathf.RoundToInt(12f / dt);
                    for (int step = 1; step <= steps; step++)
                    {
                        if (manager != null)
                        {
                            manager.Step(dt);
                        }
                        else
                        {
                            // Replica of the GreenhouseManager tick order.
                            env.Advance(dt);
                            EnvironmentConditions conditions = env.Current;
                            irrigation.Tick(dt);
                            for (int i = 0; i < plants.Count; i++) plants[i].Tick(dt, conditions);
                        }

                        if (step % perCheckpoint == 0)
                        {
                            string key = $"irr/{scenario.Name}/t{step / Mathf.RoundToInt(1f / dt)}";
                            put(key + "/reservoir", irrigation.WaterLitres);
                            putInt(key + "/running", irrigation.IsRunning ? 1 : 0);
                            for (int i = 0; i < plants.Count; i++)
                            {
                                put($"{key}/p{i}/water", plants[i].Simulation.Water01);
                                put($"{key}/p{i}/health", plants[i].Simulation.Health01);
                            }
                        }
                    }
                }
                finally
                {
                    foreach (GameObject o in owners) UnityEngine.Object.DestroyImmediate(o);
                    UnityEngine.Object.DestroyImmediate(envOwner);
                }
            }
        }

        private static IrrigationController NewIrrigation(IrrigationScenario s, out GameObject owner)
        {
            owner = new GameObject("RegressionIrrigation") { hideFlags = HideFlags.HideAndDontSave };
            var irrigation = owner.AddComponent<IrrigationController>();
            var so = new SerializedObject(irrigation);
            so.FindProperty("reservoirCapacityLitres").floatValue = s.Capacity;
            so.FindProperty("startingWaterLitres").floatValue = s.Start;
            so.FindProperty("mainsSupplyLitresPerHour").floatValue = s.Mains;
            so.FindProperty("flowRateLitresPerHour").floatValue = s.Flow;
            so.FindProperty("mode").enumValueIndex = (int)s.Mode;
            so.FindProperty("startBelowMoisture").floatValue = s.Below;
            so.FindProperty("stopAboveMoisture").floatValue = s.Above;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Awake does not run on a component added in Edit Mode; call it so the reservoir starts exactly as in the game.
            MethodInfo awake = typeof(IrrigationController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            awake.Invoke(irrigation, null);
            return irrigation;
        }

        private static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
