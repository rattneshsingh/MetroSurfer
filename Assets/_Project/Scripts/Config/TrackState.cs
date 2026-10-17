namespace Runner.Config
{
    /// <summary>
    /// Where the player is expected to be when a chunk begins or ends.
    /// </summary>
    /// <remarks>
    /// This is what stops the generator stitching nonsense together — a chunk that leaves you on
    /// a train roof cannot be followed by one that assumes you are on the road, and a roof run
    /// cannot begin unless something got you up there first.
    /// </remarks>
    public enum TrackState
    {
        /// <summary>On the road.</summary>
        Street = 0,

        /// <summary>Up on the traffic stack.</summary>
        Elevated = 1,

        /// <summary>Works either way — breathers and single obstacles mostly.</summary>
        Either = 2
    }
}
