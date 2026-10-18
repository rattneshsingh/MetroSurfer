using NUnit.Framework;
using Runner.Config;
using Runner.Spawning;
using UnityEngine;

namespace Runner.Tests
{
    /// <summary>
    /// Regressions found by playing rather than by the rules tests: things that were
    /// mechanically correct but looked wrong. Each test names the bug it pins down.
    /// </summary>
    public sealed class PresentationRegressionTests
    {
        /// <summary>The spawner's default row spacing. Convoy trucks must be exactly this long.</summary>
        private const float RowSpacing = 5f;

        /// <summary>Matches SegmentSpawner's defaults: ground obstacles cull at -1, overhead at 0.</summary>
        private const float GroundCull = -1f;
        private const float OverheadCull = 0f;

        /// <summary>A pit's depth cutter sits this far above the road; lane dashes must stay under it.</summary>
        private const float PitCutterHeight = 0.005f;

        private ObstacleCatalog _catalog;
        private Transform _poolRoot;
        private ObstacleFactory _factory;
        private GameObject _segmentObject;

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
            if (_segmentObject != null)
            {
                Object.DestroyImmediate(_segmentObject);
            }

            _factory.Dispose();
            Object.DestroyImmediate(_poolRoot.gameObject);
            Object.DestroyImmediate(_catalog);
        }

        // ---- Hiding: the cull line must not eat things the runner is still on ----

        [Test]
        public void RampStaysVisibleWhileTheRunnerClimbsIt()
        {
            // A ramp starts at its position and runs three metres forward. At -2 it reaches +1:
            // the runner is halfway up it. It used to vanish here.
            Assert.IsTrue(VisibleAfterCull(ObstacleKind.Ramp, -2f), "Ramp vanished mid-climb.");
            Assert.IsFalse(VisibleAfterCull(ObstacleKind.Ramp, -4.5f), "Ramp never hid once fully passed.");
        }

        [Test]
        public void TruckRoofStaysVisibleUnderTheRunnersFeet()
        {
            // A truck is centred on its position. At -2 its front is still ahead of the runner.
            Assert.IsTrue(VisibleAfterCull(ObstacleKind.Truck, -2f), "Truck vanished from under the runner.");
            Assert.IsFalse(VisibleAfterCull(ObstacleKind.Truck, -4f), "Truck never hid once fully passed.");
        }

        [Test]
        public void OverheadBarHidesAsSoonAsItIsPassed()
        {
            // Just behind the runner, the chase camera looks back through a bar above their head.
            Assert.IsFalse(VisibleAfterCull(ObstacleKind.High, -0.1f), "Bar filled the camera after being passed.");
            Assert.IsTrue(VisibleAfterCull(ObstacleKind.High, 0.5f), "Bar hid before the runner reached it.");
        }

        [Test]
        public void OnlyRaisedBarriersCountAsOverhead()
        {
            Assert.IsTrue(Build(ObstacleKind.High).IsOverhead, "A bar to roll under must be overhead.");
            Assert.IsTrue(Build(ObstacleKind.Underpass).IsOverhead, "A low bridge must be overhead.");

            ObstacleKind[] ground = { ObstacleKind.Low, ObstacleKind.Full, ObstacleKind.Ramp, ObstacleKind.Truck, ObstacleKind.Pit };
            for (int i = 0; i < ground.Length; i++)
            {
                Assert.IsFalse(Build(ground[i]).IsOverhead, $"{ground[i]} is on the ground, not overhead.");
            }
        }

        // ---- Pits: merged neighbours must stay merged ----

        [Test]
        public void OpenedPitWallsStayOpenThroughHideAndShow()
        {
            Obstacle obstacle = Build(ObstacleKind.Pit);
            PitHole pit = obstacle.Pit;
            Assert.IsNotNull(pit, "A pit was built without its hole.");

            GameObject left = obstacle.transform.Find("Interior/WallLeft").gameObject;

            pit.OpenLeft();
            obstacle.Hide();
            obstacle.OnSpawn();

            // Obstacle toggles renderers on hide and show. A merged wall must not come back.
            Assert.IsFalse(left.activeSelf, "Showing the pit again brought back a wall shared with a neighbour.");

            pit.ResetWalls();
            Assert.IsTrue(left.activeSelf, "A pit fresh from the pool must have every wall.");
        }

        [Test]
        public void PitFillsItsWholeLaneCell()
        {
            Obstacle obstacle = Build(ObstacleKind.Pit);
            obstacle.Pit.Fit(2.2f, RowSpacing);

            Vector3 interior = obstacle.transform.Find("Interior").localScale;
            Vector3 cutter = obstacle.transform.Find("Cutter").localScale;

            Assert.AreEqual(2.2f, interior.x, 0.001f);
            Assert.AreEqual(RowSpacing, interior.z, 0.001f);
            Assert.AreEqual(2.2f, cutter.x, 0.001f, "Road would show in a strip between neighbouring pits.");
            Assert.AreEqual(RowSpacing, cutter.z, 0.001f, "Road would show in a strip between pits front to back.");
        }

