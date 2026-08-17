using UnityEngine;

namespace Runner.Core
{
    /// <summary>
    /// How close the pursuer is. One number from 1 (out of sight) to 0 (caught).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Failure is graduated rather than binary. Clipping an obstacle while genuinely reacting to
    /// it costs ground instead of ending the run, which is the difference between forgiving and
    /// punishing — and the run only ends when you stop recovering.
    /// </para>
    /// <para>
    /// The rule that gives it teeth: he cannot follow you onto the stack. On the street the gap
    /// crawls back; on a roof it snaps back. So being chased is a reason to climb, which makes
    /// the traffic stack the answer to danger as well as the source of points, and makes an
    /// underpass while pursued genuinely frightening.
    /// </para>
    /// <para>Plain C#, so the rules can be tested without a scene.</para>
    /// </remarks>
    public sealed class PursuitState
    {
        private readonly float _stumbleCost;
        private readonly float _streetRecoveryPerSecond;
        private readonly float _climbRecoveryPerSecond;

        private float _distance;

        /// <summary>1 when he is out of sight, 0 when he has you.</summary>
        public float Distance => _distance;

        public bool IsCaught => _distance <= 0f;

        /// <summary>True while he is close enough to be worth showing.</summary>
        public bool IsPursuing => _distance < 1f;

        public PursuitState(float stumbleCost, float streetRecoveryPerSecond, float climbRecoveryPerSecond)
        {
            _stumbleCost = Mathf.Max(0.01f, stumbleCost);
            _streetRecoveryPerSecond = Mathf.Max(0f, streetRecoveryPerSecond);
            _climbRecoveryPerSecond = Mathf.Max(0f, climbRecoveryPerSecond);

            Reset();
        }

        public void Reset() => _distance = 1f;

        /// <summary>Costs ground. Returns true if that was the one that caught you.</summary>
        public bool Stumble()
        {
            _distance = Mathf.Max(0f, _distance - _stumbleCost);
            return IsCaught;
        }

        /// <summary>
        /// Recovers ground. Height is what decides the rate — the whole mechanic is that the
        /// street is where he catches you.
        /// </summary>
        public void Tick(float deltaTime, float supportHeight)
        {
            if (IsCaught || _distance >= 1f)
            {
                return;
            }

            float rate = supportHeight > 0f ? _climbRecoveryPerSecond : _streetRecoveryPerSecond;
            _distance = Mathf.Min(1f, _distance + rate * deltaTime);
        }
    }
}
