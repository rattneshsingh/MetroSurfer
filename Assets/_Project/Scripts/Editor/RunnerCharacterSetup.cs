using System.Collections.Generic;
using Runner.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Runner.EditorTools
{
    /// <summary>
    /// Puts an animated runner on the player in the open scene, replacing the grey cube as
    /// the thing you see. Re-runnable: it rebuilds the controller and the character each time.
    /// </summary>
    /// <remarks>
    /// Movement and collision are untouched — the hitbox and LaneController stay on the player
    /// root, the old cube keeps receiving LaneController's squash and spin but no longer draws,
    /// and the character is a sibling that only animates. Prefers the Kenney character; if its
    /// skeleton cannot be mapped as Humanoid, falls back to the animation library's mannequin.
    /// </remarks>
    public static class RunnerCharacterSetup
    {
        private const string OutputFolder = "Assets/_Project/Art/Runner";
        private const string ControllerPath = OutputFolder + "/Runner.controller";
        private const string MaterialPath = OutputFolder + "/M_Runner.mat";
        private const string SkinPath = "Assets/Kenney/Characters/Skins/skaterMaleA.png";
        private const string CharacterName = "Runner";

        /// <summary>Roughly the old cube's height, so the camera framing still works.</summary>
        private const float TargetHeight = 1.15f;

        /// <summary>Controller state to library clip. State names match PlayerAnimator.</summary>
        private static readonly (string State, string Clip)[] StateClips =
        {
            ("Idle", "Idle_Loop"),
            ("Run", "Sprint_Loop"),
            ("Jump", "Jump_Start"),
            ("JumpLoop", "Jump_Loop"),
            ("Roll", "Roll"),
            ("Stumble", "Hit_Chest"),
            ("Death", "Death01"),
        };

        [MenuItem("Runner/Set Up Runner Character", priority = 31)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("Stop Play mode first — changes made while playing are thrown away.");
                return;
            }

            LaneController player = Object.FindAnyObjectByType<LaneController>();
            if (player == null)
            {
                Debug.LogError("No player in the open scene. Open Assets/_Project/Scenes/Run.unity first.");
                return;
            }

            // Files imported before CharacterImportRules existed need its settings applied.
            AssetDatabase.ImportAsset(CharacterImportRules.AnimationLibraryPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(CharacterImportRules.KenneyCharacterPath, ImportAssetOptions.ForceUpdate);

            Dictionary<string, AnimationClip> clips = LoadClips(CharacterImportRules.AnimationLibraryPath);
            if (!HasAllClips(clips))
            {
                return;
            }

            bool usedKenney = TryLoadHumanoid(CharacterImportRules.KenneyCharacterPath, out GameObject model, out Avatar avatar);
            if (!usedKenney &&
                !TryLoadHumanoid(CharacterImportRules.AnimationLibraryPath, out model, out avatar))
            {
                Debug.LogError("Neither the Kenney character nor the library mannequin imported as a valid " +
                               "Humanoid. Check the Rig tab of both FBX files.");
                return;
            }

            AnimatorController controller = BuildController(clips);

            GameObject runner = PlaceCharacter(player, model);
            Animator animator = runner.GetComponent<Animator>();
            if (animator == null)
            {
                animator = runner.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (usedKenney)
            {
                ApplySkin(runner);
            }

            HideCube(player);
            WireAnimator(player, animator);

            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            EditorSceneManager.SaveScene(player.gameObject.scene);

            Debug.Log(usedKenney
                ? "Runner set up with the Kenney character. Press Play."
                : "Runner set up with the library mannequin — the Kenney character could not be mapped " +
                  "as Humanoid. Press Play.");
        }

        private static Dictionary<string, AnimationClip> LoadClips(string path)
        {
            Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);

            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                {
                    // Takes come in as "Armature|Sprint_Loop"; key on the part after the bar.
                    string name = clip.name;
                    int bar = name.LastIndexOf('|');
                    clips[bar >= 0 ? name.Substring(bar + 1) : name] = clip;
                }
            }

            return clips;
        }

        private static bool HasAllClips(Dictionary<string, AnimationClip> clips)
        {
            List<string> missing = new List<string>();
            for (int i = 0; i < StateClips.Length; i++)
            {
                if (!clips.ContainsKey(StateClips[i].Clip))
                {
                    missing.Add(StateClips[i].Clip);
                }
            }

            if (missing.Count == 0)
            {
                return true;
            }

            Debug.LogError("Animation library is missing: " + string.Join(", ", missing) +
                           ". Found: " + string.Join(", ", clips.Keys));
            return false;
        }

        private static bool TryLoadHumanoid(string path, out GameObject model, out Avatar avatar)
        {
            model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            avatar = null;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Avatar found)
                {
                    avatar = found;
                    break;
                }
            }

            return model != null && avatar != null && avatar.isValid && avatar.isHuman;
        }

        private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
        {
            EnsureFolder();

            // Generated, so rebuilt from scratch every time.
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            Dictionary<string, AnimatorState> states = new Dictionary<string, AnimatorState>();
            for (int i = 0; i < StateClips.Length; i++)
            {
                AnimatorState state = machine.AddState(StateClips[i].State);
                state.motion = clips[StateClips[i].Clip];
                states[StateClips[i].State] = state;
            }

            // Take-off hands itself to the airborne loop; PlayerAnimator only ever asks for Jump.
            AnimatorStateTransition toLoop = states["Jump"].AddTransition(states["JumpLoop"]);
            toLoop.hasExitTime = true;
            toLoop.exitTime = 0.9f;
            toLoop.duration = 0.1f;

            machine.defaultState = states["Idle"];
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static GameObject PlaceCharacter(LaneController player, GameObject model)
        {
            Transform existing = player.transform.Find(CharacterName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            GameObject runner = (GameObject)PrefabUtility.InstantiatePrefab(model, player.transform);
            runner.name = CharacterName;

            Transform t = runner.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            if (TryGetBounds(runner, out Bounds bounds) && bounds.size.y > 0.0001f)
            {
                float scale = TargetHeight / bounds.size.y;
                t.localScale = new Vector3(scale, scale, scale);

                // Feet on the ground, wherever the model's own origin happens to be.
                TryGetBounds(runner, out bounds);
                t.localPosition = new Vector3(0f, player.transform.position.y - bounds.min.y, 0f);
            }

            return runner;
        }

        private static bool TryGetBounds(GameObject root, out Bounds bounds)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bounds = default;
            if (renderers.Length == 0)
            {
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private static void ApplySkin(GameObject runner)
        {
            Texture2D skin = AssetDatabase.LoadAssetAtPath<Texture2D>(SkinPath);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (skin == null || lit == null)
            {
                Debug.LogWarning("Could not load the runner skin or the URP Lit shader; keeping the imported material.");
                return;
            }

            EnsureFolder();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(lit);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.shader = lit;
            material.SetTexture("_BaseMap", skin);
            material.mainTexture = skin;
            EditorUtility.SetDirty(material);

            Renderer[] renderers = runner.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] slots = renderers[i].sharedMaterials;
                for (int s = 0; s < slots.Length; s++)
                {
                    slots[s] = material;
                }

                renderers[i].sharedMaterials = slots;
            }
        }

        /// <summary>The cube stays — LaneController still drives it — but it no longer draws.</summary>
        private static void HideCube(LaneController player)
        {
            SerializedObject serialized = new SerializedObject(player);
            Transform visual = serialized.FindProperty("_visual").objectReferenceValue as Transform;
            Renderer cube = visual != null ? visual.GetComponent<Renderer>() : null;
            if (cube != null)
            {
                cube.enabled = false;
            }
        }

        private static void WireAnimator(LaneController player, Animator animator)
        {
            PlayerAnimator presenter = player.GetComponent<PlayerAnimator>();
            if (presenter == null)
            {
                presenter = player.gameObject.AddComponent<PlayerAnimator>();
            }

            SerializedObject serialized = new SerializedObject(presenter);
            serialized.FindProperty("_player").objectReferenceValue = player;
            serialized.FindProperty("_animator").objectReferenceValue = animator;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Art", "Runner");
            }
        }
    }
}
