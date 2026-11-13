using UnityEngine;

namespace BioDome.Procedural
{
    /// <summary>
    /// Scene component that runs a <see cref="ProceduralMeshGenerator"/> and puts the result on this
    /// object's MeshFilter. It owns the generated Mesh and destroys it when replaced or when the
    /// object is destroyed, so rebuilding does not leak meshes.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [DisallowMultipleComponent]
    public class ProceduralMeshBuilder : MonoBehaviour
    {
        [SerializeField, Tooltip("The recipe to build.")]
        private ProceduralMeshGenerator generator;
        [SerializeField] private bool buildOnStart = true;
        [SerializeField, Tooltip("Use the seed below instead of the generator asset's own seed (e.g. per-plant variation).")]
        private bool overrideSeed;
        [SerializeField] private int seedOverride;

        private MeshFilter meshFilter;
        private Mesh generatedMesh;

        public ProceduralMeshGenerator Generator => generator;

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        private void Start()
        {
            if (buildOnStart) Rebuild();
        }

        private void OnDestroy()
        {
            ReleaseMesh();
        }

        /// <summary>Assigns a new recipe and rebuilds immediately.</summary>
        public void SetGenerator(ProceduralMeshGenerator newGenerator, int? seed = null)
        {
            generator = newGenerator;
            if (seed.HasValue)
            {
                overrideSeed = true;
                seedOverride = seed.Value;
            }
            Rebuild();
        }

        /// <summary>Regenerates the mesh. Also available from the component's context menu.</summary>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();

            if (generator == null)
            {
                Debug.LogWarning($"{nameof(ProceduralMeshBuilder)} on '{name}' has no generator assigned.", this);
                return;
            }

            MeshData data = overrideSeed ? generator.Generate(seedOverride) : generator.Generate();

            ReleaseMesh();
            generatedMesh = new Mesh { name = $"{generator.name} (procedural)" };
            data.ApplyTo(generatedMesh);
            meshFilter.sharedMesh = generatedMesh;
        }

        private void ReleaseMesh()
        {
            if (generatedMesh == null) return;

            if (meshFilter != null && meshFilter.sharedMesh == generatedMesh) meshFilter.sharedMesh = null;

            if (Application.isPlaying) Destroy(generatedMesh);
            else DestroyImmediate(generatedMesh);

            generatedMesh = null;
        }
    }
}
