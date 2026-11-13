using System;
using System.Collections.Generic;
using UnityEngine;

namespace BioDome.Procedural
{
    /// <summary>
    /// Builds tapered tubes along an arbitrary path: trunks, stems, petioles, branches and rachises.
    /// Uses parallel-transport frames so tubes do not twist, smooth ring normals, and metre-based UVs.
    /// </summary>
    public static class StemMeshGenerator
    {
        /// <summary>
        /// Adds a tube following <paramref name="path"/>. Radius goes from <paramref name="startRadius"/> to
        /// <paramref name="endRadius"/>; <paramref name="taperPower"/> above 1 keeps it thick for longer.
        /// </summary>
        public static void AddTube(
            MeshData data,
            IList<Vector3> path,
            float startRadius,
            float endRadius,
            int radialSegments,
            int submesh,
            bool capStart,
            bool capEnd,
            float taperPower = 1f,
            float uvMetres = 0.25f)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (path.Count < 2) return;

            radialSegments = Mathf.Max(3, radialSegments);
            int count = path.Count;

            // Tangents (central differences) and parallel-transported normals.
            var tangents = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 prev = path[Mathf.Max(i - 1, 0)];
                Vector3 next = path[Mathf.Min(i + 1, count - 1)];
                Vector3 t = next - prev;
                tangents[i] = t.sqrMagnitude > 1e-12f ? t.normalized : Vector3.up;
            }

            var normals = new Vector3[count];
            normals[0] = ProceduralMeshUtility.Perpendicular(tangents[0]);
            for (int i = 1; i < count; i++)
            {
                Quaternion turn = Quaternion.FromToRotation(tangents[i - 1], tangents[i]);
                normals[i] = (turn * normals[i - 1]).normalized;
            }

            int stride = radialSegments + 1;
            int first = data.VertexCount;
            var ringPositions = new Vector3[count * stride];
            var ringNormals = new Vector3[count * stride];
            float distance = 0f;

            for (int i = 0; i < count; i++)
            {
                if (i > 0) distance += Vector3.Distance(path[i], path[i - 1]);

                float f = i / (float)(count - 1);
                float radius = Mathf.Lerp(startRadius, endRadius, Mathf.Pow(f, taperPower));
                Vector3 binormal = Vector3.Cross(tangents[i], normals[i]);

                for (int j = 0; j <= radialSegments; j++)
                {
                    float angle = j / (float)radialSegments * Mathf.PI * 2f;
                    Vector3 outward = normals[i] * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                    int k = i * stride + j;
                    ringPositions[k] = path[i] + outward * radius;
                    ringNormals[k] = outward;
                    data.AddVertex(ringPositions[k], outward, new Vector2(j / (float)radialSegments, distance / uvMetres));
                }
            }

            for (int i = 0; i < count - 1; i++)
            {
                for (int j = 0; j < radialSegments; j++)
                {
                    int a = i * stride + j;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    AddOriented(data, submesh, first + a, first + b, first + c, ringPositions[a], ringPositions[b], ringPositions[c], ringNormals[a]);
                    AddOriented(data, submesh, first + b, first + d, first + c, ringPositions[b], ringPositions[d], ringPositions[c], ringNormals[b]);
                }
            }

            if (capStart) AddCap(data, path[0], -tangents[0], normals[0], Vector3.Cross(tangents[0], normals[0]), startRadius, radialSegments, submesh);
            if (capEnd) AddCap(data, path[count - 1], tangents[count - 1], normals[count - 1], Vector3.Cross(tangents[count - 1], normals[count - 1]), endRadius, radialSegments, submesh);
        }

        private static void AddCap(
            MeshData data, Vector3 center, Vector3 facing, Vector3 axisA, Vector3 axisB, float radius, int segments, int submesh)
        {
            int centerIndex = data.AddVertex(center, facing, new Vector2(0.5f, 0.5f));
            int first = data.VertexCount;
            var rim = new Vector3[segments + 1];
            for (int j = 0; j <= segments; j++)
            {
                float angle = j / (float)segments * Mathf.PI * 2f;
                Vector3 offset = axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle);
                rim[j] = center + offset * radius;
                data.AddVertex(rim[j], facing, new Vector2(0.5f + Mathf.Cos(angle) * 0.5f, 0.5f + Mathf.Sin(angle) * 0.5f));
            }
            for (int j = 0; j < segments; j++)
            {
                AddOriented(data, submesh, centerIndex, first + j, first + j + 1, center, rim[j], rim[j + 1], facing);
            }
        }

        /// <summary>Adds triangle (a,b,c), reversing it if its geometric normal does not agree with <paramref name="wanted"/>.</summary>
        private static void AddOriented(
            MeshData data, int submesh, int a, int b, int c, Vector3 pa, Vector3 pb, Vector3 pc, Vector3 wanted)
        {
            Vector3 geometric = Vector3.Cross(pb - pa, pc - pa);
            if (Vector3.Dot(geometric, wanted) >= 0f) data.AddTriangle(a, b, c, submesh);
            else data.AddTriangle(a, c, b, submesh);
        }
    }
}
