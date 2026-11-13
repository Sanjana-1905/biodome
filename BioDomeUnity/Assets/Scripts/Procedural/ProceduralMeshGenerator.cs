using System;
using UnityEngine;

namespace BioDome.Procedural
{
    /// <summary>
    /// Base class for reusable, data-driven geometry generators (stems, leaves, pots, greenhouse
    /// panes, ...). Each generator is a ScriptableObject asset holding its parameters, so one
    /// recipe can be reused by many objects and tweaked in the Inspector.
    /// A generator only fills a <see cref="MeshData"/>; it never touches scene objects.
    /// Subclasses must be deterministic: the same seed always produces the same geometry.
    /// </summary>
    public abstract class ProceduralMeshGenerator : ScriptableObject
    {
        [Header("Generation")]
        [SerializeField, Tooltip("Seed for any randomness used by this generator.")]
        private int seed = 12345;

        public int Seed => seed;

        /// <summary>Generates geometry using the asset's own seed.</summary>
        public MeshData Generate()
        {
            return Generate(seed);
        }

        /// <summary>Generates geometry using an explicit seed (e.g. one per plant instance).</summary>
        public MeshData Generate(int seedOverride)
        {
            var data = new MeshData();
            Build(data, new System.Random(seedOverride));
            return data;
        }

        /// <summary>
        /// Write geometry into <paramref name="target"/>. Use <paramref name="random"/> for all randomness
        /// (never UnityEngine.Random) so results stay reproducible.
        /// </summary>
        protected abstract void Build(MeshData target, System.Random random);
    }
}
