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
        private const string CarKit = "Assets/Kenney/CarKit/";
        private const string Roads = "Assets/Kenney/CityKitRoads/";
        private const string Rgsdev = "Assets/Rgsdev/LowPolyVehicles/";
        private const string Commercial = "Assets/Kenney/CityKitCommercial/";

        /// <summary>
        /// Kinds with no fitting model are repainted instead. Things you must roll under get
        /// hazard stripes so they read at a glance; pits stay near-black because a hole should
        /// look like a hole.
        /// </summary>
        private static readonly (ObstacleKind Kind, Color Paint, bool Stripes)[] Paints =
        {
            (ObstacleKind.High, new Color(1f, 0.80f, 0.12f), true),
            (ObstacleKind.Underpass, new Color(0.95f, 0.55f, 0.15f), true),
            (ObstacleKind.Ramp, new Color(0.66f, 0.30f, 0.22f), false),
        };

        private static readonly string[] Buildings =
        {
            "building-a", "building-b", "building-c", "building-d", "building-e", "building-f", "building-g",
            "building-h", "building-i", "building-j", "building-k", "building-l", "building-m", "building-n",
            "building-skyscraper-a", "building-skyscraper-b", "building-skyscraper-c",
            "building-skyscraper-d", "building-skyscraper-e",
        };

        private static readonly string[] StreetProps = { Roads + "light-square.fbx", Roads + "electricity-pole.fbx" };

        // Delhi's palette: hazy warm sky, dark asphalt, red-sandstone pavement.
        private static readonly Color Sky = new Color(0.86f, 0.74f, 0.58f);
        private static readonly Color Asphalt = new Color(0.20f, 0.20f, 0.22f);
        private static readonly Color Sandstone = new Color(0.74f, 0.58f, 0.46f);
        private static readonly Color Lot = new Color(0.52f, 0.45f, 0.37f);

        /// <summary>
        /// Kind to model, with an extra turn in degrees. Kinds not listed keep their generated
        /// shape. Traffic heading the same way shows its back; oncoming traffic is turned to
        /// face the player.
        /// </summary>
        private static readonly (ObstacleKind Kind, string Model, float Yaw)[] Mapping =
        {
            (ObstacleKind.Train, TrainKit + "train-electric-subway-a.fbx", 0f),
            (ObstacleKind.Truck, CarKit + "delivery.fbx", 0f),
            (ObstacleKind.MovingTruck, CarKit + "garbage-truck.fbx", 0f),
            (ObstacleKind.OncomingTruck, CarKit + "truck.fbx", 180f),
            (ObstacleKind.StoppingBus, Rgsdev + "Bus.fbx", 0f),
            (ObstacleKind.Low, Roads + "construction-barrier.fbx", 0f),
            (ObstacleKind.Full, Roads + "construction-fence.fbx", 0f),
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
                entry.FindPropertyRelative("Yaw").floatValue = Mapping[i].Yaw;

                // A grown array copies its last element, so clear every field explicitly.
                entry.FindPropertyRelative("UsePaint").boolValue = false;
                entry.FindPropertyRelative("HazardStripes").boolValue = false;
                mapped++;
            }

            for (int i = 0; i < Paints.Length; i++)
            {
                art.arraySize++;
                SerializedProperty entry = art.GetArrayElementAtIndex(art.arraySize - 1);
                entry.FindPropertyRelative("Kind").intValue = (int)Paints[i].Kind;
                entry.FindPropertyRelative("Visual").objectReferenceValue = null;
                entry.FindPropertyRelative("Yaw").floatValue = 0f;
                entry.FindPropertyRelative("UsePaint").boolValue = true;
                entry.FindPropertyRelative("Paint").colorValue = Paints[i].Paint;
                entry.FindPropertyRelative("HazardStripes").boolValue = Paints[i].Stripes;
            }

            int buildings = FillModels(serialized.FindProperty("_buildings"), Buildings, Commercial, ".fbx");
            int props = FillModels(serialized.FindProperty("_streetProps"), StreetProps, "", "");

            serialized.FindProperty("_skyColor").colorValue = Sky;
            serialized.FindProperty("_groundColor").colorValue = Asphalt;
            serialized.FindProperty("_sidewalkColor").colorValue = Sandstone;
            serialized.FindProperty("_lotColor").colorValue = Lot;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(state);
            AssetDatabase.SaveAssets();

            AssignToScene(state);

            Debug.Log($"Delhi state ready: {mapped} of {Mapping.Length} obstacle kinds have models, " +
                      $"{Paints.Length} are painted, {buildings} buildings, {props} street props. Press Play.");
        }

        /// <summary>Loads each model into a GameObject array property, skipping any that are missing.</summary>
        private static int FillModels(SerializedProperty array, string[] names, string prefix, string suffix)
        {
            array.arraySize = 0;

            for (int i = 0; i < names.Length; i++)
            {
                string path = prefix + names[i] + suffix;
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null)
                {
                    Debug.LogWarning($"Model not found at {path} — skipped.");
                    continue;
                }

                array.arraySize++;
                array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = model;
            }

            return array.arraySize;
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
