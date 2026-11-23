using System;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Simulation;
using UnityEngine;

namespace BioDome.Plants
{
    /// <summary>
    /// Scene-side wrapper around one plant. Owns a PlantSimulation (the numbers) and presents it
    /// visually (scale and health tint). It does not decide when time passes: the GreenhouseManager
    /// calls <see cref="Tick"/>. Add a Collider so the CameraController can pick this plant.
    /// </summary>
    public class PlantController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
        private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in / fallback

        [Header("Configuration")]
        [SerializeField] private PlantData data;
        [SerializeField, Range(0f, 1f)] private float initialGrowth01 = 0.1f;
        [SerializeField, Range(0f, 1f)] private float initialWater01 = 0.6f;
        [SerializeField, Range(-1f, 1f), Tooltip("Growth this plant starts with. -1 (the default) = use the shared planting cohort's start, as every original plant does. " +
            "A value from 0 to 1 starts this plant at that growth instead (mature landscaping, pre-grown experiment plants).")]
        private float startGrowth01 = -1f;
        [SerializeField, Tooltip("When a PlantGenerator is present, growth is shown by swapping to the matching growth-stage mesh instead of scaling the plant.")]
        private bool meshDrivenGrowth = true;

        [Header("Zone (optional)")]
        [SerializeField, Tooltip("Climate adjustment for this plant's zone. Leave empty for normal greenhouse conditions (all display plants).")]
        private ZoneProfile zoneProfile;

        [SerializeField, Min(0f), Tooltip("Fraction of an equal irrigation share this plant receives. 1 = normal (default). Lower = gets less, without affecting other plants.")]
        private float waterShareWeight = 1f;
        [SerializeField, Tooltip("True when this plant is watered by its own irrigation controller instead of the greenhouse's main one (experiment pods).")]
        private bool usesOwnIrrigation;

        [Header("Presentation")]
        [SerializeField, Tooltip("Transform scaled according to growth. Defaults to this transform.")]
        private Transform visualRoot;
        [SerializeField, Tooltip("Renderers tinted by health state. Leave empty to disable tinting.")]
        private Renderer[] tintRenderers = Array.Empty<Renderer>();
        [SerializeField, Range(0.01f, 1f)] private float minScaleFraction = 0.1f;
        [SerializeField] private float scaleSmoothing = 4f;
        [SerializeField] private Color healthyTint = Color.white;
        [SerializeField] private Color stressedTint = new Color(0.85f, 0.8f, 0.4f);
        [SerializeField] private Color criticalTint = new Color(0.65f, 0.4f, 0.2f);
        [SerializeField] private Color deadTint = new Color(0.3f, 0.25f, 0.2f);

        private PlantSimulation simulation;
        private MaterialPropertyBlock propertyBlock;
        private PlantHealthState lastPublishedState;
        private PlantGrowthStage lastStage;
        private int lastLookKey = int.MinValue, lastPodKey = int.MinValue;
        // Pod plants only (they have a zone profile): visual response to the individual stress factors, eased over simulated hours so it builds up and recovers gradually.
        private float podHeat, podDry, podDroop, podDark, podDecline;
        private const float PodLookHours = 10f;
        private float displayedStemDecay; // eases towards StemDecayTarget over simulated hours, so death browns the stems gradually
        private const float StemDecayHours = 12f;
        private Transform meshTransform;
        private float swapBlend = 1f; // size ratio previous mesh / new mesh right after a swap, easing back to 1

        private PlantGenerator generator;
        private PlantMeshLibrary meshLibrary;
        private PlantMeshLibrary.Handle currentMesh;
        private PlantMeshLibrary.Handle pendingMesh;
        private int displayedStep;

        /// <summary>Raised when the discrete health state changes (including death).</summary>
        public event Action<PlantController, PlantHealthState> HealthStateChanged;

        /// <summary>Raised when the plant enters a new growth stage (Seedling, Juvenile, Established, Mature).</summary>
        public event Action<PlantController, PlantGrowthStage> GrowthStageChanged;

        public PlantData Data => data;

        /// <summary>The zone climate adjustment, or null when the plant lives in the normal greenhouse climate.</summary>
        public ZoneProfile ZoneProfile => zoneProfile;

        /// <summary>The conditions this plant's simulation received on its most recent tick (after its zone profile was applied).</summary>
        public EnvironmentConditions LastConditions { get; private set; }

