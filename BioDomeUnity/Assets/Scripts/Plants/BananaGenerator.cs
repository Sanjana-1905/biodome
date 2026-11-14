using System.Collections.Generic;
using BioDome.Procedural;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Procedural banana plant (Musa): a thick, tapered green PSEUDOSTEM with visible leaf-sheath rings, topped by a crown
    /// of enormous, broad blades whose margins are torn into parallel ribbons, like a real banana leaf. Blades rise on short
    /// stout petioles and arch over until their tips hang. Old leaves are outermost; the newest, a narrow rolled "cigar",
    /// stands upright in the middle. A mature plant also shows a hanging purple flower bud.
    /// Growth below 1 gives a short fat sucker with a few small leaves, rising to a tall plant with a wide vase-shaped crown.
    /// </summary>
    public class BananaGenerator : PlantGenerator
    {
        [Header("Banana")]
        [SerializeField, Min(0.01f), Tooltip("Pseudostem radius at the base of a fully grown plant, in metres.")]
        private float stemBaseRadius = 0.09f;
        [SerializeField, Range(0f, 3f), Tooltip("How far the big blades arch over and droop (radians of total bend).")]
        private float leafArch = 1.9f;
        [SerializeField, Range(0f, 1f), Tooltip("How far the pseudostem leans to one side.")]
        private float stemLean = 0.3f;
        [SerializeField, Range(0f, 1f), Tooltip("How ragged the blade margins are (0 = smooth).")]
        private float tearing = 0.7f;
        [SerializeField, Tooltip("Show the hanging flower bud on a mature plant.")]
        private bool flowerBud = true;

        protected override int AlgorithmVersion => 2;

        protected override void ApplyDefaults()
        {
            height = 2.8f;
            width = 2.6f;
            leafCount = 10;
            leafLength = 1.45f;
            leafWidth = 0.7f;
            leafColor = new Color(0.30f, 0.52f, 0.13f); // yellow-green, clearly different from Monstera
            colorVariation = 0.6f;
        }

        private int Mix(int hash)
        {
            hash = ProceduralMeshUtility.Combine(hash, stemBaseRadius);
            hash = ProceduralMeshUtility.Combine(hash, leafArch);
            hash = ProceduralMeshUtility.Combine(hash, stemLean);
            hash = ProceduralMeshUtility.Combine(hash, tearing);
            hash = ProceduralMeshUtility.Combine(hash, flowerBud ? 1 : 0);
            return hash;
        }

        protected override int AppendVariantKey(int hash) => Mix(hash);
        protected override int ComputeHash() => Mix(base.ComputeHash());

        protected override void Build(PlantBuildContext c)
        {
            System.Random rng = c.Rng;
            float g = c.Growth;
            float growthCurve = Mathf.SmoothStep(0f, 1f, g);
            float sg = Mathf.Lerp(0.22f, 1f, growthCurve); // overall size factor
            int leaves = Mathf.Max(4, Mathf.RoundToInt(c.LeafCount * Mathf.Lerp(0.5f, 1f, g)));

            // Pseudostem: thick, tapering, slightly flared at the base, leaning a little.
            float stemHeight = c.Height * 0.4f * Mathf.Lerp(0.2f, 1f, growthCurve);
            float leanAngle = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);
            Vector3 lean = new Vector3(Mathf.Cos(leanAngle), 0f, Mathf.Sin(leanAngle)) * (stemHeight * 0.07f * stemLean);
            List<Vector3> stemPath = ProceduralMeshUtility.SampleBezier(
                Vector3.zero,
                Vector3.up * stemHeight * 0.35f,
                lean * 0.6f + Vector3.up * stemHeight * 0.7f,
                lean + Vector3.up * stemHeight,
                10);

            float baseRadius = stemBaseRadius * Mathf.Lerp(0.4f, 1f, growthCurve) * Mathf.Clamp(c.Width / 2.6f, 0.7f, 1.4f);
            StemMeshGenerator.AddTube(c.Data, stemPath, baseRadius * 1.2f, baseRadius * 0.6f, 12, PlantBuildContext.StemSubmesh, false, true);

            // Leaf-sheath rings: short slightly fatter collars up the pseudostem.
            int collars = Mathf.Max(2, Mathf.RoundToInt(5 * Mathf.Lerp(0.5f, 1f, g)));
            for (int k = 0; k < collars; k++)
            {
                float t = (k + 0.7f) / (collars + 0.4f);
                Vector3 p = ProceduralMeshUtility.PointOnPath(stemPath, t);
                float r = Mathf.Lerp(baseRadius * 1.2f, baseRadius * 0.6f, t) * ProceduralMeshUtility.Range(rng, 1.08f, 1.16f);
                float h = stemHeight * 0.06f;
                var ring = new List<Vector3> { p - Vector3.up * h * 0.5f, p + Vector3.up * h * 0.5f };
                StemMeshGenerator.AddTube(c.Data, ring, r, r * 0.97f, 10, PlantBuildContext.StemSubmesh, false, false);
            }

            Vector3 crown = stemPath[stemPath.Count - 1];

            int ribbons = Mathf.Max(8, Mathf.RoundToInt(c.LeafLengthSegments * 0.45f));
            const float goldenAngle = 2.39996323f;
            float azimuth = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);

            for (int i = 0; i < leaves; i++)
            {
                float order = leaves > 1 ? i / (float)(leaves - 1) : 1f; // 0 = outer and oldest, 1 = central and newest
                float maturity = Mathf.Clamp01((g - 0.4f * order) / 0.6f);

                float sizeFactor = Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, maturity))
                                   * Mathf.Lerp(1f, 0.62f, order)
                                   * ProceduralMeshUtility.Range(rng, 0.92f, 1.08f);
                float length = c.LeafLength * sizeFactor;
                float leafW = c.LeafWidth * sizeFactor * Mathf.Lerp(1f, 0.32f, order * order); // the newest leaf is still rolled and narrow

                float az = azimuth + i * goldenAngle + ProceduralMeshUtility.Signed(rng, 0.15f);
                Vector3 outward = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));

                // Old leaves rise steeply on their petioles then arch far over; the newest stands almost upright.
                float pitch = Mathf.Lerp(0.85f, 1.45f, Mathf.Pow(order, 0.7f));
                float bend = leafArch * Mathf.Lerp(1.05f, 0.1f, Mathf.Pow(order, 0.8f)) * Mathf.Lerp(0.55f, 1f, maturity);

                // Short, stout petiole rising from the crown; the blade carries on from its end.
                float petioleLength = c.Height * 0.12f * sg * Mathf.Lerp(1.1f, 0.55f, order) * ProceduralMeshUtility.Range(rng, 0.9f, 1.1f);
                Vector3 p0 = crown + outward * (baseRadius * 0.3f);
                List<Vector3> petiole = PlantParts.ArchPath(p0, outward, pitch, 0.15f, petioleLength, 5);
                float radius = Mathf.Lerp(0.014f, 0.03f, sg);
                StemMeshGenerator.AddTube(c.Data, petiole, radius * 1.25f, radius * 0.8f, 6, PlantBuildContext.StemSubmesh, false, false);

                Vector3 bladeStart = petiole[petiole.Count - 1];
                List<Vector3> midrib = PlantParts.ArchPath(bladeStart, outward, pitch - 0.1f, bend, length, 10);
                PlantParts.AddRibbonBlade(c.Data, midrib, outward, leafW, ribbons, 0.55f + 0.25f * (1f - order), tearing * maturity,
                    rng, rng.Next(PlantBuildContext.LeafVariantCount), PlantBuildContext.StemSubmesh, radius * 0.55f);
            }

            // Hanging purple flower bud on a mature plant.
            if (flowerBud && g > 0.86f)
            {
                float flowerScale = Mathf.Clamp01((g - 0.86f) / 0.12f);
                Vector3 heading = new Vector3(Mathf.Cos(azimuth + 1.1f), 0f, Mathf.Sin(azimuth + 1.1f));
                List<Vector3> stalk = PlantParts.ArchPath(crown, heading, 1.05f, 2.7f, 0.5f * Mathf.Clamp(c.Height / 2.8f, 0.7f, 1.4f), 8);
                StemMeshGenerator.AddTube(c.Data, stalk, 0.014f, 0.01f, 5, PlantBuildContext.StemSubmesh, false, false);
                Vector3 end = stalk[stalk.Count - 1];
                float budRadius = 0.065f * flowerScale;
                MeshShapes.AddSphere(c.Data, end + Vector3.down * budRadius * 1.3f, budRadius, new Vector3(0.6f, 1.7f, 0.6f), 4, 8, rng, 0.05f, PlantBuildContext.AccentSubmesh);
            }
        }
    }
}
