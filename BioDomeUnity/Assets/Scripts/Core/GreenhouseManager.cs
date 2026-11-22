using System.Collections.Generic;
using BioDome.CameraSystem;
using BioDome.Environment;
using BioDome.Irrigation;
using BioDome.Plants;
using BioDome.Simulation;
using BioDome.UI;
using UnityEngine;

namespace BioDome.Core
{
    /// <summary>
    /// Composition root and simulation clock. It is the only script that knows about every other
    /// system: it ticks them in a fixed order, registers plants, and wires events between them
    /// (UI intent -> controllers, camera picks -> selection). The other systems never reference
    /// each other directly. This is a normal scene component, not a singleton.
    /// </summary>
    public class GreenhouseManager : MonoBehaviour
    {
        [Header("Systems (environment is required, the rest are optional)")]
        [SerializeField] private EnvironmentController environment;
        [SerializeField] private IrrigationController irrigation;
        [SerializeField, Tooltip("Extra irrigation controllers (experiment pods) ticked after the main one. Leave empty for none.")]
        private List<IrrigationController> extraIrrigations = new List<IrrigationController>();
        [SerializeField] private CameraController cameraController;
        [SerializeField] private UIManager uiManager;

        [Header("Plants")]
        [SerializeField, Tooltip("Plants managed by this greenhouse. Leave empty and enable Auto Discover to find them.")]
        private List<PlantController> plants = new List<PlantController>();
        [SerializeField] private bool autoDiscoverPlants = true;
        [SerializeField, Min(0.5f), Tooltip("Camera distance used when a plant is selected.")]
        private float plantFocusDistance = 3f;

        [Header("Simulation Clock")]
        [SerializeField, Min(0f), Tooltip("Simulated hours that pass per real second. 0.4 = a day in one minute.")]
        private float simulatedHoursPerSecond = 0.4f;
        [SerializeField] private bool paused;
        [SerializeField, Min(0.05f), Tooltip("Seconds between UI text refreshes.")]
        private float uiRefreshInterval = 0.2f;

        [Header("Planting Cohort")]
        [SerializeField, Range(0.05f, 0.3f), Tooltip("Growth every plant starts with, as a fraction of maturity. All plants are introduced together, so all share this value (0.10 to 0.15 is typical).")]
        private float initialGrowth01 = 0.12f;
        [SerializeField, Min(1), Tooltip("Growth-stage meshes built per frame. Lower is smoother, higher updates faster.")]
        private int meshBuildsPerFrame = 1;

        [SerializeField, Tooltip("Show the Pause / 1x / 2x / 5x / 10x speed buttons.")]
        private bool showSpeedBar = true;

        private float normalHoursPerSecond;
        private PlantController selectedPlant;
        private PlantingCohort cohort;
        private PlantMeshLibrary meshLibrary;
        private float uiTimer;
        private bool environmentReady;

        public PlantController SelectedPlant => selectedPlant;
        public bool Paused => paused;

        /// <summary>The shared planting start (initial growth and planting time) used by every plant.</summary>
        public PlantingCohort Cohort => cohort;
        public float InitialGrowth01 => initialGrowth01;

        private void Awake()
        {
            environmentReady = environment != null;
            if (!environmentReady)
            {
                Debug.LogError($"{nameof(GreenhouseManager)} on '{name}' needs an {nameof(EnvironmentController)} reference. " +
                               "The simulation will not run.", this);
                enabled = false;
                return;
            }

            if (autoDiscoverPlants)
            {
                var found = FindObjectsByType<PlantController>();
                for (int i = 0; i < found.Length; i++)
                {
                    if (!plants.Contains(found[i])) plants.Add(found[i]);
                }
            }

            if (irrigation == null) Debug.LogWarning($"{nameof(GreenhouseManager)}: no irrigation controller assigned; plants will not be watered.", this);
            if (cameraController == null) Debug.LogWarning($"{nameof(GreenhouseManager)}: no camera controller assigned; plant selection is disabled.", this);
            if (uiManager == null) Debug.LogWarning($"{nameof(GreenhouseManager)}: no UI manager assigned; no on-screen information.", this);

            meshLibrary = new PlantMeshLibrary();
            normalHoursPerSecond = Mathf.Max(0.0001f, simulatedHoursPerSecond);
            if (showSpeedBar) BioDome.UI.SimulationSpeedBar.Create(this);
        }

        private void OnEnable()
        {
            if (!environmentReady && environment == null) return;

            if (cameraController != null)
            {
                cameraController.PlantPicked += SelectPlant;
                cameraController.EmptySpaceClicked += ClearSelection;
            }

            if (uiManager != null)
            {
                uiManager.TemperatureOffsetRequested += OnTemperatureOffsetRequested;
                uiManager.HumidityOffsetRequested += OnHumidityOffsetRequested;
                uiManager.ShadeRequested += OnShadeRequested;
                uiManager.SimulationSpeedRequested += OnSimulationSpeedRequested;
                uiManager.SeasonRequested += OnSeasonRequested;
                uiManager.PausedRequested += OnPausedRequested;
                uiManager.IrrigationModeRequested += OnIrrigationModeRequested;
                uiManager.RefillRequested += OnRefillRequested;
                uiManager.ResetViewRequested += OnResetViewRequested;
            }
        }

