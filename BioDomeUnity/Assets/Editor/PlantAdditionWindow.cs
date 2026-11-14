using BioDome.Plants;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BioDome.EditorTools
{
    /// <summary>
    /// The one place to configure and run plant additions: pick a plan, edit its species, quantities and zones, check the
    /// result with a dry run, then add. Original plants are never cleared or regenerated; new plants live under their own root.
    /// Open it with BioDome > Plants > Plant Additions Window.
    /// </summary>
    public class PlantAdditionWindow : EditorWindow
    {
        private PlantAdditionPlan plan;
        private bool replacePrevious;
        private string report = "Nothing run yet. Use 'Dry run' to see what would be added.";
        private MessageType reportType = MessageType.None;
        private Vector2 scroll;
        private Vector2 reportScroll;
        private Editor planEditor;

        [MenuItem("BioDome/Plants/Plant Additions Window")]
        public static void Open()
        {
            var window = GetWindow<PlantAdditionWindow>("Plant Additions");
            window.minSize = new Vector2(520f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            if (plan == null) plan = AssetDatabase.LoadAssetAtPath<PlantAdditionPlan>(PlantSpeciesAssetBuilder.LushPlanPath)
                                     ?? AssetDatabase.LoadAssetAtPath<PlantAdditionPlan>(PlantSpeciesAssetBuilder.PlanPath);
        }

        private void OnDisable()
        {
            if (planEditor != null) DestroyImmediate(planEditor);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawSceneInfo();
            DrawSetup();
            DrawPlanPicker();
            DrawRunButtons();
            DrawReport();
            DrawPlanEditor();

            EditorGUILayout.EndScrollView();
        }

        private void DrawSceneInfo()
        {
            Scene scene = SceneManager.GetActiveScene();
            EditorGUILayout.LabelField("Open scene", $"{scene.name}{(scene.isDirty ? " *" : "")}");
            if (scene.name == "BioDome_Main")
            {
                EditorGUILayout.HelpBox(
                    "You are in BioDome_Main. Additions never touch the original plants, but it is safest to try them on a copy first: " +
                    "File > Save As... (for example BioDome_Diverse), then run this tool there.", MessageType.Info);
            }
            if (EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Stop Play Mode before adding or removing plants.", MessageType.Warning);
        }

        private void DrawSetup()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("1. Species assets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates PlantData, prefabs and materials for every species. Nothing existing is overwritten (the earlier Bird of Paradise " +
                "and Banana prefabs are upgraded to the improved shapes, keeping their PlantData). Monstera is never touched.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create / upgrade species assets")) PlantSpeciesAssetBuilder.CreateAssets();
                if (GUILayout.Button("Create lush preset plan"))
                {
                    plan = PlantSpeciesAssetBuilder.LoadOrCreateLushPlan();
                    Selection.activeObject = plan;
                }
                if (GUILayout.Button("Open preview window")) PlantPreviewWindow.Open();
            }
        }

        private void DrawPlanPicker()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("2. Plan (species, quantities, zones)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            plan = (PlantAdditionPlan)EditorGUILayout.ObjectField("Plan", plan, typeof(PlantAdditionPlan), false);
            if (EditorGUI.EndChangeCheck() && planEditor != null) { DestroyImmediate(planEditor); planEditor = null; }
        }

        private void DrawRunButtons()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("3. Add to the open scene", EditorStyles.boldLabel);
            replacePrevious = EditorGUILayout.ToggleLeft(
                "Replace plants added earlier (off = keep them and skip species that are already present)", replacePrevious);

            using (new EditorGUI.DisabledScope(plan == null || EditorApplication.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Dry run (no changes)")) Execute(true);
                    if (GUILayout.Button("Add species to selected zones")) Execute(false);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Refresh added species meshes"))
                    {
                        report = PlantAdditionBuilder.RefreshMeshes(plan);
                        reportType = MessageType.Info;
                    }
                    if (GUILayout.Button("Remove added species")) PlantAdditionBuilder.RemoveAdded();
                }
            }
        }

        private void Execute(bool dryRun)
        {
            if (!dryRun)
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!EditorUtility.DisplayDialog("Add species",
                        $"Add the planned species to the open scene '{scene.name}'?\n\n" +
                        "The original plants are not cleared, moved or regenerated. New plants go under a separate root, " +
                        "and the scene is marked modified but not saved.", "Add", "Cancel"))
                    return;
            }

            PlantAdditionBuilder.Report result = PlantAdditionBuilder.Run(plan, new PlantAdditionBuilder.Options { DryRun = dryRun, ReplacePrevious = replacePrevious });
            report = result.Text;
            reportType = result.Error != null || !result.OriginalsUnchanged ? MessageType.Error : (result.HitCap ? MessageType.Warning : MessageType.Info);
            if (result.Error != null || !result.OriginalsUnchanged) Debug.LogError(report);
            else Debug.Log("BioDome: " + report);
        }

        private void DrawReport()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.MinHeight(110), GUILayout.MaxHeight(220));
            EditorGUILayout.HelpBox(report, reportType == MessageType.None ? MessageType.None : reportType);
            EditorGUILayout.EndScrollView();
        }

        private void DrawPlanEditor()
        {
            if (plan == null) return;
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Plan contents (edit here; changes save to the asset)", EditorStyles.boldLabel);
            if (planEditor == null) planEditor = Editor.CreateEditor(plan);
            planEditor.OnInspectorGUI();
        }
    }
}
