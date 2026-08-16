namespace Runner.Core
{
    public enum GameState
    {
        /// <summary>Track is built and idling; waiting for the first input.</summary>
        Ready = 0,

        Playing = 1,

        /// <summary>Hit something. Frozen for the death beat, then restartable.</summary>
        Dead = 2
    }
}
