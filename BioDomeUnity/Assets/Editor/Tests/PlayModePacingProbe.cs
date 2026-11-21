using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Runs the REAL scene in Play Mode (batch or interactive) and logs growth pacing: real time, simulated time, speed, and per-species
    /// simulated growth, displayed (mesh) growth and stage. Writes a CSV; changes no asset and does not save the scene.
    /// Command line: -executeMethod BioDome.EditorTools.Tests.PlayModePacingProbe.Run -probeOut FILE [-probeScene PATH] [-probeSeconds 300] [-probeSpeed 2]
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModePacingProbe
    {
        private const string ActiveKey = "BioDome.PacingProbe.Active";
        private const string OutKey = "BioDome.PacingProbe.Out";
        private const string SpeedKey = "BioDome.PacingProbe.Speed";
        private const string SecondsKey = "BioDome.PacingProbe.Seconds";
        private static readonly string[] Species = { "monstera", "banana", "bird_of_paradise", "calathea", "anthurium", "orchid" };
        private static readonly float[] SampleTimes = { 0f, 0.5f, 1f, 2f, 5f, 10f, 30f, 60f, 120f, 300f };

        private static float startReal = -1f;
        private static int nextSample;
        private static int frames;
        private static StringBuilder csv;

        static PlayModePacingProbe()
        {
            if (SessionState.GetBool(ActiveKey, false) && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.update -= OnUpdate;
                EditorApplication.update += OnUpdate;
            }
        }

        public static void Run()
        {
            string scene = Arg("-probeScene") ?? "Assets/Scenes/BioDome_Working.unity";
            SessionState.SetString(OutKey, Arg("-probeOut") ?? "pacing.csv");
            SessionState.SetString(SpeedKey, Arg("-probeSpeed") ?? "");
            SessionState.SetString(SecondsKey, Arg("-probeSeconds") ?? "300");
            SessionState.SetBool(ActiveKey, true);
            EditorSceneManager.OpenScene(scene);
            startReal = -1f; frames = 0;
            // This project enters Play Mode without a domain reload, so the startup hook above does not run again: subscribe here too.
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
            EditorApplication.isPlaying = true;
        }

        private static void OnUpdate()
        {
            if (!EditorApplication.isPlaying) return;
            var manager = UnityEngine.Object.FindFirstObjectByType<BioDome.Core.GreenhouseManager>();
            var env = UnityEngine.Object.FindFirstObjectByType<EnvironmentController>();
            if (manager == null || env == null) return;

            if (startReal < 0f)
            {
                startReal = Time.realtimeSinceStartup;
                nextSample = 0;
                csv = new StringBuilder("real_s,frames,sim_hours,speed_h_per_s,paused,species,plants,growth_mean,growth_min,growth_max,displayed_mean,stage_first,mesh_pending\n");
                string s = SessionState.GetString(SpeedKey, "");
                if (s.Length > 0) typeof(BioDome.Core.GreenhouseManager).GetField("simulatedHoursPerSecond", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(manager, float.Parse(s, CultureInfo.InvariantCulture));
            }
            frames++;
            float t = Time.realtimeSinceStartup - startReal;
            float total = float.Parse(SessionState.GetString(SecondsKey, "300"), CultureInfo.InvariantCulture);

            while (nextSample < SampleTimes.Length && t >= SampleTimes[nextSample] && SampleTimes[nextSample] <= total)
            {
                Sample(t, manager, env);
                nextSample++;
            }

            if (t >= total || nextSample >= SampleTimes.Length || SampleTimes[nextSample] > total)
            {
                File.WriteAllText(SessionState.GetString(OutKey, "pacing.csv"), csv.ToString());
                SessionState.SetBool(ActiveKey, false);
                EditorApplication.update -= OnUpdate;
                EditorApplication.Exit(0);
            }
        }

        private static void Sample(float t, BioDome.Core.GreenhouseManager manager, EnvironmentController env)
        {
            float speed = (float)typeof(BioDome.Core.GreenhouseManager).GetField("simulatedHoursPerSecond", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
            float simHours = env.Current.DayCount * 24f + env.Current.TimeOfDayHours;
            var plants = UnityEngine.Object.FindObjectsByType<PlantController>(FindObjectsSortMode.None);
            int pending = 0;
            foreach (var group in plants.Where(p => p.IsInitialized && p.ZoneProfile == null && p.Data != null).GroupBy(p => p.Data.SpeciesId))
            {
                if (!Species.Contains(group.Key)) continue;
                var list = group.ToList();
                var growth = list.Select(p => p.Simulation.Growth01).ToList();
                var displayed = list.Select(p => { var g = p.GetComponentInChildren<PlantGenerator>(true); return g != null ? g.Growth01 : p.Simulation.Growth01; }).ToList();
                PlantController first = list[0];
                csv.AppendLine(string.Join(",", new[]
                {
                    t.ToString("0.0", CultureInfo.InvariantCulture), frames.ToString(), simHours.ToString("0.000", CultureInfo.InvariantCulture),
                    speed.ToString("0.00", CultureInfo.InvariantCulture), manager.Paused ? "1" : "0", group.Key, list.Count.ToString(),
                    growth.Average().ToString("0.0000", CultureInfo.InvariantCulture), growth.Min().ToString("0.0000", CultureInfo.InvariantCulture),
                    growth.Max().ToString("0.0000", CultureInfo.InvariantCulture), displayed.Average().ToString("0.0000", CultureInfo.InvariantCulture),
                    first.Simulation.Stage.ToString(), pending.ToString()
                }));
            }
        }

        private static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
