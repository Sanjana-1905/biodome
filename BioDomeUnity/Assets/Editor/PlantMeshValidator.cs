using System.Text;
using BioDome.Plants;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Checks a generated plant (scene object or prefab asset) for the problems that matter when testing:
    /// missing mesh or materials, broken normals or winding, NaN data, bad bounds, missing collider or scripts.
    /// Results go to the Console as one report.
    /// </summary>
    public static class PlantMeshValidator
    {
        private sealed class Report
        {
            public readonly StringBuilder Text = new StringBuilder();
            public int Failures;
            public int Warnings;

            public void Pass(string message) { Text.AppendLine("  [PASS] " + message); }
            public void Info(string message) { Text.AppendLine("  [INFO] " + message); }
            public void Warn(string message) { Warnings++; Text.AppendLine("  [WARN] " + message); }
            public void Fail(string message) { Failures++; Text.AppendLine("  [FAIL] " + message); }
        }

        [MenuItem("BioDome/Plants/Validate Selected Plant")]
        public static void ValidateSelected()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("BioDome: select a plant (scene object or prefab) to validate.");
                return;
            }
            ValidateAndLog(selected);
        }

        [MenuItem("BioDome/Plants/Validate Selected Plant", true)]
        private static bool ValidateSelectedEnabled()
        {
            return Selection.activeGameObject != null;
        }

        public static bool ValidateAndLog(GameObject root)
        {
            var report = new Report();
            Validate(root, report);

            string summary = $"BioDome plant validation for '{root.name}': {report.Failures} failure(s), {report.Warnings} warning(s)\n{report.Text}";
            if (report.Failures > 0) Debug.LogError(summary, root);
            else if (report.Warnings > 0) Debug.LogWarning(summary, root);
            else Debug.Log(summary, root);
            return report.Failures == 0;
        }

        private static void Validate(GameObject root, Report r)
        {
            // References and structure.
            var generator = root.GetComponent<PlantGenerator>();
            if (generator == null)
            {
                r.Fail("No PlantGenerator component on the selected object.");
                return;
            }
            r.Pass($"{generator.GetType().Name} found (seed {generator.Seed}, growth {generator.Growth01:0.00}).");

            int missingScripts = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            if (missingScripts > 0) r.Fail($"{missingScripts} missing script reference(s) in the hierarchy.");
            else r.Pass("No missing scripts.");

            var filter = root.GetComponentInChildren<MeshFilter>(true);
            var renderer = root.GetComponentInChildren<MeshRenderer>(true);
            if (filter == null || renderer == null)
            {
                r.Fail("MeshFilter or MeshRenderer is missing (the plant has not been built).");
                return;
            }

            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
            {
                r.Fail("MeshFilter has no mesh.");
                return;
            }
            if (PrefabUtility.IsPartOfPrefabAsset(root) && !EditorUtility.IsPersistent(mesh))
                r.Fail("The prefab uses a mesh that is not saved as an asset. Regenerate it with BioDome > Plants > Generate Monstera Prefabs.");
            else r.Pass($"Mesh '{mesh.name}' assigned.");

            // Visibility.
            // activeInHierarchy is always false for prefab assets (they are not in a scene), so test each
            // object's own active flag up the hierarchy instead.
            if (!renderer.enabled || !IsActiveSelfChain(renderer.gameObject)) r.Warn("Renderer is disabled or its object is inactive.");
            else r.Pass("Renderer is enabled and its objects are active.");
            Bounds bounds = mesh.bounds;
            if (bounds.size.magnitude < 0.1f || bounds.size.magnitude > 50f)
                r.Fail($"Mesh bounds look wrong: size {bounds.size}.");
            else r.Pass($"Bounds size {bounds.size.x:0.00} x {bounds.size.y:0.00} x {bounds.size.z:0.00} m.");

            // Materials.
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length != mesh.subMeshCount)
                r.Fail($"{mesh.subMeshCount} submeshes but {materials.Length} materials.");
            bool materialsOk = true;
            for (int i = 0; i < materials.Length; i++)
            {
                Material m = materials[i];
                if (m == null)
                {
                    r.Fail($"Material slot {i} is empty.");
                    materialsOk = false;
                }
                else if (m.shader == null || !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader")
                {
                    r.Fail($"Material '{m.name}' (slot {i}) has a missing or unsupported shader.");
                    materialsOk = false;
                }
            }
            if (materialsOk && materials.Length == mesh.subMeshCount)
                r.Pass($"{materials.Length} materials assigned, all with valid URP shaders.");

            // Geometry.
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (vertices.Length == 0)
            {
                r.Fail("Mesh has no vertices.");
                return;
            }
            if (normals.Length != vertices.Length)
            {
                r.Fail($"Mesh has {normals.Length} normals for {vertices.Length} vertices.");
                return;
            }

            int nanCount = 0, badLength = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (HasNaN(vertices[i]) || HasNaN(normals[i])) nanCount++;
                else if (Mathf.Abs(normals[i].magnitude - 1f) > 0.05f) badLength++;
            }
            if (nanCount > 0) r.Fail($"{nanCount} vertices or normals contain NaN or infinity.");
            else r.Pass("No NaN values.");
            if (badLength > 0) r.Warn($"{badLength} normals are not unit length.");
            else r.Pass("All normals are unit length.");

            int triangles = 0, degenerate = 0, outOfRange = 0, judged = 0, inverted = 0;
            int emptySubmeshes = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] indices = mesh.GetTriangles(s);
                if (indices.Length == 0) emptySubmeshes++;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    triangles++;
                    if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length) { outOfRange++; continue; }

                    Vector3 g = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    float length = g.magnitude;
                    if (length < 1e-9f) { degenerate++; continue; }

                    Vector3 average = normals[a] + normals[b] + normals[c];
                    if (average.sqrMagnitude < 1e-8f) continue;
                    float dot = Vector3.Dot(g / length, average.normalized);
                    if (Mathf.Abs(dot) < 0.3f) continue; // edge strips are perpendicular to their shared normals
                    judged++;
                    if (dot < 0f) inverted++;
                }
            }

            r.Info($"{vertices.Length} vertices, {triangles} triangles, {mesh.subMeshCount} submeshes.");
            if (outOfRange > 0) r.Fail($"{outOfRange} triangles reference vertices that do not exist.");
            else r.Pass("All triangle indices are valid.");
            if (degenerate > 0) r.Info($"{degenerate} zero-area triangles (expected at pointed leaf tips).");

            float invertedRatio = judged > 0 ? inverted / (float)judged : 0f;
            if (invertedRatio > 0.05f) r.Fail($"{invertedRatio * 100f:0.#}% of triangles face against their normals (inverted winding).");
            else r.Pass($"Winding agrees with normals ({inverted} of {judged} judged triangles inverted).");

            if (emptySubmeshes > 0) r.Info($"{emptySubmeshes} submesh(es) are empty (unused leaf variants or accents).");

            // Collider.
            var box = root.GetComponent<BoxCollider>();
            if (box == null) r.Warn("No BoxCollider on the root, so the plant cannot be clicked.");
            else if (box.size.sqrMagnitude < 1e-4f) r.Fail("BoxCollider has no size.");
            else r.Pass("BoxCollider present and sized.");
        }

        private static bool IsActiveSelfChain(GameObject go)
        {
            for (Transform t = go.transform; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
            }
            return true;
        }

        private static bool HasNaN(Vector3 v)
        {
            return float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                   float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z);
        }
    }
}
