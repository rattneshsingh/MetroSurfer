namespace Runner.Config
{
    /// <summary>
    /// What the generator is looking for right now: how hard, from where, and what it must not
    /// hand out again.
    /// </summary>
    /// <remarks>
    /// A struct rather than parameters so the selection call stays readable and allocation-free
    /// as the rules grow.
    /// </remarks>
    public readonly struct FormationQuery
    {
        public readonly float Distance;
        public readonly TrackState State;

        /// <summary>Hardest chunk allowed. Rises with distance.</summary>
        public readonly float MaxDifficulty;

        /// <summary>
        /// Easiest chunk allowed. Rises too, so trivial chunks stop appearing once the player
        /// has clearly outgrown them.
        /// </summary>
        public readonly float MinDifficulty;

        /// <summary>Recently used chunks, excluded so the same piece never repeats back to back.</summary>
        public readonly ObstacleFormation[] Recent;
        public readonly int RecentCount;

        public FormationQuery(
            float distance,
            TrackState state,
            float minDifficulty,
            float maxDifficulty,
            ObstacleFormation[] recent,
            int recentCount)
        {
            Distance = distance;
            State = state;
            MinDifficulty = minDifficulty;
            MaxDifficulty = maxDifficulty;
            Recent = recent;
            RecentCount = recentCount;
        }

        public bool WasRecentlyUsed(ObstacleFormation formation)
        {
            for (int i = 0; i < RecentCount; i++)
            {
                if (ReferenceEquals(Recent[i], formation))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A chunk can follow if it begins where the player actually is.</summary>
        public bool AcceptsEntry(TrackState entry) =>
            entry == TrackState.Either || State == TrackState.Either || entry == State;
    }
}
