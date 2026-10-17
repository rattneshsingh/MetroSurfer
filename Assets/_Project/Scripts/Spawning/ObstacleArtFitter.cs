using Runner.Config;
using Runner.Utils;
using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>
    /// Dresses a generated obstacle in a state's model without touching its physics.
    /// </summary>
    /// <remarks>
    /// The generated colliders are the gameplay contract: roof height, lethal sides and the
    /// landing gap are all tested against them. So the model is visual only — the grey
    /// renderers are switched off, any colliders the model brings are removed, and the model
    /// is stretched to the definition's exact box so the roof you see is the roof you land
    /// on. Runs at pool warm-up, never during play, so its allocations never reach the frame
    /// loop.
    /// </remarks>
    public static class ObstacleArtFitter
    {
        public static void Dress(GameObject obstacle, ObstacleDefinition definition, GameObject art, float yaw = 0f)
        {
            if (obstacle == null || definition == null || art == null)
            {
                return;
            }

            Renderer[] generated = obstacle.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < generated.Length; i++)
            {
                generated[i].enabled = false;
            }

            // The wrapper takes the stretch along the obstacle's own axes; the model inside it
            // takes any turn. Scaling and rotating the same transform would shear the model.
            GameObject wrapper = new GameObject("Art");
            GameObject model = Object.Instantiate(art, wrapper.transform, false);
            model.name = art.name;
            StripColliders(model);

            if (!TryGetBounds(model, out Bounds bounds))
            {
                ObjectUtility.Destroy(wrapper);
                EnableAll(generated);
                return;
            }

            // Vehicles are long; obstacles run along Z. A model authored along X gets turned,
            // then the art's own yaw is added (180 for traffic facing the player).
            float turn = bounds.size.x > bounds.size.z ? 90f : 0f;
            if (turn + yaw != 0f)
            {
                model.transform.localRotation = Quaternion.Euler(0f, turn + yaw, 0f);
                TryGetBounds(model, out bounds);
            }

            // Bottom-centre of the model onto the wrapper's origin.
            model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            Vector3 size = definition.Size;
            wrapper.transform.localScale = new Vector3(
                SafeRatio(size.x, bounds.size.x),
                SafeRatio(size.y, bounds.size.y),
                SafeRatio(size.z, bounds.size.z));

            wrapper.transform.SetParent(obstacle.transform, false);
            wrapper.transform.localPosition = new Vector3(0f, definition.BaseHeight, 0f);
        }

        /// <summary>Combined renderer bounds in world space. Shared with SceneryBuilder.</summary>
        internal static bool TryGetBounds(GameObject model, out Bounds bounds)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
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

            return bounds.size.x > 0f && bounds.size.y > 0f && bounds.size.z > 0f;
        }

        internal static void StripColliders(GameObject model)
        {
            Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                ObjectUtility.Destroy(colliders[i]);
            }
        }

        private static void EnableAll(Renderer[] renderers)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = true;
            }
        }

        private static float SafeRatio(float target, float actual) => actual > 0.0001f ? target / actual : 1f;
    }
}
