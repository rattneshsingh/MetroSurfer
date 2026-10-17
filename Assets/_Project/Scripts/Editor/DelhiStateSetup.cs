using Runner.Config;
using Runner.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Runner.EditorTools
{
    /// <summary>
    /// Creates the Delhi StateConfig, maps obstacle kinds to their models, and plugs the
    /// state into the open scene. Re-runnable: it refreshes the mapping in place, so the
    /// asset's GUID and every reference to it survive.
    /// </summary>
    /// <remarks>
    /// The mapping is data on the state, not code in gameplay: a second state is a second
    /// asset with its own table, and nothing under Scripts/ changes.
    /// </remarks>
    public static class DelhiStateSetup
    {
        private const string StateFolder = "Assets/_Project/ScriptableObjects/States";
        private const string StatePath = StateFolder + "/Delhi.asset";
        private const string TrainKit = "Assets/Kenney/TrainKit/";

        /// <summary>Kind to model. Kinds not listed keep their generated shape.</summary>
        private static readonly (ObstacleKind Kind, string Model)[] Mapping =
        {
            (ObstacleKind.Train, TrainKit + "train-electric-subway-a.fbx"),
            (ObstacleKind.Truck, TrainKit + "train-carriage-container-blue.fbx"),
            (ObstacleKind.MovingTruck, TrainKit + "train-carriage-container-red.fbx"),
            (ObstacleKind.OncomingTruck, TrainKit + "train-diesel-a.fbx"),
            (ObstacleKind.StoppingBus, TrainKit + "train-tram-modern.fbx"),
        };

        [MenuItem("Runner/Set Up Delhi State", priority = 30)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("Stop Play mode first — changes made while playing are thrown away.");
                return;
            }

            StateConfig state = LoadOrCreateState();

            SerializedObject serialized = new SerializedObject(state);
            serialized.FindProperty("_displayName").stringValue = "Delhi";

            SerializedProperty art = serialized.FindProperty("_obstacleArt");
            art.arraySize = 0;

            int mapped = 0;
            for (int i = 0; i < Mapping.Length; i++)
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Mapping[i].Model);
                if (model == null)
                {
                    Debug.LogWarning($"Model not found at {Mapping[i].Model} — {Mapping[i].Kind} keeps its grey shape.");
                    continue;
                }

                art.arraySize++;
                SerializedProperty entry = art.GetArrayElementAtIndex(art.arraySize - 1);
                entry.FindPropertyRelative("Kind").intValue = (int)Mapping[i].Kind;
                entry.FindPropertyRelative("Visual").objectReferenceValue = model;
                mapped++;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(state);
            AssetDatabase.SaveAssets();

            AssignToScene(state);

            Debug.Log($"Delhi state ready: {mapped} of {Mapping.Length} obstacle kinds have models. Press Play.");
        }

        private static StateConfig LoadOrCreateState()
        {
            StateConfig existing = AssetDatabase.LoadAssetAtPath<StateConfig>(StatePath);
            if (existing != null)
            {
                return existing;
            }

            if (!AssetDatabase.IsValidFolder(StateFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/ScriptableObjects", "States");
            }

            StateConfig created = ScriptableObject.CreateInstance<StateConfig>();
            AssetDatabase.CreateAsset(created, StatePath);
            return created;
        }

        private static void AssignToScene(StateConfig state)
        {
            GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
            if (gameManager == null)
            {
                Debug.LogWarning("No GameManager in the open scene. Open Run.unity and run this again " +
                                 "to plug Delhi in.");
                return;
            }

            SerializedObject serialized = new SerializedObject(gameManager);
            serialized.FindProperty("_state").objectReferenceValue = state;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(gameManager.gameObject.scene);
            EditorSceneManager.SaveScene(gameManager.gameObject.scene);
        }
    }
}
