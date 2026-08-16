using Runner.Config;
using Runner.Utils;
using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>
    /// A pooled hazard. Deliberately dumb — it holds no behaviour, because everything that
    /// moves it is driven by <see cref="SegmentSpawner"/> in one batched loop rather than by
    /// per-object Update calls.
    /// </summary>
    public sealed class Obstacle : MonoBehaviour, IPoolable
    {
        [SerializeField] private ObstacleKind _kind;

        private Renderer _renderer;
        private bool _hidden;

        public ObstacleKind Kind => _kind;

        /// <summary>
        /// Called once at pool warm-up. Explicit rather than relying on Awake, because a
        /// generated instance has its component added after the GameObject already exists.
        /// </summary>
        public void Initialize(ObstacleKind kind)
        {
            _kind = kind;
            _renderer = GetComponent<Renderer>();
        }

        public void OnSpawn()
        {
            Show();
        }

        public void OnDespawn()
        {
        }

        /// <summary>
        /// Stops drawing this once it is behind the player. An overhead bar passing within a
        /// couple of metres of the camera fills the entire frame and hides whatever is coming
        /// next — it has no gameplay meaning by then, so it should not be costing the player
        /// their read on the track.
        /// </summary>
        public void Hide()
        {
            if (_hidden || _renderer == null)
            {
                return;
            }

            _hidden = true;
            _renderer.enabled = false;
        }

        private void Show()
        {
            if (!_hidden || _renderer == null)
            {
                _hidden = false;
                return;
            }

            _hidden = false;
            _renderer.enabled = true;
        }
    }
}
