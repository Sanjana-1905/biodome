using System;
using System.Collections.Generic;
using BioDome.CameraSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BioDome.Experiment
{
    /// <summary>
    /// A small "go to" bar: one button per viewpoint (overview, fountain, collections, research garden, each climate pod).
    /// Each button flies the camera to a viewpoint; the overview button uses the camera's own Reset View. Builds its own canvas at runtime.
    /// </summary>
    public class ViewShortcuts : MonoBehaviour
    {
        [Serializable]
        public class ViewPoint
        {
            public string label = "View";
            [Tooltip("0 = first row (areas), 1 = second row (pods).")] public int row;
            [Tooltip("Where to look. Leave empty for the overview (Reset View).")] public Transform target;
            public float distance = 8f;
            public float yaw;
            public float pitch = 30f;
            public Color accent = new Color(0.75f, 0.85f, 0.75f);
        }

        [SerializeField] private CameraController cameraController;
        [SerializeField] private List<ViewPoint> viewPoints = new List<ViewPoint>();

        private void Start()
        {
            if (cameraController == null || viewPoints.Count == 0) { enabled = false; return; }
            Build();
        }

        private void Build()
        {
            var canvasObject = new GameObject("ViewShortcutsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            int rows = 1;
            foreach (ViewPoint v in viewPoints) rows = Mathf.Max(rows, v.row + 1);
            const float buttonH = 20f, gap = 3f, pad = 5f;
            float width = 396f, height = pad * 2f + rows * buttonH + (rows - 1) * gap;

            var panel = NewImage("Bar", canvasObject.transform, new Color(0.07f, 0.10f, 0.09f, 0.78f));
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, 46f);
            panel.sizeDelta = new Vector2(width, height);

            for (int r = 0; r < rows; r++)
            {
                var row = viewPoints.FindAll(v => v.row == r);
                if (row.Count == 0) continue;
                float cell = (width - pad * 2f - gap * (row.Count - 1)) / row.Count;
                for (int i = 0; i < row.Count; i++)
                {
                    ViewPoint view = row[i];
                    var rect = NewImage("Btn_" + view.label, panel, new Color(1f, 1f, 1f, 0.10f));
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                    rect.anchoredPosition = new Vector2(pad + i * (cell + gap), -(pad + r * (buttonH + gap)));
                    rect.sizeDelta = new Vector2(cell, buttonH);

                    var accentBar = NewImage("Accent", rect, view.accent);
                    accentBar.anchorMin = Vector2.zero; accentBar.anchorMax = new Vector2(1f, 0f);
                    accentBar.pivot = new Vector2(0.5f, 0f); accentBar.sizeDelta = new Vector2(0f, 2f);
                    accentBar.GetComponent<Image>().raycastTarget = false;

                    var textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                    textObject.transform.SetParent(rect, false);
                    var tmp = textObject.GetComponent<TextMeshProUGUI>();
                    tmp.text = view.label; tmp.fontSize = 10.5f; tmp.alignment = TextAlignmentOptions.Center;
                    tmp.color = new Color(0.92f, 0.96f, 0.92f); tmp.raycastTarget = false; tmp.textWrappingMode = TextWrappingModes.NoWrap;
                    var trt = (RectTransform)textObject.transform;
                    trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;

                    var button = rect.gameObject.AddComponent<Button>();
                    button.targetGraphic = rect.GetComponent<Image>();
                    ViewPoint captured = view;
                    button.onClick.AddListener(() => GoTo(captured));
                }
            }
        }

        private void GoTo(ViewPoint view)
        {
            if (cameraController == null) return;
            if (view.target == null) cameraController.ResetView();
            else cameraController.FlyTo(view.target.position, view.distance, view.yaw, view.pitch);
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
