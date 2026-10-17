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

        [Tooltip("Metres of run distance before this may appear at all. Most chunks should leave " +
                 "this at zero and let Difficulty do the gating instead.")]
        [Min(0f)]
        public float MinDistance;

        [Tooltip("How hard this is, 0 to 1. The generator picks from a band that rises with " +
                 "distance, so this is what keeps early running fair and late running brutal.")]
        [Range(0f, 1f)]
        public float Difficulty = 0.3f;

        [Tooltip("Where the player must be for this to make sense as an opening.")]
        public TrackState Entry = TrackState.Street;

        [Tooltip("Where the player will be when it ends. The next chunk has to accept this.")]
        public TrackState Exit = TrackState.Street;

        public int RowCount => Rows == null ? 0 : Rows.Length;
    }
}
