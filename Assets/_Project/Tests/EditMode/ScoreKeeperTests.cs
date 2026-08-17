using NUnit.Framework;
using Runner.Core;

namespace Runner.Tests
{
    public sealed class ScoreKeeperTests
    {
        private const int PointsPerCoin = 5;
        private const int CoinsPerTier = 8;
        private const int MaxMultiplier = 5;
        private const float BreakDistance = 60f;
        private const float HeightBonus = 0.6f;

        private static ScoreKeeper NewKeeper() =>
            new ScoreKeeper(PointsPerCoin, CoinsPerTier, MaxMultiplier, BreakDistance, HeightBonus);

        [Test]
        public void StartsEmptyAtSingleMultiplier()
        {
            ScoreKeeper scoring = NewKeeper();

            Assert.AreEqual(0, scoring.Score);
            Assert.AreEqual(1, scoring.Multiplier);
        }

        [Test]
        public void SurvivingWithoutCoinsScoresNothing()
        {
            ScoreKeeper scoring = NewKeeper();

            for (float distance = 0f; distance < 5000f; distance += 10f)
            {
                scoring.Tick(distance);
            }

            Assert.AreEqual(0, scoring.Score, "Distance must not score on its own.");
        }

        [Test]
        public void AStreetCoinIsWorthItsFaceValue()
        {
            ScoreKeeper scoring = NewKeeper();

            int awarded = scoring.Collect(1, supportHeight: 0f, distance: 0f);

            Assert.AreEqual(PointsPerCoin, awarded);
            Assert.AreEqual(PointsPerCoin, scoring.Score);
        }

        /// <summary>The climb has to pay, or the roofs are just a longer route to the same points.</summary>
        [Test]
        public void ARoofCoinIsWorthMoreThanTheSameCoinOnTheStreet()
        {
            int street = NewKeeper().Collect(1, supportHeight: 0f, distance: 0f);
            int roof = NewKeeper().Collect(1, supportHeight: 1.2f, distance: 0f);
            int train = NewKeeper().Collect(1, supportHeight: 2.4f, distance: 0f);

            Assert.Greater(roof, street, "A truck roof must beat the street.");
            Assert.Greater(train, roof, "A train roof must beat a truck roof.");
        }

        [Test]
        public void MultiplierClimbsATierEveryFewCoins()
        {
            ScoreKeeper scoring = NewKeeper();

            for (int i = 0; i < CoinsPerTier - 1; i++)
            {
                scoring.Collect(1, 0f, i);
            }

            Assert.AreEqual(1, scoring.Multiplier, "Tier came too early.");

            scoring.Collect(1, 0f, CoinsPerTier);
            Assert.AreEqual(2, scoring.Multiplier, "Tier did not arrive on the expected coin.");
        }

        [Test]
        public void MultiplierIsCapped()
        {
            ScoreKeeper scoring = NewKeeper();

            for (int i = 0; i < CoinsPerTier * (MaxMultiplier + 10); i++)
            {
                scoring.Collect(1, 0f, i);
            }

            Assert.AreEqual(MaxMultiplier, scoring.Multiplier, "Multiplier ran away past its cap.");
        }

        [Test]
        public void GoingTooFarWithoutACoinBreaksTheStreak()
        {
            ScoreKeeper scoring = NewKeeper();

            for (int i = 0; i < CoinsPerTier * 2; i++)
            {
                scoring.Collect(1, 0f, distance: 10f);
            }

            Assert.AreEqual(3, scoring.Multiplier);

            Assert.IsFalse(scoring.Tick(10f + BreakDistance), "Broke exactly at the limit.");
            Assert.IsTrue(scoring.Tick(10f + BreakDistance + 1f), "Streak should have broken.");
            Assert.AreEqual(1, scoring.Multiplier);
        }

        [Test]
        public void BreakingTheStreakKeepsThePointsAlreadyBanked()
        {
            ScoreKeeper scoring = NewKeeper();

            for (int i = 0; i < CoinsPerTier * 2; i++)
            {
                scoring.Collect(1, 0f, distance: 0f);
            }

            int banked = scoring.Score;
            scoring.Tick(BreakDistance * 4f);

            Assert.AreEqual(banked, scoring.Score, "A broken streak must not take points back.");
        }

        [Test]
        public void CollectingKeepsTheStreakAlive()
        {
            ScoreKeeper scoring = NewKeeper();
            float distance = 0f;

            for (int i = 0; i < CoinsPerTier * 3; i++)
            {
                distance += BreakDistance * 0.9f;
                scoring.Collect(1, 0f, distance);
                Assert.IsFalse(scoring.Tick(distance), $"Streak broke at coin {i} despite staying inside the window.");
            }

            Assert.AreEqual(4, scoring.Multiplier);
        }

        /// <summary>
        /// The whole point of the streak: a long clean run must beat a short greedy one, even
        /// when the greedy run grabs the same coins.
        /// </summary>
        [Test]
        public void ASustainedRunOutscoresTheSameCoinsTakenInBursts()
        {
            ScoreKeeper sustained = NewKeeper();
            ScoreKeeper broken = NewKeeper();

            const int coins = 40;

            for (int i = 0; i < coins; i++)
            {
                sustained.Collect(1, 0f, i * 10f);
                sustained.Tick(i * 10f);
            }

            for (int i = 0; i < coins; i++)
            {
                broken.Collect(1, 0f, i * 10f);
                broken.Tick(i * 10f + BreakDistance * 2f);
            }

            Assert.Greater(sustained.Score, broken.Score,
                "Keeping the chain alive must be worth more than the same coins collected in fragments.");
        }

        [Test]
        public void ResetClearsEverything()
        {
            ScoreKeeper scoring = NewKeeper();

            for (int i = 0; i < CoinsPerTier * 3; i++)
            {
                scoring.Collect(1, 1.2f, i);
            }

            scoring.Reset();

            Assert.AreEqual(0, scoring.Score);
            Assert.AreEqual(1, scoring.Multiplier);
            Assert.AreEqual(0, scoring.StreakCoins);
        }
    }
}
