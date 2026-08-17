using UnityEngine;
using UnityEngine.Rendering;

namespace Runner.Utils
{
    /// <summary>
    /// Builds the Phase 0 grey-cube stand-ins in code so the game runs without any authored
    /// prefabs. Load-time only — every caller is a pool warm-up factory. Delete or bypass
    /// this once real art exists; the pools already accept a serialized prefab instead.
    /// </summary>
    public static class PrimitiveFactory
    {
        private const string LitShaderName = "Universal Render Pipeline/Lit";
        private const string FallbackShaderName = "Standard";

        /// <summary>
        /// One shared material per colour keeps the draw call count down — instancing is on,
        /// so identical cubes batch.
        /// </summary>
        public static Material CreateSharedMaterial(Color color, string name)
        {
            // Explicit == check, not ??. The null-coalescing operator bypasses Unity's
            // overloaded equality and would sail straight past a destroyed/missing object.
            Shader shader = Shader.Find(LitShaderName);
            if (shader == null)
            {
                shader = Shader.Find(FallbackShaderName);
            }

            Material material = new Material(shader)
            {
                name = name,
                color = color,
                enableInstancing = true
            };
            return material;
        }

        /// <summary>What to do with the collider the primitive ships with.</summary>
        public enum ColliderMode
        {
            /// <summary>Strip it. Decoration only.</summary>
            None,

            /// <summary>Trigger, for lethal volumes.</summary>
            Trigger,

            /// <summary>Solid, for surfaces the ground probe raycasts against.</summary>
            Solid
        }

        public static GameObject CreateCube(
            string name,
            Vector3 size,
            Material material,
            int layer,
            ColliderMode colliderMode,
            bool castShadows)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.transform.localScale = size;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = castShadows;
            // No per-object lighting data needed for untextured cubes.
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            BoxCollider collider = go.GetComponent<BoxCollider>();
            switch (colliderMode)
            {
                case ColliderMode.Trigger:
                    collider.isTrigger = true;
                    break;

                case ColliderMode.Solid:
                    collider.isTrigger = false;
                    break;

                default:
                    // Warm-up runs from EditMode tests and editor tooling as well as from play,
                    // so this cannot use the deferred Destroy.
                    ObjectUtility.Destroy(collider);
                    break;
            }

            return go;
        }

        /// <summary>Empty root with a kinematic body — see <c>Segment</c> for why that matters.</summary>
        public static GameObject CreateKinematicRoot(string name)
        {
            GameObject go = new GameObject(name);

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;

            return go;
        }
    }
}
