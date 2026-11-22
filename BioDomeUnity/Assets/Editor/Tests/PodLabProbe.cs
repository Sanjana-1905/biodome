using System.Collections.Generic;
using System.Linq;
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
    /// Short targeted Play Mode check of the Pod Lab live controls (about a minute). Uses the real scene, one pod, one plant.
    /// Command line: -executeMethod BioDome.EditorTools.Tests.PodLabProbe.Run
    /// </summary>
    public static class PodLabProbe
    {
        private static int phase;
        private static float phaseStartHours;
        private static ExperimentGarden garden;
        private static GreenhouseManager manager;
        private static ExperimentPod pod, other;
        private static PlantController plant, otherPlant, display;
        private static readonly StringBuilder log = new StringBuilder();
        private static int fails;

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/BioDome_Working.unity");
            phase = 0; fails = 0; log.Clear();
            EditorApplication.update -= OnUpdate; EditorApplication.update += OnUpdate;
            EditorApplication.isPlaying = true;
        }

        private static float Now() { var c = garden.GreenhouseNow; return c.DayCount * 24f + c.TimeOfDayHours; }

        private static void Check(bool ok, string what) { if (!ok) fails++; log.AppendLine((ok ? "  pass  " : "  FAIL  ") + what); }

        private static string State(string tag, PlantController p)
        {
            var s = p.Simulation; var c = p.LastConditions;
            return $"  {tag}: T {c.TemperatureCelsius:0.0} RH {c.Humidity01:0.00} L {c.Light01:0.00} | growth {s.Growth01:0.0000} stage {s.Stage} health {s.Health01:0.000} wilt {s.Wilt01:0.00} soil {s.Water01:0.00} " +
                   $"rate x{s.GrowthFactor01:0.00} stress T{s.TemperatureStress01:0.00} H{s.HumidityStress01:0.00} L{s.LightStress01:0.00} W{s.WaterStress01:0.00}";
        }

        private static void OnUpdate()
        {
            if (!EditorApplication.isPlaying) return;
            if (garden == null || manager == null)
            {
                garden = Object.FindAnyObjectByType<ExperimentGarden>();
                manager = Object.FindAnyObjectByType<GreenhouseManager>();
                return;
            }
            if (garden.History.Count == 0 || Object.FindAnyObjectByType<PodTestPanel>() == null) return;

            switch (phase)
            {
                case 0:
                    pod = garden.Pods.First(p => p.DisplayName == "Control");
                    other = garden.Pods.First(p => p.DisplayName == "Low light");
                    plant = pod.Plants[0]; otherPlant = other.Plants[0];
                    display = Object.FindObjectsByType<PlantController>().First(p => p.ZoneProfile == null && p.Data.SpeciesId == "monstera");
                    Check(pod.Profile != pod.OriginalProfile && plant.ZoneProfile == pod.Profile, "Control pod and its plants use the pod's own runtime profile copy");
                    Check(pod.Profile != other.Profile, "Pods do not share a profile object");
                    manager.SetSpeedMultiplier(50f); // 20 simulated hours per real second
                    phaseStartHours = Now(); phase = 1;
                    log.AppendLine("START (Control pod, plant 1)").AppendLine(State("control", plant)).AppendLine(State("low-light pod", otherPlant)).AppendLine(State("greenhouse monstera", display));
                    break;

                case 1: // baseline 12 h
                    if (Now() - phaseStartHours < 12f) return;
                    log.AppendLine("AFTER 12 h baseline").AppendLine(State("control", plant));
                    pod.SetLiveConditions(15f, 0f, 1f, 1f); // +15 C
                    phaseStartHours = Now(); phase = 2;
                    break;

                case 2: // +15 C for 24 h
                    if (Now() - phaseStartHours < 24f) return;
                    {
                        float g = garden.GreenhouseNow.TemperatureCelsius;
                        log.AppendLine("AFTER 24 h at +15 C").AppendLine(State("control", plant)).AppendLine(State("low-light pod", otherPlant)).AppendLine(State("greenhouse monstera", display));
                        Check(Mathf.Abs(plant.LastConditions.TemperatureCelsius - (g + 15f)) < 0.6f, $"Control plant receives greenhouse +15 C ({plant.LastConditions.TemperatureCelsius:0.0} vs greenhouse {g:0.0})");
                        Check(Mathf.Abs(otherPlant.LastConditions.TemperatureCelsius - g) < 0.6f, "Low-light pod still receives greenhouse temperature");
                        Check(Mathf.Abs(display.LastConditions.TemperatureCelsius - g) < 0.6f, "Greenhouse plant still receives greenhouse temperature");
                        Check(pod.OriginalProfile.TemperatureOffsetCelsius == 0f, "Shared Zone_Control asset untouched");
                    }
                    pod.SetLiveConditions(0f, 0f, 1f, 0f); // restore temperature, cut irrigation
                    phaseStartHours = Now(); phase = 3;
                    break;

                case 3: // irrigation 0 until wilting (max 6 days)
                    if (plant.Simulation.Wilt01 < 0.5f && Now() - phaseStartHours < 144f) return;
                    log.AppendLine($"AFTER {Now() - phaseStartHours:0} h with irrigation share 0").AppendLine(State("control", plant)).AppendLine(State("low-light pod", otherPlant));
                    Check(plant.Simulation.Wilt01 >= 0.5f, "Plant wilts once its soil dries (irrigation 0)");
                    Check(otherPlant.Simulation.Wilt01 < 0.1f, "Other pod's plant is not wilting");
                    pod.ResetLiveConditions();
                    phaseStartHours = Now(); phase = 4;
                    break;

                case 4: // restored, 48 h
                    if (Now() - phaseStartHours < 48f) return;
                    log.AppendLine("AFTER 48 h restored").AppendLine(State("control", plant));
                    Check(plant.Simulation.Wilt01 < 0.5f && plant.Simulation.Water01 > 0.3f, "Plant recovers after irrigation is restored");
                    Debug.Log($"PODLAB {(fails == 0 ? "PASSED" : "FAILED")} ({fails} failed)\n{log}");
                    EditorApplication.update -= OnUpdate;
                    EditorApplication.Exit(fails == 0 ? 0 : 1);
                    break;
            }
        }
    }
}
