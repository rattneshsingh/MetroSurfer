using Runner.Utils;
using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>A pooled pickup. Collecting hides it; its handle returns to the pool when the
    /// owning segment recycles.</summary>
    public sealed class Coin : MonoBehaviour, IPoolable
    {
        [SerializeField] private int _value = 1;

        private Renderer _renderer;
        private Collider _collider;
        private bool _collected;

        public int Value => _value;
        public bool Collected => _collected;

        /// <summary>Called once at pool warm-up — see <see cref="Obstacle.Initialize"/>.</summary>
        public void Initialize(int value)
        {
            _value = value;
            _renderer = GetComponent<Renderer>();
            _collider = GetComponent<Collider>();
        }

        public void OnSpawn()
        {
            _collected = false;
            SetVisible(true);
        }

        public void OnDespawn()
        {
        }

        /// <summary>Returns false if this coin was already taken this pass.</summary>
        public bool TryCollect(out int value)
        {
            if (_collected)
            {
                value = 0;
                return false;
            }

            _collected = true;
            SetVisible(false);
            value = _value;
            return true;
        }

        private void SetVisible(bool visible)
        {
            if (_renderer != null)
            {
                _renderer.enabled = visible;
            }

            if (_collider != null)
            {
                _collider.enabled = visible;
            }
        }
    }
}
