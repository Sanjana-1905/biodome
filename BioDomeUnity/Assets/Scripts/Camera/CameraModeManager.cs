using BioDome.CameraSystem;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BioDome.CameraSystem
{
    /// <summary>Overview / TopDown / FirstPerson are the three modes; Free = the user moved on with a viewpoint shortcut.</summary>
    public enum CameraMode { Overview = 0, TopDown = 1, FirstPerson = 2, Free = 3 }

    /// <summary>
    /// Three clearly separated camera modes on top of the orbit <see cref="CameraController"/>:
    ///   Overview    - the whole greenhouse framed from the south, 40 degrees up. Pan / orbit / zoom stay free.      Key 1
    ///   Top-down    - the whole footprint seen from straight above. Pan and zoom to any bed or pod.                 Key 2
    ///   First person- WASD with smooth acceleration, mouse look, Shift = run, collides with walls and props.        Key 3
    /// Adds itself to the camera at runtime (no scene change). In the orbit modes every existing control (trackpad, plant picking,
    /// viewpoint shortcuts) works; navigation is no longer confined to the building or to one pivot box.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraModeManager : MonoBehaviour
    {
        [Header("Greenhouse footprint (world)")]
        [SerializeField] private Vector3 footprintCentre = new Vector3(14f, 0.5f, 0f);   // building spans x -19.4 .. 47.4, z +-12.5, ridge 12
        [SerializeField] private Vector3 footprintHalfSize = new Vector3(33.4f, 12f, 12.5f);

        [Header("Orbit modes")]
        [SerializeField] private float overviewPitch = 40f;
        [SerializeField] private float topDownPitch = 89f;
        [SerializeField, Range(0f, 0.3f), Tooltip("Free space around the footprint on screen.")] private float framingMargin = 0.08f;

        [Header("First person")]
        [SerializeField] private float eyeHeight = 1.65f;
        [SerializeField] private float walkSpeed = 3.5f;
        [SerializeField] private float runSpeed = 6.5f;
        [SerializeField] private float acceleration = 12f;
        [SerializeField] private float deceleration = 16f;
        [SerializeField] private float lookSensitivity = 0.10f;
        [SerializeField, Tooltip("Higher = more responsive, lower = smoother turning.")] private float lookResponsiveness = 40f;
        [SerializeField] private float firstPersonFov = 70f;
        [SerializeField] private float enterSeconds = 0.7f;
        [SerializeField] private float bodyRadius = 0.3f;
        [SerializeField] private float bodyHeight = 1.7f;
        [SerializeField] private float wallMargin = 0.6f;

        private CameraController orbit;
        private Camera cam;
        private CameraMode mode = CameraMode.Overview;
        private bool selfCall;
        public CameraMode Mode => mode;
        public float LookSensitivity => lookSensitivity;

        // first person
        private GameObject rig;
        private CharacterController body;
        private float fpYaw, fpPitch, verticalVelocity, savedFov, enterT = 1f;
        private Vector3 horizontalVelocity;
        private Vector2 smoothedLook;
        private bool captured; // our own "mouse captured" state (the OS cursor lock can be refused when the Game view is not focused)
        private Vector3 enterPos;
        private Quaternion enterRot;

        // ui
        private readonly Image[] buttons = new Image[3];
        private TextMeshProUGUI hintText;
        private Image panelsButtonImage;
        private TextMeshProUGUI panelsLabel;

        // HUD panels (UI only: they are hidden, never destroyed or changed)
        private GameObject controlPanelObject;
        private BioDome.Experiment.ExperimentComparisonPanel experimentPanel;
        private bool panelsHidden, panelsHiddenBeforeFirstPerson;
        private float lastAspect;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            var controller = Object.FindAnyObjectByType<CameraController>();
            if (controller != null && controller.GetComponent<CameraModeManager>() == null)
                controller.gameObject.AddComponent<CameraModeManager>();
        }

        private void Start()
        {
            orbit = GetComponent<CameraController>();
            cam = GetComponent<Camera>();
            if (orbit == null || cam == null) { enabled = false; return; }

            orbit.RaiseLimits(89f, 140f);
            orbit.SetFreeNavigation(new Vector3(-40f, 0f, -35f), new Vector3(60f, 16f, 35f), false); // the ground plane is 120 x 105 m
            orbit.ViewRequested += OnViewRequested;
            orbit.ResetRequested += OnResetRequested;
            savedFov = cam.fieldOfView;
            BuildUi();
            SetMode(CameraMode.Overview, true);
        }

        private void OnDestroy()
        {
            if (orbit != null) { orbit.ViewRequested -= OnViewRequested; orbit.ResetRequested -= OnResetRequested; }
        }

        private void OnDisable()
        {
            if (mode == CameraMode.FirstPerson) ReleaseCursor();
        }

        // --- Mode switching ----------------------------------------------------------

        public void SetMode(CameraMode newMode, bool instant = false)
        {
            if (orbit == null || newMode == CameraMode.Free) return;
            CameraMode previous = mode;
            if (previous == CameraMode.FirstPerson && newMode != CameraMode.FirstPerson) LeaveFirstPerson();

            mode = newMode;
            selfCall = true;
            switch (newMode)
            {
                case CameraMode.Overview:
                    ConfigureOverviewPose();
                    Go(overviewPitch, instant);
                    break;
                case CameraMode.TopDown:
                    Go(topDownPitch, instant);
                    break;
                case CameraMode.FirstPerson:
                    if (previous != CameraMode.FirstPerson) EnterFirstPerson();
                    break;
            }
            selfCall = false;
            RefreshUi();
        }

        private void Go(float pitch, bool instant)
        {
            float d = FitDistance(pitch);
            if (!instant) orbit.SetTransitionSmoothing(2.4f, 2.4f);
            if (instant) orbit.SnapTo(footprintCentre, d, 0f, pitch);
            else orbit.FlyTo(footprintCentre, d, 0f, pitch);
        }

        // Called when something else (viewpoint shortcut, plant pick, Reset View) moves the orbit camera.
        private void OnViewRequested()
        {
            if (selfCall) return;
            if (mode == CameraMode.FirstPerson) LeaveFirstPerson();
            mode = CameraMode.Free;
            RefreshUi();
        }

        private void OnResetRequested()
        {
            if (selfCall) return;
            mode = CameraMode.Overview;
            RefreshUi();
        }

        /// <summary>Smallest camera distance at which the whole building box (every corner) fits the screen, so perspective cannot crop the near wall.</summary>
        private float FitDistance(float pitch)
        {
            Quaternion rot = Quaternion.Euler(pitch, 0f, 0f);
            Quaternion inv = Quaternion.Inverse(rot);
            Vector3 fwd = rot * Vector3.forward;
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * (1f - framingMargin);
            float tanH = tanV * Mathf.Max(0.3f, cam.aspect);
            float lo = 5f, hi = 400f;
            for (int iter = 0; iter < 30; iter++)
            {
                float mid = 0.5f * (lo + hi);
                if (Fits(mid, fwd, inv, tanH, tanV)) hi = mid; else lo = mid;
            }
            return hi;
        }

        private bool Fits(float d, Vector3 fwd, Quaternion inv, float tanH, float tanV)
        {
            Vector3 camPos = footprintCentre - fwd * d;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = footprintCentre + new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -0.04f : 1f, (i & 4) == 0 ? -1f : 1f).Multiply(footprintHalfSize);
                Vector3 v = inv * (corner - camPos);
                if (v.z < 0.2f) return false;
                if (Mathf.Abs(v.x) / v.z > tanH || Mathf.Abs(v.y) / v.z > tanV) return false;
            }
            return true;
        }

        private void ConfigureOverviewPose()
        {
            orbit.ConfigureOverview(footprintCentre, FitDistance(overviewPitch), 0f, overviewPitch);
            lastAspect = cam.aspect;
        }

        // --- Update --------------------------------------------------------------------

        private void Update()
        {
            if (Mathf.Abs(cam.aspect - lastAspect) > 0.01f) ConfigureOverviewPose(); // window resized: Reset View keeps framing the whole building

            Keyboard kb = Keyboard.current;
            if (kb != null && !IsTextInputFocused() && !kb.ctrlKey.isPressed && !kb.leftCommandKey.isPressed && !kb.rightCommandKey.isPressed)
            {
                if (kb.digit1Key.wasPressedThisFrame) SetMode(CameraMode.Overview);
                else if (kb.digit2Key.wasPressedThisFrame) SetMode(CameraMode.TopDown);
                else if (kb.digit3Key.wasPressedThisFrame) SetMode(CameraMode.FirstPerson);
                else if (kb.hKey.wasPressedThisFrame) SetPanelsHidden(!panelsHidden);
                else if (kb.rKey.wasPressedThisFrame && mode == CameraMode.FirstPerson) SetMode(CameraMode.Overview);
                else if (mode == CameraMode.FirstPerson)
                {
                    if (kb.leftBracketKey.wasPressedThisFrame) { lookSensitivity = Mathf.Max(0.03f, lookSensitivity / 1.2f); UpdateHint(); }
                    if (kb.rightBracketKey.wasPressedThisFrame) { lookSensitivity = Mathf.Min(0.5f, lookSensitivity * 1.2f); UpdateHint(); }
                }
            }

            if (mode == CameraMode.FirstPerson) UpdateFirstPerson();
        }

        // --- First person ---------------------------------------------------------------

        private void EnsureRig()
        {
            if (rig != null) return;
            rig = new GameObject("FirstPersonBody");
            body = rig.AddComponent<CharacterController>();
            body.height = bodyHeight;
            body.radius = bodyRadius;
            body.center = new Vector3(0f, bodyHeight * 0.5f, 0f);
            body.skinWidth = 0.03f;
            body.stepOffset = 0.25f;
            body.slopeLimit = 45f;
            body.minMoveDistance = 0f;
        }

        private void EnterFirstPerson()
        {
            EnsureRig();
            rig.SetActive(true);
            // Close-up views: appear where you were looking. From the far overview / top-down: start at the entrance, not in the middle of the fountain.
            Vector3 spawn = FindSpawn(orbit.Distance < 30f ? orbit.Pivot : new Vector3(-13f, 0f, 0f));
            enterPos = cam.transform.position;
            enterRot = cam.transform.rotation;
            orbit.enabled = false;

            body.enabled = false;
            rig.transform.position = spawn;
            body.enabled = true;
            IgnorePlantColliders();

            fpYaw = orbit.Yaw;
            fpPitch = 5f;
            verticalVelocity = 0f;
            horizontalVelocity = Vector3.zero;
            smoothedLook = Vector2.zero;
            savedFov = cam.fieldOfView;
            HidePanelsForFirstPerson();
            enterT = 0f; // glide from the orbit camera to the player's eyes instead of snapping
            LockCursor();
        }

        /// <summary>
        /// Plant colliders are one big box per plant that grows with the canopy (a mature banana is 3.5 x 2.2 x 3.5 m) and they change size while the
        /// simulation runs, so they pinch aisles and can grow over a standing player. Foliage is therefore walk-through; walls, beds, benches, stones and
        /// the fountain stay solid.
        /// </summary>
        private void IgnorePlantColliders()
        {
            foreach (BioDome.Plants.PlantController plant in Object.FindObjectsByType<BioDome.Plants.PlantController>(FindObjectsInactive.Include))
            {
                foreach (Collider c in plant.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(body, c, true);
            }
        }

        private void LeaveFirstPerson()
        {
            ReleaseCursor();
            enterT = 1f;
            cam.fieldOfView = savedFov;
            if (rig != null) rig.SetActive(false); // its collider must not block the orbit camera
            orbit.enabled = true;
            orbit.BeginFrom(cam.transform.position, cam.transform.rotation); // the next FlyTo starts from where the player stood
            RestorePanels();
        }

        private void UpdateFirstPerson()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            if (kb != null && kb.escapeKey.wasPressedThisFrame) ReleaseCursor();
            // The editor / OS can also take the lock away (Esc in the editor, focus loss): treat that as released.
            if (captured && Application.isFocused && Cursor.lockState == CursorLockMode.None && enterT >= 1f && Cursor.visible) captured = false;
            if (!captured && mouse != null && mouse.leftButton.wasPressedThisFrame && !IsPointerOverUi()) LockCursor();
            bool locked = captured;

            Vector2 rawLook = Vector2.zero;
            if (locked && mouse != null) rawLook = Vector2.ClampMagnitude(mouse.delta.ReadValue(), 200f);

            if (enterT < 1f)
            {
                enterT = Mathf.Min(1f, enterT + dt / Mathf.Max(0.05f, enterSeconds));
                float k = enterT * enterT * (3f - 2f * enterT);
                Quaternion targetRot = Quaternion.Euler(fpPitch, fpYaw, 0f);
                cam.transform.SetPositionAndRotation(Vector3.Lerp(enterPos, rig.transform.position + Vector3.up * eyeHeight, k), Quaternion.Slerp(enterRot, targetRot, k));
                cam.fieldOfView = Mathf.Lerp(savedFov, firstPersonFov, k);
                return;
            }

            // Look: exponentially smoothed deltas (the total turn is preserved, only spread over a few frames).
            smoothedLook = Vector2.Lerp(smoothedLook, rawLook, 1f - Mathf.Exp(-lookResponsiveness * dt));
            fpYaw += smoothedLook.x * lookSensitivity;
            fpPitch = Mathf.Clamp(fpPitch - smoothedLook.y * lookSensitivity, -85f, 85f);

            // Move: input sets a target velocity; the body accelerates and brakes towards it.
            Vector3 targetVelocity = Vector3.zero;
            if (locked && kb != null && !IsTextInputFocused())
            {
                float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                float speed = kb.shiftKey.isPressed ? runSpeed : walkSpeed;
                targetVelocity = Quaternion.Euler(0f, fpYaw, 0f) * new Vector3(x, 0f, z).normalized * speed;
            }
            float rate = targetVelocity.sqrMagnitude > 0.0001f ? acceleration : deceleration;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * dt);

            verticalVelocity = body.isGrounded ? -1f : Mathf.Max(verticalVelocity - 20f * dt, -30f);
            body.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);

            // Sliding along an obstacle: do not keep "pushing" velocity the body could not realise.
            Vector3 actual = new Vector3(body.velocity.x, 0f, body.velocity.z);
            if (actual.sqrMagnitude < horizontalVelocity.sqrMagnitude) horizontalVelocity = Vector3.Lerp(horizontalVelocity, actual, 0.5f);

            // Keep inside the glass shell (works even where a wall has no collider) and never below the floor.
            Vector3 p = rig.transform.position;
            Vector2 bx = orbit.BuildingX;
            float zLimit = orbit.BuildingHalfWidth - wallMargin;
            Vector3 clamped = new Vector3(Mathf.Clamp(p.x, bx.x + wallMargin, bx.y - wallMargin), Mathf.Max(p.y, 0f), Mathf.Clamp(p.z, -zLimit, zLimit));
            if ((clamped - p).sqrMagnitude > 1e-8f) body.Move(clamped - p);

            cam.transform.SetPositionAndRotation(rig.transform.position + Vector3.up * eyeHeight, Quaternion.Euler(fpPitch, fpYaw, 0f));
        }

        private bool IsFree(Vector3 p)
        {
            Vector3 bottom = p + Vector3.up * (bodyRadius + 0.1f);
            Vector3 top = p + Vector3.up * (bodyHeight - bodyRadius);
            foreach (Collider c in Physics.OverlapCapsule(bottom, top, bodyRadius + 0.15f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.GetComponentInParent<BioDome.Plants.PlantController>() == null) return false; // plants are walk-through, everything else is not
            }
            return true;
        }

        /// <summary>Where the player appears: the ground point being looked at if it is inside the building and free, else the nearest free spot, else near the entrance.</summary>
        private Vector3 FindSpawn(Vector3 wanted)
        {
            Vector2 bx = orbit.BuildingX;
            float zLimit = orbit.BuildingHalfWidth - wallMargin - 0.5f;
            Vector3 centre = new Vector3(Mathf.Clamp(wanted.x, bx.x + 1.5f, bx.y - 1.5f), 0.02f, Mathf.Clamp(wanted.z, -zLimit, zLimit));
            float[] radii = { 0f, 1.5f, 3f, 5f, 8f, 12f };
            foreach (float r in radii)
            {
                int n = r == 0f ? 1 : 16;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    Vector3 c = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    c.x = Mathf.Clamp(c.x, bx.x + 1.5f, bx.y - 1.5f);
                    c.z = Mathf.Clamp(c.z, -zLimit, zLimit);
                    if (IsFree(c)) return c;
                }
            }
            Vector3[] entrance = { new Vector3(-14f, 0.02f, 0f), new Vector3(-10f, 0.02f, 0f), new Vector3(-12f, 0.02f, 4f), new Vector3(-12f, 0.02f, -4f) };
            foreach (Vector3 e in entrance) if (IsFree(e)) return e;
            return centre;
        }

        private void LockCursor() { captured = true; Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        private void ReleaseCursor() { captured = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

        // --- UI --------------------------------------------------------------------------

        private void BuildUi()
        {
            var canvasObject = new GameObject("CameraModeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            // Same reference as the main HUD (1920x1080, match 0.5), so it scales with the top bar it sits under.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            string[] labels = { "Overview  [1]", "Top-down  [2]", "First person  [3]" };
            // Narrow enough (<= 456 at the 1920 reference) to stay clear of the Pod Lab button at any aspect ratio.
            const float w = 116f, pw = 86f, h = 36f, gap = 4f, pad = 5f;
            float width = pad * 2f + w * 3f + pw + gap * 3f;

            var panel = NewImage("ModeBar", canvasObject.transform, new Color(0.07f, 0.10f, 0.09f, 0.88f));
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -74f); // just below the 64-high top bar
            panel.sizeDelta = new Vector2(width, h + pad * 2f);

            for (int i = 0; i < 3; i++)
            {
                var rect = NewImage("Mode_" + i, panel, new Color(1f, 1f, 1f, 0.10f));
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(pad + i * (w + gap), -pad);
                rect.sizeDelta = new Vector2(w, h);
                buttons[i] = rect.GetComponent<Image>();

                var textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(rect, false);
                var tmp = textObject.GetComponent<TextMeshProUGUI>();
                tmp.text = labels[i]; tmp.fontSize = 15f; tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = new Color(0.92f, 0.96f, 0.92f); tmp.raycastTarget = false; tmp.textWrappingMode = TextWrappingModes.NoWrap;
                var trt = (RectTransform)textObject.transform;
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;

                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = buttons[i];
                CameraMode target = (CameraMode)i;
                button.onClick.AddListener(() =>
                {
                    SetMode(target);
                    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); // keep Space / WASD for the camera
                });
            }

            // Panels toggle: the two big side panels can hide the greenhouse; one click (or H) clears them.
            var pr = NewImage("Panels", panel, new Color(1f, 1f, 1f, 0.10f));
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0f, 1f);
            pr.anchoredPosition = new Vector2(pad + 3f * (w + gap), -pad);
            pr.sizeDelta = new Vector2(pw, h);
            panelsButtonImage = pr.GetComponent<Image>();
            var po = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            po.transform.SetParent(pr, false);
            panelsLabel = po.GetComponent<TextMeshProUGUI>();
            panelsLabel.text = "Panels [H]"; panelsLabel.fontSize = 15f; panelsLabel.alignment = TextAlignmentOptions.Center;
            panelsLabel.color = new Color(0.92f, 0.96f, 0.92f); panelsLabel.raycastTarget = false; panelsLabel.textWrappingMode = TextWrappingModes.NoWrap;
            var prt = (RectTransform)po.transform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;
            var pb = pr.gameObject.AddComponent<Button>();
            pb.targetGraphic = panelsButtonImage;
            pb.onClick.AddListener(() =>
            {
                SetPanelsHidden(!panelsHidden);
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            });

            // Compact controls guide for the current mode (never intercepts clicks).
            var hintBg = NewImage("Guide", canvasObject.transform, new Color(0.07f, 0.10f, 0.09f, 0.70f));
            hintBg.GetComponent<Image>().raycastTarget = false;
            hintBg.anchorMin = hintBg.anchorMax = hintBg.pivot = new Vector2(0.5f, 1f);
            hintBg.anchoredPosition = new Vector2(0f, -74f - (h + pad * 2f) - 4f);
            hintBg.sizeDelta = new Vector2(820f, 26f);
            var hintObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            hintObject.transform.SetParent(hintBg, false);
            hintText = hintObject.GetComponent<TextMeshProUGUI>();
            hintText.fontSize = 14f; hintText.enableAutoSizing = true; hintText.fontSizeMin = 10f; hintText.fontSizeMax = 14f; hintText.alignment = TextAlignmentOptions.Center; hintText.textWrappingMode = TextWrappingModes.NoWrap;
            hintText.color = new Color(1f, 1f, 1f, 0.92f); hintText.raycastTarget = false;
            var hrt = (RectTransform)hintObject.transform;
            hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one; hrt.offsetMin = hrt.offsetMax = Vector2.zero;
        }

        private void UpdateHint()
        {
            if (hintText == null) return;
            hintText.text = mode == CameraMode.FirstPerson
                ? $"WASD walk · Shift run · Mouse look · [ ] sensitivity {lookSensitivity:0.00} · Esc release mouse · Click recapture · H panels"
                : "Drag pan · Right-drag / Option+drag orbit · Scroll zoom · WASD pan · Q/E rotate · R overview · H panels";
        }

        private void FindPanels()
        {
            if (controlPanelObject == null) controlPanelObject = GameObject.Find("ControlPanel");
            if (experimentPanel == null) experimentPanel = Object.FindAnyObjectByType<BioDome.Experiment.ExperimentComparisonPanel>();
        }

        public void SetPanelsHidden(bool hidden)
        {
            FindPanels();
            panelsHidden = hidden;
            if (controlPanelObject != null) controlPanelObject.SetActive(!hidden);
            if (experimentPanel != null) experimentPanel.SetVisible(!hidden); // its own visibility switch (it also hides itself while a plant is selected)
            RefreshUi();
        }

        private void HidePanelsForFirstPerson() { panelsHiddenBeforeFirstPerson = panelsHidden; SetPanelsHidden(true); }
        private void RestorePanels() { SetPanelsHidden(panelsHiddenBeforeFirstPerson); }

        private void RefreshUi()
        {
            if (panelsButtonImage != null) panelsButtonImage.color = panelsHidden ? new Color(0.35f, 0.65f, 0.40f, 0.90f) : new Color(1f, 1f, 1f, 0.10f);
            if (panelsLabel != null) panelsLabel.text = "Panels [H]";
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null) continue;
                buttons[i].color = (int)mode == i ? new Color(0.35f, 0.65f, 0.40f, 0.90f) : new Color(1f, 1f, 1f, 0.10f);
            }
            UpdateHint();
        }

        private static RectTransform NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        private static bool IsPointerOverUi() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private static bool IsTextInputFocused()
        {
            EventSystem es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            GameObject g = es.currentSelectedGameObject;
            return g.GetComponent<TMP_InputField>() != null || g.GetComponent<InputField>() != null;
        }
    }

    internal static class Vector3Ext
    {
        public static Vector3 Multiply(this Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
    }
}
