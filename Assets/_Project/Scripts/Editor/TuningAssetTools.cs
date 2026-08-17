using Runner.Config;
using UnityEditor;
using UnityEngine;

namespace Runner.EditorTools
{
    /// <summary>
    /// Refreshes the tuning assets from their code defaults without deleting them.
    /// </summary>
    /// <remarks>
    /// The scene builder deliberately reuses existing assets so a rebuild never discards
    /// tuning. The cost is that changing a default in code has no effect on an asset that
    /// already exists, which is a genuinely confusing failure — new obstacle formations simply
    /// never appear and nothing reports a problem.
    /// <para>
    /// CopySerialized rather than delete-and-recreate: deleting changes the asset's GUID, which
    /// would silently break every scene reference pointing at it.
    /// </para>
    /// </remarks>
    public static class TuningAssetTools
    {
        private const string Folder = "Assets/_Project/ScriptableObjects";

        [MenuItem("Runner/Reset Tuning Assets to Defaults", priority = 20)]
        public static void ResetTuningAssets()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Reset tuning assets?",
                "This overwrites ObstacleTable, IntroSequence and DifficultyCurve with the " +
                "defaults defined in code.\n\nAny values you have tuned on those assets will be lost. " +
                "Scene references are preserved.",
                "Reset",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            int refreshed = 0;
            refreshed += Refresh($"{Folder}/ObstacleTable.asset", ObstacleTable.CreateDefault) ? 1 : 0;
            refreshed += Refresh($"{Folder}/IntroSequence.asset", IntroSequence.CreateDefault) ? 1 : 0;
            refreshed += Refresh($"{Folder}/DifficultyCurve.asset", DifficultyCurve.CreateDefault) ? 1 : 0;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Reset {refreshed} tuning asset(s) to code defaults. Press Play to see the change.");
        }

        private static bool Refresh<T>(string path, System.Func<T> createDefault) where T : ScriptableObject
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                return false;
            }

            T fresh = createDefault();
            string keepName = existing.name;

            // Copies the serialized fields in place, so the asset keeps its GUID and every
            // reference to it stays intact.
            EditorUtility.CopySerialized(fresh, existing);
            existing.name = keepName;

            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            return true;
        }
    }
}
