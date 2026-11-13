using BioDome.Plants;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>
    /// Inspector for plant generators: rebuilds the mesh when a shape parameter changes, and adds
    /// Rebuild, Randomize Seed and Validate buttons. Colour changes need no rebuild.
    /// </summary>
    [CustomEditor(typeof(PlantGenerator), true)]
    public class PlantGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var generator = (PlantGenerator)target;

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                generator.Rebuild(false); // only regenerates if a shape parameter actually changed
                EditorUtility.SetDirty(generator);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild"))
                {
                    Undo.RecordObject(generator, "Rebuild Plant");
                    generator.Rebuild(true);
                }
                if (GUILayout.Button("Randomize Seed"))
                {
                    Undo.RecordObject(generator, "Randomize Plant Seed");
                    generator.RandomizeSeed();
                    generator.Rebuild(false);
                }
            }
            if (GUILayout.Button("Validate Mesh")) PlantMeshValidator.ValidateAndLog(generator.gameObject);

            if (PrefabUtility.IsPartOfPrefabAsset(generator))
            {
                EditorGUILayout.HelpBox(
                    "Editing a prefab asset here creates a mesh that is not saved. Regenerate prefabs with BioDome > Plants > Generate Monstera Prefabs.",
                    MessageType.Warning);
            }
        }
    }
}
