using NUnit.Framework;
using Runner.Config;
using Runner.Spawning;
using UnityEngine;

namespace Runner.Tests
{
    /// <summary>
    /// Drives the spawner's real Tick against real Transforms, without needing Play mode.
    /// </summary>
    /// <remarks>
    /// The spawner is scrolled explicitly by GameManager rather than by its own Update, which
    /// means the whole scroll-and-recycle system can be stepped by hand here — thousands of
    /// frames in milliseconds, deterministically, with no game loop involved. That is the
    /// payoff for not putting Update on it.
    /// </remarks>
    public sealed class SegmentSpawnerTests
    {
        private const float FrameTime = 1f / 60f;
        private const float LaneWidth = 2.2f;

        private GameObject _host;
        private SegmentSpawner _spawner;
        private DifficultyCurve _curve;
        private ObstacleTable _table;
        private IntroSequence _intro;

        [SetUp]
        public void SetUp()
        {
            _curve = DifficultyCurve.CreateDefault();
            _table = ObstacleTable.CreateDefault();
            _intro = IntroSequence.CreateDefault();

            _host = new GameObject("SpawnerHost");
            _spawner = _host.AddComponent<SegmentSpawner>();
            _spawner.Initialize(LaneWidth, _curve, _table, null, new JumpProfile(1.5f, 0.45f, 1.5f));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_curve);
            Object.DestroyImmediate(_table);
            Object.DestroyImmediate(_intro);
        }

        [Test]
        public void InitialisingBuildsATrack()
        {
            Assert.IsTrue(_spawner.IsBuilt);
            Assert.Greater(SegmentPositions().Length, 1, "No segments were created.");
            Assert.Greater(ActiveObstacleCount(), 0, "The track was built empty.");
        }

        /// <summary>
        /// The bug this exists for: segment placement used a running counter that never scrolled
        /// with the world, so every recycle dropped the next segment further ahead of the real
        /// track edge than the last. The hole widened by exactly the distance travelled, which
        /// is why the ground "disappeared randomly" only after a while.
        /// </summary>
        [Test]
        public void TrackStaysContinuousOverALongRun()
        {
            float distance = 0f;

            for (int frame = 0; frame < 20000; frame++)
            {
                float speed = _curve.EvaluateSpeed(distance);
                distance += speed * FrameTime;
                _spawner.Tick(FrameTime, speed, distance);

                if (frame % 500 != 0)
                {
                    continue;
                }

                AssertUniformSpacing(frame, distance);
            }

            AssertUniformSpacing(20000, distance);
        }

        /// <summary>Restarting must not quietly leak a second track's worth of objects.</summary>
        [Test]
        public void RepeatedRestartsDoNotGrowThePools()
        {
            float distance = 0f;
            for (int frame = 0; frame < 2000; frame++)
            {
                float speed = _curve.EvaluateSpeed(distance);
                distance += speed * FrameTime;
                _spawner.Tick(FrameTime, speed, distance);
            }

            _spawner.ResetRun();
            int afterFirstReset = TotalObstacleInstances();

            for (int run = 0; run < 5; run++)
            {
                for (int frame = 0; frame < 2000; frame++)
                {
                    float speed = _curve.EvaluateSpeed(distance);
                    distance += speed * FrameTime;
                    _spawner.Tick(FrameTime, speed, distance);
                }

                _spawner.ResetRun();
            }

            Assert.AreEqual(afterFirstReset, TotalObstacleInstances(),
                "Pools grew across restarts — something is being spawned without being released.");
        }

        [Test]
        public void ResetRunRebuildsTheSameTrack()
        {
            float[] first = SegmentPositions();

            float distance = 0f;
            for (int frame = 0; frame < 3000; frame++)
            {
                float speed = _curve.EvaluateSpeed(distance);
                distance += speed * FrameTime;
                _spawner.Tick(FrameTime, speed, distance);
            }

            _spawner.ResetRun();
            float[] afterReset = SegmentPositions();

            CollectionAssert.AreEqual(first, afterReset, "Reset did not restore the starting layout.");
        }

        /// <summary>The track has to extend far enough ahead that its end is never on screen.</summary>
        [Test]
        public void TrackAlwaysExtendsPastTheCameraFarClip()
        {
            const float FarClip = 200f;
            float distance = 0f;

            for (int frame = 0; frame < 10000; frame++)
            {
                float speed = _curve.EvaluateSpeed(distance);
                distance += speed * FrameTime;
                _spawner.Tick(FrameTime, speed, distance);

                if (frame % 250 != 0)
                {
                    continue;
                }

                float[] positions = SegmentPositions();
                float furthest = float.MinValue;

                for (int i = 0; i < positions.Length; i++)
                {
                    furthest = Mathf.Max(furthest, positions[i]);
                }

                Assert.Greater(furthest, FarClip,
                    $"Frame {frame}: the track ends at {furthest:0}, inside the {FarClip}m far clip.");
            }
        }

        private void AssertUniformSpacing(int frame, float distance)
        {
            float[] positions = SegmentPositions();
            System.Array.Sort(positions);

            float minGap = float.MaxValue;
            float maxGap = float.MinValue;

            for (int i = 1; i < positions.Length; i++)
            {
                float gap = positions[i] - positions[i - 1];
                minGap = Mathf.Min(minGap, gap);
                maxGap = Mathf.Max(maxGap, gap);
            }

            Assert.Less(maxGap - minGap, 0.01f,
                $"Frame {frame} ({distance:0}m): segments are unevenly spaced " +
                $"(gaps {minGap:0.00} to {maxGap:0.00}), so the track has a hole in it.");
        }

        /// <summary>
        /// Reads the live segment layout straight off the hierarchy. Coupling a test to
        /// structure is a fair trade for not adding an accessor that exists only for tests.
        /// </summary>
        private float[] SegmentPositions()
        {
            Transform segmentRoot = _host.transform.Find("~Segments");
            Assert.IsNotNull(segmentRoot, "Segment root was never created.");

            float[] positions = new float[segmentRoot.childCount];
            for (int i = 0; i < segmentRoot.childCount; i++)
            {
                positions[i] = segmentRoot.GetChild(i).localPosition.z;
            }

            return positions;
        }

        private int ActiveObstacleCount() => _host.GetComponentsInChildren<Obstacle>(false).Length;

        private int TotalObstacleInstances() => _host.GetComponentsInChildren<Obstacle>(true).Length;
    }
}
