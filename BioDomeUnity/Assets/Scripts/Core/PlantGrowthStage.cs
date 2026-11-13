namespace BioDome.Core
{
    /// <summary>Discrete development stage derived from a plant's growth. Thresholds are set per species in PlantData.</summary>
    public enum PlantGrowthStage
    {
        Seedling = 0,
        Juvenile = 1,
        Established = 2,
        Mature = 3
    }
}
