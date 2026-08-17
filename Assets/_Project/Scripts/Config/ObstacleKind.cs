namespace Runner.Config
{
    /// <summary>How the player is expected to beat an obstacle. Drives its size and height.</summary>
    public enum ObstacleKind
    {
        /// <summary>Low barrier — jump it.</summary>
        Low = 0,

        /// <summary>Overhead barrier — roll under it.</summary>
        High = 1,

        /// <summary>Full-height block — the lane must be abandoned.</summary>
        Full = 2,

        /// <summary>Sloped surface that carries the player up to the next tier. Never lethal.</summary>
        Ramp = 3,

        /// <summary>Tier one of the stack. Landable roof, lethal sides.</summary>
        Truck = 4,

        /// <summary>Tier two. Too tall to reach from the street — you get here off a truck.</summary>
        Train = 5,

        /// <summary>A truck out of step with traffic. Same roof, but the gap to it keeps moving.</summary>
        MovingTruck = 6,

        /// <summary>Traffic coming the other way. Closes fast — get out of its lane.</summary>
        OncomingTruck = 7,

        /// <summary>A hole in the road. Jump it or be in another lane; rolling will not save you.</summary>
        Pit = 8,

        /// <summary>
        /// A low bridge across the whole road. Lethal from its underside upward, which is the
        /// inverse of everything else here — it filters by altitude rather than by lane, and it
        /// is the only thing that can punish you for being high.
        /// </summary>
        Underpass = 9,

        /// <summary>
        /// Traffic that brakes to a halt as you close on it. The only obstacle here that
        /// punishes committing early rather than reacting late.
        /// </summary>
        StoppingBus = 10
    }
}