        private void OnDisable()
        {
            if (cameraController != null)
            {
                cameraController.PlantPicked -= SelectPlant;
                cameraController.EmptySpaceClicked -= ClearSelection;
            }

            if (uiManager != null)
            {
                uiManager.TemperatureOffsetRequested -= OnTemperatureOffsetRequested;
                uiManager.HumidityOffsetRequested -= OnHumidityOffsetRequested;
                uiManager.ShadeRequested -= OnShadeRequested;
                uiManager.SimulationSpeedRequested -= OnSimulationSpeedRequested;
                uiManager.SeasonRequested -= OnSeasonRequested;
                uiManager.PausedRequested -= OnPausedRequested;
                uiManager.IrrigationModeRequested -= OnIrrigationModeRequested;
                uiManager.RefillRequested -= OnRefillRequested;
                uiManager.ResetViewRequested -= OnResetViewRequested;
            }
        }

        private void Start()
        {
            // Registration happens in Start so every PlantController.Awake has already created its simulation.
            // One cohort for everyone: same initial growth, same planting time. No plant gets its own random age.
            float nowHours = environment.Current.DayCount * 24f + environment.Current.TimeOfDayHours;
            cohort = new PlantingCohort(initialGrowth01, nowHours);
            for (int i = 0; i < plants.Count; i++)
            {
                if (plants[i] == null) continue;
                plants[i].SetMeshLibrary(meshLibrary);
                plants[i].Plant(cohort);
            }

            PrimeVisuals();

            if (irrigation != null)
            {
                for (int i = 0; i < plants.Count; i++)
                {
                    if (plants[i] != null && plants[i].UsesOwnIrrigation) continue; // experiment pods have their own irrigation
                    irrigation.RegisterPlant(plants[i]);
                }
            }

            if (uiManager != null)
            {
                uiManager.SyncControls(
                    environment.TemperatureOffsetCelsius,
                    environment.HumidityOffset,
                    environment.ShadeLevel,
                    simulatedHoursPerSecond,
                    environment.CurrentSeason,
                    paused,
                    irrigation != null ? irrigation.CurrentMode : IrrigationController.Mode.Off);
            }

            RefreshUi();
        }

