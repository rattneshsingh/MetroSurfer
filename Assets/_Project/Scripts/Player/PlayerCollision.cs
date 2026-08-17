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

        [Tooltip("Read to decide whether a hit was a stumble or a flat-footed collision.")]
        [SerializeField] private LaneController _lane;

        [Tooltip("A jump or roll this recently still counts as reacting, even if it has already " +
                 "finished. Mistiming an attempt should cost ground, not the run.")]
        [SerializeField] private float _reactionWindow = 0.35f;

        private int _obstacleLayer = -1;
        private int _coinLayer = -1;
        private bool _armed = true;

        /// <summary>Raised when the player hits an obstacle, with how badly.</summary>
        public event System.Action<HitSeverity> Hit;

        /// <summary>Raised with the coin's value.</summary>
        public event System.Action<int> CoinCollected;

        private void Awake()
        {
            if (_lane == null)
            {
                _lane = GetComponent<LaneController>();
            }

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

        /// <summary>
        /// Were you doing something when you hit it? Mid lane-change, airborne or rolling all
        /// mean you were reacting, and reacting badly should cost ground rather than the run.
        /// Standing still and walking into it means you were not reacting at all.
        /// </summary>
        private HitSeverity Classify()
        {
            if (_lane == null)
            {
                return HitSeverity.HeadOn;
            }

            bool reacting =
                _lane.IsChangingLanes ||
                !_lane.IsGrounded ||
                _lane.IsSliding ||
                _lane.TimeSinceAction <= _reactionWindow;

            return reacting ? HitSeverity.Glancing : HitSeverity.HeadOn;
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
                // Disarmed until the run's owner re-arms, so one obstacle cannot register
                // three separate stumbles as it passes through you.
                _armed = false;
                Hit?.Invoke(Classify());
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
