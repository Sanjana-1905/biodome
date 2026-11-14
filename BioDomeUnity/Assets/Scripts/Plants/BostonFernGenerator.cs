using System.Collections.Generic;
using BioDome.Procedural;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Procedural Boston fern (Nephrolepis exaltata): a dense, rounded MOUND of many long, narrow, sword-shaped fronds
    /// that rise from one crown and arch gracefully outwards, each lined with dozens of tiny leaflets. Young fronds in the
    /// middle stand upright, older outer ones spill over the sides. A young plant is a small tuft of short upright fronds.
    /// Base fields are reused as: Leaf Count = fronds, Leaf Length = frond length, Leaf Width = leaflet length.
    /// </summary>
    public class BostonFernGenerator : PlantGenerator
    {
        [Header("Boston Fern")]
        [SerializeField, Range(8, 36), Tooltip("Leaflet pairs on a full-length frond.")]
        private int leafletPairs = 17;
        [SerializeField, Min(0.003f), Tooltip("Leaflet width in metres.")]
        private float leafletWidth = 0.036f;
        [SerializeField, Range(0.3f, 1.4f), Tooltip("How far the frond tips arch over (radians).")]
        private float archAmount = 1.0f;

        protected override int AlgorithmVersion => 1;

        protected override void ApplyDefaults()
        {
            height = 0.6f;
            width = 0.95f;
            leafCount = 22;
            leafLength = 0.56f;
            leafWidth = 0.11f;
            leafColor = new Color(0.30f, 0.58f, 0.18f);
            colorVariation = 0.55f;
        }

        private int Mix(int hash)
        {
            hash = ProceduralMeshUtility.Combine(hash, leafletPairs);
            hash = ProceduralMeshUtility.Combine(hash, leafletWidth);
            hash = ProceduralMeshUtility.Combine(hash, archAmount);
            return hash;
        }

        protected override int AppendVariantKey(int hash) => Mix(hash);
        protected override int ComputeHash() => Mix(base.ComputeHash());

        protected override void Build(PlantBuildContext c)
        {
            System.Random rng = c.Rng;
            float g = c.Growth;
            float sg = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, g));
            int fronds = Mathf.Max(5, Mathf.RoundToInt(c.LeafCount * Mathf.Lerp(0.4f, 1f, g)));

            const float goldenAngle = 2.39996323f;
            float azimuth = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);

            for (int i = 0; i < fronds; i++)
            {
                float order = fronds > 1 ? i / (float)(fronds - 1) : 1f; // 0 = outer and oldest, 1 = central and newest
                float maturity = Mathf.Clamp01((g - 0.35f * order) / 0.65f);

                float az = azimuth + i * goldenAngle + ProceduralMeshUtility.Signed(rng, 0.18f);
                Vector3 outward = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));

                float length = c.LeafLength * Mathf.Lerp(0.4f, 1f, Mathf.SmoothStep(0f, 1f, maturity))
                               * Mathf.Lerp(1f, 0.75f, order) * ProceduralMeshUtility.Range(rng, 0.85f, 1.12f);
                float pitch = Mathf.Lerp(0.85f, 1.35f, Mathf.Pow(order, 0.8f)) + ProceduralMeshUtility.Signed(rng, 0.1f);
                float bend = archAmount * Mathf.Lerp(1.35f, 0.4f, order) * Mathf.Lerp(0.7f, 1f, maturity);

                // Fronds start from a small crown, not a single point.
                Vector3 start = outward * (0.015f * sg * ProceduralMeshUtility.Range(rng, 0.5f, 1.5f));
                List<Vector3> rachis = PlantParts.ArchPath(start, outward, pitch, bend, length, 8);

                int pairs = Mathf.Max(6, Mathf.RoundToInt(leafletPairs * Mathf.Lerp(0.55f, 1f, maturity)));
                float leafletLength = c.LeafWidth * Mathf.Lerp(0.5f, 1f, maturity) * ProceduralMeshUtility.Range(rng, 0.9f, 1.1f);
                int variant = rng.Next(PlantBuildContext.LeafVariantCount);

                // Detail 1 uses single-segment leaflets (8 triangles each) so a dense mound stays cheap.
                int segments = c.LeafLengthSegments <= 28 ? 1 : 2;
                PlantParts.AddFrond(c.Data, rachis, outward, pairs, leafletLength, leafletWidth * Mathf.Lerp(0.7f, 1f, sg),
                    1.15f, 0.2f, segments, 0.0022f, PlantBuildContext.StemSubmesh, variant, rng, 0.08f);
            }
        }
    }
}
