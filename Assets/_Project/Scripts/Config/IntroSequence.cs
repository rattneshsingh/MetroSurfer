using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// The authored opening of a run. Plays before procedural generation takes over, so the
    /// first thirty seconds teach the three verbs in a fixed, fair order rather than throwing
    /// random rows at a player who has not learnt them yet.
    /// </summary>
    /// <remarks>
    /// Each entry is one row, spaced by the spawner's row spacing. Empty rows are deliberate —
    /// they are the breathing room that makes a lesson readable.
    /// </remarks>
    [CreateAssetMenu(menuName = "Runner/Intro Sequence", fileName = "IntroSequence")]
    public sealed class IntroSequence : ScriptableObject
    {
        [SerializeField] private IntroRow[] _rows;

        public int Count => _rows == null ? 0 : _rows.Length;

        public IntroRow Get(int index) => _rows[index];

        /// <summary>Runtime stand-in used when no asset is assigned.</summary>
        public static IntroSequence CreateDefault()
        {
            IntroSequence sequence = CreateInstance<IntroSequence>();
            sequence.name = "IntroSequence (runtime default)";
            sequence.FillDefaults();
            return sequence;
        }

        private void Reset() => FillDefaults();

        private void FillDefaults()
        {
            _rows = new[]
            {
                // Settle. Let the player feel the speed and find the lanes before anything asks
                // something of them.
                Empty(false),
                Empty(true),
                Empty(false),
                Empty(true),

                // Lesson 1 — move. Middle lane blocked, both sides open.
                Blocked(0b010, ObstacleKind.Full),
                Empty(true),
                Empty(false),

                // Lesson 2 — jump. Full width, low enough to clear.
                Blocked(ObstaclePattern.AllLanesMask, ObstacleKind.Low),
                Empty(true),
                Empty(false),

                // Lesson 3 — slide. Full width, overhead.
                Blocked(ObstaclePattern.AllLanesMask, ObstacleKind.High),
                Empty(true),
                Empty(false),

                // Recap, now with a direction forced each time.
                Blocked(0b011, ObstacleKind.Full),
                Empty(true),
                Blocked(0b110, ObstacleKind.Full),
                Empty(true),
                Blocked(ObstaclePattern.AllLanesMask, ObstacleKind.Low),
                Empty(true),
                Blocked(ObstaclePattern.AllLanesMask, ObstacleKind.High),
                Empty(true),
                Empty(false)
            };
        }

        private static IntroRow Empty(bool coins) => new IntroRow
        {
            LaneMask = 0,
            Kind = ObstacleKind.Low,
            Coins = coins
        };

        private static IntroRow Blocked(int laneMask, ObstacleKind kind) => new IntroRow
        {
            LaneMask = laneMask,
            Kind = kind,
            Coins = false
        };
    }
}
