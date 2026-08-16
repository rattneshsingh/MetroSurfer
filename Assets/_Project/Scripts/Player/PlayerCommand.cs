namespace Runner.Player
{
    /// <summary>
    /// Intent produced by <see cref="PlayerInputReader"/> for a single frame. Flags because a
    /// frame can legitimately carry more than one (swipe left while a buffered jump lands).
    /// </summary>
    /// <remarks>
    /// Test with bitwise <c>&amp;</c>, never <c>Enum.HasFlag</c> — that boxes under IL2CPP.
    /// </remarks>
    [System.Flags]
    public enum PlayerCommand
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
        Jump = 1 << 2,
        Slide = 1 << 3
    }
}
