using UnityEditor;

namespace Runner.EditorTools
{
    /// <summary>
    /// Import settings for the runner character and its animations, applied automatically so
    /// nobody has to click through the Rig and Animation tabs.
    /// </summary>
    /// <remarks>
    /// Follows the setup the Universal Animation Library's author documents for Unity: bake
    /// axis conversion, Humanoid rig with an avatar made from the model, and looping on every
    /// clip whose name ends in _Loop. The Kenney character gets a Humanoid rig too, so the
    /// library's animations can be retargeted onto it.
    /// </remarks>
    public sealed class CharacterImportRules : AssetPostprocessor
    {
        public const string AnimationLibraryPath = "Assets/Quaternius/UniversalAnimationLibrary/UAL1_Standard.fbx";
        public const string KenneyCharacterPath = "Assets/Kenney/Characters/Model/characterMedium.fbx";

        private void OnPreprocessModel()
        {
            if (assetPath != AnimationLibraryPath && assetPath != KenneyCharacterPath)
            {
                return;
            }

            ModelImporter importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            if (assetPath == AnimationLibraryPath)
            {
                importer.bakeAxisConversion = true;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (assetPath != AnimationLibraryPath)
            {
                return;
            }

            ModelImporter importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;

            for (int i = 0; i < clips.Length; i++)
            {
                bool loops = clips[i].name.EndsWith("_Loop");
                clips[i].loopTime = loops;
                clips[i].loopPose = loops;
            }

            importer.clipAnimations = clips;
        }
    }
}
