using NUnit.Framework;
using Runner.Config;
using Runner.Spawning;
using UnityEngine;

namespace Runner.Tests
{
    /// <summary>
    /// Tests obstacle construction on its own, which only became possible once building things
    /// was separated from scrolling the world.
    /// </summary>
    public sealed class ObstacleFactoryTests
    {
        private ObstacleCatalog _catalog;
        private Transform _poolRoot;
        private ObstacleFactory _factory;

        private static ObstacleFactorySettings Settings() => new ObstacleFactorySettings(
            LayerMask.NameToLayer("Obstacle"),
            LayerMask.NameToLayer("Surface"),
            autoCapacity: 4,
            Color.grey, Color.black, Color.red,
            driftAmplitude: 2f, driftPeriod: 3f,
            oncomingSpeed: 14f, oncomingMaxTravel: 90f,
            busCruiseSpeed: 6f, busBrakeTriggerZ: 45f, busBrakeDuration: 0.9f);

        [SetUp]
        public void SetUp()
        {
            _catalog = ObstacleCatalog.CreateDefault();
            _poolRoot = new GameObject("FactoryRoot").transform;
            _factory = new ObstacleFactory(_catalog, _poolRoot, Settings(), () => 0.5f);
        }

        [TearDown]
        public void TearDown()
        {
            _factory.Dispose();
            Object.DestroyImmediate(_poolRoot.gameObject);
            Object.DestroyImmediate(_catalog);
        }

        [Test]
        public void EveryCatalogueEntryCanBeBuilt()
        {
            foreach (ObstacleKind kind in System.Enum.GetValues(typeof(ObstacleKind)))
            {
                Obstacle obstacle = _factory.Get(kind, out int handle);

                Assert.IsNotNull(obstacle, $"{kind} produced nothing.");
                Assert.AreEqual(kind, obstacle.Kind, $"{kind} was built with the wrong identity.");

                _factory.Release(kind, handle);
            }
        }

        /// <summary>
        /// The gap between the roof and the top of the kill volume is what makes landing on a
        /// truck survivable. Without it the whole stack is unreachable.
        /// </summary>
        [Test]
        public void LandablePiecesKeepTheirRoofClearOfTheLethalVolume()
        {
            foreach (ObstacleKind kind in new[]
                     {
                         ObstacleKind.Truck, ObstacleKind.Train,
                         ObstacleKind.MovingTruck, ObstacleKind.StoppingBus
                     })
            {
                Obstacle obstacle = _factory.Get(kind, out int handle);
                Transform lethal = obstacle.transform.Find("Lethal");

                Assert.IsNotNull(lethal, $"{kind} has no kill volume, so it can never hurt anyone.");

                BoxCollider box = lethal.GetComponent<BoxCollider>();
                float lethalTop = box.center.y + box.size.y * 0.5f;
                float roof = _catalog.RoofHeight(kind);

                Assert.Less(lethalTop, roof,
                    $"{kind}'s kill volume reaches {lethalTop:0.00}, at or above its {roof:0.00} roof — " +
                    "standing on it would be fatal.");

                _factory.Release(kind, handle);
            }
        }

        /// <summary>
        /// A ramp is solid underneath but safe on top. Get this wrong in either direction and it
        /// either kills people for riding it, or lets them run through it at street level.
        /// </summary>
        [Test]
        public void RampIsSolidUnderneathButSafeToRide()
        {
            Obstacle ramp = _factory.Get(ObstacleKind.Ramp, out int handle);
            ObstacleDefinition definition = _catalog.Get(ObstacleKind.Ramp);

            Transform lethal = ramp.transform.Find("Lethal");
            Assert.IsNotNull(lethal, "A ramp with no solid part can be run through from the side.");

            BoxCollider box = lethal.GetComponent<BoxCollider>();
            float solidTop = box.center.y + box.size.y * 0.5f;
            float solidStartZ = box.center.z - box.size.z * 0.5f;

            // Height of the slope where the solid part begins. The slope starts at RampStart.
            float slopeHeightThere = definition.RampRise * ((solidStartZ - definition.RampStart) / definition.RampRun);
            const float HitboxBottom = 0.075f;

            Assert.LessOrEqual(solidTop, slopeHeightThere + HitboxBottom,
                $"The solid part reaches {solidTop:0.00} where the slope is only " +
                $"{slopeHeightThere:0.00} — it would catch someone riding the ramp properly.");

            Assert.Greater(solidTop, 0.4f,
                "The solid part is too shallow to catch a runner arriving at street level.");

            Assert.Greater(solidStartZ, definition.RampStart,
                "The entrance to the ramp must stay open, or you cannot get on it.");

            _factory.Release(ObstacleKind.Ramp, handle);
        }

