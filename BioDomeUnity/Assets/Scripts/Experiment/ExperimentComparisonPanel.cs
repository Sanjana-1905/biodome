using System.Collections.Generic;
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
        private sealed class Row { public ExperimentPod pod; public TMP_Text name; public Cell[] cells = new Cell[4]; }

        private readonly List<Row> rows = new List<Row>();
        private GameObject body;
        private TMP_Text statusText;
        private float timer;

        private static readonly string[] Headings = { "Growth", "Health", "Moisture", "Stress" };
        private static readonly Color[] BarColors =
        {
            new Color(0.45f, 0.82f, 0.45f), new Color(0.35f, 0.78f, 0.75f), new Color(0.40f, 0.62f, 0.95f), new Color(0.95f, 0.50f, 0.35f)
        };

        private void Start()
        {
            if (garden == null) { enabled = false; return; }
            Build();
        }

        private void Update()
        {
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
            }
            if (statusText != null && garden.StartChecked)
                statusText.text = garden.StartsEquivalent ? "All groups started from an equivalent state" : "Warning: groups did not start equivalent";
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
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            const float width = 372f, headerH = 26f, rowH = 24f, pad = 8f;
            float bodyH = pad + 18f + garden.Pods.Count * rowH + 20f + pad;

            RectTransform panel = NewRect("Panel", canvasObject.transform, PanelColor);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
            panel.anchoredPosition = new Vector2(-10f, 46f);
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

            float nameW = 112f, cellW = (width - nameW - pad * 2f) / 4f;
            float y = -pad;

            // Column headings.
            for (int c = 0; c < 4; c++)
                NewText("H" + c, bodyRect, Headings[c], 11f, SubText, TextAlignmentOptions.Midline, FontStyles.Normal,
                    new Vector2(pad + nameW + c * cellW, y), new Vector2(cellW, 18f), true);
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
                rows.Add(row);
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
            tmp.raycastTarget = false; tmp.textWrappingMode = TextWrappingModes.NoWrap; tmp.overflowMode = TextOverflowModes.Overflow;
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
