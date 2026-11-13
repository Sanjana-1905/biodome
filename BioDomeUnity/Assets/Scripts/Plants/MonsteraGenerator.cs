using System.Collections.Generic;
using BioDome.Procedural;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Procedural Monstera deliciae: a short central stem carrying long arching petioles, each ending in a
    /// large heart-shaped leaf with splits and holes. Older leaves are lower, larger and more droopy; the newest
    /// leaves are small, upright and still entire. Growth below 1 gives fewer, smaller, less split leaves.
    /// </summary>
    public class MonsteraGenerator : PlantGenerator
    {
        [Header("Monstera")]
        [SerializeField, Range(0, 7), Tooltip("Splits per side on a fully mature leaf.")]
        private int splitCount = 7;
        [SerializeField, Range(0, 7), Tooltip("Holes per side on a fully mature leaf.")]
        private int holeCount = 6;
        [SerializeField, Min(0f), Tooltip("Blade thickness in metres. 0 gives a single-sided surface.")]
        private float leafThickness = 0.003f;
        [SerializeField, Range(0f, 2f), Tooltip("Multiplier for how much leaf tips droop.")]
        private float leafDroop = 1f;
        [SerializeField, Min(0.002f)] private float petioleRadius = 0.013f;

        protected override int AlgorithmVersion => 1;

        protected override void ApplyDefaults()
        {
            height = 1.2f;
            width = 1.2f;
            leafCount = 9;
            leafLength = 0.5f;
            leafWidth = 0.42f;
            leafColor = new Color(0.16f, 0.42f, 0.14f);
        }

        protected override int AppendVariantKey(int hash)
        {
            hash = ProceduralMeshUtility.Combine(hash, splitCount);
            hash = ProceduralMeshUtility.Combine(hash, holeCount);
            hash = ProceduralMeshUtility.Combine(hash, leafThickness);
            hash = ProceduralMeshUtility.Combine(hash, leafDroop);
            hash = ProceduralMeshUtility.Combine(hash, petioleRadius);
            return hash;
        }

        protected override int ComputeHash()
        {
            int h = base.ComputeHash();
            h = ProceduralMeshUtility.Combine(h, splitCount);
            h = ProceduralMeshUtility.Combine(h, holeCount);
            h = ProceduralMeshUtility.Combine(h, leafThickness);
            h = ProceduralMeshUtility.Combine(h, leafDroop);
            h = ProceduralMeshUtility.Combine(h, petioleRadius);
            return h;
        }

        protected override void Build(PlantBuildContext c)
        {
            System.Random rng = c.Rng;
            float g = c.Growth;
            float sg = Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, g)); // overall size factor
            int leaves = Mathf.Max(2, Mathf.RoundToInt(c.LeafCount * Mathf.Lerp(0.4f, 1f, g)));

            // Central stem: a gently leaning curve.
            float stemHeight = c.Height * 0.42f * sg;
            float leanAngle = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);
            Vector3 lean = new Vector3(Mathf.Cos(leanAngle), 0f, Mathf.Sin(leanAngle)) * (c.Height * 0.05f * sg);
            List<Vector3> stemPath = ProceduralMeshUtility.SampleBezier(
                Vector3.zero,
                lean * 0.15f + Vector3.up * stemHeight * 0.35f,
                lean * 0.6f + Vector3.up * stemHeight * 0.7f,
                lean + Vector3.up * stemHeight,
                12);

            float stemRadius = Mathf.Lerp(0.012f, 0.03f, sg) * Mathf.Clamp(c.Width / 1.2f, 0.7f, 1.5f);
            StemMeshGenerator.AddTube(c.Data, stemPath, stemRadius, stemRadius * 0.65f, 10, PlantBuildContext.StemSubmesh, false, true);

            // Leaves, oldest (outer, low) to newest (inner, high), spaced by the golden angle.
            const float goldenAngle = 2.39996323f;
            float azimuth = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);

            for (int i = 0; i < leaves; i++)
            {
                float order = leaves > 1 ? i / (float)(leaves - 1) : 1f;
                float maturity = Mathf.Clamp01((g - 0.45f * order) / 0.6f);

                float sizeFactor = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, maturity))
                                   * Mathf.Lerp(1f, 0.85f, order)
                                   * ProceduralMeshUtility.Range(rng, 0.92f, 1.08f);
                float length = c.LeafLength * sizeFactor;
                float leafW = c.LeafWidth * sizeFactor;

                float az = azimuth + i * goldenAngle + ProceduralMeshUtility.Signed(rng, 0.2f);
                Vector3 outward = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));

                // Where the petiole leaves the stem and where the leaf blade attaches.
                float attach = Mathf.Lerp(0.1f, 1f, Mathf.Pow(order, 0.8f));
                Vector3 p0 = ProceduralMeshUtility.PointOnPath(stemPath, attach);
                float reach = Mathf.Max(0.04f, c.Width * 0.5f * Mathf.Lerp(1f, 0.3f, order) * ProceduralMeshUtility.Range(rng, 0.85f, 1f) * sg - length * 0.3f);
                float y3 = Mathf.Lerp(0.35f, 0.85f, order) * c.Height * sg * ProceduralMeshUtility.Range(rng, 0.92f, 1.05f);
                y3 = Mathf.Max(y3, p0.y + 0.08f);
                Vector3 p3 = new Vector3(p0.x + outward.x * reach, y3, p0.z + outward.z * reach);

                // Blade heading: older leaves are flatter, newer ones more upright.
                float pitch = Mathf.Lerp(-0.05f, 0.55f, order) + ProceduralMeshUtility.Signed(rng, 0.1f);
                Vector3 forward = (outward * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 normal = Vector3.Cross(forward, right);
                float roll = ProceduralMeshUtility.Signed(rng, 18f);
                Quaternion rotation = Quaternion.AngleAxis(roll, forward) * Quaternion.LookRotation(normal, forward);

                // Petiole: an arch from the stem to the blade base, arriving along the blade heading.
                float distance = Vector3.Distance(p0, p3);
                Vector3 p1 = p0 + Vector3.Lerp(Vector3.up, outward, 0.45f).normalized * (distance * 0.35f);
                Vector3 p2 = p3 - forward * (distance * 0.3f);
                List<Vector3> petiole = ProceduralMeshUtility.SampleBezier(p0, p1, p2, p3, 10);
                float radius = petioleRadius * Mathf.Lerp(0.8f, 1.1f, sg);
                StemMeshGenerator.AddTube(c.Data, petiole, radius * 1.15f, radius * 0.8f, 6, PlantBuildContext.StemSubmesh, false, false);

                var leaf = new LeafSettings
                {
                    Length = length,
                    Width = leafW,
                    Thickness = leafThickness,
                    Shape = LeafShape.Cordate,
                    LengthSegments = c.LeafLengthSegments,
                    HalfWidthSegments = c.LeafHalfWidthSegments,
                    InitialPitch = ProceduralMeshUtility.Range(rng, 0.1f, 0.3f),
                    TotalBend = -leafDroop * Mathf.Lerp(0.9f, 0.3f, order) * Mathf.Lerp(0.7f, 1f, maturity),
                    Cup = ProceduralMeshUtility.Range(rng, 0.1f, 0.2f),
                    Ripple = 0.012f,
                    LobeDepth = ProceduralMeshUtility.Range(rng, 0.13f, 0.19f),
                    Maturity = maturity,
                    Fenestrate = true,
                    SplitCount = splitCount,
                    HoleCount = holeCount
                };

                int variant = rng.Next(PlantBuildContext.LeafVariantCount);
                LeafMeshGenerator.Build(c.Data, leaf, rng, Matrix4x4.TRS(p3, rotation, Vector3.one), variant);
            }
        }
    }
}
