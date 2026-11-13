using UnityEngine;

namespace BioDome.Procedural
{
    /// <summary>
    /// Minimal concrete generator that proves the pipeline end to end (asset -> MeshData -> Mesh).
    /// Also usable as a pole, pipe or placeholder stem. Create via Assets > Create > BioDome > Procedural > Cylinder.
    /// </summary>
    [CreateAssetMenu(fileName = "CylinderGenerator", menuName = "BioDome/Procedural/Cylinder", order = 100)]
    public class CylinderMeshGenerator : ProceduralMeshGenerator
    {
        [Header("Shape")]
        [SerializeField, Min(0.001f)] private float bottomRadius = 0.1f;
        [SerializeField, Min(0f)] private float topRadius = 0.1f;
        [SerializeField, Min(0.001f)] private float height = 1f;

        [Header("Resolution")]
        [SerializeField, Range(3, 64)] private int radialSegments = 12;
        [SerializeField, Range(1, 32)] private int heightSegments = 1;

        [Header("Caps")]
        [SerializeField] private bool capBottom = true;
        [SerializeField] private bool capTop = true;

        protected override void Build(MeshData target, System.Random random)
        {
            MeshPrimitives.AddCylinder(
                target, Vector3.zero, bottomRadius, topRadius, height,
                radialSegments, heightSegments, capBottom, capTop);
        }
    }
}
