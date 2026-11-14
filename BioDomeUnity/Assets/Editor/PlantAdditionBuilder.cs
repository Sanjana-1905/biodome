using System.Collections.Generic;
using System.Text;
using BioDome.Environment;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Adds species to SELECTED planting zones without clearing, moving or regenerating anything that already exists.
    ///
    /// Safety design:
    ///  - New plants live under their own root, "BioDome_PlantAdditions". The original "BioDome_Plants" root is never
    ///    modified, so the existing Monstera keep their positions, scale, growth and simulation state.
    ///  - Only the zones named in the plan are touched; others are skipped.
    ///  - Existing plants (originals and earlier additions) are only READ. Their real positions and mature footprints keep new
    ///    plants a sensible distance away, so different species can share a bed without piling on top of each other.
    ///  - By default earlier additions are kept, and species already added are skipped, so running again is safe.
    ///  - A dry run reports counts and estimated triangles without changing anything.
    ///  - A checksum of every original plant's transform is taken before and after and reported.
    ///  - Initial growth and mesh detail come from the existing PlantPopulationSettings, so the shared cohort start (12%)
    ///    is identical for every plant. The GreenhouseManager finds the new plants automatically at Play.
    /// </summary>
    public static class PlantAdditionBuilder
    {
        public const string RootName = "BioDome_PlantAdditions";

        public sealed class Options
        {
            /// <summary>Report only: nothing in the scene or the asset database is changed.</summary>
            public bool DryRun;
            /// <summary>Delete the previously added plants first. Off by default: earlier additions are kept.</summary>
            public bool ReplacePrevious;
        }

        public sealed class Report
        {
            public string Error;
            public string Text = "";
            public int Added;
            public int KeptEarlierAdditions;
            public int Originals;
            public bool OriginalsUnchanged = true;
            public long EstimatedMatureTriangles;
            public bool HitCap;
            public readonly Dictionary<string, int> PerSpecies = new Dictionary<string, int>();
        }

        // A plant that already stands in the bed (or was just placed): where it is and how much room it needs.
        private struct Obstacle
        {
            public Vector2 Position;
            public float Radius;
            public float Overlap;
            public string Label;
        }

        // --- Entry points -------------------------------------------------------------

        public static Report Run(PlantAdditionPlan plan, Options options)
        {
            var report = new Report();
            options = options ?? new Options();

            string problem = ValidatePlan(plan);
            if (problem != null) { report.Error = problem; report.Text = problem; return report; }

            var settings = AssetDatabase.LoadAssetAtPath<PlantPopulationSettings>(PlantPopulationBuilder.SettingsPath);
            if (settings == null)
            {
                report.Error = $"The existing PlantPopulationSettings asset was not found at {PlantPopulationBuilder.SettingsPath}.";
                report.Text = report.Error;
                return report;
            }

            PlantingZone[] zoneArray = Object.FindObjectsByType<PlantingZone>();
            if (zoneArray.Length == 0)
            {
                report.Error = "The open scene has no PlantingZone objects. Open Assets/Scenes/BioDome_Main.unity (or a copy of it) first.";
                report.Text = report.Error;
                return report;
            }
            var zones = new List<PlantingZone>(zoneArray);
            zones.Sort((a, b) => string.CompareOrdinal(a.ZoneName, b.ZoneName)); // stable order for repeatable results

            GameObject root = FindRoot();
            if (root != null && options.ReplacePrevious && !options.DryRun)
            {
                Undo.DestroyObjectImmediate(root);
                root = null;
            }

            // Snapshot: every plant now in the scene. Originals are everything outside the additions root.
            PlantController[] all = Object.FindObjectsByType<PlantController>();
            var originals = new List<PlantController>();
            var obstacles = new List<Obstacle>();
            var presentLabels = new HashSet<string>();
            int keptAdditions = 0;

            foreach (PlantController p in all)
            {
                bool isAddition = root != null && p.transform.IsChildOf(root.transform);
                if (isAddition && options.ReplacePrevious) continue; // dry run with replace: pretend they are gone

                if (isAddition)
                {
                    keptAdditions++;
                    presentLabels.Add(LabelOf(p.name));
                }
                else originals.Add(p);

                obstacles.Add(new Obstacle
                {
                    Position = new Vector2(p.transform.position.x, p.transform.position.z),
                    Radius = RadiusOf(p),
                    Overlap = isAddition ? 0.45f : 0.2f,
                    Label = isAddition ? LabelOf(p.name) : ""
                });
            }

            int checksumBefore = Checksum(originals);
            report.Originals = originals.Count;
            report.KeptEarlierAdditions = keptAdditions;

            var text = new StringBuilder();
            text.AppendLine(options.DryRun
                ? $"DRY RUN: nothing will be changed. {originals.Count} original plants are in the scene."
                : $"Adding species to selected zones. {originals.Count} original plants are left untouched.");
            if (keptAdditions > 0) text.AppendLine($"  Keeping {keptAdditions} plants added earlier ({(options.ReplacePrevious ? "will be replaced" : "kept")}).");

            // Plan entries, tall layers first.
            var order = new List<int>();
            for (int i = 0; i < plan.additions.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int byLayer = plan.additions[a].layer.CompareTo(plan.additions[b].layer);
                return byLayer != 0 ? byLayer : a.CompareTo(b);
            });

            var groups = new Dictionary<PlantingZone, Transform>();
            var meshCache = new Dictionary<PlantAdditionEntry, Mesh[]>();
            var triangleCache = new Dictionary<GameObject, int>();
            var nameCounters = new Dictionary<string, int>();
            int totalAdded = keptAdditions;

            foreach (int index in order)
            {
                PlantAdditionEntry entry = plan.additions[index];
                string label = entry.species.label;

                if (presentLabels.Contains(label))
                {
                    text.AppendLine($"  {label}: already present from an earlier run, skipped.");
                    continue;
                }

                float radius = RadiusFor(entry);
                int placedForEntry = 0;
                int matchedZones = 0;
                foreach (PlantingZone zone in zones)
                {
                    if (!PlantAdditionPlan.Matches(entry, zone.ZoneName)) continue;
                    matchedZones++;

                    int room = Mathf.Min(entry.maxPerZone, Mathf.Max(0, plan.maxTotalAdded - totalAdded));
                    if (room < entry.maxPerZone) report.HitCap = true;
                    List<Vector3> spots = FindSpots(entry, zone, obstacles, plan.seed, index, radius, room);

                    foreach (Vector3 local in spots)
                    {
                        Vector3 world = zone.transform.TransformPoint(local);
                        obstacles.Add(new Obstacle
                        {
                            Position = new Vector2(world.x, world.z),
                            Radius = radius,
                            Overlap = entry.overlapAllowance,
                            Label = label
                        });
                    }

                    text.AppendLine($"  {label}: zone {zone.ZoneName}: {spots.Count} of {entry.maxPerZone} placed.");
                    totalAdded += spots.Count;
                    placedForEntry += spots.Count;
                    if (options.DryRun || spots.Count == 0) continue;

                    if (root == null)
                    {
                        root = new GameObject(RootName);
                        Undo.RegisterCreatedObjectUndo(root, "Add Species To Selected Zones");
                    }
                    if (!meshCache.TryGetValue(entry, out Mesh[] meshes))
                    {
                        meshes = PlantPopulationBuilder.BuildVariantMeshes(entry.species, settings);
                        meshCache.Add(entry, meshes);
                    }
                    if (!groups.TryGetValue(zone, out Transform group))
                    {
                        Transform existingGroup = root.transform.Find($"Plants_{zone.ZoneName}");
                        if (existingGroup == null)
                        {
                            var groupObject = new GameObject($"Plants_{zone.ZoneName}");
                            groupObject.transform.SetParent(root.transform, false);
                            existingGroup = groupObject.transform;
                        }
                        group = existingGroup;
                        groups.Add(zone, group);
                    }

                    var random = new System.Random(plan.seed ^ StableHash(zone.ZoneName) ^ StableHash(label) ^ 0x5bd1e995);
                    foreach (Vector3 local in spots)
                    {
                        nameCounters.TryGetValue(label, out int n);
                        nameCounters[label] = ++n;

                        int variant = random.Next(meshes.Length);
                        var plant = (GameObject)PrefabUtility.InstantiatePrefab(entry.species.prefab, group);
                        plant.name = $"{label}_{n:000}";

                        var generator = plant.GetComponent<PlantGenerator>();
                        PlantPopulationBuilder.ApplyVariant(generator, variant, settings, entry.species);
                        generator.DisplayMesh(meshes[variant], settings.initialGrowth01);

                        // Natural variation: random heading and a small scale change. Neither makes a plant look older.
                        float yaw = entry.randomRotation ? (float)random.NextDouble() * 360f : 0f;
                        float scale = 1f + ((float)random.NextDouble() * 2f - 1f) * entry.sizeVariation;
                        plant.transform.SetPositionAndRotation(zone.transform.TransformPoint(local), Quaternion.Euler(0f, yaw, 0f));
                        plant.transform.localScale = Vector3.one * scale;
                    }
                }

                if (matchedZones == 0) text.AppendLine($"  [WARN] {label}: none of its zone names matched a PlantingZone in this scene.");
                else text.AppendLine($"  {label}: {placedForEntry} plants {(options.DryRun ? "would be " : "")}added across {matchedZones} zone(s).");

                report.Added += placedForEntry;
                report.PerSpecies.TryGetValue(label, out int already);
                report.PerSpecies[label] = already + placedForEntry;

                if (placedForEntry > 0)
                    report.EstimatedMatureTriangles += (long)placedForEntry * MatureTriangles(entry.species.prefab, triangleCache);
            }

            if (report.HitCap) text.AppendLine($"  [LIMIT] The plan's population cap of {plan.maxTotalAdded} added plants was reached; some entries were trimmed.");

            if (!options.DryRun)
            {
                int checksumAfter = Checksum(originals);
                report.OriginalsUnchanged = checksumBefore == checksumAfter;
                text.AppendLine(report.OriginalsUnchanged
                    ? $"  Verified: all {originals.Count} original plants are unchanged (transform checksum identical before and after)."
                    : "  [ERROR] An original plant's transform changed. Use Edit > Undo and report this.");
                if (root != null)
                {
                    EditorSceneManager.MarkSceneDirty(root.scene);
                    Selection.activeGameObject = root;
                }
                AssetDatabase.SaveAssets();
            }

            text.AppendLine($"  {(options.DryRun ? "Would add" : "Added")} {report.Added} plants (total additions in scene: {totalAdded}). " +
                            $"Estimated triangles of the added plants at full maturity: {report.EstimatedMatureTriangles.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}.");
            if (!options.DryRun) text.AppendLine("  The scene is marked modified, NOT saved.");

            report.Text = text.ToString();
            return report;
        }

        /// <summary>Rebuilds the initial-growth mesh assets of every species in the plan (after a generator was improved).</summary>
        public static string RefreshMeshes(PlantAdditionPlan plan)
        {
            string problem = ValidatePlan(plan);
            if (problem != null) return problem;
            var settings = AssetDatabase.LoadAssetAtPath<PlantPopulationSettings>(PlantPopulationBuilder.SettingsPath);
            if (settings == null) return "PlantPopulationSettings not found.";

            var done = new HashSet<GameObject>();
            int count = 0;
            foreach (PlantAdditionEntry entry in plan.additions)
            {
                if (!done.Add(entry.species.prefab)) continue;
                PlantPopulationBuilder.BuildVariantMeshes(entry.species, settings);
                count++;
            }
            AssetDatabase.SaveAssets();
            return $"Refreshed the initial-growth meshes of {count} species. Added plants in the scene pick them up automatically. Monstera meshes were not touched.";
        }

        [MenuItem("BioDome/Plants/Remove Added Species")]
        public static void RemoveAdded()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("BioDome plants", "Stop Play Mode first.", "OK");
                return;
            }

            GameObject root = FindRoot();
            if (root == null)
            {
                EditorUtility.DisplayDialog("BioDome plants", $"There is no '{RootName}' object in the open scene.", "OK");
                return;
            }

            int count = root.GetComponentsInChildren<PlantController>(true).Length;
            if (!EditorUtility.DisplayDialog("BioDome plants",
                    $"Remove the {count} added plants under '{RootName}'?\n(The original plants under 'BioDome_Plants' are not touched.)",
                    "Remove", "Cancel"))
                return;

            EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.DestroyObjectImmediate(root);
            Debug.Log($"BioDome: removed {count} added plants. The original plants were not touched. Scene is marked modified, NOT saved.");
        }

        // --- Placement ------------------------------------------------------------------

        /// <summary>
        /// Finds up to <paramref name="max"/> positions (zone-local, y = 0). Each position is the best of a small batch of random
        /// candidates that respect every plant already there (distance and mature footprints), scored by the entry's placement
        /// bias. Deterministic for a given plan seed, zone and entry.
        /// </summary>
        private static List<Vector3> FindSpots(PlantAdditionEntry entry, PlantingZone zone, List<Obstacle> obstacles,
            int planSeed, int entryIndex, float radius, int max)
        {
            var spots = new List<Vector3>();
            float halfX = zone.Size.x * 0.5f - entry.edgeMargin;
            float halfZ = zone.Size.y * 0.5f - entry.edgeMargin;
            if (halfX <= 0f || halfZ <= 0f || max <= 0) return spots;

            var random = new System.Random(planSeed ^ StableHash(zone.ZoneName) ^ (entryIndex * 104729));
            Vector3 center = zone.transform.position;
            float backSign = center.z >= 0f ? 1f : -1f; // the central aisle runs along world X, so "back" is away from z = 0
            var local = new List<Obstacle>(); // spots chosen in this call (obstacles for the next pick)

            for (int pick = 0; pick < max; pick++)
            {
                bool found = false;
                Vector3 best = Vector3.zero;
                float bestScore = float.MinValue;
                int valid = 0;

                for (int attempt = 0; attempt < 240 && valid < 14; attempt++)
                {
                    var candidate = new Vector3(
                        (float)(random.NextDouble() * 2.0 - 1.0) * halfX, 0f,
                        (float)(random.NextDouble() * 2.0 - 1.0) * halfZ);
                    Vector3 world = zone.transform.TransformPoint(candidate);
                    var point = new Vector2(world.x, world.z);

                    if (!Fits(point, entry, radius, obstacles) || !Fits(point, entry, radius, local)) continue;
                    valid++;

                    float score = BiasScore(entry.bias, candidate.x / halfX, candidate.z / halfZ, world, center, backSign, zone.Size.y * 0.5f)
                                  + (float)random.NextDouble() * 0.35f;
                    if (score > bestScore) { bestScore = score; best = candidate; found = true; }
                }

                if (!found) break; // no room left for this species in this zone

                spots.Add(best);
                Vector3 bestWorld = zone.transform.TransformPoint(best);
                local.Add(new Obstacle { Position = new Vector2(bestWorld.x, bestWorld.z), Radius = radius, Overlap = entry.overlapAllowance, Label = entry.species.label });
            }
            return spots;
        }

        /// <summary>True if a plant of this entry fits at <paramref name="point"/> among the given plants.</summary>
        private static bool Fits(Vector2 point, PlantAdditionEntry entry, float radius, List<Obstacle> others)
        {
            float clearance = entry.clearanceFromExisting;
            for (int i = 0; i < others.Count; i++)
            {
                Obstacle o = others[i];
                float distance = (o.Position - point).magnitude;

                if (distance < clearance) return false;
                if (o.Label == entry.species.label && distance < entry.minSpacingBetweenNew) return false;

                // Mature footprints may overlap by the more generous of the two allowances (ground cover may sit under others).
                float allowed = Mathf.Max(entry.overlapAllowance, o.Overlap);
                if (distance < (radius + o.Radius) * (1f - allowed)) return false;
            }
            return true;
        }

        private static float BiasScore(PlacementBias bias, float nx, float nz, Vector3 world, Vector3 zoneCenter, float backSign, float halfDepth)
        {
            float ax = Mathf.Abs(nx), az = Mathf.Abs(nz);
            switch (bias)
            {
                case PlacementBias.Edges: return Mathf.Max(ax, az);
                case PlacementBias.Corners: return ax * az * 1.6f;
                case PlacementBias.Center: return 1f - Mathf.Max(ax, az);
                case PlacementBias.BackRow: return Mathf.Clamp((world.z - zoneCenter.z) * backSign / Mathf.Max(0.1f, halfDepth), -1f, 1f);
                case PlacementBias.FrontRow: return -Mathf.Clamp((world.z - zoneCenter.z) * backSign / Mathf.Max(0.1f, halfDepth), -1f, 1f);
                default: return 0f;
            }
        }

        private static float RadiusFor(PlantAdditionEntry entry)
        {
            return entry.species.data.MatureCanopyWidth * 0.5f * entry.radiusScale;
        }

        private static float RadiusOf(PlantController plant)
        {
            return plant.Data != null ? plant.Data.MatureCanopyWidth * 0.5f * 0.7f : 0.6f;
        }

        // --- Estimates and checks ---------------------------------------------------------

        /// <summary>Triangles of one plant of this species at full growth, built in memory from a temporary prefab copy.</summary>
        private static int MatureTriangles(GameObject prefab, Dictionary<GameObject, int> cache)
        {
            if (cache.TryGetValue(prefab, out int cached)) return cached;

            int triangles = 0;
            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject contents = PrefabUtility.LoadPrefabContents(path); // an isolated temporary scene, not the open one
            try
            {
                var generator = contents.GetComponent<PlantGenerator>();
                var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                generator.Vary(generator.Seed, 0, 1f, 1f, 1f, 1); // detail 1, as in the greenhouse
                generator.BuildMeshInto(mesh, 1f);
                for (int s = 0; s < mesh.subMeshCount; s++) triangles += (int)(mesh.GetIndexCount(s) / 3);
                Object.DestroyImmediate(mesh);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            cache[prefab] = triangles;
            return triangles;
        }

        public static string ValidatePlan(PlantAdditionPlan plan)
        {
            if (plan == null) return "No plant addition plan is selected.";
            if (plan.additions == null || plan.additions.Count == 0) return "The plant addition plan has no entries.";

            for (int i = 0; i < plan.additions.Count; i++)
            {
                PlantAdditionEntry e = plan.additions[i];
                string label = e?.species != null && !string.IsNullOrEmpty(e.species.label) ? e.species.label : $"entry {i + 1}";
                if (e == null || e.species == null || e.species.prefab == null || e.species.data == null)
                    return $"'{label}' needs a prefab and a PlantData asset in the plan. (Run 'Create Or Upgrade Species Assets', then recreate the preset.)";
                if (e.species.prefab.GetComponent<PlantGenerator>() == null || e.species.prefab.GetComponent<PlantController>() == null)
                    return $"The prefab for '{label}' needs both a PlantGenerator and a PlantController.";
                if (e.zoneNames == null || e.zoneNames.Count == 0)
                    return $"'{label}' lists no zones. Add zone names (for example Bed_N1_2) to the plan.";
            }
            return null;
        }

        /// <summary>The label part of an instance name such as "BostonFern_012".</summary>
        private static string LabelOf(string instanceName)
        {
            int underscore = instanceName.LastIndexOf('_');
            return underscore > 0 ? instanceName.Substring(0, underscore) : instanceName;
        }

        /// <summary>Order-independent hash of every plant's position, rotation and scale (rounded), to prove nothing moved.</summary>
        private static int Checksum(List<PlantController> plants)
        {
            unchecked
            {
                int sum = plants.Count;
                foreach (PlantController p in plants)
                {
                    if (p == null) continue;
                    Transform t = p.transform;
                    int h = 17;
                    h = h * 31 + Mathf.RoundToInt(t.position.x * 1000f);
                    h = h * 31 + Mathf.RoundToInt(t.position.y * 1000f);
                    h = h * 31 + Mathf.RoundToInt(t.position.z * 1000f);
                    h = h * 31 + Mathf.RoundToInt(t.eulerAngles.y * 100f);
                    h = h * 31 + Mathf.RoundToInt(t.localScale.x * 1000f);
                    sum += h; // addition keeps it independent of the order plants are found in
                }
                return sum;
            }
        }

        private static GameObject FindRoot()
        {
            foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == RootName) return go;
            }
            return null;
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = (int)2166136261;
                for (int i = 0; i < text.Length; i++) hash = (hash ^ text[i]) * 16777619;
                return hash;
            }
        }
    }
}
