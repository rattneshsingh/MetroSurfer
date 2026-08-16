using Runner.Spawning;
using UnityEngine;

namespace Runner.Player
{
    /// <summary>
    /// Single place where hitting things turns into game outcomes. Raises events rather than
    /// calling GameManager directly, so nothing under Runner.Player depends on Runner.Core.
    /// </summary>
    /// <remarks>
    /// Requires a kinematic Rigidbody on this GameObject — a trigger only reports against
    /// another trigger when one of the pair carries a body. The obstacles and coins are
    /// body-less triggers, so the player is the one that has to have it.
    /// </remarks>
    public sealed class PlayerCollision : MonoBehaviour
    {
        [SerializeField] private string _obstacleLayerName = "Obstacle";
        [SerializeField] private string _coinLayerName = "Coin";

        private int _obstacleLayer = -1;
        private int _coinLayer = -1;
        private bool _armed = true;

        /// <summary>Raised once per run, when the player hits an obstacle.</summary>
        public event System.Action Hit;

        /// <summary>Raised with the coin's value.</summary>
        public event System.Action<int> CoinCollected;

        private void Awake()
        {
            _obstacleLayer = LayerMask.NameToLayer(_obstacleLayerName);
            _coinLayer = LayerMask.NameToLayer(_coinLayerName);

            if (_obstacleLayer < 0 || _coinLayer < 0)
            {
                Debug.LogError(
                    $"[{nameof(PlayerCollision)}] Missing layers. Add '{_obstacleLayerName}' and " +
                    $"'{_coinLayerName}' under Project Settings > Tags and Layers.", this);
            }
        }

        /// <summary>Re-enables hit detection for a fresh run.</summary>
        public void Arm()
        {
            _armed = true;
        }

        public void Disarm()
        {
            _armed = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_armed)
            {
                return;
            }

            int layer = other.gameObject.layer;

            if (layer == _obstacleLayer)
            {
                _armed = false;
                Hit?.Invoke();
                return;
            }

            if (layer != _coinLayer)
            {
                return;
            }

            // TryGetComponent does not allocate, and this runs at pickup rate — a few times a
            // second — not every frame.
            if (other.TryGetComponent(out Coin coin) && coin.TryCollect(out int value))
            {
                CoinCollected?.Invoke(value);
            }
        }
    }
}
