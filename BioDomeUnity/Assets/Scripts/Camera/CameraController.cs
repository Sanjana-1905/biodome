using System;
using BioDome.Plants;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BioDome.CameraSystem
{
    /// <summary>
    /// Orbit / pan / zoom camera for inspecting the greenhouse, plus click-picking of plants.
    /// Uses the new Input System directly (the project has the legacy Input Manager disabled).
    ///   Right mouse drag = orbit, Middle mouse drag = pan, Scroll = zoom, Left click = pick a plant.
    /// Attach to the camera object. It only raises <see cref="PlantPicked"/>; what happens next
    /// is decided by the GreenhouseManager.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField, Tooltip("Camera used for picking. Defaults to the Camera on this object.")]
        private Camera targetCamera;

        [Header("Start View")]
        [SerializeField] private Vector3 defaultPivot = new Vector3(0f, 1.5f, 0f);
        [SerializeField, Min(0.1f)] private float defaultDistance = 12f;
        [SerializeField] private float defaultYaw = 30f;
        [SerializeField] private float defaultPitch = 25f;

        [Header("Limits")]
        [SerializeField, Min(0.1f)] private float minDistance = 1.5f;
        [SerializeField, Min(0.1f)] private float maxDistance = 40f;
        [SerializeField] private float minPitch = -5f;
        [SerializeField] private float maxPitch = 85f;

        [Header("Sensitivity")]
        [SerializeField] private float orbitSpeed = 0.2f;
        [SerializeField] private float panSpeed = 0.01f;
        [SerializeField, Tooltip("Zoom per scroll notch, as a fraction of current distance.")]
        private float zoomSpeed = 0.1f;
        [SerializeField, Tooltip("Higher = snappier. Lower = floatier.")]
        private float smoothing = 10f;

        [Header("Picking")]
        [SerializeField] private LayerMask pickMask = ~0;
        [SerializeField] private float maxPickDistance = 100f;
        [SerializeField, Tooltip("Pixels the mouse may move between press and release and still count as a click.")]
        private float clickMovementTolerance = 5f;

        private Vector3 pivot;
        private float yaw;
        private float pitch;
        private float distance;

        private Vector3 targetPivot;
        private float targetYaw;
        private float targetPitch;
        private float targetDistance;

        private Transform followTarget;
        private bool pressStartedOverUi;
        private Vector2 leftPressPosition;

        /// <summary>Raised when the user left-clicks a plant.</summary>
        public event Action<PlantController> PlantPicked;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = GetComponent<Camera>();
            if (targetCamera == null)
            {
                Debug.LogWarning($"{nameof(CameraController)} on '{name}' has no Camera. Orbiting works but plant picking is disabled.", this);
            }

            ResetView(true);
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return; // no mouse (e.g. headset only); camera simply holds position

            HandleOrbitPanZoom(mouse);
            HandlePicking(mouse);
        }

        private void LateUpdate()
        {
            if (followTarget != null) targetPivot = followTarget.position;

            float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            pivot = Vector3.Lerp(pivot, targetPivot, t);
            yaw = Mathf.LerpAngle(yaw, targetYaw, t);
            pitch = Mathf.Lerp(pitch, targetPitch, t);
            distance = Mathf.Lerp(distance, targetDistance, t);

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
        }

        // --- Public API ----------------------------------------------------------

        /// <summary>Smoothly frames a transform. Pass follow = true to keep tracking it.</summary>
        public void FocusOn(Transform target, float focusDistance = 3f, bool follow = false)
        {
            if (target == null) return;

            followTarget = follow ? target : null;
            targetPivot = target.position;
            targetDistance = Mathf.Clamp(focusDistance, minDistance, maxDistance);
        }

        public void FocusOnPoint(Vector3 point, float focusDistance)
        {
            followTarget = null;
            targetPivot = point;
            targetDistance = Mathf.Clamp(focusDistance, minDistance, maxDistance);
        }

        /// <summary>Returns to the start view. Pass instant = true to skip smoothing.</summary>
        public void ResetView(bool instant = false)
        {
            followTarget = null;
            targetPivot = defaultPivot;
            targetYaw = defaultYaw;
            targetPitch = Mathf.Clamp(defaultPitch, minPitch, maxPitch);
            targetDistance = Mathf.Clamp(defaultDistance, minDistance, maxDistance);

            if (instant)
            {
                pivot = targetPivot;
                yaw = targetYaw;
                pitch = targetPitch;
                distance = targetDistance;
            }
        }

        // --- Input ---------------------------------------------------------------

        private void HandleOrbitPanZoom(Mouse mouse)
        {
            if (mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame || mouse.leftButton.wasPressedThisFrame)
            {
                pressStartedOverUi = IsPointerOverUi();
            }

            Vector2 delta = mouse.delta.ReadValue();

            if (mouse.rightButton.isPressed && !pressStartedOverUi)
            {
                targetYaw += delta.x * orbitSpeed;
                targetPitch = Mathf.Clamp(targetPitch - delta.y * orbitSpeed, minPitch, maxPitch);
            }

            if (mouse.middleButton.isPressed && !pressStartedOverUi)
            {
                followTarget = null; // manual pan cancels following
                float scale = panSpeed * targetDistance;
                targetPivot -= (transform.right * delta.x + transform.up * delta.y) * scale;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !IsPointerOverUi())
            {
                // Windows reports +-120 per notch; normalise to roughly +-1.
                float notches = scroll / 120f;
                targetDistance = Mathf.Clamp(targetDistance * (1f - notches * zoomSpeed), minDistance, maxDistance);
            }
        }

        private void HandlePicking(Mouse mouse)
        {
            if (targetCamera == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                leftPressPosition = mouse.position.ReadValue();
            }

            if (!mouse.leftButton.wasReleasedThisFrame || pressStartedOverUi) return;

            Vector2 releasePosition = mouse.position.ReadValue();
            if ((releasePosition - leftPressPosition).sqrMagnitude > clickMovementTolerance * clickMovementTolerance) return;

            Ray ray = targetCamera.ScreenPointToRay(releasePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, maxPickDistance, pickMask, QueryTriggerInteraction.Ignore))
            {
                PlantController plant = hit.collider.GetComponentInParent<PlantController>();
                if (plant != null) PlantPicked?.Invoke(plant);
            }
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
        }
    }
}
