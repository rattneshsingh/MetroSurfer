using UnityEngine;

namespace Runner.Config
{
    /// <summary>One authored row of the opening sequence.</summary>
    [System.Serializable]
    public struct IntroRow
    {
        [Tooltip("Bit per lane: 1 = left, 2 = middle, 4 = right. 0 leaves the row empty.")]
        [Range(0, ObstaclePattern.AllLanesMask)]
        public int LaneMask;

        public ObstacleKind Kind;

        [Tooltip("Lay a coin trail on this row, in whichever lane stays open.")]
        public bool Coins;

        [Tooltip("Arc the coins across this row along the player's jump trajectory, instead of " +
                 "laying them flat. Use on an empty row between two landable rows: Kind then " +
                 "names the tier the jump launches from rather than anything to spawn.")]
        public bool CoinArc;
    }
}
