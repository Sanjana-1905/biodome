using UnityEngine;

namespace BioDome.Environment
{
    /// <summary>
    /// Marks a rectangular area of soil where plants will be placed in a later phase.
    /// The transform position is the centre of the soil surface; the zone lies in the local XZ plane.
    /// </summary>
    public class PlantingZone : MonoBehaviour
    {
        [SerializeField] private string zoneName = "Zone";
        [SerializeField, Tooltip("Width (X) and depth (Z) of the plantable area in metres.")]
        private Vector2 size = new Vector2(4f, 2f);
        [SerializeField, Min(0.1f), Tooltip("Suggested distance between plants.")]
        private float plantSpacing = 0.6f;

        public string ZoneName => zoneName;
        public Vector2 Size => size;
        public float PlantSpacing => plantSpacing;

        /// <summary>Sets name and size (used by the greenhouse builder).</summary>
        public void Configure(string newName, Vector2 areaSize)
        {
            zoneName = newName;
            size = new Vector2(Mathf.Max(0.1f, areaSize.x), Mathf.Max(0.1f, areaSize.y));
        }

        /// <summary>True if a world-space point lies inside the zone footprint.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.y * 0.5f;
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireCube(new Vector3(0f, 0.02f, 0f), new Vector3(size.x, 0.04f, size.y));
        }

        private void OnValidate()
        {
            size = new Vector2(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y));
        }
    }
}
