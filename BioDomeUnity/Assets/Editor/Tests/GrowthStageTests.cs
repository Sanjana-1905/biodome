using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BioDome.Core;
using BioDome.Plants;
using BioDome.Simulation;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Stage 1 growth tests: five stages, species differences, environmental influence, and time-step independence.
    /// Pure simulation (PlantSimulation); reads the species assets but changes nothing. The golden baseline is not involved.
    /// Menu: BioDome > Tests > Run Growth Stage Tests. Command line: -executeMethod BioDome.EditorTools.Tests.GrowthStageTests.RunBatch
    /// </summary>
    public static class GrowthStageTests
    {
        private const float SimHours = 240f; // ten simulated days

        [MenuItem("BioDome/Tests/Run Growth Stage Tests")]
        public static void RunFromMenu()
        {
            string text = Run(out int failed);
            Debug.Log("GROWTHTESTS " + (failed == 0 ? "PASSED" : "FAILED") + "\n" + text);
        }

        public static void RunBatch()
        {
            int exit;
            try
            {
                string text = Run(out int failed);
                Debug.Log("GROWTHTESTS " + (failed == 0 ? "PASSED" : "FAILED") + "\n" + text);
                exit = failed == 0 ? 0 : 1;
            }
            catch (Exception e) { Debug.LogError("GROWTHTESTS ERROR: " + e); exit = 2; }
            EditorApplication.Exit(exit);
        }

        private static string Run(out int failed)
        {
            PlantSimulation.GrowthModel previous = PlantSimulation.ActiveModel;
            PlantSimulation.HealthModel previousHealth = PlantSimulation.ActiveHealthModel;
            PlantSimulation.ActiveModel = PlantSimulation.GrowthModel.Scientific;
            PlantSimulation.ActiveHealthModel = PlantSimulation.HealthModel.Original; // these tests are about growth, not health damage
            try { return RunChecks(out failed); }
            finally { PlantSimulation.ActiveModel = previous; PlantSimulation.ActiveHealthModel = previousHealth; }
        }

        private static string RunChecks(out int failed)
        {
            var sb = new StringBuilder();
            int passed = 0, fail = 0;
            void Check(bool ok, string name, string detail = "")
            {
                if (ok) { passed++; sb.AppendLine("  pass  " + name); }
                else { fail++; sb.AppendLine("  FAIL  " + name + (detail.Length > 0 ? "  -> " + detail : "")); }
            }

            var species = new List<PlantData>();
            foreach (string guid in AssetDatabase.FindAssets("t:PlantData", new[] { "Assets/ScriptableObjects/Plants" }))
                species.Add(AssetDatabase.LoadAssetAtPath<PlantData>(AssetDatabase.GUIDToAssetPath(guid)));
            Check(species.Count >= 11, "species assets found", species.Count + " found");

            // 1. Five stages, in order, for every species.
            foreach (PlantData d in species)
            {
                var seen = new HashSet<PlantGrowthStage>();
                int last = -1; bool ordered = true;
                for (int i = 0; i <= 1000; i++)
                {
                    PlantGrowthStage s = d.GetStage(i / 1000f);
                    if ((int)s < last) ordered = false;
                    last = (int)s; seen.Add(s);
                }
                Check(ordered && seen.Count == 5, d.DisplayName + ": five ordered growth stages", seen.Count + " stages seen");
            }

            // 2. Species differ in speed, size, water and conditions.
            Check(species.Select(d => d.DaysToMaturity).Distinct().Count() >= 6, "species have different growth durations");
            Check(species.Select(d => d.MatureHeight).Distinct().Count() >= 6, "species have different mature sizes");
            Check(species.Select(d => d.WaterUseLitresPerHour).Distinct().Count() >= 6, "species have different water requirements");
            Check(species.Select(d => d.IdealLight01.ToString()).Distinct().Count() >= 6, "species have different light requirements");
            Check(species.Select(d => d.IdealTemperatureCelsius.ToString()).Distinct().Count() >= 5, "species have different temperature preferences");

            // 3. Growth responds to each environmental factor (soil water held constant unless it is the factor under test).
            PlantData monstera = species.First(d => d.SpeciesId == "monstera");
            float ideal = GrowAt(monstera, Mid(monstera.IdealTemperatureCelsius), Mid(monstera.IdealHumidity01), Mid(monstera.IdealLight01), 0.6f, 0.05f);
            float hot = GrowAt(monstera, 42f, Mid(monstera.IdealHumidity01), Mid(monstera.IdealLight01), 0.6f, 0.05f);
            float cold = GrowAt(monstera, 6f, Mid(monstera.IdealHumidity01), Mid(monstera.IdealLight01), 0.6f, 0.05f);
            float dry = GrowAt(monstera, Mid(monstera.IdealTemperatureCelsius), 0.05f, Mid(monstera.IdealLight01), 0.6f, 0.05f);
            float dark = GrowAt(monstera, Mid(monstera.IdealTemperatureCelsius), Mid(monstera.IdealHumidity01), 0f, 0.6f, 0.05f);
            float parched = GrowAt(monstera, Mid(monstera.IdealTemperatureCelsius), Mid(monstera.IdealHumidity01), Mid(monstera.IdealLight01), 0.02f, 0.05f);
            sb.AppendLine($"  info  Monstera growth over {SimHours} h: ideal {ideal:0.000}, hot {hot:0.000}, cold {cold:0.000}, dry air {dry:0.000}, dark {dark:0.000}, dry soil {parched:0.000}");
            Check(ideal > hot, "heat slows growth");
            Check(ideal > cold, "cold slows growth");
            Check(ideal > dry, "dry air slows growth");
            Check(ideal > dark, "low light slows growth");
            Check(ideal > parched, "dry soil slows growth");
            Check(Mathf.Approximately(GrowAt(monstera, 30f, 0.6f, 0.5f, 0.6f, 0f), 0f) == false, "growth progresses from a seedling");

            // 3b. Severe conditions meaningfully suppress growth (scientific model), and factors interact multiplicatively.
            Check(hot < 0.05f * Mathf.Max(0.0001f, ideal - 0.05f) + 0.05f, "42 C stops Monstera growth (within 5 % of the starting size)", $"grew {hot - 0.05f:0.000} vs ideal {ideal - 0.05f:0.000}");
            Check(cold < 0.05f * Mathf.Max(0.0001f, ideal - 0.05f) + 0.05f, "6 C stops Monstera growth", $"grew {cold - 0.05f:0.000}");
            float fHot = FactorAt(monstera, 36f, Mid(monstera.IdealHumidity01), Mid(monstera.IdealLight01), Mid(monstera.IdealSoilMoisture01));
            float fDry = FactorAt(monstera, Mid(monstera.IdealTemperatureCelsius), Mid(monstera.IdealHumidity01), Mid(monstera.IdealLight01), 0.1f);
            float fBoth = FactorAt(monstera, 36f, Mid(monstera.IdealHumidity01), Mid(monstera.IdealLight01), 0.1f);
            Check(Mathf.Abs(fBoth - fHot * fDry) < 0.02f && fBoth < Mathf.Min(fHot, fDry), "combined stresses multiply (heat x drought is worse than either)", $"{fHot:0.000} x {fDry:0.000} = {fHot * fDry:0.000}, simulated {fBoth:0.000}");

            // 3c. Each representative species responds according to ITS OWN configured tolerances (analytic check against the asset values).
            string[] representative = { "monstera", "banana", "bird_of_paradise", "calathea", "anthurium", "orchid" };
            var conditions = new (string name, float t, float h, float l, float w)[]
            {
                ("ideal", float.NaN, float.NaN, float.NaN, float.NaN), ("15 C", 15f, float.NaN, float.NaN, float.NaN), ("36 C", 36f, float.NaN, float.NaN, float.NaN),
                ("42 C", 42f, float.NaN, float.NaN, float.NaN), ("dry air", float.NaN, 0.15f, float.NaN, float.NaN), ("dark", float.NaN, float.NaN, 0f, float.NaN),
                ("drought", float.NaN, float.NaN, float.NaN, 0.1f), ("heat + drought", 36f, float.NaN, float.NaN, 0.1f),
            };
            foreach (string id in representative)
            {
                PlantData d = species.FirstOrDefault(x => x.SpeciesId == id);
                if (d == null) { Check(false, id + " asset found"); continue; }
                var line = new StringBuilder();
                bool ok = true;
                foreach (var c in conditions)
                {
                    float t = float.IsNaN(c.t) ? Mid(d.IdealTemperatureCelsius) : c.t, h = float.IsNaN(c.h) ? Mid(d.IdealHumidity01) : c.h;
                    float l = float.IsNaN(c.l) ? Mid(d.IdealLight01) : c.l, w = float.IsNaN(c.w) ? Mid(d.IdealSoilMoisture01) : c.w;
                    float simulated = FactorAt(d, t, h, l, w), expected = ExpectedFactor(d, t, h, l, w);
                    line.Append($" {c.name} {simulated:0.00}");
                    if (Mathf.Abs(simulated - expected) > 0.02f) { ok = false; line.Append($"(expected {expected:0.00})"); }
                }
                sb.AppendLine("  info  " + d.DisplayName + " growth factor:" + line);
                Check(ok, d.DisplayName + ": growth factor follows its own tolerances");
            }

            // 4. Time-step independence: same simulated duration, different step sizes.
            foreach (PlantData d in species)
            {
                float t = Mid(d.IdealTemperatureCelsius), h = Mid(d.IdealHumidity01), l = Mid(d.IdealLight01), w = Mid(d.IdealSoilMoisture01);
                float a = GrowAt(d, t, h, l, w, 0.02f, 0.01f);
                float b = GrowAt(d, t, h, l, w, 0.02f, 0.25f); // the manager's maximum sub-step
                float c = GrowAt(d, t, h, l, w, 0.02f, 0.0166f); // ~60 fps at 1 sim-hour per second
                Check(Mathf.Abs(a - b) < 0.005f && Mathf.Abs(a - c) < 0.005f, d.DisplayName + ": growth independent of step size",
                    $"dt 0.01h: {a:0.0000}, 0.25h: {b:0.0000}, 0.0166h: {c:0.0000}");
            }
            // Stressed case (dry soil + heat) should also agree between small and the maximum sub-step.
            float s1 = GrowAt(monstera, 36f, 0.3f, 0.9f, 0.1f, 0.05f, 0.01f);
            float s2 = GrowAt(monstera, 36f, 0.3f, 0.9f, 0.1f, 0.05f, 0.25f);
            Check(Mathf.Abs(s1 - s2) < 0.01f, "stressed growth independent of sub-step size", $"{s1:0.0000} vs {s2:0.0000}");

            // 5. Visual growth: mesh step corresponds to the simulated growth.
            bool visual = true;
            for (int i = 0; i <= 1000; i++)
            {
                float g = i / 1000f;
                if (Mathf.Abs(PlantGenerator.GrowthOfStep(PlantGenerator.StepOf(g)) - g) > 0.5f / PlantGenerator.GrowthSteps + 1e-4f) visual = false;
            }
            Check(visual, "displayed mesh step is always within half a step of the simulated growth");

            failed = fail;
            sb.Insert(0, $"{passed} passed, {fail} failed\n");
            return sb.ToString();
        }

        /// <summary>Runs long enough for light acclimation to settle, in constant conditions, and returns the environmental growth factor.</summary>
        private static float FactorAt(PlantData data, float temperature, float humidity, float light, float soil)
        {
            var sim = new PlantSimulation(data, 0.05f, 1f, soil);
            var env = new EnvironmentConditions(temperature, humidity, light, Season.Spring, 12f, 0);
            for (int i = 0; i < 2400; i++)
            {
                sim.Tick(0.1f, env);
                float missing = soil - sim.Water01;
                if (missing > 0f) sim.AddWater(missing * data.WaterCapacityLitres);
            }
            return sim.GrowthFactor01;
        }

        // Independent re-statement of the model from the species configuration only.
        private static float ExpectedFactor(PlantData d, float t, float h, float l, float soil)
        {
            float R(float s) => 1f - s * s;
            float Range(float v, Vector2 r, float tol) => v < r.x ? Mathf.Clamp01((r.x - v) / tol) : v > r.y ? Mathf.Clamp01((v - r.y) / tol) : 0f;
            Vector2 m = d.IdealSoilMoisture01;
            float sw = soil < m.x ? Mathf.Clamp01((m.x - soil) / Mathf.Max(m.x, 0.01f)) : soil > m.y ? Mathf.Clamp01((soil - m.y) / Mathf.Max(1f - m.y, 0.01f)) : 0f;
            return R(Range(t, d.IdealTemperatureCelsius, d.TemperatureTolerance)) * R(Range(h, d.IdealHumidity01, d.HumidityTolerance))
                 * R(Range(l, d.IdealLight01, d.LightTolerance)) * R(sw);
        }

        private static float Mid(Vector2 range) => (range.x + range.y) * 0.5f;

        /// <summary>Grows a seedling for SimHours in constant conditions and returns its growth. Soil water is held at waterHold (0 = free-drying).</summary>
        private static float GrowAt(PlantData data, float temperature, float humidity, float light, float waterHold, float startGrowth, float dt = 0.1f)
        {
            var sim = new PlantSimulation(data, startGrowth, 1f, waterHold);
            var env = new EnvironmentConditions(temperature, humidity, light, Season.Spring, 12f, 0);
            int steps = Mathf.RoundToInt(SimHours / dt);
            for (int i = 0; i < steps; i++)
            {
                sim.Tick(dt, env);
                float missing = waterHold - sim.Water01;
                if (missing > 0f) sim.AddWater(missing * data.WaterCapacityLitres); // hold soil moisture constant
            }
            return sim.Growth01;
        }
    }
}
