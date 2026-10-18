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
        private const string PlayerTuningAssetPath = ScriptableObjectFolder + "/PlayerTuning.asset";
        private const string RunTuningAssetPath = ScriptableObjectFolder + "/RunTuning.asset";

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

        /// <summary>
        /// Rebuilds Run.unity from nothing and then runs every setup step, so the result is the
        /// whole game — Delhi, the runner, the pursuer — not a bare Phase 0 track. Tuning assets
        /// are reused, so tuned values survive.
        /// </summary>
        [MenuItem("Runner/Rebuild Whole Scene From Scratch", priority = 0)]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("Stop Play mode first — changes made while playing are thrown away.");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Rebuild the whole scene?",
                    "This replaces " + ScenePath + " with a freshly built scene, then sets up Delhi, " +
                    "the runner and the pursuer again.\n\nHand-made changes to the scene itself " +
                    "(camera, lighting, moved objects) are lost. Tuning assets are kept.\n\n" +
                    "For UI changes only, use Runner > Rebuild HUD instead.",
                    "Rebuild",
                    "Cancel"))
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EnsureLayers();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CameraShake cameraShake = ConfigureCamera();

            LaneController player = BuildPlayer(out PlayerCollision playerCollision, out BoxCollider hitbox, out Transform visual);
            Pursuer pursuer = BuildPursuer();
            BuildBootstrap(out PlayerInputReader input, out SegmentSpawner spawner, out GameManager gameManager);
            HudController hud = BuildHud(out RectTransform[] touchBlockers);

            DifficultyCurve difficulty = LoadOrCreateAsset(DifficultyAssetPath, DifficultyCurve.CreateDefault);
            ObstacleTable obstacleTable = LoadOrCreateAsset(ObstacleTableAssetPath, ObstacleTable.CreateDefault);
            IntroSequence introSequence = LoadOrCreateAsset(IntroSequenceAssetPath, IntroSequence.CreateDefault);
            ObstacleCatalog catalog = LoadOrCreateAsset(CatalogAssetPath, ObstacleCatalog.CreateDefault);
            PlayerTuning playerTuning = LoadOrCreateAsset(PlayerTuningAssetPath, PlayerTuning.CreateDefault);
            RunTuning runTuning = LoadOrCreateAsset(RunTuningAssetPath, RunTuning.CreateDefault);

            WireSpawner(spawner, introSequence, catalog);
            WirePlayer(player, hitbox, visual, input, playerTuning);
            WireInputBlockers(input, touchBlockers);
            WireGameManager(gameManager, player, spawner, input, hud, difficulty, obstacleTable, pursuer, cameraShake, runTuning);

            SaveScene(scene);

            Selection.activeGameObject = player.gameObject;

            Debug.Log(
                "Phase 0 scene built at " + ScenePath + ".\n" +
                "Press Play. A/D or arrows to switch lanes, W/Up/Space to jump, S/Down to slide, " +
                "Esc/P to pause, R to restart.\n" +
                "Tune feel on the PlayerTuning and RunTuning assets -- those survive pressing Stop.");

            WarnIfTextMeshProUnavailable();

            // Silences the unused-variable warning while keeping the out param documented as
            // part of the build contract.
            _ = playerCollision;

            // The bare scene is only the skeleton. Put the game back on it, in dependency order:
            // the state first, then the characters that stand in it.
            DelhiStateSetup.SetUp();
            RunnerCharacterSetup.SetUp();
            RunnerCharacterSetup.SetUpPursuer();
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

        private static CameraShake ConfigureCamera()
        {
            Camera camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                return null;
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

            // Added after positioning, so the shake captures the right base position.
            return camera.gameObject.AddComponent<CameraShake>();
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

        /// <summary>
        /// A red cube that lives behind the player. Crude, but it only has to answer one
        /// question at a glance: how much trouble am I in?
        /// </summary>
        private static Pursuer BuildPursuer()
        {
            GameObject root = new GameObject("Pursuer");
            root.transform.position = new Vector3(0f, 0f, -9f);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<BoxCollider>());

            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            visual.transform.localScale = new Vector3(0.75f, 1.1f, 0.75f);

            Renderer renderer = visual.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(renderer.sharedMaterial) { color = new Color(0.6f, 0.1f, 0.1f) };

            Pursuer pursuer = root.AddComponent<Pursuer>();

            SerializedObject serialized = new SerializedObject(pursuer);
            SetReference(serialized, "_visual", visual.transform);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return pursuer;
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

        /// <summary>The game's name as shown on the title screen.</summary>
        private const string GameTitle = "METRO RUNNER";

        private static readonly Color Gold = new Color(1f, 0.80f, 0.22f);
        private static readonly Color Go = new Color(0.30f, 0.82f, 0.48f);
        private static readonly Color Neutral = new Color(0.86f, 0.87f, 0.91f);
        private static readonly Color Alert = new Color(1f, 0.36f, 0.45f);
        private static readonly Color Ink = new Color(0.10f, 0.11f, 0.14f);
        private static readonly Color Pill = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color Card = new Color(0.12f, 0.13f, 0.17f, 0.96f);

        /// <summary>
        /// Replaces only the HUD in the open scene and rewires it. Unlike Build Phase 0 Scene,
        /// this keeps every other object as it is, so hand-tuned camera or lighting survives.
        /// </summary>
        [MenuItem("Runner/Rebuild HUD", priority = 1)]
        public static void RebuildHud()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("Stop Play mode first — changes made while playing are thrown away.");
                return;
            }

            GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
            if (gameManager == null)
            {
                Debug.LogError("No GameManager in the open scene. Open " + ScenePath + " first.");
                return;
            }

            HudController old = Object.FindAnyObjectByType<HudController>(FindObjectsInactive.Include);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            HudController hud = BuildHud(out RectTransform[] touchBlockers);

            SerializedObject serialized = new SerializedObject(gameManager);
            SetReference(serialized, "_hud", hud);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            WireInputBlockers(Object.FindAnyObjectByType<PlayerInputReader>(), touchBlockers);

            Scene scene = gameManager.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("HUD rebuilt and saved. Press Play.");
        }

        private static HudController BuildHud(out RectTransform[] touchBlockers)
        {
            GameObject canvasObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = LayerMask.NameToLayer("UI");

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Expand keeps the whole 1080x1920 layout on screen at any aspect: a phone shows it
            // edge to edge, a landscape window or desktop browser shows it as a centred column
            // instead of cutting off the top and bottom.
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            EnsureEventSystem();

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();

            // ---- Running: coins and distance top-left, score centre, pause top-right ----
            GameObject runningRoot = CreateGroup(canvasRect, "Running");
            RectTransform running = runningRoot.GetComponent<RectTransform>();

            Image coinsPill = CreateImage(running, "CoinsPill", RoundedSprite, Pill);
            Place(coinsPill.rectTransform, new Vector2(0f, 1f), new Vector2(300f, 104f), new Vector2(36f, -48f));

            Image coinIcon = CreateImage(coinsPill.rectTransform, "CoinIcon", CircleSprite, Gold);
            Place(coinIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(64f, 64f), new Vector2(22f, 0f));

            TextMeshProUGUI coinsText = CreateText(coinsPill.rectTransform, "CoinsText", "0", 60f, TextAlignmentOptions.Left);
            coinsText.fontStyle = FontStyles.Bold;
            Stretch(coinsText.rectTransform);
            coinsText.rectTransform.offsetMin = new Vector2(104f, 0f);

            TextMeshProUGUI distanceText = CreateText(running, "DistanceText", "0 m", 44f, TextAlignmentOptions.Left);
            distanceText.alpha = 0.85f;
            Place(distanceText.rectTransform, new Vector2(0f, 1f), new Vector2(300f, 60f), new Vector2(52f, -164f));

            TextMeshProUGUI scoreText = CreateText(running, "ScoreText", "0", 104f, TextAlignmentOptions.Top);
            scoreText.fontStyle = FontStyles.Bold;
            AnchorTop(scoreText.rectTransform, new Vector2(600f, 130f), -40f);

            Image multiplierBadge = CreateImage(running, "MultiplierBadge", RoundedSprite, Gold);
            AnchorTop(multiplierBadge.rectTransform, new Vector2(170f, 84f), -178f);

            TextMeshProUGUI multiplierText = CreateText(multiplierBadge.rectTransform, "MultiplierText", "x2", 56f,
                TextAlignmentOptions.Center);
            multiplierText.fontStyle = FontStyles.Bold;
            multiplierText.color = Ink;
            Stretch(multiplierText.rectTransform);
            multiplierBadge.gameObject.SetActive(false);

            Button pauseButton = CreateButton(running, "PauseButton", "II", CircleSprite, Pill, Color.white, 56f);
            Place(pauseButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(112f, 112f), new Vector2(-36f, -48f));

            // ---- Title ----
            GameObject readyPanel = CreatePanel(canvasRect, "ReadyPanel", new Color(0f, 0f, 0f, 0.35f));
            RectTransform ready = readyPanel.GetComponent<RectTransform>();

            TextMeshProUGUI title = CreateText(ready, "Title", GameTitle, 170f, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.color = Gold;

            // Shrinks to fit its box, so a longer name never runs off a narrow screen.
            title.enableAutoSizing = true;
            title.fontSizeMin = 80f;
            title.fontSizeMax = 170f;
            AnchorCentre(title.rectTransform, new Vector2(1000f, 220f), 520f);

            TextMeshProUGUI tagline = CreateText(ready, "Tagline", "Run the city. Climb the traffic.", 50f,
                TextAlignmentOptions.Center);
            tagline.alpha = 0.9f;
            AnchorCentre(tagline.rectTransform, new Vector2(1000f, 80f), 380f);

            Image howToCard = CreateImage(ready, "HowTo", RoundedSprite, new Color(0f, 0f, 0f, 0.5f));
            AnchorCentre(howToCard.rectTransform, new Vector2(860f, 330f), -20f);

            TextMeshProUGUI howTo = CreateText(howToCard.rectTransform, "HowToText",
                "SWIPE LEFT / RIGHT  -  change lane\nSWIPE UP or TAP  -  jump\nSWIPE DOWN  -  roll",
                44f, TextAlignmentOptions.Center);
            howTo.lineSpacing = 30f;
            Stretch(howTo.rectTransform);

            TextMeshProUGUI keyboardHint = CreateText(ready, "KeyboardHint",
                "Keyboard: arrows or WASD, Space to jump, Esc to pause", 34f, TextAlignmentOptions.Center);
            keyboardHint.alpha = 0.7f;
            AnchorCentre(keyboardHint.rectTransform, new Vector2(1000f, 60f), -250f);

            TextMeshProUGUI readyBest = CreateText(ready, "BestText", "BEST  0", 56f, TextAlignmentOptions.Center);
            readyBest.fontStyle = FontStyles.Bold;
            readyBest.color = Gold;
            AnchorCentre(readyBest.rectTransform, new Vector2(800f, 90f), -400f);
            readyBest.gameObject.SetActive(false);

            TextMeshProUGUI readyPrompt = CreateText(ready, "Prompt", "TAP TO RUN", 76f, TextAlignmentOptions.Center);
            readyPrompt.fontStyle = FontStyles.Bold;
            AnchorCentre(readyPrompt.rectTransform, new Vector2(900f, 120f), -620f);

            // ---- Pause ----
            GameObject pausePanel = CreatePanel(canvasRect, "PausePanel", new Color(0f, 0f, 0f, 0.6f));
            Image pauseCard = CreateImage(pausePanel.GetComponent<RectTransform>(), "Card", RoundedSprite, Card);
            AnchorCentre(pauseCard.rectTransform, new Vector2(760f, 640f), 0f);

            TextMeshProUGUI pausedText = CreateText(pauseCard.rectTransform, "PausedText", "PAUSED", 100f,
                TextAlignmentOptions.Center);
            pausedText.fontStyle = FontStyles.Bold;
            AnchorCentre(pausedText.rectTransform, new Vector2(700f, 140f), 200f);

            Button resumeButton = CreateButton(pauseCard.rectTransform, "ResumeButton", "RESUME", RoundedSprite, Go, Ink, 60f);
            AnchorCentre(resumeButton.GetComponent<RectTransform>(), new Vector2(520f, 140f), 20f);

            Button pauseRestartButton = CreateButton(pauseCard.rectTransform, "RestartButton", "RESTART", RoundedSprite,
                Neutral, Ink, 60f);
            AnchorCentre(pauseRestartButton.GetComponent<RectTransform>(), new Vector2(520f, 140f), -170f);

            // ---- Game over ----
            GameObject gameOverPanel = CreatePanel(canvasRect, "GameOverPanel", new Color(0f, 0f, 0f, 0.7f));
            Image gameOverCard = CreateImage(gameOverPanel.GetComponent<RectTransform>(), "Card", RoundedSprite, Card);
            AnchorCentre(gameOverCard.rectTransform, new Vector2(880f, 1180f), 0f);
            RectTransform card = gameOverCard.rectTransform;

            TextMeshProUGUI gameOverText = CreateText(card, "GameOverText", "RUN OVER", 90f, TextAlignmentOptions.Center);
            gameOverText.fontStyle = FontStyles.Bold;
            AnchorCentre(gameOverText.rectTransform, new Vector2(800f, 130f), 470f);

            TextMeshProUGUI scoreLabel = CreateText(card, "ScoreLabel", "SCORE", 40f, TextAlignmentOptions.Center);
            scoreLabel.alpha = 0.7f;
            AnchorCentre(scoreLabel.rectTransform, new Vector2(600f, 60f), 340f);

            TextMeshProUGUI finalScoreText = CreateText(card, "FinalScoreText", "0", 160f, TextAlignmentOptions.Center);
            finalScoreText.fontStyle = FontStyles.Bold;
            finalScoreText.color = Gold;
            AnchorCentre(finalScoreText.rectTransform, new Vector2(800f, 190f), 220f);

            Image newBestBadge = CreateImage(card, "NewBestBadge", RoundedSprite, Alert);
            AnchorCentre(newBestBadge.rectTransform, new Vector2(360f, 80f), 90f);

            TextMeshProUGUI newBestText = CreateText(newBestBadge.rectTransform, "Label", "NEW BEST!", 48f,
                TextAlignmentOptions.Center);
            newBestText.fontStyle = FontStyles.Bold;
            Stretch(newBestText.rectTransform);
            newBestBadge.gameObject.SetActive(false);

            TextMeshProUGUI finalCoinsText = CreateStatRow(card, "Coins", "COINS", -60f);
            TextMeshProUGUI finalDistanceText = CreateStatRow(card, "Distance", "DISTANCE", -150f);
            TextMeshProUGUI finalBestText = CreateStatRow(card, "Best", "BEST", -240f);

            Button restartButton = CreateButton(card, "RestartButton", "RUN AGAIN", RoundedSprite, Go, Ink, 64f);
            AnchorCentre(restartButton.GetComponent<RectTransform>(), new Vector2(600f, 150f), -420f);

            TextMeshProUGUI restartHint = CreateText(card, "RestartHint", "or press R", 34f, TextAlignmentOptions.Center);
            restartHint.alpha = 0.6f;
            AnchorCentre(restartHint.rectTransform, new Vector2(600f, 60f), -530f);

            HudController hud = canvasObject.AddComponent<HudController>();

            SerializedObject hudSerialized = new SerializedObject(hud);
            SetReference(hudSerialized, "_runningRoot", runningRoot);
            SetReference(hudSerialized, "_scoreText", scoreText);
            SetReference(hudSerialized, "_coinsText", coinsText);
            SetReference(hudSerialized, "_distanceText", distanceText);
            SetReference(hudSerialized, "_multiplierBadge", multiplierBadge.gameObject);
            SetReference(hudSerialized, "_multiplierText", multiplierText);
            SetReference(hudSerialized, "_pauseButton", pauseButton);
            SetReference(hudSerialized, "_readyPanel", readyPanel);
            SetReference(hudSerialized, "_readyBestText", readyBest);
            SetReference(hudSerialized, "_readyPrompt", readyPrompt);
            SetReference(hudSerialized, "_pausePanel", pausePanel);
            SetReference(hudSerialized, "_resumeButton", resumeButton);
            SetReference(hudSerialized, "_pauseRestartButton", pauseRestartButton);
            SetReference(hudSerialized, "_gameOverPanel", gameOverPanel);
            SetReference(hudSerialized, "_finalScoreText", finalScoreText);
            SetReference(hudSerialized, "_finalCoinsText", finalCoinsText);
            SetReference(hudSerialized, "_finalDistanceText", finalDistanceText);
            SetReference(hudSerialized, "_finalBestText", finalBestText);
            SetReference(hudSerialized, "_newBestBadge", newBestBadge.gameObject);
            SetReference(hudSerialized, "_restartButton", restartButton);
            hudSerialized.ApplyModifiedPropertiesWithoutUndo();

            // A touch that starts on any button is that button's, never a jump.
            touchBlockers = new[]
            {
                pauseButton.GetComponent<RectTransform>(),
                resumeButton.GetComponent<RectTransform>(),
                pauseRestartButton.GetComponent<RectTransform>(),
                restartButton.GetComponent<RectTransform>()
            };

            // GameManager binds the clicks in Awake, so these can start hidden.
            runningRoot.SetActive(false);
            pausePanel.SetActive(false);
            gameOverPanel.SetActive(false);

            return hud;
        }

        private static void WireInputBlockers(PlayerInputReader input, RectTransform[] blockers)
        {
            if (input == null)
            {
                Debug.LogError("No PlayerInputReader found — tapping a button will also jump.");
                return;
            }

            SerializedObject serialized = new SerializedObject(input);
            SerializedProperty property = serialized.FindProperty("_touchBlockers");
            if (property == null)
            {
                Debug.LogError("PlayerInputReader has no _touchBlockers field — assign it by hand.");
                return;
            }

            property.arraySize = blockers.Length;
            for (int i = 0; i < blockers.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = blockers[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Unity's built-in rounded, 9-sliced UI sprite. Ships with every project.</summary>
        private static Sprite RoundedSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        /// <summary>Unity's built-in circle sprite.</summary>
        private static Sprite CircleSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        private static GameObject CreateGroup(RectTransform parent, string name)
        {
            GameObject group = new GameObject(name, typeof(RectTransform));
            group.layer = LayerMask.NameToLayer("UI");

            RectTransform rect = group.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Stretch(rect);
            return group;
        }

        private static Image CreateImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.layer = LayerMask.NameToLayer("UI");

            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            Image image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;

            // Decoration must not swallow the swipes and taps that drive the runner.
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A label on the left, its value on the right. Returns the value text.</summary>
        private static TextMeshProUGUI CreateStatRow(RectTransform parent, string name, string label, float y)
        {
            GameObject row = new GameObject(name + "Row", typeof(RectTransform));
            row.layer = LayerMask.NameToLayer("UI");

            RectTransform rect = row.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            AnchorCentre(rect, new Vector2(640f, 80f), y);

            TextMeshProUGUI labelText = CreateText(rect, "Label", label, 46f, TextAlignmentOptions.Left);
            labelText.alpha = 0.75f;
            Stretch(labelText.rectTransform);

            TextMeshProUGUI valueText = CreateText(rect, "Value", "0", 52f, TextAlignmentOptions.Right);
            valueText.fontStyle = FontStyles.Bold;
            Stretch(valueText.rectTransform);

            return valueText;
        }

        /// <summary>Anchors and pivots to the same corner, then offsets inward from it.</summary>
        private static void Place(RectTransform rect, Vector2 corner, Vector2 size, Vector2 offset)
        {
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
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

        private static Button CreateButton(
            RectTransform parent, string name, string label, Sprite sprite, Color background, Color textColor, float fontSize)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.layer = LayerMask.NameToLayer("UI");

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            Image image = buttonObject.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = background;

            TextMeshProUGUI text = CreateText(rect, "Label", label, fontSize, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            text.color = textColor;
            text.raycastTarget = false;
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

        private static void WirePlayer(
            LaneController player,
            BoxCollider hitbox,
            Transform visual,
            PlayerInputReader input,
            PlayerTuning tuning)
        {
            SerializedObject serialized = new SerializedObject(player);
            SetReference(serialized, "_hitbox", hitbox);
            SetReference(serialized, "_visual", visual);
            SetReference(serialized, "_input", input);
            SetReference(serialized, "_tuning", tuning);

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
            ObstacleTable obstacleTable,
            Pursuer pursuer,
            CameraShake cameraShake,
            RunTuning runTuning)
        {
            SerializedObject serialized = new SerializedObject(gameManager);
            SetReference(serialized, "_runTuning", runTuning);
            SetReference(serialized, "_pursuer", pursuer);
            SetReference(serialized, "_cameraShake", cameraShake);
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
