using System.Collections.Generic;
using UnityEngine;

namespace BioDome.Procedural
{
    /// <summary>
    /// Cheap, reusable plant geometry that the shared leaf generator does not cover: low-polygon double-sided leaflets,
    /// pinnate fronds (palms, ferns), torn banana-style blades, round leaflets and arching paths.
    /// Everything is double-sided with correct normals on both faces, because the plant materials are two-sided and
    /// URP does not flip normals on back faces. Output goes into a <see cref="MeshData"/> submesh chosen by the caller.
    /// </summary>
    public static class PlantParts
    {
        /// <summary>
        /// The front and back copies of every two-sided face are pushed this far (metres) apart along the normal. Without it the two
        /// coplanar copies fight for the depth buffer on cull-off materials and the surface looks speckled or dark.
        /// </summary>
        public const float FaceOffset = 0.0012f;

        // --- Triangles and quads ----------------------------------------------------

        /// <summary>
        /// Adds one triangle that is visible from both sides. The front face looks along <paramref name="na"/>+<paramref name="nb"/>+<paramref name="nc"/>;
        /// the winding is chosen automatically so geometry and normals always agree.
        /// </summary>
        public static void AddTwoSidedTriangle(
            MeshData d, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc,
            Vector2 ua, Vector2 ub, Vector2 uc, int submesh)
        {
            Vector3 geometric = Vector3.Cross(b - a, c - a);
            if (geometric.sqrMagnitude < 1e-14f) return;

            bool flip = Vector3.Dot(geometric, na + nb + nc) < 0f;

            int f0 = d.AddVertex(a + na * FaceOffset, na, ua);
            int f1 = d.AddVertex(b + nb * FaceOffset, nb, ub);
            int f2 = d.AddVertex(c + nc * FaceOffset, nc, uc);
            if (!flip) d.AddTriangle(f0, f1, f2, submesh); else d.AddTriangle(f0, f2, f1, submesh);

            int k0 = d.AddVertex(a - na * FaceOffset, -na, ua);
            int k1 = d.AddVertex(b - nb * FaceOffset, -nb, ub);
            int k2 = d.AddVertex(c - nc * FaceOffset, -nc, uc);
            if (!flip) d.AddTriangle(k0, k2, k1, submesh); else d.AddTriangle(k0, k1, k2, submesh);
        }

        /// <summary>Two-sided quad (a, b, c, d in order around its edge) with one shared normal hint.</summary>
        public static void AddTwoSidedQuad(
            MeshData d, Vector3 a, Vector3 b, Vector3 c, Vector3 e, Vector3 normalHint,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ue, int submesh)
        {
            AddTwoSidedTriangle(d, a, b, c, normalHint, normalHint, normalHint, ua, ub, uc, submesh);
            AddTwoSidedTriangle(d, a, c, e, normalHint, normalHint, normalHint, ua, uc, ue, submesh);
        }

        // --- Paths --------------------------------------------------------------------

