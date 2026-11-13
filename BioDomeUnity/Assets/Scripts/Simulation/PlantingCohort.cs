using UnityEngine;

namespace BioDome.Simulation
{
    /// <summary>
    /// Shared start of a group of plants that are introduced to the greenhouse together.
    /// Every plant in the cohort starts at the same growth and the same simulation time, so differences
    /// between plants later come only from their environment (water, temperature, light, health), never from random ages.
    /// Runtime-only data; the GreenhouseManager creates one at start.
    /// </summary>
    public sealed class PlantingCohort
    {
        /// <summary>Growth, 0 to 1, that every plant in the cohort starts with.</summary>
        public float InitialGrowth01 { get; }

        /// <summary>Simulation time (hours since day 0, 00:00) when the cohort was planted.</summary>
        public float PlantedAtHours { get; }

        public PlantingCohort(float initialGrowth01, float plantedAtHours)
        {
            InitialGrowth01 = Mathf.Clamp01(initialGrowth01);
            PlantedAtHours = plantedAtHours;
        }

        /// <summary>Hours since planting at the given simulation time.</summary>
        public float AgeHours(float nowHours)
        {
            return Mathf.Max(0f, nowHours - PlantedAtHours);
        }

        public float AgeDays(float nowHours)
        {
            return AgeHours(nowHours) / 24f;
        }
    }
}
