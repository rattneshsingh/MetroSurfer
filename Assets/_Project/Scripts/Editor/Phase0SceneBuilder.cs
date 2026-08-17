using System.IO;
using Runner.Config;
using Runner.Core;
using Runner.Player;
using Runner.Spawning;
using Runner.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Runner.EditorTools
{
    /// <summary>
    /// Builds and wires the whole Phase 0 scene from one menu item, so none of it has to be
    /// assembled by hand in the Inspector.
    /// </summary>
    /// <remarks>
    /// Idempotent where it can be: re-running overwrites the scene file but reuses any tuning
    /// assets that already exist, so tweaked curves survive a rebuild. Lives in an Editor
    /// folder, so it is compiled into Assembly-CSharp-Editor and never ships in a build.
    /// </remarks>
    public static class Phase0SceneBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Run.unity";
        private const string ScriptableObjectFolder = "Assets/_Project/ScriptableObjects";
        private const string DifficultyAssetPath = ScriptableObjectFolder + "/DifficultyCurve.asset";
        private const string ObstacleTableAssetPath = ScriptableObjectFolder + "/ObstacleTable.asset";
        private const string IntroSequenceAssetPath = ScriptableObjectFolder + "/IntroSequence.asset";
        private const string CatalogAssetPath = ScriptableObjectFolder + "/ObstacleCatalog.asset";

        private const string PlayerLayerName = "Player";
        private const string ObstacleLayerName = "Obstacle";
        private const string CoinLayerName = "Coin";
        private const string SurfaceLayerName = "Surface";

        private const int PlayerLayerIndex = 6;
        private const int ObstacleLayerIndex = 7;
        private const int CoinLayerIndex = 8;
        private const int SurfaceLayerIndex = 9;

        private static readonly Vector3 CameraPosition = new Vector3(0f, 3.4f, -6.4f);
        private static readonly Vector3 CameraEuler = new Vector3(17f, 0f, 0f);

        /// <summary>Camera background and fog share this, so the horizon has no visible seam.</summary>
        private static readonly Color SkyColor = new Color(0.45f, 0.52f, 0.60f);

        [MenuItem("Runner/Build Phase 0 Scene", priority = 0)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EnsureLayers();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            ConfigureCamera();

            LaneController player = BuildPlayer(out PlayerCollision playerCollision, out BoxCollider hitbox, out Transform visual);
            BuildBootstrap(out PlayerInputReader input, out SegmentSpawner spawner, out GameManager gameManager);
            HudController hud = BuildHud();

            DifficultyCurve difficulty = LoadOrCreateAsset(DifficultyAssetPath, DifficultyCurve.CreateDefault);
            ObstacleTable obstacleTable = LoadOrCreateAsset(ObstacleTableAssetPath, ObstacleTable.CreateDefault);
            IntroSequence introSequence = LoadOrCreateAsset(IntroSequenceAssetPath, IntroSequence.CreateDefault);
            ObstacleCatalog catalog = LoadOrCreateAsset(CatalogAssetPath, ObstacleCatalog.CreateDefault);

            WireSpawner(spawner, introSequence, catalog);
            WirePlayer(player, hitbox, visual, input);
            WireGameManager(gameManager, player, spawner, input, hud, difficulty, obstacleTable);

            SaveScene(scene);

            Selection.activeGameObject = player.gameObject;

            Debug.Log(
                "Phase 0 scene built at " + ScenePath + ".\n" +
                "Press Play. A/D or arrows to switch lanes, W/Up/Space to jump, S/Down to slide, R to restart.\n" +
                "Tune feel on the Player object; tune difficulty on the DifficultyCurve asset.");

            WarnIfTextMeshProUnavailable();

            // Silences the unused-variable warning while keeping the out param documented as
            // part of the build contract.
            _ = playerCollision;
        }

        private static void EnsureLayers()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("Could not open ProjectSettings/TagManager.asset — add the layers by hand.");
                return;
            }

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            AssignLayer(layers, PlayerLayerIndex, PlayerLayerName);
            AssignLayer(layers, ObstacleLayerIndex, ObstacleLayerName);
            AssignLayer(layers, CoinLayerIndex, CoinLayerName);
            AssignLayer(layers, SurfaceLayerIndex, SurfaceLayerName);

            tagManager.ApplyModifiedProperties();
        }

        private static void AssignLayer(SerializedProperty layers, int index, string layerName)
        {
            // Already defined anywhere? Leave it alone — the scripts look layers up by name,
            // not by index, so it does not matter which slot it landed in.
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                {
                    return;
                }
            }

            if (index >= layers.arraySize)
            {
                Debug.LogError($"Layer slot {index} is out of range — add '{layerName}' by hand.");
                return;
            }

            SerializedProperty slot = layers.GetArrayElementAtIndex(index);
            if (!string.IsNullOrEmpty(slot.stringValue))
            {
                Debug.LogError($"User Layer {index} is already '{slot.stringValue}'. " +
                               $"Add '{layerName}' to a free slot by hand.");
                return;
            }

            slot.stringValue = layerName;
        }

        private static void ConfigureCamera()
        {
            Camera camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                return;
            }

            Transform t = camera.transform;
            t.position = CameraPosition;
            t.rotation = Quaternion.Euler(CameraEuler);

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = SkyColor;
            camera.farClipPlane = 200f;

            // Fog ends before the far clip, which ends well before the track does. Between them
            // the player can never see the world stop or a segment pop into place.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyColor;
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 190f;
        }

        private static LaneController BuildPlayer(
            out PlayerCollision playerCollision,
            out BoxCollider hitbox,
            out Transform visual)
        {
            GameObject playerObject = new GameObject("Player");
            playerObject.layer = LayerMask.NameToLayer(PlayerLayerName);
            playerObject.transform.position = Vector3.zero;

            hitbox = playerObject.AddComponent<BoxCollider>();
            hitbox.isTrigger = true;
            hitbox.center = new Vector3(0f, 0.5f, 0f);
            hitbox.size = new Vector3(0.45f, 0.85f, 0.45f);

            // Trigger events only fire when one side of the pair carries a body. The obstacles
            // and coins are body-less triggers, so the player is the one that has to have it.
            Rigidbody body = playerObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;

            LaneController laneController = playerObject.AddComponent<LaneController>();
            playerCollision = playerObject.AddComponent<PlayerCollision>();

            GameObject visualObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visualObject.name = "Visual";
            visualObject.layer = playerObject.layer;

            // The primitive brings its own collider; the hitbox on the root is the real one and
            // is deliberately smaller than this mesh.
            Object.DestroyImmediate(visualObject.GetComponent<BoxCollider>());

            visualObject.transform.SetParent(playerObject.transform, false);
            visualObject.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            visualObject.transform.localScale = new Vector3(0.7f, 1f, 0.7f);

            visual = visualObject.transform;
            return laneController;
        }

        private static void BuildBootstrap(
            out PlayerInputReader input,
            out SegmentSpawner spawner,
            out GameManager gameManager)
        {
            GameObject bootstrap = new GameObject("Bootstrap");

            input = bootstrap.AddComponent<PlayerInputReader>();
            spawner = bootstrap.AddComponent<SegmentSpawner>();
            gameManager = bootstrap.AddComponent<GameManager>();
        }

        private static HudController BuildHud()
        {
            GameObject canvasObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = LayerMask.NameToLayer("UI");

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();

            TextMeshProUGUI scoreText = CreateText(canvasRect, "ScoreText", "0", 90f, TextAlignmentOptions.Top);
            AnchorTop(scoreText.rectTransform, new Vector2(600f, 140f), -90f);

            TextMeshProUGUI multiplierText = CreateText(canvasRect, "MultiplierText", "x2", 64f, TextAlignmentOptions.Top);
            multiplierText.color = new Color(1f, 0.85f, 0.3f);
            AnchorTop(multiplierText.rectTransform, new Vector2(400f, 100f), -230f);
            multiplierText.gameObject.SetActive(false);

            GameObject readyPanel = CreatePanel(canvasRect, "ReadyPanel", new Color(0f, 0f, 0f, 0.35f));
            TextMeshProUGUI readyText = CreateText(readyPanel.GetComponent<RectTransform>(), "ReadyText",
                "TAP OR PRESS A KEY", 70f, TextAlignmentOptions.Center);
            AnchorCentre(readyText.rectTransform, new Vector2(900f, 200f), 0f);

            GameObject gameOverPanel = CreatePanel(canvasRect, "GameOverPanel", new Color(0f, 0f, 0f, 0.65f));
            RectTransform gameOverRect = gameOverPanel.GetComponent<RectTransform>();

            TextMeshProUGUI gameOverText = CreateText(gameOverRect, "GameOverText", "GAME OVER", 90f, TextAlignmentOptions.Center);
            AnchorCentre(gameOverText.rectTransform, new Vector2(900f, 180f), 220f);

            TextMeshProUGUI finalScoreText = CreateText(gameOverRect, "FinalScoreText", "0", 120f, TextAlignmentOptions.Center);
            AnchorCentre(finalScoreText.rectTransform, new Vector2(900f, 200f), 40f);

            Button restartButton = CreateButton(gameOverRect, "RestartButton", "RESTART");
            AnchorCentre(restartButton.GetComponent<RectTransform>(), new Vector2(460f, 140f), -200f);

            HudController hud = canvasObject.AddComponent<HudController>();

            SerializedObject hudSerialized = new SerializedObject(hud);
            SetReference(hudSerialized, "_scoreText", scoreText);
            SetReference(hudSerialized, "_multiplierText", multiplierText);
            SetReference(hudSerialized, "_readyPanel", readyPanel);
            SetReference(hudSerialized, "_gameOverPanel", gameOverPanel);
            SetReference(hudSerialized, "_finalScoreText", finalScoreText);
            SetReference(hudSerialized, "_restartButton", restartButton);
            hudSerialized.ApplyModifiedPropertiesWithoutUndo();

            // GameManager binds the click in Awake, so the panel can start hidden.
            gameOverPanel.SetActive(false);

            return hud;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem));

            // The project runs the Input System package exclusively — the legacy
            // StandaloneInputModule would throw at runtime.
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }

        private static GameObject CreatePanel(RectTransform parent, string name, Color color)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.layer = LayerMask.NameToLayer("UI");

            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Stretch(rect);

            panel.GetComponent<Image>().color = color;
            return panel;
        }

        private static TextMeshProUGUI CreateText(
            RectTransform parent, string name, string content, float fontSize, TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform));
            textObject.layer = LayerMask.NameToLayer("UI");

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            return text;
        }

        private static Button CreateButton(RectTransform parent, string name, string label)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.layer = LayerMask.NameToLayer("UI");

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            buttonObject.GetComponent<Image>().color = new Color(0.85f, 0.85f, 0.88f);

            TextMeshProUGUI text = CreateText(rect, "Label", label, 60f, TextAlignmentOptions.Center);
            text.color = Color.black;
            Stretch(text.rectTransform);

            return buttonObject.GetComponent<Button>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void AnchorTop(RectTransform rect, Vector2 size, float y)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, y);
        }

        private static void AnchorCentre(RectTransform rect, Vector2 size, float y)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, y);
        }

        private static void WireSpawner(SegmentSpawner spawner, IntroSequence introSequence, ObstacleCatalog catalog)
        {
            SerializedObject serialized = new SerializedObject(spawner);
            SetReference(serialized, "_introSequence", introSequence);
            SetReference(serialized, "_catalog", catalog);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WirePlayer(LaneController player, BoxCollider hitbox, Transform visual, PlayerInputReader input)
        {
            SerializedObject serialized = new SerializedObject(player);
            SetReference(serialized, "_hitbox", hitbox);
            SetReference(serialized, "_visual", visual);
            SetReference(serialized, "_input", input);

            // The ground probe needs to know which layer counts as standable.
            int surfaceLayer = LayerMask.NameToLayer(SurfaceLayerName);
            if (surfaceLayer >= 0)
            {
                SerializedProperty mask = serialized.FindProperty("_surfaceLayers");
                if (mask != null)
                {
                    mask.intValue = 1 << surfaceLayer;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireGameManager(
            GameManager gameManager,
            LaneController player,
            SegmentSpawner spawner,
            PlayerInputReader input,
            HudController hud,
            DifficultyCurve difficulty,
            ObstacleTable obstacleTable)
        {
            SerializedObject serialized = new SerializedObject(gameManager);
            SetReference(serialized, "_player", player);
            SetReference(serialized, "_spawner", spawner);
            SetReference(serialized, "_input", input);
            SetReference(serialized, "_hud", hud);
            SetReference(serialized, "_difficulty", difficulty);
            SetReference(serialized, "_obstacleTable", obstacleTable);
            // _state is left null on purpose. Phase 0 has no state theming.
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetReference(SerializedObject serialized, string propertyPath, Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyPath);
            if (property == null)
            {
                Debug.LogError($"No serialized field '{propertyPath}' on {serialized.targetObject.GetType().Name} — " +
                               "assign it by hand.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static T LoadOrCreateAsset<T>(string path, System.Func<T> factory) where T : ScriptableObject
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(ScriptableObjectFolder);

            T created = factory();
            created.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();

            return created;
        }

        private static void SaveScene(Scene scene)
        {
            EnsureFolder("Assets/_Project/Scenes");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }

        private static void EnsureFolder(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            string absolute = Path.Combine(Directory.GetCurrentDirectory(), assetFolderPath);
            Directory.CreateDirectory(absolute);
            AssetDatabase.Refresh();
        }

        private static void WarnIfTextMeshProUnavailable()
        {
            if (TMP_Settings.defaultFontAsset != null)
            {
                return;
            }

            Debug.LogWarning(
                "TextMeshPro has no default font asset, so the HUD text will not render.\n" +
                "Run Window > TextMeshPro > Import TMP Essential Resources, then run " +
                "Runner > Build Phase 0 Scene again.");
        }
    }
}
