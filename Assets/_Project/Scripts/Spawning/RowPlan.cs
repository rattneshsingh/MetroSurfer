using Runner.Config;

namespace Runner.Spawning
{
    /// <summary>
    /// What one row should contain. A decision, with nothing spawned yet — which is what makes
    /// generation testable without a scene.
    /// </summary>
    public readonly struct RowPlan
    {
        /// <summary>Lane index carrying no coins.</summary>
        public const int NoCoinLane = -1;

        public readonly int LaneMask;
        public readonly ObstacleKind Kind;

        /// <summary>Lane for the coin trail, or <see cref="NoCoinLane"/>.</summary>
        public readonly int CoinLane;

        /// <summary>Coins sit on the roof of this row's obstacle rather than on the street.</summary>
        public readonly bool CoinsOnRoof;

        /// <summary>Coins follow the jump arc across a gap rather than lying flat.</summary>
        public readonly bool CoinArc;

        public RowPlan(int laneMask, ObstacleKind kind, int coinLane, bool coinsOnRoof, bool coinArc = false)
        {
            LaneMask = laneMask;
            Kind = kind;
            CoinLane = coinLane;
            CoinsOnRoof = coinsOnRoof;
            CoinArc = coinArc;
        }

        public bool HasObstacles => LaneMask != 0;
        public bool HasCoins => CoinLane != NoCoinLane;
    }
}
