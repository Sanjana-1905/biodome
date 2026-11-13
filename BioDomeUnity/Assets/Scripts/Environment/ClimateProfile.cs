using System;
using BioDome.Core;
using UnityEngine;

namespace BioDome.Environment
{
    /// <summary>Climate numbers for one season.</summary>
    [Serializable]
    public class SeasonClimate
    {
        [Tooltip("Average outdoor-style temperature for the season (C), before greenhouse heating.")]
        public float meanTemperatureCelsius = 20f;
        [Tooltip("Day/night temperature swing (C). Peak is mid-afternoon.")]
        public float dailySwingCelsius = 6f;
        [Range(0f, 1f)] public float baseHumidity01 = 0.6f;
        [Range(0f, 24f)] public float sunriseHour = 6f;
        [Range(0f, 24f)] public float sunsetHour = 18f;
        [Tooltip("Brightest possible light level (0-1) at solar noon with no shading.")]
        [Range(0f, 1f)] public float peakLight01 = 1f;

        public SeasonClimate() { }

        public SeasonClimate(float meanTemp, float swing, float humidity, float sunrise, float sunset, float peakLight)
        {
            meanTemperatureCelsius = meanTemp;
            dailySwingCelsius = swing;
            baseHumidity01 = humidity;
            sunriseHour = sunrise;
            sunsetHour = sunset;
            peakLight01 = peakLight;
        }
    }

    /// <summary>
    /// Per-season climate settings. Create via Assets > Create > BioDome > Climate Profile.
    /// Pure data; the EnvironmentController turns it into live conditions.
    /// </summary>
    [CreateAssetMenu(fileName = "NewClimateProfile", menuName = "BioDome/Climate Profile", order = 1)]
    public class ClimateProfile : ScriptableObject
    {
        [SerializeField] private SeasonClimate spring = new SeasonClimate(16f, 6f, 0.65f, 6.0f, 19.0f, 0.85f);
        [SerializeField] private SeasonClimate summer = new SeasonClimate(26f, 8f, 0.55f, 5.0f, 21.0f, 1.00f);
        [SerializeField] private SeasonClimate autumn = new SeasonClimate(14f, 5f, 0.70f, 7.0f, 18.0f, 0.70f);
        [SerializeField] private SeasonClimate winter = new SeasonClimate(5f, 4f, 0.75f, 8.0f, 16.0f, 0.45f);

        public SeasonClimate GetClimate(Season season)
        {
            switch (season)
            {
                case Season.Spring: return spring;
                case Season.Summer: return summer;
                case Season.Autumn: return autumn;
                default: return winter;
            }
        }

        private void OnValidate()
        {
            EnsureValid(ref spring);
            EnsureValid(ref summer);
            EnsureValid(ref autumn);
            EnsureValid(ref winter);
        }

        private static void EnsureValid(ref SeasonClimate climate)
        {
            if (climate == null) climate = new SeasonClimate();
            // Sunset must come after sunrise or the daylight maths breaks down.
            if (climate.sunsetHour <= climate.sunriseHour + 1f)
                climate.sunsetHour = Mathf.Min(24f, climate.sunriseHour + 1f);
        }
    }
}
