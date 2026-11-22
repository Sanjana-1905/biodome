using System.Collections.Generic;
using System.Linq;
using System.Text;
using BioDome.Core;
using BioDome.Experiment;
using BioDome.Irrigation;
using BioDome.Plants;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BioDome.EditorTools.Tests
{
    /// <summary>Read-only audit of BioDome_Working: structure counts, missing scripts, broken (destroyed) object references. Changes nothing.</summary>
    public static class SceneIntegrityCheck
    {
        public static void RunBatch()
        {
            var sb = new StringBuilder();
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/BioDome_Working.unity");
            var roots = scene.GetRootGameObjects();
            sb.AppendLine("ROOTS: " + string.Join(", ", roots.Select(r => r.name + "(" + r.transform.childCount + ")")));
            var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
            sb.AppendLine("GAMEOBJECTS: " + all.Count);
            int missing = all.Sum(g => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(g));
            sb.AppendLine("MISSING SCRIPTS: " + missing);
            var broken = new List<string>();
            foreach (var g in all)
                foreach (var c in g.GetComponents<Component>())
                {
                    if (c == null) continue;
                    var so = new SerializedObject(c); var it = so.GetIterator();
                    while (it.NextVisible(true))
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null && !it.objectReferenceEntityIdValue.Equals(default(UnityEngine.EntityId)))
                            broken.Add(g.name + "/" + c.GetType().Name + "." + it.propertyPath);
                }
            sb.AppendLine("BROKEN REFERENCES: " + broken.Count + (broken.Count > 0 ? " e.g. " + string.Join("; ", broken.Take(8)) : ""));
            var plants = Object.FindObjectsByType<PlantController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine("PLANTS: " + plants.Length + " | display " + plants.Count(p => p.ZoneProfile == null) + " | pods " + plants.Count(p => p.ZoneProfile != null));
            sb.AppendLine("BY SPECIES (display): " + string.Join(", ", plants.Where(p => p.ZoneProfile == null).GroupBy(p => p.Data != null ? p.Data.SpeciesId : "NULL").OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count())));
            sb.AppendLine("PODS: " + Object.FindObjectsByType<ExperimentPod>(FindObjectsSortMode.None).Length + " | ExperimentGarden " + Object.FindObjectsByType<ExperimentGarden>(FindObjectsSortMode.None).Length + " | ComparisonPanel " + Object.FindObjectsByType<ExperimentComparisonPanel>(FindObjectsSortMode.None).Length);
            sb.AppendLine("IRRIGATION CONTROLLERS: " + Object.FindObjectsByType<IrrigationController>(FindObjectsSortMode.None).Length);
            sb.AppendLine("CAMERAS: " + Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length + " | CameraController " + Object.FindObjectsByType<BioDome.CameraSystem.CameraController>(FindObjectsSortMode.None).Length);
            sb.AppendLine("FOG: " + RenderSettings.fog);
            var names = all.Select(g => g.name).ToList();
            foreach (string key in new[] { "Fountain", "Zone", "Bed", "Pod", "Research", "Glass", "Path" })
                sb.AppendLine($"NAME~{key}: {names.Count(n => n.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0)} e.g. " + string.Join(", ", names.Where(n => n.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0 && !n.Contains("(")).Distinct().Take(10)));
            System.IO.File.WriteAllText("C:/Projects/biodome/Docs/Verification/scene_integrity.txt", sb.ToString());
            Debug.Log("SCENEAUDIT\n" + sb);
            EditorApplication.Exit(0);
        }
    }
}
