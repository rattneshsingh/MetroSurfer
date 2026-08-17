namespace Runner.Config
{
    /// <summary>
    /// What each kind means for the player, independent of how it is drawn or sized.
    /// Generation and testing both need these answers without touching a GameObject.
    /// </summary>
    public static class ObstacleKindRules
    {
        /// <summary>True if the player can stand on its roof.</summary>
        public static bool IsLandable(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.Truck:
                case ObstacleKind.MovingTruck:
                case ObstacleKind.OncomingTruck:
                case ObstacleKind.StoppingBus:
                case ObstacleKind.Train:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True if the obstacle is lethal above its clearance rather than below it. Such an
        /// obstacle occupies every lane by design and is still passable — on foot, at street
        /// level — so it must never be treated as closing the road.
        /// </summary>
        public static bool IsOverhead(ObstacleKind kind) => kind == ObstacleKind.Underpass;

        /// <summary>
        /// True if occupying that lane cannot be survived in place — no jump and no roll gets
        /// you through, so the lane has to be abandoned. A row of these across every lane is
        /// unbeatable and must never be generated.
        /// </summary>
        public static bool ClosesLane(ObstacleKind kind)
        {
            switch (kind)
            {
                // Too tall to clear from the street.
                case ObstacleKind.Full:
                case ObstacleKind.Train:

                // A single pit is jumpable, but pits come in runs and you cannot stay airborne
                // across several. Treated as closing so a full-width run can never be authored.
                case ObstacleKind.Pit:
                    return true;

                default:
                    return false;
            }
        }
    }
}
