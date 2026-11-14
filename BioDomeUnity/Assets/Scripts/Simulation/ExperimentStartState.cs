using System.Collections.Generic;
using BioDome.Plants;
using UnityEngine;

namespace BioDome.Simulation
{
    /// <summary>
    /// The one initial state shared by every plant in an experiment (all five pods): same species data, growth, health and soil moisture.
    /// Differences afterwards can then only come from each pod's <see cref="BioDome.Environment.ZoneProfile"/>.
    /// </summary>
    public sealed class ExperimentStartState
    {
        public PlantData Data { get; }
        public float Growth01 { get; }
        public float Health01 { get; }
        public float Water01 { get; }

        public ExperimentStartState(PlantData data, float growth01, float health01 = 1f, float water01 = 0.6f)
        {
            Data = data;
            Growth01 = Mathf.Clamp01(growth01);
            Health01 = Mathf.Clamp01(health01);
            Water01 = Mathf.Clamp01(water01);
        }

        /// <summary>Starts from the greenhouse cohort's growth, so experimental plants begin like the normal planting.</summary>
        public static ExperimentStartState FromCohort(PlantData data, PlantingCohort cohort, float health01 = 1f, float water01 = 0.6f)
        {
            return new ExperimentStartState(data, cohort.InitialGrowth01, health01, water01);
        }

        public PlantSimulation CreateSimulation() => new PlantSimulation(Data, Growth01, Health01, Water01);
    }

    /// <summary>Checks that simulations genuinely started from equivalent states.</summary>
    public static class ExperimentEquivalence
    {
        /// <summary>Returns a description of every difference from the first simulation (empty = equivalent).</summary>
        public static List<string> Differences(IReadOnlyList<PlantSimulation> simulations)
        {
            var problems = new List<string>();
            if (simulations == null || simulations.Count < 2) return problems;

            PlantSimulation a = simulations[0];
            for (int i = 1; i < simulations.Count; i++)
            {
                PlantSimulation b = simulations[i];
                if (a.Data != b.Data) problems.Add($"#{i}: different PlantData");
                if (a.Growth01 != b.Growth01) problems.Add($"#{i}: growth {a.Growth01} vs {b.Growth01}");
                if (a.Health01 != b.Health01) problems.Add($"#{i}: health {a.Health01} vs {b.Health01}");
                if (a.Water01 != b.Water01) problems.Add($"#{i}: water {a.Water01} vs {b.Water01}");
                if (a.AgeHours != b.AgeHours) problems.Add($"#{i}: age {a.AgeHours} vs {b.AgeHours}");
                if (a.State != b.State) problems.Add($"#{i}: state {a.State} vs {b.State}");
                if (a.Stage != b.Stage) problems.Add($"#{i}: stage {a.Stage} vs {b.Stage}");
            }
            return problems;
        }
    }
}
