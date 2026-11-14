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

        [Header("Control Value Labels (optional)")]
        [SerializeField] private TMP_Text temperatureOffsetValueText;
        [SerializeField] private TMP_Text humidityOffsetValueText;
        [SerializeField] private TMP_Text shadeValueText;

        [Header("Irrigation")]
        [SerializeField] private TMP_Text irrigationStatusText;
        [SerializeField, Tooltip("Mode selector: put the three toggles in one ToggleGroup.")]
        private Toggle irrigationOffToggle;
        [SerializeField] private Toggle irrigationManualToggle;
        [SerializeField] private Toggle irrigationAutoToggle;
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
        public event Action<IrrigationController.Mode> IrrigationModeRequested;
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
            if (irrigationOffToggle != null) irrigationOffToggle.onValueChanged.AddListener(OnIrrigationOffChanged);
            if (irrigationManualToggle != null) irrigationManualToggle.onValueChanged.AddListener(OnIrrigationManualChanged);
            if (irrigationAutoToggle != null) irrigationAutoToggle.onValueChanged.AddListener(OnIrrigationAutoChanged);
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
            if (irrigationOffToggle != null) irrigationOffToggle.onValueChanged.RemoveListener(OnIrrigationOffChanged);
            if (irrigationManualToggle != null) irrigationManualToggle.onValueChanged.RemoveListener(OnIrrigationManualChanged);
            if (irrigationAutoToggle != null) irrigationAutoToggle.onValueChanged.RemoveListener(OnIrrigationAutoChanged);
            if (refillButton != null) refillButton.onClick.RemoveListener(OnRefillClicked);
            if (resetViewButton != null) resetViewButton.onClick.RemoveListener(OnResetViewClicked);
        }

        // --- Display API (called by GreenhouseManager) ---------------------------

        /// <summary>Sets control widgets to match simulation state without raising events.</summary>
        public void SyncControls(
            float temperatureOffset, float humidityOffset, float shade,
            float simulationSpeed, Season season, bool paused, IrrigationController.Mode irrigationMode)
        {
            if (temperatureOffsetSlider != null) temperatureOffsetSlider.SetValueWithoutNotify(temperatureOffset);
            if (humidityOffsetSlider != null) humidityOffsetSlider.SetValueWithoutNotify(humidityOffset);
            if (shadeSlider != null) shadeSlider.SetValueWithoutNotify(shade);
            if (simulationSpeedSlider != null) simulationSpeedSlider.SetValueWithoutNotify(simulationSpeed);
            if (seasonDropdown != null) seasonDropdown.SetValueWithoutNotify((int)season);
            if (pauseToggle != null) pauseToggle.SetIsOnWithoutNotify(paused);
            ShowIrrigationMode(irrigationMode);

            ShowTemperatureOffset(temperatureOffset);
            ShowHumidityOffset(humidityOffset);
            ShowShade(shade);
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
            ShowIrrigationMode(mode); // keeps the selector correct if the mode is changed elsewhere (e.g. the Inspector)

            if (irrigationStatusText == null) return;
            string state = running ? "Watering" : "Idle";
            irrigationStatusText.text = $"{state} ({mode})\nReservoir {reservoir01 * 100f:0}% ({litres:0} L)";
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
                builder.Append("Status: <b>").Append(StateColorTag(sim.State)).Append(sim.State).Append("</color></b>\n");
                builder.Append("Growth stage: <b>").Append(sim.Stage).Append("</b>\n");
                builder.Append("Health: ").Append(Percent(sim.Health01)).Append('\n');
                builder.Append("Growth: ").Append(Percent(sim.Growth01)).Append('\n');
                builder.Append("Soil moisture: ").Append(Percent(sim.Water01)).Append('\n');
                builder.Append("Age: ").Append((sim.AgeHours / 24f).ToString("0.0")).Append(" days\n\n");

                builder.Append("Stress: <b>").Append(Percent(sim.Stress01)).Append("</b>\n");
                builder.Append("  Temperature  ").Append(Percent(sim.TemperatureStress01)).Append('\n');
                builder.Append("  Humidity  ").Append(Percent(sim.HumidityStress01)).Append('\n');
                builder.Append("  Light  ").Append(Percent(sim.LightStress01)).Append('\n');
                builder.Append("  Water  ").Append(Percent(sim.WaterStress01));

                if (!string.IsNullOrEmpty(data.Description)) builder.Append("\n\n").Append(data.Description);
                plantDetailsText.text = builder.ToString();
            }
        }

        // --- Helpers -------------------------------------------------------------

        private static string Percent(float value01) => (value01 * 100f).ToString("0") + "%";

        private static string StateColorTag(PlantHealthState state)
        {
            switch (state)
            {
                case PlantHealthState.Healthy: return "<color=#7FD67F>";
                case PlantHealthState.Stressed: return "<color=#F2D45C>";
                case PlantHealthState.Critical: return "<color=#F29A4B>";
                default: return "<color=#E86A5F>";
            }
        }

        private void ShowIrrigationMode(IrrigationController.Mode mode)
        {
            // Switching one toggle on makes its ToggleGroup switch the others off.
            switch (mode)
            {
                case IrrigationController.Mode.Off:
                    if (irrigationOffToggle != null && !irrigationOffToggle.isOn) irrigationOffToggle.SetIsOnWithoutNotify(true);
                    break;
                case IrrigationController.Mode.Manual:
                    if (irrigationManualToggle != null && !irrigationManualToggle.isOn) irrigationManualToggle.SetIsOnWithoutNotify(true);
                    break;
                case IrrigationController.Mode.Auto:
                    if (irrigationAutoToggle != null && !irrigationAutoToggle.isOn) irrigationAutoToggle.SetIsOnWithoutNotify(true);
                    break;
            }
        }

        private void ShowTemperatureOffset(float celsius)
        {
            if (temperatureOffsetValueText != null) temperatureOffsetValueText.text = celsius.ToString("+0.0;-0.0;0.0") + " C";
        }

        private void ShowHumidityOffset(float offset)
        {
            if (humidityOffsetValueText != null) humidityOffsetValueText.text = (offset * 100f).ToString("+0;-0;0") + " %";
        }

        private void ShowShade(float shade01)
        {
            if (shadeValueText != null) shadeValueText.text = (shade01 * 100f).ToString("0") + " %";
        }

        // --- Widget callbacks ----------------------------------------------------

        private void OnTemperatureOffsetChanged(float value) { ShowTemperatureOffset(value); TemperatureOffsetRequested?.Invoke(value); }
        private void OnHumidityOffsetChanged(float value) { ShowHumidityOffset(value); HumidityOffsetRequested?.Invoke(value); }
        private void OnShadeChanged(float value) { ShowShade(value); ShadeRequested?.Invoke(value); }
        private void OnSpeedChanged(float value) => SimulationSpeedRequested?.Invoke(value);
        private void OnPauseChanged(bool value) => PausedRequested?.Invoke(value);
        private void OnRefillClicked() => RefillRequested?.Invoke();
        private void OnResetViewClicked() => ResetViewRequested?.Invoke();

        // A ToggleGroup also reports the toggle that switched OFF; only the one switched ON is a request.
        private void OnIrrigationOffChanged(bool on) { if (on) IrrigationModeRequested?.Invoke(IrrigationController.Mode.Off); }
        private void OnIrrigationManualChanged(bool on) { if (on) IrrigationModeRequested?.Invoke(IrrigationController.Mode.Manual); }
        private void OnIrrigationAutoChanged(bool on) { if (on) IrrigationModeRequested?.Invoke(IrrigationController.Mode.Auto); }

        private void OnSeasonDropdownChanged(int index)
        {
            if (index < 0 || index > 3) return;
            SeasonRequested?.Invoke((Season)index);
        }
    }
}
