using BioDome.Procedural;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>Everything a concrete plant needs to build its geometry. Created by <see cref="PlantGenerator"/>.</summary>
    public sealed class PlantBuildContext
    {
        public const int LeafVariantCount = 3;
        public const int StemSubmesh = 3;
        public const int AccentSubmesh = 4;
        /// <summary>Second flower colour (for example a spadix or a lip). Species that do not use it never create this submesh.</summary>
        public const int Accent2Submesh = 5;

        public MeshData Data;
        public System.Random Rng;
        /// <summary>Growth 0 to 1, already quantised so tiny changes do not regenerate the mesh.</summary>
        public float Growth;
        /// <summary>Flowering 0 to 1, quantised to four levels. 1 (the default) = the full, original flowering.</summary>
        public float Bloom = 1f;
        public float Height;
        public float Width;
        public int LeafCount;
        public float LeafLength;
        public float LeafWidth;
        public int LeafLengthSegments;
        public int LeafHalfWidthSegments;
    }

    /// <summary>
    /// Base class for procedural plants. Builds one merged mesh (one child object, a few submeshes) from
    /// shared parameters and a seed, then applies shared materials with per-submesh colour tints.
    ///
    /// Submesh layout: 0-2 leaf variants, 3 stems, 4 accents (flowers). One leaf material is reused for all
    /// variants; colour variation uses material property blocks, so no materials are created per plant or per leaf.
    /// The mesh is only rebuilt when a parameter that affects the shape changes (tracked with a hash).
    /// Subclasses implement <see cref="Build"/>.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public abstract class PlantGenerator : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        /// <summary>Growth is quantised to this many steps; the mesh is only rebuilt when the step changes.</summary>
        public const int GrowthSteps = 20;

        public static int StepOf(float growth01)
        {
            return Mathf.Clamp(Mathf.RoundToInt(growth01 * GrowthSteps), 0, GrowthSteps);
        }

        public static float GrowthOfStep(int step)
        {
            return Mathf.Clamp(step, 0, GrowthSteps) / (float)GrowthSteps;
        }

        /// <summary>Flower levels: 0 = none, 3 = full bloom (the original meshes).</summary>
        public const int BloomLevels = 3;

        /// <summary>One number identifying a shared mesh: growth step plus bloom level (growth steps are below 32).</summary>
        public static int MeshCode(int growthStep, int bloomLevel) => growthStep + 32 * Mathf.Clamp(bloomLevel, 0, BloomLevels);
        public static int StepOfCode(int code) => code & 31;
        public static int BloomLevelOfCode(int code) => code >> 5;
        public static int BloomLevelOf(float bloom01) => Mathf.Clamp(Mathf.RoundToInt(bloom01 * BloomLevels), 0, BloomLevels);

        [Header("Size")]
        [SerializeField, Min(0.1f)] protected float height = 1.2f;
        [SerializeField, Min(0.1f)] protected float width = 1.2f;

        [Header("Leaves")]
        [SerializeField, Range(2, 40)] protected int leafCount = 9;
        [SerializeField, Min(0.02f)] protected float leafLength = 0.5f;
        [SerializeField, Min(0.02f)] protected float leafWidth = 0.42f;
        [SerializeField] protected Color leafColor = new Color(0.16f, 0.42f, 0.14f);
        [SerializeField, Range(0f, 1f), Tooltip("How strongly the three leaf variants differ in colour.")]
        protected float colorVariation = 0.7f;

        [Header("Growth and Variation")]
        [SerializeField, Range(0f, 1f)] protected float growth = 1f;
        [SerializeField] protected int seed = 1234;
        [SerializeField, Range(1, 3), Tooltip("Mesh resolution: 1 low, 2 medium, 3 high.")]
        protected int detail = 2;

        [Header("Materials (shared assets)")]
        [SerializeField] protected Material leafMaterial;
        [SerializeField, Tooltip("Optional different materials for leaf variants 2 and 3. Empty entries use the leaf material.")]
        protected Material[] leafVariantMaterials = new Material[0];
        [SerializeField] protected Material stemMaterial;
        [SerializeField] protected Material accentMaterial;
        [SerializeField, Tooltip("Optional second accent material (submesh 5), e.g. a spadix or an orchid lip. Falls back to the accent material.")]
        protected Material accent2Material;

        [Header("Collider")]
        [SerializeField] protected bool fitCollider = true;

        [SerializeField, HideInInspector] private MeshFilter meshFilter;
        [SerializeField, HideInInspector] private MeshRenderer meshRenderer;
        [SerializeField, HideInInspector] private Mesh builtMesh;
        [SerializeField, HideInInspector] private int builtHash;

        private MaterialPropertyBlock block;
        // Not serialised on purpose: a duplicated object or a reloaded domain never owns the mesh, so it builds its own.
        private Mesh ownedMesh;
        private Color healthTint = Color.white;
        private float leafDecline;   // 0 = healthy foliage, 0.5 = yellowed, 1 = brown and dry
        private float stemDecay;     // 0 = living stems, 1 = dead woody stems (only reached when the whole plant is dead)

        // Natural deterioration colours: they replace the species colour by blending (multiplying a dark green by brown would give black).
        private static readonly Color DryYellow = new Color(0.66f, 0.58f, 0.24f);
        private static readonly Color DryBrown = new Color(0.44f, 0.31f, 0.16f);
        private static readonly Color DeadWood = new Color(0.34f, 0.25f, 0.16f);

        private static readonly Color[] VariantMultipliers =
        {
            new Color(1f, 1f, 1f),
            new Color(0.82f, 1.06f, 0.78f),
            new Color(1.12f, 1.02f, 0.7f)
        };

        public float Growth01 => growth;
        public int Seed => seed;
        public Mesh BuiltMesh => builtMesh;
        public MeshRenderer Renderer => meshRenderer;

        /// <summary>Bump when a subclass changes its algorithm so existing plants regenerate.</summary>
        protected virtual int AlgorithmVersion => 1;

        /// <summary>Writes the plant geometry into <see cref="PlantBuildContext.Data"/>.</summary>
        protected abstract void Build(PlantBuildContext context);

        /// <summary>Subclasses set species-specific default values (called from Reset and by editor tools).</summary>
        protected virtual void ApplyDefaults() { }

        // --- Unity messages -------------------------------------------------------

        private void OnEnable()
        {
            if (Application.isPlaying) EnsureBuilt();
            ApplyAppearance(); // property blocks are not serialised, so reapply them
        }

        private void OnDestroy()
        {
            if (Application.isPlaying && builtMesh != null && builtMesh == ownedMesh && !IsAsset(builtMesh))
                Destroy(builtMesh);
        }

        private void Reset()
        {
            ApplyDefaults();
            AssignDefaultMaterials();
            if (!Application.isPlaying) Rebuild(true);
        }

        protected virtual void OnValidate()
        {
            height = Mathf.Max(0.1f, height);
            width = Mathf.Max(0.1f, width);
            leafLength = Mathf.Max(0.02f, leafLength);
            leafWidth = Mathf.Max(0.02f, leafWidth);
            ApplyAppearance(); // colour changes are cheap and need no rebuild
        }

        // --- Public API -----------------------------------------------------------

        public void SetMaterials(Material leaf, Material stem, Material accent = null)
        {
            leafMaterial = leaf;
            stemMaterial = stem;
            if (accent != null) accentMaterial = accent;
        }

        /// <summary>Sets the flower materials (submeshes 4 and 5). Null leaves the current value unchanged.</summary>
        public void SetAccentMaterials(Material accent, Material accent2)
        {
            if (accent != null) accentMaterial = accent;
            if (accent2 != null) accent2Material = accent2;
        }

        public bool HasAccentMaterial => accentMaterial != null;
        public bool HasAccent2Material => accent2Material != null;

        public void Configure(float newHeight, float newWidth, int newLeafCount, float newLeafLength, float newLeafWidth,
            float newGrowth, int newSeed)
        {
            height = Mathf.Max(0.1f, newHeight);
            width = Mathf.Max(0.1f, newWidth);
            leafCount = Mathf.Clamp(newLeafCount, 2, 40);
            leafLength = Mathf.Max(0.02f, newLeafLength);
            leafWidth = Mathf.Max(0.02f, newLeafWidth);
            growth = Mathf.Clamp01(newGrowth);
            seed = newSeed;
        }

        /// <summary>Sets only the growth and seed (used when creating species prefabs from the generator's own defaults).</summary>
        public void SetGrowthAndSeed(float newGrowth, int newSeed)
        {
            growth = Mathf.Clamp01(newGrowth);
            seed = newSeed;
        }

        public void SetLeafColor(Color color)
        {
            leafColor = color;
            ApplyAppearance();
        }

        public void RandomizeSeed()
        {
            seed = Random.Range(1, 1000000);
        }

        /// <summary>Sets growth and rebuilds only when the quantised value changes.</summary>
        public void SetGrowth(float value)
        {
            growth = Mathf.Clamp01(value);
            Rebuild(false);
        }

        public void ApplyDefaultSettings()
        {
            ApplyDefaults();
        }

        /// <summary>
        /// Key identifying this plant's shape independent of growth: plants with equal keys can share growth-stage meshes.
        /// </summary>
        public int VariantKey
        {
            get
            {
                int h = 17;
                h = ProceduralMeshUtility.Combine(h, AlgorithmVersion);
                h = ProceduralMeshUtility.Combine(h, height);
                h = ProceduralMeshUtility.Combine(h, width);
                h = ProceduralMeshUtility.Combine(h, leafCount);
                h = ProceduralMeshUtility.Combine(h, leafLength);
                h = ProceduralMeshUtility.Combine(h, leafWidth);
                h = ProceduralMeshUtility.Combine(h, seed);
                h = ProceduralMeshUtility.Combine(h, detail);
                return AppendVariantKey(h);
            }
        }

        /// <summary>Subclasses fold their own shape settings into <see cref="VariantKey"/>.</summary>
        protected virtual int AppendVariantKey(int hash)
        {
            return hash;
        }

        /// <summary>
        /// Small, deterministic variation of this plant: seed, leaf count, proportions, growth and mesh detail.
        /// Used to make a few distinct variants of one species at the same growth stage.
        /// </summary>
        public void Vary(int newSeed, int leafCountDelta, float heightFactor, float widthFactor, float newGrowth, int newDetail)
        {
            seed = newSeed;
            leafCount = Mathf.Clamp(leafCount + leafCountDelta, 2, 40);
            height = Mathf.Max(0.1f, height * heightFactor);
            width = Mathf.Max(0.1f, width * widthFactor);
            growth = Mathf.Clamp01(newGrowth);
            detail = Mathf.Clamp(newDetail, 1, 3);
        }

        /// <summary>
        /// Builds this plant's geometry at <paramref name="growth01"/> into <paramref name="target"/> without touching
        /// this object's own mesh or renderer. Used to create meshes that many plants share.
        /// </summary>
        public void BuildMeshInto(Mesh target, float growth01, float bloom01 = 1f)
        {
            if (target == null) throw new System.ArgumentNullException(nameof(target));

            float quantised = GrowthOfStep(StepOf(growth01));
            PlantBuildContext context = NewContext(quantised);
            context.Bloom = BloomLevelOf(bloom01) / (float)BloomLevels;
            Build(context);
            context.Data.ApplyTo(target);
            target.name = $"{GetType().Name}_{seed}_g{StepOf(growth01)}";
        }

        /// <summary>Shows an existing (usually shared or asset) mesh without rebuilding. The generator does not own it.</summary>
        public void DisplayMesh(Mesh mesh, float growth01)
        {
            if (mesh == null) return;

            EnsureRenderer();
            if (ownedMesh != null && ownedMesh != mesh && !IsAsset(ownedMesh) && Application.isPlaying) Destroy(ownedMesh);
            ownedMesh = null;

            growth = Mathf.Clamp01(growth01);
            meshFilter.sharedMesh = mesh;
            builtMesh = mesh;
            builtHash = ComputeHash();

            ApplyMaterials(mesh.subMeshCount);
            ApplyAppearance();
            FitColliderTo(mesh.bounds);

#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>Multiplies the leaf colour, e.g. to show plant health. White = unchanged.</summary>
        public void SetHealthTint(Color tint)
        {
            healthTint = tint;
            ApplyAppearance();
        }

        /// <summary>
        /// Shows the plant's condition. <paramref name="foliageDecline01"/>: 0 healthy, 0.5 yellowed, 1 brown/dry leaves (some leaf variants decline earlier,
        /// so a damaged plant has a mix). <paramref name="stemDecay01"/>: 0 living green stems, 1 dead woody stems and faded flowers.
        /// <paramref name="dull"/> multiplies the result (used for a slight dulling while wilting). All zero/white = the normal species look.
        /// </summary>
        public void SetCondition(float foliageDecline01, float stemDecay01, Color dull)
        {
            leafDecline = Mathf.Clamp01(foliageDecline01);
            stemDecay = Mathf.Clamp01(stemDecay01);
            healthTint = dull;
            ApplyAppearance();
        }

        private Color LeafColor(int variant)
        {
            Color c = leafColor * Color.Lerp(Color.white, VariantMultipliers[variant], colorVariation);
            if (leafDecline > 0f)
            {
                float d = Mathf.Clamp01(leafDecline * (1f + 0.3f * variant)); // variant 2 declines first, variant 0 last
                c = Color.Lerp(c, DryYellow, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d * 2f)));
                c = Color.Lerp(c, DryBrown, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d * 2f - 1f)));
            }
            c *= healthTint;
            c.a = 1f;
            return c;
        }

        private static Color MaterialColor(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty(BaseColorId)) return m.GetColor(BaseColorId);
            if (m.HasProperty(ColorId)) return m.GetColor(ColorId);
            return Color.white;
        }

        /// <summary>Rebuilds the mesh if parameters changed (or when forced). Optionally writes into an existing mesh asset.</summary>
        [ContextMenu("Rebuild Plant")]
        public void Rebuild()
        {
            Rebuild(true);
        }

        public void Rebuild(bool force, Mesh targetMesh = null)
        {
            int hash = ComputeHash();
            if (!force && targetMesh == null && builtMesh != null && builtHash == hash)
            {
                ApplyAppearance();
                return;
            }

            if (leafMaterial == null || stemMaterial == null)
                Debug.LogWarning($"{name}: leaf or stem material is not assigned, so the plant will not render correctly.", this);

            var context = new PlantBuildContext
            {
                Data = new MeshData(),
                Rng = new System.Random(seed),
                Growth = QuantisedGrowth(),
                Height = height,
                Width = width,
                LeafCount = leafCount,
                LeafLength = leafLength,
                LeafWidth = leafWidth
            };
            ApplyDetail(context);
            Build(context);

            EnsureRenderer();

            Mesh mesh = targetMesh;
            if (mesh == null)
            {
                // Reuse our own in-memory mesh, but never modify a mesh asset or one shared with a duplicated object.
                bool reusable = builtMesh != null && builtMesh == ownedMesh && !IsAsset(builtMesh);
                mesh = reusable ? builtMesh : new Mesh();
                ownedMesh = mesh;
                mesh.name = $"{GetType().Name}_{seed}"; // only for meshes we create; an asset keeps its own file name
            }
            context.Data.ApplyTo(mesh);

            meshFilter.sharedMesh = mesh;
            builtMesh = mesh;
            builtHash = hash;

            ApplyMaterials(mesh.subMeshCount);
            ApplyAppearance();
            FitColliderTo(mesh.bounds);

#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        // --- Internals ------------------------------------------------------------

        private void EnsureBuilt()
        {
            if (builtMesh == null || builtHash != ComputeHash()) Rebuild(true);
        }

        private float QuantisedGrowth()
        {
            return Mathf.Round(growth * GrowthSteps) / GrowthSteps;
        }

        private PlantBuildContext NewContext(float growthValue)
        {
            var context = new PlantBuildContext
            {
                Data = new MeshData(),
                Rng = new System.Random(seed),
                Growth = growthValue,
                Height = height,
                Width = width,
                LeafCount = leafCount,
                LeafLength = leafLength,
                LeafWidth = leafWidth
            };
            ApplyDetail(context);
            return context;
        }

        private void ApplyDetail(PlantBuildContext c)
        {
            switch (detail)
            {
                case 1: c.LeafLengthSegments = 28; c.LeafHalfWidthSegments = 10; break;
                case 3: c.LeafLengthSegments = 56; c.LeafHalfWidthSegments = 20; break;
                default: c.LeafLengthSegments = 40; c.LeafHalfWidthSegments = 14; break;
            }
        }

        /// <summary>Hash of every setting that changes the geometry. Colours and materials are excluded on purpose.</summary>
        protected virtual int ComputeHash()
        {
            int h = 17;
            h = ProceduralMeshUtility.Combine(h, AlgorithmVersion);
            h = ProceduralMeshUtility.Combine(h, height);
            h = ProceduralMeshUtility.Combine(h, width);
            h = ProceduralMeshUtility.Combine(h, leafCount);
            h = ProceduralMeshUtility.Combine(h, leafLength);
            h = ProceduralMeshUtility.Combine(h, leafWidth);
            h = ProceduralMeshUtility.Combine(h, QuantisedGrowth());
            h = ProceduralMeshUtility.Combine(h, seed);
            h = ProceduralMeshUtility.Combine(h, detail);
            return h;
        }

        private void EnsureRenderer()
        {
            if (meshFilter != null && meshRenderer != null) return;

            Transform child = transform.Find("Mesh");
            GameObject go = child != null ? child.gameObject : new GameObject("Mesh");
            go.transform.SetParent(transform, false);

            meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = go.AddComponent<MeshFilter>();
            meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = go.AddComponent<MeshRenderer>();
        }

        /// <summary>The material this plant uses for a submesh (leaf variants, stem, flowers). Same rule the live mesh uses.</summary>
        public Material MaterialForSubmesh(int i)
        {
            if (i == PlantBuildContext.StemSubmesh) return stemMaterial;
            if (i == PlantBuildContext.AccentSubmesh) return accentMaterial != null ? accentMaterial : leafMaterial;
            if (i == PlantBuildContext.Accent2Submesh) return accent2Material != null ? accent2Material : (accentMaterial != null ? accentMaterial : leafMaterial);
            if (i < PlantBuildContext.LeafVariantCount && leafVariantMaterials != null && i < leafVariantMaterials.Length && leafVariantMaterials[i] != null)
                return leafVariantMaterials[i];
            return leafMaterial;
        }

        /// <summary>The colour tint a leaf-variant submesh (0 to 2) gets. Returns false for submeshes that are not tinted.</summary>
        public bool TryGetLeafTint(int submesh, out Color tint)
        {
            tint = Color.white;
            if (submesh < 0 || submesh >= PlantBuildContext.LeafVariantCount) return false;
            tint = LeafColor(submesh);
            return true;
        }

        private void ApplyMaterials(int submeshCount)
        {
            var materials = new Material[Mathf.Max(1, submeshCount)];
            for (int i = 0; i < materials.Length; i++) materials[i] = MaterialForSubmesh(i);
            meshRenderer.sharedMaterials = materials;
        }

        /// <summary>Applies per-variant leaf tints with a property block (no material instances are created).</summary>
        private void ApplyAppearance()
        {
            if (meshRenderer == null) return;
            int materialCount = meshRenderer.sharedMaterials.Length;
            if (materialCount == 0) return;

            if (block == null) block = new MaterialPropertyBlock();
            for (int i = 0; i < PlantBuildContext.LeafVariantCount && i < materialCount; i++)
            {
                Color tint = LeafColor(i);
                block.Clear();
                block.SetColor(BaseColorId, tint);
                block.SetColor(ColorId, tint);
                meshRenderer.SetPropertyBlock(block, i);
            }

            // Stems and flowers keep their material colours unless the plant is dying/dead; then they fade to dry woody brown.
            for (int i = PlantBuildContext.LeafVariantCount; i < materialCount; i++)
            {
                block.Clear();
                if (stemDecay > 0f)
                {
                    Color baseColor = MaterialColor(MaterialForSubmesh(i));
                    Color target = i == PlantBuildContext.StemSubmesh ? DeadWood : DryBrown;
                    Color c = Color.Lerp(baseColor, target, Mathf.SmoothStep(0f, 1f, stemDecay));
                    c.a = baseColor.a;
                    block.SetColor(BaseColorId, c);
                    block.SetColor(ColorId, c);
                }
                meshRenderer.SetPropertyBlock(block, i);
            }
        }

        private void FitColliderTo(Bounds bounds)
        {
            if (!fitCollider) return;
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, new Vector3(0.05f, 0.05f, 0.05f));
        }

        private void AssignDefaultMaterials()
        {
#if UNITY_EDITOR
            if (leafMaterial == null) leafMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/BioDome_Leaf.mat");
            if (stemMaterial == null) stemMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/BioDome_Stem.mat");
#endif
        }

        private static bool IsAsset(Object obj)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.IsPersistent(obj);
#else
            return false;
#endif
        }
    }
}
