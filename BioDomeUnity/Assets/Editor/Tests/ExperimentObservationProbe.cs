using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BioDome.Core;
using BioDome.Experiment;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Play Mode observation of the five experimental pods in the real scene, untouched, over simulated days.
    /// Prints equivalence/isolation checks and the Day 1 / 7 / 14 / 30 comparison table, and writes the daily log.
    /// Command line: -executeMethod BioDome.EditorTools.Tests.ExperimentObservationProbe.Run -probeOut FILE [-probeSpeed 8] [-probeDay 30]
    /// </summary>
    public static class ExperimentObservationProbe
    {
        private static string outPath;
        private static int lastDay = 30;
        private static bool started, announced;

        [MenuItem("BioDome/Tests/Run Experiment Observation (30 days)")]
        public static void RunFromMenu() { outPath = Path.GetFullPath("Docs/Verification/experiment_daily.csv"); Begin(8f); }

        public static void Run()
        {
            outPath = Arg("-probeOut") ?? "experiment_daily.csv";
            lastDay = int.Parse(Arg("-probeDay") ?? "30", CultureInfo.InvariantCulture);
            EditorSceneManager.OpenScene("Assets/Scenes/BioDome_Working.unity");
            Begin(float.Parse(Arg("-probeSpeed") ?? "8", CultureInfo.InvariantCulture));
        }

        private static float speed;
        private static void Begin(float s)
        {
            speed = s; started = false; announced = false;
            EditorApplication.update -= OnUpdate; EditorApplication.update += OnUpdate;
            EditorApplication.isPlaying = true;
        }

        private static void OnUpdate()
        {
            if (!EditorApplication.isPlaying) return;
            var manager = Object.FindFirstObjectByType<GreenhouseManager>();
            var garden = Object.FindFirstObjectByType<ExperimentGarden>();
            if (manager == null || garden == null || garden.History.Count == 0) return;
            if (!started)
            {
                started = true;
                typeof(GreenhouseManager).GetField("simulatedHoursPerSecond", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, speed);
            }
            if (!announced)
            {
                announced = true;
                var first = garden.History[0];
                Debug.Log($"EXPERIMENT START day {first.Day}: equivalent={garden.StartsEquivalent} ({garden.EquivalenceReport}) isolated={garden.ConditionsIsolated()}");
                foreach (var pod in garden.Pods)
                {
                    var m = pod.Measure(); var p = pod.Plants[0];
                    Debug.Log($"EXPERIMENT POD {pod.DisplayName}: plants {m.Total} species {string.Join("/", pod.Plants.Select(x => x.Data.SpeciesId).Distinct())} growth {m.Growth:0.000} health {m.Health:0.000} moisture {m.Moisture:0.000} | profile T{pod.Profile.TemperatureOffsetCelsius:+0;-0;0} H{pod.Profile.HumidityOffset:+0.00;-0.00;0.00} L x{pod.Profile.LightMultiplier:0.00} intake {pod.Profile.WaterIntakeFactor:0.00} share {pod.Profile.WaterShareWeight:0.00} | irrigation {(pod.Irrigation != null ? pod.Irrigation.name + (pod.Irrigation.SpeciesAware ? " [species-aware]" : " [fixed thresholds]") : "none")}");
                }
                int leak = Object.FindObjectsByType<PlantController>(FindObjectsSortMode.None).Count(p => p.ZoneProfile != null && !garden.Pods.Any(pd => pd.Plants.Contains(p)));
                Debug.Log("EXPERIMENT LEAK plants with a zone profile that belong to no pod: " + leak);
            }
            if (garden.SnapshotOfDay(lastDay) == null) return;

            var sb = new StringBuilder("day,pod,alive,total,growth,health,moisture,stress,temp_stress,humidity_stress,light_stress,water_stress\n");
            foreach (var snap in garden.History)
                for (int i = 0; i < garden.Pods.Count; i++)
                {
                    var m = snap.Pods[i];
                    sb.AppendLine(string.Join(",", new[] { snap.Day.ToString(), garden.Pods[i].DisplayName, m.Alive.ToString(), m.Total.ToString(), F(m.Growth), F(m.Health), F(m.Moisture), F(m.Stress), F(m.TemperatureStress), F(m.HumidityStress), F(m.LightStress), F(m.WaterStress) }));
                }
            File.WriteAllText(outPath, sb.ToString());
            foreach (int d in new[] { 1, 7, 14, 30 }.Where(d => d <= lastDay))
            {
                var snap = garden.SnapshotOfDay(d);
                Debug.Log($"EXPERIMENT TABLE day {d} (sim hour {snap.Hours:0}) " + string.Join(" | ", Enumerable.Range(0, garden.Pods.Count).Select(i =>
                {
                    var m = snap.Pods[i];
                    return $"{garden.Pods[i].DisplayName}: alive {m.Alive}/{m.Total} g{m.Growth:0.000} h{m.Health:0.000} moist{m.Moisture:0.00} stress{m.Stress:0.00} (T{m.TemperatureStress:0.00} H{m.HumidityStress:0.00} L{m.LightStress:0.00} W{m.WaterStress:0.00})";
                })));
            }
            EditorApplication.update -= OnUpdate;
            EditorApplication.Exit(0);
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
