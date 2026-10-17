using System.Collections.Generic;
using System.IO;
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

            GameObject runner = PlaceCharacter(player.transform, model, CharacterName, TargetHeight);
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
                ApplySkin(runner, AssetDatabase.LoadAssetAtPath<Texture2D>(SkinPath), MaterialPath);
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

        private const string PursuerControllerPath = OutputFolder + "/Pursuer.controller";
        private const string PursuerMaterialPath = OutputFolder + "/M_Pursuer.mat";
        private const string PursuerName = "Officer";

        /// <summary>A head taller than the runner, so he reads as the grown-up doing the chasing.</summary>
        private const float PursuerHeight = 1.3f;

        /// <summary>Khaki, the colour of a Delhi traffic police uniform. An original look, not any game's chaser.</summary>
        private static readonly Color Khaki = new Color(0.74f, 0.64f, 0.44f);

        /// <summary>
        /// Replaces the pursuer's red block with the animation library's mannequin in a khaki
        /// uniform, sprinting. The mannequin is the rig the animations were made for, so nothing
        /// is retargeted, and its build differs from the runner's so the two never read alike.
        /// </summary>
        /// <remarks>
        /// The Pursuer component still shows, hides and moves whatever its Visual field points
        /// at; this only points it at the officer instead of the cube. No runtime code changes.
        /// </remarks>
        [MenuItem("Runner/Set Up Pursuer Character", priority = 32)]
        public static void SetUpPursuer()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("Stop Play mode first — changes made while playing are thrown away.");
                return;
            }

            Runner.Core.Pursuer pursuer = Object.FindAnyObjectByType<Runner.Core.Pursuer>();
            if (pursuer == null)
            {
                Debug.LogError("No Pursuer in the open scene. Open Assets/_Project/Scenes/Run.unity first.");
                return;
            }

            Dictionary<string, AnimationClip> clips = LoadClips(CharacterImportRules.AnimationLibraryPath);
            if (!clips.TryGetValue("Sprint_Loop", out AnimationClip sprint))
            {
                Debug.LogError("Animation library has no Sprint_Loop. Run Set Up Runner Character first.");
                return;
            }

            // The Kenney character in a generated uniform; the mannequin is only the fallback.
            bool usedKenney = TryLoadHumanoid(CharacterImportRules.KenneyCharacterPath, out GameObject model, out Avatar avatar);
            if (!usedKenney &&
                !TryLoadHumanoid(CharacterImportRules.AnimationLibraryPath, out model, out avatar))
            {
                Debug.LogError("No character imported as a valid Humanoid. Run Set Up Runner Character " +
                               "first, which applies the import settings.");
                return;
            }

            AnimatorController controller = BuildSprintController(sprint);

            SerializedObject serialized = new SerializedObject(pursuer);
            SerializedProperty visualProperty = serialized.FindProperty("_visual");
            Transform oldVisual = visualProperty.objectReferenceValue as Transform;

            GameObject officer = PlaceCharacter(pursuer.transform, model, PursuerName, PursuerHeight);

            Animator animator = officer.GetComponent<Animator>();
            if (animator == null)
            {
                animator = officer.AddComponent<Animator>();
            }

            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            Texture2D uniform = usedKenney ? CreateOfficerSkin() : null;
            if (uniform != null)
            {
                ApplySkin(officer, uniform, PursuerMaterialPath);
            }
            else
            {
                ApplySolidColor(officer, Khaki, PursuerMaterialPath);
            }

            // Retire the cube: Pursuer only toggles its Visual, so a cube left active would stay on screen.
            if (oldVisual != null && oldVisual != officer.transform)
            {
                Renderer[] cube = oldVisual.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < cube.Length; i++)
                {
                    cube[i].enabled = false;
                }

                oldVisual.gameObject.SetActive(false);
            }

            visualProperty.objectReferenceValue = officer.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Hidden until the chase starts, the same as the cube was.
            officer.SetActive(false);

            EditorSceneManager.MarkSceneDirty(pursuer.gameObject.scene);
            EditorSceneManager.SaveScene(pursuer.gameObject.scene);

            Debug.Log(uniform != null
                ? "Pursuer set up: a uniformed officer replaces the red block. Press Play."
                : "Pursuer set up with the khaki mannequin fallback. Press Play.");
        }

        private const string OfficerBaseSkinPath = "Assets/Kenney/Characters/Skins/criminalMaleA.png";
        private const string OfficerSkinPath = OutputFolder + "/officerUniform.png";

        private static readonly Color32 UniformKhaki = new Color32(206, 180, 124, 255);
        private static readonly Color32 TrouserKhaki = new Color32(168, 144, 98, 255);
        private static readonly Color32 BeltBrown = new Color32(104, 72, 42, 255);

        /// <summary>
        /// Builds an original police-uniform skin from Kenney's CC0 criminalMaleA skin: the white
        /// shirt and trousers become khaki, the green straps a brown leather belt. Face, hair,
        /// buttons and shoes are untouched. Regenerated from the source on every run, so the
        /// result can always be rebuilt from what is in the repository.
        /// </summary>
        /// <remarks>
        /// Regions follow Kenney's skin layout (top-origin pixels on a 1024 sheet): shirt
        /// x 0–640, y 490–1024; trousers x 612–1024, y 764–1024. Texture rows count from the
        /// bottom, so the bounds below are flipped.
        /// </remarks>
        private static Texture2D CreateOfficerSkin()
        {
            string fullSource = Path.GetFullPath(OfficerBaseSkinPath);
            if (!File.Exists(fullSource))
            {
                Debug.LogWarning("Base skin not found at " + OfficerBaseSkinPath + ".");
                return null;
            }

            Texture2D sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!sheet.LoadImage(File.ReadAllBytes(fullSource)))
            {
                Object.DestroyImmediate(sheet);
                return null;
            }

            int width = sheet.width;
            int height = sheet.height;
            Color32[] pixels = sheet.GetPixels32();

            for (int y = 0; y < height; y++)
            {
                float fromBottom = (float)y / height;
                for (int x = 0; x < width; x++)
                {
                    float across = (float)x / width;
                    bool shirt = across < 0.625f && fromBottom < 0.522f;
                    bool trousers = across >= 0.598f && fromBottom < 0.254f;
                    if (!shirt && !trousers)
                    {
                        continue;
                    }

                    int i = y * width + x;
                    pixels[i] = Recolour(pixels[i], trousers && !shirt ? TrouserKhaki : UniformKhaki);
                }
            }

            sheet.SetPixels32(pixels);
            sheet.Apply();

            EnsureFolder();
            File.WriteAllBytes(Path.GetFullPath(OfficerSkinPath), sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            AssetDatabase.ImportAsset(OfficerSkinPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(OfficerSkinPath);
        }

        /// <summary>Whites become the cloth colour, keeping their shading; greens become the belt.</summary>
        private static Color32 Recolour(Color32 c, Color32 cloth)
        {
            int max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            int min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));

            if (min > 170 && max - min < 40)
            {
                float shade = max / 255f;
                return new Color32(
                    (byte)(cloth.r * shade), (byte)(cloth.g * shade), (byte)(cloth.b * shade), c.a);
            }

            if (c.g > c.r + 40 && c.g > c.b)
            {
                float shade = c.g / 160f;
                return new Color32(
                    (byte)Mathf.Min(255f, BeltBrown.r * shade),
                    (byte)Mathf.Min(255f, BeltBrown.g * shade),
                    (byte)Mathf.Min(255f, BeltBrown.b * shade),
                    c.a);
            }

            return c;
        }

        private static AnimatorController BuildSprintController(AnimationClip sprint)
        {
            EnsureFolder();

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(PursuerControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(PursuerControllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(PursuerControllerPath);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState run = machine.AddState("Run");
            run.motion = sprint;
            machine.defaultState = run;

            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void ApplySolidColor(GameObject character, Color color, string materialPath)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogWarning("URP Lit shader not found; keeping the imported material.");
                return;
            }

            EnsureFolder();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(lit);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.shader = lit;
            material.SetColor("_BaseColor", color);
            material.color = color;
            EditorUtility.SetDirty(material);

            Renderer[] renderers = character.GetComponentsInChildren<Renderer>(true);
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

        /// <summary>Replaces any previous child of that name with the model, sized and on its feet.</summary>
        private static GameObject PlaceCharacter(Transform parent, GameObject model, string name, float height)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            GameObject character = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            character.name = name;

            Transform t = character.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            if (TryGetBounds(character, out Bounds bounds) && bounds.size.y > 0.0001f)
            {
                float scale = height / bounds.size.y;
                t.localScale = new Vector3(scale, scale, scale);

                // Feet on the ground, wherever the model's own origin happens to be.
                TryGetBounds(character, out bounds);
                t.localPosition = new Vector3(0f, parent.position.y - bounds.min.y, 0f);
            }

            return character;
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

        private static void ApplySkin(GameObject runner, Texture2D skin, string materialPath)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (skin == null || lit == null)
            {
                Debug.LogWarning("Could not load the skin or the URP Lit shader; keeping the imported material.");
                return;
            }

            EnsureFolder();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(lit);
                AssetDatabase.CreateAsset(material, materialPath);
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
