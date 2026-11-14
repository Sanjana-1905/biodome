using System;
using BioDome.Core;
using BioDome.Plants;
using UnityEngine;

namespace BioDome.Simulation
{
    /// <summary>
    /// Pure plant simulation: growth, water, stress and health.
    /// Plain C# class with no MonoBehaviour dependency and no rendering code, so it can be
    /// unit tested and so presentation (PlantController) stays completely separate.
    /// All time is expressed in simulated hours.
    /// </summary>
    public sealed class PlantSimulation
    {
        private const float WaterStressWeight = 2f; // water counts double in the overall stress average
        private const float WaterDrainTemperatureReference = 22f;

        public PlantData Data { get; }

        /// <summary>Growth progress, 0 (seed) to 1 (mature).</summary>
        public float Growth01 { get; private set; }

        /// <summary>Continuous health value, 0 (dead) to 1 (perfect).</summary>
        public float Health01 { get; private set; }

        /// <summary>Soil moisture, 0 (dry) to 1 (saturated).</summary>
        public float Water01 { get; private set; }

        public float AgeHours { get; private set; }
        public PlantHealthState State { get; private set; }

        /// <summary>Combined stress from the most recent tick, 0 (none) to 1 (maximum).</summary>
        public float Stress01 { get; private set; }

        // Per-factor stress from the most recent tick, useful for UI and debugging.
        public float TemperatureStress01 { get; private set; }
        public float HumidityStress01 { get; private set; }
        public float LightStress01 { get; private set; }
        public float WaterStress01 { get; private set; }

        /// <summary>
        /// Fraction of delivered water the soil takes up (1 = all, the default and the normal greenhouse behaviour).
        /// Experimental water-stress zones lower it; nothing else changes.
        /// </summary>
        public float WaterIntakeFactor { get; set; } = 1f;

        public bool IsAlive => State != PlantHealthState.Dead;

        /// <summary>Development stage for the current growth, using the species thresholds.</summary>
        public PlantGrowthStage Stage => Data.GetStage(Growth01);

        public PlantSimulation(PlantData data, float initialGrowth01 = 0f, float initialHealth01 = 1f, float initialWater01 = 0.6f)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            Data = data;
            Growth01 = Mathf.Clamp01(initialGrowth01);
            Health01 = Mathf.Clamp01(initialHealth01);
            Water01 = Mathf.Clamp01(initialWater01);
            State = EvaluateState(Health01);
        }

        /// <summary>Advance the simulation by <paramref name="deltaHours"/> simulated hours.</summary>
        public void Tick(float deltaHours, in EnvironmentConditions environment)
        {
            if (deltaHours <= 0f || !IsAlive) return;

            AgeHours += deltaHours;

            ConsumeWater(deltaHours, environment);
            EvaluateStress(environment);
            UpdateHealth(deltaHours);
            UpdateGrowth(deltaHours, environment.Season);

            State = EvaluateState(Health01);
        }

        /// <summary>Adds water to the soil. Returns the litres actually accepted (the rest overflows).</summary>
        public float AddWater(float litres)
        {
            if (litres <= 0f || !IsAlive) return 0f;
            if (WaterIntakeFactor != 1f) litres *= Mathf.Clamp01(WaterIntakeFactor);

            float capacity = Data.WaterCapacityLitres;
            float currentLitres = Water01 * capacity;
            float accepted = Mathf.Min(litres, capacity - currentLitres);
            Water01 = Mathf.Clamp01((currentLitres + accepted) / capacity);
            return accepted;
        }

        /// <summary>Litres of water still needed to fill the soil completely.</summary>
        public float WaterDeficitLitres => (1f - Water01) * Data.WaterCapacityLitres;

        /// <summary>Restores a dead or damaged plant to full health.</summary>
        public void Revive(float health01 = 1f)
        {
            Health01 = Mathf.Clamp01(health01);
            State = EvaluateState(Health01);
        }

        // --- Internals -----------------------------------------------------------

        private void ConsumeWater(float deltaHours, in EnvironmentConditions env)
        {
            // Warmer, brighter, drier air makes the plant use more water.
            float temperatureFactor = Mathf.Clamp(1f + (env.TemperatureCelsius - WaterDrainTemperatureReference) * 0.04f, 0.3f, 2.5f);
            float lightFactor = Mathf.Lerp(0.5f, 1.5f, env.Light01);
            float dryAirFactor = Mathf.Lerp(1.4f, 0.7f, env.Humidity01);

            float litresUsed = Data.WaterUseLitresPerHour * temperatureFactor * lightFactor * dryAirFactor * deltaHours;
            Water01 = Mathf.Clamp01(Water01 - litresUsed / Data.WaterCapacityLitres);
        }

        private void EvaluateStress(in EnvironmentConditions env)
        {
            TemperatureStress01 = RangeStress(env.TemperatureCelsius, Data.IdealTemperatureCelsius, Data.TemperatureTolerance);
            HumidityStress01 = RangeStress(env.Humidity01, Data.IdealHumidity01, Data.HumidityTolerance);
            LightStress01 = RangeStress(env.Light01, Data.IdealLight01, Data.LightTolerance);

            // Soil moisture has a hard 0-1 range, so its tolerance is the distance to that boundary.
            Vector2 moisture = Data.IdealSoilMoisture01;
            if (Water01 < moisture.x)
                WaterStress01 = Mathf.Clamp01((moisture.x - Water01) / Mathf.Max(moisture.x, 0.01f));
            else if (Water01 > moisture.y)
                WaterStress01 = Mathf.Clamp01((Water01 - moisture.y) / Mathf.Max(1f - moisture.y, 0.01f));
            else
                WaterStress01 = 0f;

            float weighted = TemperatureStress01 + HumidityStress01 + LightStress01 + WaterStress01 * WaterStressWeight;
            Stress01 = Mathf.Clamp01(weighted / (3f + WaterStressWeight));
        }

        private void UpdateHealth(float deltaHours)
        {
            float perHour = Data.HealthRecoveryPerHour * (1f - Stress01) - Data.StressDamagePerHour * Stress01;
            Health01 = Mathf.Clamp01(Health01 + perHour * deltaHours);
        }

        private void UpdateGrowth(float deltaHours, Season season)
        {
            if (Growth01 >= 1f) return;

            float baseRatePerHour = 1f / (Data.DaysToMaturity * 24f);
            float rate = baseRatePerHour * Data.GetGrowthMultiplier(season) * (1f - Stress01) * Health01;
            Growth01 = Mathf.Clamp01(Growth01 + rate * deltaHours);
        }

        private PlantHealthState EvaluateState(float health01)
        {
            if (health01 <= 0f) return PlantHealthState.Dead;
            if (health01 >= Data.HealthyThreshold) return PlantHealthState.Healthy;
            if (health01 >= Data.StressedThreshold) return PlantHealthState.Stressed;
            return PlantHealthState.Critical;
        }

        /// <summary>0 inside the ideal range, rising linearly to 1 at <paramref name="tolerance"/> beyond it.</summary>
        private static float RangeStress(float value, Vector2 idealRange, float tolerance)
        {
            if (value < idealRange.x) return Mathf.Clamp01((idealRange.x - value) / tolerance);
            if (value > idealRange.y) return Mathf.Clamp01((value - idealRange.y) / tolerance);
            return 0f;
        }
    }
}
