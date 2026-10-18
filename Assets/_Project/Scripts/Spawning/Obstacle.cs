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

        /// <summary>
        /// Everything that draws this obstacle: a state's model when it has been dressed,
        /// otherwise the generated mesh. Never the generated mesh hidden under a model.
        /// </summary>
        private Renderer[] _visible;

        /// <summary>The one renderer the brake-light material swap targets.</summary>
        private Renderer _renderer;
        private MovingObstacle _drift;
        private PitHole _pit;
        private bool _overhead;
        private Material _defaultMaterial;
        private bool _hidden;

        public ObstacleKind Kind => _kind;

        /// <summary>Null on anything that keeps pace with the world.</summary>
        public MovingObstacle Drift => _drift;

        /// <summary>The visible hole, on pits only. Cached at warm-up so placement never searches.</summary>
        public PitHole Pit => _pit;

        /// <summary>Passed under rather than around or over. See <see cref="Initialize"/>.</summary>
        public bool IsOverhead => _overhead;

        /// <summary>
        /// Called once at pool warm-up. Explicit rather than relying on Awake, because a
        /// generated instance has its component added after the GameObject already exists.
        /// </summary>
        /// <param name="overhead">
        /// True for anything the runner passes under. Hidden as soon as it is passed, so the
        /// chase camera never has to look back through it. Nothing else is ever hidden.
        /// </param>
        public void Initialize(ObstacleKind kind, bool overhead = false)
        {
            _kind = kind;
            _overhead = overhead;
            _visible = CollectVisibleRenderers();
            _renderer = _visible.Length > 0 ? _visible[0] : null;
            _drift = GetComponent<MovingObstacle>();
            _pit = GetComponent<PitHole>();

            if (_renderer != null)
            {
                _defaultMaterial = _renderer.sharedMaterial;
            }
        }

        public void OnSpawn()
        {
            Show();
            RestoreMaterial();
        }

        /// <summary>Swaps to a warning colour. Used for brake lights.</summary>
        public void SetMaterial(Material material)
        {
            if (_renderer != null && material != null)
            {
                _renderer.sharedMaterial = material;
            }
        }

        private void RestoreMaterial()
        {
            if (_renderer != null && _defaultMaterial != null)
            {
                _renderer.sharedMaterial = _defaultMaterial;
            }
        }

        public void OnDespawn()
        {
            if (_drift != null)
            {
                _drift.Stop();
            }
        }

        /// <summary>
        /// Stops drawing this once it is behind the player. An overhead bar passing within a
        /// couple of metres of the camera fills the entire frame and hides whatever is coming
        /// next — it has no gameplay meaning by then, so it should not be costing the player
        /// their read on the track.
        /// </summary>
        public void Hide()
        {
            if (_hidden)
            {
                return;
            }

            _hidden = true;
            SetVisible(false);
        }

        private void Show()
        {
            if (!_hidden)
            {
                return;
            }

            _hidden = false;
            SetVisible(true);
        }

        /// <summary>Indexed loop over the cached array: no allocation on spawn or pass.</summary>
        private void SetVisible(bool visible)
        {
            if (_visible == null)
            {
                return;
            }

            for (int i = 0; i < _visible.Length; i++)
            {
                _visible[i].enabled = visible;
            }
        }

        /// <summary>
        /// The renderers that are switched on at warm-up. A dressed obstacle has already had its
        /// generated mesh switched off, so this picks up only the model. Warm-up only.
        /// </summary>
        private Renderer[] CollectVisibleRenderers()
        {
            // Landable pieces keep their mesh on a child, so search downward.
            Renderer[] all = GetComponentsInChildren<Renderer>(true);

            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].enabled)
                {
                    count++;
                }
            }

            Renderer[] visible = new Renderer[count];
            int next = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].enabled)
                {
                    visible[next++] = all[i];
                }
            }

            return visible;
        }
    }
}
