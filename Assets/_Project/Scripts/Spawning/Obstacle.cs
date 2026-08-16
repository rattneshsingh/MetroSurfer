using Runner.Config;
using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>
    /// A pooled hazard. Deliberately dumb — it holds no behaviour, because everything that
    /// moves it is driven by <see cref="SegmentSpawner"/> in one batched loop rather than by
    /// per-object Update calls.
    /// </summary>
    public sealed class Obstacle : MonoBehaviour
    {
        [SerializeField] private ObstacleKind _kind;

        public ObstacleKind Kind => _kind;

        /// <summary>
        /// Called once at pool warm-up. Explicit rather than relying on Awake, because a
        /// generated instance has its component added after the GameObject already exists.
        /// </summary>
        public void Initialize(ObstacleKind kind)
        {
            _kind = kind;
        }
    }
}
