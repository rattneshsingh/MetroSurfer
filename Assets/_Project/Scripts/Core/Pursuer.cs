using UnityEngine;

namespace Runner.Core
{
    /// <summary>
    /// The visible chaser. Reads one number and turns it into something you can feel behind you.
    /// </summary>
    /// <remarks>
    /// Deliberately dumb: it holds no rules and makes no decisions, so the pursuit logic stays
    /// in a plain class that can be tested. This is the readout, not the mechanic.
    /// </remarks>
    public sealed class Pursuer : MonoBehaviour
    {
        [Tooltip("Z when he is on your heels. Just behind the player and large in frame.")]
        [SerializeField] private float _nearZ = -1.2f;

        [Tooltip("Z at his most distant while still pursuing. Must stay clearly in front of the " +
                 "camera: anything past it is invisible, and anything very close to it fills the " +
                 "screen. With the camera at -6.4 the usable band is roughly -1 to -4.5.")]
        [SerializeField] private float _farZ = -4.5f;

        [Tooltip("How quickly he slides to his new position. Snapping reads as teleporting.")]
        [SerializeField] private float _followSharpness = 9f;

        [Tooltip("How quickly he lines up with the player's lane.")]
        [SerializeField] private float _lateralSharpness = 6f;

        [SerializeField] private Transform _visual;

        private Transform _transform;
        private float _currentZ;
        private bool _shown;

        private void Awake()
        {
            _transform = transform;
            _currentZ = _farZ;

            // Set explicitly rather than through Show: the flag starts matching, so the guard
            // there would skip the call and leave him on screen from the first frame.
            if (_visual != null)
            {
                _visual.gameObject.SetActive(false);
            }

            _shown = false;
        }

        public void Reset()
        {
            _currentZ = _farZ;

            if (_visual != null)
            {
                _visual.gameObject.SetActive(false);
            }

            _shown = false;

            if (_transform == null)
            {
                _transform = transform;
            }

            _transform.localPosition = new Vector3(0f, 0f, _farZ);
        }

        /// <param name="distance">1 when out of sight, 0 when he has you.</param>
        /// <param name="playerX">Lane position to line up with.</param>
        public void UpdatePursuit(float distance, float playerX, float deltaTime, bool pursuing)
        {
            Show(pursuing);

            if (!pursuing)
            {
                _currentZ = _farZ;
                return;
            }

            float targetZ = Mathf.Lerp(_nearZ, _farZ, distance);

            // Exponential smoothing rather than a fixed lerp factor, so the feel does not change
            // with frame rate.
            _currentZ = Mathf.Lerp(_currentZ, targetZ, 1f - Mathf.Exp(-_followSharpness * deltaTime));

            Vector3 position = _transform.localPosition;
            position.x = Mathf.Lerp(position.x, playerX, 1f - Mathf.Exp(-_lateralSharpness * deltaTime));
            position.z = _currentZ;
            _transform.localPosition = position;
        }

        private void Show(bool visible)
        {
            if (_shown == visible || _visual == null)
            {
                return;
            }

            _shown = visible;
            _visual.gameObject.SetActive(visible);
        }
    }
}
