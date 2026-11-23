using System.Collections.Generic;
using System.Text;
using BioDome.CameraSystem;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Plants;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BioDome.Experiment
{
    /// <summary>
    /// "Pod Lab": a live test panel for the five experimental pods. Pick a pod, change its temperature / humidity / light / irrigation share,
    /// run the simulation fast or pause it, and read one plant's live simulation values. It edits the pod's own runtime profile
    /// (see <see cref="ExperimentPod.SetLiveConditions"/>), which the GreenhouseManager applies to that pod's plants every step,
    /// so it changes the real simulation input of that pod only. It adds no simulation of its own.
    /// Created at run time by <see cref="ExperimentGarden"/>. Toggle with the "Pod Lab" button or the L key.
    /// </summary>
    public class PodTestPanel : MonoBehaviour
    {
        private ExperimentGarden garden;
        private GreenhouseManager manager;
        private CameraController cameraController;

        private int podIndex, plantIndex;
        private float startHours = -1f;
        private float timer;
        private float measuredHoursPerSecond, lastMeasureHours = -1f, lastMeasureReal;
        private ExperimentComparisonPanel comparisonPanel;
        private BioDome.UI.UIManager uiManager;
        private readonly List<TMP_Text> podButtonLabels = new List<TMP_Text>();

        private GameObject panel;
        private TMP_Text toggleLabel, podTitle, readout, plantLabel;
        private readonly List<Image> podButtons = new List<Image>();
        private readonly List<(Image image, float multiplier)> speedButtons = new List<(Image, float)>();
        private Image pauseButton;
        private TMP_Text pauseLabel;
        private readonly TMP_Text[] valueLabels = new TMP_Text[4];
        private readonly StringBuilder text = new StringBuilder(1024);

        private static readonly Color PanelColor = new Color(0.06f, 0.09f, 0.08f, 0.94f);
        private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color PausedColor = new Color(0.78f, 0.58f, 0.18f, 0.95f);
        private static readonly Color ActiveColor = new Color(0.30f, 0.62f, 0.36f, 0.95f);
        private static readonly Color TextColor = new Color(0.92f, 0.96f, 0.92f, 1f);
        private static readonly Color SubText = new Color(0.70f, 0.80f, 0.72f, 1f);

        // Stepper settings: label, step, min, max.
        private static readonly string[] ParamNames = { "Temperature offset", "Humidity offset", "Light (x greenhouse)", "Irrigation share" };
        private static readonly float[] Steps = { 2f, 0.05f, 0.1f, 0.1f };
        private static readonly float[] Mins = { -20f, -0.6f, 0f, 0f };
        private static readonly float[] Maxs = { 20f, 0.4f, 1f, 1f };
        private static readonly float[] Speeds = { 1f, 10f, 50f, 100f };

        public void Init(ExperimentGarden owner) => garden = owner;

        private ExperimentPod Pod => garden.Pods[podIndex];

        private void Start()
        {
            if (garden == null || garden.Pods.Count == 0) { enabled = false; return; }
            manager = FindAnyObjectByType<GreenhouseManager>();
            cameraController = FindAnyObjectByType<CameraController>();
            comparisonPanel = FindAnyObjectByType<ExperimentComparisonPanel>();
            uiManager = FindAnyObjectByType<BioDome.UI.UIManager>();
            Build();
            panel.SetActive(false);
            Refresh();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame) Toggle();
            if (startHours < 0f) startHours = NowHours();
            MeasureSpeed();
            if (!panel.activeSelf) return;
            timer += Time.unscaledDeltaTime;
            if (timer < 0.2f) return;
            timer = 0f;
            Refresh();
        }

        // The simulation speed actually achieved (simulated hours per real second), measured over about a second.
        private void MeasureSpeed()
        {
            float now = NowHours(), real = Time.realtimeSinceStartup;
            if (lastMeasureHours < 0f) { lastMeasureHours = now; lastMeasureReal = real; return; }
            if (real - lastMeasureReal < 1f) return;
            measuredHoursPerSecond = Mathf.Max(0f, now - lastMeasureHours) / (real - lastMeasureReal);
            lastMeasureHours = now; lastMeasureReal = real;
        }

        private float NowHours()
        {
            EnvironmentConditions c = garden.GreenhouseNow;
            return c.DayCount * 24f + c.TimeOfDayHours;
        }

        // --- Actions ---------------------------------------------------------------

        public bool IsOpen => panel != null && panel.activeSelf;
        public void SetOpen(bool open) { if (IsOpen != open) Toggle(); }

        private void Toggle()
        {
            panel.SetActive(!panel.activeSelf);
            toggleLabel.text = panel.activeSelf ? "Close Pod Lab (L)" : "Pod Lab (L)";
            if (comparisonPanel != null) comparisonPanel.SetVisible(!panel.activeSelf); // both use the right-hand column
            if (uiManager != null) uiManager.DockPlantPanelBottomLeft(panel.activeSelf);   // and so does the plant inspector
            Refresh();
        }

        public void SelectPod(int index)
        {
            podIndex = Mathf.Clamp(index, 0, garden.Pods.Count - 1);
            plantIndex = 0;
            FollowInspector();
            Refresh();
        }

        public void FocusPod()
        {
            if (cameraController == null || Pod == null) return;
            cameraController.SnapTo(Pod.transform.position + Vector3.up * 0.5f, 6.5f, 30f, 28f);
            PlantController p = CurrentPlant();
            if (manager != null && p != null) manager.SelectPlant(p);
        }

        private float[] CurrentValues()
        {
            var z = Pod.Profile;
            return new[] { z.TemperatureOffsetCelsius, z.HumidityOffset, z.LightMultiplier, z.WaterShareWeight };
        }

        private void Change(int parameter, int direction)
        {
            if (Pod == null || Pod.Profile == null) return;
            float[] v = CurrentValues();
            v[parameter] = Mathf.Clamp(Mathf.Round((v[parameter] + direction * Steps[parameter]) / Steps[parameter]) * Steps[parameter], Mins[parameter], Maxs[parameter]);
            Pod.SetLiveConditions(v[0], v[1], v[2], v[3]);
            Refresh();
        }

        public void SetSpeed(float multiplier)
        {
            if (manager == null) return;
            manager.SetPaused(false);
            manager.SetSpeedMultiplier(multiplier);
            Refresh();
        }

        private void TogglePause()
        {
            if (manager == null) return;
            manager.SetPaused(!manager.Paused);
            Refresh();
        }

        // If the plant inspector is showing an experiment plant, keep it on the plant selected here.
        private void FollowInspector()
        {
            if (manager == null || manager.SelectedPlant == null || manager.SelectedPlant.ZoneProfile == null) return;
            PlantController p = CurrentPlant();
            if (p != null && p != manager.SelectedPlant) manager.SelectPlant(p);
        }

        private void StepPlant(int direction)
        {
            plantIndex += direction;
            FollowInspector();
            Refresh();
        }

        private PlantController CurrentPlant()
        {
            var plants = Pod.Plants;
            if (plants.Count == 0) return null;
            plantIndex = (plantIndex % plants.Count + plants.Count) % plants.Count;
            return plants[plantIndex];
        }

        // --- Readout ---------------------------------------------------------------

        private void Refresh()
        {
            if (panel == null || !panel.activeSelf) return;
            ExperimentPod pod = Pod;

            for (int i = 0; i < podButtons.Count; i++)
            {
                bool selected = i == podIndex;
                podButtons[i].color = selected ? ActiveColor : ButtonColor;
                podButtonLabels[i].fontStyle = selected ? FontStyles.Bold | FontStyles.Underline : FontStyles.Normal;
            }
            if (manager != null)
            {
                pauseButton.color = manager.Paused ? PausedColor : ButtonColor;
                pauseLabel.text = manager.Paused ? "Resume" : "Pause";
                float current = manager.SimulatedHoursPerSecond / Mathf.Max(0.0001f, manager.NormalHoursPerSecond);
                foreach (var (image, m) in speedButtons) image.color = !manager.Paused && Mathf.Abs(current - m) <= 0.01f * m ? ActiveColor : ButtonColor;
            }

            podTitle.text = $"<color=#{ColorUtility.ToHtmlStringRGB(pod.Accent)}>{pod.DisplayName}</color>";
            float[] v = CurrentValues();
            ZoneProfile o = pod.OriginalProfile;
            float[] orig = { o.TemperatureOffsetCelsius, o.HumidityOffset, o.LightMultiplier, o.WaterShareWeight };
            valueLabels[0].text = Mark($"{v[0]:+0;-0;0} °C", v[0], orig[0]);
            valueLabels[1].text = Mark($"{v[1] * 100f:+0;-0;0} % RH", v[1], orig[1]);
            valueLabels[2].text = Mark($"{v[2] * 100f:0} %", v[2], orig[2]);
            valueLabels[3].text = Mark($"{v[3] * 100f:0} %", v[3], orig[3]);

            PlantController plant = CurrentPlant();
            plantLabel.text = plant != null ? $"Plant {plantIndex + 1} / {pod.Plants.Count}" : "No plants";

            EnvironmentConditions g = garden.GreenhouseNow;
            float now = NowHours(), elapsed = Mathf.Max(0f, now - startHours);
            float tod = g.TimeOfDayHours;
            text.Clear();
            text.Append($"<b>Day {g.DayCount + 1}  {Mathf.FloorToInt(tod):00}:{Mathf.FloorToInt(tod % 1f * 60f):00}</b>   elapsed {Mathf.FloorToInt(elapsed / 24f)} d {elapsed % 24f:0.0} h");
            text.Append(manager == null ? "" : manager.Paused ? "   <color=#f0c060>PAUSED</color>" : $"   {manager.SimulatedHoursPerSecond / manager.NormalHoursPerSecond:0.#}x");
            text.Append('\n');
            if (manager != null && !manager.Paused)
                text.Append($"<color=#9fb3a6>Speed set {manager.SimulatedHoursPerSecond:0.#} sim h/s, achieved {measuredHoursPerSecond:0.#} sim h/s</color>\n");
            text.Append($"Greenhouse  {g.TemperatureCelsius:0.0}°C  {g.Humidity01 * 100f:0}% RH  light {g.Light01 * 100f:0}%\n");
            if (plant != null && plant.IsInitialized)
            {
                EnvironmentConditions c = plant.LastConditions; // what the plant's simulation actually received last step
                var s = plant.Simulation;
                text.Append($"Plant gets  {c.TemperatureCelsius:0.0}°C  {c.Humidity01 * 100f:0}% RH  light {c.Light01 * 100f:0}%\n");
                EnvironmentConditions next = pod.ConditionsFor(g);
                if (Mathf.Abs(next.TemperatureCelsius - c.TemperatureCelsius) > 0.05f || Mathf.Abs(next.Humidity01 - c.Humidity01) > 0.005f || Mathf.Abs(next.Light01 - c.Light01) > 0.005f)
                    text.Append($"<color=#f0c060>New setting  {next.TemperatureCelsius:0.0}°C  {next.Humidity01 * 100f:0}% RH  light {next.Light01 * 100f:0}%  (applies on the next step{(manager != null && manager.Paused ? ", resume to apply" : "")})</color>\n");
                text.Append($"<color=#{(s.IsAlive ? "9fe0a0" : "f08a6a")}>{s.State}</color>   stage <b>{s.Stage.DisplayName()}</b>   growth <b>{s.Growth01 * 100f:0.0}%</b>\n");
                text.Append($"Health {s.Health01 * 100f:0}%   Wilting {s.Wilt01 * 100f:0}%   Bloom {s.Bloom01 * 100f:0}%{(s.IsRecovering ? "   <color=#9fe0a0>recovering</color>" : "")}\n");
                text.Append($"Soil moisture {s.Water01 * 100f:0}%   growth rate x{s.GrowthFactor01:0.00}\n");
                text.Append("Stress  ").Append(Bar("temp", s.TemperatureStress01)).Append(Bar("hum", s.HumidityStress01)).Append('\n');
                text.Append("        ").Append(Bar("light", s.LightStress01)).Append(Bar("water", s.WaterStress01)).Append('\n');
                ExperimentPod.Metrics m = pod.Measure();
                text.Append($"<color=#9fb3a6>Pod avg: growth {m.Growth * 100f:0}%  health {m.Health * 100f:0}%  soil {m.Moisture * 100f:0}%  alive {m.Alive}/{m.Total}</color>");
                if (!plant.Data.CanFlower) text.Append($"\n<color=#9fb3a6>{plant.Data.DisplayName} does not flower in this simulation.</color>");
            }
            readout.text = text.ToString();
        }

        private static string Mark(string label, float value, float original) =>
            Mathf.Abs(value - original) < 1e-4f ? label : $"<color=#f0c060>{label}</color>";

        private static string Bar(string name, float v)
        {
            string color = v < 0.3f ? "9fe0a0" : v < 0.6f ? "f0c060" : "f08a6a";
            return $"{name} <color=#{color}>{v * 100f:0}%</color>   ";
        }

        // --- UI construction -------------------------------------------------------

        private void Build()
        {
            var canvasObject = new GameObject("PodTestCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 7; // above the environment panel it covers while open
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            // Right-hand column, under the speed bar. The comparison panel (same column) is hidden while Pod Lab is open.
            RectTransform toggle = Rect("PodLabToggle", canvasObject.transform, new Color(0.20f, 0.42f, 0.26f, 0.95f));
            TopRight(toggle, new Vector2(-12f - 336f - 8f, -56f), new Vector2(124f, 30f)); // left of the speed bar, clear of the plant inspector
            toggleLabel = Label(toggle, "Pod Lab (L)", 11.5f, FontStyles.Bold);
            AddClick(toggle, Toggle);

            const float width = 300f, pad = 8f, rowH = 22f;
            RectTransform p = Rect("PodLabPanel", canvasObject.transform, PanelColor);
            TopRight(p, new Vector2(-12f, -92f), new Vector2(width, 428f));
            panel = p.gameObject;
            float y = -pad;

            Text(p, "POD LAB · live experiment controls", 12.5f, FontStyles.Bold, new Vector2(pad, y), new Vector2(width - 60f, 18f));
            RectTransform close = Rect("Close", p, ButtonColor);
            TopLeft(close, new Vector2(width - pad - 44f, y), new Vector2(44f, 18f));
            Label(close, "Close", 10f, FontStyles.Normal);
            AddClick(close, Toggle);
            y -= 22f;

            // Pod selection.
            float cell = (width - pad * 2f - 4f * 3f) / garden.Pods.Count;
            string[] shortNames = { "Control", "Heat", "Humidity", "Light", "Water" };
            for (int i = 0; i < garden.Pods.Count; i++)
            {
                int index = i;
                RectTransform b = Rect("Pod" + i, p, ButtonColor);
                TopLeft(b, new Vector2(pad + i * (cell + 3f), y), new Vector2(cell, rowH));
                string name = i < shortNames.Length ? shortNames[i] : garden.Pods[i].DisplayName;
                podButtonLabels.Add(Label(b, name, 10.5f, FontStyles.Normal));
                RectTransform accent = Rect("Accent", b, garden.Pods[i].Accent);
                accent.anchorMin = Vector2.zero; accent.anchorMax = new Vector2(1f, 0f); accent.pivot = new Vector2(0.5f, 0f); accent.sizeDelta = new Vector2(0f, 2f);
                accent.GetComponent<Image>().raycastTarget = false;
                AddClick(b, () => SelectPod(index));
                podButtons.Add(b.GetComponent<Image>());
            }
            y -= rowH + 6f;

            podTitle = Text(p, "", 12f, FontStyles.Bold, new Vector2(pad, y), new Vector2(120f, rowH));
            Button(p, "Go to pod", new Vector2(pad + 116f, y), new Vector2(80f, rowH), FocusPod);
            Button(p, "Reset pod", new Vector2(pad + 200f, y), new Vector2(84f, rowH), () => { Pod.ResetLiveConditions(); Refresh(); });
            y -= rowH + 6f;

            // Parameter steppers.
            for (int i = 0; i < 4; i++)
            {
                int parameter = i;
                Text(p, ParamNames[i], 11f, FontStyles.Normal, new Vector2(pad, y), new Vector2(130f, rowH));
                Button(p, "-", new Vector2(pad + 136f, y), new Vector2(28f, rowH), () => Change(parameter, -1));
                valueLabels[i] = Text(p, "", 11.5f, FontStyles.Bold, new Vector2(pad + 166f, y), new Vector2(84f, rowH), TextAlignmentOptions.Center);
                Button(p, "+", new Vector2(pad + 252f, y), new Vector2(28f, rowH), () => Change(parameter, +1));
                y -= rowH + 3f;
            }
            Text(p, "Offsets add to this pod's own controlled climate (not the greenhouse); light and water scale it.", 9f, FontStyles.Italic, new Vector2(pad, y), new Vector2(width - pad * 2f, 14f)).color = SubText;
            y -= 18f;

            // Simulation speed.
            Text(p, "Speed", 11f, FontStyles.Normal, new Vector2(pad, y), new Vector2(44f, rowH));
            float sx = pad + 46f, sw = (width - sx - pad - 4f * 3f) / 5f;
            pauseButton = Button(p, "Pause", new Vector2(sx, y), new Vector2(sw, rowH), TogglePause);
            pauseLabel = pauseButton.GetComponentInChildren<TMP_Text>();
            for (int i = 0; i < Speeds.Length; i++)
            {
                float m = Speeds[i];
                speedButtons.Add((Button(p, m + "x", new Vector2(sx + (i + 1) * (sw + 3f), y), new Vector2(sw, rowH), () => SetSpeed(m)), m));
            }
            y -= rowH + 6f;

            // Plant selection.
            Button(p, "<", new Vector2(pad, y), new Vector2(28f, rowH), () => StepPlant(-1));
            plantLabel = Text(p, "", 11f, FontStyles.Normal, new Vector2(pad + 30f, y), new Vector2(110f, rowH), TextAlignmentOptions.Center);
            Button(p, ">", new Vector2(pad + 142f, y), new Vector2(28f, rowH), () => StepPlant(+1));
            Button(p, "Inspect plant", new Vector2(pad + 176f, y), new Vector2(108f, rowH), () => { var pl = CurrentPlant(); if (manager != null && pl != null) manager.SelectPlant(pl); });
            y -= rowH + 6f;

            readout = Text(p, "", 10.5f, FontStyles.Normal, new Vector2(pad, y), new Vector2(width - pad * 2f, 14f * 13f));
            readout.textWrappingMode = TextWrappingModes.Normal;
            readout.alignment = TextAlignmentOptions.TopLeft;
        }

        private static void AddClick(RectTransform rect, UnityEngine.Events.UnityAction action)
        {
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(action);
        }

        private Image Button(Transform parent, string label, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            RectTransform b = Rect("Btn_" + label, parent, ButtonColor);
            TopLeft(b, position, size);
            Label(b, label, 11f, FontStyles.Normal);
            AddClick(b, action);
            return b.GetComponent<Image>();
        }

        private static void TopRight(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static void TopLeft(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static RectTransform Rect(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = color.a > 0f;
            return (RectTransform)go.transform;
        }

        private static TMP_Text Label(RectTransform parent, string text, float size, FontStyles style)
        {
            TMP_Text t = NewTmp(parent, text, size, style, TextAlignmentOptions.Center);
            var rt = (RectTransform)t.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return t;
        }

        private static TMP_Text Text(Transform parent, string text, float size, FontStyles style, Vector2 position, Vector2 sizeDelta,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            TMP_Text t = NewTmp(parent, text, size, style, alignment);
            TopLeft((RectTransform)t.transform, position, sizeDelta);
            return t;
        }

        private static TMP_Text NewTmp(Transform parent, string text, float size, FontStyles style, TextAlignmentOptions alignment)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = TextColor; tmp.fontStyle = style; tmp.alignment = alignment;
            tmp.raycastTarget = false; tmp.richText = true; tmp.textWrappingMode = TextWrappingModes.NoWrap; tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }
    }
}