        /// <summary>
        /// Samples an arching spine: it starts at <paramref name="pitch"/> radians above horizontal (towards the horizontal
        /// direction <paramref name="outward"/>) and its angle falls by <paramref name="bend"/> radians along the length,
        /// so a positive bend arches over and droops. Used for fronds and big leaf blades.
        /// </summary>
        public static List<Vector3> ArchPath(Vector3 start, Vector3 outward, float pitch, float bend, float length, int steps)
        {
            steps = Mathf.Max(2, steps);
            outward.y = 0f;
            outward = outward.sqrMagnitude < 1e-8f ? Vector3.forward : outward.normalized;

            var points = new List<Vector3>(steps + 1) { start };
            Vector3 position = start;
            float stepLength = length / steps;
            for (int i = 0; i < steps; i++)
            {
                float s = (i + 0.5f) / steps;
                float angle = pitch - bend * Mathf.Pow(s, 1.25f);
                position += (outward * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * stepLength;
                points.Add(position);
            }
            return points;
        }

        /// <summary>Unit tangent of a path at a fraction of its length.</summary>
        public static Vector3 TangentOnPath(IList<Vector3> path, float fraction)
        {
            Vector3 a = ProceduralMeshUtility.PointOnPath(path, Mathf.Clamp01(fraction - 0.04f));
            Vector3 b = ProceduralMeshUtility.PointOnPath(path, Mathf.Clamp01(fraction + 0.04f));
            Vector3 t = b - a;
            return t.sqrMagnitude > 1e-12f ? t.normalized : Vector3.up;
        }

        // --- Leaflets and fronds --------------------------------------------------------

        /// <summary>Tapered, pointed leaflet outline: 0 at the base rising to 1 near a third of the way, then narrowing to a point.</summary>
        private static float LeafletProfile(float u)
        {
            float body = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Clamp01(u), 0.75f))), 0.8f);
            return Mathf.Max(0.18f * (1f - u), body);
        }

        /// <summary>
        /// One low-polygon double-sided leaflet: a gently folded blade from <paramref name="start"/> along <paramref name="direction"/>.
        /// <paramref name="up"/> is the side the blade faces; <paramref name="droop"/> curves the tip downwards (0 to about 0.6).
        /// <paramref name="segments"/> 1 gives a cheap pointed blade (8 triangles counting both faces), 3 a smoothly curved one.
        /// </summary>
        public static void AddLeaflet(
            MeshData d, Vector3 start, Vector3 direction, Vector3 up, float length, float width,
            float droop, int segments, int submesh, float fold = 0.16f)
        {
            if (length <= 1e-4f || width <= 1e-5f) return;
            segments = Mathf.Max(1, segments);

            Vector3 dir = direction.normalized;
            Vector3 w = Vector3.Cross(up, dir);
            if (w.sqrMagnitude < 1e-8f) w = ProceduralMeshUtility.Perpendicular(dir);
            w.Normalize();

            int rows = segments + 1;
            var left = new Vector3[rows];
            var mid = new Vector3[rows];
            var right = new Vector3[rows];
            var normals = new Vector3[rows];
            for (int j = 0; j < rows; j++)
            {
                float u = j / (float)segments;
                Vector3 p = start + dir * (length * u) - up * (droop * length * u * u);
                Vector3 tangent = dir - up * (2f * droop * u);
                Vector3 n = Vector3.Cross(tangent, w).normalized;
                float half = width * 0.5f * LeafletProfile(u);

                left[j] = p - w * half;
                mid[j] = p + n * (half * fold);
                right[j] = p + w * half;
                normals[j] = n;
            }

            // Front vertices first, then the mirrored back vertices (opposite normals).
            var front = new int[rows * 3];
            var back = new int[rows * 3];
            for (int j = 0; j < rows; j++)
            {
                float v = j / (float)segments;
                Vector3 lift = normals[j] * FaceOffset;
                front[j * 3 + 0] = d.AddVertex(left[j] + lift, normals[j], new Vector2(0f, v));
                front[j * 3 + 1] = d.AddVertex(mid[j] + lift, normals[j], new Vector2(0.5f, v));
                front[j * 3 + 2] = d.AddVertex(right[j] + lift, normals[j], new Vector2(1f, v));
                back[j * 3 + 0] = d.AddVertex(left[j] - lift, -normals[j], new Vector2(0f, v));
                back[j * 3 + 1] = d.AddVertex(mid[j] - lift, -normals[j], new Vector2(0.5f, v));
                back[j * 3 + 2] = d.AddVertex(right[j] - lift, -normals[j], new Vector2(1f, v));
            }

            // With w = up x dir, the winding (left_j, left_j+1, mid_j+1) faces along +normal.
            for (int j = 0; j < segments; j++)
            {
                int l0 = j * 3, m0 = j * 3 + 1, r0 = j * 3 + 2;
                int l1 = (j + 1) * 3, m1 = (j + 1) * 3 + 1, r1 = (j + 1) * 3 + 2;

                d.AddTriangle(front[l0], front[l1], front[m1], submesh);
                d.AddTriangle(front[l0], front[m1], front[m0], submesh);
                d.AddTriangle(front[m0], front[m1], front[r1], submesh);
                d.AddTriangle(front[m0], front[r1], front[r0], submesh);

                d.AddTriangle(back[l0], back[m1], back[l1], submesh);
                d.AddTriangle(back[l0], back[m0], back[m1], submesh);
                d.AddTriangle(back[m0], back[r1], back[m1], submesh);
                d.AddTriangle(back[m0], back[r0], back[r1], submesh);
            }
        }

        /// <summary>
        /// A pinnate frond (palm, fern): a thin rachis along <paramref name="path"/> with pairs of leaflets on both sides.
        /// Leaflet length rises quickly from the base, peaks about a third of the way and tapers to the tip.
        /// <paramref name="outward"/> is the horizontal direction the frond points, so the leaflets spread in a shallow V.
        /// </summary>
        public static void AddFrond(
            MeshData d, IList<Vector3> path, Vector3 outward, int pairs, float leafletLength, float leafletWidth,
            float leafletAngle, float droop, int segments, float rachisRadius, int rachisSubmesh, int leafSubmesh,
            System.Random rng, float startFraction = 0.12f)
        {
            if (path == null || path.Count < 2) return;

            if (rachisRadius > 0f)
                StemMeshGenerator.AddTube(d, path, rachisRadius, rachisRadius * 0.3f, 4, rachisSubmesh, false, false);

            outward.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, outward.sqrMagnitude < 1e-8f ? Vector3.forward : outward.normalized).normalized;

            pairs = Mathf.Max(1, pairs);
            for (int k = 0; k < pairs; k++)
            {
                float t = Mathf.Lerp(startFraction, 0.97f, (k + 0.5f) / pairs);
                Vector3 p = ProceduralMeshUtility.PointOnPath(path, t);
                Vector3 tangent = TangentOnPath(path, t);
                Vector3 up = Vector3.Cross(tangent, side).normalized; // faces upwards for a frond lying along 'outward'

                float profile = t < 0.3f ? Mathf.Lerp(0.55f, 1f, t / 0.3f) : Mathf.Lerp(1f, 0.22f, (t - 0.3f) / 0.7f);
                float length = leafletLength * profile * ProceduralMeshUtility.Range(rng, 0.92f, 1.08f);

                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 direction = (tangent * Mathf.Cos(leafletAngle) + side * (s * Mathf.Sin(leafletAngle))).normalized;
                    AddLeaflet(d, p, direction, up, length, leafletWidth, droop, segments, leafSubmesh);
                }
            }
        }

        // --- Torn banana-style blade ------------------------------------------------------

        /// <summary>
        /// A large banana-type blade: a stout midrib along <paramref name="midrib"/> with parallel strips (ribbons) running out
        /// to each edge. Strips sag at the edges and the margin is torn into separate ribbons, like a real banana leaf.
        /// <paramref name="outward"/> is the horizontal heading of the arch, which fixes the blade's width axis.
        /// </summary>
        public static void AddRibbonBlade(
            MeshData d, IList<Vector3> midrib, Vector3 outward, float width, int ribbons, float sag, float tear,
            System.Random rng, int leafSubmesh, int ribSubmesh, float ribRadius)
        {
            if (midrib == null || midrib.Count < 2) return;

            outward.y = 0f;
            Vector3 o = outward.sqrMagnitude < 1e-8f ? Vector3.forward : outward.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, o).normalized;

            StemMeshGenerator.AddTube(d, midrib, ribRadius, ribRadius * 0.3f, 5, ribSubmesh, false, false);

            ribbons = Mathf.Max(4, ribbons);
            for (int i = 0; i < ribbons; i++)
            {
                float t0 = i / (float)ribbons;
                for (int s = -1; s <= 1; s += 2)
                {
                    // Torn margin: ribbons nearer the tip and edge leave bigger gaps between them.
                    float cover = 1f - tear * Mathf.Lerp(0.15f, 0.8f, t0) * (float)rng.NextDouble();
                    float t1 = t0 + cover / ribbons;

                    Vector3 p0 = ProceduralMeshUtility.PointOnPath(midrib, t0);
                    Vector3 p1 = ProceduralMeshUtility.PointOnPath(midrib, Mathf.Min(1f, t1));
                    Vector3 tan0 = TangentOnPath(midrib, t0);
                    Vector3 tan1 = TangentOnPath(midrib, Mathf.Min(1f, t1));
                    Vector3 n0 = Vector3.Cross(tan0, side).normalized;
                    Vector3 n1 = Vector3.Cross(tan1, side).normalized;

                    float w0 = width * 0.5f * BananaProfile(t0);
                    float w1 = width * 0.5f * BananaProfile(Mathf.Min(1f, t1));
                    float droop0 = sag * Mathf.Lerp(0.35f, 1f, t0) * ProceduralMeshUtility.Range(rng, 0.85f, 1.15f);
                    float droop1 = sag * Mathf.Lerp(0.35f, 1f, Mathf.Min(1f, t1)) * ProceduralMeshUtility.Range(rng, 0.85f, 1.15f);
                    const float slant = 0.32f; // lateral strips lean towards the tip

                    Vector3 outer0 = p0 + side * (s * w0 * Mathf.Cos(droop0)) - n0 * (w0 * Mathf.Sin(droop0)) + tan0 * (w0 * slant);
                    Vector3 outer1 = p1 + side * (s * w1 * Mathf.Cos(droop1)) - n1 * (w1 * Mathf.Sin(droop1)) + tan1 * (w1 * slant);
                    Vector3 mid0 = p0 + side * (s * w0 * 0.5f * Mathf.Cos(droop0 * 0.5f)) - n0 * (w0 * 0.5f * Mathf.Sin(droop0 * 0.5f)) + tan0 * (w0 * slant * 0.5f);
                    Vector3 mid1 = p1 + side * (s * w1 * 0.5f * Mathf.Cos(droop1 * 0.5f)) - n1 * (w1 * 0.5f * Mathf.Sin(droop1 * 0.5f)) + tan1 * (w1 * slant * 0.5f);

                    float uOuter = 0.5f + s * 0.5f;
                    float uMid = 0.5f + s * 0.25f;
                    Vector3 hint = (n0 + n1).normalized;
                    AddTwoSidedQuad(d, p0, p1, mid1, mid0, hint,
                        new Vector2(0.5f, t0), new Vector2(0.5f, t1), new Vector2(uMid, t1), new Vector2(uMid, t0), leafSubmesh);
                    AddTwoSidedQuad(d, mid0, mid1, outer1, outer0, hint,
                        new Vector2(uMid, t0), new Vector2(uMid, t1), new Vector2(uOuter, t1), new Vector2(uOuter, t0), leafSubmesh);
                }
            }
        }

        /// <summary>Banana blade outline: broad from near the base, rounded towards the tip.</summary>
        private static float BananaProfile(float t)
        {
            return Mathf.Pow(Mathf.Max(0.02f, Mathf.Sin(Mathf.PI * Mathf.Pow(Mathf.Clamp01(t), 0.8f))), 0.55f);
        }

        // --- Small round leaf ---------------------------------------------------------------

        /// <summary>A tiny cupped round leaf (a six-sided fan), double-sided. Used for creeping ground cover.</summary>
        public static void AddRoundLeaf(MeshData d, Vector3 center, Vector3 normal, float radius, float cup, System.Random rng, int submesh)
        {
            Vector3 n = normal.normalized;
            Vector3 a = ProceduralMeshUtility.Perpendicular(n);
            Vector3 b = Vector3.Cross(n, a);
            float phase = ProceduralMeshUtility.Range(rng, 0f, Mathf.PI * 2f);
            const int sides = 6;

            Vector3 apex = center + n * (radius * cup);
            for (int i = 0; i < sides; i++)
            {
                float a0 = phase + i * Mathf.PI * 2f / sides;
                float a1 = phase + (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = center + (a * Mathf.Cos(a0) + b * Mathf.Sin(a0)) * radius;
                Vector3 p1 = center + (a * Mathf.Cos(a1) + b * Mathf.Sin(a1)) * radius;
                AddTwoSidedTriangle(d, apex, p0, p1, n, n, n,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f + 0.5f * Mathf.Cos(a0), 0.5f + 0.5f * Mathf.Sin(a0)),
                    new Vector2(0.5f + 0.5f * Mathf.Cos(a1), 0.5f + 0.5f * Mathf.Sin(a1)), submesh);
            }
        }
    }
}
