using UnityEngine;

namespace BioDome.Procedural
{
    /// <summary>
    /// Small reusable building blocks that generators combine. Stateless static helpers that write into a MeshData.
    /// More primitives (tube along a path, quad strip, disc, ...) belong here.
    /// </summary>
    public static class MeshPrimitives
    {
        /// <summary>
        /// Adds an upright cylinder whose bottom centre is at <paramref name="baseCenter"/>.
        /// Radius may differ top and bottom to make cones and tapered stems.
        /// </summary>
        public static void AddCylinder(
            MeshData data,
            Vector3 baseCenter,
            float bottomRadius,
            float topRadius,
            float height,
            int radialSegments,
            int heightSegments,
            bool capBottom,
            bool capTop,
            int submesh = 0)
        {
            if (data == null) throw new System.ArgumentNullException(nameof(data));

            radialSegments = Mathf.Max(3, radialSegments);
            heightSegments = Mathf.Max(1, heightSegments);

            // Slope of the side wall, so normals tilt correctly on tapered cylinders.
            float slope = height > 0f ? (bottomRadius - topRadius) / height : 0f;

            int firstRing = data.VertexCount;
            for (int y = 0; y <= heightSegments; y++)
            {
                float v = y / (float)heightSegments;
                float radius = Mathf.Lerp(bottomRadius, topRadius, v);

                for (int x = 0; x <= radialSegments; x++) // duplicated seam vertex keeps UVs continuous
                {
                    float u = x / (float)radialSegments;
                    float angle = u * Mathf.PI * 2f;
                    Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                    Vector3 position = baseCenter + direction * radius + Vector3.up * (height * v);
                    Vector3 normal = new Vector3(direction.x, slope, direction.z).normalized;
                    data.AddVertex(position, normal, new Vector2(u, v));
                }
            }

            int stride = radialSegments + 1;
            for (int y = 0; y < heightSegments; y++)
            {
                for (int x = 0; x < radialSegments; x++)
                {
                    int a = firstRing + y * stride + x;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    data.AddTriangle(a, c, b, submesh);
                    data.AddTriangle(b, c, d, submesh);
                }
            }

            if (capBottom) AddCap(data, baseCenter, bottomRadius, radialSegments, false, submesh);
            if (capTop) AddCap(data, baseCenter + Vector3.up * height, topRadius, radialSegments, true, submesh);
        }

        private static void AddCap(MeshData data, Vector3 center, float radius, int radialSegments, bool facesUp, int submesh)
        {
            Vector3 normal = facesUp ? Vector3.up : Vector3.down;
            int centerIndex = data.AddVertex(center, normal, new Vector2(0.5f, 0.5f));

            int firstRim = data.VertexCount;
            for (int x = 0; x <= radialSegments; x++)
            {
                float angle = x / (float)radialSegments * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector2 uv = new Vector2(0.5f + direction.x * 0.5f, 0.5f + direction.z * 0.5f);
                data.AddVertex(center + direction * radius, normal, uv);
            }

            for (int x = 0; x < radialSegments; x++)
            {
                int current = firstRim + x;
                int next = current + 1;
                if (facesUp) data.AddTriangle(centerIndex, next, current, submesh);
                else data.AddTriangle(centerIndex, current, next, submesh);
            }
        }
    }
}
