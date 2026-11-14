using System;
using BioDome.Plants;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BioDome.CameraSystem
{
    /// <summary>
    /// Orbit / pan / zoom camera for inspecting the greenhouse and the research garden, plus click-picking of plants.
    /// Uses the new Input System directly (the project has the legacy Input Manager disabled). Works in the Game view.
    ///   Mouse:    right drag = orbit, middle drag = pan, scroll = zoom, left click = pick a plant, double-click the ground = fly there.
    ///   Trackpad: Option + left drag = orbit, Shift + left drag = pan, two-finger scroll = zoom.
    ///   Keyboard: W A S D = pan (Shift = faster), Q / E = rotate, R = reset to the overview.
    /// The pivot is kept inside a bounding box (the target can never be lost), the camera never goes below the ground, and at close range it is
    /// pushed in front of solid props (fountain, benches, rocks) instead of passing through them. It only raises <see cref="PlantPicked"/>;
    /// what happens next is decided by the GreenhouseManager.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField, Tooltip("Camera used for picking. Defaults to the Camera on this object.")]
        private Camera targetCamera;

        [Header("Overview (Reset View)")]
        [SerializeField] private Vector3 defaultPivot = new Vector3(-16f, 0.5f, 0f);
        [SerializeField, Min(0.1f)] private float defaultDistance = 56f;
        [SerializeField] private float defaultYaw = 15f;
        [SerializeField] private float defaultPitch = 38f;

        [Header("Limits")]
        [SerializeField, Min(0.1f)] private float minDistance = 0.8f;
        [SerializeField, Min(0.1f)] private float maxDistance = 90f;
        [SerializeField] private float minPitch = 4f;
        [SerializeField] private float maxPitch = 88f;
        [SerializeField, Tooltip("The pivot is clamped to this box (world space), so panning can never lose the scene.")]
        private Vector3 pivotMin = new Vector3(-75f, 0f, -60f);
        [SerializeField] private Vector3 pivotMax = new Vector3(40f, 14f, 60f);
        [SerializeField, Min(0f), Tooltip("The camera never goes lower than this above the ground.")]
        private float groundClearance = 0.45f;

        [Header("Building confinement (keeps the experience indoors)")]
        [SerializeField, Tooltip("When on, the camera itself is kept inside the glass building: between the end walls, inside the side walls and under the roof slope.")]
        private bool confineToBuilding;
        [SerializeField] private Vector2 buildingX = new Vector2(-19.4f, 47.4f);
        [SerializeField, Min(1f), Tooltip("Half the building width; the camera stays this far (minus a margin) from the centre line.")]
        private float buildingHalfWidth = 12.5f;
        [SerializeField] private float eaveHeight = 7f;
        [SerializeField] private float ridgeHeight = 12f;
        [SerializeField, Min(0f)] private float wallMargin = 0.35f;

        [Header("Collision")]
        [SerializeField, Min(0f)] private float collisionRadius = 0.3f;
        [SerializeField, Min(0f), Tooltip("Collision is only checked up to this camera distance. Beyond it the camera is a distant overview and may look through the roof frame.")]
        private float collisionMaxDistance = 16f;
        [SerializeField, Tooltip("Colliders with these names never block the camera (the building shell and the outside ground).")]
        private string[] ignoreColliderNames = { "Frame", "Ground", "Glass", "Plinth", "Entrance" };

        [Header("Sensitivity")]
        [SerializeField] private float orbitSpeed = 0.2f;
        [SerializeField] private float panSpeed = 0.0015f;
        [SerializeField, Tooltip("Zoom per scroll notch, as a fraction of current distance.")]
        private float zoomSpeed = 0.1f;
        [SerializeField, Tooltip("Higher = snappier. Lower = floatier.")]
        private float smoothing = 10f;
        [SerializeField, Tooltip("Keyboard travel speed in metres per second per metre of camera distance.")]
        private float keyPanSpeed = 0.5f;
        [SerializeField, Min(1f), Tooltip("Slowest keyboard travel speed (m/s), so crossing the 68 m greenhouse never takes long even when zoomed in. Shift triples it.")]
        private float keyMinSpeed = 7f;
        [SerializeField] private float keyRotateSpeed = 70f;

        [Header("Picking")]
        [SerializeField] private LayerMask pickMask = ~0;
        [SerializeField] private float maxPickDistance = 120f;
        [SerializeField, Tooltip("Pixels the mouse may move between press and release and still count as a click.")]
        private float clickMovementTolerance = 5f;
        [SerializeField, Min(0.1f)] private float doubleClickSeconds = 0.35f;

        private Vector3 pivot;
        private float yaw;
        private float pitch;
        private float distance;

        private Vector3 targetPivot;
        private float targetYaw;
        private float targetPitch;
        private float targetDistance;

        private float collisionDistance = float.MaxValue;
        private readonly RaycastHit[] hitBuffer = new RaycastHit[24];

        private Transform followTarget;
        private bool pressStartedOverUi;
        private Vector2 leftPressPosition;
        private float lastClickTime = -10f;

        private enum LeftDragMode { None, Orbit, Pan }
        private LeftDragMode leftDragMode;
        private bool leftPressHadModifier;
        private bool leftPanning;

        /// <summary>Raised when the user left-clicks a plant.</summary>
        public event Action<PlantController> PlantPicked;

        /// <summary>
        /// Raised when the user left-clicks (without dragging, and not on UI) and no plant is under the cursor.
        /// That includes clicking other scenery. Used to deselect the current plant.
        /// </summary>
        public event Action EmptySpaceClicked;

        public Vector3 Pivot => pivot;
        public float Distance => distance;
        public float Yaw => yaw;
        public float Pitch => pitch;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = GetComponent<Camera>();
            if (targetCamera == null)
            {
                Debug.LogWarning($"{nameof(CameraController)} on '{name}' has no Camera. Orbiting works but plant picking is disabled.", this);
            }
            else if (targetCamera.nearClipPlane > 0.1f)
            {
                targetCamera.nearClipPlane = 0.1f; // close inspection of plants and pods
            }

            ResetView(true);
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                HandleOrbitPanZoom(mouse);
                HandlePicking(mouse);
            }
            HandleKeyboard();
        }

        private void LateUpdate()
        {
            if (followTarget != null) targetPivot = followTarget.position;
            targetPivot = ClampPivot(targetPivot);

            float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            pivot = Vector3.Lerp(pivot, targetPivot, t);
            yaw = Mathf.LerpAngle(yaw, targetYaw, t);
            pitch = Mathf.Lerp(pitch, targetPitch, t);
            distance = Mathf.Lerp(distance, targetDistance, t);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 backward = -(rotation * Vector3.forward);

            // Push the camera in front of solid props at close range (smoothly, so it never pops).
            float allowed = distance <= collisionMaxDistance ? ClearDistance(pivot, backward, distance) : distance;
            float k = allowed < collisionDistance ? 1f : 1f - Mathf.Exp(-3f * Time.deltaTime);
            collisionDistance = float.IsPositiveInfinity(collisionDistance) || collisionDistance > 1e6f ? allowed : Mathf.Lerp(collisionDistance, allowed, k);
            float used = Mathf.Min(distance, collisionDistance);

            Vector3 position = pivot + backward * used;
            if (position.y < groundClearance) position.y = groundClearance; // never under the floor
            if (confineToBuilding) position = ConfineToBuilding(position);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(pivot - position, Vector3.up));
        }

        // --- Public API ----------------------------------------------------------

        /// <summary>Smoothly frames a transform. Pass follow = true to keep tracking it.</summary>
        public void FocusOn(Transform target, float focusDistance = 3f, bool follow = false)
        {
            if (target == null) return;

            followTarget = follow ? target : null;
            targetPivot = ClampPivot(target.position);
            targetDistance = Mathf.Clamp(focusDistance, minDistance, maxDistance);
        }

        public void FocusOnPoint(Vector3 point, float focusDistance)
        {
            followTarget = null;
            targetPivot = ClampPivot(point);
            targetDistance = Mathf.Clamp(focusDistance, minDistance, maxDistance);
        }

        /// <summary>Smoothly flies to a viewpoint: where to look, how far away, and the compass direction and tilt to look from.</summary>
        public void FlyTo(Vector3 point, float focusDistance, float newYaw, float newPitch)
        {
            FocusOnPoint(point, focusDistance);
            targetYaw = newYaw;
            targetPitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
        }

        /// <summary>Jumps to a viewpoint instantly (no smoothing).</summary>
        public void SnapTo(Vector3 point, float focusDistance, float newYaw, float newPitch)
        {
            FlyTo(point, focusDistance, newYaw, newPitch);
            pivot = targetPivot;
            yaw = targetYaw;
            pitch = targetPitch;
            distance = targetDistance;
            collisionDistance = float.MaxValue;
        }

        /// <summary>Returns to the overview. Pass instant = true to skip smoothing.</summary>
        public void ResetView(bool instant = false)
        {
            followTarget = null;
            targetPivot = ClampPivot(defaultPivot);
            targetYaw = defaultYaw;
            targetPitch = Mathf.Clamp(defaultPitch, minPitch, maxPitch);
            targetDistance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);

            if (instant)
            {
                pivot = targetPivot;
                yaw = targetYaw;
                pitch = targetPitch;
                distance = targetDistance;
                collisionDistance = float.MaxValue;
            }
        }

        // --- Input ---------------------------------------------------------------

        private void HandleOrbitPanZoom(Mouse mouse)
        {
            if (mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame || mouse.leftButton.wasPressedThisFrame)
            {
                pressStartedOverUi = IsPointerOverUi();
            }

            // Trackpad-friendly alternative to right/middle drag. The mode is decided once, when the left button
            // goes down, so releasing or pressing a modifier mid-drag cannot switch modes.
            if (mouse.leftButton.wasPressedThisFrame)
            {
                leftPressPosition = mouse.position.ReadValue();
                leftDragMode = pressStartedOverUi ? LeftDragMode.None : ModifierDragMode();
                leftPressHadModifier = leftDragMode != LeftDragMode.None;
            }
            if (!mouse.leftButton.isPressed) leftDragMode = LeftDragMode.None;

            Vector2 delta = mouse.delta.ReadValue();

            // A plain left-button drag pans (like a map). It only starts after the pointer has moved past the click tolerance,
            // so a normal click still picks a plant, and it never starts over UI.
            bool plainLeftDrag = false;
            if (mouse.leftButton.isPressed && leftDragMode == LeftDragMode.None && !pressStartedOverUi && !leftPressHadModifier)
            {
                Vector2 now = mouse.position.ReadValue();
                if (leftPanning || (now - leftPressPosition).sqrMagnitude > clickMovementTolerance * clickMovementTolerance) { leftPanning = true; plainLeftDrag = true; }
            }
            if (!mouse.leftButton.isPressed) leftPanning = false;

            if ((mouse.rightButton.isPressed && !pressStartedOverUi) || leftDragMode == LeftDragMode.Orbit)
            {
                targetYaw += delta.x * orbitSpeed;
                targetPitch = Mathf.Clamp(targetPitch - delta.y * orbitSpeed, minPitch, maxPitch);
            }

            if ((mouse.middleButton.isPressed && !pressStartedOverUi) || leftDragMode == LeftDragMode.Pan || plainLeftDrag)
            {
                followTarget = null; // manual pan cancels following
                float scale = panSpeed * Mathf.Max(targetDistance, 2f);
                // Pan along the ground plane under the current view, so it feels the same at any tilt.
                Vector3 right = new Vector3(transform.right.x, 0f, transform.right.z).normalized;
                Vector3 forward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
                targetPivot -= (right * delta.x + forward * delta.y) * scale;
                targetPivot = ClampPivot(targetPivot);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !IsPointerOverUi())
            {
                // Windows reports +-120 per notch; normalise to roughly +-1.
                float notches = scroll / 120f;
                targetDistance = Mathf.Clamp(targetDistance * (1f - notches * zoomSpeed), minDistance, maxDistance);
            }
        }

        private void HandleKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.rKey.wasPressedThisFrame) ResetView();

            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            if (x != 0f || z != 0f)
            {
                followTarget = null;
                float speed = Mathf.Max(keyMinSpeed, keyPanSpeed * targetDistance) * (keyboard.shiftKey.isPressed ? 3f : 1f);
                Quaternion heading = Quaternion.Euler(0f, targetYaw, 0f);
                targetPivot += heading * new Vector3(x, 0f, z).normalized * (speed * Time.deltaTime);
                targetPivot = ClampPivot(targetPivot);
            }

            float turn = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
            if (turn != 0f) targetYaw += turn * keyRotateSpeed * Time.deltaTime;
        }

        private void HandlePicking(Mouse mouse)
        {
            if (targetCamera == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                leftPressPosition = mouse.position.ReadValue();
            }

            // A modifier + left press (Option/Shift) is a camera drag, never a click on a plant or empty space.
            if (!mouse.leftButton.wasReleasedThisFrame || pressStartedOverUi || leftPressHadModifier) return;

            Vector2 releasePosition = mouse.position.ReadValue();
            if ((releasePosition - leftPressPosition).sqrMagnitude > clickMovementTolerance * clickMovementTolerance) return;
            if (IsPointerOverUi()) return; // released over a UI element: not a world click

            PlantController plant = null;
            bool hitGround = false;
            Vector3 groundPoint = default;
            Ray ray = targetCamera.ScreenPointToRay(releasePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, maxPickDistance, pickMask, QueryTriggerInteraction.Ignore))
            {
                plant = hit.collider.GetComponentInParent<PlantController>();
                if (plant == null && hit.point.y < 6f && !IsIgnored(hit.collider))
                {
                    hitGround = true;
                    groundPoint = hit.point;
                }
                else if (plant == null && hit.collider.name == "Ground")
                {
                    hitGround = true;
                    groundPoint = hit.point;
                }
            }

            bool doubleClick = Time.unscaledTime - lastClickTime <= doubleClickSeconds;
            lastClickTime = Time.unscaledTime;

            if (plant != null) PlantPicked?.Invoke(plant);
            else
            {
                if (doubleClick && hitGround) FocusOnPoint(groundPoint, Mathf.Clamp(targetDistance * 0.55f, 4f, 30f));
                EmptySpaceClicked?.Invoke();
            }
        }

        // --- Helpers ---------------------------------------------------------------

        private Vector3 ClampPivot(Vector3 p)
        {
            return new Vector3(
                Mathf.Clamp(p.x, pivotMin.x, pivotMax.x),
                Mathf.Clamp(p.y, pivotMin.y, pivotMax.y),
                Mathf.Clamp(p.z, pivotMin.z, pivotMax.z));
        }

        /// <summary>Clamps a camera position inside the building: x between the end walls, z inside the side walls, y under the roof slope.</summary>
        private Vector3 ConfineToBuilding(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, buildingX.x, buildingX.y);
            float zLimit = buildingHalfWidth - wallMargin;
            p.z = Mathf.Clamp(p.z, -zLimit, zLimit);
            float roofHere = eaveHeight + (ridgeHeight - eaveHeight) * (1f - Mathf.Min(1f, Mathf.Abs(p.z) / buildingHalfWidth));
            p.y = Mathf.Clamp(p.y, groundClearance, Mathf.Max(groundClearance, roofHere - wallMargin));
            return p;
        }

        private bool IsIgnored(Collider c)
        {
            for (int i = 0; i < ignoreColliderNames.Length; i++)
                if (c.name == ignoreColliderNames[i]) return true;
            return false;
        }

        /// <summary>How far back from <paramref name="from"/> the camera may go before it would touch a solid prop.</summary>
        private float ClearDistance(Vector3 from, Vector3 backward, float wanted)
        {
            int count = Physics.SphereCastNonAlloc(from, collisionRadius, backward, hitBuffer, wanted, ~0, QueryTriggerInteraction.Ignore);
            float best = wanted;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = hitBuffer[i];
                if (h.collider == null || h.distance <= 0f) continue; // started inside this collider: ignore it
                if (IsIgnored(h.collider)) continue;
                if (h.collider.GetComponentInParent<PlantController>() != null) continue; // foliage never blocks the camera
                if (h.distance < best) best = h.distance;
            }
            return Mathf.Max(best - 0.05f, Mathf.Min(1.0f, wanted));
        }

        private static LeftDragMode ModifierDragMode()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return LeftDragMode.None;
            if (keyboard.altKey.isPressed) return LeftDragMode.Orbit;
            if (keyboard.shiftKey.isPressed) return LeftDragMode.Pan;
            return LeftDragMode.None;
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private void OnValidate()
        {
            minDistance = Mathf.Max(0.1f, minDistance);
            maxDistance = Mathf.Max(minDistance, maxDistance);
            maxPitch = Mathf.Max(minPitch, maxPitch);
            defaultDistance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);
            pivotMax = Vector3.Max(pivotMax, pivotMin);
        }
    }
}
