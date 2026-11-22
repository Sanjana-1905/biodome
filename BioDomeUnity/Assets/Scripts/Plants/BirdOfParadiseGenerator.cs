using System.Collections.Generic;
using BioDome.Procedural;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Procedural Bird of Paradise (Strelitzia): no trunk, just a stiff FAN of long bare petioles that rise almost vertically,
    /// each carrying one large, leathery, blue-green paddle blade whose broad face lies in the plane of the fan, like a hand fan.
    /// Leaves alternate left and right in two ranks; outer ones lean further, the newest central leaves stand tallest.
    /// A mature plant also shows crest flowers: an orange fan of sepals and two blue tongues on a green boat-shaped bract,
    /// held on tall stalks. Growth below 1 gives fewer, shorter, smaller leaves and no flowers.
    /// </summary>
    public class BirdOfParadiseGenerator : PlantGenerator
    {
        [Header("Bird of Paradise")]
        [SerializeField, Range(0f, 1f), Tooltip("1 = a perfectly flat two-ranked fan, 0 = leaves spread in all directions.")]
        private float fanFlatness = 0.85f;
        [SerializeField, Range(10f, 80f), Tooltip("How far the outermost leaves lean from vertical, in degrees.")]
        private float fanSpreadDegrees = 38f;
        [SerializeField, Range(0f, 1.5f), Tooltip("Multiplier for how much blade tips arch outwards.")]
        private float leafDroop = 0.5f;
        [SerializeField, Min(0.002f)] private float petioleRadius = 0.012f;
        [SerializeField, Range(0, 4), Tooltip("Flower stalks on a fully mature plant.")]
        private int flowerStalks = 2;

        protected override int AlgorithmVersion => 2;

        protected override void ApplyDefaults()
        {
            height = 1.8f;
            width = 1.6f;
            leafCount = 9;
            leafLength = 0.8f;
            leafWidth = 0.3f;
            leafColor = new Color(0.12f, 0.38f, 0.26f); // blue-green, clearly different from Monstera
            colorVariation = 0.5f;
        }

        private int Mix(int hash)
        {
            hash = ProceduralMeshUtility.Combine(hash, fanFlatness);
            hash = ProceduralMeshUtility.Combine(hash, fanSpreadDegrees);
            hash = ProceduralMeshUtility.Combine(hash, leafDroop);
            hash = ProceduralMeshUtility.Combine(hash, petioleRadius);
            hash = ProceduralMeshUtility.Combine(hash, flowerStalks);
            return hash;
        }

        protected override int AppendVariantKey(int hash) => Mix(hash);
        protected override int ComputeHash() => Mix(base.ComputeHash());

        protected override void Build(PlantBuildContext c)
        {
            System.Random rng = c.Rng;
            float g = c.Growth;
            float sg = Mathf.Lerp(0.3f, 1f, Mathf.SmoothStep(0f, 1f, g)); // overall size factor
            int leaves = Mathf.Max(3, Mathf.RoundToInt(c.LeafCount * Mathf.Lerp(0.4f, 1f, g)));

            // The fan lies in the vertical plane containing this axis; blade faces point across it.
            float fanAngle = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);
            Vector3 axis = new Vector3(Mathf.Cos(fanAngle), 0f, Mathf.Sin(fanAngle));
            Vector3 across = new Vector3(-axis.z, 0f, axis.x);
            float spread = fanSpreadDegrees * Mathf.Deg2Rad;
            float jitter = (1f - fanFlatness) * Mathf.PI * 0.5f;

            int lengthSegments = Mathf.Max(10, Mathf.RoundToInt(c.LeafLengthSegments * 0.55f));
            int widthSegments = Mathf.Max(3, Mathf.RoundToInt(c.LeafHalfWidthSegments * 0.35f));

            var crowns = new List<Vector3>();
            for (int i = 0; i < leaves; i++)
            {
                float order = leaves > 1 ? i / (float)(leaves - 1) : 1f; // 0 = outer and oldest, 1 = central and newest
                float maturity = Mathf.Clamp01((g - 0.3f * order) / 0.7f);
                float sizeFactor = Mathf.Lerp(0.4f, 1f, Mathf.SmoothStep(0f, 1f, maturity))
                                   * ProceduralMeshUtility.Range(rng, 0.9f, 1.1f);

                // Two ranks: leaves alternate sides of the fan; outer leaves lean further.
                float side = (i % 2 == 0) ? 1f : -1f;
                float tilt = Mathf.Lerp(spread, 0.08f, Mathf.Pow(order, 0.9f)) * ProceduralMeshUtility.Range(rng, 0.85f, 1.12f);
                Vector3 lean = Quaternion.AngleAxis(ProceduralMeshUtility.Signed(rng, jitter) * Mathf.Rad2Deg, Vector3.up) * (axis * side);

                float petioleLength = c.Height * 0.52f * sg * Mathf.Lerp(0.75f, 1.12f, order) * ProceduralMeshUtility.Range(rng, 0.92f, 1.08f);
                float reach = Mathf.Sin(tilt) * petioleLength;
                float rise = Mathf.Cos(tilt) * petioleLength;

                // All petioles leave a tight crown at ground level.
                Vector3 p0 = across * ProceduralMeshUtility.Signed(rng, 0.045f * c.Width * sg) + axis * ProceduralMeshUtility.Signed(rng, 0.03f);
                Vector3 p3 = p0 + Vector3.up * rise + lean * reach;
                Vector3 p1 = p0 + Vector3.up * (rise * 0.45f);
                Vector3 p2 = p0 + Vector3.up * (rise * 0.85f) + lean * (reach * 0.55f);
                if (i == 0) crowns.Add(p3);

                List<Vector3> petiole = ProceduralMeshUtility.SampleBezier(p0, p1, p2, p3, 8);
                float radius = petioleRadius * Mathf.Lerp(0.7f, 1.1f, sg);
                StemMeshGenerator.AddTube(c.Data, petiole, radius * 1.7f, radius * 0.75f, 6, PlantBuildContext.StemSubmesh, false, false);

                // The blade continues the petiole, flaring slightly outwards, its broad face in the fan plane.
                Vector3 forward = ((p3 - p2).normalized + lean * 0.3f).normalized;
                Vector3 normal = across * 0.92f - lean * 0.4f;
                normal -= forward * Vector3.Dot(normal, forward);
                if (normal.sqrMagnitude < 1e-4f) normal = across;
                normal.Normalize();
                float roll = ProceduralMeshUtility.Signed(rng, 9f);
                Quaternion rotation = Quaternion.AngleAxis(roll, forward) * Quaternion.LookRotation(normal, forward);

                var leaf = new LeafSettings
                {
                    Length = c.LeafLength * sizeFactor,
                    Width = c.LeafWidth * sizeFactor,
                    Thickness = 0.0015f, // two faces, so the underside is lit correctly
                    Shape = LeafShape.Lanceolate,
                    LengthSegments = lengthSegments,
                    HalfWidthSegments = widthSegments,
                    InitialPitch = ProceduralMeshUtility.Range(rng, 0.0f, 0.08f),
                    TotalBend = -leafDroop * Mathf.Lerp(0.3f, 0.9f, 1f - order) * Mathf.Lerp(0.7f, 1f, maturity),
                    Cup = ProceduralMeshUtility.Range(rng, 0.05f, 0.1f),
                    Ripple = 0.005f,
                    LobeDepth = 0f,
                    Maturity = maturity,
                    Fenestrate = false
                };

                int variant = rng.Next(PlantBuildContext.LeafVariantCount);
                LeafMeshGenerator.Build(c.Data, leaf, rng, Matrix4x4.TRS(p3, rotation, Vector3.one), variant);
            }

            // Crest flowers on tall stalks, only on a mature plant.
            int stalks = Mathf.RoundToInt(flowerStalks * Mathf.Clamp01((g - 0.62f) / 0.3f) * c.Bloom);
            for (int s = 0; s < stalks; s++)
            {
                float stalkAngle = fanAngle + (s - (stalks - 1) * 0.5f) * 0.9f + ProceduralMeshUtility.Signed(rng, 0.3f);
                Vector3 heading = new Vector3(Mathf.Cos(stalkAngle), 0f, Mathf.Sin(stalkAngle));
                AddFlower(c, rng, heading, sg, s);
            }
        }

        private void AddFlower(PlantBuildContext c, System.Random rng, Vector3 heading, float sg, int index)
        {
            float stalkHeight = c.Height * ProceduralMeshUtility.Range(rng, 0.84f, 0.98f) * sg;
            Vector3 baseP = new Vector3(heading.x * 0.03f * index, 0f, heading.z * 0.03f * index);
            Vector3 top = baseP + heading * (c.Width * 0.08f) + Vector3.up * stalkHeight;
            List<Vector3> stalk = ProceduralMeshUtility.SampleBezier(
                baseP, baseP + Vector3.up * stalkHeight * 0.4f,
                top - Vector3.up * stalkHeight * 0.25f - heading * 0.02f, top, 8);
            StemMeshGenerator.AddTube(c.Data, stalk, 0.01f, 0.007f, 5, PlantBuildContext.StemSubmesh, false, false);

            // Boat-shaped bract lying nearly horizontal at the top of the stalk.
            float beak = 0.24f * Mathf.Clamp(c.Height / 1.8f, 0.7f, 1.4f);
            Vector3 bractEnd = top + heading * beak + Vector3.up * (beak * 0.1f);
            List<Vector3> bract = ProceduralMeshUtility.SampleBezier(
                top, top + heading * beak * 0.35f + Vector3.up * 0.02f, top + heading * beak * 0.75f, bractEnd, 6);
            StemMeshGenerator.AddTube(c.Data, bract, 0.03f, 0.005f, 6, PlantBuildContext.StemSubmesh, false, true);

            Vector3 sideAxis = Vector3.Cross(Vector3.up, heading).normalized;
            Vector3 origin = top + heading * (beak * 0.2f) + Vector3.up * 0.012f;

            // Three orange sepals fanning upward and forward, two blue tongues beside them.
            for (int k = 0; k < 3; k++)
            {
                float yaw = (k - 1) * 0.28f;
                float elevation = 0.55f + (k == 1 ? 0.18f : 0f);
                AddPetal(c, rng, origin, heading, sideAxis, yaw, elevation, 0.3f * c.Height / 1.8f, 0.06f, PlantBuildContext.AccentSubmesh);
            }
            for (int k = 0; k < 2; k++)
            {
                float yaw = (k == 0 ? -1f : 1f) * 0.12f;
                AddPetal(c, rng, origin + Vector3.down * 0.005f, heading, sideAxis, yaw, 0.2f, 0.25f * c.Height / 1.8f, 0.045f, PlantBuildContext.Accent2Submesh);
            }
        }

        private static void AddPetal(PlantBuildContext c, System.Random rng, Vector3 origin, Vector3 heading, Vector3 sideAxis,
            float yaw, float elevation, float length, float width, int submesh)
        {
            Vector3 direction = Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.up) * heading;
            Vector3 forward = (direction * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation)).normalized;
            Vector3 normal = Vector3.Cross(forward, Vector3.Cross(Vector3.up, direction)).normalized; // faces sideways-up
            if (normal.sqrMagnitude < 1e-4f) normal = sideAxis;
            Quaternion rotation = Quaternion.LookRotation(sideAxis, forward);

            var petal = new LeafSettings
            {
                Length = length,
                Width = width,
                Thickness = 0.001f,
                Shape = LeafShape.Lanceolate,
                LengthSegments = 6,
                HalfWidthSegments = 2,
                InitialPitch = 0f,
                TotalBend = -0.25f,
                Cup = 0.2f,
                Ripple = 0f,
                Maturity = 1f
            };
            LeafMeshGenerator.Build(c.Data, petal, rng, Matrix4x4.TRS(origin, rotation, Vector3.one), submesh);
        }
    }
}
