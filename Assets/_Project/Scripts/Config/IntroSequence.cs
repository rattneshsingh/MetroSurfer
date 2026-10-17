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
        /// <summary>Bumped whenever the built-in defaults change.</summary>
        public const int CurrentDefaultsVersion = 2;

        [HideInInspector]
        [SerializeField] private int _defaultsVersion;

        public int DefaultsVersion => _defaultsVersion;

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
            // Twelve rows, about sixty metres, seven seconds. Long enough to name the three
            // verbs once each, short enough that it is not most of a run.
            //
            // It used to be thirty-one rows: a settle, three lessons, a full recap and two climb
            // sequences. That was a hundred and fifty-five metres of identical track at the front
            // of every single attempt, which made every run feel like the same run. Climbing does
            // not need teaching here -- the ramp chunks introduce it in play.
            _defaultsVersion = CurrentDefaultsVersion;

            _rows = new[]
            {
                Empty(false),
                Empty(true),

                // Move.
                Blocked(0b010, ObstacleKind.Full),
                Empty(true),

                // Jump.
                Blocked(ObstaclePattern.AllLanesMask, ObstacleKind.Low),
                Empty(true),

                // Roll.
                Blocked(ObstaclePattern.AllLanesMask, ObstacleKind.High),
                Empty(true),

                // One climb, so the stack is not a surprise later. It happens to you rather
                // than being asked of you.
                Blocked(0b010, ObstacleKind.Ramp),
                Blocked(0b010, ObstacleKind.Truck),
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
