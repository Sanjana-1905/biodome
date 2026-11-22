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

        /// <summary>Health response to stress. Original = averaged stress only. SevereStress (v2) = also damages health when a single factor is near its tolerance limit (instantaneous). SevereSustained (v3) = the same, but temperature damage follows the exposure over time.</summary>
        public enum HealthModel { Original = 0, SevereStress = 1, SevereSustained = 2 }

        /// <summary>Health model used by every plant (default SevereStress). Tests switch it to Original to reproduce the earlier baselines.</summary>
        public static HealthModel ActiveHealthModel = HealthModel.SevereSustained;

        /// <summary>Extra health damage per hour (as a fraction of the species' stress damage) at a single factor's maximum stress. Starts only beyond 60 % of tolerance.</summary>
        public const float SevereDamageFraction = 0.3f;
        public const float SevereStressStart = 0.6f;

        /// <summary>Time constants (simulated hours) for wilting: quick to droop, slower to recover.</summary>
        public const float WiltInHours = 6f;

        /// <summary>Time constant (simulated hours) of the thermal exposure average used by the SevereSustained health model.</summary>
        public const float ThermalExposureHours = 24f;
        public const float WiltOutHours = 24f;

        /// <summary>Flower build-up and fade times (simulated hours).</summary>
        public const float BloomBuildHours = 72f;
        public const float BloomFadeHours = 48f;

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

        /// <summary>Visible wilting, 0 (turgid) to 1 (fully drooped). Follows water stress with a delay and recovers slowly after watering.</summary>
        public float Wilt01 { get; private set; }

        /// <summary>Flowering state, 0 (no flowers) to 1 (full bloom). Always 0 for species that do not flower.</summary>
        public float Bloom01 { get; private set; }

        /// <summary>True while the plant is visibly getting better: wilting is falling or health is rising while not yet healthy.</summary>
        public bool IsRecovering { get; private set; }

        /// <summary>Time-averaged temperature stress, 0 to 1 (SevereSustained health model). Rises with sustained heat or cold, falls when conditions return.</summary>
        public float ThermalExposure01 { get; private set; }

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
            Bloom01 = Data.CanFlower && Growth01 >= Data.FloweringMinGrowth ? 1f : 0f; // an established flowering plant starts in bloom
        }

        /// <summary>Advance the simulation by <paramref name="deltaHours"/> simulated hours.</summary>
        public void Tick(float deltaHours, in EnvironmentConditions environment)
        {
            if (deltaHours <= 0f || !IsAlive) return;

            AgeHours += deltaHours;

            ConsumeWater(deltaHours, environment);
            EvaluateStress(environment);
            if (ActiveHealthModel == HealthModel.SevereSustained)
                ThermalExposure01 += (TemperatureStress01 - ThermalExposure01) * (1f - Mathf.Exp(-deltaHours / ThermalExposureHours)); // exponential average: independent of step size
            float healthBefore = Health01;
            float wiltBefore = Wilt01;
            UpdateHealth(deltaHours);
            if (ActiveModel == GrowthModel.Scientific) UpdateAcclimatedLight(deltaHours, environment);
            UpdateGrowth(deltaHours, environment.Season);
            UpdateWilt(deltaHours);
            UpdateBloom(deltaHours);
            IsRecovering = (Wilt01 < wiltBefore - 1e-5f) || (Health01 > healthBefore + 1e-6f && Health01 < Data.HealthyThreshold);

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
            if (ActiveHealthModel != HealthModel.Original)
            {
                // A single climate factor near its tolerance limit (very hot, very cold, very dry air, very dark) damages health even when the average looks mild.
                // Nothing happens until a factor is past 60 % of its tolerance, then damage rises smoothly. Light uses the acclimated value so night is not damage.
                float temperatureForDamage = ActiveHealthModel == HealthModel.SevereSustained ? ThermalExposure01 : TemperatureStress01;
                float worst = Mathf.Max(Mathf.Max(temperatureForDamage, HumidityStress01), ActiveModel == GrowthModel.Scientific ? AcclimatedLightStress01 : LightStress01); // water is excluded: drought already counts double and shows as wilting first
                float severe = Mathf.Clamp01((worst - SevereStressStart) / (1f - SevereStressStart));
                severe = severe * severe * (3f - 2f * severe);
                perHour -= Data.StressDamagePerHour * SevereDamageFraction * severe;
            }
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

        private void UpdateWilt(float deltaHours)
        {
            // Target wilting follows soil dryness (water stress): none while moisture is in the ideal range, full at about 80 % water stress.
            float target = Mathf.Clamp01(WaterStress01 / 0.8f);
            target = target * target * (3f - 2f * target);
            float tau = target > Wilt01 ? WiltInHours : WiltOutHours;
            Wilt01 += (target - Wilt01) * (1f - Mathf.Exp(-deltaHours / tau)); // exponential: independent of step size
        }

        private void UpdateBloom(float deltaHours)
        {
            if (!Data.CanFlower) { Bloom01 = 0f; return; }

            // Flowers need a mature, healthy plant in suitable conditions with enough water; they fade slowly when conditions turn bad.
            bool suitable = Growth01 >= Data.FloweringMinGrowth
                            && Health01 >= Data.HealthyThreshold
                            && WaterStress01 < 0.3f
                            && (ActiveModel == GrowthModel.Scientific ? GrowthFactor01 >= 0.6f : Stress01 < 0.3f);
            float rate = suitable ? 1f / BloomBuildHours : -1f / BloomFadeHours;
            Bloom01 = Mathf.Clamp01(Bloom01 + rate * deltaHours);
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
