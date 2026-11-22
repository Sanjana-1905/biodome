using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BioDome.Core;
using BioDome.Irrigation;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Real Play Mode check of Stage 2: dries one plant per species (watering off), then waters it, and logs the SIMULATED wilt/health/bloom next to what the
    /// plant actually DISPLAYS (mesh height scale, leaf tint, flower level of the shown mesh). Also logs whole-greenhouse health.
    /// Command line: -executeMethod BioDome.EditorTools.Tests.PlayModeHealthProbe.Run -probeOut FILE [-probeSpeed 4]
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeHealthProbe
    {
        private static readonly string[] Species = { "monstera", "banana", "bird_of_paradise", "calathea", "anthurium", "orchid" };
        private const float DryUntil = 8f, Every = 4f;
        private static float EndAt = 70f;
        private static int initialPlants, initialPods;
        private static bool control;
        private static float startReal = -1f, nextSample;
        private static bool rewatered;
        private static StringBuilder csv;
        private static readonly Dictionary<string, PlantController> subjects = new Dictionary<string, PlantController>();
        private static string outPath;

        public static void Run()
        {
            outPath = Arg("-probeOut") ?? "health.csv";
            EditorSceneManager.OpenScene("Assets/Scenes/BioDome_Working.unity");
            startReal = -1f; nextSample = 0f; rewatered = false; subjects.Clear();
            control = Arg("-probeControl") != null; // control: touch nothing, just observe the normal greenhouse
            EndAt = float.Parse(Arg("-probeSeconds") ?? "70", CultureInfo.InvariantCulture);
            if (Arg("-probeHealth") == "original") BioDome.Simulation.PlantSimulation.ActiveHealthModel = BioDome.Simulation.PlantSimulation.HealthModel.Original;
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
            EditorApplication.isPlaying = true;
        }

        private static void OnUpdate()
        {
            if (!EditorApplication.isPlaying) return;
            var manager = Object.FindFirstObjectByType<BioDome.Core.GreenhouseManager>();
            if (manager == null) return;
            var all = Object.FindObjectsByType<PlantController>(FindObjectsSortMode.None);
            if (all.Length == 0 || !all.All(p => p.IsInitialized)) return;

            if (startReal < 0f)
            {
                startReal = Time.realtimeSinceStartup;
                initialPlants = all.Count(p => p.ZoneProfile == null); initialPods = all.Count(p => p.ZoneProfile != null);
                if (Arg("-probeIrrigation") == "legacy")
                    foreach (var ic in Object.FindObjectsByType<IrrigationController>(FindObjectsSortMode.None))
                        typeof(IrrigationController).GetField("speciesAware", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ic, false);
                csv = new StringBuilder("real_s,phase,species,growth,health,wilt,bloom,condition_recovering,mesh_scale_y,expected_scale_y,tint_r,tint_g,tint_b,shown_bloom_level,greenhouse_min_health,greenhouse_dead\n");
                var mt = typeof(BioDome.Core.GreenhouseManager);
                mt.GetField("simulatedHoursPerSecond", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, float.Parse(Arg("-probeSpeed") ?? "4", CultureInfo.InvariantCulture));
                foreach (string id in Species)
                {
                    var p = all.Where(x => x.ZoneProfile == null && x.Data.SpeciesId == id && x.Simulation.Growth01 >= 0.9f).OrderBy(x => x.name).FirstOrDefault();
                    if (p == null) continue;
                    subjects[id] = p;

                }
            }

            float t = Time.realtimeSinceStartup - startReal;
            if (!control && t < DryUntil)
                foreach (var p in subjects.Values) SetWater(p, Mathf.Min(p.Simulation.Water01, 0.02f)); // hold ONLY these plants dry (the rest of the greenhouse is watered normally)
            if (!control && !rewatered && t >= DryUntil)
            {
                rewatered = true;
                foreach (var p in subjects.Values) p.ReceiveWater(100f);
            }
            if (t >= nextSample)
            {
                nextSample += Every;
                Sample(t, all);
            }
            if (t >= EndAt)
            {
                var irr = Object.FindFirstObjectByType<IrrigationController>();
                var dead = all.Where(p => !p.Simulation.IsAlive).GroupBy(p => (p.ZoneProfile != null ? "pod:" : "") + p.Data.SpeciesId).Select(g => g.Key + "=" + g.Count());
                var water = all.Where(p => p.Simulation.IsAlive).GroupBy(p => (p.ZoneProfile != null ? "pod:" : "") + p.Data.SpeciesId).Select(g => g.Key + " water " + g.Average(p => p.Simulation.Water01).ToString("0.00") + " health " + g.Average(p => p.Simulation.Health01).ToString("0.00"));
                var cause = all.Where(p => !p.Simulation.IsAlive).GroupBy(p => (p.ZoneProfile != null ? "pod:" : "") + p.Data.SpeciesId).Select(g => g.Key + " T" + g.Average(p => p.Simulation.TemperatureStress01).ToString("0.00") + " H" + g.Average(p => p.Simulation.HumidityStress01).ToString("0.00") + " L" + g.Average(p => p.Simulation.LightStress01).ToString("0.00") + " W" + g.Average(p => p.Simulation.WaterStress01).ToString("0.00") + " water" + g.Average(p => p.Simulation.Water01).ToString("0.00") + " ageDays" + g.Average(p => p.Simulation.AgeHours / 24f).ToString("0.0"));
                Debug.Log("PROBE CAUSE (stress factors of dead plants at death): " + string.Join(" | ", cause));
                var ics = Object.FindObjectsByType<IrrigationController>(FindObjectsSortMode.None);
                var stress = all.Where(p => p.Simulation.IsAlive).GroupBy(p => (p.ZoneProfile != null ? "pod:" : "") + p.Data.SpeciesId).Select(g => g.Key + " W" + g.Average(p => p.Simulation.WaterStress01).ToString("0.00") + " moist" + g.Average(p => p.Simulation.Water01).ToString("0.00"));
                Debug.Log("PROBE IRRIGATION mode " + (Arg("-probeIrrigation") ?? "scene") + " | controllers " + ics.Length + " speciesAware " + ics.Count(c => c.SpeciesAware) + " | litres delivered " + ics.Sum(c => c.TotalDeliveredLitres).ToString("0") + " | sprinkler-hours (sum) " + ics.Sum(c => c.RunningHours).ToString("0") + " | mean reservoir " + ics.Average(c => c.Reservoir01).ToString("0.00"));
                var table = ics.OrderBy(c => c.transform.parent != null ? c.transform.parent.name : c.name).Select(c => (c.transform.parent != null ? c.transform.parent.name : c.name) + " [" + (c.SpeciesAware ? "sp" : "orig") + "] on " + c.RunningHours.ToString("0") + "h deliver " + c.DeliveringHours.ToString("0") + "h litres " + c.TotalDeliveredLitres.ToString("0") + " starts " + c.WateringStarts + " maxOver " + c.MaxOvershoot.ToString("0.000"));
                Debug.Log("PROBE CONTROLLERS: " + string.Join(" | ", table));
                Debug.Log("PROBE POPULATION initial greenhouse " + initialPlants + " pods " + initialPods + " | alive greenhouse " + all.Count(p => p.ZoneProfile == null && p.Simulation.IsAlive) + " pods " + all.Count(p => p.ZoneProfile != null && p.Simulation.IsAlive) + " | stress/moisture of survivors: " + string.Join("; ", stress));
                Debug.Log("PROBE SUMMARY health model " + BioDome.Simulation.PlantSimulation.ActiveHealthModel + " | reservoir " + (irr != null ? irr.Reservoir01.ToString("0.00") : "n/a") + " | dead by species: " + string.Join(", ", dead) + " | alive: " + string.Join("; ", water));
                File.WriteAllText(outPath, csv.ToString());
                EditorApplication.update -= OnUpdate;
                EditorApplication.Exit(0);
            }
        }

        private static void Sample(float t, PlantController[] all)
        {
            float minHealth = all.Where(p => p.ZoneProfile == null).Min(p => p.Simulation.Health01);
            int dead = all.Count(p => p.ZoneProfile == null && !p.Simulation.IsAlive);
            foreach (var kv in subjects)
            {
                var p = kv.Value; var sim = p.Simulation;
                var gen = p.GetComponentInChildren<PlantGenerator>(true);
                Transform mesh = gen.transform.Find("Mesh");
                Color tint = (Color)typeof(PlantGenerator).GetField("healthTint", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(gen);
                int code = (int)typeof(PlantController).GetField("displayedStep", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(p);
                float expected = 1f - p.Data.WiltSag * sim.Wilt01;
                csv.AppendLine(string.Join(",", new[]
                {
                    t.ToString("0.0", CultureInfo.InvariantCulture), t < DryUntil ? "drought" : "rewatered", kv.Key,
                    F(sim.Growth01), F(sim.Health01), F(sim.Wilt01), F(sim.Bloom01), sim.IsRecovering ? "1" : "0",
                    F(mesh.localScale.y), F(expected), F(tint.r), F(tint.g), F(tint.b),
                    PlantGenerator.BloomLevelOfCode(code).ToString(), F(minHealth), dead.ToString()
                }));
            }
        }

        private static void SetWater(PlantController p, float value)
        {
            typeof(BioDome.Simulation.PlantSimulation).GetProperty("Water01").GetSetMethod(true).Invoke(p.Simulation, new object[] { value });
        }

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        private static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