        /// <summary>
        /// Swaps the zone profile while running, WITHOUT resetting the plant (used by the live pod test panel, which gives each pod its own runtime copy).
        /// The plant's irrigation share and water intake follow the new profile.
        /// </summary>
        public void SetZoneProfileLive(ZoneProfile profile)
        {
            zoneProfile = profile;
            ApplyProfileWaterSettings();
        }

        /// <summary>Re-reads irrigation share and intake from the current profile (call after the profile's values change).</summary>
        public void ApplyProfileWaterSettings()
        {
            if (zoneProfile == null) return;
            waterShareWeight = zoneProfile.WaterShareWeight;
            if (simulation != null) simulation.WaterIntakeFactor = zoneProfile.WaterIntakeFactor;
        }

        /// <summary>Relative share of irrigation water (default 1).</summary>
        public float WaterShareWeight => waterShareWeight;

        /// <summary>True when an experiment pod's own irrigation controller waters this plant, so the main one must not.</summary>
        public bool UsesOwnIrrigation => usesOwnIrrigation;

        public void SetWaterShareWeight(float weight) => waterShareWeight = Mathf.Max(0f, weight);

        /// <summary>Shared experiment start state, or null for ordinary plants.</summary>
        public ExperimentStartState ExperimentStart { get; private set; }
        public PlantSimulation Simulation => simulation;
        public bool IsInitialized => simulation != null;

        /// <summary>Growth this plant starts with, or a negative value when it uses the shared cohort start.</summary>
        public float StartGrowth01 => startGrowth01;
        private bool UsesMeshGrowth => meshDrivenGrowth && generator != null;

        private void Awake()
        {
            if (visualRoot == null) visualRoot = transform;
            propertyBlock = new MaterialPropertyBlock();
            generator = GetComponentInChildren<PlantGenerator>(true);

            if (data != null)
            {
                Initialize(data);
            }
            else
            {
                Debug.LogError($"{nameof(PlantController)} on '{name}' has no {nameof(PlantData)} assigned.", this);
            }
        }

        // --- Growth-stage preview (visual only) ------------------------------------------------
        private float previewTarget = -1f; // growth the player asked to see (0..1), or -1 = live
        private float previewShown = -1f;  // growth currently displayed while a preview is active or easing back to live (-1 = not previewing)
        private const float PreviewSpeed = 0.45f; // growth units per real second: a seedling grows into a mature plant in a couple of seconds

        /// <summary>True while a preview is shown or easing back to the live simulation.</summary>
        public bool IsPreviewingGrowth => previewShown >= 0f;

        /// <summary>
        /// Shows this plant at the given growth (0..1) WITHOUT touching the simulation: growth, health, moisture, stress and plant data are not modified.
        /// The displayed size glides there through the real growth meshes (no pop). Same species, same position.
        /// </summary>
        public void SetGrowthPreview(float growth01)
        {
            if (simulation == null || !UsesMeshGrowth) return;
            previewTarget = Mathf.Clamp01(growth01);
            if (previewShown < 0f) previewShown = simulation.Growth01;
        }

        /// <summary>Returns to the live simulation (the display glides back to the plant's real growth).</summary>
        public void ClearGrowthPreview() { previewTarget = -1f; }

        private float DisplayGrowth01 => previewShown >= 0f ? previewShown : simulation.Growth01;

        private void UpdatePreview()
        {
            if (previewShown < 0f || simulation == null) return;
            float target = previewTarget >= 0f ? previewTarget : simulation.Growth01;
            previewShown = Mathf.MoveTowards(previewShown, target, PreviewSpeed * Time.unscaledDeltaTime);
            if (previewTarget < 0f && Mathf.Abs(previewShown - simulation.Growth01) < 0.003f) previewShown = -1f;
            UpdateVisualGrowth(); // the simulation may be paused, so the preview drives its own mesh updates
        }

