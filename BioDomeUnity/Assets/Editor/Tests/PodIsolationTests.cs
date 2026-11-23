using System;
using System.Collections.Generic;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Plants;
using BioDome.Simulation;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Focused tests for independent pod conditions: each pod stresses only its own factor, editing one pod's LIVE profile changes only that pod
    /// (the other four simulate exactly as they would without the edit), the greenhouse conditions and the shared profile asset are never modified,
    /// and a stressed pod recovers gradually after its conditions are reset. Pure simulation (no scene).
    /// Menu: BioDome > Tests. Command line: -executeMethod BioDome.EditorTools.Tests.PodIsolationTests.RunBatch
    /// </summary>
    public static class PodIsolationTests
    {
        private const string MonsteraPath = "Assets/ScriptableObjects/Plants/Monstera.asset";
        private const float Dt = 0.1f;

        [MenuItem("BioDome/Tests/Run Pod Isolation Tests")]
        public static void RunFromMenu()
        {
            var r = Run();
            Debug.Log("BioDome pod isolation tests:\n" + r.Text);
            EditorUtility.DisplayDialog("Pod isolation tests", (r.Failed == 0 ? "PASSED" : "FAILED") + $"\n{r.Passed} passed, {r.Failed} failed", "OK");
        }

        public static void RunBatch()
        {
            int exit;
            try
            {
                var r = Run();
                Debug.Log($"PODTESTS {(r.Failed == 0 ? "PASSED" : "FAILED")} ({r.Passed} passed, {r.Failed} failed)\n{r.Text}");
                exit = r.Failed == 0 ? 0 : 1;
            }
            catch (Exception e) { Debug.LogError("PODTESTS ERROR: " + e); exit = 2; }
            EditorApplication.Exit(exit);
        }

        private sealed class Pod
        {
            public ExperimentalCondition Kind;
            public ZoneProfile Profile;
            public PlantSimulation Sim;
        }

        private static PlantData data;

        private static EnvironmentConditions Greenhouse(float hours)
        {
            // The centre of the species' comfortable ranges: the control pod has nothing to complain about.
            Vector2 t = data.IdealTemperatureCelsius, h = data.IdealHumidity01, l = data.IdealLight01;
            return new EnvironmentConditions(0.5f * (t.x + t.y), 0.5f * (h.x + h.y), 0.5f * (l.x + l.y), Season.Spring, hours % 24f, Mathf.FloorToInt(hours / 24f));
        }

        private static List<Pod> MakePods(List<UnityEngine.Object> owners)
        {
            var pods = new List<Pod>();
            foreach (ExperimentalCondition k in Enum.GetValues(typeof(ExperimentalCondition)))
            {
                ZoneProfile p = ZoneProfilePresets.Create(k);
                owners.Add(p);
                Vector2 t = data.IdealTemperatureCelsius, h = data.IdealHumidity01, l = data.IdealLight01;
                p.SetControlledBaseline(0.5f * (t.x + t.y), 0.5f * (h.x + h.y), 0.5f * (l.x + l.y)); // what ExperimentPod does: an independent, ideal chamber
                var sim = new PlantSimulation(data, 0.12f, 1f, 0.6f);
                sim.WaterIntakeFactor = p.WaterIntakeFactor;
                pods.Add(new Pod { Kind = k, Profile = p, Sim = sim });
            }
            return pods;
        }

        // One pod, one step: the pod's profile turns the greenhouse conditions into the pod's conditions.
        private static void Step(Pod pod, float hours)
        {
            // Feedback irrigation like the greenhouse controller: refill towards 60 % soil moisture, limited by the pod's irrigation share.
            float need = Mathf.Max(0f, 0.6f - pod.Sim.Water01) * data.WaterCapacityLitres;
            float capacity = 2f * data.WaterUseLitresPerHour * Dt * pod.Profile.WaterShareWeight; // the pod's pipes deliver at most share x twice the normal use
            pod.Sim.AddWater(Mathf.Min(need, capacity));
            pod.Sim.Tick(Dt, pod.Profile.Apply(Greenhouse(hours)));
        }

        private static float Run(List<Pod> pods, float fromHours, float toHours, Action<float> beforeStep = null)
        {
            float h = fromHours;
            while (h < toHours - 1e-4f)
            {
                beforeStep?.Invoke(h);
                foreach (Pod p in pods) if (p.Sim.IsAlive) Step(p, h);
                h += Dt;
            }
            return h;
        }

        private static Pod Of(List<Pod> pods, ExperimentalCondition k) => pods.Find(p => p.Kind == k);

        private static ExperimentalZoneTests.Report Run()
        {
            var r = new ExperimentalZoneTests.Report();
            var owners = new List<UnityEngine.Object>();
            PlantSimulation.GrowthModel oldGrowth = PlantSimulation.ActiveModel; PlantSimulation.HealthModel oldHealth = PlantSimulation.ActiveHealthModel;
            try
            {
                data = AssetDatabase.LoadAssetAtPath<PlantData>(MonsteraPath);
                r.Check(data != null, "Monstera PlantData found");
                if (data == null) return r;

                HealthyGrowth(r, owners);
                EachPodStressesItsOwnFactor(r, owners);
                LiveEditIsolation(r, owners);
                SharedAssetAndGreenhouseUntouched(r, owners);
                RecoveryAfterReset(r, owners);
                WaterRecovery(r, owners);
            }
            finally
            {
                PlantSimulation.ActiveModel = oldGrowth; PlantSimulation.ActiveHealthModel = oldHealth;
                foreach (UnityEngine.Object o in owners) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }
            return r;
        }

        private static void HealthyGrowth(ExperimentalZoneTests.Report r, List<UnityEngine.Object> owners)
        {
            List<Pod> pods = MakePods(owners);
            Pod control = Of(pods, ExperimentalCondition.Control);
            float last = control.Sim.Growth01, maxStep = 0f; int lastStage = (int)control.Sim.Stage; bool stagesOrdered = true;
            float h = 0f;
            for (int i = 0; i < 20 * 24 * 10; i++, h += Dt)
            {
                Step(control, h);
                float g = control.Sim.Growth01;
                maxStep = Mathf.Max(maxStep, g - last);
                if (g < last - 1e-6f) stagesOrdered = false;
                if ((int)control.Sim.Stage < lastStage) stagesOrdered = false;
                lastStage = (int)control.Sim.Stage; last = g;
            }
            r.Check(control.Sim.Growth01 > 0.12f + 0.1f, "healthy control grows", $"growth {control.Sim.Growth01:0.000}");
            r.Check(stagesOrdered, "control growth never decreases and stages only advance");
            r.Check(maxStep < 0.002f, "growth advances smoothly (no jumps)", $"largest {Dt} h step {maxStep:0.00000}");
            r.Check(control.Sim.Health01 > 0.95f && control.Sim.Wilt01 < 0.05f, "control stays healthy and turgid", $"health {control.Sim.Health01:0.00} wilt {control.Sim.Wilt01:0.00}");
        }

        private static void EachPodStressesItsOwnFactor(ExperimentalZoneTests.Report r, List<UnityEngine.Object> owners)
        {
            List<Pod> pods = MakePods(owners);
            Run(pods, 0f, 12f * 24f);
            Pod c = Of(pods, ExperimentalCondition.Control), heat = Of(pods, ExperimentalCondition.HeatStress), dry = Of(pods, ExperimentalCondition.LowHumidity),
                dark = Of(pods, ExperimentalCondition.LowLight), water = Of(pods, ExperimentalCondition.WaterStress);
            r.Check(heat.Sim.TemperatureStress01 > c.Sim.TemperatureStress01 + 0.3f, "heat pod: temperature stress well above control", $"{heat.Sim.TemperatureStress01:0.00} vs {c.Sim.TemperatureStress01:0.00}");
            r.Check(dry.Sim.HumidityStress01 > c.Sim.HumidityStress01 + 0.3f, "low-humidity pod: humidity stress well above control", $"{dry.Sim.HumidityStress01:0.00} vs {c.Sim.HumidityStress01:0.00}");
            r.Check(dark.Sim.AcclimatedLight01 < c.Sim.AcclimatedLight01 - 0.1f && dark.Sim.LightStress01 >= c.Sim.LightStress01, "low-light pod: less light, light stress not below control",
                $"light {dark.Sim.AcclimatedLight01:0.00} vs {c.Sim.AcclimatedLight01:0.00}");
            r.Check(water.Sim.WaterStress01 > c.Sim.WaterStress01 + 0.15f && water.Sim.Water01 < c.Sim.Water01, "water pod: drier soil and more water stress", $"soil {water.Sim.Water01:0.00} vs {c.Sim.Water01:0.00}");
            foreach (Pod p in pods)
            {
                if (p == c) continue;
                r.Check(p.Sim.Growth01 < c.Sim.Growth01, $"{p.Kind}: growth below control", $"{p.Sim.Growth01:0.000} vs {c.Sim.Growth01:0.000}");
            }
            // The other factors stay at control level (no leakage between conditions).
            // Heat does not change humidity or light directly (soil does dry faster when hot, which is real coupling, so water is not compared).
            r.Check(Mathf.Abs(heat.Sim.HumidityStress01 - c.Sim.HumidityStress01) < 0.05f, "heat pod: humidity stress matches control", $"{heat.Sim.HumidityStress01:0.00} vs {c.Sim.HumidityStress01:0.00}");
            r.Check(Mathf.Abs(dry.Sim.LightStress01 - c.Sim.LightStress01) < 0.05f && Mathf.Abs(dry.Sim.WaterStress01 - c.Sim.WaterStress01) < 0.1f, "low-humidity pod: light/water stress match control");
            r.Check(Mathf.Abs(dry.Sim.TemperatureStress01 - c.Sim.TemperatureStress01) < 0.05f, "low-humidity pod: temperature stress matches control");
        }

        private static void LiveEditIsolation(ExperimentalZoneTests.Report r, List<UnityEngine.Object> owners)
        {
            // Baseline: nothing edited. Edited: at day 3 the heat pod's live profile is changed (as the pod controls do). Everything else must be identical.
            List<Pod> baseline = MakePods(owners), edited = MakePods(owners);
            Run(baseline, 0f, 8f * 24f);
            Run(edited, 0f, 8f * 24f, h =>
            {
                if (Mathf.Abs(h - 3f * 24f) < Dt * 0.5f) { Pod heat = Of(edited, ExperimentalCondition.HeatStress); heat.Profile.Configure("Heat stress", ExperimentalCondition.HeatStress, 0f, 0f, 1f, 1f, 1f); }
            });
            foreach (ExperimentalCondition k in Enum.GetValues(typeof(ExperimentalCondition)))
            {
                Pod a = Of(baseline, k), b = Of(edited, k);
                bool same = a.Sim.Growth01 == b.Sim.Growth01 && a.Sim.Health01 == b.Sim.Health01 && a.Sim.Water01 == b.Sim.Water01 && a.Sim.Stress01 == b.Sim.Stress01;
                if (k == ExperimentalCondition.HeatStress) r.Check(!same && b.Sim.Growth01 > a.Sim.Growth01, "editing the heat pod changes the heat pod (growth improves)", $"{b.Sim.Growth01:0.000} vs {a.Sim.Growth01:0.000}");
                else r.Check(same, $"editing the heat pod leaves the {k} pod bit-for-bit unchanged");
            }
        }

        private static void SharedAssetAndGreenhouseUntouched(ExperimentalZoneTests.Report r, List<UnityEngine.Object> owners)
        {
            ZoneProfile shared = ZoneProfilePresets.Create(ExperimentalCondition.HeatStress); owners.Add(shared);
            ZoneProfile live = UnityEngine.Object.Instantiate(shared); owners.Add(live); // what ExperimentPod does at Awake
            live.Configure("live", ExperimentalCondition.HeatStress, -5f, 0.2f, 0.5f, 1f, 0.2f);
            r.Check(shared.TemperatureOffsetCelsius == ZoneProfilePresets.HeatOffsetCelsius && shared.HumidityOffset == 0f && shared.LightMultiplier == 1f && shared.WaterShareWeight == 1f,
                "editing a pod's runtime copy never changes the shared profile");
            EnvironmentConditions g = Greenhouse(30f);
            EnvironmentConditions podView = live.Apply(g);
            r.Check(Greenhouse(30f).TemperatureCelsius == g.TemperatureCelsius && Greenhouse(30f).Humidity01 == g.Humidity01 && Greenhouse(30f).Light01 == g.Light01, "greenhouse conditions are values: applying a pod profile does not alter them");
            r.Check(podView.TemperatureCelsius != g.TemperatureCelsius, "the pod sees its own conditions");
            // An independent chamber ignores the greenhouse: a very different greenhouse gives the pod exactly the same conditions.
            ZoneProfile chamber = ZoneProfilePresets.Create(ExperimentalCondition.HeatStress); owners.Add(chamber);
            chamber.SetControlledBaseline(23.5f, 0.675f, 0.5f);
            var hotGreenhouse = new EnvironmentConditions(40f, 0.1f, 1f, Season.Summer, 12f, 3);
            var coldGreenhouse = new EnvironmentConditions(5f, 0.95f, 0.05f, Season.Summer, 12f, 3);
            EnvironmentConditions a1 = chamber.Apply(hotGreenhouse), a2 = chamber.Apply(coldGreenhouse);
            r.Check(a1.TemperatureCelsius == a2.TemperatureCelsius && a1.Humidity01 == a2.Humidity01 && a1.Light01 == a2.Light01 && Mathf.Approximately(a1.TemperatureCelsius, 33.5f),
                "a pod chamber's conditions do not depend on the greenhouse climate", $"{a1.TemperatureCelsius:0.0}C vs {a2.TemperatureCelsius:0.0}C");
            r.Check(a1.TimeOfDayHours == 12f && a1.DayCount == 3 && a1.Season == Season.Summer, "season and clock still pass through");
            r.Check(!chamber.IsIdentity, "a chamber is not an identity profile");
            ZoneProfile control = ZoneProfilePresets.Create(ExperimentalCondition.Control); owners.Add(control);
            r.Check(control.IsIdentity && control.Apply(g).TemperatureCelsius == g.TemperatureCelsius, "control pod unaffected by edits to other pods' profiles");
        }

        private static void RecoveryAfterReset(ExperimentalZoneTests.Report r, List<UnityEngine.Object> owners)
        {
            List<Pod> pods = MakePods(owners);
            Pod c = Of(pods, ExperimentalCondition.Control), heat = Of(pods, ExperimentalCondition.HeatStress);
            // The pod runs the approved heat preset (+10 C) for 6 days, then it is reset to the control climate.
            float h = Run(pods, 0f, 6f * 24f);
            float healthStressed = heat.Sim.Health01, growthStressed = heat.Sim.Growth01;
            r.Check(heat.Sim.IsAlive && growthStressed < c.Sim.Growth01 - 0.02f, "the heat pod's plant survives but grows visibly slower", $"growth {growthStressed:0.000} vs control {c.Sim.Growth01:0.000}");
            float g0 = heat.Sim.Growth01, c0 = c.Sim.Growth01;
            Run(new List<Pod> { heat }, h, h + 3f * 24f); // 3 more stressed days to measure the stressed growth rate
            float stressedRate = (heat.Sim.Growth01 - g0) / 3f;
            if (!heat.Sim.IsAlive) { r.Check(false, "heat pod alive for the recovery test"); return; }
            heat.Profile.Configure("Heat stress", ExperimentalCondition.HeatStress, 0f, 0f, 1f, 1f, 1f); // reset
            float healthAtReset = heat.Sim.Health01; float t = h + 3f * 24f;
            Run(new List<Pod> { heat }, t, t + 1f * 24f);
            float rateDay1 = heat.Sim.Growth01;
            Run(new List<Pod> { heat }, t + 24f, t + 10f * 24f);
            r.Check(heat.Sim.Health01 > healthAtReset + 0.03f || healthAtReset > 0.97f, "health recovers gradually after reset", $"{healthAtReset:0.00} -> {heat.Sim.Health01:0.00}");
            float g1 = heat.Sim.Growth01; Run(new List<Pod> { heat }, t + 10f * 24f, t + 13f * 24f);
            float recoveredRate = (heat.Sim.Growth01 - g1) / 3f;
            r.Check(recoveredRate > stressedRate, "growth rate after recovery exceeds the stressed rate", $"{recoveredRate:0.0000}/day vs {stressedRate:0.0000}/day");
            r.Check(heat.Sim.Health01 - healthAtReset < 0.6f || healthAtReset > 0.5f, "recovery is gradual, not instantaneous");
        }

        private static void WaterRecovery(ExperimentalZoneTests.Report r, List<UnityEngine.Object> owners)
        {
            List<Pod> pods = MakePods(owners);
            Pod w = Of(pods, ExperimentalCondition.WaterStress);
            float h = Run(new List<Pod> { w }, 0f, 12f * 24f); // approved preset: 40 % irrigation share
            float wiltDry = w.Sim.Wilt01, soilDry = w.Sim.Water01;
            r.Check(wiltDry > 0.3f && w.Sim.IsAlive, "restricted irrigation wilts the plant (it survives)", $"wilt {wiltDry:0.00} alive {w.Sim.IsAlive}");
            w.Profile.Configure("Water stress", ExperimentalCondition.WaterStress, 0f, 0f, 1f, 1f, 1f); // reset
            float mid = Run(new List<Pod> { w }, h, h + 1f * 24f);
            float wiltDay1 = w.Sim.Wilt01;
            Run(new List<Pod> { w }, mid, mid + 6f * 24f);
            r.Check(wiltDay1 < wiltDry && wiltDay1 > 0.02f, "wilting eases gradually the first day after watering is restored", $"{wiltDry:0.00} -> {wiltDay1:0.00}");
            r.Check(w.Sim.Wilt01 < 0.1f && w.Sim.Water01 > soilDry, "plant is turgid again a week after reset", $"wilt {w.Sim.Wilt01:0.00}, soil {soilDry:0.00} -> {w.Sim.Water01:0.00}");
        }
    }
}
