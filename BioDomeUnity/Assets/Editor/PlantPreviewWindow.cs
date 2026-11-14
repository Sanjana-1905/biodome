using System.Collections.Generic;
using System.IO;
using BioDome.Plants;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Editor-only window to inspect every species at 25%, 50%, 75% and 100% growth (plus the 12% greenhouse start).
    /// It renders in Unity's isolated preview scene: the open scene, the simulation and all assets stay untouched.
    /// Open it with BioDome > Plants > Plant Preview Window.
    /// </summary>
    public class PlantPreviewWindow : EditorWindow
    {
        public static readonly float[] Stages = { 0.12f, 0.25f, 0.50f, 0.75f, 1.00f };
        private const string PrefabFolder = "Assets/Prefabs/Plants";
        private const int CellWidth = 230;
        private const int CellHeight = 260;

        private sealed class Row
        {
            public GameObject Prefab;
            public string Label;
            public PlantPreviewRenderer.Result[] Results = new PlantPreviewRenderer.Result[Stages.Length];
        }

        private readonly List<Row> rows = new List<Row>();
        private Vector2 scroll;
        private int variant;
        private int detail = 1;
        private bool showInitial = true;

        [MenuItem("BioDome/Plants/Plant Preview Window")]
        public static void Open()
        {
            var window = GetWindow<PlantPreviewWindow>("Plant Preview");
            window.minSize = new Vector2(520f, 360f);
            window.Show();
        }

        private void OnEnable() => EditorApplication.delayCall += Refresh;
        private void OnDisable() => ClearImages();
        private void OnDestroy() => ClearImages();

        /// <summary>Species prefabs (one per species; the Monstera growth-stage prefabs are skipped).</summary>
        public static List<GameObject> FindSpeciesPrefabs()
        {
            var list = new List<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.EndsWith("_Young") || name.EndsWith("_Medium") || name.EndsWith("_Mature")) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponent<PlantGenerator>() != null) list.Add(prefab);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        private void Refresh()
        {
            if (this == null) return;
            ClearImages();

            List<GameObject> prefabs = FindSpeciesPrefabs();
            try
            {
                for (int p = 0; p < prefabs.Count; p++)
                {
                    EditorUtility.DisplayProgressBar("Plant Preview", $"Rendering {prefabs[p].name}...", p / (float)Mathf.Max(1, prefabs.Count));
                    var row = new Row { Prefab = prefabs[p], Label = prefabs[p].name };
                    var controller = prefabs[p].GetComponent<PlantController>();
                    if (controller != null && controller.Data != null) row.Label = $"{controller.Data.DisplayName}  ({prefabs[p].name})";

                    for (int s = 0; s < Stages.Length; s++)
                        row.Results[s] = PlantPreviewRenderer.Render(prefabs[p], Stages[s], variant, CellWidth, CellHeight, detail);
                    rows.Add(row);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Repaint();
        }

        private void ClearImages()
        {
            foreach (Row row in rows)
            {
                foreach (PlantPreviewRenderer.Result r in row.Results)
                    if (r != null && r.Image != null) DestroyImmediate(r.Image);
            }
            rows.Clear();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                GUILayout.Label("Look", GUILayout.Width(32));
                variant = EditorGUILayout.IntSlider(variant, 0, 3, GUILayout.Width(150));
                GUILayout.Label("Mesh detail", GUILayout.Width(70));
                detail = EditorGUILayout.IntPopup(detail, new[] { "1 (greenhouse)", "2", "3" }, new[] { 1, 2, 3 }, GUILayout.Width(110));
                showInitial = GUILayout.Toggle(showInitial, "12% start", EditorStyles.toolbarButton, GUILayout.Width(64));
                bool changed = EditorGUI.EndChangeCheck();

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Save PNGs...", EditorStyles.toolbarButton, GUILayout.Width(90))) SavePngs();
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)) || changed) Refresh();
            }

            EditorGUILayout.HelpBox(
                "Previews are rendered in an isolated preview scene. The open scene, the simulation and your assets are not changed. " +
                "Mesh detail 1 is what the greenhouse uses.", MessageType.None);

            if (rows.Count == 0)
            {
                EditorGUILayout.LabelField($"No species prefabs found in {PrefabFolder}.");
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Row row in rows)
            {
                EditorGUILayout.LabelField(row.Label, EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int s = 0; s < Stages.Length; s++)
                    {
                        if (s == 0 && !showInitial) continue;
                        DrawCell(row.Results[s], Stages[s]);
                    }
                }
                GUILayout.Space(8);
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawCell(PlantPreviewRenderer.Result result, float growth)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(CellWidth)))
            {
                Rect rect = GUILayoutUtility.GetRect(CellWidth, CellHeight, GUILayout.Width(CellWidth), GUILayout.Height(CellHeight));
                if (result != null && result.Image != null) GUI.DrawTexture(rect, result.Image, ScaleMode.ScaleToFit);
                else EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));

                string info = result == null
                    ? $"{growth * 100f:0}%"
                    : $"{growth * 100f:0}%   {result.Triangles.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} tris   {result.Size.y:0.00} m tall";
                EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
            }
        }

        private void SavePngs()
        {
            string folder = EditorUtility.SaveFolderPanel("Save plant preview PNGs", Directory.GetCurrentDirectory(), "PlantPreviews");
            if (string.IsNullOrEmpty(folder)) return;

            int saved = 0;
            foreach (Row row in rows)
            {
                for (int s = 0; s < Stages.Length; s++)
                {
                    PlantPreviewRenderer.Result r = row.Results[s];
                    if (r == null || r.Image == null) continue;
                    File.WriteAllBytes(Path.Combine(folder, $"{row.Prefab.name}_g{Mathf.RoundToInt(Stages[s] * 100f):000}.png"), r.Image.EncodeToPNG());
                    saved++;
                }
            }
            Debug.Log($"BioDome: saved {saved} preview PNGs to {folder}");
        }
    }
}
