using System.Collections.Generic;
using BioDome.Procedural;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Procedural Areca palm (Dypsis lutescens): a CLUMP of several slender, gently leaning canes, each topped by a loose crown
    /// of long, arching, feathery pinnate fronds. Inner canes are tallest and upright, outer canes shorter and leaning outwards.
    /// Young fronds stand upright; old ones arch over and droop. A young plant is two or three short canes with a few small fronds.
    /// Base fields are reused as: Leaf Count = number of canes, Leaf Length = frond length, Leaf Width = leaflet length.
    /// </summary>
    public class ArecaPalmGenerator : PlantGenerator
    {
        [Header("Areca Palm")]
        [SerializeField, Range(2, 8), Tooltip("Fronds on each cane of a fully grown plant.")]
        private int frondsPerCane = 5;
        [SerializeField, Range(6, 24), Tooltip("Leaflet pairs on a full-length frond.")]
        private int leafletPairs = 14;
        [SerializeField, Min(0.003f)] private float caneRadius = 0.016f;
        [SerializeField, Min(0.005f), Tooltip("Leaflet width in metres.")]
        private float leafletWidth = 0.065f;

        protected override int AlgorithmVersion => 1;

        protected override void ApplyDefaults()
        {
            height = 2.2f;
            width = 1.8f;
            leafCount = 6;
            leafLength = 1.05f;
            leafWidth = 0.34f;
            leafColor = new Color(0.22f, 0.46f, 0.12f);
            colorVariation = 0.45f;
        }

        private int Mix(int hash)
        {
            hash = ProceduralMeshUtility.Combine(hash, frondsPerCane);
            hash = ProceduralMeshUtility.Combine(hash, leafletPairs);
            hash = ProceduralMeshUtility.Combine(hash, caneRadius);
            hash = ProceduralMeshUtility.Combine(hash, leafletWidth);
            return hash;
        }

        protected override int AppendVariantKey(int hash) => Mix(hash);
        protected override int ComputeHash() => Mix(base.ComputeHash());

        protected override void Build(PlantBuildContext c)
        {
            System.Random rng = c.Rng;
            float g = c.Growth;
            float growthCurve = Mathf.SmoothStep(0f, 1f, g);
            float sg = Mathf.Lerp(0.28f, 1f, growthCurve);
            int canes = Mathf.Max(2, Mathf.RoundToInt(c.LeafCount * Mathf.Lerp(0.3f, 1f, g)));

            int leafletSegments = c.LeafLengthSegments <= 28 ? 1 : 2; // detail 1 keeps leaflets as cheap pointed blades
            const float goldenAngle = 2.39996323f;
            float clumpAzimuth = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);

            for (int i = 0; i < canes; i++)
            {
                float order = canes > 1 ? i / (float)(canes - 1) : 0f; // 0 = central and tallest, 1 = outer and shortest
                float caneMaturity = Mathf.Clamp01((g - 0.2f * order) / 0.8f);

                // Base position in a small ring, lean away from the centre.
                float ringAngle = clumpAzimuth + i * goldenAngle;
                Vector3 outward = new Vector3(Mathf.Cos(ringAngle), 0f, Mathf.Sin(ringAngle));
                float ringRadius = (i == 0 ? 0f : 0.04f + 0.1f * Mathf.Sqrt(order)) * Mathf.Clamp(c.Width / 1.8f, 0.7f, 1.4f);
                Vector3 baseP = outward * ringRadius;

                float caneHeight = c.Height * Mathf.Lerp(1f, 0.55f, Mathf.Pow(order, 0.8f)) * sg * ProceduralMeshUtility.Range(rng, 0.9f, 1.05f) * 0.62f;
                float leanAngle = Mathf.Lerp(0.03f, 0.4f, order) * ProceduralMeshUtility.Range(rng, 0.7f, 1.2f);
                Vector3 lean = outward * Mathf.Sin(leanAngle) * caneHeight + Vector3.up * Mathf.Cos(leanAngle) * caneHeight;
                Vector3 top = baseP + lean;
                List<Vector3> path = ProceduralMeshUtility.SampleBezier(
                    baseP, baseP + Vector3.up * caneHeight * 0.4f, top - lean.normalized * caneHeight * 0.3f + outward * caneHeight * 0.03f, top, 8);

                float radius = caneRadius * Mathf.Lerp(0.55f, 1f, sg);
                StemMeshGenerator.AddTube(c.Data, path, radius * 1.25f, radius * 0.8f, 6, PlantBuildContext.StemSubmesh, false, true);

                // Crown of fronds, oldest lowest and most drooping, newest nearly upright.
                int fronds = Mathf.Max(3, Mathf.RoundToInt(frondsPerCane * Mathf.Lerp(0.55f, 1f, caneMaturity)));
                float frondAzimuth = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);
                for (int f = 0; f < fronds; f++)
                {
                    float fOrder = fronds > 1 ? f / (float)(fronds - 1) : 1f; // 0 = oldest, 1 = newest
                    float az = frondAzimuth + f * goldenAngle + ProceduralMeshUtility.Signed(rng, 0.2f);
                    Vector3 frondOut = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));

                    float maturity = Mathf.Clamp01((g - 0.25f * fOrder) / 0.75f);
                    float frondLength = c.LeafLength * Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, maturity))
                                        * Mathf.Lerp(1f, 0.7f, fOrder) * ProceduralMeshUtility.Range(rng, 0.88f, 1.1f);
                    float pitch = Mathf.Lerp(0.3f, 1.3f, Mathf.Pow(fOrder, 0.9f)) + ProceduralMeshUtility.Signed(rng, 0.1f);
                    float bend = Mathf.Lerp(1.5f, 0.35f, fOrder) * Mathf.Lerp(0.6f, 1f, maturity);

                    List<Vector3> rachis = PlantParts.ArchPath(top, frondOut, pitch, bend, frondLength, 9);
                    int pairs = Mathf.Max(5, Mathf.RoundToInt(leafletPairs * Mathf.Lerp(0.5f, 1f, maturity)));
                    float leafletLength = c.LeafWidth * Mathf.Lerp(0.45f, 1f, maturity) * ProceduralMeshUtility.Range(rng, 0.9f, 1.1f);

                    int variant = rng.Next(PlantBuildContext.LeafVariantCount);
                    PlantParts.AddFrond(c.Data, rachis, frondOut, pairs, leafletLength, leafletWidth * Mathf.Lerp(0.6f, 1f, sg),
                        0.95f, 0.3f, leafletSegments, 0.0035f, PlantBuildContext.StemSubmesh, variant, rng);
                }
            }
        }
    }
}
