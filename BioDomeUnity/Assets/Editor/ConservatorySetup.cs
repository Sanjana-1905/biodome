using System.Collections.Generic;
using System.Text;
using BioDome.CameraSystem;
using BioDome.Environment;
using BioDome.Experiment;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// One-shot (re)build of the research garden, the collection beds and the navigation shortcuts in BioDome_Working, with safety checks.
    /// Only the roots it owns are replaced (BioDome_ExperimentGarden, BioDome_CollectionGardens, BioDome_Navigation). Saves only if every check passes.
    /// Menu: BioDome > Conservatory > Rebuild Research Garden, Collection and Navigation.
    /// </summary>
    public static class ConservatorySetup
    {
        private const string WorkingScene = "Assets/Scenes/BioDome_Working.unity";
        public const string NavRoot = "BioDome_Navigation";

        [MenuItem("BioDome/Conservatory/Rebuild Research Garden, Collection and Navigation")]
        public static void BuildFromMenu() => Debug.Log("BioDome conservatory:\n" + Build(true));

        public static void BuildBatch()
        {
            int exit = 0;
            try
            {
                EditorSceneManager.OpenScene(WorkingScene, OpenSceneMode.Single);
                string result = Build(true);
                Debug.Log("CONSERVATORY\n" + result);
                if (result.Contains("ERROR")) exit = 1;
            }
            catch (System.Exception e) { Debug.LogError("CONSERVATORY EXCEPTION " + e); exit = 2; }
            EditorApplication.Exit(exit);
        }

        public static string Build(bool save)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != WorkingScene) return $"ERROR: the open scene is '{scene.path}'. Open {WorkingScene} first.";

            GameObject plantsRoot = GameObject.Find("BioDome_Plants");
            if (plantsRoot == null) return "ERROR: BioDome_Plants not found.";
            int originals = plantsRoot.GetComponentsInChildren<PlantController>(true).Length;
            long before = Checksum(plantsRoot);
            int zonesBefore = Object.FindObjectsByType<PlantingZone>().Length;

            var log = new StringBuilder();
            string e = ConservatoryExpansion.Build();
            log.AppendLine("== Expansion (rear bays, floor, main path, lamps) ==").AppendLine(e);
            if (e.Contains("ERROR")) return log.ToString();
            string a = ExperimentGardenBuilder.Build(false);
            log.AppendLine("== Research wing ==").AppendLine(a);
            if (a.Contains("ERROR")) return log.ToString();
            string b = CollectionGardenBuilder.Build(false);
            log.AppendLine("== Collection beds ==").AppendLine(b);
            if (b.Contains("ERROR")) return log.ToString();
            log.AppendLine("== Established planting ==").AppendLine(EstablishAdditions());
            string ls = LandscapeBuilder.Build();
            log.AppendLine("== Landscape (clusters, vines, benches, lilies) ==").AppendLine(ls);
            if (ls.Contains("ERROR")) return log.ToString();
            string at = AtmosphereBuilder.Build();
            log.AppendLine("== Atmosphere ==").AppendLine(at);
            if (at.Contains("ERROR")) return log.ToString();
            string c = BuildNavigation();
            log.AppendLine("== Navigation ==").AppendLine(c);
            if (c.Contains("ERROR")) return log.ToString();

            long after = Checksum(plantsRoot);
            int zonesAfter = Object.FindObjectsByType<PlantingZone>().Length;
            int originalsAfter = plantsRoot.GetComponentsInChildren<PlantController>(true).Length;
            bool allZonesActive = true;
            foreach (PlantingZone z in Object.FindObjectsByType<PlantingZone>()) allZonesActive &= z.gameObject.activeInHierarchy;
            if (after != before || originalsAfter != originals || zonesAfter != zonesBefore || !allZonesActive)
                return log + $"ERROR: safety check failed (originals {originals}->{originalsAfter}, checksum {before}->{after}, zones {zonesBefore}->{zonesAfter}, all zones active {allZonesActive}). Not saved.";

            if (save) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            log.Insert(0, $"SAFETY OK: {originalsAfter} original plants unchanged (checksum {after}), {zonesAfter} planting zones present and active.{(save ? " Scene saved." : "")}\n");
            return log.ToString();
        }

        /// <summary>
        /// The 318 plants added to the original zones in an earlier phase (BioDome_PlantAdditions) start at 65 % growth instead of 12 %, so the original garden
        /// is not a field of seedlings. The 130 original Monstera under BioDome_Plants are not touched. Set StartGrowth01 back to -1 on a plant to undo.
        /// </summary>
        private static string EstablishAdditions()
        {
            GameObject add = GameObject.Find("BioDome_PlantAdditions");
            if (add == null) return "  (no BioDome_PlantAdditions root)";
            int n = 0;
            foreach (PlantController pc in add.GetComponentsInChildren<PlantController>(true))
            {
                var so = new SerializedObject(pc);
                so.FindProperty("startGrowth01").floatValue = 0.65f;
                so.ApplyModifiedPropertiesWithoutUndo();
                n++;
            }
            var manager = Object.FindFirstObjectByType<BioDome.Core.GreenhouseManager>();
            if (manager != null)
            {
                var mso = new SerializedObject(manager);
                mso.FindProperty("meshBuildsPerFrame").intValue = 4; // established plants swap to their growth meshes within a few frames of Play
                mso.ApplyModifiedPropertiesWithoutUndo();
            }
            return $"  {n} earlier-addition plants now start at 65 % growth (the 130 originals are unchanged); mesh builds per frame 4";
        }

        private static string BuildNavigation()
        {
            var cameraController = Object.FindFirstObjectByType<CameraController>();
            if (cameraController == null) return "ERROR: CameraController not found.";

            GameObject old = GameObject.Find(NavRoot);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(NavRoot);

            // Camera: an indoor overview from the entrance end looking down the length of the conservatory, confined to the building.
            var camSo = new SerializedObject(cameraController);
            camSo.FindProperty("defaultPivot").vector3Value = new Vector3(-2f, 1.2f, 0f);
            camSo.FindProperty("defaultDistance").floatValue = 18f;
            camSo.FindProperty("defaultYaw").floatValue = 90f;
            camSo.FindProperty("defaultPitch").floatValue = 13f;
            camSo.FindProperty("minDistance").floatValue = 0.8f;
            camSo.FindProperty("maxDistance").floatValue = 60f;
            camSo.FindProperty("minPitch").floatValue = 4f;
            camSo.FindProperty("maxPitch").floatValue = 80f;
            camSo.FindProperty("pivotMin").vector3Value = new Vector3(-19.5f, 0f, -11.7f);
            camSo.FindProperty("pivotMax").vector3Value = new Vector3(47.5f, 8f, 11.7f);
            camSo.FindProperty("panSpeed").floatValue = 0.0015f;
            camSo.FindProperty("keyPanSpeed").floatValue = 0.5f;
            camSo.FindProperty("keyMinSpeed").floatValue = 7f;
            camSo.FindProperty("wallMargin").floatValue = 0.35f;
            camSo.FindProperty("confineToBuilding").boolValue = true;
            camSo.FindProperty("buildingX").vector2Value = new Vector2(-19.4f, ConservatoryExpansion.NewEastX - 0.6f);
            camSo.FindProperty("buildingHalfWidth").floatValue = ConservatoryExpansion.HalfWidth;
            camSo.FindProperty("eaveHeight").floatValue = 7f;
            camSo.FindProperty("ridgeHeight").floatValue = 12f;
            camSo.ApplyModifiedPropertiesWithoutUndo();

            // The footer hint lists the real controls.
            foreach (var t in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include))
            {
                if (t.text != null && t.text.Contains("Orbit") && t.text.Contains("Pan") && t.text.Contains("Zoom"))
                {
                    t.text = "Drag / Middle-drag: Pan    Right-drag: Orbit    Scroll: Zoom    WASD: Move (Shift = fast)    Q E: Rotate    Space / C: Up / Down    Double-click floor: Fly there    R: Reset";
                    t.enableAutoSizing = true;
                    t.fontSizeMin = 10f;
                    t.fontSizeMax = t.fontSize;
                    EditorUtility.SetDirty(t);
                }
            }

            var shortcuts = root.AddComponent<ViewShortcuts>();
            var views = new List<(string label, int row, Vector3 point, float dist, float yaw, float pitch, Color accent, bool overview)>();
            var neutral = new Color(0.75f, 0.88f, 0.75f);
            var green = new Color(0.55f, 0.85f, 0.5f);
            views.Add(("Overview", 0, Vector3.zero, 0, 0, 0, neutral, true));
            views.Add(("Garden", 0, new Vector3(-6f, 0.6f, 5.5f), 10f, 0f, 28f, neutral, false));
            views.Add(("Fountain", 0, new Vector3(ConservatoryExpansion.PlazaX, 1.0f, 0f), 11f, 90f, 26f, new Color(0.45f, 0.75f, 0.95f), false));
            views.Add(("Collection N", 0, new Vector3(CollectionGardenBuilder.RoomX, 0.6f, CollectionGardenBuilder.RoomZ), 10f, 0f, 30f, green, false));
            views.Add(("Collection S", 0, new Vector3(CollectionGardenBuilder.RoomX, 0.6f, -CollectionGardenBuilder.RoomZ), 10f, 180f, 30f, green, false));
            views.Add(("Research", 0, new Vector3(40f, 0.9f, 0f), 12f, 90f, 26f, new Color(0.9f, 0.85f, 0.4f), false));
            var shortLabels = new Dictionary<ExperimentalCondition, string>
            {
                { ExperimentalCondition.Control, "Control" }, { ExperimentalCondition.HeatStress, "Heat" }, { ExperimentalCondition.LowHumidity, "Humidity" },
                { ExperimentalCondition.LowLight, "Light" }, { ExperimentalCondition.WaterStress, "Water" }
            };
            var order = new[] { ExperimentalCondition.Control, ExperimentalCondition.HeatStress, ExperimentalCondition.LowHumidity, ExperimentalCondition.LowLight, ExperimentalCondition.WaterStress };
            var placements = new Dictionary<ExperimentalCondition, (Vector3 centre, Vector3 outward, Color accent)>();
            foreach (var p in ExperimentGardenBuilder.PodPlacements()) placements[p.condition] = (p.centre, p.outward, p.accent);
            foreach (ExperimentalCondition cnd in order)
            {
                var pl = placements[cnd];
                float yaw = Mathf.Atan2(pl.outward.x, pl.outward.z) * Mathf.Rad2Deg;
                views.Add((shortLabels[cnd], 1, pl.centre + Vector3.up * 0.8f, 6.2f, yaw, 26f, pl.accent, false));
            }

            var so = new SerializedObject(shortcuts);
            so.FindProperty("cameraController").objectReferenceValue = cameraController;
            SerializedProperty list = so.FindProperty("viewPoints");
            list.arraySize = views.Count;
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                Transform target = null;
                if (!v.overview)
                {
                    var go = new GameObject("VP_" + v.label.Replace(" ", ""));
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = v.point;
                    target = go.transform;
                }
                SerializedProperty e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("label").stringValue = v.label;
                e.FindPropertyRelative("row").intValue = v.row;
                e.FindPropertyRelative("target").objectReferenceValue = target;
                e.FindPropertyRelative("distance").floatValue = v.dist;
                e.FindPropertyRelative("yaw").floatValue = v.yaw;
                e.FindPropertyRelative("pitch").floatValue = v.pitch;
                e.FindPropertyRelative("accent").colorValue = v.accent;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return $"{views.Count} view shortcuts; camera indoor overview from the entrance end (pivot (-2, 1.2, 0), distance 18, pitch 13), camera confined to the building, yaw 15, pitch 38.";
        }

        private static long Checksum(GameObject root)
        {
            long h = 17;
            foreach (PlantController p in root.GetComponentsInChildren<PlantController>(true))
            {
                Transform t = p.transform;
                h = h * 31 + p.name.GetHashCode();
                h = h * 31 + Mathf.RoundToInt(t.position.x * 1000f);
                h = h * 31 + Mathf.RoundToInt(t.position.y * 1000f);
                h = h * 31 + Mathf.RoundToInt(t.position.z * 1000f);
                h = h * 31 + Mathf.RoundToInt(t.eulerAngles.y * 100f);
                h = h * 31 + Mathf.RoundToInt(t.localScale.x * 1000f);
            }
            return h;
        }
    }
}
