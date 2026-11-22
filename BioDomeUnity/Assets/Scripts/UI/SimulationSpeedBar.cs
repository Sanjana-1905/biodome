using System.Collections.Generic;
using BioDome.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BioDome.UI
{
    /// <summary>
    /// Small runtime-built bar with Pause and 1x / 2x / 5x / 10x buttons. 1x is the pace configured on the GreenhouseManager.
    /// It only calls the manager's speed API (so environment, irrigation and plants always share one clock) and highlights the active choice.
    /// </summary>
    public sealed class SimulationSpeedBar : MonoBehaviour
    {
        private static readonly float[] Multipliers = { 1f, 2f, 5f, 10f, 50f, 100f };
        private static readonly Color Idle = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color Active = new Color(0.45f, 0.8f, 0.5f, 0.55f);

        private GreenhouseManager manager;
        private Image pauseImage;
        private readonly List<Image> speedImages = new List<Image>();

        public static SimulationSpeedBar Create(GreenhouseManager manager)
        {
            var go = new GameObject("SimulationSpeedBar");
            go.transform.SetParent(manager.transform, false);
            var bar = go.AddComponent<SimulationSpeedBar>();
            bar.manager = manager;
            bar.Build();
            return bar;
        }

        private void Build()
        {
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            const float w = 44f, h = 20f, gap = 3f, pad = 5f;
            int count = Multipliers.Length + 1;
            var panel = NewImage("Bar", canvasObject.transform, new Color(0.07f, 0.10f, 0.09f, 0.78f));
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-12f, -56f);
            panel.sizeDelta = new Vector2(pad * 2f + count * w + (count - 1) * gap, pad * 2f + h);

            pauseImage = AddButton(panel, "Pause", 0, w, h, gap, pad, () => manager.SetPaused(!manager.Paused));
            for (int i = 0; i < Multipliers.Length; i++)
            {
                float m = Multipliers[i];
                speedImages.Add(AddButton(panel, m + "x", i + 1, w, h, gap, pad, () => { manager.SetPaused(false); manager.SetSpeedMultiplier(m); }));
            }
        }

        private static Image AddButton(RectTransform parent, string label, int index, float w, float h, float gap, float pad, UnityEngine.Events.UnityAction onClick)
        {
            var rect = NewImage("Btn_" + label, parent, Idle);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(pad + index * (w + gap), -pad);
            rect.sizeDelta = new Vector2(w, h);

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(rect, false);
            var tmp = textObject.GetComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 10.5f; tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.92f, 0.96f, 0.92f); tmp.raycastTarget = false; tmp.textWrappingMode = TextWrappingModes.NoWrap;
            var trt = (RectTransform)textObject.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;

            var image = rect.GetComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            return image;
        }

        private void Update()
        {
            if (manager == null) return;
            bool paused = manager.Paused;
            pauseImage.color = paused ? Active : Idle;
            float current = manager.SimulatedHoursPerSecond / manager.NormalHoursPerSecond;
            for (int i = 0; i < speedImages.Count; i++)
                speedImages[i].color = !paused && Mathf.Abs(current - Multipliers[i]) < 0.01f * Multipliers[i] ? Active : Idle;
        }

        private static RectTransform NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }
    }
}
