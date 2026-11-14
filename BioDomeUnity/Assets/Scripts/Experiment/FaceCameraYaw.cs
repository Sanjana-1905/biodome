using UnityEngine;

namespace BioDome.Experiment
{
    /// <summary>Turns a sign around the vertical axis so it faces the camera (labels stay readable from any side).</summary>
    public class FaceCameraYaw : MonoBehaviour
    {
        private Camera cam;

        private void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;
            Vector3 away = transform.position - cam.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
