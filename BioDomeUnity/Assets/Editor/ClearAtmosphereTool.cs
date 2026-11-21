using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BioDome.EditorTools
{
    /// <summary>
    /// One-shot menu command: removes global fog and the ground mist banks and lifts the ambient light a little.
    /// Runs inside the Editor on the open scene (so it is never overwritten by the Editor's own save). Touches only render settings
    /// and the MistBank_* objects; no plants, zones, pods, beds or simulation objects are changed.
    /// </summary>
    public static class ClearAtmosphereTool
    {
        [MenuItem("BioDome/Visual/Apply Clear Atmosphere (no fog)")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("BioDome", "Stop Play Mode first, then run this again.", "OK");
                return;
            }

            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.78f, 0.82f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.66f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.40f, 0.42f, 0.31f);

            int banks = 0;
            GameObject root = GameObject.Find(AtmosphereBuilder.RootName);
            if (root != null)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith("MistBank_") && t.gameObject.activeSelf)
                    {
                        Undo.RecordObject(t.gameObject, "Disable mist bank");
                        t.gameObject.SetActive(false);
                        banks++;
                    }
                }
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[BioDome] Clear atmosphere applied: fog off, {banks} mist banks disabled, ambient lifted. Scene '{scene.name}' saved.");
        }
    }
}
