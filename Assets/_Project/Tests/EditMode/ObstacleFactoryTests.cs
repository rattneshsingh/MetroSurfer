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

        /// <summary>A ramp that can kill you is a wall with extra steps.</summary>
        [Test]
        public void RampsHaveNoLethalVolume()
        {
            Obstacle ramp = _factory.Get(ObstacleKind.Ramp, out int handle);

            Assert.IsNull(ramp.transform.Find("Lethal"), "A ramp must never be lethal.");

            _factory.Release(ObstacleKind.Ramp, handle);
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
