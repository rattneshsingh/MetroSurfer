using UnityEngine;

namespace Runner.Config
{
    /// <summary>One authored row of obstacles: which lanes are occupied, and by what.</summary>
    /// <remarks>
    /// Public fields rather than [SerializeField] private — this is a plain serializable data
    /// struct inside a ScriptableObject, not a MonoBehaviour tunable, and properties on a
    /// struct would just add copies.
    /// </remarks>
    [System.Serializable]
    public struct ObstaclePattern
    {
        public const int AllLanesMask = 0b111;

        [Tooltip("Bit per lane: 1 = left, 2 = middle, 4 = right. Combine for multi-lane rows.")]
        [Range(1, AllLanesMask)]
        public int LaneMask;

        public ObstacleKind Kind;

        [Tooltip("Relative selection weight against other eligible patterns.")]
        [Min(0f)]
        public float Weight;

        [Tooltip("Metres of run distance before this pattern may appear.")]
        [Min(0f)]
        public float MinDistance;

        public bool OccupiesLane(int lane) => (LaneMask & (1 << lane)) != 0;

        /// <summary>A row the player cannot pass by any means — never allowed to spawn.</summary>
        public bool IsImpassable => Kind == ObstacleKind.Full && LaneMask == AllLanesMask;
    }
}
