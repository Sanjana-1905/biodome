using System.Collections.Generic;
using BioDome.Procedural;
using UnityEngine;

namespace BioDome.Plants
{
    public enum GroundCoverStyle
    {
        /// <summary>A dense, bumpy cushion of rounded green mounds with tiny spore stalks (moss).</summary>
        MossCushion = 0,
        /// <summary>A low carpet of wiry creeping stems dotted with tiny round leaves (baby's tears).</summary>
        Creeping = 1
    }

    /// <summary>
    /// Procedural low ground cover, built as ONE flat patch per object (not hundreds of plants). Two styles:
    /// a moss cushion (merged rounded mounds) and a creeping carpet of tiny round leaves. The patch spreads outwards and fills
    /// in as it grows. The patch width (Width) is the footprint; the patch is only a few centimetres tall.
    /// Base fields are reused as: Leaf Count = mounds (moss) or leaves (creeping).
    /// </summary>
    public class GroundCoverGenerator : PlantGenerator
    {
        [Header("Ground Cover")]
        [SerializeField] private GroundCoverStyle style = GroundCoverStyle.MossCushion;

        protected override int AlgorithmVersion => 1;

        public GroundCoverStyle Style => style;

        /// <summary>Chooses the style and loads that style's default size and colour (used when the prefab is created).</summary>
        public void SetStyle(GroundCoverStyle newStyle)
        {
            style = newStyle;
            ApplyDefaults();
        }

        protected override void ApplyDefaults()
        {
            if (style == GroundCoverStyle.MossCushion)
            {
                height = 0.09f;
                width = 0.9f;
                leafCount = 90;
                leafLength = 0.085f;
                leafWidth = 0.085f;
                leafColor = new Color(0.20f, 0.42f, 0.10f);
                colorVariation = 0.6f;
            }
            else
            {
                height = 0.06f;
                width = 0.8f;
                leafCount = 60; // leaf-bearing stems; each carries several leaves
                leafLength = 0.045f;
                leafWidth = 0.045f;
                leafColor = new Color(0.27f, 0.52f, 0.14f);
                colorVariation = 0.5f;
            }
        }

        private int Mix(int hash)
        {
            return ProceduralMeshUtility.Combine(hash, (int)style);
        }

        protected override int AppendVariantKey(int hash) => Mix(hash);
        protected override int ComputeHash() => Mix(base.ComputeHash());

        protected override void Build(PlantBuildContext c)
        {
            if (style == GroundCoverStyle.MossCushion) BuildCushion(c);
            else BuildCreeping(c);
        }

        private static void BuildCushion(PlantBuildContext c)
        {
            System.Random rng = c.Rng;
            float g = c.Growth;
            float spread = Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, g));
            float patchRadius = c.Width * 0.5f * spread;
            int mounds = Mathf.Max(6, Mathf.RoundToInt(c.LeafCount * Mathf.Lerp(0.35f, 1f, g)));
            float tall = Mathf.Clamp(c.Height / 0.09f, 0.5f, 2f);

            for (int i = 0; i < mounds; i++)
            {
                float angle = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);
                float radial = Mathf.Sqrt((float)rng.NextDouble()) * patchRadius;
                float edge = radial / Mathf.Max(patchRadius, 0.01f);

                // Bigger, taller mounds in the middle; small ones at the margin.
                float radius = c.LeafLength * Mathf.Lerp(0.7f, 1f, g) * Mathf.Lerp(1.15f, 0.55f, edge) * ProceduralMeshUtility.Range(rng, 0.8f, 1.2f);
                Vector3 center = new Vector3(Mathf.Cos(angle) * radial, radius * 0.1f, Mathf.Sin(angle) * radial);

                MeshShapes.AddSphere(c.Data, center, radius, new Vector3(1f, 0.5f * tall, 1f), 4, 8, rng, 0.2f,
                    rng.Next(PlantBuildContext.LeafVariantCount));
            }

            // Tiny spore stalks poking up between the mounds.
            int stalks = Mathf.RoundToInt(14 * Mathf.Lerp(0.3f, 1f, g));
            for (int s = 0; s < stalks; s++)
            {
                float angle = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);
                float radial = Mathf.Sqrt((float)rng.NextDouble()) * patchRadius * 0.9f;
                Vector3 p = new Vector3(Mathf.Cos(angle) * radial, c.LeafLength * 0.3f * tall, Mathf.Sin(angle) * radial);
                Vector3 lean = new Vector3(ProceduralMeshUtility.Signed(rng, 0.012f), 0f, ProceduralMeshUtility.Signed(rng, 0.012f));
                float length = ProceduralMeshUtility.Range(rng, 0.02f, 0.035f) * tall;
                var stalk = new List<Vector3> { p, p + lean * 0.5f + Vector3.up * (length * 0.5f), p + lean + Vector3.up * length };
                StemMeshGenerator.AddTube(c.Data, stalk, 0.0016f, 0.0012f, 3, 1, false, true); // leaf-variant submesh: takes the moss material and tint
            }
        }

        private static void BuildCreeping(PlantBuildContext c)
        {
            System.Random rng = c.Rng;
            float g = c.Growth;
            float spread = Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, g));
            float patchRadius = c.Width * 0.5f * spread;
            int stems = Mathf.Max(5, Mathf.RoundToInt(c.LeafCount * 0.25f * Mathf.Lerp(0.4f, 1f, g)));
            int leavesPerStem = Mathf.Max(3, Mathf.RoundToInt(c.LeafCount / (float)Mathf.Max(1, c.LeafCount / 10) * Mathf.Lerp(0.5f, 1f, g)));
            float leafRadius = c.LeafLength * Mathf.Lerp(0.6f, 1f, g);

            const float goldenAngle = 2.39996323f;
            float azimuth = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);
            for (int s = 0; s < stems; s++)
            {
                float angle = azimuth + s * goldenAngle;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float length = patchRadius * ProceduralMeshUtility.Range(rng, 0.6f, 1f);

                // A wandering ground-hugging stem.
                var path = new List<Vector3>();
                Vector3 p = Vector3.up * 0.004f;
                path.Add(p);
                for (int k = 1; k <= 4; k++)
                {
                    direction = Quaternion.AngleAxis(ProceduralMeshUtility.Signed(rng, 28f), Vector3.up) * direction;
                    p += direction * (length / 4f);
                    p.y = 0.004f + ProceduralMeshUtility.Range(rng, 0f, 0.012f);
                    path.Add(p);
                }
                StemMeshGenerator.AddTube(c.Data, path, 0.0016f, 0.001f, 3, PlantBuildContext.StemSubmesh, false, true);

                for (int l = 0; l < leavesPerStem; l++)
                {
                    float t = (l + 0.5f) / leavesPerStem;
                    Vector3 point = ProceduralMeshUtility.PointOnPath(path, t);
                    Vector3 offset = new Vector3(ProceduralMeshUtility.Signed(rng, 0.018f), 0.006f, ProceduralMeshUtility.Signed(rng, 0.018f));
                    Vector3 normal = (Vector3.up + new Vector3(ProceduralMeshUtility.Signed(rng, 0.35f), 0f, ProceduralMeshUtility.Signed(rng, 0.35f))).normalized;
                    float radius = leafRadius * ProceduralMeshUtility.Range(rng, 0.7f, 1.2f) * Mathf.Lerp(1f, 0.7f, t);
                    PlantParts.AddRoundLeaf(c.Data, point + offset, normal, radius, 0.25f, rng, rng.Next(PlantBuildContext.LeafVariantCount));
                }
            }
        }
    }
}