        private void Update()
        {
            UpdatePreview();
            if (simulation != null && UsesMeshGrowth && swapBlend != 1f)
            {
                // After a growth-step mesh swap the plant starts at the size of the previous mesh and eases to the new one (no visible pop).
                swapBlend = Mathf.Abs(swapBlend - 1f) < 0.002f ? 1f : Mathf.Lerp(swapBlend, 1f, 1f - Mathf.Exp(-5f * Time.deltaTime));
                ApplyWilt();
            }
            if (simulation == null || visualRoot == null || UsesMeshGrowth) return;

            float target = Mathf.Lerp(minScaleFraction, 1f, simulation.Growth01) * data.MaxScale;
            Vector3 targetScale = Vector3.one * target;
            float t = 1f - Mathf.Exp(-scaleSmoothing * Time.deltaTime);
            visualRoot.localScale = Vector3.Lerp(visualRoot.localScale, targetScale, t);
        }

        /// <summary>Creates (or recreates) the simulation. Call this when spawning plants at runtime.</summary>
        public void Initialize(PlantData plantData)
        {
            if (plantData == null)
            {
                Debug.LogError($"{nameof(PlantController)}.{nameof(Initialize)} called with null {nameof(PlantData)}.", this);
                return;
            }

            data = plantData;
            if (visualRoot == null) visualRoot = transform;
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

            ExperimentStartState experiment = ExperimentStart;
            simulation = experiment != null
                ? new PlantSimulation(data, experiment.Growth01, experiment.Health01, experiment.Water01)
                : new PlantSimulation(data, startGrowth01 >= 0f ? startGrowth01 : initialGrowth01, 1f, initialWater01);
            simulation.WaterIntakeFactor = zoneProfile != null ? zoneProfile.WaterIntakeFactor : 1f;
            lastPublishedState = simulation.State;

            lastStage = simulation.Stage;
            displayedStep = generator != null ? PlantGenerator.MeshCode(PlantGenerator.StepOf(generator.Growth01), PlantGenerator.BloomLevels) : 0; // the saved mesh is in full bloom

            if (!UsesMeshGrowth)
            {
                float start = Mathf.Lerp(minScaleFraction, 1f, simulation.Growth01) * data.MaxScale;
                visualRoot.localScale = Vector3.one * start;
            }
            displayedStemDecay = StemDecayTarget();
            lastLookKey = int.MinValue;
            ApplyTint();
            ApplyWilt();
        }

        /// <summary>
        /// Starts this plant from a shared planting cohort: same initial growth as every other plant in it.
        /// Call before the first Tick. Resets the simulation (health and soil moisture return to their start values).
        /// </summary>
        public void Plant(PlantingCohort cohort)
        {
            if (cohort == null || data == null) return;
            if (ExperimentStart != null) { Initialize(data); return; } // experimental plants keep their shared experiment start
            initialGrowth01 = cohort.InitialGrowth01;
            Initialize(data);
        }

        /// <summary>
        /// Makes this an experimental plant: it uses <paramref name="start"/> (shared by every experimental plant) as its initial state
        /// and lives in the climate described by <paramref name="profile"/>. Resets the simulation.
        /// </summary>
        public void ConfigureExperiment(ExperimentStartState start, ZoneProfile profile)
        {
            if (start == null || start.Data == null) return;
            ExperimentStart = start;
            zoneProfile = profile;
            waterShareWeight = profile != null ? profile.WaterShareWeight : 1f;
            usesOwnIrrigation = true;
            Initialize(start.Data);
        }

        /// <summary>Provides the shared growth-stage mesh library (created by the GreenhouseManager).</summary>
        public void SetMeshLibrary(PlantMeshLibrary library)
        {
            ReleaseMeshes();
            meshLibrary = library;
        }

        private void OnDestroy()
        {
            ReleaseMeshes();
        }

        private void ReleaseMeshes()
        {
            if (meshLibrary == null) return;
            meshLibrary.Release(currentMesh);
            meshLibrary.Release(pendingMesh);
            currentMesh = null;
            pendingMesh = null;
        }

