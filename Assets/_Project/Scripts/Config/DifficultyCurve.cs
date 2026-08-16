using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// Speed and density ramp over run distance. Every value here is meant to be dragged in
    /// the Inspector while the game is running — that is the point of it being an asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Runner/Difficulty Curve", fileName = "DifficultyCurve")]
    public sealed class DifficultyCurve : ScriptableObject
    {
        [Header("Speed (metres/second)")]
        [SerializeField] private float _startSpeed = 8f;
        [SerializeField] private float _maxSpeed = 26f;

        [Tooltip("Distance in metres over which speed climbs from start to max.")]
        [SerializeField] private float _rampDistance = 2200f;

        [Tooltip("Normalised distance (x) to normalised speed (y).")]
        [SerializeField] private AnimationCurve _speedOverDistance = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Obstacle density (chance a given row spawns something)")]
        [SerializeField, Range(0f, 1f)] private float _startObstacleDensity = 0.30f;
        [SerializeField, Range(0f, 1f)] private float _maxObstacleDensity = 0.80f;
        [SerializeField] private AnimationCurve _obstacleDensityOverDistance = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Coins")]
        [SerializeField, Range(0f, 1f)] private float _coinRowChance = 0.45f;

        public float StartSpeed => _startSpeed;

        /// <summary>
        /// AnimationCurve.Evaluate is a native call with no managed allocation, so this is
        /// safe to call every frame from Update.
        /// </summary>
        public float EvaluateSpeed(float distance)
        {
            float t = NormalisedDistance(distance);
            return Mathf.Lerp(_startSpeed, _maxSpeed, _speedOverDistance.Evaluate(t));
        }

        public float EvaluateObstacleDensity(float distance)
        {
            float t = NormalisedDistance(distance);
            return Mathf.Lerp(_startObstacleDensity, _maxObstacleDensity, _obstacleDensityOverDistance.Evaluate(t));
        }

        public float EvaluateCoinChance(float distance) => _coinRowChance;

        /// <summary>Runtime stand-in used when no asset is assigned, so the game runs out of the box.</summary>
        public static DifficultyCurve CreateDefault()
        {
            DifficultyCurve curve = CreateInstance<DifficultyCurve>();
            curve.name = "DifficultyCurve (runtime default)";
            return curve;
        }

        private float NormalisedDistance(float distance)
        {
            if (_rampDistance <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01(distance / _rampDistance);
        }
    }
}
