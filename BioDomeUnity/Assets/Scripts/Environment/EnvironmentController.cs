using System;
using BioDome.Core;
using UnityEngine;

namespace BioDome.Environment
{
    /// <summary>
    /// Owns the greenhouse environment: the clock (time of day, day count, season), the user-adjustable
    /// controls (heating, humidity, shading) and the sun light. It does not advance itself; the
    /// GreenhouseManager calls <see cref="Advance"/> so the whole simulation shares one clock.
    /// </summary>
    public class EnvironmentController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField, Tooltip("Required. Per-season climate numbers.")]
        private ClimateProfile climateProfile;
        [SerializeField, Tooltip("Optional. Directional light rotated and dimmed to follow the day/night cycle.")]
        private Light sunLight;

        [Header("Calendar")]
        [SerializeField] private Season startSeason = Season.Spring;
        [SerializeField, Range(0f, 24f)] private float startTimeOfDayHours = 8f;
        [SerializeField, Min(1)] private int daysPerSeason = 10;
        [SerializeField] private bool autoAdvanceSeasons = true;

        [Header("Greenhouse Controls")]
        [SerializeField, Range(-10f, 10f), Tooltip("Heating (+) or cooling (-) applied on top of the outdoor temperature, in C.")]
        private float temperatureOffsetCelsius;
        [SerializeField, Range(-0.3f, 0.3f), Tooltip("Humidifier (+) or dehumidifier (-) strength.")]
        private float humidityOffset;
        [SerializeField, Range(0f, 1f), Tooltip("Shade screen deployment. 1 blocks most sunlight.")]
        private float shadeLevel;

        [Header("Sun Light")]
        [SerializeField] private float maxSunIntensity = 1.2f;
        [SerializeField, Tooltip("Compass direction (Y rotation) the sun travels along.")]
        private float sunYawDegrees = 170f;
        [SerializeField, Range(0f, 1f), Tooltip("How much a fully deployed shade screen reduces light.")]
        private float shadeEffectiveness = 0.7f;

        private float timeOfDayHours;
        private int dayCount;
        private int daysIntoSeason;
        private Season season;
        private EnvironmentConditions current = EnvironmentConditions.Default;

        /// <summary>Raised once whenever the season changes.</summary>
        public event Action<Season> SeasonChanged;

        /// <summary>Raised every time conditions are recomputed (every Advance call).</summary>
        public event Action<EnvironmentConditions> ConditionsChanged;

        public EnvironmentConditions Current => current;
        public Season CurrentSeason => season;
        public float TemperatureOffsetCelsius => temperatureOffsetCelsius;
        public float HumidityOffset => humidityOffset;
        public float ShadeLevel => shadeLevel;
        public bool HasClimateProfile => climateProfile != null;

        private void Awake()
        {
            if (climateProfile == null)
            {
                Debug.LogError($"{nameof(EnvironmentController)} on '{name}' has no {nameof(ClimateProfile)} assigned. " +
                               "Environment will not update.", this);
            }

            season = startSeason;
            timeOfDayHours = Mathf.Repeat(startTimeOfDayHours, 24f);
            dayCount = 0;
            daysIntoSeason = 0;
            Recompute();
        }

        /// <summary>Moves the clock forward and recomputes conditions and sun position.</summary>
        public void Advance(float deltaHours)
        {
            if (climateProfile == null) return;

            if (deltaHours > 0f)
            {
                timeOfDayHours += deltaHours;
                while (timeOfDayHours >= 24f)
                {
                    timeOfDayHours -= 24f;
                    dayCount++;
                    daysIntoSeason++;

                    if (autoAdvanceSeasons && daysIntoSeason >= daysPerSeason)
                    {
                        daysIntoSeason = 0;
                        ChangeSeason(season.Next());
                    }
                }
            }

            Recompute();
            ConditionsChanged?.Invoke(current);
        }

        // --- Controls (called by UI via the GreenhouseManager) -------------------

        public void SetSeason(Season newSeason)
        {
            daysIntoSeason = 0;
            ChangeSeason(newSeason);
            Recompute();
        }

        public void SetTimeOfDay(float hours)
        {
            timeOfDayHours = Mathf.Repeat(hours, 24f);
            Recompute();
        }

        public void SetTemperatureOffset(float celsius)
        {
            temperatureOffsetCelsius = Mathf.Clamp(celsius, -10f, 10f);
            Recompute();
        }

        public void SetHumidityOffset(float offset)
        {
            humidityOffset = Mathf.Clamp(offset, -0.3f, 0.3f);
            Recompute();
        }

        public void SetShadeLevel(float level01)
        {
            shadeLevel = Mathf.Clamp01(level01);
            Recompute();
        }

        // --- Internals -----------------------------------------------------------

        private void ChangeSeason(Season newSeason)
        {
            if (newSeason == season) return;
            season = newSeason;
            SeasonChanged?.Invoke(season);
        }

        private void Recompute()
        {
            if (climateProfile == null) return;

            SeasonClimate climate = climateProfile.GetClimate(season);

            float daylight = ComputeDaylight01(climate);

            // Peaks at 15:00, lowest at 03:00.
            float dailyWave = Mathf.Sin((timeOfDayHours - 9f) / 24f * Mathf.PI * 2f);
            float temperature = climate.meanTemperatureCelsius + climate.dailySwingCelsius * dailyWave + temperatureOffsetCelsius;

            // Slightly drier air when it is warmer than the seasonal mean.
            float humidity = Mathf.Clamp01(climate.baseHumidity01 - 0.1f * dailyWave + humidityOffset);

            float light = daylight * climate.peakLight01 * (1f - shadeLevel * shadeEffectiveness);

            current = new EnvironmentConditions(temperature, humidity, Mathf.Clamp01(light), season, timeOfDayHours, dayCount);

            UpdateSun(daylight, climate);
        }

        /// <summary>0 at night, rising to 1 at solar noon.</summary>
        private float ComputeDaylight01(SeasonClimate climate)
        {
            float length = Mathf.Max(climate.sunsetHour - climate.sunriseHour, 0.01f);
            float t = (timeOfDayHours - climate.sunriseHour) / length;
            if (t <= 0f || t >= 1f) return 0f;
            return Mathf.Sin(t * Mathf.PI);
        }

        private void UpdateSun(float daylight, SeasonClimate climate)
        {
            if (sunLight == null) return;

            float length = Mathf.Max(climate.sunsetHour - climate.sunriseHour, 0.01f);
            float t = (timeOfDayHours - climate.sunriseHour) / length;

            // 0 degrees at sunrise, 90 at solar noon, 180 at sunset. Outside daylight the sun stays below the horizon.
            float elevation = t >= 0f && t <= 1f ? t * 180f : (t < 0f ? -10f : 190f);
            sunLight.transform.rotation = Quaternion.Euler(elevation, sunYawDegrees, 0f);
            sunLight.intensity = daylight * maxSunIntensity * climate.peakLight01;
        }

        private void OnValidate()
        {
            maxSunIntensity = Mathf.Max(0f, maxSunIntensity);
            daysPerSeason = Mathf.Max(1, daysPerSeason);
        }
    }
}
