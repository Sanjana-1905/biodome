using System;
using System.Collections.Generic;
using System.Text;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Plants;
using BioDome.Simulation;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Tests for zone profiles and the experimental architecture. They complement the golden-value regression (which proves existing
    /// behaviour is unchanged) by proving the new layer behaves as designed. No scene is needed or touched.
    /// Menu: BioDome > Tests. Command line: -executeMethod BioDome.EditorTools.Tests.ExperimentalZoneTests.RunBatch
    /// </summary>
    public static class ExperimentalZoneTests
    {
        private const string MonsteraPath = "Assets/ScriptableObjects/Plants/Monstera.asset";

        internal sealed class Report
        {
            public int Passed, Failed;
            public readonly StringBuilder Text = new StringBuilder();
            public void Check(bool ok, string name, string detail = "")
            {
                if (ok) { Passed++; Text.AppendLine("  pass  " + name); }
                else { Failed++; Text.AppendLine("  FAIL  " + name + (detail.Length > 0 ? " -> " + detail : "")); }
            }
        }

        [MenuItem("BioDome/Tests/Run Experimental Zone Tests")]
        public static void RunFromMenu()
        {
            Report r = Run();
            Debug.Log("BioDome zone tests:\n" + r.Text);
            EditorUtility.DisplayDialog("Experimental zone tests", (r.Failed == 0 ? "PASSED" : "FAILED") + $"\n{r.Passed} passed, {r.Failed} failed", "OK");
        }

        public static void RunBatch()
        {
            int exit;
            try
            {
                Report r = Run();
                Debug.Log($"ZONETESTS {(r.Failed == 0 ? "PASSED" : "FAILED")} ({r.Passed} passed, {r.Failed} failed)\n{r.Text}");
                exit = r.Failed == 0 ? 0 : 1;
            }
            catch (Exception e) { Debug.LogError("ZONETESTS ERROR: " + e); exit = 2; }
            EditorApplication.Exit(exit);
        }

        private static Report Run()
        {
            var r = new Report();
            var created = new List<UnityEngine.Object>();
            try
            {
                var profiles = new Dictionary<ExperimentalCondition, ZoneProfile>();
                foreach (ExperimentalCondition c in Enum.GetValues(typeof(ExperimentalCondition)))
                {
                    profiles[c] = ZoneProfilePresets.Create(c);
                    created.Add(profiles[c]);
                }

                TestProfileMath(r, profiles);
                TestAssets(r);

                var monstera = AssetDatabase.LoadAssetAtPath<PlantData>(MonsteraPath);
                r.Check(monstera != null, "Monstera PlantData found");
                if (monstera != null)
                {
                    TestEquivalentStart(r, monstera, profiles, created);
                    TestControlMatchesNormal(r, monstera, profiles);
                    TestDivergence(r, monstera, profiles);
                    TestDisplayPlantsUnaffected(r, monstera, profiles);
                    TestWaterIntakeDefault(r, monstera);
                    TestStartGrowth(r, monstera, owners0: created);
                    IrrigationShareTests.Run(r.Check, monstera, profiles);
                }
            }
            finally
            {
                foreach (UnityEngine.Object o in created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }
            return r;
        }

        private static readonly EnvironmentConditions Sample = new EnvironmentConditions(22f, 0.6f, 0.7f, Season.Summer, 13f, 4);

        private static void TestProfileMath(Report r, Dictionary<ExperimentalCondition, ZoneProfile> p)
        {
            EnvironmentConditions c = p[ExperimentalCondition.Control].Apply(Sample);
            r.Check(p[ExperimentalCondition.Control].IsIdentity, "Control profile is identity");
            r.Check(c.TemperatureCelsius == 22f && c.Humidity01 == 0.6f && c.Light01 == 0.7f && c.TimeOfDayHours == 13f && c.DayCount == 4 && c.Season == Season.Summer,
                "Control leaves conditions untouched");

            EnvironmentConditions h = p[ExperimentalCondition.HeatStress].Apply(Sample);
            r.Check(Mathf.Approximately(h.TemperatureCelsius, 32f) && h.Humidity01 == 0.6f && h.Light01 == 0.7f, "Heat: +10C only");

            EnvironmentConditions d = p[ExperimentalCondition.LowHumidity].Apply(Sample);
            r.Check(Mathf.Approximately(d.Humidity01, 0.3f) && d.TemperatureCelsius == 22f && d.Light01 == 0.7f, "Low humidity: -0.30 only");
            r.Check(p[ExperimentalCondition.LowHumidity].Apply(new EnvironmentConditions(22f, 0.1f, 0.7f, Season.Summer, 13f, 4)).Humidity01 == 0f, "Low humidity clamps at 0");

            EnvironmentConditions l = p[ExperimentalCondition.LowLight].Apply(Sample);
            r.Check(Mathf.Approximately(l.Light01, 0.28f) && l.TemperatureCelsius == 22f && l.Humidity01 == 0.6f, "Low light: x0.40 only");

            EnvironmentConditions w = p[ExperimentalCondition.WaterStress].Apply(Sample);
            r.Check(w.TemperatureCelsius == 22f && w.Humidity01 == 0.6f && w.Light01 == 0.7f, "Water stress leaves air/light untouched");
            r.Check(p[ExperimentalCondition.WaterStress].WaterShareWeight == 0.4f, "Water stress share weight 0.4");
            foreach (ExperimentalCondition k in Enum.GetValues(typeof(ExperimentalCondition)))
            {
                r.Check(p[k].WaterIntakeFactor == 1f, $"{k} intake stays 1");
                if (k != ExperimentalCondition.WaterStress) r.Check(p[k].WaterShareWeight == 1f, $"{k} share weight stays 1");
            }
        }

        private static void TestAssets(Report r)
        {
            foreach (ExperimentalCondition k in Enum.GetValues(typeof(ExperimentalCondition)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ZoneProfile>(ZoneProfileAssetBuilder.PathFor(k));
                var preset = ZoneProfilePresets.Create(k);
                bool ok = asset != null && asset.Condition == k && asset.TemperatureOffsetCelsius == preset.TemperatureOffsetCelsius &&
                          asset.HumidityOffset == preset.HumidityOffset && asset.LightMultiplier == preset.LightMultiplier &&
                          asset.WaterIntakeFactor == preset.WaterIntakeFactor && asset.WaterShareWeight == preset.WaterShareWeight;
                r.Check(ok, $"Asset {k} exists and matches preset");
                UnityEngine.Object.DestroyImmediate(preset);
            }
        }

        private static PlantController NewPlant(PlantData data, List<UnityEngine.Object> owners, string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            owners.Add(go);
            var plant = go.AddComponent<PlantController>();
            plant.Initialize(data);
            return plant;
        }

        private static void TestEquivalentStart(Report r, PlantData data, Dictionary<ExperimentalCondition, ZoneProfile> profiles, List<UnityEngine.Object> owners)
        {
            var start = new ExperimentStartState(data, 0.12f, 1f, 0.6f);
            var sims = new List<PlantSimulation>();
            foreach (ExperimentalCondition k in Enum.GetValues(typeof(ExperimentalCondition)))
            {
                PlantController plant = NewPlant(data, owners, "Exp_" + k);
                plant.ConfigureExperiment(start, profiles[k]);
                // The manager re-plants every plant from its cohort at Start; experimental plants must keep the shared start.
                plant.Plant(new PlantingCohort(0.25f, 0f));
                sims.Add(plant.Simulation);
                r.Check(plant.Simulation.WaterIntakeFactor == profiles[k].WaterIntakeFactor && plant.WaterShareWeight == profiles[k].WaterShareWeight, $"{k} plant carries its profile's water settings");
            }
            List<string> diffs = ExperimentEquivalence.Differences(sims);
            r.Check(diffs.Count == 0, "Five experimental plants start equivalent", string.Join("; ", diffs));
            r.Check(Mathf.Approximately(sims[0].Growth01, 0.12f) && Mathf.Approximately(sims[0].Water01, 0.6f) && sims[0].Health01 == 1f,
                "Experimental start state is used (not the greenhouse cohort override)");

            // The checker itself must notice a difference.
            var odd = new PlantSimulation(data, 0.3f);
            r.Check(ExperimentEquivalence.Differences(new[] { sims[0], odd }).Count > 0, "Equivalence check detects a different start");
        }

        /// <summary>Builds a manager (no irrigation) over the plants and steps it. Uses the real GreenhouseManager.Step.</summary>
        private static GreenhouseManager NewManager(List<PlantController> plants, List<UnityEngine.Object> owners, out EnvironmentController env)
        {
            var envObject = new GameObject("ZoneTestEnv") { hideFlags = HideFlags.HideAndDontSave };
            owners.Add(envObject);
            env = envObject.AddComponent<EnvironmentController>();
            var envSo = new SerializedObject(env);
            envSo.FindProperty("climateProfile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ClimateProfile>("Assets/ScriptableObjects/Environment/DefaultClimate.asset");
            envSo.ApplyModifiedPropertiesWithoutUndo();
            typeof(EnvironmentController).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(env, null);

            var go = new GameObject("ZoneTestManager") { hideFlags = HideFlags.HideAndDontSave };
            owners.Add(go);
            var manager = go.AddComponent<GreenhouseManager>();
            var so = new SerializedObject(manager);
            so.FindProperty("environment").objectReferenceValue = env;
            so.FindProperty("autoDiscoverPlants").boolValue = false;
            SerializedProperty list = so.FindProperty("plants");
            list.arraySize = plants.Count;
            for (int i = 0; i < plants.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = plants[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return manager;
        }

        private static void TestControlMatchesNormal(Report r, PlantData data, Dictionary<ExperimentalCondition, ZoneProfile> profiles)
        {
            var owners = new List<UnityEngine.Object>();
            try
            {
                var start = new ExperimentStartState(data, 0.12f, 1f, 0.6f);
                PlantController normal = NewPlant(data, owners, "Normal");
                PlantController control = NewPlant(data, owners, "Control");
                control.ConfigureExperiment(start, profiles[ExperimentalCondition.Control]);
                // Normal plant built with identical values, without any profile or experiment state.
                // PlantController defaults are growth 0.1; match the experiment start via the cohort the manager would apply.
                normal.Plant(new PlantingCohort(0.12f, 0f));

                GreenhouseManager manager = NewManager(new List<PlantController> { normal, control }, owners, out EnvironmentController env);
                bool identical = true; string where = "";
                for (int step = 1; step <= 20 * 24 * 10; step++) // 20 days at 0.1 h
                {
                    manager.Step(0.1f);
                    PlantSimulation a = normal.Simulation, b = control.Simulation;
                    if (a.Growth01 != b.Growth01 || a.Health01 != b.Health01 || a.Water01 != b.Water01 || a.Stress01 != b.Stress01 || a.AgeHours != b.AgeHours)
                    { identical = false; where = $"step {step}"; break; }
                }
                r.Check(identical, "Control group is bit-identical to a normal greenhouse plant over 20 days", where);
            }
            finally { foreach (UnityEngine.Object o in owners) if (o != null) UnityEngine.Object.DestroyImmediate(o); }
        }

        private static void TestDivergence(Report r, PlantData data, Dictionary<ExperimentalCondition, ZoneProfile> profiles)
        {
            var owners = new List<UnityEngine.Object>();
            try
            {
                var start = new ExperimentStartState(data, 0.12f, 1f, 0.6f);
                var plants = new List<PlantController>();
                foreach (ExperimentalCondition k in Enum.GetValues(typeof(ExperimentalCondition)))
                {
                    PlantController p = NewPlant(data, owners, "Div_" + k);
                    p.ConfigureExperiment(start, profiles[k]);
                    plants.Add(p);
                }
                GreenhouseManager manager = NewManager(plants, owners, out EnvironmentController env);
                // A warmed spring greenhouse in which the control plants are comfortable, so each pod's difference is its own condition.
                env.SetSeason(Season.Spring);
                env.SetTemperatureOffset(6f);
                env.SetTimeOfDay(8f);

                // No irrigation controller: each plant is watered by hand with exactly its own hourly water use (so soil never saturates),
                // scaled by the plant's water-share weight (water stress gets half). Temperature stress is summed over the run.
                const float dt = 0.1f;
                var tempStress = new float[plants.Count];
                var humidityStress = new float[plants.Count];
                var lightStress = new float[plants.Count];
                int aliveSteps = 0;
                for (int step = 1; step <= 12 * 24 * 10; step++)
                {
                    foreach (PlantController p in plants) p.ReceiveWater(p.Data.WaterUseLitresPerHour * dt * p.WaterShareWeight);
                    manager.Step(dt);
                    bool allAlive = true; // a dead plant stops ticking, so only compare while every pod is alive
                    foreach (PlantController p in plants) allAlive &= p.Simulation.IsAlive;
                    if (!allAlive) break;
                    aliveSteps++;
                    for (int i = 0; i < plants.Count; i++)
                    {
                        tempStress[i] += plants[i].Simulation.TemperatureStress01;
                        humidityStress[i] += plants[i].Simulation.HumidityStress01;
                        lightStress[i] += plants[i].Simulation.LightStress01;
                    }
                }

                // Each pod must stress its own factor more than control does (the direction of the effect on growth depends on the species
                // and season, e.g. +10C can help in spring, so the tests assert on the factor each pod manipulates).
                r.Check(tempStress[1] > tempStress[0], "Heat pod accumulates more temperature stress than control", $"{tempStress[1]} vs {tempStress[0]}");
                r.Check(humidityStress[2] > humidityStress[0], "Low-humidity pod accumulates more humidity stress than control", $"{humidityStress[2]} vs {humidityStress[0]}");
                r.Check(lightStress[3] != lightStress[0], "Low-light pod light stress differs from control", $"{lightStress[3]} vs {lightStress[0]}");
                r.Check(tempStress[2] == tempStress[0] && tempStress[3] == tempStress[0] && tempStress[4] == tempStress[0], "Pods that do not change temperature see control's temperature stress");
                r.Check(humidityStress[1] == humidityStress[0] && humidityStress[3] == humidityStress[0] && humidityStress[4] == humidityStress[0], "Pods that do not change humidity see control's humidity stress");
                r.Check(lightStress[1] == lightStress[0] && lightStress[2] == lightStress[0] && lightStress[4] == lightStress[0], "Pods that do not change light see control's light stress");
                PlantSimulation control = plants[0].Simulation;
                var summary = new StringBuilder();
                for (int i = 0; i < plants.Count; i++)
                    summary.Append($"[{(ExperimentalCondition)i}: {plants[i].Simulation.State} g={plants[i].Simulation.Growth01:F3} h={plants[i].Simulation.Health01:F3} w={plants[i].Simulation.Water01:F3} T={tempStress[i]:F1} H={humidityStress[i]:F1} L={lightStress[i]:F1}] ");
                r.Text.AppendLine($"  info  compared over {aliveSteps} steps while all pods alive; final: " + summary);
                r.Check(aliveSteps >= 5 * 24 * 10, "All pods stay alive for at least 5 days (comparison window)", $"{aliveSteps} steps");
                r.Check(plants[4].Simulation.Water01 < control.Water01, "Water-stress group holds less soil water than control",
                    $"control {control.Water01}, water-stress {plants[4].Simulation.Water01}");
                var differs = new HashSet<float>();
                foreach (PlantController p in plants) differs.Add(Mathf.Round(p.Simulation.Growth01 * 1e5f) + p.Simulation.Health01);
                r.Check(differs.Count >= 3, "Groups diverge from each other over time", $"{differs.Count} distinct outcomes");
            }
            finally { foreach (UnityEngine.Object o in owners) if (o != null) UnityEngine.Object.DestroyImmediate(o); }
        }

        private static void TestDisplayPlantsUnaffected(Report r, PlantData data, Dictionary<ExperimentalCondition, ZoneProfile> profiles)
        {
            // Display plants share a manager with experimental plants; with no irrigation they must evolve exactly as they do alone.
            var owners = new List<UnityEngine.Object>();
            try
            {
                PlantController alone = NewPlant(data, owners, "AloneDisplay");
                GreenhouseManager soloManager = NewManager(new List<PlantController> { alone }, owners, out EnvironmentController soloEnv);

                PlantController display = NewPlant(data, owners, "MixedDisplay");
                var plants = new List<PlantController> { display };
                var start = new ExperimentStartState(data, 0.12f, 1f, 0.6f);
                foreach (ExperimentalCondition k in Enum.GetValues(typeof(ExperimentalCondition)))
                {
                    PlantController p = NewPlant(data, owners, "MixedExp_" + k);
                    p.ConfigureExperiment(start, profiles[k]);
                    plants.Add(p);
                }
                GreenhouseManager mixedManager = NewManager(plants, owners, out EnvironmentController mixedEnv);

                bool same = true;
                for (int step = 1; step <= 20 * 24 * 10 && same; step++)
                {
                    soloManager.Step(0.1f);
                    mixedManager.Step(0.1f);
                    PlantSimulation a = alone.Simulation, b = display.Simulation;
                    same = a.Growth01 == b.Growth01 && a.Health01 == b.Health01 && a.Water01 == b.Water01 && a.Stress01 == b.Stress01;
                }
                r.Check(same, "Display plant beside experimental plants evolves bit-identically to a lone display plant");
                r.Check(display.ZoneProfile == null && display.ExperimentStart == null && display.Simulation.WaterIntakeFactor == 1f && display.WaterShareWeight == 1f && !display.UsesOwnIrrigation, "Display plant has no zone profile, experiment state, intake change, weight change or own irrigation");
            }
            finally { foreach (UnityEngine.Object o in owners) if (o != null) UnityEngine.Object.DestroyImmediate(o); }
        }

        private static void TestStartGrowth(Report r, PlantData data, List<UnityEngine.Object> owners0)
        {
            // Default (-1): the cohort decides, exactly as for every original plant. A value 0-1 starts that plant at that growth.
            var go = new GameObject("StartGrowthTest") { hideFlags = HideFlags.HideAndDontSave };
            owners0.Add(go);
            var plant = go.AddComponent<PlantController>();
            plant.Initialize(data);
            r.Check(plant.StartGrowth01 < 0f, "StartGrowth01 defaults to -1 (cohort start)");
            plant.Plant(new PlantingCohort(0.12f, 0f));
            r.Check(Mathf.Approximately(plant.Simulation.Growth01, 0.12f), "Default plant starts at the cohort growth", plant.Simulation.Growth01.ToString());

            var go2 = new GameObject("StartGrowthTest2") { hideFlags = HideFlags.HideAndDontSave };
            owners0.Add(go2);
            var mature = go2.AddComponent<PlantController>();
            var so = new SerializedObject(mature);
            so.FindProperty("startGrowth01").floatValue = 0.6f;
            so.ApplyModifiedPropertiesWithoutUndo();
            mature.Initialize(data);
            mature.Plant(new PlantingCohort(0.12f, 0f));
            r.Check(Mathf.Approximately(mature.Simulation.Growth01, 0.6f), "Plant with StartGrowth01 0.6 starts at 0.6 even after the cohort is applied", mature.Simulation.Growth01.ToString());
            r.Check(mature.Simulation.Health01 == 1f && mature.Simulation.Water01 == 0.6f, "Pre-grown plant keeps normal start health and water");
        }

        private static void TestWaterIntakeDefault(Report r, PlantData data)
        {
            var a = new PlantSimulation(data, 0.12f, 1f, 0.3f);
            var b = new PlantSimulation(data, 0.12f, 1f, 0.3f) { WaterIntakeFactor = 0.5f };
            float litres = 0.3f * data.WaterCapacityLitres;
            float accA = a.AddWater(litres), accB = b.AddWater(litres);
            r.Check(a.WaterIntakeFactor == 1f && accA == litres, "WaterIntakeFactor defaults to 1 and AddWater is unchanged");
            r.Check(Mathf.Approximately(accB, litres * 0.5f), "Intake 0.5 halves absorbed water");
        }
    }
}
