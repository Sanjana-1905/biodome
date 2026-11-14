using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Window for the safe mature-growth preview: shows every plant at full growth as a temporary overlay in the Scene view and Game view,
    /// without changing the scene, the plants or the simulation. Open it with BioDome > Plants > Mature Growth Preview.
    /// Closing the window switches the preview off.
    /// </summary>
    public class PlantMaturePreviewWindow : EditorWindow
    {
        private List<PlantMaturePreview.ViewPreset> views = new List<PlantMaturePreview.ViewPreset>();
        private string[] viewNames = new string[0];
        private int selectedView;
        private Vector2 scroll;
        private string message = "";

        [MenuItem("BioDome/Plants/Mature Growth Preview")]
        public static void Open()
        {
            var window = GetWindow<PlantMaturePreviewWindow>("Mature Preview");
            window.minSize = new Vector2(380f, 440f);
            window.Show();
        }

        private void OnEnable()
        {
            PlantMaturePreview.StateChanged += Repaint;
            RefreshViews();
        }

        private void OnDisable()
        {
            PlantMaturePreview.StateChanged -= Repaint;
            PlantMaturePreview.SetActive(false); // closing the window restores the normal view
        }

        private void RefreshViews()
        {
            views = PlantMaturePreview.BuildViews();
            viewNames = new string[views.Count];
            for (int i = 0; i < views.Count; i++) viewNames[i] = views[i].Name;
            selectedView = Mathf.Clamp(selectedView, 0, Mathf.Max(0, views.Count - 1));
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            Scene scene = SceneManager.GetActiveScene();
            EditorGUILayout.LabelField("Open scene", $"{scene.name}{(scene.isDirty ? " *" : "")}");

            EditorGUILayout.HelpBox(
                "Draws every plant at full growth as a temporary overlay in the Scene and Game views. Nothing is changed: the scene is not " +
                "modified or saved, plant health, moisture, age and growth are untouched, and the simulation clock does not move. The live " +
                "plants keep their normal size underneath. Closing this window, entering Play Mode or opening another scene switches it off.",
                MessageType.Info);

            DrawToggle();
            DrawViews();
            DrawScreenshots();

            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void DrawToggle()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                bool wanted = GUILayout.Toggle(PlantMaturePreview.Active, PlantMaturePreview.Active ? "Mature preview: ON" : "Mature preview: OFF", "Button", GUILayout.Height(34));
                if (wanted != PlantMaturePreview.Active)
                {
                    if (!PlantMaturePreview.SetActive(wanted)) message = "The preview only works in Edit Mode.";
                    else message = wanted ? "" : "Preview off. The scene looks exactly as it did.";
                    RefreshViews();
                }

                EditorGUI.BeginChangeCheck();
                float growth = EditorGUILayout.Slider("Preview growth", PlantMaturePreview.Growth * 100f, 10f, 100f) / 100f;
                if (EditorGUI.EndChangeCheck()) PlantMaturePreview.Growth = growth;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Stop Play Mode to use the preview.", MessageType.Warning);

            if (PlantMaturePreview.Active)
            {
                EditorGUILayout.LabelField("Plants shown", PlantMaturePreview.PlantCount.ToString());
                EditorGUILayout.LabelField("Temporary meshes", PlantMaturePreview.MeshCount.ToString());
                EditorGUILayout.LabelField("Triangles drawn (overlay)", PlantMaturePreview.TriangleCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
                if (!PlantMaturePreview.SceneUntouched)
                    EditorGUILayout.HelpBox("The scene is marked modified. The preview does not edit anything, so this came from something else. Check before saving.", MessageType.Warning);
                else
                    EditorGUILayout.LabelField("Scene modified by preview", "no");
            }
        }

        private void DrawViews()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Camera angles (Scene view)", EditorStyles.boldLabel);
            if (views.Count == 0)
            {
                EditorGUILayout.HelpBox("No planting zones found in the open scene, so there are no preset angles.", MessageType.None);
                if (GUILayout.Button("Look again")) RefreshViews();
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(views[0].Name)) Go(views[0]);
                if (GUILayout.Button(views[1].Name)) Go(views[1]);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                selectedView = EditorGUILayout.Popup(selectedView, viewNames);
                if (GUILayout.Button("Go", GUILayout.Width(50))) Go(views[selectedView]);
                if (GUILayout.Button("Next", GUILayout.Width(60)))
                {
                    selectedView = (selectedView + 1) % views.Count;
                    Go(views[selectedView]);
                }
            }
            EditorGUILayout.LabelField("The Game view shows the preview too, from the scene's own camera.", EditorStyles.miniLabel);
        }

        private void Go(PlantMaturePreview.ViewPreset view)
        {
            message = PlantMaturePreview.GoTo(view) ? $"Scene view moved to: {view.Name}" : "Open a Scene view first (Window > General > Scene).";
        }

        private void DrawScreenshots()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Screenshots", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!PlantMaturePreview.Active || views.Count == 0))
            {
                if (GUILayout.Button("Save PNGs of every angle..."))
                {
                    string folder = EditorUtility.SaveFolderPanel("Save mature preview PNGs", Directory.GetCurrentDirectory(), "MaturePreview");
                    if (!string.IsNullOrEmpty(folder))
                    {
                        int count = PlantMaturePreview.SaveScreenshots(folder, views);
                        message = $"Saved {count} PNGs to {folder}";
                    }
                }
            }
            if (!PlantMaturePreview.Active) EditorGUILayout.LabelField("Turn the preview on first.", EditorStyles.miniLabel);
        }
    }
}
