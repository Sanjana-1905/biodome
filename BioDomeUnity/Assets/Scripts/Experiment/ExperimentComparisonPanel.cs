using System.Collections.Generic;
using BioDome.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BioDome.Experiment
{
    /// <summary>
    /// Compact comparison table for the five pods: growth, health, soil moisture and stress as bars with percentages.
    /// Builds its own small canvas at runtime (bottom-right, collapsible) so the existing UI is untouched.
    /// </summary>
    public class ExperimentComparisonPanel : MonoBehaviour
    {
        [SerializeField] private ExperimentGarden garden;
        [SerializeField, Min(0.1f)] private float refreshInterval = 0.25f;

        private static readonly Color PanelColor = new Color(0.07f, 0.10f, 0.09f, 0.86f);
        private static readonly Color BarBack = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color TextColor = new Color(0.92f, 0.96f, 0.92f, 1f);
        private static readonly Color SubText = new Color(0.72f, 0.80f, 0.74f, 1f);

        private sealed class Cell { public Image fill; public TMP_Text value; }
        private sealed class Row { public ExperimentPod pod; public TMP_Text name; public Cell[] cells = new Cell[4]; public TMP_Text delta; public TMP_Text[] sources = new TMP_Text[4]; }

        private readonly List<Row> rows = new List<Row>();
        private GameObject body;
        private TMP_Text statusText;
        private TMP_Text clockText;
        private float timer;
        private ExperimentPod control;

        private static readonly string[] Headings = { "Growth", "Health", "Moisture", "Stress" };
        private static readonly Color[] BarColors =
        {
            new Color(0.45f, 0.82f, 0.45f), new Color(0.35f, 0.78f, 0.75f), new Color(0.40f, 0.62f, 0.95f), new Color(0.95f, 0.50f, 0.35f)
        };

        private GameObject canvasRoot;

        /// <summary>Shows or hides the whole panel (Pod Lab hides it while open, since both use the right-hand column).</summary>
        public void SetVisible(bool visible) => externallyHidden = !visible;

        private bool externallyHidden;
        private GreenhouseManager manager;

        // The right-hand column shows one panel at a time: Pod Lab, else the plant inspector (top-right), else this panel.
        private void UpdateVisibility()
        {
            if (canvasRoot == null) return;
            bool show = !externallyHidden && (manager == null || manager.SelectedPlant == null);
            if (canvasRoot.activeSelf != show) canvasRoot.SetActive(show);
        }

        private void Start()
        {
            if (garden == null) { enabled = false; return; }
            manager = FindAnyObjectByType<GreenhouseManager>();
            control = garden.Pods[garden.ControlIndex];
            Build();
        }

        private void Update()
        {
            UpdateVisibility();
            timer += Time.unscaledDeltaTime;
            if (timer < refreshInterval) return;
            timer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            foreach (Row row in rows)
            {
                ExperimentPod.Metrics m = row.pod.Measure();
                row.name.text = m.Alive < m.Total ? $"{row.pod.DisplayName}  <size=70%>{m.Alive}/{m.Total}</size>" : row.pod.DisplayName;
                Set(row.cells[0], m.Growth); Set(row.cells[1], m.Health); Set(row.cells[2], m.Moisture); Set(row.cells[3], m.Stress);
                row.sources[0].text = Pct(m.TemperatureStress); row.sources[1].text = Pct(m.HumidityStress);
                row.sources[2].text = Pct(m.LightStress); row.sources[3].text = Pct(m.WaterStress);
                if (row.pod == control) row.delta.text = "<color=#9fb3a6>control</color>";
                else
                {
                    ExperimentPod.Metrics c = control.Measure();
                    row.delta.text = $"{Signed(m.Growth - c.Growth)} / {Signed(m.Health - c.Health)}";
                }
            }
            if (clockText != null)
            {
                float t = garden.CurrentTimeOfDay;
                EnvironmentConditions g = garden.GreenhouseNow;
                clockText.text = $"Day {garden.CurrentDay}  {Mathf.FloorToInt(t):00}:{Mathf.FloorToInt((t % 1f) * 60f):00}  ·  {g.Season}  ·  greenhouse {g.TemperatureCelsius:0.0}°C";
            }
            if (statusText != null && garden.StartChecked)
                statusText.text = garden.StartsEquivalent ? "All groups started from an equivalent state" : "Warning: groups did not start equivalent";
        }

        private static string Pct(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f) + "%";

        // Difference from the control group in percentage points; green = better than control for growth/health.
        private static string Signed(float diff)
        {
            int pp = Mathf.RoundToInt(diff * 100f);
            string color = pp > 0 ? "#7fd67f" : pp < 0 ? "#f08a6a" : "#9fb3a6";
            return $"<color={color}>{(pp > 0 ? "+" : "")}{pp}</color>";
        }

        private static void Set(Cell c, float value01)
        {
            value01 = Mathf.Clamp01(value01);
            var rt = (RectTransform)c.fill.transform;
            rt.anchorMax = new Vector2(value01, 1f);
            c.value.text = Mathf.RoundToInt(value01 * 100f) + "%";
        }

        // --- UI construction -----------------------------------------------------

        private void Build()
        {
            var canvasObject = new GameObject("ExperimentCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasRoot = canvasObject;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            const float width = 430f, headerH = 26f, rowH = 24f, pad = 8f;
            int n = garden.Pods.Count;
            float bodyH = pad + 20f + 18f + n * rowH + 22f + 34f + n * rowH + 22f + pad;

            RectTransform panel = NewRect("Panel", canvasObject.transform, PanelColor);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
            panel.anchoredPosition = new Vector2(-10f, 100f); // above the view-shortcut bar
            panel.sizeDelta = new Vector2(width, headerH + bodyH);

            // Header with collapse button.
            RectTransform header = NewRect("Header", panel, new Color(1f, 1f, 1f, 0.06f));
            header.anchorMin = new Vector2(0f, 1f); header.anchorMax = new Vector2(1f, 1f); header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, headerH);
            NewText("Title", header, "Experiment · Monstera climate pods", 13f, TextColor, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, new Vector2(10f, 0f), new Vector2(-54f, 0f));

            RectTransform toggle = NewRect("Toggle", header, new Color(1f, 1f, 1f, 0.14f));
            toggle.anchorMin = new Vector2(1f, 0.5f); toggle.anchorMax = new Vector2(1f, 0.5f); toggle.pivot = new Vector2(1f, 0.5f);
            toggle.anchoredPosition = new Vector2(-4f, 0f); toggle.sizeDelta = new Vector2(46f, 18f);
            TMP_Text toggleLabel = NewText("Label", toggle, "Hide", 11f, TextColor, TextAlignmentOptions.Center, FontStyles.Normal, Vector2.zero, Vector2.zero);
            var button = toggle.gameObject.AddComponent<Button>();
            button.targetGraphic = toggle.GetComponent<Image>();
            button.onClick.AddListener(() =>
            {
                body.SetActive(!body.activeSelf);
                toggleLabel.text = body.activeSelf ? "Hide" : "Show";
                panel.sizeDelta = new Vector2(width, headerH + (body.activeSelf ? bodyH : 0f));
            });

            // Body.
            RectTransform bodyRect = NewRect("Body", panel, Color.clear);
            bodyRect.anchorMin = Vector2.zero; bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = Vector2.zero; bodyRect.offsetMax = new Vector2(0f, -headerH);
            body = bodyRect.gameObject;

            float nameW = 112f, deltaW = 62f, cellW = (width - nameW - deltaW - pad * 2f) / 4f;
            float y = -pad;

            clockText = NewText("Clock", bodyRect, "", 11.5f, TextColor, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, new Vector2(pad, y), new Vector2(width - pad * 2f, 20f), true);
            y -= 20f;

            // Column headings.
            for (int c = 0; c < 4; c++)
                NewText("H" + c, bodyRect, Headings[c], 11f, SubText, TextAlignmentOptions.Midline, FontStyles.Normal,
                    new Vector2(pad + nameW + c * cellW, y), new Vector2(cellW, 18f), true);
            NewText("HDelta", bodyRect, "vs control", 10f, SubText, TextAlignmentOptions.Midline, FontStyles.Normal,
                new Vector2(pad + nameW + 4f * cellW, y), new Vector2(deltaW, 18f), true);
            y -= 18f;

            foreach (ExperimentPod pod in garden.Pods)
            {
                var row = new Row { pod = pod };
                RectTransform swatch = NewRect("Swatch", bodyRect, pod.Accent);
                SetTopLeft(swatch, new Vector2(pad, y - 4f), new Vector2(4f, rowH - 8f));
                row.name = NewText("Name", bodyRect, pod.DisplayName, 12f, TextColor, TextAlignmentOptions.MidlineLeft, FontStyles.Bold,
                    new Vector2(pad + 9f, y), new Vector2(nameW - 9f, rowH), true);
                for (int c = 0; c < 4; c++)
                {
                    float x = pad + nameW + c * cellW;
                    RectTransform back = NewRect("Bar", bodyRect, BarBack);
                    SetTopLeft(back, new Vector2(x + 2f, y - 5f), new Vector2(cellW - 4f, rowH - 10f));
                    RectTransform fill = NewRect("Fill", back, BarColors[c]);
                    fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f); fill.offsetMin = fill.offsetMax = Vector2.zero;
                    TMP_Text value = NewText("Value", back, "0%", 10f, TextColor, TextAlignmentOptions.Center, FontStyles.Normal, Vector2.zero, Vector2.zero);
                    row.cells[c] = new Cell { fill = fill.GetComponent<Image>(), value = value };
                }
                row.delta = NewText("Delta", bodyRect, "", 11f, TextColor, TextAlignmentOptions.Midline, FontStyles.Normal,
                    new Vector2(pad + nameW + 4f * cellW, y), new Vector2(deltaW, rowH), true);
                rows.Add(row);
                y -= rowH;
            }
            NewText("SourcesTitle", bodyRect, "Stress sources (average per group, % of maximum)", 10.5f, SubText, TextAlignmentOptions.MidlineLeft, FontStyles.Italic,
                new Vector2(pad, y - 2f), new Vector2(width - pad * 2f, 18f), true);
            string[] srcHeadings = { "Temp", "Humidity", "Light", "Water" };
            for (int c = 0; c < 4; c++)
                NewText("SH" + c, bodyRect, srcHeadings[c], 10f, SubText, TextAlignmentOptions.Midline, FontStyles.Normal,
                    new Vector2(pad + nameW + c * cellW, y - 18f), new Vector2(cellW, 16f), true);
            y -= 36f;
            foreach (Row row in rows)
            {
                NewText("SName", bodyRect, row.pod.DisplayName, 11f, TextColor, TextAlignmentOptions.MidlineLeft, FontStyles.Normal,
                    new Vector2(pad + 9f, y), new Vector2(nameW - 9f, rowH), true);
                for (int c = 0; c < 4; c++)
                    row.sources[c] = NewText("S" + c, bodyRect, "0%", 11f, TextColor, TextAlignmentOptions.Midline, FontStyles.Normal,
                        new Vector2(pad + nameW + c * cellW, y), new Vector2(cellW, rowH), true);
                y -= rowH;
            }

            statusText = NewText("Status", bodyRect, "", 10.5f, SubText, TextAlignmentOptions.MidlineLeft, FontStyles.Italic,
                new Vector2(pad, y - 1f), new Vector2(width - pad * 2f, 18f), true);
            Refresh();
        }

        private static void SetTopLeft(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static RectTransform NewRect(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = color.a > 0f;
            return (RectTransform)go.transform;
        }

        // topLeft = true: position/size are absolute from the parent's top-left. false: position/size are insets (fill parent).
        private static TMP_Text NewText(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment, FontStyles style, Vector2 position, Vector2 sizeOrInset, bool topLeft = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = alignment; tmp.fontStyle = style;
            tmp.raycastTarget = false; tmp.richText = true; tmp.textWrappingMode = TextWrappingModes.NoWrap; tmp.overflowMode = TextOverflowModes.Overflow;
            var rt = (RectTransform)go.transform;
            if (topLeft) SetTopLeft(rt, position, sizeOrInset);
            else
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = position; rt.offsetMax = sizeOrInset;
            }
            return tmp;
        }
    }
}
