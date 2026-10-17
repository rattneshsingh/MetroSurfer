using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// What one kind of obstacle looks like in one state. Visual only: the generated
    /// colliders, roof and lethal volume stay exactly as the catalogue builds them, and the
    /// model is stretched to fit that box.
    /// </summary>
    [System.Serializable]
    public sealed class ObstacleArt
    {
        public ObstacleKind Kind;

        [Tooltip("Model shown instead of the grey box. Any colliders on it are removed.")]
        public GameObject Visual;
    }
}