        /// <summary>Swaps to the shared mesh for the current growth step once it is ready.</summary>
        private void UpdateVisualGrowth()
        {
            if (!UsesMeshGrowth || meshLibrary == null) return;

            float shownGrowth = DisplayGrowth01;
            int bloomLevel = data.CanFlower ? PlantGenerator.BloomLevelOf(simulation.Bloom01) : PlantGenerator.BloomLevels;
            if (previewShown >= 0f && data.CanFlower) bloomLevel = shownGrowth >= data.FloweringMinGrowth ? PlantGenerator.BloomLevels : 0; // a young plant has no flowers
            int step = PlantGenerator.MeshCode(PlantGenerator.StepOf(shownGrowth), bloomLevel);
            int target = pendingMesh != null ? pendingMesh.Step : displayedStep;
            if (step != target)
            {
                meshLibrary.Release(pendingMesh);
                pendingMesh = meshLibrary.Request(generator, step);
            }

            if (pendingMesh != null && pendingMesh.Ready)
            {
                PlantMeshLibrary.Handle old = currentMesh;
                currentMesh = pendingMesh;
                pendingMesh = null;
                displayedStep = currentMesh.Step;
                float oldSize = old != null && old.Mesh != null ? old.Mesh.bounds.size.y : 0f, newSize = currentMesh.Mesh.bounds.size.y;
                generator.DisplayMesh(currentMesh.Mesh, PlantGenerator.GrowthOfStep(PlantGenerator.StepOfCode(displayedStep)));
                swapBlend = oldSize > 0.01f && newSize > 0.01f ? Mathf.Clamp(oldSize / newSize, 0.6f, 1.6f) : 1f;
                ApplyWilt();
                meshLibrary.Release(old);
            }
        }

        /// <summary>Requests (and, if already built, shows) the mesh for the current growth. Used once at start so plants never appear at a wrong size.</summary>
        public void SyncVisualNow()
        {
            if (simulation != null) UpdateVisualGrowth();
        }

        /// <summary>Advance the simulation. Called by the GreenhouseManager once per frame.</summary>
        public void Tick(float deltaHours, in EnvironmentConditions environment)
        {
            if (simulation == null) return;

            LastConditions = environment;
            simulation.Tick(deltaHours, environment);

            if (simulation.State != lastPublishedState)
            {
                lastPublishedState = simulation.State;
                ApplyTint();
                HealthStateChanged?.Invoke(this, lastPublishedState);
            }

            displayedStemDecay += (StemDecayTarget() - displayedStemDecay) * (1f - Mathf.Exp(-deltaHours / StemDecayHours));
            UpdatePodLook(deltaHours);

            // Continuous visual response: re-apply only when health, wilting or stem decay has moved by about 2 %.
            int look = Mathf.RoundToInt(simulation.Health01 * 50f) + 100 * Mathf.RoundToInt(simulation.Wilt01 * 50f) + 10000 * Mathf.RoundToInt(displayedStemDecay * 50f);
            int podKey = Mathf.RoundToInt(podHeat * 20f) + 21 * Mathf.RoundToInt(podDry * 20f) + 441 * Mathf.RoundToInt(podDroop * 20f) + 9261 * Mathf.RoundToInt(podDark * 20f) + 194481 * Mathf.RoundToInt(podDecline * 20f);
            if (look != lastLookKey || podKey != lastPodKey)
            {
                lastLookKey = look;
                lastPodKey = podKey;
                ApplyTint();
                ApplyWilt();
            }

            PlantGrowthStage stage = simulation.Stage;
            if (stage != lastStage)
            {
                lastStage = stage;
                GrowthStageChanged?.Invoke(this, stage);
            }

            UpdateVisualGrowth();
        }

        /// <summary>Adds water to this plant's soil. Returns litres actually accepted.</summary>
        public float ReceiveWater(float litres)
        {
            return simulation != null ? simulation.AddWater(litres) : 0f;
        }

        private void ApplyTint()
        {
            if (simulation == null) return;

            if (generator != null)
            {
                // Generated plants blend towards natural yellow/brown (keeps per-variant leaf colours); stems only change when the plant dies.
                generator.SetCondition(FoliageDecline(), displayedStemDecay, WiltDull());
                return;
            }

            Color tint = TintFor(simulation.State);

            if (tintRenderers == null) return;
            propertyBlock.SetColor(BaseColorId, tint);
            propertyBlock.SetColor(ColorId, tint);

            for (int i = 0; i < tintRenderers.Length; i++)
            {
                Renderer r = tintRenderers[i];
                if (r != null) r.SetPropertyBlock(propertyBlock);
            }
        }

        // Leaves: unchanged while healthy, yellowing through "stressed", brown and dry towards zero health; dead = fully dry.
        private float FoliageDecline()
        {
            if (simulation.State == PlantHealthState.Dead) return 1f;
            float h = simulation.Health01;
            float fromHealth = h >= data.HealthyThreshold ? 0f : Mathf.InverseLerp(data.HealthyThreshold, 0f, h);
            return Mathf.Max(fromHealth, podDecline);
        }

