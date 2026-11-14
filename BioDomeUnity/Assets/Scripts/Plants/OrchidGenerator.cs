using System.Collections.Generic;
using BioDome.Procedural;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Procedural moth orchid (Phalaenopsis): a low rosette of a few broad, thick, glossy strap leaves that arch out to
    /// alternate sides, with one or two slender flower SPIKES that rise from the centre and arch over, carrying a row of
    /// flat five-petalled blooms (two big round wing petals, three narrower sepals, and a small lip in a second colour).
    /// Spikes and flowers appear on a maturing plant; a young plant is just a few small leaves.
    /// </summary>
    public class OrchidGenerator : PlantGenerator
    {
        [Header("Orchid")]
        [SerializeField, Range(0, 3), Tooltip("Flower spikes on a fully mature plant.")]
        private int spikeCount = 2;
        [SerializeField, Range(3, 12), Tooltip("Flowers on a fully grown spike.")]
        private int flowersPerSpike = 7;
        [SerializeField, Min(0.02f), Tooltip("Flower diameter in metres.")]
        private float flowerSize = 0.055f;

        protected override int AlgorithmVersion => 1;

        protected override void ApplyDefaults()
        {
            height = 0.5f;
            width = 0.42f;
            leafCount = 6;
            leafLength = 0.4f;
            leafWidth = 0.125f;
            leafColor = new Color(0.09f, 0.33f, 0.14f);
            colorVariation = 0.35f;
        }

        private int Mix(int hash)
        {
            hash = ProceduralMeshUtility.Combine(hash, spikeCount);
            hash = ProceduralMeshUtility.Combine(hash, flowersPerSpike);
            hash = ProceduralMeshUtility.Combine(hash, flowerSize);
            return hash;
        }

        protected override int AppendVariantKey(int hash) => Mix(hash);
        protected override int ComputeHash() => Mix(base.ComputeHash());

        protected override void Build(PlantBuildContext c)
        {
            System.Random rng = c.Rng;
            float g = c.Growth;
            float sg = Mathf.Lerp(0.4f, 1f, Mathf.SmoothStep(0f, 1f, g));
            int leaves = Mathf.Max(3, Mathf.RoundToInt(c.LeafCount * Mathf.Lerp(0.55f, 1f, g)));

            // Leaves alternate left and right along one axis, like a real moth orchid.
            float axisAngle = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);
            Vector3 axis = new Vector3(Mathf.Cos(axisAngle), 0f, Mathf.Sin(axisAngle));
            Vector3 across = new Vector3(-axis.z, 0f, axis.x);

            for (int i = 0; i < leaves; i++)
            {
                float order = leaves > 1 ? i / (float)(leaves - 1) : 1f; // 0 = oldest and lowest, 1 = newest
                float maturity = Mathf.Clamp01((g - 0.3f * order) / 0.7f);
                float sizeFactor = Mathf.Lerp(0.45f, 1f, Mathf.SmoothStep(0f, 1f, maturity))
                                   * Mathf.Lerp(1f, 0.8f, order) * ProceduralMeshUtility.Range(rng, 0.9f, 1.1f);

                float side = (i % 2 == 0) ? 1f : -1f;
                Vector3 outward = (axis * side + across * ProceduralMeshUtility.Signed(rng, 0.3f)).normalized;
                float pitch = Mathf.Lerp(0.12f, 0.38f, order) + ProceduralMeshUtility.Signed(rng, 0.06f);
                Vector3 forward = (outward * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 normal = Vector3.Cross(forward, right);
                Quaternion rotation = Quaternion.AngleAxis(ProceduralMeshUtility.Signed(rng, 8f), forward) * Quaternion.LookRotation(normal, forward);

                Vector3 origin = outward * 0.012f + Vector3.up * (0.02f * order);
                var leaf = new LeafSettings
                {
                    Length = c.LeafLength * sizeFactor,
                    Width = c.LeafWidth * sizeFactor,
                    Thickness = 0.003f,
                    Shape = LeafShape.Ovate,
                    LengthSegments = 10,
                    HalfWidthSegments = 3,
                    InitialPitch = ProceduralMeshUtility.Range(rng, 0.05f, 0.2f),
                    TotalBend = -ProceduralMeshUtility.Range(rng, 0.35f, 0.6f),
                    Cup = ProceduralMeshUtility.Range(rng, 0.1f, 0.2f),
                    Ripple = 0.004f,
                    LobeDepth = 0f,
                    Maturity = maturity,
                    Fenestrate = false
                };
                int variant = rng.Next(PlantBuildContext.LeafVariantCount);
                LeafMeshGenerator.Build(c.Data, leaf, rng, Matrix4x4.TRS(origin, rotation, Vector3.one), variant);
            }

            int spikes = Mathf.RoundToInt(spikeCount * Mathf.Clamp01((g - 0.45f) / 0.3f));
            for (int s = 0; s < spikes; s++)
            {
                float heading = axisAngle + Mathf.PI * 0.5f + (s - (spikes - 1) * 0.5f) * 0.9f + ProceduralMeshUtility.Signed(rng, 0.25f);
                AddSpike(c, rng, new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading)), sg);
            }
        }

        private void AddSpike(PlantBuildContext c, System.Random rng, Vector3 heading, float sg)
        {
            float spikeLength = c.Height * ProceduralMeshUtility.Range(rng, 1.15f, 1.4f) * sg;
            Vector3 start = Vector3.up * 0.03f;
            List<Vector3> path = PlantParts.ArchPath(start, heading, 1.45f, 2.1f, spikeLength, 12);
            StemMeshGenerator.AddTube(c.Data, path, 0.0032f, 0.0018f, 4, PlantBuildContext.StemSubmesh, false, true);

            Vector3 side = Vector3.Cross(Vector3.up, heading).normalized;
            float bloomScale = Mathf.Clamp01((c.Growth - 0.5f) / 0.4f);
            int flowers = Mathf.Max(0, Mathf.RoundToInt(flowersPerSpike * bloomScale));
            for (int k = 0; k < flowers; k++)
            {
                float t = Mathf.Lerp(0.5f, 0.98f, flowers > 1 ? k / (float)(flowers - 1) : 1f);
                Vector3 p = ProceduralMeshUtility.PointOnPath(path, t);
                float sign = (k % 2 == 0) ? 1f : -1f;
                Vector3 facing = (side * sign + heading * 0.25f).normalized; // blooms face out to alternate sides
                float size = flowerSize * Mathf.Lerp(1f, 0.55f, t * t) * bloomScale * ProceduralMeshUtility.Range(rng, 0.92f, 1.08f);
                AddFlower(c, rng, p + facing * 0.006f, facing, size);
            }
        }

        // Petal roles around a bloom: angle from "up" in the flower plane, length factor, width factor, is the lip.
        private static readonly float[] PetalAngle = { 0f, 80f, -80f, 150f, -150f, 180f };
        private static readonly float[] PetalLength = { 0.85f, 1.0f, 1.0f, 0.85f, 0.85f, 0.5f };
        private static readonly float[] PetalWidth = { 0.5f, 1.05f, 1.05f, 0.5f, 0.5f, 0.4f };

        private static void AddFlower(PlantBuildContext c, System.Random rng, Vector3 center, Vector3 facing, float size)
        {
            for (int p = 0; p < PetalAngle.Length; p++)
            {
                Vector3 up = Quaternion.AngleAxis(PetalAngle[p], facing) * Vector3.up;
                Quaternion rotation = Quaternion.LookRotation(facing, up);
                bool lip = p == PetalAngle.Length - 1;

                var petal = new LeafSettings
                {
                    Length = size * PetalLength[p],
                    Width = size * PetalWidth[p],
                    Thickness = 0.0008f,
                    Shape = LeafShape.Ovate,
                    LengthSegments = 3,
                    HalfWidthSegments = 1,
                    InitialPitch = 0f,
                    TotalBend = -0.2f,
                    Cup = 0.22f,
                    Ripple = 0f,
                    LobeDepth = 0f,
                    Maturity = 1f
                };
                LeafMeshGenerator.Build(c.Data, petal, rng, Matrix4x4.TRS(center + facing * (lip ? 0.002f : 0f), rotation, Vector3.one),
                    lip ? PlantBuildContext.Accent2Submesh : PlantBuildContext.AccentSubmesh);
            }
        }
    }
}
