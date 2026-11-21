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
        /// <summary>Which growth response is used. Original = the first model (averaged stress), kept so the original golden baseline stays reproducible.</summary>
        public enum GrowthModel { Original = 0, Scientific = 1 }

        /// <summary>Growth model used by every plant. Scientific (the default) is the species-tolerance model; tests switch it to Original to re-run the original baseline.</summary>
        public static GrowthModel ActiveModel = GrowthModel.Scientific;

        /// <summary>Time constant (simulated hours) over which a plant acclimates to light, so the day/night cycle is averaged (roughly a daily light integral).</summary>
        public const float LightAcclimationHours = 24f;

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

        /// <summary>
        /// Environmental growth factor from the most recent tick, 0 (growth stopped) to 1 (all conditions ideal). Scientific model only.
        /// Product over temperature, humidity, light and soil moisture of (1 - stress^2), where each stress is the distance beyond the species'
        /// ideal range divided by its configured tolerance.
        /// </summary>
        public float GrowthFactor01 { get; private set; } = 1f;

        /// <summary>Light the plant has acclimated to (running average of <see cref="EnvironmentConditions.Light01"/>), used by the Scientific growth model.</summary>
        public float AcclimatedLight01 { get; private set; }
        private bool lightInitialised;

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
            if (ActiveModel == GrowthModel.Scientific) UpdateAcclimatedLight(deltaHours, environment);
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

        private float AcclimatedLightStress01;

        /// <summary>Growth response of one factor: 1 inside the ideal range, falling smoothly to 0 at the species' tolerance limit.</summary>
        private static float Response(float stress01) => 1f - stress01 * stress01;

        private void UpdateAcclimatedLight(float deltaHours, in EnvironmentConditions env)
        {
            if (!lightInitialised) { AcclimatedLight01 = 0.5f * (Data.IdealLight01.x + Data.IdealLight01.y); lightInitialised = true; }
            // Exponential average: independent of step size.
            AcclimatedLight01 += (env.Light01 - AcclimatedLight01) * (1f - Mathf.Exp(-deltaHours / LightAcclimationHours));
            AcclimatedLightStress01 = RangeStress(AcclimatedLight01, Data.IdealLight01, Data.LightTolerance);
        }

        private void UpdateGrowth(float deltaHours, Season season)
        {
            if (Growth01 >= 1f) return;

            float baseRatePerHour = 1f / (Data.DaysToMaturity * 24f);
            float rate;
            if (ActiveModel == GrowthModel.Original)
            {
                rate = baseRatePerHour * Data.GetGrowthMultiplier(season) * (1f - Stress01) * Health01;
            }
            else
            {
                // Each limiting factor can stop growth on its own at its configured tolerance, and factors multiply (independent limitations compound).
                float factor = Response(TemperatureStress01) * Response(HumidityStress01) * Response(AcclimatedLightStress01) * Response(WaterStress01);
                GrowthFactor01 = factor;
                rate = baseRatePerHour * Data.GetGrowthMultiplier(season) * factor * Health01;
            }
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
