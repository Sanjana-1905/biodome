using System;
using System.Text;
using BioDome.Core;
using BioDome.Irrigation;
using BioDome.Plants;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BioDome.UI
{
    /// <summary>
    /// Presentation layer only. Displays data pushed to it by the GreenhouseManager and turns user
    /// input into events. It holds no references to the simulation controllers, so UI can be
    /// rebuilt without touching them. Every serialized reference is optional: missing ones are skipped.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("Environment Readouts")]
        [SerializeField] private TMP_Text seasonText;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private TMP_Text temperatureText;
        [SerializeField] private TMP_Text humidityText;
        [SerializeField] private TMP_Text lightText;

        [Header("Environment Controls")]
        [SerializeField, Tooltip("Range should be -10 to 10 (C).")]
        private Slider temperatureOffsetSlider;
        [SerializeField, Tooltip("Range should be -0.3 to 0.3.")]
        private Slider humidityOffsetSlider;
        [SerializeField, Tooltip("Range should be 0 to 1.")]
        private Slider shadeSlider;
        [SerializeField, Tooltip("Range e.g. 0.1 to 4 (simulated hours per real second).")]
        private Slider simulationSpeedSlider;
        [SerializeField, Tooltip("Options must be in order: Spring, Summer, Autumn, Winter.")]
        private TMP_Dropdown seasonDropdown;
        [SerializeField] private Toggle pauseToggle;

        [Header("Irrigation")]
        [SerializeField] private TMP_Text irrigationStatusText;
        [SerializeField] private Toggle autoIrrigationToggle;
        [SerializeField] private Button refillButton;

        [Header("Plant Inspector")]
        [SerializeField] private GameObject plantPanel;
        [SerializeField] private TMP_Text plantNameText;
        [SerializeField] private TMP_Text plantDetailsText;
        [SerializeField] private Button resetViewButton;

        private readonly StringBuilder builder = new StringBuilder(256);

        // --- Events: user intent, listened to by the GreenhouseManager -----------

        public event Action<float> TemperatureOffsetRequested;
        public event Action<float> HumidityOffsetRequested;
        public event Action<float> ShadeRequested;
        public event Action<float> SimulationSpeedRequested;
        public event Action<Season> SeasonRequested;
        public event Action<bool> PausedRequested;
        public event Action<bool> AutoIrrigationRequested;
        public event Action RefillRequested;
        public event Action ResetViewRequested;

        private void Awake()
        {
            if (plantPanel != null) plantPanel.SetActive(false);
        }

        private void OnEnable()
        {
            if (temperatureOffsetSlider != null) temperatureOffsetSlider.onValueChanged.AddListener(OnTemperatureOffsetChanged);
            if (humidityOffsetSlider != null) humidityOffsetSlider.onValueChanged.AddListener(OnHumidityOffsetChanged);
            if (shadeSlider != null) shadeSlider.onValueChanged.AddListener(OnShadeChanged);
            if (simulationSpeedSlider != null) simulationSpeedSlider.onValueChanged.AddListener(OnSpeedChanged);
            if (seasonDropdown != null) seasonDropdown.onValueChanged.AddListener(OnSeasonDropdownChanged);
            if (pauseToggle != null) pauseToggle.onValueChanged.AddListener(OnPauseChanged);
            if (autoIrrigationToggle != null) autoIrrigationToggle.onValueChanged.AddListener(OnAutoIrrigationChanged);
            if (refillButton != null) refillButton.onClick.AddListener(OnRefillClicked);
            if (resetViewButton != null) resetViewButton.onClick.AddListener(OnResetViewClicked);
        }

        private void OnDisable()
        {
            if (temperatureOffsetSlider != null) temperatureOffsetSlider.onValueChanged.RemoveListener(OnTemperatureOffsetChanged);
            if (humidityOffsetSlider != null) humidityOffsetSlider.onValueChanged.RemoveListener(OnHumidityOffsetChanged);
            if (shadeSlider != null) shadeSlider.onValueChanged.RemoveListener(OnShadeChanged);
            if (simulationSpeedSlider != null) simulationSpeedSlider.onValueChanged.RemoveListener(OnSpeedChanged);
            if (seasonDropdown != null) seasonDropdown.onValueChanged.RemoveListener(OnSeasonDropdownChanged);
            if (pauseToggle != null) pauseToggle.onValueChanged.RemoveListener(OnPauseChanged);
            if (autoIrrigationToggle != null) autoIrrigationToggle.onValueChanged.RemoveListener(OnAutoIrrigationChanged);
            if (refillButton != null) refillButton.onClick.RemoveListener(OnRefillClicked);
            if (resetViewButton != null) resetViewButton.onClick.RemoveListener(OnResetViewClicked);
        }

        // --- Display API (called by GreenhouseManager) ---------------------------

        /// <summary>Sets control widgets to match simulation state without raising events.</summary>
        public void SyncControls(
            float temperatureOffset, float humidityOffset, float shade,
            float simulationSpeed, Season season, bool paused, bool autoIrrigation)
        {
            if (temperatureOffsetSlider != null) temperatureOffsetSlider.SetValueWithoutNotify(temperatureOffset);
            if (humidityOffsetSlider != null) humidityOffsetSlider.SetValueWithoutNotify(humidityOffset);
            if (shadeSlider != null) shadeSlider.SetValueWithoutNotify(shade);
            if (simulationSpeedSlider != null) simulationSpeedSlider.SetValueWithoutNotify(simulationSpeed);
            if (seasonDropdown != null) seasonDropdown.SetValueWithoutNotify((int)season);
            if (pauseToggle != null) pauseToggle.SetIsOnWithoutNotify(paused);
            if (autoIrrigationToggle != null) autoIrrigationToggle.SetIsOnWithoutNotify(autoIrrigation);
        }

        public void ShowEnvironment(in EnvironmentConditions conditions)
        {
            if (seasonText != null) seasonText.text = $"{conditions.Season} - Day {conditions.DayCount + 1}";

            if (timeText != null)
            {
                int hours = Mathf.FloorToInt(conditions.TimeOfDayHours);
                int minutes = Mathf.FloorToInt((conditions.TimeOfDayHours - hours) * 60f);
                timeText.text = $"{hours:00}:{minutes:00}";
            }

            if (temperatureText != null) temperatureText.text = $"{conditions.TemperatureCelsius:0.0} C";
            if (humidityText != null) humidityText.text = $"{conditions.Humidity01 * 100f:0}% humidity";
            if (lightText != null) lightText.text = $"{conditions.Light01 * 100f:0}% light";

            // Keep the dropdown in step when the season changes automatically.
            if (seasonDropdown != null && seasonDropdown.value != (int)conditions.Season)
                seasonDropdown.SetValueWithoutNotify((int)conditions.Season);
        }

        public void ShowIrrigation(float reservoir01, float litres, bool running, IrrigationController.Mode mode)
        {
            if (irrigationStatusText == null) return;
            string state = running ? "Watering" : "Idle";
            irrigationStatusText.text = $"{state} ({mode}) - Reservoir {reservoir01 * 100f:0}% ({litres:0} L)";
        }

        /// <summary>Shows details for a plant, or hides the panel when <paramref name="plant"/> is null.</summary>
        public void ShowPlant(PlantController plant)
        {
            bool hasPlant = plant != null && plant.IsInitialized;
            if (plantPanel != null) plantPanel.SetActive(hasPlant);
            if (!hasPlant) return;

            PlantData data = plant.Data;
            var sim = plant.Simulation;

            if (plantNameText != null)
                plantNameText.text = string.IsNullOrEmpty(data.ScientificName)
                    ? data.DisplayName
                    : $"{data.DisplayName} ({data.ScientificName})";

            if (plantDetailsText != null)
            {
                builder.Clear();
                builder.Append("Status: ").Append(sim.State).Append('\n');
                builder.Append("Health: ").Append((sim.Health01 * 100f).ToString("0")).Append("%\n");
                builder.Append("Growth: ").Append((sim.Growth01 * 100f).ToString("0")).Append("%\n");
                builder.Append("Soil moisture: ").Append((sim.Water01 * 100f).ToString("0")).Append("%\n");
                builder.Append("Stress: ").Append((sim.Stress01 * 100f).ToString("0")).Append("%\n");
                builder.Append("Age: ").Append((sim.AgeHours / 24f).ToString("0.0")).Append(" days");
                if (!string.IsNullOrEmpty(data.Description)) builder.Append("\n\n").Append(data.Description);
                plantDetailsText.text = builder.ToString();
            }
        }

        // --- Widget callbacks ----------------------------------------------------

        private void OnTemperatureOffsetChanged(float value) => TemperatureOffsetRequested?.Invoke(value);
        private void OnHumidityOffsetChanged(float value) => HumidityOffsetRequested?.Invoke(value);
        private void OnShadeChanged(float value) => ShadeRequested?.Invoke(value);
        private void OnSpeedChanged(float value) => SimulationSpeedRequested?.Invoke(value);
        private void OnPauseChanged(bool value) => PausedRequested?.Invoke(value);
        private void OnAutoIrrigationChanged(bool value) => AutoIrrigationRequested?.Invoke(value);
        private void OnRefillClicked() => RefillRequested?.Invoke();
        private void OnResetViewClicked() => ResetViewRequested?.Invoke();

        private void OnSeasonDropdownChanged(int index)
        {
            if (index < 0 || index > 3) return;
            SeasonRequested?.Invoke((Season)index);
        }
    }
}
