using System;
using System.Collections.Generic;
using UnityEngine;

namespace BioDome.Procedural
{
    public enum LeafShape
    {
        /// <summary>Egg shaped, pointed base and tip.</summary>
        Ovate = 0,
        /// <summary>Heart shaped with rounded basal lobes (Monstera).</summary>
        Cordate = 1,
        /// <summary>Long and narrow (ferns, palm leaflets, willow-like leaves).</summary>
        Lanceolate = 2
    }

    /// <summary>Parameters for one leaf. Lengths are in metres, angles in radians.</summary>
    public sealed class LeafSettings
    {
        public float Length = 0.5f;
        public float Width = 0.42f;
        /// <summary>Blade thickness. 0 makes a single surface (use a two-sided material).</summary>
        public float Thickness = 0.003f;
        public LeafShape Shape = LeafShape.Cordate;

        public int LengthSegments = 40;
        public int HalfWidthSegments = 14;

        /// <summary>Elevation of the blade at its base, relative to the leaf plane.</summary>
        public float InitialPitch = 0.25f;
        /// <summary>Total change in pitch along the blade; negative droops the tip.</summary>
        public float TotalBend = -0.9f;
        /// <summary>Edge lift relative to the midrib, as a fraction of half-width (cupping).</summary>
        public float Cup = 0.15f;
        /// <summary>Wavy margin amplitude as a fraction of length.</summary>
        public float Ripple = 0.012f;
        /// <summary>How far the basal lobes extend behind the stalk, as a fraction of length (cordate only).</summary>
        public float LobeDepth = 0.16f;

        /// <summary>0 = young leaf, 1 = fully mature. Controls how developed the splits and holes are.</summary>
        public float Maturity = 1f;
        public bool Fenestrate = false;
        /// <summary>Splits per side, 0 to 7.</summary>
        public int SplitCount = 7;
        /// <summary>Holes per side, 0 to 7.</summary>
        public int HoleCount = 6;
    }

    /// <summary>
    /// Procedural leaf blades built from a parametric (v along the midrib, s across) grid.
    /// The surface has a central midrib groove, cupping, a drooping arc, a rippled margin and optional thickness.
    /// Monstera-style splits and holes are cut from the grid, following the lateral vein direction used by the leaf texture.
    /// UVs: u = 0..1 across the blade (0.5 on the midrib), v = 0..1 from base to tip.
    /// </summary>
    public static class LeafMeshGenerator
    {
        /// <summary>Lateral veins per side painted by the leaf texture; splits sit between them.</summary>
        public const int LateralVeinCount = 8;
        /// <summary>How far (in v) a lateral vein advances towards the tip between the midrib and the margin.</summary>
        public const float VeinSlant = 0.16f;

        /// <summary>
        /// Builds one leaf and appends it to <paramref name="target"/>. The leaf is created with its base at the
        /// origin, X across the blade, Y along it and Z as the upward normal, then moved by <paramref name="transform"/>.
        /// </summary>
        public static void Build(MeshData target, LeafSettings settings, System.Random rng, Matrix4x4 transform, int submesh = 0)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var local = new MeshData();
            new Builder(local, settings, rng, submesh).Run();
            target.Append(local, transform);
        }

        // --- Fenestration data ----------------------------------------------------

        private struct Slit { public float V, Depth, W0, W1; }
        private struct Hole { public float V, T, Rv, Rt; }

        private sealed class Fenestration
        {
            public readonly Slit[][] Slits = { new Slit[0], new Slit[0] };
            public readonly Hole[][] Holes = { new Hole[0], new Hole[0] };

            public static Fenestration Create(LeafSettings s, System.Random rng)
            {
                var f = new Fenestration();
                float m = s.Maturity;
                if (m < 0.35f) return f; // juvenile leaves are entire

                float grown = Mathf.SmoothStep(0f, 1f, (m - 0.35f) / 0.55f);
                float depthStart = Mathf.Lerp(0.72f, 0.26f, grown);

                int splitCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(s.SplitCount, 7) * grown), 0, 7);
                int holeCount = m > 0.6f ? Mathf.Clamp(s.HoleCount, 0, 7) : 0;

