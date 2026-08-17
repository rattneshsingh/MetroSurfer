using NUnit.Framework;
using Runner.Core;

namespace Runner.Tests
{
    public sealed class PursuitStateTests
    {
        private const float StumbleCost = 0.34f;
        private const float StreetRecovery = 0.12f;
        private const float ClimbRecovery = 0.6f;

        private const float TruckRoof = 1.2f;
        private const float Street = 0f;

        private static PursuitState NewPursuit() =>
            new PursuitState(StumbleCost, StreetRecovery, ClimbRecovery);

        private static void Run(PursuitState pursuit, float seconds, float height)
        {
            const float Step = 1f / 60f;
            for (float t = 0f; t < seconds; t += Step)
            {
                pursuit.Tick(Step, height);
            }
        }

        [Test]
        public void StartsClearAndUnpursued()
        {
            PursuitState pursuit = NewPursuit();

            Assert.AreEqual(1f, pursuit.Distance, 0.001f);
            Assert.IsFalse(pursuit.IsPursuing);
            Assert.IsFalse(pursuit.IsCaught);
        }

        [Test]
        public void AStumbleBringsHimIntoView()
        {
            PursuitState pursuit = NewPursuit();

            Assert.IsFalse(pursuit.Stumble(), "One stumble should not be fatal.");
            Assert.IsTrue(pursuit.IsPursuing, "He should be visible after a stumble.");
            Assert.Less(pursuit.Distance, 1f);
        }

        /// <summary>
        /// Three strikes. Two is unforgiving, four makes the chase meaningless.
        /// </summary>
        [Test]
        public void ThreeStumblesGetsYouCaught()
        {
            PursuitState pursuit = NewPursuit();

            Assert.IsFalse(pursuit.Stumble());
            Assert.IsFalse(pursuit.Stumble());
            Assert.IsTrue(pursuit.Stumble(), "The third stumble should end the run.");
            Assert.IsTrue(pursuit.IsCaught);
        }

        /// <summary>
        /// The whole mechanic. He cannot follow you onto the stack, so height is the answer to
        /// being chased — which is what makes the traffic stack more than a scoring detour.
        /// </summary>
        [Test]
        public void ClimbingShakesHimOffFarFasterThanRunning()
        {
            PursuitState onFoot = NewPursuit();
            PursuitState onRoof = NewPursuit();

            onFoot.Stumble();
            onRoof.Stumble();

            Run(onFoot, 1f, Street);
            Run(onRoof, 1f, TruckRoof);

            Assert.Greater(onRoof.Distance, onFoot.Distance,
                "Climbing must shake him off faster than staying on the street.");
        }

        [Test]
        public void StayingOnTheStreetLetsHimLinger()
        {
            PursuitState pursuit = NewPursuit();
            pursuit.Stumble();

            Run(pursuit, 1.5f, Street);

            Assert.IsTrue(pursuit.IsPursuing,
                "He should still be there after a second and a half on the street — otherwise " +
                "a stumble costs nothing and there is no chase.");
        }

        [Test]
        public void ARoofClearsHimEventually()
        {
            PursuitState pursuit = NewPursuit();
            pursuit.Stumble();

            Run(pursuit, 3f, TruckRoof);

            Assert.IsFalse(pursuit.IsPursuing, "Three seconds on a roof should lose him entirely.");
            Assert.AreEqual(1f, pursuit.Distance, 0.001f);
        }

        [Test]
        public void RecoveryNeverOvershoots()
        {
            PursuitState pursuit = NewPursuit();
            pursuit.Stumble();

            Run(pursuit, 30f, TruckRoof);

            Assert.AreEqual(1f, pursuit.Distance, 0.001f, "Distance must be clamped at 1.");
        }

        /// <summary>Once he has you, running does not undo it.</summary>
        [Test]
        public void BeingCaughtIsFinal()
        {
            PursuitState pursuit = NewPursuit();

            pursuit.Stumble();
            pursuit.Stumble();
            pursuit.Stumble();

            Run(pursuit, 5f, TruckRoof);

            Assert.IsTrue(pursuit.IsCaught, "A caught player must not recover by climbing.");
            Assert.AreEqual(0f, pursuit.Distance, 0.001f);
        }

        /// <summary>
        /// Recovering between stumbles has to buy you something, or the chase is just a
        /// three-hit counter and position never matters.
        /// </summary>
        [Test]
        public void RecoveringBetweenStumblesSurvivesMoreOfThem()
        {
            PursuitState reckless = NewPursuit();
            reckless.Stumble();
            reckless.Stumble();

            PursuitState careful = NewPursuit();
            careful.Stumble();
            Run(careful, 2f, TruckRoof);
            careful.Stumble();

            Assert.Greater(careful.Distance, reckless.Distance,
                "Climbing between stumbles should leave you in better shape than taking both cold.");
        }

        [Test]
        public void ResetClearsHim()
        {
            PursuitState pursuit = NewPursuit();

            pursuit.Stumble();
            pursuit.Stumble();
            pursuit.Reset();

            Assert.AreEqual(1f, pursuit.Distance, 0.001f);
            Assert.IsFalse(pursuit.IsPursuing);
            Assert.IsFalse(pursuit.IsCaught);
        }
    }
}
