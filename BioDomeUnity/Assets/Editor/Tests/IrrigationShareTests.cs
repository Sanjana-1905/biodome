using System;
using System.Collections.Generic;
using System.Reflection;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Irrigation;
using BioDome.Plants;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>Tests for the per-plant water-share weight and pod irrigation. (The golden baseline separately proves weight-1 behaviour is unchanged.)</summary>
    internal static class IrrigationShareTests
    {
        public static void Run(Action<bool, string, string> check, PlantData data, Dictionary<ExperimentalCondition, ZoneProfile> profiles)
        {
            var owners = new List<UnityEngine.Object>();
            try
            {
                // 1. A weight scales one plant's share without affecting the others.
                IrrigationController irr = NewIrrigation(owners, IrrigationController.Mode.Manual, 1000f, 1f);
                PlantController full = NewPlant(data, owners, 1f), half = NewPlant(data, owners, 0.5f), none = NewPlant(data, owners, 0f);
                foreach (PlantController p in new[] { full, half, none }) { p.Plant(new BioDome.Simulation.PlantingCohort(0.12f, 0f)); irr.RegisterPlant(p); }
                float f0 = full.Simulation.Water01, h0 = half.Simulation.Water01, n0 = none.Simulation.Water01;
                irr.Tick(1f); // 1 L/h flow for one hour
                float fGain = (full.Simulation.Water01 - f0) * data.WaterCapacityLitres;
                float hGain = (half.Simulation.Water01 - h0) * data.WaterCapacityLitres;
                check(Mathf.Approximately(fGain, 0.5f) && Mathf.Approximately(hGain, 0.25f), "Weight 1 gets the equal share (1 L / 2 thirsty), weight 0.5 gets half of it", $"{fGain} / {hGain}");
                check(none.Simulation.Water01 == n0, "Weight 0 plant receives no water", "");

                // 1b. Lowering one plant's weight leaves every other plant's share exactly as it was.
                IrrigationController irr1b = NewIrrigation(owners, IrrigationController.Mode.Manual, 1000f, 1f);
                PlantController x = NewPlant(data, owners, 1f), y = NewPlant(data, owners, 1f), z = NewPlant(data, owners, 0.25f);
                foreach (PlantController p in new[] { x, y, z }) { p.Plant(new BioDome.Simulation.PlantingCohort(0.12f, 0f)); irr1b.RegisterPlant(p); }
                irr1b.Tick(0.9f);
                check(x.Simulation.Water01 == y.Simulation.Water01 && Mathf.Approximately((x.Simulation.Water01 - 0.6f) * data.WaterCapacityLitres, 0.3f) && Mathf.Approximately((z.Simulation.Water01 - 0.6f) * data.WaterCapacityLitres, 0.075f),
                    "Weight 0.25 plant gets a quarter share; neighbours keep the full equal share", $"{x.Simulation.Water01} {z.Simulation.Water01}");

                // 2. Equal weights == plain equal split.
                IrrigationController irr2 = NewIrrigation(owners, IrrigationController.Mode.Manual, 1000f, 1f);
                PlantController a = NewPlant(data, owners, 1f), b = NewPlant(data, owners, 1f), c = NewPlant(data, owners, 1f);
                foreach (PlantController p in new[] { a, b, c }) { p.Plant(new BioDome.Simulation.PlantingCohort(0.12f, 0f)); irr2.RegisterPlant(p); }
                irr2.Tick(0.7f);
                float expected = 0.6f + (1f * 0.7f / 3f) / data.WaterCapacityLitres;
                check(Mathf.Abs(a.Simulation.Water01 - expected) < 1e-6f && a.Simulation.Water01 == b.Simulation.Water01 && b.Simulation.Water01 == c.Simulation.Water01, "Equal weights give the exact equal split (budget / count)", $"{a.Simulation.Water01} vs {expected}");

                // 3. A pod irrigation controller leaves the main one (and display plants) untouched.
                IrrigationController main = NewIrrigation(owners, IrrigationController.Mode.Auto, 500f, 150f);
                IrrigationController pod = NewIrrigation(owners, IrrigationController.Mode.Auto, 500f, 150f);
                PlantController display = NewPlant(data, owners, 1f);
                PlantController podPlant = NewPlant(data, owners, 1f);
                podPlant.ConfigureExperiment(new BioDome.Simulation.ExperimentStartState(data, 0.12f, 1f, 0.1f), profiles[ExperimentalCondition.WaterStress]);
                display.Plant(new BioDome.Simulation.PlantingCohort(0.12f, 0f));
                main.RegisterPlant(display); pod.RegisterPlant(podPlant);
                float reservoirBefore = main.WaterLitres;
                for (int i = 0; i < 200; i++) pod.Tick(0.1f); // very dry pod plant triggers only the pod's sprinklers
                check(main.WaterLitres == reservoirBefore && display.Simulation.Water01 == 0.6f, "Pod irrigation does not touch the main reservoir or display plants", "");
                check(podPlant.UsesOwnIrrigation && podPlant.WaterShareWeight == ZoneProfilePresets.WaterShare && pod.WaterLitres < 500f, "Pod plant is watered by the pod controller with the water-stress weight", "");
            }
            finally { foreach (UnityEngine.Object o in owners) if (o != null) UnityEngine.Object.DestroyImmediate(o); }
        }

        private static PlantController NewPlant(PlantData data, List<UnityEngine.Object> owners, float weight)
        {
            var go = new GameObject("ShareTestPlant") { hideFlags = HideFlags.HideAndDontSave };
            owners.Add(go);
            var plant = go.AddComponent<PlantController>();
            plant.Initialize(data);
            plant.SetWaterShareWeight(weight);
            return plant;
        }

        private static IrrigationController NewIrrigation(List<UnityEngine.Object> owners, IrrigationController.Mode mode, float capacity, float flow)
        {
            var go = new GameObject("ShareTestIrrigation") { hideFlags = HideFlags.HideAndDontSave };
            owners.Add(go);
            var irr = go.AddComponent<IrrigationController>();
            var so = new SerializedObject(irr);
            so.FindProperty("reservoirCapacityLitres").floatValue = capacity;
            so.FindProperty("startingWaterLitres").floatValue = capacity;
            so.FindProperty("mainsSupplyLitresPerHour").floatValue = 0f;
            so.FindProperty("flowRateLitresPerHour").floatValue = flow;
            so.FindProperty("mode").enumValueIndex = (int)mode;
            so.ApplyModifiedPropertiesWithoutUndo();
            typeof(IrrigationController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(irr, null);
            return irr;
        }
    }
}