        /// <summary>
        /// Builds the growth meshes every plant needs for its starting growth before the first frame, so plants appear at their true size
        /// instead of showing a stale mesh and visibly "growing" into place over the first seconds.
        /// </summary>
        private void PrimeVisuals()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < plants.Count; i++) if (plants[i] != null) plants[i].SyncVisualNow(); // request
            int built = meshLibrary.Process(int.MaxValue);                                         // build all
            for (int i = 0; i < plants.Count; i++) if (plants[i] != null) plants[i].SyncVisualNow(); // show
            Debug.Log($"{nameof(GreenhouseManager)}: primed {built} growth meshes for {plants.Count} plants in {timer.ElapsedMilliseconds} ms.");
        }

        // --- Simulation speed ------------------------------------------------------

        /// <summary>Simulated hours per real second at "1x" (the pace set in the Inspector: 0.4 = one simulated day per real minute).</summary>
        public float NormalHoursPerSecond => normalHoursPerSecond;

        /// <summary>Current speed in simulated hours per real second.</summary>
        public float SimulatedHoursPerSecond => simulatedHoursPerSecond;

        /// <summary>Sets the speed as a multiple of the normal pace (1, 2, 5, 10...). Affects environment, irrigation and plants equally.</summary>
        public void SetSpeedMultiplier(float multiplier)
        {
            simulatedHoursPerSecond = Mathf.Max(0f, multiplier) * normalHoursPerSecond;
        }

        public void SetPaused(bool value) => paused = value;

        private const float MaxStepHours = 0.25f;
        private const int MaxStepsPerFrame = 16;

        private void Update()
        {
            // meshLibrary (like the cohort and every PlantSimulation) is runtime-only state created in Awake/Start. Unity does
            // not serialise it, so if scripts are recompiled while Play Mode is running, the component keeps updating but this
            // state is gone. It cannot be rebuilt without silently resetting every plant, so stop with a clear message.
            if (meshLibrary == null)
            {
                Debug.LogError($"{nameof(GreenhouseManager)} lost its runtime state, most likely because scripts were recompiled while Play Mode was running. " +
                               "The simulation has been stopped. Exit Play Mode and press Play again. " +
                               "To avoid this, set Unity > Settings > General > Script Changes While Playing to 'Recompile After Finished Playing'.", this);
                enabled = false;
                return;
            }

            float deltaHours = paused ? 0f : Time.deltaTime * simulatedHoursPerSecond;

            // Large frames (high simulation speed or a slow frame) are split into small, equal sub-steps so growth, water and stress
            // give the same result at any frame rate or speed. Normal speeds still use exactly one step per frame.
            int steps = Mathf.Clamp(Mathf.CeilToInt(deltaHours / MaxStepHours), 1, MaxStepsPerFrame);
            float stepHours = deltaHours / steps;
            for (int i = 0; i < steps; i++) Step(stepHours);

            meshLibrary.Process(meshBuildsPerFrame);

            uiTimer += Time.unscaledDeltaTime;
            if (uiTimer >= uiRefreshInterval)
            {
                uiTimer = 0f;
                RefreshUi();
            }
        }

        /// <summary>
        /// Advances the whole simulation by <paramref name="deltaHours"/> simulated hours: environment first, then irrigation, then every plant.
        /// This is exactly what <c>Update</c> does each frame (it passes the frame's share of simulated time); it is a separate method so the
        /// simulation can be stepped with a chosen time step, which the numeric regression tests rely on. It touches no frame time, no UI and
        /// no mesh building, and needs only the serialized environment, irrigation and plant references.
        /// </summary>
        public void Step(float deltaHours)
        {
            // Fixed order: environment first, then everything that reacts to it.
            environment.Advance(deltaHours);
            EnvironmentConditions conditions = environment.Current;

            if (irrigation != null) irrigation.Tick(deltaHours);
            for (int i = 0; i < extraIrrigations.Count; i++)
            {
                if (extraIrrigations[i] != null) extraIrrigations[i].Tick(deltaHours);
            }

            for (int i = 0; i < plants.Count; i++)
            {
                PlantController plant = plants[i];
                if (plant == null) continue;

                // Plants with a zone profile (experimental pods) see their zone's climate; every other plant sees the greenhouse climate untouched.
                ZoneProfile zone = plant.ZoneProfile;
                if (zone != null) plant.Tick(deltaHours, zone.Apply(conditions));
                else plant.Tick(deltaHours, conditions);
            }
        }

        // --- Public API ----------------------------------------------------------

        public void RegisterPlant(PlantController plant)
        {
            if (plant == null || plants.Contains(plant)) return;
            plants.Add(plant);
            if (irrigation != null && !plant.UsesOwnIrrigation) irrigation.RegisterPlant(plant);

            if (cohort != null) // plants added while running join the same cohort settings
            {
                plant.SetMeshLibrary(meshLibrary);
                plant.Plant(cohort);
            }
        }

        private void OnDestroy()
        {
            if (meshLibrary != null) meshLibrary.Dispose();
        }

        public void UnregisterPlant(PlantController plant)
        {
            if (plant == null) return;
            plants.Remove(plant);
            if (irrigation != null) irrigation.UnregisterPlant(plant);
            if (selectedPlant == plant) ClearSelection();
        }

        public void SelectPlant(PlantController plant)
        {
            if (plant == null) return;

            selectedPlant = plant;
            if (cameraController != null) cameraController.FocusOn(plant.transform, plantFocusDistance, false);
            if (uiManager != null) uiManager.ShowPlant(plant);
        }

        public void ClearSelection()
        {
            selectedPlant = null;
            if (uiManager != null) uiManager.ShowPlant(null);
        }

        // --- UI intent handlers --------------------------------------------------

        private void OnTemperatureOffsetRequested(float value) => environment.SetTemperatureOffset(value);
        private void OnHumidityOffsetRequested(float value) => environment.SetHumidityOffset(value);
        private void OnShadeRequested(float value) => environment.SetShadeLevel(value);
        private void OnSimulationSpeedRequested(float value) => simulatedHoursPerSecond = Mathf.Max(0f, value);
        private void OnSeasonRequested(Season season) => environment.SetSeason(season);
        private void OnPausedRequested(bool value) => paused = value;
        private void OnIrrigationModeRequested(IrrigationController.Mode mode) { if (irrigation != null) irrigation.SetMode(mode); }
        private void OnRefillRequested() { if (irrigation != null) irrigation.RefillFull(); }

        private void OnResetViewRequested()
        {
            if (cameraController != null) cameraController.ResetView();
            ClearSelection();
        }

        private void RefreshUi()
        {
            if (uiManager == null) return;

            uiManager.ShowEnvironment(environment.Current);
            uiManager.SyncPaused(paused);

            if (irrigation != null)
                uiManager.ShowIrrigation(irrigation.Reservoir01, irrigation.WaterLitres, irrigation.IsRunning, irrigation.CurrentMode);

            if (selectedPlant != null) uiManager.ShowPlant(selectedPlant);
        }
    }
}
