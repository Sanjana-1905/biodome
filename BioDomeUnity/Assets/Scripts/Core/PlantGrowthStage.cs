namespace BioDome.Core
{
    /// <summary>Discrete development stage (five stages) derived from a plant's growth. Thresholds are set per species in PlantData.</summary>
    public enum PlantGrowthStage
    {
        Seedling = 0,
        Juvenile = 1,
        Established = 2,
        Mature = 3,
        FullyDeveloped = 4
    }

    public static class PlantGrowthStageNames
    {
        /// <summary>Readable stage name for the UI: Seedling, Early vegetative, Developing, Mature, Fully developed.</summary>
        public static string DisplayName(this PlantGrowthStage stage)
        {
            switch (stage)
            {
                case PlantGrowthStage.Seedling: return "Seedling";
                case PlantGrowthStage.Juvenile: return "Early vegetative";
                case PlantGrowthStage.Established: return "Developing";
                case PlantGrowthStage.Mature: return "Mature";
                default: return "Fully developed";
            }
        }
    }
}
