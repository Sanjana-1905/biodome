namespace BioDome.Core
{
    /// <summary>The four seasons the greenhouse can simulate.</summary>
    public enum Season
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3
    }

    public static class SeasonExtensions
    {
        /// <summary>Returns the season that follows <paramref name="season"/>, wrapping Winter to Spring.</summary>
        public static Season Next(this Season season)
        {
            return (Season)(((int)season + 1) % 4);
        }
    }
}
