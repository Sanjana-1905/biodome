using System;
using System.Collections.Generic;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Shares growth-stage meshes between plants. Plants of one variant that reach the same growth step use one
    /// Mesh, so a large cohort costs a handful of meshes instead of one per plant. Meshes are built lazily, a
    /// limited number per frame, and are destroyed when no plant uses them any more.
    /// Plain C# object owned by the GreenhouseManager (no singleton).
    /// </summary>
    public sealed class PlantMeshLibrary
    {
        internal sealed class Entry
        {
            public (Type type, int variant, int step) Key;
            public PlantGenerator Source;
            public int Step;
            public Mesh Mesh;
            public int Users;
            public bool Queued;
        }

        /// <summary>A plant's claim on one shared mesh. Release it when the plant moves on or is destroyed.</summary>
        public sealed class Handle
        {
            internal Entry Entry;
            public int Step => Entry.Step;
            public bool Ready => Entry.Mesh != null;
            public Mesh Mesh => Entry.Mesh;
        }

        private readonly Dictionary<(Type, int, int), Entry> entries = new Dictionary<(Type, int, int), Entry>();
        private readonly Queue<Entry> queue = new Queue<Entry>();

        public int MeshCount
        {
            get
            {
                int count = 0;
                foreach (Entry e in entries.Values) if (e.Mesh != null) count++;
                return count;
            }
        }

        public int PendingCount => queue.Count;

        /// <summary>Asks for the mesh of <paramref name="source"/>'s shape at a growth step. It is built on a later Process call.</summary>
        public Handle Request(PlantGenerator source, int step)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var key = (source.GetType(), source.VariantKey, step);
            if (!entries.TryGetValue(key, out Entry entry))
            {
                entry = new Entry { Key = key, Step = step };
                entries.Add(key, entry);
            }

            entry.Source = source; // keep a live source in case an earlier one was destroyed
            entry.Users++;

            if (entry.Mesh == null && !entry.Queued)
            {
                entry.Queued = true;
                queue.Enqueue(entry);
            }
            return new Handle { Entry = entry };
        }

        public void Release(Handle handle)
        {
            if (handle == null || handle.Entry == null) return;

            Entry entry = handle.Entry;
            handle.Entry = null;
            entry.Users--;
            if (entry.Users > 0) return;

            if (entry.Mesh != null)
            {
                UnityEngine.Object.Destroy(entry.Mesh);
                entry.Mesh = null;
                entries.Remove(entry.Key);
            }
            // An unbuilt entry stays queued and is dropped in Process.
        }

        /// <summary>Builds up to <paramref name="budget"/> queued meshes. Returns how many were built.</summary>
        public int Process(int budget)
        {
            int built = 0;
            while (built < budget && queue.Count > 0)
            {
                Entry entry = queue.Dequeue();
                entry.Queued = false;

                if (entry.Users <= 0)
                {
                    entries.Remove(entry.Key);
                    continue;
                }
                if (entry.Source == null) continue; // source destroyed; another request will refresh it

                var mesh = new Mesh();
                entry.Source.BuildMeshInto(mesh, PlantGenerator.GrowthOfStep(PlantGenerator.StepOfCode(entry.Step)), PlantGenerator.BloomLevelOfCode(entry.Step) / (float)PlantGenerator.BloomLevels);
                entry.Mesh = mesh;
                built++;
            }
            return built;
        }

        public void Dispose()
        {
            foreach (Entry entry in entries.Values)
            {
                if (entry.Mesh != null) UnityEngine.Object.Destroy(entry.Mesh);
                entry.Mesh = null;
            }
            entries.Clear();
            queue.Clear();
        }
    }
}
