using BioDome.Plants;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Window for populating the greenhouse (BioDome > Plants > Populate Planting Zones...).
    /// It shows the Plant Population Settings asset (density, spacing, initial growth, variation, seed, cap, species
    /// per zone) and runs <see cref="PlantPopulationBuilder"/>. The Low, Medium and High buttons set the preset and populate.
    /// </summary>
    public class PlantPlacementWindow : EditorWindow
    {
        private Editor settingsEditor;
        private Vector2 scroll;

        [MenuItem("BioDome/Plants/Populate Planting Zones...")]
        public static void Open()
        {
            GetWindow<PlantPlacementWindow>("Plant Population").minSize = new Vector2(380f, 460f);
        }

        [MenuItem("BioDome/Plants/Clear Placed Plants")]
        public static void ClearPlacedPlants()
        {
            if (PlantPopulationBuilder.ClearExisting()) Debug.Log("BioDome: placed plants removed.");
            else Debug.Log("BioDome: no placed plants to remove in the open scene.");
        }

        private void OnDisable()
        {
            if (settingsEditor != null) DestroyImmediate(settingsEditor);
        }

        private void OnGUI()
        {
            PlantPopulationSettings settings = PlantPopulationBuilder.LoadOrCreateSettings();
            if (settingsEditor == null || settingsEditor.target != settings)
            {
                if (settingsEditor != null) DestroyImmediate(settingsEditor);
                settingsEditor = Editor.CreateEditor(settings);
            }

            EditorGUILayout.HelpBox(
                "Open BioDome_Main first. All plants start at the same growth (one cohort). Plant counts follow bed size, " +
                "mature canopy width and spacing. Running this again replaces the plants; it never duplicates them.",
                MessageType.Info);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            settingsEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Low")) Run(settings, DensityPreset.Low);
                if (GUILayout.Button("Medium")) Run(settings, DensityPreset.Medium);
                if (GUILayout.Button("High")) Run(settings, DensityPreset.High);
            }
            if (GUILayout.Button("Populate With Current Settings", GUILayout.Height(28f))) PlantPopulationBuilder.Populate(settings);
            if (GUILayout.Button("Clear Placed Plants")) ClearPlacedPlants();
        }

        private static void Run(PlantPopulationSettings settings, DensityPreset preset)
        {
            settings.densityPreset = preset;
            EditorUtility.SetDirty(settings);
            PlantPopulationBuilder.Populate(settings);
        }
    }
}
