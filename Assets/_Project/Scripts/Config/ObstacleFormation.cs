using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// A multi-row set-piece: ramp into truck, a convoy to hop along, a truck that gets you
    /// high enough for a train.
    /// </summary>
    /// <remarks>
    /// Single-row picking cannot express any of this. A ramp on its own launches you at nothing,
    /// and a train on its own is unreachable from the street — the meaning is in the ordering.
    /// A class rather than a struct because Unity serializes nested arrays far more reliably
    /// inside a class.
    /// </remarks>
    [System.Serializable]
    public class ObstacleFormation
    {
        [Tooltip("Editor-facing label only. Nothing reads this at runtime.")]
        public string Name;

        [Tooltip("Played in order, one per row. An empty lane mask leaves that row clear.")]
        public IntroRow[] Rows;

        [Min(0f)]
        public float Weight = 1f;

        [Tooltip("Metres of run distance before this formation may appear.")]
        [Min(0f)]
        public float MinDistance;

        public int RowCount => Rows == null ? 0 : Rows.Length;
    }
}
