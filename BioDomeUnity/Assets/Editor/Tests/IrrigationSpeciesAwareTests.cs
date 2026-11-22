using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BioDome.Core;
using BioDome.Irrigation;
using BioDome.Plants;
using BioDome.Simulation;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>
    /// Tests for species-aware irrigation (targets from each species' ideal soil-moisture range), manual/auto/off modes, pod independence and water accounting.
    /// Menu: BioDome > Tests > Run Irrigation Species Tests. Command line: -executeMethod BioDome.EditorTools.Tests.IrrigationSpeciesAwareTests.RunBatch
    /// </summary>
    public static class IrrigationSpeciesAwareTests
    {
        [MenuItem("BioDome/Tests/Run Irrigation Species Tests")]
        public static void RunFromMenu()
        {
            string text = Run(out int failed);
            Debug.Log("IRRIGATIONTESTS " + (failed == 0 ? "PASSED" : "FAILED") + "\n" + text);
        }

        public static void RunBatch()
        {
            int exit;
            try
            {
                string text = Run(out int failed);
                Debug.Log("IRRIGATIONTESTS " + (failed == 0 ? "PASSED" : "FAILED") + "\n" + text);
                exit = failed == 0 ? 0 : 1;
            }
            catch (Exception e) { Debug.LogError("IRRIGATIONTESTS ERROR: " + e); exit = 2; }
            EditorApplication.Exit(exit);
        }

        private static string Run(out int failed)
        {
            var owners = new List<UnityEngine.Object>();
            try { return RunChecks(owners, out failed); }
            finally { foreach (UnityEngine.Object o in owners) if (o != null) UnityEngine.Object.DestroyImmediate(o); }
        }

        private static string RunChecks(List<UnityEngine.Object> owners, out int failed)
        {
            var sb = new StringBuilder();
            int passed = 0, fail = 0;
            void Check(bool ok, string name, string detail = "")
            {
                if (ok) { passed++; sb.AppendLine("  pass  " + name); }
                else { fail++; sb.AppendLine("  FAIL  " + name + (detail.Length > 0 ? "  -> " + detail : "")); }
            }

            var species = new Dictionary<string, PlantData>();
            foreach (string guid in AssetDatabase.FindAssets("t:PlantData", new[] { "Assets/ScriptableObjects/Plants" }))
            { var d = AssetDatabase.LoadAssetAtPath<PlantData>(AssetDatabase.GUIDToAssetPath(guid)); species[d.SpeciesId] = d; }
            PlantData monstera = species["monstera"], orchid = species["orchid"], moss = species["moss"];

            // 1. A dry-soil Monstera receives water, up to its own stop level and no further.
            {
                var irr = NewIrrigation(owners, IrrigationController.Mode.Auto, 500f, 20f, speciesAware: true);
                var p = NewPlant(monstera, owners, 0.10f); irr.RegisterPlant(p);
                float start = irr.StartLevelFor(p), stop = irr.StopLevelFor(p);
                for (int i = 0; i < 400; i++) irr.Tick(0.1f);
                Check(p.Simulation.Water01 > start && p.Simulation.Water01 <= stop + 0.005f && irr.TotalDeliveredLitres > 0f,
                    "dry-soil Monstera receives water and stops at its own target", $"water {p.Simulation.Water01:0.000}, target {stop:0.000}");
            }

            // 2. An Orchid (ideal soil 0.2-0.5) is never watered beyond its configured target, even with a huge flow.
            {
                var irr = NewIrrigation(owners, IrrigationController.Mode.Auto, 5000f, 500f, speciesAware: true);
                var p = NewPlant(orchid, owners, 0.10f); irr.RegisterPlant(p);
                float stop = irr.StopLevelFor(p), max = 0f;
                for (int i = 0; i < 600; i++) { irr.Tick(0.1f); max = Mathf.Max(max, p.Simulation.Water01); }
                Check(max <= stop + 0.005f && max >= irr.StartLevelFor(p), "Orchid is watered up to, but never beyond, its configured target", $"max {max:0.000}, target {stop:0.000}, ideal max {orchid.IdealSoilMoisture01.y}");
                Check(irr.MaxOvershoot < 0.005f, "no overshoot beyond the stop level is recorded", $"{irr.MaxOvershoot:0.0000}");
            }

            // 3. Moss in a realistic loop (daily consumption + irrigation) is never saturated and stays near its ideal range.
            {
                var irr = NewIrrigation(owners, IrrigationController.Mode.Auto, 5000f, 6f, speciesAware: true);
                var p = NewPlant(moss, owners, 0.6f); irr.RegisterPlant(p);
                var env = Env(moss);
                float max = 0f, sum = 0f; int saturated = 0, n = 0;
                for (int i = 0; i < 24 * 20 * 10; i++)
                {
                    p.Simulation.Tick(0.1f, env); irr.Tick(0.1f);
                    float w = p.Simulation.Water01; max = Mathf.Max(max, w); sum += w; n++; if (w >= 0.95f) saturated++;
                }
                float mean = sum / n;
                Check(saturated == 0 && max <= irr.StopLevelFor(p) + 0.02f && mean >= moss.IdealSoilMoisture01.x - 0.05f && mean <= moss.IdealSoilMoisture01.y,
                    "moss is not continuously saturated and stays within its ideal moisture range over 20 days", $"max {max:0.00}, mean {mean:0.00}, saturated steps {saturated}");
                Check(p.Simulation.IsAlive && p.Simulation.WaterStress01 < 0.05f, "moss stays alive with no water stress", $"stress {p.Simulation.WaterStress01:0.00}");
                Check(irr.WateringStarts > 0 && irr.WateringStarts < 150, "irrigation does not chatter (a bounded number of watering cycles in 20 days)", $"{irr.WateringStarts} starts");
            }

            // 4. Plants already inside their ideal range are not watered.
            {
                var irr = NewIrrigation(owners, IrrigationController.Mode.Auto, 500f, 50f, speciesAware: true);
                var p = NewPlant(orchid, owners, 0.40f); irr.RegisterPlant(p);   // ideal 0.2-0.5, start level 0.275
                var q = NewPlant(monstera, owners, 0.60f); irr.RegisterPlant(q); // ideal 0.35-0.75, start level 0.45
                float reservoir = irr.WaterLitres;
                for (int i = 0; i < 300; i++) irr.Tick(0.1f);
                Check(irr.WaterLitres == reservoir && irr.TotalDeliveredLitres == 0f && !irr.IsRunning, "plants already within their ideal moisture range are not watered", $"delivered {irr.TotalDeliveredLitres}");
            }

            // 5. Different species in one controller have different requirements.
            {
                var irr = NewIrrigation(owners, IrrigationController.Mode.Auto, 500f, 20f, speciesAware: true);
                var m = NewPlant(monstera, owners, 0.30f); var o = NewPlant(orchid, owners, 0.30f);
                irr.RegisterPlant(m); irr.RegisterPlant(o);
                bool differentTargets = Mathf.Abs(irr.StopLevelFor(m) - irr.StopLevelFor(o)) > 0.1f;
                for (int i = 0; i < 300; i++) irr.Tick(0.1f);
                Check(differentTargets && m.Simulation.Water01 > 0.55f && Mathf.Approximately(o.Simulation.Water01, 0.30f),
                    "in one controller the thirsty Monstera is watered while the Orchid (already at 0.30, above its start level) is left alone",
                    $"monstera {m.Simulation.Water01:0.00}, orchid {o.Simulation.Water01:0.00}");
            }

            // 6. Experimental pod watering stays independent: the pod controller keeps the original behaviour and its own reservoir.
            {
                var main = NewIrrigation(owners, IrrigationController.Mode.Auto, 500f, 50f, speciesAware: true);
                var pod = NewIrrigation(owners, IrrigationController.Mode.Auto, 500f, 50f, speciesAware: false);
                var display = NewPlant(monstera, owners, 0.6f); main.RegisterPlant(display);
                var podPlant = NewPlant(monstera, owners, 0.10f); podPlant.SetWaterShareWeight(0.25f); podPlant.Simulation.WaterIntakeFactor = 0.4f; pod.RegisterPlant(podPlant);
                float mainBefore = main.WaterLitres;
                var env = Env(monstera);
                for (int i = 0; i < 24 * 10 * 10; i++) { display.Simulation.Tick(0.1f, env); podPlant.Simulation.Tick(0.1f, env); main.Tick(0.1f); pod.Tick(0.1f); }
                Check(!pod.SpeciesAware && main.SpeciesAware, "pod controller keeps the original behaviour while the greenhouse controller is species-aware");
                Check(pod.TotalDeliveredLitres > 0f && main.TotalDeliveredLitres >= 0f && podPlant.Simulation.Water01 < display.Simulation.Water01,
                    "the water-stress pod plant stays drier than the greenhouse plant while automatic irrigation is active elsewhere", $"pod {podPlant.Simulation.Water01:0.00} vs display {display.Simulation.Water01:0.00}");
                Check(mainBefore - main.WaterLitres <= main.TotalDeliveredLitres + 1e-3f, "the pod's watering never draws from the greenhouse reservoir", "");
            }

            // 7. Modes: Off waters nothing, Manual waters everything (ignoring species targets), Auto (species-aware) respects targets.
            {
                var off = NewIrrigation(owners, IrrigationController.Mode.Off, 500f, 50f, speciesAware: true);
                var manual = NewIrrigation(owners, IrrigationController.Mode.Manual, 500f, 50f, speciesAware: true);
                var auto = NewIrrigation(owners, IrrigationController.Mode.Auto, 500f, 50f, speciesAware: true);
                var a = NewPlant(orchid, owners, 0.45f); var b = NewPlant(orchid, owners, 0.45f); var c = NewPlant(orchid, owners, 0.45f);
                off.RegisterPlant(a); manual.RegisterPlant(b); auto.RegisterPlant(c);
                for (int i = 0; i < 100; i++) { off.Tick(0.1f); manual.Tick(0.1f); auto.Tick(0.1f); }
                Check(a.Simulation.Water01 == 0.45f && off.TotalDeliveredLitres == 0f, "Off mode delivers no water");
                Check(b.Simulation.Water01 > 0.45f && manual.TotalDeliveredLitres > 0f, "Manual mode waters regardless of the species target", $"{b.Simulation.Water01:0.00}");
                Check(c.Simulation.Water01 == 0.45f && auto.TotalDeliveredLitres == 0f, "Auto mode leaves a plant inside its target range alone");
            }

            // 8. Water accounting: litres leaving the reservoir equal litres gained by plants (no consumption, no overflow).
            {
                var irr = NewIrrigation(owners, IrrigationController.Mode.Auto, 500f, 5f, speciesAware: true);
                var plants = new[] { NewPlant(monstera, owners, 0.1f), NewPlant(orchid, owners, 0.05f), NewPlant(moss, owners, 0.2f) };
                foreach (var p in plants) irr.RegisterPlant(p);
                float before = plants.Sum(p => p.Simulation.Water01 * p.Data.WaterCapacityLitres);
                float reservoir = irr.WaterLitres;
                for (int i = 0; i < 1500; i++) irr.Tick(0.1f);
                float gained = plants.Sum(p => p.Simulation.Water01 * p.Data.WaterCapacityLitres) - before;
                float taken = reservoir - irr.WaterLitres;
                Check(Mathf.Abs(gained - taken) < 0.01f && Mathf.Abs(taken - irr.TotalDeliveredLitres) < 0.01f && gained > 0.5f, "reservoir litres taken equal litres the plants gained", $"gained {gained:0.000}, taken {taken:0.000}, counter {irr.TotalDeliveredLitres:0.000}");
            }

            // 9. Original behaviour is unchanged when species-aware is off (default).
            {
                var irr = NewIrrigation(owners, IrrigationController.Mode.Auto, 5000f, 50f, speciesAware: false);
                var p = NewPlant(orchid, owners, 0.10f); irr.RegisterPlant(p);
                for (int i = 0; i < 600; i++) irr.Tick(0.1f);
                Check(p.Simulation.Water01 > 0.9f, "with species-aware off, the original controller still waters toward saturation (original behaviour preserved)", $"{p.Simulation.Water01:0.00}");
            }

            // 10. The spray follows ACTUAL delivery, not the controller's on/off state.
            {
                // (a) On, but only a trickle is delivered (plant consumes steadily, flow is large): running, yet no spray.
                var trickle = NewIrrigation(owners, IrrigationController.Mode.Auto, 5000f, 20f, speciesAware: true);
                var p = NewPlant(monstera, owners, 0.5f); trickle.RegisterPlant(p);
                var env = Env(monstera); bool everSprayed = false; bool wasRunning = false;
                for (int i = 0; i < 24 * 10 * 10; i++) { p.Simulation.Tick(0.1f, env); trickle.Tick(0.1f); wasRunning |= trickle.IsRunning; everSprayed |= trickle.IsSpraying; }
                Check(wasRunning && !everSprayed, "controller 'on' with only a trickle delivered does not spray", $"running {wasRunning}, sprayed {everSprayed}, share {trickle.DeliveryFraction01:0.00}");

                // (b) Heavy delivery to many dry plants: sprays, then stops once they reach their targets.
                var heavy = NewIrrigation(owners, IrrigationController.Mode.Auto, 5000f, 5f, speciesAware: true);
                var dry = Enumerable.Range(0, 20).Select(_ => NewPlant(monstera, owners, 0.05f)).ToList();
                foreach (var d in dry) heavy.RegisterPlant(d);
                bool sprayedWhileDry = false;
                for (int i = 0; i < 20; i++) { heavy.Tick(0.1f); sprayedWhileDry |= heavy.IsSpraying; }
                for (int i = 0; i < 4000; i++) heavy.Tick(0.1f);
                Check(sprayedWhileDry && !heavy.IsSpraying && !heavy.IsRunning, "heavy delivery to dry plants sprays, and the spray stops when they reach their targets", $"sprayed {sprayedWhileDry}, now {heavy.IsSpraying}");

                // (c) Manual: sprays while it really waters, not when every plant is already full.
                var manualFull = NewIrrigation(owners, IrrigationController.Mode.Manual, 5000f, 5f, speciesAware: true);
                var fullPlant = NewPlant(monstera, owners, 1f); manualFull.RegisterPlant(fullPlant);
                for (int i = 0; i < 100; i++) manualFull.Tick(0.1f);
                var manualDry = NewIrrigation(owners, IrrigationController.Mode.Manual, 5000f, 5f, speciesAware: true);
                foreach (var d in Enumerable.Range(0, 10).Select(_ => NewPlant(monstera, owners, 0.1f))) manualDry.RegisterPlant(d);
                for (int i = 0; i < 20; i++) manualDry.Tick(0.1f);
                Check(manualFull.IsRunning && !manualFull.IsSpraying, "Manual mode with every plant already full is on but does not spray");
                Check(manualDry.IsRunning && manualDry.IsSpraying, "Manual mode delivering to thirsty plants sprays");

                // (d) Off: never sprays, even with dry plants and a spray that was active.
                var toggled = NewIrrigation(owners, IrrigationController.Mode.Manual, 5000f, 5f, speciesAware: true);
                foreach (var d in Enumerable.Range(0, 10).Select(_ => NewPlant(monstera, owners, 0.1f))) toggled.RegisterPlant(d);
                for (int i = 0; i < 20; i++) toggled.Tick(0.1f);
                bool before = toggled.IsSpraying;
                toggled.SetMode(IrrigationController.Mode.Off);
                for (int i = 0; i < 20; i++) toggled.Tick(0.1f);
                Check(before && !toggled.IsSpraying && toggled.DeliveryFraction01 == 0f, "switching to Off stops the spray at once", $"before {before}");

                // (e) Controllers are independent (pod controllers included).
                var podLike = NewIrrigation(owners, IrrigationController.Mode.Auto, 5000f, 5f, speciesAware: false);
                var quiet = NewIrrigation(owners, IrrigationController.Mode.Auto, 5000f, 5f, speciesAware: true);
                foreach (var d in Enumerable.Range(0, 10).Select(_ => NewPlant(monstera, owners, 0.1f))) podLike.RegisterPlant(d);
                var okPlant = NewPlant(monstera, owners, 0.6f); quiet.RegisterPlant(okPlant);
                for (int i = 0; i < 20; i++) { podLike.Tick(0.1f); quiet.Tick(0.1f); }
                Check(podLike.IsSpraying && !quiet.IsSpraying, "one controller spraying does not make another spray (pod controllers are independent)");

                // (f) Effects hook: a spray that is wired in stops when the controller stops spraying (no errors when none is wired).
                var go = new GameObject("TestSpray") { hideFlags = HideFlags.HideAndDontSave }; owners.Add(go);
                var ps = go.AddComponent<ParticleSystem>();
                var wired = NewIrrigation(owners, IrrigationController.Mode.Manual, 5000f, 5f, speciesAware: true);
                new SerializedObject(wired).FindProperty("sprayEffects").arraySize = 0;
                var so = new SerializedObject(wired); var arr = so.FindProperty("sprayEffects"); arr.arraySize = 1; arr.GetArrayElementAtIndex(0).objectReferenceValue = ps; so.ApplyModifiedPropertiesWithoutUndo();
                foreach (var d in Enumerable.Range(0, 10).Select(_ => NewPlant(monstera, owners, 0.1f))) wired.RegisterPlant(d);
                for (int i = 0; i < 20; i++) wired.Tick(0.1f);
                bool played = wired.IsSpraying;
                wired.SetMode(IrrigationController.Mode.Off); wired.Tick(0.1f);
                Check(played && !wired.IsSpraying, "a wired spray effect follows delivery on and off without errors");
            }

            failed = fail;
            sb.Insert(0, $"{passed} passed, {fail} failed\n");
            return sb.ToString();
        }

        private static EnvironmentConditions Env(PlantData d) =>
            new EnvironmentConditions((d.IdealTemperatureCelsius.x + d.IdealTemperatureCelsius.y) * 0.5f, (d.IdealHumidity01.x + d.IdealHumidity01.y) * 0.5f, (d.IdealLight01.x + d.IdealLight01.y) * 0.5f, Season.Spring, 12f, 0);

        private static PlantController NewPlant(PlantData data, List<UnityEngine.Object> owners, float water01)
        {
            var go = new GameObject("IrrigationTestPlant") { hideFlags = HideFlags.HideAndDontSave };
            owners.Add(go);
            var plant = go.AddComponent<PlantController>();
            plant.Initialize(data);
            typeof(PlantSimulation).GetProperty("Water01").GetSetMethod(true).Invoke(plant.Simulation, new object[] { water01 });
            return plant;
        }

        private static IrrigationController NewIrrigation(List<UnityEngine.Object> owners, IrrigationController.Mode mode, float capacity, float flow, bool speciesAware)
        {
            var go = new GameObject("IrrigationTestController") { hideFlags = HideFlags.HideAndDontSave };
            owners.Add(go);
            var irr = go.AddComponent<IrrigationController>();
            var so = new SerializedObject(irr);
            so.FindProperty("reservoirCapacityLitres").floatValue = capacity;
            so.FindProperty("startingWaterLitres").floatValue = capacity;
            so.FindProperty("mainsSupplyLitresPerHour").floatValue = 0f;
            so.FindProperty("flowRateLitresPerHour").floatValue = flow;
            so.FindProperty("mode").enumValueIndex = (int)mode;
            so.FindProperty("speciesAware").boolValue = speciesAware;
            so.ApplyModifiedPropertiesWithoutUndo();
            typeof(IrrigationController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(irr, null);
            return irr;
        }
    }
}
