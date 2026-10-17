using Runner.Config;
using Runner.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace Runner.Spawning
{
    /// <summary>
    /// Lines both sides of a track segment with a state's street: pavement, a ground lot,
    /// buildings and kerbside props. Everything is parented to the segment, so it scrolls and
    /// recycles with it at no per-frame cost.
    /// </summary>
    /// <remarks>
    /// Runs once per segment at warm-up, never during play. Scenery is purely visual: models
    /// lose their colliders and do not cast shadows, which matters on the target phone and
    /// keeps the physics scene exactly the track. Picks come from the spawner's seeded
    /// presentation RNG, so a seed always builds the same city.
    /// </remarks>
    public sealed class SceneryBuilder
    {
        /// <summary>Top of the pavement and lot, a kerb's height above the road.</summary>
        private const float KerbHeight = 0.15f;

        /// <summary>How far the lot runs out from the pavement. Fog hides its far edge.</summary>
        private const float LotWidth = 40f;

        /// <summary>Gap left between neighbouring buildings.</summary>
        private const float BuildingGap = 0.5f;

        /// <summary>Kerbside props stand this far out from the track edge.</summary>
        private const float PropInset = 0.6f;

        private readonly StateConfig _state;
        private readonly float _trackHalfWidth;
        private readonly float _segmentLength;
        private readonly System.Random _rng;
        private readonly int _layer;
        private readonly Material _sidewalkMaterial;
        private readonly Material _lotMaterial;

        public SceneryBuilder(StateConfig state, float trackHalfWidth, float segmentLength, System.Random rng, int layer)
        {
            _state = state;
            _trackHalfWidth = trackHalfWidth;
            _segmentLength = segmentLength;
            _rng = rng;
            _layer = layer;

            _sidewalkMaterial = PrimitiveFactory.CreateSharedMaterial(state.SidewalkColor, "M_Sidewalk");
            _lotMaterial = PrimitiveFactory.CreateSharedMaterial(state.LotColor, "M_Lot");
        }

        public void Dress(Transform segment)
        {
            DressSide(segment, -1f);
            DressSide(segment, 1f);
        }

        private void DressSide(Transform segment, float side)
        {
            float sidewalk = _state.SidewalkWidth;
            float frontage = _trackHalfWidth + sidewalk;

            // Slabs reach down past the road surface so no seam shows at the kerb.
            AddSlab(segment, "Sidewalk", _sidewalkMaterial, side, _trackHalfWidth, sidewalk);
            AddSlab(segment, "Lot", _lotMaterial, side, frontage, LotWidth);

            float slot = Mathf.Max(1f, _state.BuildingSlotLength);
            for (float z = BuildingGap * 0.5f; z + slot <= _segmentLength + 0.01f; z += slot + BuildingGap)
            {
                GameObject building = Pick(_state.Buildings);
                if (building != null)
                {
                    PlaceBuilding(segment, building, side, frontage, z, slot);
                }
            }

            GameObject prop = Pick(_state.StreetProps);
            if (prop != null)
            {
                PlaceProp(segment, prop, side);
            }
        }

        private void AddSlab(Transform segment, string name, Material material, float side, float innerEdge, float width)
        {
            const float depth = 0.65f;
            GameObject slab = PrimitiveFactory.CreateCube(
                name, new Vector3(width, depth, _segmentLength), material, _layer,
                PrimitiveFactory.ColliderMode.None, castShadows: false);

            slab.transform.SetParent(segment, false);
            slab.transform.localPosition = new Vector3(
                side * (innerEdge + width * 0.5f), KerbHeight - depth * 0.5f, _segmentLength * 0.5f);
        }

        /// <summary>Front to the road, scaled evenly to the slot, back corner on the frontage line.</summary>
        private void PlaceBuilding(Transform segment, GameObject prefab, float side, float frontage, float z, float slot)
        {
            // Left side faces +X, right side faces -X; the state's facing corrects the model's own.
            float yaw = (side < 0f ? 90f : -90f) + _state.BuildingFacing;
            Transform t = Spawn(segment, prefab, yaw);

            if (!Measure(t, segment, out Bounds bounds) || bounds.size.z <= 0.0001f)
            {
                return;
            }

            float scale = slot / bounds.size.z;
            t.localScale = new Vector3(scale, scale, scale);
            Measure(t, segment, out bounds);

            float nearEdge = side < 0f ? bounds.max.x : bounds.min.x;
            t.localPosition += new Vector3(
                side * frontage - nearEdge,
                KerbHeight - bounds.min.y,
                z - bounds.min.z);
        }

        private void PlaceProp(Transform segment, GameObject prefab, float side)
        {
            Transform t = Spawn(segment, prefab, side < 0f ? 90f : -90f);

            if (!Measure(t, segment, out Bounds bounds) || bounds.size.y <= 0.0001f)
            {
                return;
            }

            float scale = _state.StreetPropHeight / bounds.size.y;
            t.localScale = new Vector3(scale, scale, scale);
            Measure(t, segment, out bounds);

            t.localPosition += new Vector3(
                side * (_trackHalfWidth + PropInset) - bounds.center.x,
                KerbHeight - bounds.min.y,
                -bounds.center.z);
        }

        private Transform Spawn(Transform segment, GameObject prefab, float yaw)
        {
            GameObject instance = Object.Instantiate(prefab, segment, false);
            instance.name = prefab.name;
            instance.layer = _layer;
            ObstacleArtFitter.StripColliders(instance);

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            }

            Transform t = instance.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            t.localScale = Vector3.one;
            return t;
        }

        /// <summary>Bounds in the segment's space. Segments are never rotated, so this is a shift.</summary>
        private static bool Measure(Transform model, Transform segment, out Bounds bounds)
        {
            if (!ObstacleArtFitter.TryGetBounds(model.gameObject, out bounds))
            {
                return false;
            }

            bounds.center -= segment.position;
            return true;
        }

        private GameObject Pick(GameObject[] options)
        {
            if (options == null || options.Length == 0)
            {
                return null;
            }

            return options[_rng.Next(0, options.Length)];
        }
    }
}
