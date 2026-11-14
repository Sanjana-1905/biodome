using System.Collections.Generic;
using BioDome.Procedural;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Procedural Anthurium: a compact rosette of glossy, deep-green HEART-shaped leaves on long petioles, with slender flower
    /// stalks rising among them. Each stalk ends in a waxy red heart-shaped SPATHE with a curved cream-yellow SPADIX pointing out
    /// of it. Flowers (accent submeshes 4 and 5) appear on a maturing plant; a young plant is leaves only.
    /// </summary>
    public class AnthuriumGenerator : PlantGenerator
    {
        [Header("Anthurium")]
        [SerializeField, Range(0, 8), Tooltip("Flowers on a fully mature plant.")]
        private int flowerCount = 5;
        [SerializeField, Min(0.03f), Tooltip("Spathe length in metres on a fully grown flower.")]
        private float spatheLength = 0.17f;
        [SerializeField, Min(0.001f)] private float petioleRadius = 0.0055f;

        protected override int AlgorithmVersion => 1;

        protected override void ApplyDefaults()
        {
            height = 0.6f;
            width = 0.6f;
            leafCount = 9;
            leafLength = 0.27f;
            leafWidth = 0.21f;
            leafColor = new Color(0.09f, 0.34f, 0.13f);
            colorVariation = 0.45f;
        }

        private int Mix(int hash)
        {
            hash = ProceduralMeshUtility.Combine(hash, flowerCount);
            hash = ProceduralMeshUtility.Combine(hash, spatheLength);
            hash = ProceduralMeshUtility.Combine(hash, petioleRadius);
            return hash;
        }

        protected override int AppendVariantKey(int hash) => Mix(hash);
        protected override int ComputeHash() => Mix(base.ComputeHash());

        protected override void Build(PlantBuildContext c)
        {
            System.Random rng = c.Rng;
            float g = c.Growth;
            float sg = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, g));
            int leaves = Mathf.Max(3, Mathf.RoundToInt(c.LeafCount * Mathf.Lerp(0.4f, 1f, g)));

            int lengthSegments = Mathf.Max(8, Mathf.RoundToInt(c.LeafLengthSegments * 0.4f));
            int widthSegments = Mathf.Max(3, Mathf.RoundToInt(c.LeafHalfWidthSegments * 0.3f));

            const float goldenAngle = 2.39996323f;
            float azimuth = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);

            for (int i = 0; i < leaves; i++)
            {
                float order = leaves > 1 ? i / (float)(leaves - 1) : 1f; // 0 = outer and oldest, 1 = central and newest
                float maturity = Mathf.Clamp01((g - 0.3f * order) / 0.7f);
                float sizeFactor = Mathf.Lerp(0.45f, 1f, Mathf.SmoothStep(0f, 1f, maturity))
                                   * Mathf.Lerp(1f, 0.85f, order) * ProceduralMeshUtility.Range(rng, 0.9f, 1.1f);

                float az = azimuth + i * goldenAngle + ProceduralMeshUtility.Signed(rng, 0.2f);
                Vector3 outward = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));

                float tilt = Mathf.Lerp(1.15f, 0.25f, Mathf.Pow(order, 0.9f)) * ProceduralMeshUtility.Range(rng, 0.85f, 1.1f);
                float petioleLength = c.Height * Mathf.Lerp(0.5f, 0.85f, order) * sg * ProceduralMeshUtility.Range(rng, 0.9f, 1.08f);
                Vector3 p0 = outward * (0.012f * (1f - order) + 0.004f);
                Vector3 p3 = p0 + (Vector3.up * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt)) * petioleLength;
                Vector3 p1 = p0 + Vector3.up * (petioleLength * 0.5f);
                Vector3 p2 = Vector3.Lerp(p1, p3, 0.65f);
                List<Vector3> petiole = ProceduralMeshUtility.SampleBezier(p0, p1, p2, p3, 6);
                float radius = petioleRadius * Mathf.Lerp(0.7f, 1.1f, sg);
                StemMeshGenerator.AddTube(c.Data, petiole, radius * 1.3f, radius * 0.8f, 5, PlantBuildContext.StemSubmesh, false, false);

                // Heart-shaped blade, held out and slightly drooping, face up.
                float pitch = Mathf.Lerp(0.05f, 0.85f, order);
                Vector3 forward = (outward * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 normal = Vector3.Cross(forward, right);
                Quaternion rotation = Quaternion.AngleAxis(ProceduralMeshUtility.Signed(rng, 12f), forward) * Quaternion.LookRotation(normal, forward);

                var leaf = new LeafSettings
                {
                    Length = c.LeafLength * sizeFactor,
                    Width = c.LeafWidth * sizeFactor,
                    Thickness = 0.0016f,
                    Shape = LeafShape.Cordate,
                    LengthSegments = lengthSegments,
                    HalfWidthSegments = widthSegments,
                    InitialPitch = ProceduralMeshUtility.Range(rng, 0.1f, 0.3f),
                    TotalBend = -(0.1f + 0.4f * (1f - order)),
                    Cup = ProceduralMeshUtility.Range(rng, 0.05f, 0.12f),
                    Ripple = 0.006f,
                    LobeDepth = ProceduralMeshUtility.Range(rng, 0.2f, 0.26f),
                    Maturity = maturity,
                    Fenestrate = false
                };
                int variant = rng.Next(PlantBuildContext.LeafVariantCount);
                LeafMeshGenerator.Build(c.Data, leaf, rng, Matrix4x4.TRS(p3, rotation, Vector3.one), variant);
            }

            // Flowers: only on a maturing plant.
            int flowers = Mathf.RoundToInt(flowerCount * Mathf.Clamp01((g - 0.5f) / 0.4f));
            for (int f = 0; f < flowers; f++) AddFlower(c, rng, azimuth + 0.7f + f * goldenAngle, sg);
        }

        private void AddFlower(PlantBuildContext c, System.Random rng, float angle, float sg)
        {
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float stalkHeight = c.Height * ProceduralMeshUtility.Range(rng, 1.05f, 1.4f) * sg;
            Vector3 baseP = outward * 0.012f;
            Vector3 top = baseP + outward * (c.Width * ProceduralMeshUtility.Range(rng, 0.1f, 0.22f)) + Vector3.up * stalkHeight;

            List<Vector3> stalk = ProceduralMeshUtility.SampleBezier(
                baseP, baseP + Vector3.up * stalkHeight * 0.5f, top - Vector3.up * stalkHeight * 0.2f - outward * 0.02f, top, 8);
            StemMeshGenerator.AddTube(c.Data, stalk, 0.0035f, 0.0028f, 4, PlantBuildContext.StemSubmesh, false, false);

            // The spathe is a waxy, cupped heart facing up and out. Its tip points away from the plant.
            float scale = spatheLength / 0.17f;
            float elevation = ProceduralMeshUtility.Range(rng, 0.7f, 1.1f);
            Vector3 forward = (outward * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation)).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 normal = Vector3.Cross(forward, right);
            Quaternion rotation = Quaternion.AngleAxis(ProceduralMeshUtility.Signed(rng, 15f), forward) * Quaternion.LookRotation(normal, forward);

            var spathe = new LeafSettings
            {
                Length = spatheLength,
                Width = spatheLength * 0.92f,
                Thickness = 0.0025f,
                Shape = LeafShape.Cordate,
                LengthSegments = 10,
                HalfWidthSegments = 4,
                InitialPitch = 0.05f,
                TotalBend = -0.35f,
                Cup = 0.28f,
                Ripple = 0.01f,
                LobeDepth = 0.3f,
                Maturity = 1f,
                Fenestrate = false
            };
            LeafMeshGenerator.Build(c.Data, spathe, rng, Matrix4x4.TRS(top, rotation, Vector3.one), PlantBuildContext.AccentSubmesh);

            // The spadix: a slim, curved, cream-yellow spike standing out of the spathe's base.
            Vector3 spadixDir = (forward * 0.55f + normal * 0.75f).normalized;
            float spadixLength = 0.1f * scale;
            Vector3 s0 = top + normal * (0.012f * scale);
            Vector3 s3 = s0 + spadixDir * spadixLength;
            List<Vector3> spadix = ProceduralMeshUtility.SampleBezier(
                s0, s0 + spadixDir * spadixLength * 0.4f, s0 + spadixDir * spadixLength * 0.75f + forward * spadixLength * 0.1f, s3, 6);
            StemMeshGenerator.AddTube(c.Data, spadix, 0.0075f * scale, 0.0025f * scale, 6, PlantBuildContext.Accent2Submesh, false, true);
        }
    }
}
