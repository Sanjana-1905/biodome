using System;
using System.Collections.Generic;
using BioDome.Environment;
using BioDome.Irrigation;
using BioDome.Plants;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Shared tools for the landscaping builders: mature plant meshes (built once, saved under new names, pre-assigned so plants never pop from
    /// seedling to mature), simulated plant spawning with a chosen start growth, a compact recipe language for layered compositions, benches,
    /// irrigation groups and materials. Nothing here touches the original plants or meshes.
    /// </summary>
    internal sealed class PlantKit
    {
        public const string MeshFolder = "Assets/Art/Models/Plants/Landscape";

        public readonly Dictionary<string, PlantSpeciesEntry> Species = new Dictionary<string, PlantSpeciesEntry>();
        public readonly PlantPopulationSettings Settings;
        public int Spawned;
        public long Triangles;
        private readonly Dictionary<(string, int, int), Mesh> meshes = new Dictionary<(string, int, int), Mesh>();

        private PlantKit(PlantPopulationSettings settings) { Settings = settings; }

        public static PlantKit Create(out string error)
        {
            error = null;
            var settings = AssetDatabase.LoadAssetAtPath<PlantPopulationSettings>(PlantPopulationBuilder.SettingsPath);
            if (settings == null) { error = "PlantPopulationSettings not found."; return null; }
            var kit = new PlantKit(settings);
            foreach (PlantSpeciesEntry e in settings.species)
                if (e != null && e.prefab != null && e.data != null) kit.Species[e.prefab.name] = e;
            foreach (string planPath in new[] { "Assets/ScriptableObjects/Settings/PlantAdditionPlan_Lush.asset", "Assets/ScriptableObjects/Settings/PlantAdditionPlan.asset" })
            {
                var plan = AssetDatabase.LoadAssetAtPath<PlantAdditionPlan>(planPath);
                if (plan == null) continue;
                foreach (PlantAdditionEntry a in plan.additions)
                    if (a != null && a.species != null && a.species.prefab != null && a.species.data != null && !kit.Species.ContainsKey(a.species.prefab.name))
                        kit.Species[a.species.prefab.name] = a.species;
            }
            foreach (string n in AllSpecies)
                if (!kit.Species.ContainsKey(n)) { error = $"species '{n}' not found."; return null; }
            return kit;
        }

        public static readonly string[] AllSpecies =
        {
            "Banana", "ArecaPalm", "BirdOfParadise", "Monstera", "Philodendron", "BostonFern", "Calathea", "Anthurium", "Orchid", "Moss", "CreepingGroundCover"
        };

        /// <summary>Rough mature canopy radius (m): how much room a plant of this species wants.</summary>
        public static float Radius(string species)
        {
            switch (species)
            {
                case "Banana": return 0.95f;
                case "ArecaPalm": return 0.85f;
                case "BirdOfParadise": return 0.80f;
                case "Monstera": return 0.70f;
                case "Philodendron": return 0.55f;
                case "BostonFern": return 0.45f;
                case "Calathea": return 0.40f;
                case "Anthurium": return 0.38f;
                case "Orchid": return 0.28f;
                case "Moss": return 0.45f;
                default: return 0.32f; // creeping ground cover
            }
        }

        /// <summary>Only tall species cast shadows (cheaper, and low plants gain nothing from it).</summary>
        public static bool CastsShadow(string species)
        {
            return species == "Banana" || species == "ArecaPalm" || species == "BirdOfParadise" || species == "Monstera" || species == "Philodendron";
        }

        // --- Meshes ------------------------------------------------------------------

        public Mesh MeshFor(string label, int variant, float growth)
        {
            int step = PlantGenerator.StepOf(growth);
            var key = (label, variant, step);
            if (meshes.TryGetValue(key, out Mesh cached)) return cached;

            PlantSpeciesEntry sp = Species[label];
            GreenhouseAssetFactory.EnsureFolder(MeshFolder);
            string path = $"{MeshFolder}/{label}_V{variant + 1}_g{step:00}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh();

            var temp = (GameObject)UnityEngine.Object.Instantiate(sp.prefab);
            try
            {
                var gen = temp.GetComponent<PlantGenerator>();
                PlantPopulationBuilder.ApplyVariant(gen, variant, Settings, sp);
                gen.BuildMeshInto(mesh, PlantGenerator.GrowthOfStep(step));
            }
            finally { UnityEngine.Object.DestroyImmediate(temp); }

            mesh.name = $"{label}_V{variant + 1}_g{step:00}";
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            meshes[key] = mesh;
            return mesh;
        }

        // --- Spawning ----------------------------------------------------------------

        /// <summary>
        /// Places a simulated plant that starts at <paramref name="growth"/> (it keeps normal health, water and growth simulation) with its mesh
        /// already assigned. It is flagged to use its own irrigation (a bed group), so the main irrigation is never involved.
        /// </summary>
        public PlantController Spawn(string label, Transform parent, Vector3 localPos, float yaw, float scale, float growth, int variant)
        {
            PlantSpeciesEntry sp = Species[label];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(sp.prefab, parent);
            go.name = $"{label}_{Spawned + 1:0000}";
            Spawned++;

            var gen = go.GetComponent<PlantGenerator>();
            PlantPopulationBuilder.ApplyVariant(gen, variant, Settings, sp);
            Mesh mesh = MeshFor(label, variant, growth);
            gen.DisplayMesh(mesh, PlantGenerator.GrowthOfStep(PlantGenerator.StepOf(growth)));
            for (int s = 0; s < mesh.subMeshCount; s++) Triangles += (int)(mesh.GetIndexCount(s) / 3);

            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && !CastsShadow(label)) renderer.shadowCastingMode = ShadowCastingMode.Off;

            var pc = go.GetComponent<PlantController>();
            var so = new SerializedObject(pc);
            so.FindProperty("usesOwnIrrigation").boolValue = true;
            so.FindProperty("startGrowth01").floatValue = growth;
            so.ApplyModifiedPropertiesWithoutUndo();
            return pc;
        }

        // --- Compositions ------------------------------------------------------------

        public struct Layer
        {
            public string Species;
            public int Count;
            public float R0, R1;
            public bool Cover; // may overlap and spill over the edge
        }

        /// <summary>Parses "Banana:3@0-0.5, Moss:6@0.7-1.1c" (species:count@innerRadius-outerRadius as a fraction of the oval; c = ground cover).</summary>
        public static List<Layer> Parse(string recipe)
        {
            var list = new List<Layer>();
            foreach (string part in recipe.Split(','))
            {
                string t = part.Trim();
                if (t.Length == 0) continue;
                bool cover = t.EndsWith("c", StringComparison.Ordinal);
                if (cover) t = t.Substring(0, t.Length - 1);
                string[] a = t.Split('@'), b = a[0].Split(':'), r = a[1].Split('-');
                list.Add(new Layer
                {
                    Species = b[0].Trim(), Count = int.Parse(b[1], System.Globalization.CultureInfo.InvariantCulture),
                    R0 = float.Parse(r[0], System.Globalization.CultureInfo.InvariantCulture), R1 = float.Parse(r[1], System.Globalization.CultureInfo.InvariantCulture), Cover = cover
                });
            }
            return list;
        }

        /// <summary>
        /// Fills an oval (semi-axes a along x and b along z, centred at <paramref name="centre"/> in <paramref name="parent"/>'s space) with a layered composition:
        /// tall plants in the middle, mid-height plants around them and low cover on the edge. Deterministic for a given rng.
        /// Returns how many plants were placed.
        /// </summary>
        public int Compose(Transform parent, System.Random rng, List<PlantController> into, Vector2 centre, float a, float b, string recipe, float baseY, float growth = 1f)
        {
            var placed = new List<(Vector2 p, float r, bool cover)>();
            int count = 0;
            foreach (Layer layer in Parse(recipe))
            {
                float r = Radius(layer.Species);
                int done = 0;
                for (int attempt = 0; attempt < 90 && done < layer.Count; attempt++)
                {
                    float t = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float rr = Mathf.Lerp(layer.R0, layer.R1, Mathf.Sqrt((float)rng.NextDouble()));
                    var pt = new Vector2(centre.x + a * rr * Mathf.Cos(t), centre.y + b * rr * Mathf.Sin(t));
                    bool ok = true;
                    foreach (var q in placed)
                    {
                        float need;
                        if (layer.Cover && q.cover) need = Mathf.Max(0.22f, (r + q.r) * 0.55f);
                        else if (layer.Cover || q.cover) need = Mathf.Min(r, q.r) * 0.4f + 0.1f;
                        else need = (r + q.r) * 0.42f;
                        if ((pt - q.p).sqrMagnitude < need * need) { ok = false; break; }
                    }
                    if (!ok) continue;

                    float scale = 0.9f + (float)rng.NextDouble() * 0.22f;
                    into.Add(Spawn(layer.Species, parent, new Vector3(pt.x, baseY, pt.y), (float)rng.NextDouble() * 360f, scale, growth, rng.Next(4)));
                    placed.Add((pt, r, layer.Cover));
                    done++; count++;
                }
            }
            return count;
        }

        // --- Irrigation ----------------------------------------------------------------

        /// <summary>Adds a bed irrigation group (own controller, shared settings) to <paramref name="owner"/> for the given plants.</summary>
        public static IrrigationController AddIrrigation(GameObject owner, List<PlantController> plants)
        {
            var irrObject = new GameObject("Irrigation");
            irrObject.transform.SetParent(owner.transform, false);
            var irr = irrObject.AddComponent<IrrigationController>();
            var so = new SerializedObject(irr);
            float flow = Mathf.Max(0.5f, plants.Count * 0.2f);
            so.FindProperty("reservoirCapacityLitres").floatValue = 300f;
            so.FindProperty("startingWaterLitres").floatValue = 300f;
            so.FindProperty("mainsSupplyLitresPerHour").floatValue = flow;
            so.FindProperty("flowRateLitresPerHour").floatValue = flow;
            so.FindProperty("mode").enumValueIndex = (int)IrrigationController.Mode.Auto;
            so.FindProperty("startBelowMoisture").floatValue = 0.40f;
            so.FindProperty("stopAboveMoisture").floatValue = 0.70f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var group = owner.GetComponent<BedIrrigationGroup>() ?? owner.AddComponent<BedIrrigationGroup>();
            var gso = new SerializedObject(group);
            gso.FindProperty("irrigation").objectReferenceValue = irr;
            SerializedProperty list = gso.FindProperty("plants");
            list.arraySize = plants.Count;
            for (int i = 0; i < plants.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = plants[i];
            gso.ApplyModifiedPropertiesWithoutUndo();
            return irr;
        }

        // --- Geometry helpers ------------------------------------------------------------

        public static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/{name}.mat");

        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        /// <summary>A park bench (timber seat and back on steel legs). Faces local -z.</summary>
        public static Transform Bench(Transform parent, string name, Vector3 pos, float yaw, Material timber, Material steel)
        {
            var b = new GameObject(name).transform;
            b.SetParent(parent, false);
            b.localPosition = pos;
            b.localRotation = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 3; i++) Prim(PrimitiveType.Cube, "Seat" + i, b, new Vector3(0f, 0.45f, -0.18f + i * 0.17f), new Vector3(1.5f, 0.04f, 0.14f), timber);
            for (int i = 0; i < 2; i++)
            {
                var back = Prim(PrimitiveType.Cube, "Back" + i, b, new Vector3(0f, 0.68f + i * 0.17f, 0.24f), new Vector3(1.5f, 0.12f, 0.035f), timber);
                back.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            }
            foreach (float x in new[] { -0.68f, 0.68f })
            {
                Prim(PrimitiveType.Cube, "Leg", b, new Vector3(x, 0.22f, 0f), new Vector3(0.05f, 0.44f, 0.5f), steel);
                var post = Prim(PrimitiveType.Cube, "BackPost", b, new Vector3(x, 0.66f, 0.26f), new Vector3(0.05f, 0.45f, 0.05f), steel);
                post.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            }
            return b;
        }

        /// <summary>A transparent URP material (alpha blended, no depth write).</summary>
        public static Material TransparentMaterial(string path, Color color, string shaderName, Texture2D map = null, bool doubleSided = true)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = m == null;
            Shader shader = Shader.Find(shaderName);
            if (created) m = new Material(shader);
            m.shader = shader;
            m.SetColor("_BaseColor", color);
            if (map != null) m.SetTexture("_BaseMap", map);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            if (created) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        public static Material LitMaterial(string path, Color color, float smoothness, float metallic = 0f, Color? emission = null, bool doubleSided = false)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = m == null;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (created) m = new Material(shader);
            m.shader = shader;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                m.SetColor("_EmissionColor", emission.Value);
            }
            if (created) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }
    }
}
