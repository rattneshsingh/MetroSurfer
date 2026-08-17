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
        private readonly ObjectPool<Coin> _pool;
        private readonly Material _material;
        private readonly CoinPlacerSettings _settings;

        public CoinPlacer(Transform poolRoot, CoinPlacerSettings settings, Coin prefab)
        {
            _settings = settings;
            _material = PrimitiveFactory.CreateSharedMaterial(settings.Color, "M_Coin");

            _pool = new ObjectPool<Coin>(
                () => Create(prefab), poolRoot, settings.Capacity, "Coin");
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
                GameObject go = PrimitiveFactory.CreateCube(
                    "Coin", _settings.Size, _material, _settings.Layer,
                    PrimitiveFactory.ColliderMode.Trigger, castShadows: false);

                coin = go.AddComponent<Coin>();
            }

            coin.Initialize(_settings.Value);
            return coin;
        }
    }
}
