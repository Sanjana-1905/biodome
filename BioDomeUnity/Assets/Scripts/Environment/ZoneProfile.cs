using BioDome.Core;
using UnityEngine;

namespace BioDome.Environment
{
    /// <summary>The five independent experimental conditions.</summary>
    public enum ExperimentalCondition
    {
        Control = 0,
        HeatStress = 1,
        LowHumidity = 2,
        LowLight = 3,
        WaterStress = 4
    }

    /// <summary>
    /// Reusable description of how a zone's climate differs from the greenhouse's shared climate.
    /// Pure data plus one pure function (<see cref="Apply"/>): the greenhouse conditions go in, the zone's conditions come out.
    /// A plant with no profile (every original display plant) sees the greenhouse conditions unchanged.
    /// Create via Assets > Create > BioDome > Zone Profile, or use <see cref="ZoneProfilePresets"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "NewZoneProfile", menuName = "BioDome/Zone Profile", order = 2)]
    public class ZoneProfile : ScriptableObject
    {
        [SerializeField] private string displayName = "Zone";
        [SerializeField] private ExperimentalCondition condition = ExperimentalCondition.Control;
        [SerializeField, Range(-30f, 30f), Tooltip("Added to the greenhouse air temperature (C).")]
        private float temperatureOffsetCelsius;
        [SerializeField, Range(-1f, 1f), Tooltip("Added to relative humidity (0-1 scale, so -0.3 means 30 percentage points drier).")]
        private float humidityOffset;
        [SerializeField, Range(0f, 1f), Tooltip("Multiplies the light level. 1 = unchanged, lower = shaded.")]
        private float lightMultiplier = 1f;
        [SerializeField, Range(0f, 1f), Tooltip("Fraction of delivered water the soil takes up (advanced; 1 = unchanged).")]
        private float waterIntakeFactor = 1f;

        [SerializeField, Range(0f, 1f), Tooltip("Share of an equal irrigation split each plant in this zone receives. 1 = normal; lower = water-limited.")]
        private float waterShareWeight = 1f;

        public float WaterShareWeight => waterShareWeight;
        public string DisplayName => displayName;
        public ExperimentalCondition Condition => condition;
        public float TemperatureOffsetCelsius => temperatureOffsetCelsius;
        public float HumidityOffset => humidityOffset;
        public float LightMultiplier => lightMultiplier;
        public float WaterIntakeFactor => waterIntakeFactor;

        /// <summary>True when the profile changes nothing.</summary>
        public bool IsIdentity =>
            temperatureOffsetCelsius == 0f && humidityOffset == 0f && lightMultiplier == 1f && waterIntakeFactor == 1f && waterShareWeight == 1f;

        /// <summary>Returns the conditions this zone experiences. Identity profiles return the input untouched.</summary>
        public EnvironmentConditions Apply(in EnvironmentConditions greenhouse)
        {
            if (temperatureOffsetCelsius == 0f && humidityOffset == 0f && lightMultiplier == 1f) return greenhouse;

            return new EnvironmentConditions(
                greenhouse.TemperatureCelsius + temperatureOffsetCelsius,
                Mathf.Clamp01(greenhouse.Humidity01 + humidityOffset),
                Mathf.Clamp01(greenhouse.Light01 * lightMultiplier),
                greenhouse.Season,
                greenhouse.TimeOfDayHours,
                greenhouse.DayCount);
        }

        /// <summary>Sets every value (used by presets, editor tools and tests).</summary>
        public void Configure(string name, ExperimentalCondition kind, float temperatureOffset, float humidity, float light, float waterIntake, float waterShare = 1f)
        {
            waterShareWeight = Mathf.Clamp01(waterShare);
            displayName = name;
            condition = kind;
            temperatureOffsetCelsius = temperatureOffset;
            humidityOffset = humidity;
            lightMultiplier = Mathf.Clamp01(light);
            waterIntakeFactor = Mathf.Clamp01(waterIntake);
        }

        private void OnValidate()
        {
            lightMultiplier = Mathf.Clamp01(lightMultiplier);
            waterIntakeFactor = Mathf.Clamp01(waterIntakeFactor);
        }
    }

    /// <summary>The approved values of the five experimental conditions, in one place.</summary>
    public static class ZoneProfilePresets
    {
        public const float HeatOffsetCelsius = 10f;
        public const float LowHumidityOffset = -0.30f;
        public const float LowLightMultiplier = 0.40f;
        public const float WaterShare = 0.25f;

        public static string NameOf(ExperimentalCondition condition)
        {
            switch (condition)
            {
                case ExperimentalCondition.HeatStress: return "Heat stress";
                case ExperimentalCondition.LowHumidity: return "Low humidity";
                case ExperimentalCondition.LowLight: return "Low light";
                case ExperimentalCondition.WaterStress: return "Water stress";
                default: return "Control";
            }
        }

        /// <summary>Fills an existing profile with the preset for <paramref name="condition"/>.</summary>
        public static void Fill(ZoneProfile profile, ExperimentalCondition condition)
        {
            float t = 0f, h = 0f, l = 1f, w = 1f, share = 1f;
            switch (condition)
            {
                case ExperimentalCondition.HeatStress: t = HeatOffsetCelsius; break;
                case ExperimentalCondition.LowHumidity: h = LowHumidityOffset; break;
                case ExperimentalCondition.LowLight: l = LowLightMultiplier; break;
                case ExperimentalCondition.WaterStress: share = WaterShare; break;
            }
            profile.Configure(NameOf(condition), condition, t, h, l, w, share);
        }

        /// <summary>Creates a new in-memory profile (not an asset) for <paramref name="condition"/>.</summary>
        public static ZoneProfile Create(ExperimentalCondition condition)
        {
            var profile = ScriptableObject.CreateInstance<ZoneProfile>();
            Fill(profile, condition);
            return profile;
        }
    }
}
