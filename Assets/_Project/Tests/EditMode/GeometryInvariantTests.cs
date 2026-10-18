using NUnit.Framework;
using Runner.Config;
using Runner.Player;
using UnityEngine;

namespace Runner.Tests
{
    /// <summary>
    /// Asserts that the player's body and the world's obstacles still agree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every obstacle height in this game is chosen relative to two numbers: how tall the
    /// player's hitbox is, and how high their jump takes it. Change either and several obstacles
    /// silently stop meaning what they were designed to mean — a "roll under this" bar becomes
    /// jumpable, a "change lane" block becomes clearable, and nothing anywhere reports a
    /// problem. It simply stops being the game it was.
    /// </para>
    /// <para>
    /// That relationship was worked out by hand three times during development and got it wrong
    /// once, producing obstacles three and a half times the player's height. It lived only in
    /// conversation. Now it lives here.
    /// </para>
    /// </remarks>
    public sealed class GeometryInvariantTests
    {
        private GameObject _host;
        private LaneController _player;
        private ObstacleCatalog _catalog;

        /// <summary>Bottom of the standing hitbox, relative to the feet.</summary>
        private const float StandingBottom = 0.075f;
        private const float StandingTop = 0.925f;
        private const float RollingBottom = 0.045f;
        private const float RollingTop = 0.395f;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("GeometryPlayer");
            _host.AddComponent<BoxCollider>();
            _player = _host.AddComponent<LaneController>();
            _catalog = ObstacleCatalog.CreateDefault();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_catalog);
        }

        private float JumpApex => _player.Jump.ApexHeight;

        private static bool Overlaps(float aBottom, float aTop, float bBottom, float bTop) =>
            aBottom < bTop && bBottom < aTop;

        private bool HitsWhileStanding(ObstacleKind kind, float feetHeight = 0f)
        {
            ObstacleDefinition d = _catalog.Get(kind);
            return Overlaps(feetHeight + StandingBottom, feetHeight + StandingTop, d.LethalBottom, d.LethalTop);
        }

        private bool HitsWhileRolling(ObstacleKind kind)
        {
            ObstacleDefinition d = _catalog.Get(kind);
            return Overlaps(RollingBottom, RollingTop, d.LethalBottom, d.LethalTop);
        }

        private bool HitsAtJumpApex(ObstacleKind kind, float feetHeight = 0f)
        {
            ObstacleDefinition d = _catalog.Get(kind);
            float apex = feetHeight + JumpApex;
            return Overlaps(apex + StandingBottom, apex + StandingTop, d.LethalBottom, d.LethalTop);
        }

        [Test]
        public void EveryObstacleKindHasADefinition()
        {
            foreach (ObstacleKind kind in System.Enum.GetValues(typeof(ObstacleKind)))
            {
                Assert.IsNotNull(_catalog.Get(kind), $"{kind} has no entry in the catalogue.");
            }
        }

        /// <summary>A low barrier is the jump lesson. If it can be rolled it teaches nothing.</summary>
        [Test]
        public void LowBarrierMustBeJumpedAndCannotBeRolled()
        {
            Assert.IsTrue(HitsWhileStanding(ObstacleKind.Low), "A low barrier should hit a runner.");
            Assert.IsTrue(HitsWhileRolling(ObstacleKind.Low), "A low barrier should not be beatable by rolling.");
            Assert.IsFalse(HitsAtJumpApex(ObstacleKind.Low), "A low barrier must be clearable by jumping.");
        }

        /// <summary>
        /// The overhead bar is the roll lesson, and the invariant most easily broken by tuning:
        /// raise the jump far enough and you can simply hop over it, which quietly deletes the
        /// roll from the game.
        /// </summary>
        [Test]
        public void OverheadBarMustBeRolledAndCannotBeJumped()
        {
            Assert.IsTrue(HitsWhileStanding(ObstacleKind.High), "An overhead bar should hit a runner.");
            Assert.IsFalse(HitsWhileRolling(ObstacleKind.High), "An overhead bar must be clearable by rolling.");
            Assert.IsTrue(HitsAtJumpApex(ObstacleKind.High),
                $"Jump apex {JumpApex:0.00} clears the overhead bar, so rolling is now optional.");
        }

        /// <summary>A full block means "leave this lane". It must not be survivable in place.</summary>
        [Test]
        public void FullBlockCannotBeBeatenInPlace()
        {
            Assert.IsTrue(HitsWhileStanding(ObstacleKind.Full));
            Assert.IsTrue(HitsWhileRolling(ObstacleKind.Full));
            Assert.IsTrue(HitsAtJumpApex(ObstacleKind.Full),
                $"Jump apex {JumpApex:0.00} clears a full block, so it no longer forces a lane change.");
        }

        [Test]
        public void PitMustBeJumpedAndCannotBeRolled()
        {
            Assert.IsTrue(HitsWhileStanding(ObstacleKind.Pit));
            Assert.IsTrue(HitsWhileRolling(ObstacleKind.Pit), "Rolling over a hole should not work.");
            Assert.IsFalse(HitsAtJumpApex(ObstacleKind.Pit), "A pit must be clearable by jumping.");
        }

        /// <summary>
        /// The underpass exists to punish height. It has to be harmless at street level and
        /// lethal on a roof, or it is either pointless or an ambush.
        /// </summary>
        [Test]
        public void UnderpassIsSafeOnTheStreetAndLethalOnARoof()
        {
            float truckRoof = _catalog.RoofHeight(ObstacleKind.Truck);
            float trainRoof = _catalog.RoofHeight(ObstacleKind.Train);

            Assert.IsFalse(HitsWhileStanding(ObstacleKind.Underpass),
                "An underpass must be passable on foot at street level.");
            Assert.IsTrue(HitsWhileStanding(ObstacleKind.Underpass, truckRoof),
                "An underpass must catch a player standing on a truck.");
            Assert.IsTrue(HitsWhileStanding(ObstacleKind.Underpass, trainRoof),
                "An underpass must catch a player standing on a train.");
            Assert.IsTrue(HitsAtJumpApex(ObstacleKind.Underpass, trainRoof),
                "An underpass must not be jumpable from the highest tier.");
        }

        /// <summary>
        /// Vehicles are climbed, not hopped onto. If a jump from the street can land on a roof,
        /// trucks stop being obstacles and ramps stop having a purpose — which is exactly how the
        /// game played until this was turned around. The ramp is the entrance to the stack.
        /// </summary>
        /// <remarks>
        /// A falling runner lands on any surface the ground probe finds, and the probe starts
        /// above the feet, so the highest reachable roof is the jump apex plus that head start.
        /// </remarks>
        [Test]
        public void VehicleRoofsAreOutOfJumpingReachFromTheStreet()
        {
            PlayerTuning tuning = PlayerTuning.CreateDefault();
            try
            {
                float highestLanding = JumpApex + tuning.ProbeStartHeight;

                foreach (ObstacleKind kind in new[]
                         {
                             ObstacleKind.Truck, ObstacleKind.MovingTruck,
                             ObstacleKind.OncomingTruck, ObstacleKind.StoppingBus, ObstacleKind.Train
                         })
                {
                    float roof = _catalog.RoofHeight(kind);

                    Assert.Greater(roof, highestLanding,
                        $"A jump from the street lands anything up to {highestLanding:0.00}, so the " +
                        $"{roof:0.00} {kind} roof can be hopped onto and is no longer an obstacle.");
                }
            }
            finally
            {
                Object.DestroyImmediate(tuning);
            }
        }

        /// <summary>
        /// The ramp fills its row cell and ends exactly where the truck in the next row begins,
        /// so riding it up arrives on the roof with no step, gap or wall at the top.
        /// </summary>
        [Test]
        public void RampFillsItsRowAndMeetsTheNextTruck()
        {
            const float RowSpacing = 5f;

            ObstacleDefinition ramp = _catalog.Get(ObstacleKind.Ramp);
            ObstacleDefinition truck = _catalog.Get(ObstacleKind.Truck);

            float rampEnd = ramp.RampStart + ramp.RampRun;
            float truckFront = RowSpacing - truck.Size.z * 0.5f;

            Assert.AreEqual(-RowSpacing * 0.5f, ramp.RampStart, 0.01f, "The ramp should start at its row's near edge.");
            Assert.AreEqual(truckFront, rampEnd, 0.01f,
                $"The ramp ends at {rampEnd:0.00} but the next truck starts at {truckFront:0.00}.");
        }

        /// <summary>
        /// Tier two must not be reachable from the road, or the ramp-and-truck route it is
        /// supposed to reward becomes pointless.
        /// </summary>
        [Test]
        public void TrainRoofIsOnlyReachableFromATruck()
        {
            float truckRoof = _catalog.RoofHeight(ObstacleKind.Truck);
            float trainRoof = _catalog.RoofHeight(ObstacleKind.Train);

            Assert.Greater(trainRoof, truckRoof, "A train should stand above a truck.");
            Assert.Less(StandingBottom + JumpApex, trainRoof,
                "A train roof is reachable straight from the street, so trucks are redundant.");
            Assert.Greater(truckRoof + StandingBottom + JumpApex, trainRoof,
                "A train roof is unreachable even from a truck, so the stack has no second tier.");
        }

        /// <summary>The ramp is the on-ramp to tier one. It has to actually arrive there.</summary>
        [Test]
        public void RampRisesToTruckRoofHeight()
        {
            ObstacleDefinition ramp = _catalog.Get(ObstacleKind.Ramp);
            float truckRoof = _catalog.RoofHeight(ObstacleKind.Truck);

            Assert.AreEqual(truckRoof, ramp.RampRise, 0.05f,
                "A ramp that does not end at roof height leaves a step at the top.");
        }

        /// <summary>
        /// Ramps are walked up, not jumped onto. If the climb per frame ever exceeds the
        /// controller's step tolerance the ramp becomes a wall, and at speed that is a death.
        /// </summary>
        [Test]
        public void RampIsGentleEnoughToWalkUpAtFullSpeed()
        {
            ObstacleDefinition ramp = _catalog.Get(ObstacleKind.Ramp);
            DifficultyCurve curve = DifficultyCurve.CreateDefault();

            try
            {
                float topSpeed = curve.EvaluateSpeed(1000000f);
                float slope = ramp.RampRise / ramp.RampRun;
                float risePerFrame = topSpeed * (1f / 60f) * slope;

                // Matches LaneController's serialized default.
                const float StepUpTolerance = 0.35f;

                Assert.Less(risePerFrame, StepUpTolerance,
                    $"At {topSpeed:0} m/s the ramp climbs {risePerFrame:0.000} per frame, over the " +
                    $"{StepUpTolerance} step tolerance — it stops being a ramp and becomes a wall.");
            }
            finally
            {
                Object.DestroyImmediate(curve);
            }
        }

        /// <summary>Nothing should tower over the player. This is the skyscraper guard.</summary>
        [Test]
        public void NoObstacleIsAbsurdlyTallRelativeToThePlayer()
        {
            const float PlayerHeight = 1f;
            const float MaxReasonableMultiple = 2.5f;

            for (int i = 0; i < _catalog.Count; i++)
            {
                ObstacleDefinition definition = _catalog.At(i);

                // The underpass is a piece of architecture spanning the road, not a vehicle.
                if (definition.Kind == ObstacleKind.Underpass)
                {
                    continue;
                }

                float top = definition.BaseHeight + definition.Size.y;

                Assert.LessOrEqual(top, PlayerHeight * MaxReasonableMultiple,
                    $"{definition.Kind} reaches {top:0.00}, over {MaxReasonableMultiple}x the player's " +
                    "height. That reads as a wall rather than an obstacle.");
            }
        }

        /// <summary>
        /// The camera has to clear everything it passes, or obstacles fly through it and blank
        /// the screen — which is exactly what happened once.
        /// </summary>
        [Test]
        public void CameraSitsAboveEveryVehicle()
        {
            // Matches Phase0SceneBuilder's camera height.
            const float CameraHeight = 3.4f;

            for (int i = 0; i < _catalog.Count; i++)
            {
                ObstacleDefinition definition = _catalog.At(i);

                if (definition.Kind == ObstacleKind.Underpass)
                {
                    continue;
                }

                float top = definition.BaseHeight + definition.Size.y;

                Assert.Less(top, CameraHeight,
                    $"{definition.Kind} reaches {top:0.00}, at or above the camera at {CameraHeight}. " +
                    "It will pass through the camera and fill the frame.");
            }
        }
    }
}
