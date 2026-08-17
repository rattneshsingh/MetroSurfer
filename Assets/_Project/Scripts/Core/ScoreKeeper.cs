using UnityEngine;

namespace Runner.Core
{
    /// <summary>
    /// Scoring rules: coins only, multiplied by how high you were standing when you took them
    /// and by how well you have kept the chain going.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Surviving earns nothing on its own — distance drives difficulty, not the scoreboard. That
    /// is deliberate: the coins live on the roofs, so tying score to coins is what makes the
    /// risky route the scoring route. Distance-based scoring would make the whole traffic stack
    /// optional.
    /// </para>
    /// <para>
    /// The streak is what stops that collapsing into "grab a few coins and die". A long clean
    /// run compounds, so endurance still matters — it just has to be endurance spent collecting
    /// rather than endurance spent avoiding.
    /// </para>
    /// <para>
    /// Plain C#, no MonoBehaviour, so the rules can be tested directly.
    /// </para>
    /// </remarks>
    public sealed class ScoreKeeper
    {
        private readonly int _pointsPerCoin;
        private readonly int _coinsPerTier;
        private readonly int _maxMultiplier;
        private readonly float _breakDistance;
        private readonly float _heightBonusPerUnit;

        private int _score;
        private int _streakCoins;
        private int _multiplier;
        private float _distanceAtLastCoin;

        public int Score => _score;
        public int Multiplier => _multiplier;
        public int StreakCoins => _streakCoins;

        /// <summary>Coins still needed to reach the next tier, or zero once capped.</summary>
        public int CoinsToNextTier =>
            _multiplier >= _maxMultiplier ? 0 : _coinsPerTier - _streakCoins % _coinsPerTier;

        public ScoreKeeper(
            int pointsPerCoin,
            int coinsPerTier,
            int maxMultiplier,
            float breakDistance,
            float heightBonusPerUnit)
        {
            _pointsPerCoin = Mathf.Max(1, pointsPerCoin);
            _coinsPerTier = Mathf.Max(1, coinsPerTier);
            _maxMultiplier = Mathf.Max(1, maxMultiplier);
            _breakDistance = Mathf.Max(0.01f, breakDistance);
            _heightBonusPerUnit = Mathf.Max(0f, heightBonusPerUnit);

            Reset();
        }

        public void Reset()
        {
            _score = 0;
            _streakCoins = 0;
            _multiplier = 1;
            _distanceAtLastCoin = 0f;
        }

        /// <summary>
        /// Breaks the streak once the player has gone too far without a coin. Measured in metres
        /// rather than seconds on purpose — a time limit would be far more forgiving early in a
        /// run than late, when the same seconds cover three times the ground.
        /// </summary>
        /// <returns>True if the streak broke on this call.</returns>
        public bool Tick(float distance)
        {
            if (_streakCoins == 0 || distance - _distanceAtLastCoin <= _breakDistance)
            {
                return false;
            }

            _streakCoins = 0;
            _multiplier = 1;
            return true;
        }

        /// <summary>
        /// Drops the chain without touching banked points. Called when the player stumbles: a
        /// scrappy run should be worth less than a clean one, which is how failure stays felt
        /// even when it does not end the run.
        /// </summary>
        public void BreakStreak()
        {
            _streakCoins = 0;
            _multiplier = 1;
        }

        /// <summary>Awards a coin and returns the points it was worth.</summary>
        public int Collect(int coinValue, float supportHeight, float distance)
        {
            _streakCoins++;
            _distanceAtLastCoin = distance;
            _multiplier = Mathf.Clamp(1 + _streakCoins / _coinsPerTier, 1, _maxMultiplier);

            float heightBonus = 1f + Mathf.Max(0f, supportHeight) * _heightBonusPerUnit;
            int awarded = Mathf.RoundToInt(coinValue * _pointsPerCoin * heightBonus * _multiplier);

            _score += awarded;
            return awarded;
        }
    }
}
