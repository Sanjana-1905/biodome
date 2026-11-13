using UnityEngine;

namespace BioDome.Environment
{
    /// <summary>
    /// Dimensions, layout and material choices for the procedural greenhouse.
    /// Edit this asset, then run BioDome > Build Greenhouse Scene to regenerate.
    /// Create via Assets > Create > BioDome > Greenhouse Settings (the builder also creates one automatically).
    /// All lengths are in Unity units (1 unit = 1 metre).
    /// </summary>
    [CreateAssetMenu(fileName = "GreenhouseSettings", menuName = "BioDome/Greenhouse Settings", order = 2)]
    public class GreenhouseSettings : ScriptableObject
    {
        [Header("Dimensions")]
        [Min(12f)] public float length = 40f;
        [Min(10f)] public float width = 25f;
        [Tooltip("Total height from floor to the roof ridge.")]
        [Min(5f)] public float height = 12f;
        [Tooltip("Height of the side walls up to the eaves. The roof rises from here to the ridge.")]
        [Min(3f)] public float wallHeight = 7f;
        [Min(0.1f)] public float plinthHeight = 0.6f;

        [Header("Structure")]
        [Tooltip("Target distance between structural frames along the length. The actual value is adjusted so frames divide the length evenly.")]
        [Min(1.5f)] public float bayLength = 4f;
        [Tooltip("Target width of glass panels on the end walls.")]
        [Min(1f)] public float panelWidth = 1.8f;
        [Range(2, 8)] public int roofRows = 4;
        [Min(1)] public int braceEveryNBays = 3;
        [Tooltip("Height of the entrance opening and of the horizontal transom beams.")]
        [Min(2.5f)] public float doorHeight = 3.6f;

        [Header("Interior Layout")]
        [Min(1.5f)] public float pathWidth = 3f;
        [Min(3.2f)] public float plazaRadius = 4.2f;
        [Tooltip("Distance from the entrance wall to the first raised bed.")]
        [Min(2f)] public float entranceMargin = 5f;
        [Range(0, 4)] public int bedRowsPerSide = 2;
        [Range(0, 6)] public int bedsPerRow = 3;
        [Min(0.2f)] public float bedHeight = 0.5f;
        [Tooltip("Gap between beds along the length (also the width of cross paths).")]
        [Min(0.8f)] public float bedGap = 1.4f;
        [Min(0.8f)] public float aisleWidth = 1.2f;
        [Min(0.6f)] public float borderPlanterWidth = 1.2f;

        [Header("Props")]
        [Min(0)] public int decorativeStones = 36;
        [Range(0, 8)] public int plazaBenches = 4;
        [Range(0, 3)] public int pathBenchPairs = 1;
        [Tooltip("A pendant lamp is hung on every Nth structural frame. 0 disables lamps.")]
        [Min(0)] public int lampEveryNFrames = 2;
        [Tooltip("Adds a real point light to each lamp. Turn off for lower cost.")]
        public bool addPointLights = true;
        public int seed = 4217;

        [Header("Materials (empty = default asset is created in Art/Materials)")]
        public Material steel;
        public Material glass;
        public Material stone;
        public Material timber;
        public Material soil;
        public Material pavers;
        public Material gravel;
        public Material ground;
        public Material water;
        public Material pipe;
        public Material lampBulb;

        private void OnValidate()
        {
            height = Mathf.Max(height, wallHeight + 1.5f);
            wallHeight = Mathf.Clamp(wallHeight, 3f, height - 1.5f);
            doorHeight = Mathf.Min(doorHeight, wallHeight - 1f);
        }
    }
}
