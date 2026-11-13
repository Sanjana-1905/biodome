namespace BioDome.Core
{
    /// <summary>
    /// Immutable snapshot of the greenhouse environment at one moment.
    /// This is the only data that flows from the environment into the plant simulation,
    /// so the simulation never needs a reference to any MonoBehaviour.
    /// </summary>
    public readonly struct EnvironmentConditions
    {
        /// <summary>Air temperature in degrees Celsius.</summary>
        public readonly float TemperatureCelsius;

        /// <summary>Relative humidity, 0 (dry) to 1 (saturated).</summary>
        public readonly float Humidity01;

        /// <summary>Light reaching the plants, 0 (dark) to 1 (full sun).</summary>
        public readonly float Light01;

        public readonly Season Season;

        /// <summary>Hour of the day, 0 (inclusive) to 24 (exclusive).</summary>
        public readonly float TimeOfDayHours;

        /// <summary>Whole days elapsed since the simulation started.</summary>
        public readonly int DayCount;

        public EnvironmentConditions(
            float temperatureCelsius,
            float humidity01,
            float light01,
            Season season,
            float timeOfDayHours,
            int dayCount)
        {
            TemperatureCelsius = temperatureCelsius;
            Humidity01 = humidity01;
            Light01 = light01;
            Season = season;
            TimeOfDayHours = timeOfDayHours;
            DayCount = dayCount;
        }

        /// <summary>A mild, plant-friendly default used before the environment has been simulated.</summary>
        public static EnvironmentConditions Default =>
            new EnvironmentConditions(22f, 0.6f, 0.7f, Season.Spring, 12f, 0);
    }
}
