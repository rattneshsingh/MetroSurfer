using UnityEngine;

namespace Runner.Utils
{
    /// <summary>Lifetime helpers that behave correctly whether or not the game is running.</summary>
    public static class ObjectUtility
    {
        /// <summary>
        /// Destroys an object from either edit mode or play mode.
        /// </summary>
        /// <remarks>
        /// Object.Destroy defers to the end of the frame, and edit mode has no frame — Unity
        /// refuses the call and logs an error, leaving the object alive. Any code that can run
        /// from an editor script or an EditMode test has to branch here, and every pool warm-up
        /// path in this project can.
        /// </remarks>
        public static void Destroy(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
