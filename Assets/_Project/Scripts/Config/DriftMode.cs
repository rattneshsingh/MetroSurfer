namespace Runner.Config
{
    /// <summary>How a vehicle fails to keep pace with the world.</summary>
    public enum DriftMode
    {
        /// <summary>Keeps pace with the world. Most obstacles.</summary>
        None = -1,

        /// <summary>Surges ahead and drops back around its spawn point. Stays in bounds forever.</summary>
        Oscillate = 0,

        /// <summary>Bears down on the player, then holds once it is well past. Oncoming traffic.</summary>
        Approach = 1,

        /// <summary>
        /// Rolls along with traffic until the player is close, then brakes to a stop. The lane
        /// you judged safe stops being safe.
        /// </summary>
        Decelerate = 2
    }
}