                for (int side = 0; side < 2; side++)
                {
                    var slits = new Slit[splitCount];
                    for (int i = 0; i < splitCount; i++)
                    {
                        int b = BoundaryIndex(i, splitCount);
                        slits[i] = new Slit
                        {
                            V = b / (float)LateralVeinCount,
                            Depth = Mathf.Clamp(depthStart + ProceduralMeshUtility.Signed(rng, 0.05f), 0.2f, 0.9f),
                            W0 = 0.010f + ProceduralMeshUtility.Signed(rng, 0.003f),
                            W1 = 0.024f + ProceduralMeshUtility.Signed(rng, 0.006f)
                        };
                    }
                    f.Slits[side] = slits;

                    var holes = new Hole[holeCount];
                    for (int i = 0; i < holeCount; i++)
                    {
                        int b = BoundaryIndex(i, holeCount);
                        holes[i] = new Hole
                        {
                            V = b / (float)LateralVeinCount,
                            T = 0.13f + ProceduralMeshUtility.Signed(rng, 0.02f),
                            Rv = 0.03f * Mathf.Lerp(0.7f, 1f, m) + ProceduralMeshUtility.Signed(rng, 0.004f),
                            Rt = 0.085f
                        };
                    }
                    f.Holes[side] = holes;
                }
                return f;
            }

            /// <summary>Spreads <paramref name="count"/> items over the 7 gaps between the 8 lateral veins.</summary>
            private static int BoundaryIndex(int i, int count)
            {
                if (count <= 1) return 4;
                return 1 + Mathf.RoundToInt(i * 6f / (count - 1));
            }

            public bool IsSolid(float v, float t, int side)
            {
                Slit[] slits = Slits[side];
                for (int i = 0; i < slits.Length; i++)
                {
                    Slit sl = slits[i];
                    if (t < sl.Depth) continue;
                    float centre = sl.V + VeinSlant * t;
                    float halfWidth = sl.W0 + sl.W1 * ((t - sl.Depth) / Mathf.Max(0.05f, 1f - sl.Depth));
                    if (Mathf.Abs(v - centre) < halfWidth) return false;
                }

                Hole[] holes = Holes[side];
                for (int i = 0; i < holes.Length; i++)
                {
                    Hole h = holes[i];
                    float dv = (v - (h.V + VeinSlant * h.T)) / h.Rv;
                    float dt = (t - h.T) / h.Rt;
                    if (dv * dv + dt * dt < 1f) return false;
                }
                return true;
            }
        }

        // --- Leaf surface builder -------------------------------------------------

        private sealed class Builder
        {
            private readonly MeshData data;
            private readonly LeafSettings s;
            private readonly int submesh;

            private readonly int nv;
            private readonly int nw;
            private readonly float length;
            private readonly float halfWidth;
            private readonly float alpha0;
            private readonly float curvature;
            private readonly float thickness;
            private readonly float grooveDepth;
            private readonly float grooveWidth;
            private readonly float ripplePhase;
            private readonly float rippleFrequency;
            private readonly Fenestration fenestration;

            private readonly int[] topIndex;
            private readonly int[] bottomIndex;
            private readonly Vector3[] topPosition;
            private readonly Vector3[] topNormal;
            private readonly Vector3[] bottomPosition;
            private readonly bool[,] solid;

            public Builder(MeshData data, LeafSettings settings, System.Random rng, int submesh)
            {
                this.data = data;
                s = settings;
                this.submesh = submesh;

                nv = Mathf.Max(4, s.LengthSegments);
                nw = Mathf.Max(2, s.HalfWidthSegments) * 2;
                length = Mathf.Max(0.001f, s.Length);
                halfWidth = Mathf.Max(0.001f, s.Width) * 0.5f;
                alpha0 = s.InitialPitch;
                curvature = s.TotalBend / length;
                thickness = Mathf.Max(0f, s.Thickness);
                grooveDepth = thickness * 0.4f;
                grooveWidth = Mathf.Max(0.004f, halfWidth * 0.05f);
                ripplePhase = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);
                rippleFrequency = ProceduralMeshUtility.Range(rng, 3f, 5f);

                fenestration = s.Fenestrate && s.Shape == LeafShape.Cordate ? Fenestration.Create(s, rng) : null;

                int corners = (nv + 1) * (nw + 1);
                topIndex = NewIndexArray(corners);
                bottomIndex = NewIndexArray(corners);
                topPosition = new Vector3[corners];
                topNormal = new Vector3[corners];
                bottomPosition = new Vector3[corners];
                solid = new bool[nv, nw];
            }

            public void Run()
            {
                // Which grid cells exist (the rest are splits and holes).
                for (int i = 0; i < nv; i++)
                {
                    float v = (i + 0.5f) / nv;
                    for (int j = 0; j < nw; j++)
                    {
                        float sv = -1f + (j + 0.5f) * 2f / nw;
                        solid[i, j] = fenestration == null || fenestration.IsSolid(v, Mathf.Abs(sv), sv < 0f ? 0 : 1);
                    }
                }

                // Top surface.
                for (int i = 0; i < nv; i++)
                {
                    for (int j = 0; j < nw; j++)
                    {
                        if (!solid[i, j]) continue;
                        int a = TopVertex(i, j), b = TopVertex(i, j + 1), c = TopVertex(i + 1, j + 1), d = TopVertex(i + 1, j);
                        Vector3 want = topNormal[Corner(i, j)];
                        AddTriangle(a, b, c, topPosition[Corner(i, j)], topPosition[Corner(i, j + 1)], topPosition[Corner(i + 1, j + 1)], want);
                        AddTriangle(a, c, d, topPosition[Corner(i, j)], topPosition[Corner(i + 1, j + 1)], topPosition[Corner(i + 1, j)], want);
                    }
                }

                if (thickness <= 0f) return;

                // Underside, facing the opposite way.
                for (int i = 0; i < nv; i++)
                {
                    for (int j = 0; j < nw; j++)
                    {
                        if (!solid[i, j]) continue;
                        int a = BottomVertex(i, j), b = BottomVertex(i, j + 1), c = BottomVertex(i + 1, j + 1), d = BottomVertex(i + 1, j);
                        Vector3 want = -topNormal[Corner(i, j)];
                        AddTriangle(a, b, c, bottomPosition[Corner(i, j)], bottomPosition[Corner(i, j + 1)], bottomPosition[Corner(i + 1, j + 1)], want);
                        AddTriangle(a, c, d, bottomPosition[Corner(i, j)], bottomPosition[Corner(i + 1, j + 1)], bottomPosition[Corner(i + 1, j)], want);
                    }
                }

                // Edge strip joining top and underside wherever a solid cell borders empty space.
                for (int i = 0; i < nv; i++)
                {
                    for (int j = 0; j < nw; j++)
                    {
                        if (!solid[i, j]) continue;
                        if (!IsSolid(i + 1, j)) AddRim(i, j, i + 1, j, i + 1, j + 1, i, j);
                        if (!IsSolid(i - 1, j)) AddRim(i, j, i, j, i, j + 1, i, j);
                        if (!IsSolid(i, j + 1)) AddRim(i, j, i, j + 1, i + 1, j + 1, i, j);
                        if (!IsSolid(i, j - 1)) AddRim(i, j, i, j, i + 1, j, i, j);
                    }
                }
            }

            // --- Surface maths ----------------------------------------------------

            private static void Spine(float sigma, float alpha0, float k, out float y, out float z, out float sinA, out float cosA)
            {
                float a = alpha0 + k * sigma;
                sinA = Mathf.Sin(a);
                cosA = Mathf.Cos(a);
                if (Mathf.Abs(k) < 1e-4f)
                {
                    y = Mathf.Cos(alpha0) * sigma;
                    z = Mathf.Sin(alpha0) * sigma;
                }
                else
                {
                    y = (sinA - Mathf.Sin(alpha0)) / k;
                    z = -(cosA - Mathf.Cos(alpha0)) / k;
                }
            }

            /// <summary>Half-width of the blade as a 0-1 fraction at position v along the midrib.</summary>
            private float Outline(float v)
            {
                switch (s.Shape)
                {
                    case LeafShape.Cordate:
                    {
                        const float shoulder = 0.36f;
                        const float baseWidth = 0.5f;
                        if (v < shoulder) return baseWidth + (1f - baseWidth) * Mathf.Sin(v / shoulder * Mathf.PI * 0.5f);
                        float y = (v - shoulder) / (1f - shoulder);
                        return 1f - Mathf.Pow(y, 1.8f);
                    }
                    case LeafShape.Lanceolate:
                        return Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(v, 0.85f))), 0.7f);
                    default:
                        return Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(v, 0.75f))), 0.85f);
                }
            }

            /// <summary>Point on the top or underside at (v, s); <paramref name="up"/> receives the local upward direction.</summary>
            private Vector3 Surface(float v, float sv, bool underside, out Vector3 up)
            {
                float t = Mathf.Abs(sv);
                float f = Outline(v);
                float x = sv * halfWidth * f;

                float lobe = s.Shape == LeafShape.Cordate ? s.LobeDepth : 0f;
                float sigma = v * length - lobe * length * Mathf.Pow(t, 1.6f) * Mathf.Pow(1f - v, 2f);

                Spine(sigma, alpha0, curvature, out float y, out float z, out float sinA, out float cosA);

                float sf = sv * f;
                float offset = s.Cup * halfWidth * sf * sf;
                offset += s.Ripple * length * Mathf.Sin(sigma / length * rippleFrequency * Mathf.PI * 2f + ripplePhase) * t * t * f;

                float groove = Mathf.Exp(-(x * x) / (grooveWidth * grooveWidth));
                offset += underside ? (-thickness + grooveDepth * groove) : (-grooveDepth * groove);

                up = new Vector3(0f, -sinA, cosA);
                return new Vector3(x, y + up.y * offset, z + up.z * offset);
            }

            private Vector3 TopNormal(float v, float sv)
            {
                float dv = 0.5f / nv;
                float ds = 1f / nw;
                float v0 = Mathf.Max(0f, v - dv), v1 = Mathf.Min(1f, v + dv);
                float s0 = Mathf.Max(-1f, sv - ds), s1 = Mathf.Min(1f, sv + ds);

                Vector3 up;
                Vector3 alongV = Surface(v1, sv, false, out up) - Surface(v0, sv, false, out up);
                Vector3 alongS = Surface(v, s1, false, out up) - Surface(v, s0, false, out up);
                Surface(v, sv, false, out up);

                // Unit-length tangents first: Vector3.Normalize returns zero below 1e-5, and these raw
                // differences are only centimetres long, so their cross product would be zeroed.
                float lv = alongV.magnitude;
                float ls = alongS.magnitude;
                if (lv < 1e-9f || ls < 1e-9f) return up;

                Vector3 n = Vector3.Cross(alongV / lv, alongS / ls);
                float nl = n.magnitude;
                if (nl < 1e-3f) return up; // tangents (almost) parallel, e.g. at the pointed tip
                n /= nl;
                return Vector3.Dot(n, up) < 0f ? -n : n;
            }

            // --- Vertex and triangle helpers ----------------------------------------

            private int Corner(int i, int j)
            {
                return i * (nw + 1) + j;
            }

            private static int[] NewIndexArray(int count)
            {
                var array = new int[count];
                for (int i = 0; i < count; i++) array[i] = -1;
                return array;
            }

            private int TopVertex(int i, int j)
            {
                int k = Corner(i, j);
                if (topIndex[k] >= 0) return topIndex[k];

                float v = i / (float)nv;
                float sv = -1f + 2f * j / nw;
                Vector3 up;
                topPosition[k] = Surface(v, sv, false, out up);
                topNormal[k] = TopNormal(v, sv);
                topIndex[k] = data.AddVertex(topPosition[k], topNormal[k], new Vector2((sv + 1f) * 0.5f, v));
                return topIndex[k];
            }

            private int BottomVertex(int i, int j)
            {
                int k = Corner(i, j);
                if (bottomIndex[k] >= 0) return bottomIndex[k];

                TopVertex(i, j); // ensures the normal exists
                float v = i / (float)nv;
                float sv = -1f + 2f * j / nw;
                Vector3 up;
                bottomPosition[k] = Surface(v, sv, true, out up);
                bottomIndex[k] = data.AddVertex(bottomPosition[k], -topNormal[k], new Vector2((sv + 1f) * 0.5f, v));
                return bottomIndex[k];
            }

            private bool IsSolid(int i, int j)
            {
                return i >= 0 && i < nv && j >= 0 && j < nw && solid[i, j];
            }

            /// <summary>Adds triangle (a,b,c) or its reverse so that it faces <paramref name="want"/>.</summary>
            private void AddTriangle(int a, int b, int c, Vector3 pa, Vector3 pb, Vector3 pc, Vector3 want)
            {
                Vector3 g = Vector3.Cross(pb - pa, pc - pa);
                if (Vector3.Dot(g, want) >= 0f) data.AddTriangle(a, b, c, submesh);
                else data.AddTriangle(a, c, b, submesh);
            }

            /// <summary>Rim quad along the edge between corners (pi,pj) and (qi,qj) of cell (ci,cj), facing away from the cell.</summary>
            private void AddRim(int ci, int cj, int pi, int pj, int qi, int qj, int cellI, int cellJ)
            {
                int kp = Corner(pi, pj), kq = Corner(qi, qj);
                int tp = TopVertex(pi, pj), tq = TopVertex(qi, qj);
                int bp = BottomVertex(pi, pj), bq = BottomVertex(qi, qj);

                Vector3 cellCentre = (topPosition[Corner(cellI, cellJ)] + topPosition[Corner(cellI, cellJ + 1)] +
                                      topPosition[Corner(cellI + 1, cellJ + 1)] + topPosition[Corner(cellI + 1, cellJ)]) * 0.25f;
                Vector3 outward = (topPosition[kp] + topPosition[kq]) * 0.5f - cellCentre;

                AddTriangle(tp, tq, bq, topPosition[kp], topPosition[kq], bottomPosition[kq], outward);
                AddTriangle(tp, bq, bp, topPosition[kp], bottomPosition[kq], bottomPosition[kp], outward);
            }
        }
    }
}
