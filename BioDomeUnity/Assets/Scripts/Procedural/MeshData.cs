using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BioDome.Procedural
{
    /// <summary>
    /// CPU-side mesh buffer that generators write into. It is a plain class (no Unity object), so
    /// generation is cheap to test and several parts can be combined with <see cref="Append"/> before
    /// a single Unity Mesh is built with <see cref="ApplyTo"/> (Unity Mesh API).
    /// Every vertex always carries position, normal, UV and colour, so the buffers cannot get out of sync.
    /// Triangle winding is clockwise when seen from the front face (Unity convention).
    /// </summary>
    public sealed class MeshData
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<List<int>> submeshes = new List<List<int>> { new List<int>() };

        public int VertexCount => vertices.Count;
        public int SubmeshCount => submeshes.Count;
        public int TriangleCount
        {
            get
            {
                int indices = 0;
                for (int i = 0; i < submeshes.Count; i++) indices += submeshes[i].Count;
                return indices / 3;
            }
        }

        public void Clear()
        {
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            colors.Clear();
            submeshes.Clear();
            submeshes.Add(new List<int>());
        }

        /// <summary>Adds a vertex and returns its index.</summary>
        public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv, Color color)
        {
            vertices.Add(position);
            normals.Add(normal);
            uvs.Add(uv);
            colors.Add(color);
            return vertices.Count - 1;
        }

        public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            return AddVertex(position, normal, uv, Color.white);
        }

        /// <summary>Adds one triangle (indices must refer to existing vertices).</summary>
        public void AddTriangle(int a, int b, int c, int submesh = 0)
        {
            if (submesh < 0) throw new ArgumentOutOfRangeException(nameof(submesh));
            int count = vertices.Count;
            if (a < 0 || b < 0 || c < 0 || a >= count || b >= count || c >= count)
                throw new ArgumentOutOfRangeException(nameof(a), "Triangle index refers to a vertex that does not exist.");

            EnsureSubmesh(submesh);
            List<int> list = submeshes[submesh];
            list.Add(a);
            list.Add(b);
            list.Add(c);
        }

        /// <summary>Adds two triangles for the quad a-b-c-d, given in clockwise order seen from the front.</summary>
        public void AddQuad(int a, int b, int c, int d, int submesh = 0)
        {
            AddTriangle(a, b, c, submesh);
            AddTriangle(a, c, d, submesh);
        }

        /// <summary>
        /// Copies another MeshData into this one, transformed by <paramref name="transform"/>.
        /// Submeshes keep their index so materials stay aligned across parts.
        /// </summary>
        public void Append(MeshData other, Matrix4x4 transform)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (other == this) throw new ArgumentException("Cannot append a MeshData to itself.", nameof(other));

            int offset = vertices.Count;
            Matrix4x4 normalMatrix = transform.inverse.transpose;
            bool flipWinding = transform.determinant < 0f; // mirrored transforms invert the winding

            for (int i = 0; i < other.vertices.Count; i++)
            {
                vertices.Add(transform.MultiplyPoint3x4(other.vertices[i]));
                normals.Add(normalMatrix.MultiplyVector(other.normals[i]).normalized);
                uvs.Add(other.uvs[i]);
                colors.Add(other.colors[i]);
            }

            for (int s = 0; s < other.submeshes.Count; s++)
            {
                EnsureSubmesh(s);
                List<int> source = other.submeshes[s];
                List<int> target = submeshes[s];
                for (int i = 0; i + 2 < source.Count; i += 3)
                {
                    if (flipWinding)
                    {
                        target.Add(source[i] + offset);
                        target.Add(source[i + 2] + offset);
                        target.Add(source[i + 1] + offset);
                    }
                    else
                    {
                        target.Add(source[i] + offset);
                        target.Add(source[i + 1] + offset);
                        target.Add(source[i + 2] + offset);
                    }
                }
            }
        }

        /// <summary>Writes this data into a Unity Mesh, replacing its previous contents.</summary>
        public void ApplyTo(Mesh mesh, bool recalculateNormals = false)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));

            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);

            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++)
            {
                mesh.SetTriangles(submeshes[i], i, false);
            }

            mesh.RecalculateBounds();
            if (recalculateNormals) mesh.RecalculateNormals();
        }

        private void EnsureSubmesh(int index)
        {
            while (submeshes.Count <= index) submeshes.Add(new List<int>());
        }
    }
}