        /// <summary>
        /// The runner's height on a ramp can trail the slope by a frame of climbing. If the
        /// solid underside sits closer under the slope than that, riding up properly clips it
        /// and kills — which is how ramps were killing people at full speed.
        /// </summary>
        [Test]
        public void RampUndersideLeavesRoomForAFrameOfClimb()
        {
            Obstacle ramp = _factory.Get(ObstacleKind.Ramp, out int handle);
            ObstacleDefinition definition = _catalog.Get(ObstacleKind.Ramp);
            DifficultyCurve curve = DifficultyCurve.CreateDefault();

            try
            {
                BoxCollider box = ramp.transform.Find("Lethal").GetComponent<BoxCollider>();
                float solidTop = box.center.y + box.size.y * 0.5f;
                float solidStartZ = box.center.z - box.size.z * 0.5f;
                float slope = definition.RampRise / definition.RampRun;
                float slopeHeightThere = slope * (solidStartZ - definition.RampStart);

                float topSpeed = curve.EvaluateSpeed(1000000f);
                float climbPerFrame = topSpeed * (1f / 60f) * slope;

                Assert.Less(solidTop, slopeHeightThere - climbPerFrame,
                    $"The solid underside is only {slopeHeightThere - solidTop:0.00} below the slope, " +
                    $"less than the {climbPerFrame:0.00} a runner climbs in one frame at top speed.");
            }
            finally
            {
                Object.DestroyImmediate(curve);
                _factory.Release(ObstacleKind.Ramp, handle);
            }
        }

        [Test]
        public void LandableBodiesSitOnTheSurfaceLayerSoTheGroundProbeFindsThem()
        {
            Obstacle truck = _factory.Get(ObstacleKind.Truck, out int handle);
            Transform body = truck.transform.Find("Body");

            Assert.IsNotNull(body);
            Assert.AreEqual(LayerMask.NameToLayer("Surface"), body.gameObject.layer,
                "A roof the probe cannot see is not standable.");

            _factory.Release(ObstacleKind.Truck, handle);
        }

        [Test]
        public void DriftingKindsGetAMoverAndStillKindsDoNot()
        {
            Obstacle moving = _factory.Get(ObstacleKind.MovingTruck, out int movingHandle);
            Obstacle still = _factory.Get(ObstacleKind.Truck, out int stillHandle);

            Assert.IsNotNull(moving.Drift, "A drifting kind was built without a mover.");
            Assert.IsNull(still.Drift, "A stationary kind was given a mover it does not need.");

            _factory.Release(ObstacleKind.MovingTruck, movingHandle);
            _factory.Release(ObstacleKind.Truck, stillHandle);
        }

        /// <summary>
        /// The path that matters the moment real art exists, and which has never run: a
        /// definition carrying a prefab must use it instead of generating a primitive.
        /// </summary>
        [Test]
        public void AnAuthoredPrefabIsUsedInsteadOfAGeneratedShape()
        {
            GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            prefab.name = "AuthoredTruck";

            ObstacleCatalog catalog = ObstacleCatalog.CreateDefault();
            Transform root = new GameObject("PrefabFactoryRoot").transform;

            try
            {
                catalog.Get(ObstacleKind.Truck).Prefab = prefab;
                catalog.Invalidate();

                ObstacleFactory factory = new ObstacleFactory(catalog, root, Settings(), () => 0.5f);

                try
                {
                    Obstacle built = factory.Get(ObstacleKind.Truck, out int handle);

                    Assert.IsNotNull(built, "The prefab path produced nothing.");
                    Assert.AreEqual(ObstacleKind.Truck, built.Kind);
                    StringAssert.Contains("AuthoredTruck", built.name,
                        "The factory generated a primitive instead of using the authored prefab.");

                    factory.Release(ObstacleKind.Truck, handle);
                }
                finally
                {
                    factory.Dispose();
                }
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ReleasedObstaclesComeBackFromThePool()
        {
            Obstacle first = _factory.Get(ObstacleKind.Low, out int handle);
            _factory.Release(ObstacleKind.Low, handle);

            Obstacle second = _factory.Get(ObstacleKind.Low, out int _);

            Assert.AreSame(first, second, "The factory built a new instance instead of reusing one.");
        }
    }
}
