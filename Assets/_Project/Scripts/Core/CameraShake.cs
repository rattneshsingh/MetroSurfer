using UnityEngine;

namespace Runner.Core
{
    /// <summary>
    /// The camera's framing: a lateral follow that keeps the runner on screen, plus a short,
    /// sharp knock on impact — the cheapest "I felt that" lever there is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything is an offset from a base position captured at Awake rather than accumulated,
    /// so neither a shake interrupted by another one nor the follow can drift the camera off
    /// its framing. Runs in LateUpdate so it applies after anything that moves the player.
    /// </para>
    /// <para>
    /// The follow only tracks part of the player's lane offset. A portrait screen is narrow,
    /// so a fixed camera loses an outer-lane runner off the edge; a fully locked camera hides
    /// the lane change itself. Part-way keeps both.
    /// </para>
    /// <para>
    /// Kept under its original name because the scene references the component; renaming it
    /// would break that reference for no behavioural gain.
    /// </para>
    /// </remarks>
    public sealed class CameraShake : MonoBehaviour
    {
        [SerializeField] private float _defaultStrength = 0.28f;
        [SerializeField] private float _defaultDuration = 0.35f;

        [Tooltip("Higher is more jittery, lower is more of a lurch.")]
        [SerializeField] private float _frequency = 26f;

        [Header("Follow")]
        [Tooltip("Share of the runner's sideways offset the camera follows. 0 never moves, 1 locks on.")]
        [SerializeField, Range(0f, 1f)] private float _followFraction = 0.65f;

        [Tooltip("How quickly the camera catches up with a lane change. Higher is snappier.")]
        [SerializeField] private float _followSharpness = 9f;

        [Tooltip("Share of the height the runner is standing at (a roof, a train) the camera rises " +
                 "by. Without it, a runner on a train roof is at the camera's own height.")]
        [SerializeField, Range(0f, 1f)] private float _heightFraction = 0.85f;

        [Tooltip("How quickly the camera rises and settles with the runner's footing. Lower than " +
                 "the sideways follow, so a jump does not bob the view.")]
        [SerializeField] private float _heightSharpness = 4f;

        private Transform _transform;
        private Vector3 _basePosition;
        private float _duration;
        private float _remaining;
        private float _strength;
        private float _seed;

        private float _followTarget;
        private float _followX;
        private float _heightTarget;
        private float _followY;

        private void Awake()
        {
            _transform = transform;
            _basePosition = _transform.localPosition;
            _seed = Random.value * 100f;
        }

        /// <summary>
        /// Where the runner is sideways, and the height of what they are standing on. Call every
        /// frame; it is two float writes. Footing, not the jump arc: following every jump would
        /// bob the whole view up and down.
        /// </summary>
        public void Follow(float playerX, float supportHeight)
        {
            _followTarget = playerX * _followFraction;
            _heightTarget = supportHeight * _heightFraction;
        }

        public void Shake() => Shake(_defaultStrength, _defaultDuration);

        public void Shake(float strength, float duration)
        {
            _strength = Mathf.Max(strength, _strength * (_remaining / Mathf.Max(0.0001f, _duration)));
            _duration = Mathf.Max(0.01f, duration);
            _remaining = _duration;
            _seed = Random.value * 100f;
        }

        public void StopShake()
        {
            _remaining = 0f;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            // Exponential approach: frame-rate independent and never overshoots.
            _followX = Mathf.Lerp(_followX, _followTarget, 1f - Mathf.Exp(-_followSharpness * dt));
            _followY = Mathf.Lerp(_followY, _heightTarget, 1f - Mathf.Exp(-_heightSharpness * dt));

            Vector3 shake = Vector3.zero;
            if (_remaining > 0f)
            {
                _remaining -= dt;

                if (_remaining > 0f)
                {
                    // Squared falloff: hits hard and settles quickly, rather than wobbling out.
                    float life = _remaining / _duration;
                    float amplitude = _strength * life * life;
                    float time = Time.time * _frequency;

                    // Perlin rather than Random, so the motion is a shudder instead of static.
                    shake.x = (Mathf.PerlinNoise(_seed + time, 0f) - 0.5f) * 2f * amplitude;
                    shake.y = (Mathf.PerlinNoise(0f, _seed + time) - 0.5f) * 2f * amplitude;
                }
            }

            _transform.localPosition = _basePosition + new Vector3(_followX, _followY, 0f) + shake;
        }
    }
}
