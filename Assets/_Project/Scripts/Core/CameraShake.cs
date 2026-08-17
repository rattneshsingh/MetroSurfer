using UnityEngine;

namespace Runner.Core
{
    /// <summary>
    /// A short, sharp camera knock. The cheapest "I felt that" lever there is.
    /// </summary>
    /// <remarks>
    /// Offsets from a base position captured at Awake rather than accumulating, so a shake
    /// interrupted by another one cannot drift the camera off its framing. Runs in LateUpdate so
    /// it applies after anything that positions the camera.
    /// </remarks>
    public sealed class CameraShake : MonoBehaviour
    {
        [SerializeField] private float _defaultStrength = 0.28f;
        [SerializeField] private float _defaultDuration = 0.35f;

        [Tooltip("Higher is more jittery, lower is more of a lurch.")]
        [SerializeField] private float _frequency = 26f;

        private Transform _transform;
        private Vector3 _basePosition;
        private float _duration;
        private float _remaining;
        private float _strength;
        private float _seed;

        private void Awake()
        {
            _transform = transform;
            _basePosition = _transform.localPosition;
            _seed = Random.value * 100f;
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

            if (_transform != null)
            {
                _transform.localPosition = _basePosition;
            }
        }

        private void LateUpdate()
        {
            if (_remaining <= 0f)
            {
                return;
            }

            _remaining -= Time.deltaTime;

            if (_remaining <= 0f)
            {
                _transform.localPosition = _basePosition;
                return;
            }

            // Squared falloff: hits hard and settles quickly, rather than wobbling out.
            float life = _remaining / _duration;
            float amplitude = _strength * life * life;
            float time = Time.time * _frequency;

            // Perlin rather than Random, so the motion is a shudder instead of static.
            float x = (Mathf.PerlinNoise(_seed + time, 0f) - 0.5f) * 2f * amplitude;
            float y = (Mathf.PerlinNoise(0f, _seed + time) - 0.5f) * 2f * amplitude;

            _transform.localPosition = _basePosition + new Vector3(x, y, 0f);
        }
    }
}
