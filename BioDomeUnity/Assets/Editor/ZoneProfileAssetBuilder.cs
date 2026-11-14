using System.IO;
using BioDome.Environment;
using UnityEditor;
using UnityEngine;

namespace BioDome.EditorTools
{
    /// <summary>Creates (or updates in place) the five experimental ZoneProfile assets. Touches no scene.</summary>
    public static class ZoneProfileAssetBuilder
    {
        public const string Folder = "Assets/ScriptableObjects/Zones";

        public static string PathFor(ExperimentalCondition condition) => $"{Folder}/Zone_{condition}.asset";

        [MenuItem("BioDome/Zones/Create Experimental Zone Profiles")]
        public static void CreateAll()
        {
            Directory.CreateDirectory(Folder);
            foreach (ExperimentalCondition condition in System.Enum.GetValues(typeof(ExperimentalCondition)))
            {
                string path = PathFor(condition);
                var profile = AssetDatabase.LoadAssetAtPath<ZoneProfile>(path);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<ZoneProfile>();
                    ZoneProfilePresets.Fill(profile, condition);
                    AssetDatabase.CreateAsset(profile, path);
                }
                else
                {
                    ZoneProfilePresets.Fill(profile, condition);
                    EditorUtility.SetDirty(profile);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"BioDome: experimental zone profiles ready in {Folder}.");
        }
    }
}