        /// <summary>
        /// Pod plants show each stress factor on the plant itself, from the live simulation values: heat curls the canopy and yellows the leaves, dry air dulls and
        /// browns the leaf edges, a drying soil droops the leaves before real wilting starts, and low light darkens and stretches the foliage. All eased over
        /// simulated hours (so it builds gradually and recovers when conditions recover). Display plants (no zone profile) are not touched.
        /// </summary>
        private void UpdatePodLook(float deltaHours)
        {
            if (zoneProfile == null) return;
            float k = 1f - Mathf.Exp(-deltaHours / PodLookHours);
            bool hot = LastConditions.TemperatureCelsius > data.IdealTemperatureCelsius.y;
            float heat = hot ? simulation.TemperatureStress01 : 0f;
            float dry = simulation.HumidityStress01;
            float preDroop = Mathf.Clamp01((0.45f - simulation.Water01) / 0.2f) * 0.7f; // starts just before the soil is truly short of water (below the ideal 35 %)
            float dark = Mathf.Clamp01((data.IdealLight01.x + 0.15f - simulation.AcclimatedLight01) / 0.30f);
            float decline = Mathf.Clamp01(0.55f * heat + 1.5f * dry + 1.2f * simulation.WaterStress01 + 0.7f * preDroop);
            podHeat += (heat - podHeat) * k;
            podDry += (dry - podDry) * k;
            podDroop += (preDroop - podDroop) * k;
            podDark += (dark - podDark) * k;
            podDecline += (decline - podDecline) * k;
        }

        // Stems: stay living green unless the plant is close to death (a little below 15 % health) and turn fully woody once it is Dead.
        private float StemDecayTarget()
        {
            if (simulation.State == PlantHealthState.Dead) return 1f;
            return 0.3f * Mathf.InverseLerp(0.15f, 0f, simulation.Health01);
        }

        private Color WiltDull()
        {
            Color c = Color.Lerp(Color.white, new Color(0.9f, 0.87f, 0.68f), simulation.Wilt01 * 0.6f);
            return podDark > 0.001f ? c * Color.Lerp(Color.white, new Color(0.40f, 0.54f, 0.52f), Mathf.Clamp01(podDark * 1.3f)) : c; // low light: deeper, cooler, darker foliage
        }

        private Color TintFor(PlantHealthState state)
        {
            if (simulation == null || state == PlantHealthState.Dead) return deadTint;

            // Smooth colour from health: full colour when healthy, easing to a yellow-olive when stressed and to brown when critical.
            float h = simulation.Health01;
            Color c;
            if (h >= data.HealthyThreshold) c = healthyTint;
            else if (h >= data.StressedThreshold) c = Color.Lerp(stressedTint, healthyTint, Mathf.InverseLerp(data.StressedThreshold, data.HealthyThreshold, h));
            else c = Color.Lerp(criticalTint, stressedTint, Mathf.InverseLerp(0f, data.StressedThreshold, h));

            // Wilting dulls the foliage a little (subtle, never cartoonish).
            return c * Color.Lerp(Color.white, new Color(0.9f, 0.87f, 0.68f), simulation.Wilt01 * 0.6f);
        }

        /// <summary>Droops the plant: a species-specific loss of height and a slight spread of the canopy, scaled by the plant's wilting.</summary>
        private void ApplyWilt()
        {
            if (simulation == null || generator == null) return;
            if (meshTransform == null) meshTransform = generator.transform.Find("Mesh");
            if (meshTransform == null) return;

            float w = Mathf.Max(simulation.Wilt01, podDroop);
            float sag = data.WiltSag * w;
            float spread = data.WiltSpread * w;
            // Pod plants: heat and dry air curl the canopy in; low light stretches it (etiolation). Zero for display plants.
            float curl = 0.10f * podHeat + 0.18f * podDry + 0.12f * podDroop;
            float height = 1f - sag - 0.04f * podHeat - 0.08f * podDroop - 0.06f * podDry + 0.15f * podDark;
            float width = 1f + spread - curl - 0.08f * podDark;
            meshTransform.localScale = new Vector3(width, height, width) * swapBlend;
        }
    }
}
