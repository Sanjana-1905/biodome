using System.Collections.Generic;
using BioDome.Procedural;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Procedural Calathea: a dense, upright ROSETTE of broad, oval, slightly wavy leaves on slender petioles, held like a
    /// bouquet. Outer leaves lean out and sit lower, inner leaves stand nearly vertical. The leaves are drawn with a
    /// striped herringbone pattern (a generated texture on the Calathea material), which is what makes the species recognisable.
    /// A young plant is a small tuft of a few short oval leaves.
    /// </summary>
    public class CalatheaGenerator : PlantGenerator
    {
        [Header("Calathea")]
        [SerializeField, Range(20f, 80f), Tooltip("How far the outermost leaves lean from vertical, in degrees.")]
        private float outerTiltDegrees = 58f;
        [SerializeField, Min(0.001f)] private float petioleRadius = 0.0045f;

        protected override int AlgorithmVersion => 1;

        protected override void ApplyDefaults()
        {
            height = 0.7f;
            width = 0.8f;
            leafCount = 12;
            leafLength = 0.32f;
            leafWidth = 0.18f;
            leafColor = new Color(0.95f, 1f, 0.95f); // near white: the colour comes from the pattern texture
            colorVariation = 0.3f;
        }

        private int Mix(int hash)
        {
            hash = ProceduralMeshUtility.Combine(hash, outerTiltDegrees);
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
            int leaves = Mathf.Max(4, Mathf.RoundToInt(c.LeafCount * Mathf.Lerp(0.4f, 1f, g)));

            int lengthSegments = Mathf.Max(8, Mathf.RoundToInt(c.LeafLengthSegments * 0.4f));
            int widthSegments = Mathf.Max(3, Mathf.RoundToInt(c.LeafHalfWidthSegments * 0.3f));

            const float goldenAngle = 2.39996323f;
            float azimuth = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);
            float outerTilt = outerTiltDegrees * Mathf.Deg2Rad;

            for (int i = 0; i < leaves; i++)
            {
                float order = leaves > 1 ? i / (float)(leaves - 1) : 1f; // 0 = outer and oldest, 1 = central and newest
                float maturity = Mathf.Clamp01((g - 0.35f * order) / 0.65f);
                float sizeFactor = Mathf.Lerp(0.4f, 1f, Mathf.SmoothStep(0f, 1f, maturity))
                                   * Mathf.Lerp(1f, 0.82f, order) * ProceduralMeshUtility.Range(rng, 0.9f, 1.1f);

                float az = azimuth + i * goldenAngle + ProceduralMeshUtility.Signed(rng, 0.2f);
                Vector3 outward = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));

                float tilt = Mathf.Lerp(outerTilt, 0.2f, Mathf.Pow(order, 0.9f)) * ProceduralMeshUtility.Range(rng, 0.85f, 1.1f);
                float petioleLength = c.Height * 0.46f * sg * Mathf.Lerp(0.55f, 1f, order) * ProceduralMeshUtility.Range(rng, 0.9f, 1.1f);

                Vector3 p0 = outward * (0.015f * (1f - order) + 0.005f);
                Vector3 axisDir = Vector3.up * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt);
                Vector3 p3 = p0 + axisDir * petioleLength;
                Vector3 p1 = p0 + Vector3.up * (petioleLength * 0.5f);
                Vector3 p2 = Vector3.Lerp(p1, p3, 0.6f) + outward * (petioleLength * 0.06f);
                List<Vector3> petiole = ProceduralMeshUtility.SampleBezier(p0, p1, p2, p3, 6);
                float radius = petioleRadius * Mathf.Lerp(0.7f, 1.1f, sg);
                StemMeshGenerator.AddTube(c.Data, petiole, radius * 1.3f, radius * 0.8f, 5, PlantBuildContext.StemSubmesh, false, false);

                // Blade: carries on from the petiole, leaning a little further out; its face looks outwards.
                float bladeTilt = tilt + 0.3f;
                Vector3 forward = (Vector3.up * Mathf.Cos(bladeTilt) + outward * Mathf.Sin(bladeTilt)).normalized;
                Vector3 normal = (Vector3.up * Mathf.Sin(bladeTilt) - outward * Mathf.Cos(bladeTilt)).normalized;
                float roll = ProceduralMeshUtility.Signed(rng, 14f);
                Quaternion rotation = Quaternion.AngleAxis(roll, forward) * Quaternion.LookRotation(normal, forward);

                var leaf = new LeafSettings
                {
                    Length = c.LeafLength * sizeFactor,
                    Width = c.LeafWidth * sizeFactor,
                    Thickness = 0.0015f,
                    Shape = LeafShape.Ovate,
                    LengthSegments = lengthSegments,
                    HalfWidthSegments = widthSegments,
                    InitialPitch = ProceduralMeshUtility.Range(rng, 0.02f, 0.12f),
                    TotalBend = -(0.25f + 0.45f * (1f - order)) * Mathf.Lerp(0.7f, 1f, maturity),
                    Cup = ProceduralMeshUtility.Range(rng, 0.08f, 0.16f),
                    Ripple = 0.014f, // the wavy margin is characteristic
                    LobeDepth = 0f,
                    Maturity = maturity,
                    Fenestrate = false
                };

                int variant = rng.Next(PlantBuildContext.LeafVariantCount);
                LeafMeshGenerator.Build(c.Data, leaf, rng, Matrix4x4.TRS(p3, rotation, Vector3.one), variant);
            }
        }
    }
}
