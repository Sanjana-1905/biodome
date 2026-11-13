using System;
using BioDome.Core;
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

        /// <summary>Raised when the discrete health state changes (including death).</summary>
        public event Action<PlantController, PlantHealthState> HealthStateChanged;

        public PlantData Data => data;
        public PlantSimulation Simulation => simulation;
        public bool IsInitialized => simulation != null;

        private void Awake()
        {
            if (visualRoot == null) visualRoot = transform;
            propertyBlock = new MaterialPropertyBlock();

            if (data != null)
            {
                Initialize(data);
            }
            else
            {
                Debug.LogError($"{nameof(PlantController)} on '{name}' has no {nameof(PlantData)} assigned.", this);
            }
        }

        private void Update()
        {
            if (simulation == null || visualRoot == null) return;

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

            simulation = new PlantSimulation(data, initialGrowth01, 1f, initialWater01);
            lastPublishedState = simulation.State;

            float start = Mathf.Lerp(minScaleFraction, 1f, simulation.Growth01) * data.MaxScale;
            visualRoot.localScale = Vector3.one * start;
            ApplyTint();
        }

        /// <summary>Advance the simulation. Called by the GreenhouseManager once per frame.</summary>
        public void Tick(float deltaHours, in EnvironmentConditions environment)
        {
            if (simulation == null) return;

            simulation.Tick(deltaHours, environment);

            if (simulation.State != lastPublishedState)
            {
                lastPublishedState = simulation.State;
                ApplyTint();
                HealthStateChanged?.Invoke(this, lastPublishedState);
            }
        }

        /// <summary>Adds water to this plant's soil. Returns litres actually accepted.</summary>
        public float ReceiveWater(float litres)
        {
            return simulation != null ? simulation.AddWater(litres) : 0f;
        }

        private void ApplyTint()
        {
            if (tintRenderers == null || simulation == null) return;

            Color tint = TintFor(simulation.State);
            propertyBlock.SetColor(BaseColorId, tint);
            propertyBlock.SetColor(ColorId, tint);

            for (int i = 0; i < tintRenderers.Length; i++)
            {
                Renderer r = tintRenderers[i];
                if (r != null) r.SetPropertyBlock(propertyBlock);
            }
        }

        private Color TintFor(PlantHealthState state)
        {
            switch (state)
            {
                case PlantHealthState.Healthy: return healthyTint;
                case PlantHealthState.Stressed: return stressedTint;
                case PlantHealthState.Critical: return criticalTint;
                default: return deadTint;
            }
        }
    }
}
