using System.Text;
using BioDome.CameraSystem;
using BioDome.Core;
using BioDome.Environment;
using BioDome.Plants;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BioDome.Experiment
{
    /// <summary>
    /// Context-sensitive pod controls. When the player (first-person camera) or the inspection view comes close to one climate pod, a compact panel for THAT pod
    /// appears: its live settings (temperature, humidity, light, irrigation share), the live response of its plants, and a reset button. It disappears when the player
    /// walks away. Every change goes through <see cref="ExperimentPod.SetLiveConditions"/>, i.e. the pod's own runtime profile: no other pod and not the greenhouse
    /// environment is touched. No simulation is added. Keys (so it also works while the mouse is captured in first person):
    /// Up / Down = choose a setting, Left / Right = change it, Backspace = reset the pod. The buttons work whenever the cursor is free.
    /// Added at run time next to the <see cref="ExperimentGarden"/> (the scene is not modified); also gives each pod its <see cref="PodVisualCues"/>.
    /// </summary>
    public class PodProximityPanel : MonoBehaviour
    {
        [SerializeField, Tooltip("Distance (m) from the pod centre at which the panel appears for a walking player.")] private float showDistance = 3.3f;
        [SerializeField, Tooltip("Same, for the inspection (orbit) camera's focus point.")] private float showDistanceOrbit = 3.0f;
        [SerializeField, Tooltip("Extra distance before the panel hides again, so it does not flicker at the edge.")] private float hideMargin = 0.7f;

        private ExperimentGarden garden;
        private CameraModeManager modes;
        private CameraController orbit;
        private Camera cam;
        private PodTestPanel podLab;

        private int podIndex = -1;
        private int row;
        private float timer;

        private GameObject panel;
        private TMP_Text title, readout, hint;
        private readonly Image[] rowBackground = new Image[4];
        private readonly TMP_Text[] values = new TMP_Text[4];
        private readonly Image[] stageButtons = new Image[5];
        private Image liveButton;
        private readonly StringBuilder text = new StringBuilder(512);

        private static readonly string[] Names = { "Temperature offset", "Humidity offset", "Light (x greenhouse)", "Irrigation share" };
        private static readonly float[] Steps = { 2f, 0.05f, 0.1f, 0.1f };
        private static readonly float[] Mins = { -20f, -0.6f, 0f, 0f };
        private static readonly float[] Maxs = { 20f, 0.4f, 1f, 1f };

        private static readonly Color PanelColor = new Color(0.06f, 0.09f, 0.08f, 0.93f);
        private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.14f);
        private static readonly Color RowOn = new Color(0.30f, 0.62f, 0.36f, 0.35f);
        private static readonly Color TextColor = new Color(0.92f, 0.96f, 0.92f, 1f);
        private static readonly Color SubText = new Color(0.70f, 0.80f, 0.72f, 1f);

        public int NearbyPodIndex => podIndex;
        public bool IsShowing => panel != null && panel.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            var g = Object.FindAnyObjectByType<ExperimentGarden>();
            if (g != null && g.GetComponent<PodProximityPanel>() == null) g.gameObject.AddComponent<PodProximityPanel>();
        }

        private void Start()
        {
            garden = GetComponent<ExperimentGarden>();
            if (garden == null || garden.Pods.Count == 0) { enabled = false; return; }
            modes = FindAnyObjectByType<CameraModeManager>();
            orbit = FindAnyObjectByType<CameraController>();
            cam = Camera.main;
            podLab = GetComponent<PodTestPanel>();
            foreach (ExperimentPod pod in garden.Pods)
                if (pod != null && pod.GetComponent<PodVisualCues>() == null) pod.gameObject.AddComponent<PodVisualCues>();
            Build();
            panel.SetActive(false);
        }

        private bool TryReference(out Vector3 position, out float range)
        {
            position = default; range = 0f;
            if (cam == null) cam = Camera.main;
            if (modes == null) modes = FindAnyObjectByType<CameraModeManager>();
            if (modes != null && modes.Mode == CameraMode.FirstPerson && cam != null) { position = cam.transform.position; range = showDistance; return true; }
            if (orbit != null && orbit.enabled && orbit.Distance < 14f) { position = orbit.Pivot; range = showDistanceOrbit; return true; }
            return false;
        }

        private bool relabeled;

        // The bottom shortcut bar names the pods by their conditions ("Low humidity", not just "Humidity").
        private void RelabelShortcuts()
        {
            relabeled = true;
            string[,] names = { { "Btn_Heat", "Heat stress" }, { "Btn_Humidity", "Low humidity" }, { "Btn_Light", "Low light" }, { "Btn_Water", "Water stress" } };
            for (int i = 0; i < names.GetLength(0); i++)
            {
                GameObject b = GameObject.Find(names[i, 0]);
                TMP_Text label = b != null ? b.GetComponentInChildren<TMP_Text>() : null;
                if (label != null) { label.text = names[i, 1]; label.fontSize = 9f; label.enableAutoSizing = true; label.fontSizeMin = 7f; label.fontSizeMax = 10.5f; }
            }
        }

        private void Update()
        {
            if (!relabeled && Time.frameCount > 10) RelabelShortcuts();
            int nearest = -1;
            if (TryReference(out Vector3 p, out float range) && (podLab == null || !podLab.IsOpen))
            {
                float best = float.MaxValue;
                for (int i = 0; i < garden.Pods.Count; i++)
                {
                    ExperimentPod pod = garden.Pods[i];
                    if (pod == null) continue;
                    Vector3 d = pod.transform.position - p; d.y = 0f;
                    if (d.magnitude < best) { best = d.magnitude; nearest = i; }
                }
                float limit = range + (podIndex == nearest && podIndex >= 0 ? hideMargin : 0f);
                if (best > limit) nearest = -1;
            }

            if (nearest != podIndex)
            {
                podIndex = nearest;
                panel.SetActive(podIndex >= 0);
                if (podIndex >= 0) row = 0;
                Refresh();
            }
            if (podIndex < 0) return;

            HandleKeys();
            timer += Time.unscaledDeltaTime;
            if (timer >= 0.2f) { timer = 0f; Refresh(); }
        }

        private void HandleKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<TMP_InputField>() != null) return;
            if (kb.downArrowKey.wasPressedThisFrame) { row = (row + 1) % 4; Refresh(); }
            if (kb.upArrowKey.wasPressedThisFrame) { row = (row + 3) % 4; Refresh(); }
            if (kb.rightArrowKey.wasPressedThisFrame) Change(row, +1);
            if (kb.leftArrowKey.wasPressedThisFrame) Change(row, -1);
            if (kb.backspaceKey.wasPressedThisFrame) ResetPod();
            if (kb.periodKey.wasPressedThisFrame) StepStage(+1);
            if (kb.commaKey.wasPressedThisFrame) StepStage(-1);
            if (kb.slashKey.wasPressedThisFrame) SetStage(-1);
        }

        // --- Actions (all on this pod only) --------------------------------------------

        private ExperimentPod Pod => podIndex >= 0 ? garden.Pods[podIndex] : null;

        private float[] Current()
        {
            ZoneProfile z = Pod.Profile;
            return new[] { z.TemperatureOffsetCelsius, z.HumidityOffset, z.LightMultiplier, z.WaterShareWeight };
        }

        public void Change(int parameter, int direction)
        {
            ExperimentPod pod = Pod;
            if (pod == null || pod.Profile == null) return;
            row = parameter;
            float[] v = Current();
            v[parameter] = Mathf.Clamp(Mathf.Round((v[parameter] + direction * Steps[parameter]) / Steps[parameter]) * Steps[parameter], Mins[parameter], Maxs[parameter]);
            pod.SetLiveConditions(v[0], v[1], v[2], v[3]);
            Refresh();
        }

        /// <summary>Visual-only growth-stage preview of the nearby pod's plants (0 Seed .. 4 Mature), or -1 for the live simulation.</summary>
        public void SetStage(int stage)
        {
            if (Pod == null) return;
            Pod.SetGrowthPreview(stage);
            Refresh();
        }

        private void StepStage(int direction)
        {
            if (Pod == null) return;
            int current = Pod.PreviewStage;
            int next = current < 0 ? (direction > 0 ? 0 : ExperimentPod.StageNames.Length - 1) : current + direction;
            SetStage(next < 0 || next >= ExperimentPod.StageNames.Length ? -1 : next);
        }

        public void ResetPod()
        {
            if (Pod == null) return;
            Pod.ResetLiveConditions();
            Refresh();
        }

        // --- Readout -----------------------------------------------------------------------

        private void Refresh()
        {
            if (panel == null || !panel.activeSelf || Pod == null) return;
            ExperimentPod pod = Pod;
            title.text = $"<color=#{ColorUtility.ToHtmlStringRGB(pod.Accent)}>{pod.DisplayName}</color> <size=70%><color=#9fb3a6>pod controls</color></size>";

            float[] v = Current();
            ZoneProfile o = pod.OriginalProfile;
            float[] orig = { o.TemperatureOffsetCelsius, o.HumidityOffset, o.LightMultiplier, o.WaterShareWeight };
            string[] label = { $"{v[0]:+0;-0;0} °C", $"{v[1] * 100f:+0;-0;0} % RH", $"{v[2] * 100f:0} %", $"{v[3] * 100f:0} %" };
            for (int i = 0; i < 4; i++)
            {
                values[i].text = Mathf.Abs(v[i] - orig[i]) < 1e-4f ? label[i] : $"<color=#f0c060>{label[i]}</color>";
                rowBackground[i].color = i == row ? RowOn : Color.clear;
            }

            for (int i = 0; i < stageButtons.Length; i++) stageButtons[i].color = pod.PreviewStage == i ? RowOn * 2f : ButtonColor;
            liveButton.color = pod.PreviewStage < 0 ? RowOn * 2f : ButtonColor;

            EnvironmentConditions g = garden.GreenhouseNow;
            EnvironmentConditions c = pod.ConditionsFor(g);
            ExperimentPod.Metrics m = pod.Measure();
            PlantController sample = null;
            for (int i = 0; i < pod.Plants.Count && sample == null; i++) if (pod.Plants[i] != null && pod.Plants[i].IsInitialized) sample = pod.Plants[i];

            text.Clear();
            if (pod.PreviewStage >= 0) text.Append($"<color=#f0c060>Showing {ExperimentPod.StageNames[pod.PreviewStage]} (preview only). Values below are the real simulation.</color>\n");
            text.Append($"Pod climate   <b>{c.TemperatureCelsius:0.0}°C  {c.Humidity01 * 100f:0}% RH  light {c.Light01 * 100f:0}%</b>\n");
            text.Append($"<color=#9fb3a6>Greenhouse   {g.TemperatureCelsius:0.0}°C  {g.Humidity01 * 100f:0}% RH  light {g.Light01 * 100f:0}%</color>\n");
            text.Append($"Growth <b>{m.Growth * 100f:0}%</b>   Health <b>{m.Health * 100f:0}%</b>   Soil <b>{m.Moisture * 100f:0}%</b>   alive {m.Alive}/{m.Total}\n");
            text.Append($"Stress {Bar("temp", m.TemperatureStress)}{Bar("hum", m.HumidityStress)}{Bar("light", m.LightStress)}{Bar("water", m.WaterStress)}");
            if (sample != null)
            {
                var s = sample.Simulation;
                text.Append($"\nPlant 1: <b>{s.Stage.DisplayName()}</b>, {s.State}, wilt {s.Wilt01 * 100f:0}%{(s.IsRecovering ? " <color=#9fe0a0>recovering</color>" : "")}, growth rate x{s.GrowthFactor01:0.00}");
            }
            readout.text = text.ToString();
        }

        private static string Bar(string name, float v)
        {
            string color = v < 0.3f ? "9fe0a0" : v < 0.6f ? "f0c060" : "f08a6a";
            return $"{name} <color=#{color}>{v * 100f:0}%</color>  ";
        }

        // --- UI ----------------------------------------------------------------------------

        private void Build()
        {
            var canvasObject = new GameObject("PodProximityCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 7;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            const float width = 400f, pad = 10f, rowH = 30f;
            float height = pad + 30f + 20f + 4f * (rowH + 2f) + 36f + 8f + 58f + 128f + pad;
            RectTransform p = NewImage("Panel", canvasObject.transform, PanelColor);
            panel = p.gameObject;
            p.anchorMin = p.anchorMax = p.pivot = new Vector2(0f, 0f);
            p.anchoredPosition = new Vector2(20f, 72f); // bottom-left, below the environment panel and above the footer
            p.sizeDelta = new Vector2(width, height);

            float y = -pad;
            title = NewText("Title", p, "", 19f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, new Vector2(pad, y), new Vector2(width - 2 * pad, 30f));
            y -= 30f;
            hint = NewText("Hint", p, "↑↓ choose · ←→ change · Backspace reset · Esc = mouse", 11.5f, FontStyles.Italic, TextAlignmentOptions.MidlineLeft, new Vector2(pad, y), new Vector2(width - 2 * pad, 20f));
            hint.color = SubText;
            y -= 22f;

            for (int i = 0; i < 4; i++)
            {
                int index = i;
                RectTransform r = NewImage("Row" + i, p, Color.clear);
                Place(r, new Vector2(pad, y), new Vector2(width - 2 * pad, rowH));
                rowBackground[i] = r.GetComponent<Image>();
                rowBackground[i].raycastTarget = false;
                NewText("Name", r, Names[i], 14.5f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, new Vector2(6f, 0f), new Vector2(190f, rowH));
                NewButton(r, "-", new Vector2(width - 2 * pad - 168f, -2f), new Vector2(34f, rowH - 4f), () => Change(index, -1));
                values[i] = NewText("Value", r, "", 14.5f, FontStyles.Bold, TextAlignmentOptions.Midline, new Vector2(width - 2 * pad - 132f, 0f), new Vector2(92f, rowH));
                NewButton(r, "+", new Vector2(width - 2 * pad - 38f, -2f), new Vector2(34f, rowH - 4f), () => Change(index, +1));
                y -= rowH + 2f;
            }
            NewButton(p, "Reset this pod", new Vector2(pad, y - 2f), new Vector2(140f, 30f), ResetPod);
            y -= 38f;
            NewText("StageLabel", p, "Growth preview (visual only, simulation untouched)  ·  , . keys", 11.5f, FontStyles.Italic, TextAlignmentOptions.MidlineLeft, new Vector2(pad, y), new Vector2(width - 2 * pad, 18f)).color = SubText;
            y -= 20f;
            float bw = (width - 2 * pad - 5 * 4f) / 6f;
            for (int i = 0; i < 5; i++)
            {
                int stage = i;
                stageButtons[i] = NewButton(p, ExperimentPod.StageNames[i], new Vector2(pad + i * (bw + 4f), y), new Vector2(bw, 28f), () => SetStage(stage), 11f);
            }
            liveButton = NewButton(p, "Live", new Vector2(pad + 5 * (bw + 4f), y), new Vector2(bw, 28f), () => SetStage(-1), 11f);
            y -= 36f;
            readout = NewText("Readout", p, "", 13.5f, FontStyles.Normal, TextAlignmentOptions.TopLeft, new Vector2(pad, y), new Vector2(width - 2 * pad, 128f));
            readout.color = TextColor;
            readout.richText = true;
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }

        private static RectTransform NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        private static TMP_Text NewText(string name, Transform parent, string content, float size, FontStyles style, TextAlignmentOptions align, Vector2 topLeft, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = content; t.fontSize = size; t.fontStyle = style; t.alignment = align; t.color = TextColor; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            Place((RectTransform)go.transform, topLeft, box);
            return t;
        }

        private static Image NewButton(Transform parent, string label, Vector2 topLeft, Vector2 size, UnityEngine.Events.UnityAction action, float fontSize = 14f)
        {
            RectTransform r = NewImage("Btn_" + label, parent, ButtonColor);
            Place(r, topLeft, size);
            TMP_Text t = NewText("Label", r, label, fontSize, FontStyles.Normal, TextAlignmentOptions.Center, Vector2.zero, size);
            ((RectTransform)t.transform).anchorMin = Vector2.zero; ((RectTransform)t.transform).anchorMax = Vector2.one;
            ((RectTransform)t.transform).offsetMin = ((RectTransform)t.transform).offsetMax = Vector2.zero;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = r.GetComponent<Image>();
            b.onClick.AddListener(() => { action(); if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); });
            return r.GetComponent<Image>();
        }
    }
}
