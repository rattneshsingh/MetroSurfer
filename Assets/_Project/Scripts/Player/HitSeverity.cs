namespace Runner.Player
{
    /// <summary>How badly the player hit something.</summary>
    public enum HitSeverity
    {
        /// <summary>
        /// Clipped it while already doing something — mid lane-change, mid-air or mid-roll.
        /// You were trying. Stumble.
        /// </summary>
        Glancing = 0,

        /// <summary>
        /// Ran into it flat-footed and square. You were not reacting at all, so there is
        /// nothing to recover from.
        /// </summary>
        HeadOn = 1
    }
}
