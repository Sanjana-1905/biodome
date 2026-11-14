using System.Collections.Generic;
using System.Text;
using BioDome.Environment;
using BioDome.Plants;
using BioDome.Procedural;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Lengthens the greenhouse at its rear (east) end by whole structural bays, using the generator's own frame, glass and plinth modules,
    /// and builds the floor, the main path continuation, extra lamps and the side planters of the new section under "BioDome_Expansion".
    /// The original structure objects (Frame, Glass, Plinth) are switched off, not deleted: the regenerated versions are identical over the
    /// original 40 m and continue the same bays to the new rear wall. Meshes are saved under new names, so the original mesh assets used by
    /// other scenes are never overwritten. Does not save the scene (ConservatorySetup does, after its checks).
    /// </summary>
    public static class ConservatoryExpansion
    {
        public const string RootName = "BioDome_Expansion";
        public const float ExtraLength = 28f; // seven 4 m bays: the fountain (x = 13.8) ends up at the centre of the longer building

        // Shared layout numbers (world metres; the entrance is at x = -20, the original east wall was at x = +20).
        public const float OldEastX = 20f;
        public const float NewEastX = 48f;
        public const float HalfWidth = 12.5f;
        public const float MainPathHalf = 1.5f;
        public const float PlazaX = 13.8f;
        public const float PlazaRadius = 4.2f;
        public const float PathStartX = 18.25f;           // just east of the fountain plaza kerb
        public static readonly Vector2 ResearchPlaza = new Vector2(36f, 0f);
        public const float ResearchPlazaRadius = 4.0f;
        private const float Floor = 0.03f;
        private const string MeshPrefix = "Expanded_";

        public static string Build()
        {
            var log = new StringBuilder();
            GreenhouseSceneBuilder.ExpansionData data = GreenhouseSceneBuilder.GenerateExpansion(ExtraLength);
            GreenhouseSettings settings = data.Settings;
            if (Mathf.Abs(data.NewEastX - NewEastX) > 0.01f) return $"ERROR: expansion east wall at {data.NewEastX}, expected {NewEastX}.";

            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);

            // Switch off the old Frame / Glass / Plinth (kept in the scene so the change is reversible).
            GameObject generated = GameObject.Find("BioDome_Generated");
            int switchedOff = 0;
            if (generated != null)
            {
                Transform structure = generated.transform.Find("Structure");
                if (structure != null)
                    foreach (string n in new[] { "Frame", "Glass", "Plinth" })
                    {
                        Transform t = structure.Find(n);
                        if (t != null && t.gameObject.activeSelf) { t.gameObject.SetActive(false); switchedOff++; }
                    }
            }

            var root = new GameObject(RootName);
            Transform structureGroup = new GameObject("Structure").transform;
            structureGroup.SetParent(root.transform, false);
            structureGroup.position = new Vector3(data.OffsetX, 0f, 0f);

            Make("Frame", structureGroup, MeshPrefix + "Frame", data.Frame, new[] { settings.steel }, true, false);
            Make("Plinth", structureGroup, MeshPrefix + "Plinth", data.Plinth, new[] { settings.stone }, true, false);
            Make("Glass", structureGroup, MeshPrefix + "Glass", data.Glass, new[] { settings.glass }, false, true);

            // Lamps for the new frames.
            Transform lighting = new GameObject("Lighting").transform;
            lighting.SetParent(structureGroup, false);
            if (data.Lamps != null && data.Lamps.VertexCount > 0)
                Make("Fixtures", lighting, MeshPrefix + "Lamps", data.Lamps, new[] { settings.steel, settings.lampBulb }, false, true);
            // The new lamps are emissive fixtures only: extra real-time point lights are not worth their cost on a laptop GPU (sun, ambient and bloom light the new section).

            // Floor, main path and plazas (world coordinates).
            Transform interior = new GameObject("Interior").transform;
            interior.SetParent(root.transform, false);
            Make("Floor", interior, MeshPrefix + "Floor", BuildFloor(), new[] { settings.ground, settings.gravel }, false, false);
            Make("Pathways", interior, MeshPrefix + "Pathways", BuildPaths(), new[] { settings.pavers, settings.stone }, false, false);

            log.AppendLine($"  structure: {data.TotalBays} bays ({data.OriginalBays} original + {data.TotalBays - data.OriginalBays} new), east wall x={data.NewEastX}, " +
                           $"offset {data.OffsetX}; old Frame/Glass/Plinth switched off: {switchedOff}; {data.LampLights.Count / 2} new lamp fixtures, no extra real-time lights");
            return log.ToString();
        }

        // --- Floor and paths ------------------------------------------------------------

        private static MeshData BuildFloor()
        {
            var d = new MeshData();
            float x0 = OldEastX - 0.4f, x1 = NewEastX - 0.2f, z = HalfWidth - 0.2f;
            MeshShapes.AddQuadFacing(d, new Vector3(x0, 0f, -z), new Vector3(x0, 0f, z), new Vector3(x1, 0f, z), new Vector3(x1, 0f, -z), Vector3.up, 1);
            return d;
        }

        private static MeshData BuildPaths()
        {
            var d = new MeshData();
            float y = Floor, half = MainPathHalf;
            const float kerb = 0.16f;

            // Main path: from the fountain plaza kerb to the research plaza.
            float plazaWest = ResearchPlaza.x - Mathf.Sqrt(ResearchPlazaRadius * ResearchPlazaRadius - half * half);
            MeshShapes.AddQuadFacing(d, new Vector3(PathStartX, y, -half), new Vector3(PathStartX, y, half),
                new Vector3(plazaWest + 0.05f, y, half), new Vector3(plazaWest + 0.05f, y, -half), Vector3.up, 0);
            for (int s = -1; s <= 1; s += 2)
            {
                float zc = s * (half + kerb * 0.5f);
                MeshShapes.AddBox(d, new Vector3((PathStartX + plazaWest) * 0.5f, 0.05f, zc), new Vector3(plazaWest - PathStartX, 0.1f, kerb), Quaternion.identity, 1);
            }

            // Research plaza: paved disc with a kerb ring, open towards the path.
            var c = new Vector3(ResearchPlaza.x, 0f, ResearchPlaza.y);
            MeshShapes.AddDisc(d, new Vector3(c.x, y, c.z), ResearchPlazaRadius, 64, 0);
            float gap = Mathf.Asin(Mathf.Clamp01((half + kerb) / ResearchPlazaRadius)) * Mathf.Rad2Deg;
            MeshShapes.AddRing(d, c, ResearchPlazaRadius, ResearchPlazaRadius + 0.2f, 0.12f, 64, 1, 180f + gap, 540f - gap);
            return d;
        }

        // --- Helpers -------------------------------------------------------------------

        private static GameObject Make(string name, Transform parent, string meshName, MeshData data, Material[] materials, bool collider, bool noShadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            Mesh mesh = SaveMesh(meshName, data);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            int count = Mathf.Max(1, mesh.subMeshCount);
            var used = new Material[count];
            for (int i = 0; i < count; i++) used[i] = materials[Mathf.Min(i, materials.Length - 1)];
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = used;
            if (noShadows) { renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
            if (collider && mesh.vertexCount > 0) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        private static Mesh SaveMesh(string meshName, MeshData data)
        {
            GreenhouseAssetFactory.EnsureFolder(GreenhouseAssetFactory.MeshFolder);
            string path = $"{GreenhouseAssetFactory.MeshFolder}/{meshName}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh();
            mesh.name = meshName;
            data.ApplyTo(mesh);
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
