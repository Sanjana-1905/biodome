using System.Collections.Generic;
using UnityEngine;

namespace BioDome.Procedural
{
    /// <summary>
    /// Small maths helpers shared by the botanical generators: Bezier curves, deterministic random helpers,
    /// orientation frames and parameter hashing.
    /// </summary>
    public static class ProceduralMeshUtility
    {
        // --- Curves ---------------------------------------------------------------

        public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        public static Vector3 BezierTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            Vector3 d = 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
            return d.sqrMagnitude > 1e-12f ? d.normalized : (p3 - p0).normalized;
        }

        /// <summary>Samples a cubic Bezier into <paramref name="count"/> evenly spaced (in t) points, including both ends.</summary>
        public static List<Vector3> SampleBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, int count)
        {
            count = Mathf.Max(2, count);
            var points = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                points.Add(Bezier(p0, p1, p2, p3, i / (float)(count - 1)));
            }
            return points;
        }

        /// <summary>Position at a 0-1 fraction along a polyline (by index, linearly interpolated).</summary>
        public static Vector3 PointOnPath(IList<Vector3> path, float fraction)
        {
            if (path.Count == 1) return path[0];
            float f = Mathf.Clamp01(fraction) * (path.Count - 1);
            int i = Mathf.Min(Mathf.FloorToInt(f), path.Count - 2);
            return Vector3.Lerp(path[i], path[i + 1], f - i);
        }

        // --- Orientation ----------------------------------------------------------

        /// <summary>Any unit vector perpendicular to <paramref name="v"/>.</summary>
        public static Vector3 Perpendicular(Vector3 v)
        {
            Vector3 axis = Mathf.Abs(v.y) < 0.9f ? Vector3.up : Vector3.right;
            return Vector3.Cross(v, axis).normalized;
        }

        // --- Deterministic randomness ---------------------------------------------

        public static float Range(System.Random rng, float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }

        public static float Signed(System.Random rng, float magnitude)
        {
            return ((float)rng.NextDouble() * 2f - 1f) * magnitude;
        }

        // --- Hashing --------------------------------------------------------------

        /// <summary>Folds a float into a running hash with 3-decimal precision (stable across runs).</summary>
        public static int Combine(int hash, float value)
        {
            unchecked { return hash * 31 + Mathf.RoundToInt(value * 1000f); }
        }

        public static int Combine(int hash, int value)
        {
            unchecked { return hash * 31 + value; }
        }
    }
}
