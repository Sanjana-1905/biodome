using System.Collections.Generic;
using BioDome.CameraSystem;
using BioDome.Environment;
using BioDome.Irrigation;
using BioDome.Plants;
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

        private PlantController selectedPlant;
        private float uiTimer;
        private bool environmentReady;

        public PlantController SelectedPlant => selectedPlant;
        public bool Paused => paused;

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
        }

        private void OnEnable()
        {
            if (!environmentReady && environment == null) return;

            if (cameraController != null) cameraController.PlantPicked += SelectPlant;

            if (uiManager != null)
            {
                uiManager.TemperatureOffsetRequested += OnTemperatureOffsetRequested;
                uiManager.HumidityOffsetRequested += OnHumidityOffsetRequested;
                uiManager.ShadeRequested += OnShadeRequested;
                uiManager.SimulationSpeedRequested += OnSimulationSpeedRequested;
                uiManager.SeasonRequested += OnSeasonRequested;
                uiManager.PausedRequested += OnPausedRequested;
                uiManager.AutoIrrigationRequested += OnAutoIrrigationRequested;
                uiManager.RefillRequested += OnRefillRequested;
                uiManager.ResetViewRequested += OnResetViewRequested;
            }
        }

        private void OnDisable()
        {
            if (cameraController != null) cameraController.PlantPicked -= SelectPlant;

            if (uiManager != null)
            {
                uiManager.TemperatureOffsetRequested -= OnTemperatureOffsetRequested;
                uiManager.HumidityOffsetRequested -= OnHumidityOffsetRequested;
                uiManager.ShadeRequested -= OnShadeRequested;
                uiManager.SimulationSpeedRequested -= OnSimulationSpeedRequested;
                uiManager.SeasonRequested -= OnSeasonRequested;
                uiManager.PausedRequested -= OnPausedRequested;
                uiManager.AutoIrrigationRequested -= OnAutoIrrigationRequested;
                uiManager.RefillRequested -= OnRefillRequested;
                uiManager.ResetViewRequested -= OnResetViewRequested;
            }
        }

        private void Start()
        {
            // Registration happens in Start so every PlantController.Awake has already created its simulation.
            if (irrigation != null)
            {
                for (int i = 0; i < plants.Count; i++) irrigation.RegisterPlant(plants[i]);
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
                    irrigation != null && irrigation.CurrentMode == IrrigationController.Mode.Auto);
            }

            RefreshUi();
        }

        private void Update()
        {
            float deltaHours = paused ? 0f : Time.deltaTime * simulatedHoursPerSecond;

            // Fixed order: environment first, then everything that reacts to it.
            environment.Advance(deltaHours);
            EnvironmentConditions conditions = environment.Current;

            if (irrigation != null) irrigation.Tick(deltaHours);

            for (int i = 0; i < plants.Count; i++)
            {
                PlantController plant = plants[i];
                if (plant != null) plant.Tick(deltaHours, conditions);
            }

            uiTimer += Time.unscaledDeltaTime;
            if (uiTimer >= uiRefreshInterval)
            {
                uiTimer = 0f;
                RefreshUi();
            }
        }

        // --- Public API ----------------------------------------------------------

        public void RegisterPlant(PlantController plant)
        {
            if (plant == null || plants.Contains(plant)) return;
            plants.Add(plant);
            if (irrigation != null) irrigation.RegisterPlant(plant);
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
        private void OnAutoIrrigationRequested(bool enabled) { if (irrigation != null) irrigation.SetAutoEnabled(enabled); }
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

            if (irrigation != null)
                uiManager.ShowIrrigation(irrigation.Reservoir01, irrigation.WaterLitres, irrigation.IsRunning, irrigation.CurrentMode);

            if (selectedPlant != null) uiManager.ShowPlant(selectedPlant);
        }
    }
}
