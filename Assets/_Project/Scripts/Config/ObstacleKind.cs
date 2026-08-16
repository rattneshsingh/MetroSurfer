namespace Runner.Config
{
    /// <summary>How the player is expected to beat an obstacle. Drives its size and height.</summary>
    public enum ObstacleKind
    {
        /// <summary>Low barrier — jump it.</summary>
        Low = 0,

        /// <summary>Overhead barrier — slide under it.</summary>
        High = 1,

        /// <summary>Full-height block — the lane must be abandoned.</summary>
        Full = 2
    }
}