        // ---- Convoys: no gaps underfoot ----

        [Test]
        public void TrucksAreExactlyOneRowLong()
        {
            ObstacleDefinition truck = _catalog.Get(ObstacleKind.Truck);
            Assert.AreEqual(RowSpacing, truck.Size.z, 0.001f,
                "Trucks in consecutive rows must meet nose to tail, or convoys show gaps.");
        }

        // ---- Spawner-level: lane markings and coins ----

        [Test]
        public void EverySegmentHasLaneMarkingsUnderThePitCutter()
        {
            using (SpawnerFixture fixture = new SpawnerFixture())
            {
                Segment[] segments = fixture.Host.GetComponentsInChildren<Segment>(true);
                Assert.Greater(segments.Length, 0);

                int expected = -1;
                for (int s = 0; s < segments.Length; s++)
                {
                    int dashes = 0;
                    foreach (Transform child in segments[s].transform)
                    {
                        if (child.name != "LaneDash")
                        {
                            continue;
                        }

                        dashes++;
                        Assert.IsNull(child.GetComponent<Collider>(), "A lane dash must be visual only.");

                        Bounds bounds = child.GetComponent<Renderer>().bounds;
                        Assert.Less(bounds.max.y - segments[s].transform.position.y, PitCutterHeight,
                            "A lane dash pokes above the pit cutter and would float across holes.");
                    }

                    Assert.Greater(dashes, 0, $"Segment {s} has no lane markings.");
                    if (expected < 0)
                    {
                        expected = dashes;
                    }

                    Assert.AreEqual(expected, dashes, "Segments disagree on how many lane dashes they carry.");
                }
            }
        }

        [Test]
        public void CoinsNeverAllFaceTheSameWayAtOnce()
        {
            using (SpawnerFixture fixture = new SpawnerFixture())
            {
                fixture.Spawner.Tick(1f / 60f, 10f, 0f);

                Transform[] all = fixture.Host.GetComponentsInChildren<Transform>(true);
                Quaternion first = Quaternion.identity;
                bool seenFirst = false;
                bool different = false;

                for (int i = 0; i < all.Length && !different; i++)
                {
                    if (all[i].name != "Disc")
                    {
                        continue;
                    }

                    if (!seenFirst)
                    {
                        first = all[i].localRotation;
                        seenFirst = true;
                    }
                    else if (Quaternion.Angle(first, all[i].localRotation) > 1f)
                    {
                        different = true;
                    }
                }

                Assert.IsTrue(seenFirst, "No coins were built.");
                Assert.IsTrue(different, "Every coin turned in step; trails flicker into lines when edge-on.");
            }
        }

        // ---- Helpers ----

        private Obstacle Build(ObstacleKind kind)
        {
            Obstacle obstacle = _factory.Get(kind, out _);
            Assert.IsNotNull(obstacle, $"{kind} could not be built.");
            return obstacle;
        }

        /// <summary>Places one obstacle in a segment at the origin, runs the cull, reports what is left.</summary>
        private bool VisibleAfterCull(ObstacleKind kind, float localZ)
        {
            if (_segmentObject != null)
            {
                Object.DestroyImmediate(_segmentObject);
            }

            _segmentObject = new GameObject("Segment");
            Segment segment = _segmentObject.AddComponent<Segment>();
            segment.Initialize(4, 0);

            Obstacle obstacle = _factory.Get(kind, out int handle);
            obstacle.OnSpawn();
            segment.AddObstacle(obstacle, handle, kind, localZ);
            segment.HideObstaclesBehind(GroundCull, OverheadCull);

            bool visible = AnyRendererShowing(obstacle);
            _factory.Release(kind, handle);
            return visible;
        }

        private static bool AnyRendererShowing(Obstacle obstacle)
        {
            Renderer[] renderers = obstacle.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].enabled && renderers[i].gameObject.activeInHierarchy)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A real spawner on default data, torn down with everything it built.</summary>
        private sealed class SpawnerFixture : System.IDisposable
        {
            private readonly DifficultyCurve _curve = DifficultyCurve.CreateDefault();
            private readonly ObstacleTable _table = ObstacleTable.CreateDefault();

            public readonly GameObject Host = new GameObject("SpawnerHost");
            public readonly SegmentSpawner Spawner;

            public SpawnerFixture()
            {
                Spawner = Host.AddComponent<SegmentSpawner>();
                Spawner.Initialize(2.2f, _curve, _table, null, new JumpProfile(1.5f, 0.45f, 1.5f));
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Host);
                Object.DestroyImmediate(_curve);
                Object.DestroyImmediate(_table);
            }
        }
    }
}
