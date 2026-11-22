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
    /// Stage 2 tests: health, wilting, recovery and flowering, on the pure simulation (PlantSimulation) with the real species assets.
    /// Menu: BioDome > Tests > Run Health Wilt Bloom Tests. Command line: -executeMethod BioDome.EditorTools.Tests.HealthWiltBloomTests.RunBatch
    /// </summary>
    public static class HealthWiltBloomTests
    {
        [MenuItem("BioDome/Tests/Run Health Wilt Bloom Tests")]
        public static void RunFromMenu()
        {
            string text = Run(out int failed);
            Debug.Log("HEALTHTESTS " + (failed == 0 ? "PASSED" : "FAILED") + "\n" + text);
        }

        public static void RunBatch()
        {
            int exit;
            try
            {
                string text = Run(out int failed);
                Debug.Log("HEALTHTESTS " + (failed == 0 ? "PASSED" : "FAILED") + "\n" + text);
                exit = failed == 0 ? 0 : 1;
            }
            catch (Exception e) { Debug.LogError("HEALTHTESTS ERROR: " + e); exit = 2; }
            EditorApplication.Exit(exit);
        }

        private static string Run(out int failed)
        {
            var growth = PlantSimulation.ActiveModel;
            var health = PlantSimulation.ActiveHealthModel;
            PlantSimulation.ActiveModel = PlantSimulation.GrowthModel.Scientific;
            PlantSimulation.ActiveHealthModel = PlantSimulation.HealthModel.SevereSustained;
            try { return RunChecks(out failed); }
            finally { PlantSimulation.ActiveModel = growth; PlantSimulation.ActiveHealthModel = health; }
        }

        private static EnvironmentConditions Env(PlantData d, float? t = null, float? h = null, float? l = null) =>
            new EnvironmentConditions(t ?? Mid(d.IdealTemperatureCelsius), h ?? Mid(d.IdealHumidity01), l ?? Mid(d.IdealLight01), Season.Spring, 12f, 0);

        private static float Mid(Vector2 r) => (r.x + r.y) * 0.5f;

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
            PlantData Get(string id) => species.First(s => s.SpeciesId == id);
            PlantData monstera = Get("monstera"), banana = Get("banana"), orchid = Get("orchid"), anthurium = Get("anthurium"), bird = Get("bird_of_paradise");

            // 1. Healthy plants stay healthy and turgid.
            {
                var sim = new PlantSimulation(monstera, 0.6f, 1f, 0.6f);
                Run(sim, Env(monstera), 240f, 0.1f, hold: 0.6f);
                Check(sim.Health01 > 0.99f && sim.Wilt01 < 0.01f && sim.State == PlantHealthState.Healthy, "healthy plant in ideal conditions stays healthy and not wilted", $"health {sim.Health01:0.000}, wilt {sim.Wilt01:0.000}");
            }

            // 1b. A normal greenhouse (cool spring, day/night light) must not harm any species.
            {
                bool allOk = true; var worst = new StringBuilder();
                foreach (PlantData d in species)
                {
                    var sim = new PlantSimulation(d, 0.65f, 1f, 0.6f);
                    for (int i = 0; i < 480 * 10; i++)
                    {
                        float hour = (i * 0.1f) % 24f;
                        float light = Mathf.Clamp01(Mathf.Sin((hour - 6f) / 12f * Mathf.PI)) * 0.8f;
                        sim.Tick(0.1f, new EnvironmentConditions(17f + 3f * light, 0.62f, light, Season.Spring, hour, 0));
                        float missing = 0.6f - sim.Water01; if (missing > 0f) sim.AddWater(missing * d.WaterCapacityLitres);
                    }
                    if (sim.Health01 < 0.6f) { allOk = false; worst.Append($" {d.DisplayName} {sim.Health01:0.00}"); }
                }
                Check(allOk, "a cool-spring greenhouse day/night cycle does not damage any species over 20 days", worst.ToString());
            }

            // 2. Drought: wilting rises gradually, health deteriorates slowly, watering recovers gradually.
            {
                var sim = new PlantSimulation(monstera, 0.6f, 1f, 0.6f);
                var env = Env(monstera);
                float prev = 0f; bool monotone = true; float hoursToWilt = -1f; int steps = 0;
                while (sim.Wilt01 < 0.8f && steps < 20000) { sim.Tick(0.1f, env); steps++; if (sim.Wilt01 < prev - 1e-6f) monotone = false; prev = sim.Wilt01; if (hoursToWilt < 0 && sim.Wilt01 > 0.3f) hoursToWilt = steps * 0.1f; }
                float peakWilt = sim.Wilt01, healthAtPeak = sim.Health01, hours = steps * 0.1f;
                sb.AppendLine($"  info  drought: wilt 30 % after {hoursToWilt:0} h, 80 % after {hours:0} h; health then {healthAtPeak:0.00}, water {sim.Water01:0.00}");
                Check(monotone && hoursToWilt > 6f && hours > hoursToWilt, "wilting rises gradually and monotonically while soil dries");
                Check(healthAtPeak > 0.3f && healthAtPeak < 1f, "health deteriorates but a wilted plant is not yet dead", $"health {healthAtPeak:0.00}");

                sim.AddWater(10f * monstera.WaterCapacityLitres); // water generously
                sim.Tick(1f, env);
                Check(sim.Wilt01 > 0.9f * peakWilt, "recovery is not instant: still wilted one hour after watering", $"{sim.Wilt01:0.00} vs {peakWilt:0.00}");
                float wiltAt24 = 0f; float healthAt24 = 0f;
                for (int i = 1; i <= 24 * 10; i++) { sim.Tick(0.1f, env); }
                wiltAt24 = sim.Wilt01; healthAt24 = sim.Health01;
                Check(wiltAt24 < peakWilt && wiltAt24 > 0.05f, "recovery is gradual (partly recovered after a day)", $"wilt {wiltAt24:0.00}");
                Run(sim, env, 360f, 0.1f, hold: 0.6f);
                Check(sim.Wilt01 < 0.02f, "plant fully un-wilts after sustained watering", $"wilt {sim.Wilt01:0.000}");
                Check(sim.Health01 > healthAt24 - 1e-4f && sim.Health01 >= 0.95f, "health recovers after conditions improve (not permanently damaged)", $"health {sim.Health01:0.00}");
            }

            // 3. Heat and cold: brief exposure does little, sustained exposure damages; recovery after cooling.
            {
                PlantSimulation.ActiveHealthModel = PlantSimulation.HealthModel.Original;
                var calm = new PlantSimulation(monstera, 0.6f, 1f, 0.6f);
                Run(calm, Env(monstera, t: 42f), 72f, 0.1f, hold: 0.6f);
                float originalHealth = calm.Health01;
                PlantSimulation.ActiveHealthModel = PlantSimulation.HealthModel.SevereSustained;

                var hot = new PlantSimulation(monstera, 0.6f, 1f, 0.6f);
                Run(hot, Env(monstera, t: 42f), 72f, 0.1f, hold: 0.6f);
                float h72 = hot.Health01;
                Run(hot, Env(monstera, t: 42f), 72f, 0.1f, hold: 0.6f);
                float h144 = hot.Health01;
                sb.AppendLine($"  info  sustained 42 C: health 1.00 -> {h72:0.00} (72 h) -> {h144:0.00} (144 h), exposure {hot.ThermalExposure01:0.00}; original health model after 72 h: {originalHealth:0.00}");
                Check(h72 < 1f && h144 < h72, "sustained severe heat steadily lowers health");
                Check(originalHealth > 0.9f, "(for contrast) the original health model barely reacts to the same heat", $"{originalHealth:0.00}");

                var mild = new PlantSimulation(monstera, 0.6f, 1f, 0.6f);
                Run(mild, Env(monstera, t: 42f), 72f, 0.1f, hold: 0.6f);
                float damaged = mild.Health01;
                Run(mild, Env(monstera), 480f, 0.1f, hold: 0.6f);
                Check(damaged < 0.95f && mild.Health01 > 0.95f && mild.IsAlive, "a plant damaged by sustained heat recovers when conditions return to suitable", $"{damaged:0.00} -> {mild.Health01:0.00}");

                // Brief versus sustained cold (Monstera ideal 18-29 C, tolerance 10: 5 C is maximum stress).
                float MinHealthAfterCold(float coldHours, float totalHours)
                {
                    var sim = new PlantSimulation(monstera, 0.6f, 1f, 0.6f); float min = 1f;
                    for (int i = 0; i < Mathf.RoundToInt(totalHours * 10f); i++)
                    {
                        sim.Tick(0.1f, Env(monstera, t: i * 0.1f < coldHours ? 5f : (float?)null));
                        float miss = 0.6f - sim.Water01; if (miss > 0f) sim.AddWater(miss * monstera.WaterCapacityLitres);
                        min = Mathf.Min(min, sim.Health01);
                    }
                    return min;
                }
                float brief3 = MinHealthAfterCold(3f, 300f), brief12 = MinHealthAfterCold(12f, 300f), sustained120 = MinHealthAfterCold(120f, 480f);
                sb.AppendLine($"  info  cold exposure, lowest health: 3 h {brief3:0.000}, 12 h {brief12:0.000}, 120 h {sustained120:0.000}");
                Check(brief3 > 0.99f && brief12 > 0.97f, "brief cold (3-12 h at maximum stress) causes almost no damage", $"3 h {brief3:0.000}, 12 h {brief12:0.000}");
                Check(sustained120 < 0.8f && sustained120 < brief12 - 0.15f, "sustained cold (120 h) causes real damage", $"{sustained120:0.000}");

                var exposureA = new PlantSimulation(monstera, 0.6f, 1f, 0.6f); var exposureB = new PlantSimulation(monstera, 0.6f, 1f, 0.6f);
                Run(exposureA, Env(monstera, t: 5f), 100f, 0.01f, hold: 0.6f); Run(exposureB, Env(monstera, t: 5f), 100f, 0.25f, hold: 0.6f);
                Check(Mathf.Abs(exposureA.ThermalExposure01 - exposureB.ThermalExposure01) < 0.01f, "thermal exposure is independent of step size", $"{exposureA.ThermalExposure01:0.000} vs {exposureB.ThermalExposure01:0.000}");
            }

            // 3b. Banana in the real greenhouse climate (diel cycle from EnvironmentController: mean +/- swing, coldest 03:00).
            {
                (bool alive, float health, float exposure) BananaIn(float mean, float swing, int days, PlantSimulation.HealthModel model)
                {
                    var previous = PlantSimulation.ActiveHealthModel; PlantSimulation.ActiveHealthModel = model;
                    var sim = new PlantSimulation(banana, 0.65f, 1f, 0.6f); float hour = 8f;
                    for (int i = 0; i < days * 24 * 10; i++)
                    {
                        hour = (hour + 0.1f) % 24f;
                        float wave = Mathf.Sin((hour - 9f) / 24f * Mathf.PI * 2f);
                        float light = Mathf.Clamp01(Mathf.Sin((hour - 6f) / 13f * Mathf.PI)) * 0.85f;
                        sim.Tick(0.1f, new EnvironmentConditions(mean + swing * wave, 0.65f - 0.1f * wave, light, Season.Spring, hour, 0));
                        float miss = 0.65f - sim.Water01; if (miss > 0f) sim.AddWater(miss * banana.WaterCapacityLitres);
                    }
                    PlantSimulation.ActiveHealthModel = previous;
                    return (sim.IsAlive, sim.Health01, sim.ThermalExposure01);
                }
                var springNow = BananaIn(16f, 6f, 17, PlantSimulation.HealthModel.SevereSustained);
                var springV2 = BananaIn(16f, 6f, 17, PlantSimulation.HealthModel.SevereStress);
                var winter = BananaIn(5f, 4f, 30, PlantSimulation.HealthModel.SevereSustained);
                var summer = BananaIn(26f, 8f, 17, PlantSimulation.HealthModel.SevereSustained);
                sb.AppendLine($"  info  Banana, 17 days spring (10-22 C): sustained model alive={springNow.alive} health {springNow.health:0.00} exposure {springNow.exposure:0.00}; instantaneous (v2) alive={springV2.alive} health {springV2.health:0.00}");
                sb.AppendLine($"  info  Banana, 30 days winter (1-9 C): alive={winter.alive} health {winter.health:0.00}; 17 days summer (18-34 C): alive={summer.alive} health {summer.health:0.00}");
                Check(springNow.alive, "Banana survives 17 days of the spring day/night climate under the time-weighted exposure model", $"health {springNow.health:0.00}");
                Check(!winter.alive, "Banana still dies in genuinely sustained severe cold (winter climate): death is not disabled", $"alive {winter.alive}, health {winter.health:0.00}");
                Check(summer.alive && summer.health > 0.95f, "Banana thrives in the summer climate", $"health {summer.health:0.00}");
            }

            // 4. Flowering: only flowering species, only when mature, healthy and watered; fades gradually; returns after recovery.
            foreach (PlantData d in new[] { anthurium, orchid, bird })
            {
                Check(d.CanFlower, d.DisplayName + " is configured as a flowering species");
                var env = Env(d);
                var sim = new PlantSimulation(d, 1f, 1f, Mid(d.IdealSoilMoisture01));
                float w = Mid(d.IdealSoilMoisture01);
                Check(sim.Bloom01 > 0.99f, d.DisplayName + ": mature plant starts in full bloom");
                int dry = 0; while (sim.Wilt01 < 0.8f && sim.IsAlive && dry < 150 * 24 * 10) { sim.Tick(0.1f, env); dry++; } // dry out until clearly wilted (no watering)
                float faded = sim.Bloom01;
                sb.AppendLine($"  info  {d.DisplayName}: bloom when clearly wilted {faded:0.00} (health {sim.Health01:0.00}, wilt {sim.Wilt01:0.00})");
                Check(faded < 0.95f && sim.IsAlive, d.DisplayName + ": flowers fade when the plant wilts (plant survives)", $"bloom {faded:0.00}, alive {sim.IsAlive}");
                sim.AddWater(10f * d.WaterCapacityLitres);
                Run(sim, env, 24f * 20f, 0.1f, hold: w);
                Check(sim.Bloom01 > 0.95f, d.DisplayName + ": flowers return after recovery", $"bloom {sim.Bloom01:0.00} health {sim.Health01:0.00}");

                var young = new PlantSimulation(d, Mathf.Max(0f, d.FloweringMinGrowth - 0.15f), 1f, w);
                Check(young.Bloom01 == 0f, d.DisplayName + ": immature plant has no flowers");
            }
            foreach (PlantData d in species.Where(s => !s.CanFlower))
            {
                var sim = new PlantSimulation(d, 1f, 1f, 0.6f);
                Run(sim, Env(d), 100f, 0.1f, hold: 0.6f);
                if (sim.Bloom01 != 0f) Check(false, d.DisplayName + " never blooms", $"bloom {sim.Bloom01}");
            }
            Check(species.Count(s => s.CanFlower) == 3, "exactly Anthurium, Orchid and Bird of Paradise flower", string.Join(",", species.Where(s => s.CanFlower).Select(s => s.DisplayName)));

            // 5. Gradual flower development in an ideal climate after the plant matures.
            {
                var sim = new PlantSimulation(orchid, orchid.FloweringMinGrowth - 0.002f, 1f, Mid(orchid.IdealSoilMoisture01));
                var env = Env(orchid);
                float w = Mid(orchid.IdealSoilMoisture01);
                int crossed = 0;
                while (sim.Growth01 < orchid.FloweringMinGrowth && crossed < 100000) { sim.Tick(0.1f, env); crossed++; float m = w - sim.Water01; if (m > 0) sim.AddWater(m * orchid.WaterCapacityLitres); }
                sim.Tick(0.1f, env);
                float justAfter = sim.Bloom01;
                Run(sim, env, 30f, 0.1f, hold: w); float mid = sim.Bloom01;
                Run(sim, env, 100f, 0.1f, hold: w);
                Check(justAfter < 0.1f && mid > 0.1f && mid < 0.9f && sim.Bloom01 > 0.99f, "orchid buds and blooms gradually after reaching maturity", $"{justAfter:0.00} -> {mid:0.00} -> {sim.Bloom01:0.00}");
            }

            // 6. Step-size independence of wilting and flowering.
            {
                float WiltAfter(float dt)
                {
                    var s = new PlantSimulation(monstera, 0.6f, 1f, 0.6f);
                    Run(s, Env(monstera), 150f, dt);
                    return s.Wilt01;
                }
                float a = WiltAfter(0.01f), b = WiltAfter(0.25f), c = WiltAfter(0.0166f);
                Check(Mathf.Abs(a - b) < 0.02f && Mathf.Abs(a - c) < 0.02f, "wilting is independent of step size", $"{a:0.000} {b:0.000} {c:0.000}");
                float BloomAfter(float dt)
                {
                    var s = new PlantSimulation(orchid, 1f, 1f, 0.5f);
                    Run(s, Env(orchid), 200f, dt);   // drying out then fading
                    return s.Bloom01;
                }
                float ba = BloomAfter(0.01f), bb = BloomAfter(0.25f);
                Check(Mathf.Abs(ba - bb) < 0.03f, "flowering is independent of step size", $"{ba:0.000} vs {bb:0.000}");
            }

            // 7. Species respond differently.
            Check(orchid.WiltSag < monstera.WiltSag && monstera.WiltSag < banana.WiltSag, "wilting response differs by species (thick orchid leaves sag least, banana most)", $"{orchid.WiltSag}, {monstera.WiltSag}, {banana.WiltSag}");
            Check(species.Select(s => s.WiltSag).Distinct().Count() >= 6, "species have varied wilting responses");

            failed = fail;
            sb.Insert(0, $"{passed} passed, {fail} failed\n");
            return sb.ToString();
        }

        /// <summary>Ticks for <paramref name="hours"/>; if hold > 0, tops the soil up to that moisture every step.</summary>
        private static void Run(PlantSimulation sim, EnvironmentConditions env, float hours, float dt, float hold = 0f)
        {
            int steps = Mathf.RoundToInt(hours / dt);
            for (int i = 0; i < steps; i++)
            {
                sim.Tick(dt, env);
                if (hold > 0f)
                {
                    float missing = hold - sim.Water01;
                    if (missing > 0f) sim.AddWater(missing * sim.Data.WaterCapacityLitres);
                }
            }
        }
    }
}
