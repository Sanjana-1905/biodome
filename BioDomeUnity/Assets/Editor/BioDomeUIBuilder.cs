using System.Collections.Generic;
using System.Text;
using BioDome.Core;
using BioDome.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Builds the first interactive UI (Canvas, EventSystem, top bar, control panel, plant panel, footer)
    /// and wires it into the existing UIManager and GreenhouseManager. It only creates UI objects:
    /// the greenhouse, plants and simulation objects are never touched, apart from assigning
    /// GreenhouseManager.uiManager. Re-running replaces only the previous "BioDome_UI" root.
    ///
    /// All visual choices (colours, sizes, text) live in <see cref="Theme"/> so the look can be redesigned
    /// without touching UIManager or any simulation code. The scene is marked dirty but NOT saved.
    /// </summary>
    public static class BioDomeUIBuilder
    {
        public const string RootName = "BioDome_UI";
        private const string EventSystemName = "EventSystem";

        /// <summary>Everything visual. Edit here (or replace the builder) to restyle the UI.</summary>
        private static class Theme
        {
            public static readonly Color Panel = new Color(0.06f, 0.14f, 0.10f, 0.80f);
            public static readonly Color TopBar = new Color(0.05f, 0.12f, 0.09f, 0.88f);
            public static readonly Color Accent = new Color(0.55f, 0.82f, 0.58f, 1f);
            public static readonly Color Text = new Color(0.93f, 0.97f, 0.93f, 1f);
            public static readonly Color Muted = new Color(0.66f, 0.76f, 0.69f, 1f);
            public static readonly Color Divider = new Color(1f, 1f, 1f, 0.12f);
            public static readonly Color SliderTrack = new Color(0.17f, 0.28f, 0.21f, 1f);
            public static readonly Color SliderFill = new Color(0.45f, 0.72f, 0.50f, 1f);
            public static readonly Color SliderHandle = new Color(0.88f, 0.95f, 0.86f, 1f);
            public static readonly Color ToggleOff = new Color(0.15f, 0.26f, 0.19f, 0.95f);
            public static readonly Color ToggleOn = new Color(0.32f, 0.62f, 0.42f, 1f);
            public static readonly Color Button = new Color(0.22f, 0.42f, 0.30f, 1f);

            public const float TopBarHeight = 64f;
            public const float FooterHeight = 56f;
            public const float Margin = 20f;
            public const float ControlPanelWidth = 380f;
            public const float PlantPanelWidth = 400f;
            public const float PanelPadding = 20f;
            public const float PanelSpacing = 10f;

            public const float FontReadout = 24f;
            public const float FontTitle = 26f;
            public const float FontHeader = 20f;
            public const float FontBody = 20f;
            public const float FontSmall = 17f;
        }

        // Sprites shipped with Unity (no extra assets or packages needed).
        private static Sprite panelSprite;
        private static Sprite backgroundSprite;
        private static Sprite knobSprite;
        private static TMP_FontAsset font;

        // --- Entry point ---------------------------------------------------------

        [MenuItem("BioDome/Build Interactive UI")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("BioDome UI", "Stop Play Mode first. Edits made in Play Mode are lost.", "OK");
                return;
            }

            if (Resources.Load<TMP_Settings>("TMP Settings") == null)
            {
                EditorUtility.DisplayDialog("BioDome UI",
                    "TextMesh Pro essential resources are not imported yet.\n\n" +
                    "Choose Window > TextMeshPro > Import TMP Essential Resources, click Import, then run this tool again.",
                    "OK");
                return;
            }

            var manager = Object.FindAnyObjectByType<GreenhouseManager>();
            if (manager == null)
            {
                EditorUtility.DisplayDialog("BioDome UI",
                    "No GreenhouseManager found in the open scene. Open BioDome_Main (with the Simulation object) first.", "OK");
                return;
            }

            // Look at every root object of the open scene (GameObject.Find would miss an inactive one).
            List<GameObject> existingRoots = FindExistingRoots();
            if (existingRoots.Count > 0)
            {
                if (!EditorUtility.DisplayDialog("BioDome UI",
                        $"{existingRoots.Count} '{RootName}' object(s) already exist. Replace with one freshly built UI?\n" +
                        "(Only these UI objects are replaced. Nothing else in the scene is touched.)",
                        "Replace", "Cancel"))
                    return;
                foreach (GameObject old in existingRoots) Undo.DestroyObjectImmediate(old);
            }

            LoadResources();

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();

            var problems = new List<string>();
            GameObject root = BuildCanvas(out UIRefs refs);
            Undo.RegisterCreatedObjectUndo(root, "Build BioDome UI");

            var uiManager = root.AddComponent<UIManager>();
            WireUiManager(uiManager, refs, problems);
            string eventSystemNote = EnsureEventSystem();

            // The one change to an existing object: point the GreenhouseManager at the new UIManager.
            var managerObject = new SerializedObject(manager);
            SerializedProperty uiProperty = managerObject.FindProperty("uiManager");
            if (uiProperty == null) problems.Add("GreenhouseManager has no 'uiManager' field.");
            else
            {
                uiProperty.objectReferenceValue = uiManager;
                managerObject.ApplyModifiedProperties();
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;

            Report(manager, uiManager, eventSystemNote, problems);
        }

        // --- Hierarchy ------------------------------------------------------------

        /// <summary>References to every widget the UIManager needs.</summary>
        private sealed class UIRefs
        {
            public TextMeshProUGUI season, time, temperature, humidity, light;
            public Toggle pause;
            public Slider temperatureOffset, humidityOffset, shade;
            public TextMeshProUGUI temperatureOffsetValue, humidityOffsetValue, shadeValue;
            public TextMeshProUGUI irrigationStatus;
            public Toggle irrigationOff, irrigationManual, irrigationAuto;
            public Button refill;
            public GameObject plantPanel;
            public TextMeshProUGUI plantName, plantDetails;
            public Button resetView;
        }

        private static GameObject BuildCanvas(out UIRefs refs)
        {
            refs = new UIRefs();

            var root = new GameObject(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.layer = UiLayer;

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            BuildTopBar(root.transform, refs);
            BuildControlPanel(root.transform, refs);
            BuildPlantPanel(root.transform, refs);
            BuildFooter(root.transform, refs);
            return root;
        }

        private static void BuildTopBar(Transform parent, UIRefs refs)
        {
            RectTransform bar = NewPanel("TopBar", parent, Theme.TopBar);
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, Theme.TopBarHeight);

            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(24, 24, 8, 8);
            row.spacing = 28f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            refs.season = NewReadout("Season", bar, 270f, Theme.Accent, FontStyles.Bold);
            refs.time = NewReadout("Time", bar, 90f, Theme.Text, FontStyles.Normal);
            refs.temperature = NewReadout("Temperature", bar, 130f, Theme.Text, FontStyles.Normal);
            refs.humidity = NewReadout("Humidity", bar, 230f, Theme.Text, FontStyles.Normal);
            refs.light = NewReadout("Light", bar, 170f, Theme.Text, FontStyles.Normal);

            RectTransform spacer = NewRect("Spacer", bar);
            SetLayout(spacer.gameObject, flexibleWidth: 1f);

            refs.pause = NewToggle("PauseToggle", bar, "Pause", null);
            SetLayout(refs.pause.gameObject, preferredWidth: 130f);
        }

        private static void BuildControlPanel(Transform parent, UIRefs refs)
        {
            RectTransform panel = NewVerticalPanel("ControlPanel", parent, Theme.ControlPanelWidth);
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(Theme.Margin, -(Theme.TopBarHeight + Theme.Margin));

            NewHeader("Header_Environment", panel, "ENVIRONMENT");
            refs.temperatureOffset = NewSliderBlock(panel, "Temperature offset", -10f, 10f, out refs.temperatureOffsetValue);
            refs.humidityOffset = NewSliderBlock(panel, "Humidity offset", -0.3f, 0.3f, out refs.humidityOffsetValue);
            refs.shade = NewSliderBlock(panel, "Shade", 0f, 1f, out refs.shadeValue);

            NewDivider(panel);

            NewHeader("Header_Irrigation", panel, "IRRIGATION");
            refs.irrigationStatus = NewText("IrrigationStatus", panel, "Idle", Theme.FontBody, Theme.Text, TextAlignmentOptions.TopLeft, FontStyles.Normal);

            RectTransform modeRow = NewRect("ModeRow", panel);
            var rowLayout = modeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            var group = modeRow.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;

            refs.irrigationOff = NewToggle("Mode_Off", modeRow, "Off", group);
            refs.irrigationManual = NewToggle("Mode_Manual", modeRow, "Manual", group);
            refs.irrigationAuto = NewToggle("Mode_Auto", modeRow, "Auto", group);
            refs.irrigationAuto.SetIsOnWithoutNotify(true); // runtime SyncControls replaces this with the real mode

            refs.refill = NewButton("RefillButton", panel, "Refill reservoir");
        }

        private static void BuildPlantPanel(Transform parent, UIRefs refs)
        {
            RectTransform panel = NewVerticalPanel("PlantPanel", parent, Theme.PlantPanelWidth);
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-Theme.Margin, -(Theme.TopBarHeight + Theme.Margin));

            refs.plantName = NewText("PlantName", panel, "Plant", Theme.FontTitle, Theme.Accent, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            NewDivider(panel);
            refs.plantDetails = NewText("PlantDetails", panel, "", Theme.FontBody, Theme.Text, TextAlignmentOptions.TopLeft, FontStyles.Normal);

            refs.plantPanel = panel.gameObject; // UIManager hides it until a plant is selected
        }

        private static void BuildFooter(Transform parent, UIRefs refs)
        {
            RectTransform bar = NewPanel("Footer", parent, Theme.TopBar);
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, Theme.FooterHeight);

            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(24, 24, 8, 8);
            row.spacing = 20f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            TextMeshProUGUI hints = NewText("Hints", bar,
                "Option + drag: Orbit    Shift + drag: Pan    Two-finger scroll: Zoom    Left-click plant: Inspect    Click empty space: Deselect",
                Theme.FontSmall, Theme.Muted, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            SetLayout(hints.gameObject, flexibleWidth: 1f);

            refs.resetView = NewButton("ResetViewButton", bar, "Reset View");
            SetLayout(refs.resetView.gameObject, preferredWidth: 180f);
        }

        // --- Wiring ---------------------------------------------------------------

        private static void WireUiManager(UIManager ui, UIRefs r, List<string> problems)
        {
            var so = new SerializedObject(ui);
            Assign(so, "seasonText", r.season, problems);
            Assign(so, "timeText", r.time, problems);
            Assign(so, "temperatureText", r.temperature, problems);
            Assign(so, "humidityText", r.humidity, problems);
            Assign(so, "lightText", r.light, problems);
            Assign(so, "pauseToggle", r.pause, problems);
            Assign(so, "temperatureOffsetSlider", r.temperatureOffset, problems);
            Assign(so, "humidityOffsetSlider", r.humidityOffset, problems);
            Assign(so, "shadeSlider", r.shade, problems);
            Assign(so, "temperatureOffsetValueText", r.temperatureOffsetValue, problems);
            Assign(so, "humidityOffsetValueText", r.humidityOffsetValue, problems);
            Assign(so, "shadeValueText", r.shadeValue, problems);
            Assign(so, "irrigationStatusText", r.irrigationStatus, problems);
            Assign(so, "irrigationOffToggle", r.irrigationOff, problems);
            Assign(so, "irrigationManualToggle", r.irrigationManual, problems);
            Assign(so, "irrigationAutoToggle", r.irrigationAuto, problems);
            Assign(so, "refillButton", r.refill, problems);
            Assign(so, "plantPanel", r.plantPanel, problems);
            Assign(so, "plantNameText", r.plantName, problems);
            Assign(so, "plantDetailsText", r.plantDetails, problems);
            Assign(so, "resetViewButton", r.resetView, problems);
            so.ApplyModifiedProperties();

            // Read back: every reference above must have stuck. (simulationSpeedSlider and seasonDropdown are optional and left empty.)
            var check = new SerializedObject(ui);
            foreach (string name in new[]
            {
                "seasonText", "timeText", "temperatureText", "humidityText", "lightText", "pauseToggle",
                "temperatureOffsetSlider", "humidityOffsetSlider", "shadeSlider",
                "temperatureOffsetValueText", "humidityOffsetValueText", "shadeValueText",
                "irrigationStatusText", "irrigationOffToggle", "irrigationManualToggle", "irrigationAutoToggle",
                "refillButton", "plantPanel", "plantNameText", "plantDetailsText", "resetViewButton"
            })
            {
                SerializedProperty p = check.FindProperty(name);
                if (p == null || p.objectReferenceValue == null) problems.Add($"UIManager.{name} was not assigned.");
            }
        }

        private static void Assign(SerializedObject so, string field, Object value, List<string> problems)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) problems.Add($"UIManager has no field '{field}'.");
            else p.objectReferenceValue = value;
        }

        private static List<GameObject> FindExistingRoots()
        {
            var found = new List<GameObject>();
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == RootName) found.Add(go);
            }
            return found;
        }

        private static string EnsureEventSystem()
        {
            // Include inactive ones, so a disabled EventSystem is reused instead of a second one being created.
            EventSystem[] all = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            EventSystem existing = all.Length > 0 ? all[0] : null;
            if (existing == null)
            {
                var go = new GameObject(EventSystemName, typeof(EventSystem));
                Undo.RegisterCreatedObjectUndo(go, "Add EventSystem");
                AddInputModule(go);
                return "Created an EventSystem with InputSystemUIInputModule.";
            }

            // The project uses the new Input System only, so the legacy module would throw errors every frame.
            if (existing.GetComponent<InputSystemUIInputModule>() == null)
            {
                Undo.RecordObject(existing.gameObject, "Fix EventSystem input module");
                AddInputModule(existing.gameObject);
                var legacy = existing.GetComponent<StandaloneInputModule>();
                if (legacy != null)
                {
                    Undo.RecordObject(legacy, "Disable legacy input module");
                    legacy.enabled = false;
                }
                return $"Found an existing EventSystem ('{existing.name}'): added InputSystemUIInputModule and disabled the legacy StandaloneInputModule.";
            }

            return $"Reused the existing EventSystem ('{existing.name}'); it already has InputSystemUIInputModule.";
        }

        private static void AddInputModule(GameObject go)
        {
            var module = Undo.AddComponent<InputSystemUIInputModule>(go);
            module.AssignDefaultActions();
            EditorUtility.SetDirty(module);
        }

        private static void Report(GreenhouseManager manager, UIManager ui, string eventSystemNote, List<string> problems)
        {
            var text = new StringBuilder();
            text.AppendLine($"BioDome UI: built '{RootName}' (Canvas, TopBar, ControlPanel, PlantPanel, Footer) and assigned it to GreenhouseManager on '{manager.name}'.");
            text.AppendLine("  " + eventSystemNote);
            text.AppendLine("  The scene is marked as modified but NOT saved. Save it (Cmd+S) when you are happy.");
            if (problems.Count == 0) text.Append("  All UIManager references were assigned.");
            else
            {
                text.AppendLine("  PROBLEMS:");
                foreach (string p in problems) text.AppendLine("   - " + p);
            }

            if (problems.Count == 0) Debug.Log(text.ToString(), ui);
            else Debug.LogError(text.ToString(), ui);
        }

        // --- Widget factories -------------------------------------------------------

        private static int UiLayer
        {
            get
            {
                int layer = LayerMask.NameToLayer("UI");
                return layer < 0 ? 5 : layer;
            }
        }

        private static void LoadResources()
        {
            panelSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            backgroundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            font = TMP_Settings.defaultFontAsset;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Image AddImage(GameObject go, Color color, Sprite sprite, bool raycast)
        {
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        /// <summary>Translucent panel. It blocks clicks (raycastTarget) so the camera ignores presses on it.</summary>
        private static RectTransform NewPanel(string name, Transform parent, Color color)
        {
            RectTransform rt = NewRect(name, parent);
            AddImage(rt.gameObject, color, panelSprite, true);
            return rt;
        }

        /// <summary>Fixed-width panel whose height follows its content.</summary>
        private static RectTransform NewVerticalPanel(string name, Transform parent, float width)
        {
            RectTransform rt = NewPanel(name, parent, Theme.Panel);
            rt.sizeDelta = new Vector2(width, 0f);

            var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            int pad = Mathf.RoundToInt(Theme.PanelPadding);
            layout.padding = new RectOffset(pad, pad, pad, pad);
            layout.spacing = Theme.PanelSpacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        private static void SetLayout(GameObject go, float preferredWidth = -1f, float preferredHeight = -1f, float minHeight = -1f, float flexibleWidth = -1f)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            if (preferredWidth >= 0f) element.preferredWidth = preferredWidth;
            if (preferredHeight >= 0f) element.preferredHeight = preferredHeight;
            if (minHeight >= 0f) element.minHeight = minHeight;
            if (flexibleWidth >= 0f) element.flexibleWidth = flexibleWidth;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, string content, float size, Color color, TextAlignmentOptions alignment, FontStyles style)
        {
            RectTransform rt = NewRect(name, parent);
            var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.richText = true;
            text.raycastTarget = false;
            return text;
        }

        private static TextMeshProUGUI NewReadout(string name, Transform parent, float width, Color color, FontStyles style)
        {
            TextMeshProUGUI text = NewText(name, parent, name, Theme.FontReadout, color, TextAlignmentOptions.MidlineLeft, style);
            SetLayout(text.gameObject, preferredWidth: width);
            return text;
        }

        private static void NewHeader(string name, Transform parent, string content)
        {
            NewText(name, parent, content, Theme.FontHeader, Theme.Accent, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        }

        private static void NewDivider(Transform parent)
        {
            RectTransform rt = NewRect("Divider", parent);
            AddImage(rt.gameObject, Theme.Divider, null, false);
            SetLayout(rt.gameObject, preferredHeight: 2f, minHeight: 2f);
        }

        private static Slider NewSliderBlock(Transform parent, string label, float min, float max, out TextMeshProUGUI valueText)
        {
            RectTransform block = NewRect(label + " Block", parent);
            var layout = block.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            RectTransform row = NewRect("Label Row", block);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            TextMeshProUGUI name = NewText("Label", row, label, Theme.FontBody, Theme.Text, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            SetLayout(name.gameObject, flexibleWidth: 1f);
            valueText = NewText("Value", row, "", Theme.FontBody, Theme.Accent, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            SetLayout(valueText.gameObject, preferredWidth: 110f);

            var resources = new DefaultControls.Resources { standard = panelSprite, background = backgroundSprite, knob = knobSprite };
            GameObject sliderObject = DefaultControls.CreateSlider(resources);
            sliderObject.name = label + " Slider";
            sliderObject.transform.SetParent(block, false);
            SetLayerRecursive(sliderObject, UiLayer);
            SetLayout(sliderObject, preferredHeight: 26f, minHeight: 26f);

            Tint(sliderObject.transform, "Background", Theme.SliderTrack);
            Tint(sliderObject.transform, "Fill Area/Fill", Theme.SliderFill);
            Tint(sliderObject.transform, "Handle Slide Area/Handle", Theme.SliderHandle);

            var slider = sliderObject.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.SetValueWithoutNotify(Mathf.Clamp(0f, min, max));
            return slider;
        }

        private static void Tint(Transform root, string path, Color color)
        {
            Transform t = root.Find(path);
            if (t == null) return;
            var image = t.GetComponent<Image>();
            if (image != null) image.color = color;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        /// <summary>Toggle that looks like a button; a coloured "Selected" image shows when it is on.</summary>
        private static Toggle NewToggle(string name, Transform parent, string label, ToggleGroup group)
        {
            RectTransform rt = NewRect(name, parent);
            Image background = AddImage(rt.gameObject, Theme.ToggleOff, panelSprite, true);

            RectTransform selected = NewRect("Selected", rt);
            Stretch(selected);
            Image selectedImage = AddImage(selected.gameObject, Theme.ToggleOn, panelSprite, false);

            TextMeshProUGUI text = NewText("Label", rt, label, Theme.FontBody, Theme.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(text.rectTransform);

            var toggle = rt.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = selectedImage;
            toggle.group = group;
            toggle.colors = ButtonColors();
            toggle.SetIsOnWithoutNotify(false);

            SetLayout(rt.gameObject, minHeight: 44f, preferredHeight: 44f, flexibleWidth: 1f);
            return toggle;
        }

        private static Button NewButton(string name, Transform parent, string label)
        {
            RectTransform rt = NewRect(name, parent);
            Image background = AddImage(rt.gameObject, Theme.Button, panelSprite, true);

            TextMeshProUGUI text = NewText("Label", rt, label, Theme.FontBody, Theme.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(text.rectTransform);

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.colors = ButtonColors();

            SetLayout(rt.gameObject, minHeight: 44f, preferredHeight: 44f);
            return button;
        }

        private static ColorBlock ButtonColors()
        {
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.78f, 0.84f, 0.78f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            return colors;
        }
    }
}
