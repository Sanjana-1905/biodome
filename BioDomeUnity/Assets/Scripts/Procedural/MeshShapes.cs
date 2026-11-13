using System;
using UnityEngine;

namespace BioDome.Procedural
{
    /// <summary>
    /// Reusable shape builders for architecture and props (boxes, beams, pipes, quads, rings, discs, rocks).
    /// Complements <see cref="MeshPrimitives"/>. All methods write into a <see cref="MeshData"/>,
    /// use metre-scale UVs (so a material's tiling can be set in world units) and Unity's clockwise winding.
    /// </summary>
    public static class MeshShapes
    {
        private static readonly Vector3[] BoxAxes =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };

        /// <summary>Flat-shaded box. UVs are in metres on each face.</summary>
        public static void AddBox(MeshData data, Vector3 center, Vector3 size, Quaternion rotation, int submesh = 0)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            Vector3 half = size * 0.5f;
            for (int i = 0; i < BoxAxes.Length; i++)
            {
                Vector3 n = BoxAxes[i];
                Vector3 v = Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up;
                Vector3 u = Vector3.Cross(n, v); // cross(u, v) = -n, which gives clockwise winding seen from outside

                float hn = Extent(n, half);
                float hu = Extent(u, half);
                float hv = Extent(v, half);
                Vector3 c = n * hn;

                Vector3 worldNormal = rotation * n;
                int a = data.AddVertex(center + rotation * (c - u * hu - v * hv), worldNormal, new Vector2(0f, 0f));
                int b = data.AddVertex(center + rotation * (c - u * hu + v * hv), worldNormal, new Vector2(0f, hv * 2f));
                int cc = data.AddVertex(center + rotation * (c + u * hu + v * hv), worldNormal, new Vector2(hu * 2f, hv * 2f));
                int d = data.AddVertex(center + rotation * (c + u * hu - v * hv), worldNormal, new Vector2(hu * 2f, 0f));
                data.AddQuad(a, b, cc, d, submesh);
            }
        }

        /// <summary>
        /// A straight bar between two points. <paramref name="width"/> and <paramref name="height"/> are the
        /// cross-section sizes; "height" is measured along the world up direction (or forward for vertical bars).
        /// </summary>
        public static void AddBeam(MeshData data, Vector3 from, Vector3 to, float width, float height, int submesh = 0)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-4f) return;

            Vector3 dir = delta / length;
            Vector3 upHint = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(dir, upHint);
            AddBox(data, (from + to) * 0.5f, new Vector3(width, height, length), rotation, submesh);
        }

        /// <summary>A round pipe between two points.</summary>
        public static void AddTube(MeshData data, Vector3 from, Vector3 to, float radius, int segments, int submesh = 0, bool caps = true)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-4f) return;

            var part = new MeshData();
            MeshPrimitives.AddCylinder(part, Vector3.zero, radius, radius, length, segments, 1, caps, caps, submesh);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, delta / length);
            data.Append(part, Matrix4x4.TRS(from, rotation, Vector3.one));
        }

        /// <summary>
        /// A flat quad given as bottom-left, top-left, top-right, bottom-right. The corners may be in either
        /// order: the quad is flipped so it faces <paramref name="facing"/>. UVs are in metres.
        /// </summary>
        public static void AddQuadFacing(
            MeshData data, Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Vector3 facing, int submesh = 0)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            float w = (br - bl).magnitude;
            float h = (tl - bl).magnitude;
            Vector3[] points = { bl, tl, tr, br };
            Vector2[] uvs = { new Vector2(0f, 0f), new Vector2(0f, h), new Vector2(w, h), new Vector2(w, 0f) };

            Vector3 geometric = Vector3.Cross(tl - bl, tr - bl);
            if (geometric.sqrMagnitude < 1e-10f) geometric = Vector3.Cross(tr - bl, br - bl) * -1f;
            bool flip = Vector3.Dot(geometric, facing) < 0f;

            Vector3 normal = facing.normalized;
            int[] idx = new int[4];
            for (int i = 0; i < 4; i++)
            {
                int source = flip ? 3 - i : i;
                idx[i] = data.AddVertex(points[source], normal, uvs[source]);
            }
            data.AddQuad(idx[0], idx[1], idx[2], idx[3], submesh);
        }

        /// <summary>Upward-facing disc. UVs follow world XZ (in metres) so adjacent surfaces tile seamlessly.</summary>
        public static void AddDisc(MeshData data, Vector3 center, float radius, int segments, int submesh = 0)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            segments = Mathf.Max(3, segments);

            int centerIndex = data.AddVertex(center, Vector3.up, new Vector2(center.x, center.z));
            int firstRim = data.VertexCount;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector3 p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                data.AddVertex(p, Vector3.up, new Vector2(p.x, p.z));
            }
            for (int i = 0; i < segments; i++)
            {
                data.AddTriangle(centerIndex, firstRim + i + 1, firstRim + i, submesh);
            }
        }

        /// <summary>
        /// Upright ring (annulus) extruded upward from <c>center.y</c>: top face, outer wall and inner wall.
        /// Angles are measured from +X towards +Z. Pass a partial arc to leave a gap (e.g. for a path).
        /// </summary>
        public static void AddRing(
            MeshData data, Vector3 center, float innerRadius, float outerRadius, float height,
            int segments, int submesh = 0, float startDegrees = 0f, float endDegrees = 360f)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            float arc = Mathf.Abs(endDegrees - startDegrees);
            int count = Mathf.Max(3, Mathf.CeilToInt(segments * arc / 360f));
            float top = center.y + height;

            int[] outerTop = new int[count + 1];
            int[] innerTop = new int[count + 1];
            int[] outerBottom = new int[count + 1];
            int[] outerWallTop = new int[count + 1];
            int[] innerBottom = new int[count + 1];
            int[] innerWallTop = new int[count + 1];

            for (int i = 0; i <= count; i++)
            {
                float angle = Mathf.Lerp(startDegrees, endDegrees, i / (float)count) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float along = i / (float)count * arc * Mathf.Deg2Rad * outerRadius;

                Vector3 po = center + dir * outerRadius;
                Vector3 pi = center + dir * innerRadius;

                outerTop[i] = data.AddVertex(new Vector3(po.x, top, po.z), Vector3.up, new Vector2(po.x, po.z));
                innerTop[i] = data.AddVertex(new Vector3(pi.x, top, pi.z), Vector3.up, new Vector2(pi.x, pi.z));

                outerBottom[i] = data.AddVertex(new Vector3(po.x, center.y, po.z), dir, new Vector2(along, 0f));
                outerWallTop[i] = data.AddVertex(new Vector3(po.x, top, po.z), dir, new Vector2(along, height));
                innerBottom[i] = data.AddVertex(new Vector3(pi.x, center.y, pi.z), -dir, new Vector2(along, 0f));
                innerWallTop[i] = data.AddVertex(new Vector3(pi.x, top, pi.z), -dir, new Vector2(along, height));
            }

            // Angles may run backwards if end < start; flip winding in that case.
            bool reversed = endDegrees < startDegrees;
            for (int i = 0; i < count; i++)
            {
                int n = i + 1;
                AddOriented(data, reversed, submesh, outerTop[i], innerTop[i], innerTop[n], outerTop[n]);
                AddOriented(data, reversed, submesh, outerBottom[i], outerWallTop[i], outerWallTop[n], outerBottom[n]);
                AddOriented(data, reversed, submesh, innerBottom[n], innerWallTop[n], innerWallTop[i], innerBottom[i]);
            }
        }

        /// <summary>
        /// Lumpy rounded rock/boulder. <paramref name="scale"/> squashes the sphere; <paramref name="noise"/>
        /// (0 to about 0.3) randomises each vertex radius. Deterministic for a given <paramref name="random"/>.
        /// </summary>
        public static void AddSphere(
            MeshData data, Vector3 center, float radius, Vector3 scale, int rings, int segments,
            System.Random random, float noise = 0f, int submesh = 0)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (random == null) throw new ArgumentNullException(nameof(random));

            rings = Mathf.Max(3, rings);
            segments = Mathf.Max(3, segments);

            // One random radius per lattice point; the seam column reuses column 0 and the poles are fixed.
            float[,] jitter = new float[rings + 1, segments];
            for (int r = 0; r <= rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    bool pole = r == 0 || r == rings;
                    jitter[r, s] = pole ? 1f : 1f + ((float)random.NextDouble() * 2f - 1f) * noise;
                }
            }

            Vector3 inverseScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            int first = data.VertexCount;
            for (int r = 0; r <= rings; r++)
            {
                float theta = Mathf.PI * r / rings;
                for (int s = 0; s <= segments; s++)
                {
                    float phi = Mathf.PI * 2f * s / segments;
                    Vector3 dir = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    Vector3 position = Vector3.Scale(dir, scale) * (radius * jitter[r, s % segments]);
                    Vector3 normal = Vector3.Scale(dir, inverseScale).normalized;
                    data.AddVertex(center + position, normal, new Vector2(s / (float)segments, 1f - r / (float)rings));
                }
            }

            int stride = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = first + r * stride + s; // upper left
                    int b = a + 1;                  // upper right
                    int c = a + stride;             // lower left
                    int d = c + 1;                  // lower right
                    data.AddQuad(c, a, b, d, submesh);
                }
            }
        }

        private static void AddOriented(MeshData data, bool reversed, int submesh, int a, int b, int c, int d)
        {
            if (reversed) data.AddQuad(d, c, b, a, submesh);
            else data.AddQuad(a, b, c, d, submesh);
        }

        private static float Extent(Vector3 axis, Vector3 half)
        {
            return Mathf.Abs(axis.x) * half.x + Mathf.Abs(axis.y) * half.y + Mathf.Abs(axis.z) * half.z;
        }
    }
}
