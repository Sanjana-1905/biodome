using BioDome.Core;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Configuration for one plant species. Create assets via
    /// Assets > Create > BioDome > Plant Data. Holds data only; no runtime state.
    /// </summary>
    [CreateAssetMenu(fileName = "NewPlantData", menuName = "BioDome/Plant Data", order = 0)]
    public class PlantData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string displayName = "New Plant";
        [SerializeField] private string scientificName = "";
        [SerializeField, TextArea(2, 5)] private string description = "";

        [Header("Preferred Conditions (ideal range)")]
        [SerializeField] private Vector2 idealTemperatureCelsius = new Vector2(18f, 28f);
        [SerializeField] private Vector2 idealHumidity01 = new Vector2(0.4f, 0.8f);
        [SerializeField] private Vector2 idealLight01 = new Vector2(0.4f, 0.9f);
        [SerializeField, Tooltip("How far outside the ideal temperature range (in C) the plant is under maximum stress.")]
        private float temperatureTolerance = 10f;
        [SerializeField, Tooltip("How far outside the ideal humidity range (0-1) the plant is under maximum stress.")]
        private float humidityTolerance = 0.4f;
        [SerializeField, Tooltip("How far outside the ideal light range (0-1) the plant is under maximum stress.")]
        private float lightTolerance = 0.5f;

        [Header("Water")]
        [SerializeField, Tooltip("Litres of water the plant's soil can hold.")]
        private float waterCapacityLitres = 5f;
        [SerializeField, Tooltip("Litres consumed per simulated hour under neutral conditions.")]
        private float waterUseLitresPerHour = 0.05f;
        [SerializeField, Tooltip("Soil moisture (0-1) range the plant is happiest in. Outside it, water stress builds.")]
        private Vector2 idealSoilMoisture01 = new Vector2(0.4f, 0.8f);

        [Header("Growth")]
        [SerializeField, Tooltip("Simulated days to reach full size under ideal conditions.")]
        private float daysToMaturity = 20f;
        [SerializeField] private float springGrowthMultiplier = 1.2f;
        [SerializeField] private float summerGrowthMultiplier = 1.0f;
        [SerializeField] private float autumnGrowthMultiplier = 0.6f;
        [SerializeField] private float winterGrowthMultiplier = 0.2f;

        [Header("Health")]
        [SerializeField, Tooltip("Health gained per simulated hour with zero stress.")]
        private float healthRecoveryPerHour = 0.02f;
        [SerializeField, Tooltip("Health lost per simulated hour at maximum stress.")]
        private float stressDamagePerHour = 0.05f;
        [SerializeField, Range(0f, 1f), Tooltip("Health at or above this value counts as Healthy.")]
        private float healthyThreshold = 0.7f;
        [SerializeField, Range(0f, 1f), Tooltip("Health at or above this value (but below Healthy) counts as Stressed. Below it is Critical.")]
        private float stressedThreshold = 0.35f;

        [Header("Visuals")]
        [SerializeField, Tooltip("Plant size multiplier applied to the visual root at full growth.")]
        private float maxScale = 1f;

        // --- Read-only accessors -------------------------------------------------

        public string DisplayName => displayName;
        public string ScientificName => scientificName;
        public string Description => description;

        public Vector2 IdealTemperatureCelsius => idealTemperatureCelsius;
        public Vector2 IdealHumidity01 => idealHumidity01;
        public Vector2 IdealLight01 => idealLight01;
        public float TemperatureTolerance => temperatureTolerance;
        public float HumidityTolerance => humidityTolerance;
        public float LightTolerance => lightTolerance;

        public float WaterCapacityLitres => waterCapacityLitres;
        public float WaterUseLitresPerHour => waterUseLitresPerHour;
        public Vector2 IdealSoilMoisture01 => idealSoilMoisture01;

        public float DaysToMaturity => daysToMaturity;
        public float HealthRecoveryPerHour => healthRecoveryPerHour;
        public float StressDamagePerHour => stressDamagePerHour;
        public float HealthyThreshold => healthyThreshold;
        public float StressedThreshold => stressedThreshold;
        public float MaxScale => maxScale;

        public float GetGrowthMultiplier(Season season)
        {
            switch (season)
            {
                case Season.Spring: return springGrowthMultiplier;
                case Season.Summer: return summerGrowthMultiplier;
                case Season.Autumn: return autumnGrowthMultiplier;
                case Season.Winter: return winterGrowthMultiplier;
                default: return 1f;
            }
        }

        private void OnValidate()
        {
            idealTemperatureCelsius = OrderRange(idealTemperatureCelsius);
            idealHumidity01 = ClampRange01(OrderRange(idealHumidity01));
            idealLight01 = ClampRange01(OrderRange(idealLight01));
            idealSoilMoisture01 = ClampRange01(OrderRange(idealSoilMoisture01));

            temperatureTolerance = Mathf.Max(0.01f, temperatureTolerance);
            humidityTolerance = Mathf.Max(0.01f, humidityTolerance);
            lightTolerance = Mathf.Max(0.01f, lightTolerance);
            waterCapacityLitres = Mathf.Max(0.01f, waterCapacityLitres);
            waterUseLitresPerHour = Mathf.Max(0f, waterUseLitresPerHour);
            daysToMaturity = Mathf.Max(0.01f, daysToMaturity);
            healthRecoveryPerHour = Mathf.Max(0f, healthRecoveryPerHour);
            stressDamagePerHour = Mathf.Max(0f, stressDamagePerHour);
            maxScale = Mathf.Max(0.01f, maxScale);
            stressedThreshold = Mathf.Min(stressedThreshold, healthyThreshold);

            springGrowthMultiplier = Mathf.Max(0f, springGrowthMultiplier);
            summerGrowthMultiplier = Mathf.Max(0f, summerGrowthMultiplier);
            autumnGrowthMultiplier = Mathf.Max(0f, autumnGrowthMultiplier);
            winterGrowthMultiplier = Mathf.Max(0f, winterGrowthMultiplier);
        }

        private static Vector2 OrderRange(Vector2 range)
        {
            return range.x <= range.y ? range : new Vector2(range.y, range.x);
        }

        private static Vector2 ClampRange01(Vector2 range)
        {
            return new Vector2(Mathf.Clamp01(range.x), Mathf.Clamp01(range.y));
        }
    }
}
