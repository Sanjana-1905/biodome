using BioDome.Plants;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Renders one species at a chosen growth into a texture, using Unity's isolated preview scene.
    /// It never touches the open scene, the simulation or any asset: the plant is instantiated inside the preview
    /// scene, built in memory and discarded. Used by the Plant Preview window and by the batch screenshot tool.
    /// </summary>
    public static class PlantPreviewRenderer
    {
        // Same small, fixed variant offsets the population builder uses, so the preview matches what is planted.
        private static readonly int[] LeafCountDelta = { -1, 0, 1, 0, -1, 1, 0, 1 };
        private static readonly float[] HeightFactor = { 0.97f, 1.00f, 1.03f, 0.99f, 1.02f, 0.98f, 1.01f, 1.00f };
        private static readonly float[] WidthFactor = { 1.00f, 0.98f, 1.03f, 1.00f, 0.97f, 1.02f, 1.00f, 0.99f };

        public sealed class Result
        {
            public Texture2D Image;
            public int Triangles;
            public int Vertices;
            public Vector3 Size; // bounds size in metres
        }

        /// <summary>
        /// Renders the prefab's species at <paramref name="growth01"/> (0 to 1) for look number <paramref name="variant"/>.
        /// With <paramref name="useCodeDefaults"/> the generator's built-in defaults replace the prefab's stored shape settings
        /// (used by the batch tool while the generators are being developed; the window uses the real prefab values).
        /// </summary>
        public static Result Render(GameObject prefab, float growth01, int variant, int width, int height, int detail = 2,
            float yawDegrees = 28f, bool useCodeDefaults = false)
        {
            if (prefab == null || prefab.GetComponent<PlantGenerator>() == null) return null;

            var preview = new PreviewRenderUtility();
            try
            {
                SetUpLights(preview);

                GameObject instance = preview.InstantiatePrefabInScene(prefab);
                var generator = instance.GetComponent<PlantGenerator>();
                if (useCodeDefaults) generator.ApplyDefaultSettings();
                int i = Mathf.Abs(variant) % LeafCountDelta.Length;
                generator.Vary(generator.Seed + variant * 17, LeafCountDelta[i], HeightFactor[i], WidthFactor[i], growth01, detail);

                var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                generator.BuildMeshInto(mesh, growth01);
                generator.DisplayMesh(mesh, growth01);

                Bounds bounds = mesh.bounds;
                AddGround(preview, bounds);
                Frame(preview.camera, bounds, yawDegrees);

                preview.BeginStaticPreview(new Rect(0, 0, width, height));
                preview.camera.Render();
                preview.camera.Render(); // the first frame after setup can come out blank or wrongly lit; the second is the real picture
                Texture2D image = preview.EndStaticPreview();

                int triangles = 0;
                for (int s = 0; s < mesh.subMeshCount; s++) triangles += (int)(mesh.GetIndexCount(s) / 3);

                var result = new Result { Image = image, Triangles = triangles, Vertices = mesh.vertexCount, Size = bounds.size };
                Object.DestroyImmediate(mesh);
                return result;
            }
            finally
            {
                preview.Cleanup();
            }
        }

        private static void SetUpLights(PreviewRenderUtility preview)
        {
            Camera camera = preview.camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.80f, 0.86f, 0.90f, 1f);
            camera.fieldOfView = 28f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 60f;

            preview.ambientColor = new Color(0.46f, 0.50f, 0.48f, 1f);
            preview.lights[0].transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            preview.lights[0].intensity = 1.35f;
            preview.lights[0].color = new Color(1f, 0.97f, 0.9f, 1f);
            preview.lights[1].transform.rotation = Quaternion.Euler(30f, 150f, 0f);
            preview.lights[1].intensity = 0.45f;
        }

        /// <summary>A flat soil-coloured disc sized to the plant. HideAndDontSave keeps it out of any scene file.</summary>
        private static void AddGround(PreviewRenderUtility preview, Bounds bounds)
        {
            GameObject ground = EditorUtility.CreateGameObjectWithHideFlags(
                "PreviewGround", HideFlags.HideAndDontSave, typeof(MeshFilter), typeof(MeshRenderer));
            ground.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            float diameter = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.z) * 0.95f + 0.1f, 0.35f);
            ground.transform.localScale = new Vector3(diameter, 0.004f, diameter);
            ground.transform.position = new Vector3(bounds.center.x, -0.004f, bounds.center.z);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader != null ? shader : Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_BaseColor", new Color(0.30f, 0.24f, 0.18f));
            material.SetColor("_Color", new Color(0.30f, 0.24f, 0.18f));
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            preview.AddSingleGO(ground);
        }

        private static void Frame(Camera camera, Bounds bounds, float yawDegrees)
        {
            // A floor on the framing size keeps tiny ground-cover patches from being blown up to fill the picture.
            float radius = Mathf.Max(bounds.extents.magnitude * 0.92f, 0.22f);
            float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            Vector3 center = bounds.center;
            Quaternion rotation = Quaternion.Euler(16f, yawDegrees, 0f);
            camera.transform.rotation = rotation;
            camera.transform.position = center - rotation * Vector3.forward * distance;
        }
    }
}
