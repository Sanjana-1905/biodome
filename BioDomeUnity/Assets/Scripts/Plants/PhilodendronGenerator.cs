using System.Collections.Generic;
using BioDome.Procedural;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Procedural heart-leaf Philodendron: a lush, BUSHY mound made of many slender vines carrying dozens of small glossy
    /// heart-shaped leaves. Central vines climb upright then curl over; outer vines arch outwards and cascade, so the whole plant
    /// is a dense fountain of foliage. Leaves are much smaller and far more numerous than a Monstera's, and are never split.
    /// Newer leaves near the vine tips are smaller. A young plant is a few short vines with a handful of small leaves.
    /// Base fields are reused as: Leaf Count = leaves on the whole plant, Leaf Length/Width = full-size leaf.
    /// </summary>
    public class PhilodendronGenerator : PlantGenerator
    {
        [Header("Philodendron")]
        [SerializeField, Range(3, 14), Tooltip("Vines on a fully grown plant.")]
        private int vineCount = 11;
        [SerializeField, Min(0.001f)] private float vineRadius = 0.005f;

        protected override int AlgorithmVersion => 1;

        protected override void ApplyDefaults()
        {
            height = 0.9f;
            width = 1.1f;
            leafCount = 75;
            leafLength = 0.27f;
            leafWidth = 0.23f;
            leafColor = new Color(0.13f, 0.42f, 0.17f);
            colorVariation = 0.8f;
        }

        private int Mix(int hash)
        {
            hash = ProceduralMeshUtility.Combine(hash, vineCount);
            hash = ProceduralMeshUtility.Combine(hash, vineRadius);
            return hash;
        }

        protected override int AppendVariantKey(int hash) => Mix(hash);
        protected override int ComputeHash() => Mix(base.ComputeHash());

        protected override void Build(PlantBuildContext c)
        {
            System.Random rng = c.Rng;
            float g = c.Growth;
            float growthCurve = Mathf.SmoothStep(0f, 1f, g);
            float sg = Mathf.Lerp(0.35f, 1f, growthCurve);
            int vines = Mathf.Max(3, Mathf.RoundToInt(vineCount * Mathf.Lerp(0.4f, 1f, g)));
            float leavesPerVine = c.LeafCount / (float)vines * Mathf.Lerp(0.35f, 1f, g);

            int lengthSegments = Mathf.Max(5, Mathf.RoundToInt(c.LeafLengthSegments * 0.2f));
            int widthSegments = Mathf.Max(2, Mathf.RoundToInt(c.LeafHalfWidthSegments * 0.2f));

            const float goldenAngle = 2.39996323f;
            float azimuth = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);

            for (int v = 0; v < vines; v++)
            {
                float order = vines > 1 ? v / (float)(vines - 1) : 0f; // 0 = central and upright, 1 = outer and cascading
                float az = azimuth + v * goldenAngle + ProceduralMeshUtility.Signed(rng, 0.2f);
                Vector3 outward = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));

                float vineLength = c.Height * Mathf.Lerp(0.8f, 1.1f, order) * sg * ProceduralMeshUtility.Range(rng, 0.85f, 1.1f);
                float pitch = Mathf.Lerp(1.15f, 0.8f, order);
                float bend = Mathf.Lerp(1.2f, 2.4f, order) * Mathf.Lerp(0.6f, 1f, growthCurve);
                Vector3 start = outward * (0.015f + 0.04f * order);

                List<Vector3> path = PlantParts.ArchPath(start, outward, pitch, bend, vineLength, 10);
                float radius = vineRadius * Mathf.Lerp(0.6f, 1f, sg);
                StemMeshGenerator.AddTube(c.Data, path, radius * 1.3f, radius * 0.5f, 5, PlantBuildContext.StemSubmesh, false, true);

                int count = Mathf.Max(2, Mathf.RoundToInt(leavesPerVine * ProceduralMeshUtility.Range(rng, 0.85f, 1.15f)));
                for (int l = 0; l < count; l++)
                {
                    float t = Mathf.Lerp(0.05f, 1f, (l + 0.5f) / count);
                    Vector3 p = ProceduralMeshUtility.PointOnPath(path, t);

                    // Leaves alternate sides and fan out from the vine.
                    float side = (l % 2 == 0) ? 1f : -1f;
                    float yaw = az + side * ProceduralMeshUtility.Range(rng, 0.6f, 1.5f);
                    Vector3 heading = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                    float pitchLeaf = ProceduralMeshUtility.Range(rng, -0.35f, 0.55f) - order * 0.25f;
                    Vector3 forward = (heading * Mathf.Cos(pitchLeaf) + Vector3.up * Mathf.Sin(pitchLeaf)).normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                    Vector3 normal = Vector3.Cross(forward, right);
                    Quaternion rotation = Quaternion.AngleAxis(ProceduralMeshUtility.Signed(rng, 20f), forward) * Quaternion.LookRotation(normal, forward);

                    // Short petiole, then the heart-shaped blade. Older leaves (low on the vine) are bigger.
                    float petioleLength = 0.05f * sg * ProceduralMeshUtility.Range(rng, 0.8f, 1.3f);
                    Vector3 blade = p + heading * petioleLength + Vector3.up * (0.012f * sg);
                    var petiole = new List<Vector3> { p, blade };
                    StemMeshGenerator.AddTube(c.Data, petiole, radius * 0.6f, radius * 0.4f, 3, PlantBuildContext.StemSubmesh, false, false);

                    float maturity = Mathf.Clamp01((g - 0.3f * t) / 0.7f);
                    float sizeFactor = Mathf.Lerp(1f, 0.5f, t * t) * Mathf.Lerp(0.5f, 1f, Mathf.SmoothStep(0f, 1f, maturity))
                                       * ProceduralMeshUtility.Range(rng, 0.85f, 1.15f);

                    var leaf = new LeafSettings
                    {
                        Length = c.LeafLength * sizeFactor,
                        Width = c.LeafWidth * sizeFactor,
                        Thickness = 0.0012f,
                        Shape = LeafShape.Cordate,
                        LengthSegments = lengthSegments,
                        HalfWidthSegments = widthSegments,
                        InitialPitch = ProceduralMeshUtility.Range(rng, 0.05f, 0.25f),
                        TotalBend = -ProceduralMeshUtility.Range(rng, 0.25f, 0.6f),
                        Cup = ProceduralMeshUtility.Range(rng, 0.1f, 0.2f),
                        Ripple = 0.006f,
                        LobeDepth = ProceduralMeshUtility.Range(rng, 0.2f, 0.28f),
                        Maturity = maturity,
                        Fenestrate = false
                    };

                    int variant = rng.Next(PlantBuildContext.LeafVariantCount);
                    LeafMeshGenerator.Build(c.Data, leaf, rng, Matrix4x4.TRS(blade, rotation, Vector3.one), variant);
                }
            }
        }
    }
}
