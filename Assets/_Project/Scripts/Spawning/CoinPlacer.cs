using Runner.Config;
using Runner.Utils;
using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>
    /// Owns the coin pool and lays coins out — flat along a lane, or arced across a gap.
    /// </summary>
    /// <remarks>
    /// Now that score comes only from coins, where a coin sits is a design statement rather than
    /// decoration: on a roof it says climb, arced over a gap it says this jump is makeable. Worth
    /// its own class for the same reason the factory is.
    /// </remarks>
    public sealed class CoinPlacer
    {
        /// <summary>Degrees per second. One shared angle, so every coin turns in step.</summary>
        private const float SpinSpeed = 180f;

        /// <summary>Disc across, as a share of the trigger box width. Slightly larger reads better.</summary>
        private const float DiscDiameterScale = 1.3f;

        /// <summary>Disc thickness, as a share of the trigger box height.</summary>
        private const float DiscThicknessScale = 0.2f;

        /// <summary>Stands the cylinder primitive on its edge, face towards the camera.</summary>
        private static readonly Quaternion Upright = Quaternion.Euler(90f, 0f, 0f);

        private readonly ObjectPool<Coin> _pool;
        private readonly Material _material;
        private readonly CoinPlacerSettings _settings;

        /// <summary>Every generated disc, filled at warm-up. Spun from one loop, not per-coin Updates.</summary>
        private readonly System.Collections.Generic.List<Transform> _discs =
            new System.Collections.Generic.List<Transform>();

        public CoinPlacer(Transform poolRoot, CoinPlacerSettings settings, Coin prefab)
        {
            _settings = settings;
            _material = PrimitiveFactory.CreateSharedMaterial(settings.Color, "M_Coin");

            // Gold rather than yellow plastic: a little metal, a lot of shine.
            if (_material.HasProperty("_Metallic"))
            {
                _material.SetFloat("_Metallic", 0.75f);
            }

            if (_material.HasProperty("_Smoothness"))
            {
                _material.SetFloat("_Smoothness", 0.8f);
            }

            _pool = new ObjectPool<Coin>(
                () => Create(prefab), poolRoot, settings.Capacity, "Coin");
        }

        /// <summary>
        /// Turns every coin to the same angle. One rotation computed, then plain writes: no
        /// allocation and no per-coin Update. Pooled coins turn too; that is cheaper than
        /// checking which are live.
        /// </summary>
        public void Spin(float time)
        {
            Quaternion rotation = Quaternion.AngleAxis(time * SpinSpeed, Vector3.up) * Upright;

            for (int i = 0; i < _discs.Count; i++)
            {
                _discs[i].localRotation = rotation;
            }
        }

        public void Release(int handle) => _pool.Release(handle);

        /// <summary>A straight run of coins down one lane.</summary>
        public void EmitTrail(Segment segment, int lane, float localZ, float height)
        {
            float x = LaneToX(lane);
            float startZ = localZ - (_settings.PerRow - 1) * _settings.Spacing * 0.5f;

            for (int i = 0; i < _settings.PerRow; i++)
            {
                if (!TryPlace(segment, new Vector3(x, height, startZ + i * _settings.Spacing)))
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Coins along the exact parabola of a well-timed jump across a gap.
        /// </summary>
        /// <remarks>
        /// Computed, never authored. The same jump covers about 6.5m at the starting pace and
        /// roughly 16m at full tilt, so a drawn arc would be right at exactly one speed and lie
        /// everywhere else. Deriving it from the live jump profile keeps the line honest: if the
        /// coins say the jump is makeable, it is.
        /// </remarks>
        public void EmitArc(
            Segment segment, int lane, float localZ, float launchHeight, float speed, JumpProfile jump)
        {
            if (!jump.IsValid || _settings.ArcCount < 2)
            {
                EmitTrail(segment, lane, localZ, launchHeight + _settings.Height);
                return;
            }

            float airTime = jump.AirTime;
            float span = speed * airTime;
            float x = LaneToX(lane);
            float startZ = localZ - span * 0.5f;

            for (int i = 0; i < _settings.ArcCount; i++)
            {
                float t = airTime * i / (_settings.ArcCount - 1);

                Vector3 position = new Vector3(
                    x,
                    launchHeight + _settings.Height + jump.HeightAt(t),
                    startZ + speed * t);

                if (!TryPlace(segment, position))
                {
                    return;
                }
            }
        }

        public void Dispose() => ObjectUtility.Destroy(_material);

        private bool TryPlace(Segment segment, Vector3 localPosition)
        {
            if (!segment.HasCoinRoom)
            {
                return false;
            }

            Coin coin = _pool.Get(out int handle);

            Transform t = coin.transform;
            t.SetParent(segment.Root, false);
            t.localPosition = localPosition;

            segment.AddCoin(coin, handle);
            return true;
        }

        private float LaneToX(int lane) =>
            (lane - (TrackGenerator.LaneCount - 1) * 0.5f) * _settings.LaneWidth;

        private Coin Create(Coin prefab)
        {
            Coin coin;

            if (prefab != null)
            {
                coin = Object.Instantiate(prefab);
            }
            else
            {
                coin = CreateDisc();
            }

            coin.Initialize(_settings.Value);
            return coin;
        }

        /// <summary>
        /// An invisible trigger box, exactly the old coin's size so pickups behave as before,
        /// holding a gold disc that spins inside it.
        /// </summary>
        private Coin CreateDisc()
        {
            GameObject root = new GameObject("Coin");
            root.layer = _settings.Layer;

            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = _settings.Size;

            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            disc.layer = _settings.Layer;

            // The primitive brings a collider; the trigger on the root is the real one.
            ObjectUtility.Destroy(disc.GetComponent<Collider>());

            MeshRenderer renderer = disc.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // The cylinder primitive is 1 across and 2 tall, so halve the thickness.
            float diameter = _settings.Size.x * DiscDiameterScale;
            float thickness = _settings.Size.y * DiscThicknessScale;

            Transform discTransform = disc.transform;
            discTransform.SetParent(root.transform, false);
            discTransform.localScale = new Vector3(diameter, thickness * 0.5f, diameter);
            discTransform.localRotation = Upright;

            _discs.Add(discTransform);
            return root.AddComponent<Coin>();
        }
    }
}
