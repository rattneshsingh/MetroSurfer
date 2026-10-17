using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// Scoring, the chase, and the cost of a stumble. Everything about how a run is judged,
    /// kept in an asset for the same reason as <see cref="PlayerTuning"/> — Play-mode edits to
    /// a component are thrown away, and these are found by playing.
    /// </summary>
    [CreateAssetMenu(menuName = "Runner/Run Tuning", fileName = "RunTuning")]
    public sealed class RunTuning : ScriptableObject
    {
        [Header("Scoring — coins only. Distance drives difficulty, not the scoreboard.")]
        public int PointsPerCoin = 5;

        [Tooltip("Extra multiplier per unit of height, applied when the coin is taken. The " +
                 "coins are on the roofs, so this is what makes the climb worth the risk.")]
        public float HeightBonusPerUnit = 0.6f;

        [Header("Streak")]
        public int CoinsPerTier = 8;
        public int MaxMultiplier = 5;

        [Tooltip("Metres without a coin before the streak resets. Distance, not seconds — a " +
                 "timer would be far kinder early in a run than late.")]
        public float StreakBreakDistance = 60f;

        [Header("Pursuit")]
        [Tooltip("Ground lost per stumble. At 0.34 it takes three to be caught.")]
        public float StumbleCost = 0.34f;

        [Tooltip("Ground regained per second on the street. Deliberately slow.")]
        public float StreetRecoveryPerSecond = 0.12f;

        [Tooltip("Ground regained per second up on the stack. He cannot follow you there, so " +
                 "being chased is a reason to climb.")]
        public float ClimbRecoveryPerSecond = 0.6f;

        [Tooltip("Seconds of immunity after a stumble, so one obstacle cannot hit you twice " +
                 "while it passes through you.")]
        public float RecoveryWindow = 0.7f;

        [Header("Stumble cost")]
        [Tooltip("Speed immediately after a stumble, as a fraction of normal.")]
        [Range(0.2f, 1f)] public float StumbleSpeedMultiplier = 0.55f;

        [Tooltip("How quickly that speed comes back, per second.")]
        public float StumbleSpeedRecoveryPerSecond = 0.9f;

        [Header("Feel")]
        [Tooltip("Beat between the hit landing and the game-over panel appearing.")]
        public float DeathFreezeDuration = 0.45f;

        public static RunTuning CreateDefault()
        {
            RunTuning tuning = CreateInstance<RunTuning>();
            tuning.name = "RunTuning (runtime default)";
            return tuning;
        }
    }
}
