using NUnit.Framework;
using Runner.Config;
using Runner.Core;
using Runner.Spawning;
using Runner.Utils;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Runner.Tests
{
    /// <summary>
    /// Enforces the project's central performance rule: no managed allocation on the paths that
    /// run during a frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This has been asserted in code comments all through the codebase and verified nowhere.
    /// A Profiler pass would confirm it once, on one machine, on one build — these keep it true.
    /// A single stray LINQ call, string concat, boxed enum or capturing lambda will fail one of
    /// these the moment it is written.
    /// </para>
    /// <para>
    /// Every case warms up outside the measured region first. The first call into any path pays
    /// one-off JIT and static-initialisation costs that are not what this is testing.
    /// </para>
    /// <para>
    /// Note the <c>Is</c> alias at the top of the file: Unity's allocation constraint and
    /// NUnit's own <c>Is</c> collide, so this file deliberately uses Unity's and avoids NUnit's
    /// fluent assertions.
    /// </para>
    /// </remarks>
    public sealed class AllocationTests
    {
        private const float RowSpacing = 5f;

        private IntroSequence _intro;
        private ObstacleTable _table;
        private Transform _poolRoot;

        [SetUp]
        public void SetUp()
        {
            _intro = IntroSequence.CreateDefault();
            _table = ObstacleTable.CreateDefault();
            _poolRoot = new GameObject("AllocPoolRoot").transform;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_intro);
            Object.DestroyImmediate(_table);
            Object.DestroyImmediate(_poolRoot.gameObject);
        }

        /// <summary>
        /// Generation runs on every segment recycle — a burst of a dozen rows, several times a
        /// second at speed. It is also the code most likely to acquire a LINQ call by accident,
        /// since it is all filtering and weighted selection.
        /// </summary>
        [Test]
        public void GeneratingRowsDoesNotAllocate()
        {
            TrackGenerator generator = new TrackGenerator(1234, _intro, _table, 1);

            // Push past the scripted intro and warm every branch: single rows, formations,
            // coin trails and arcs.
            for (int row = 0; row < 400; row++)
            {
                generator.NextRow(row * RowSpacing, 0.9f, 0.5f);
            }

            float distance = 400f * RowSpacing;

            Assert.That(() =>
            {
                for (int row = 0; row < 500; row++)
                {
                    generator.NextRow(distance + row * RowSpacing, 0.9f, 0.5f);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WeightedPatternPickDoesNotAllocate()
        {
            _table.TryPick(500f, 0.5f, out ObstaclePattern _);
            _table.TryPickFormation(500f, 0.5f, out ObstacleFormation _);

            Assert.That(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    float roll = i / 200f;
                    _table.TryPick(500f, roll, out ObstaclePattern _);
                    _table.TryPickFormation(500f, roll, out ObstacleFormation _);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        /// <summary>Scoring runs every frame while playing, and again on every coin.</summary>
        [Test]
        public void ScoringDoesNotAllocate()
        {
            ScoreKeeper scoring = new ScoreKeeper(5, 8, 5, 60f, 0.6f);

            scoring.Tick(1f);
            scoring.Collect(1, 1.2f, 1f);

            Assert.That(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    scoring.Tick(i * 0.5f);
                    scoring.Collect(1, 1.2f, i * 0.5f);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        /// <summary>
        /// The pool exists precisely so that spawning allocates nothing. If this fails, the
        /// whole pooling design has failed at its one job.
        /// </summary>
        [Test]
        public void PoolGetAndReleaseDoNotAllocate()
        {
            ObjectPool<Transform> pool = new ObjectPool<Transform>(
                () => new GameObject("Pooled").transform, _poolRoot, 32, "AllocTest");

            Transform warm = pool.Get(out int warmHandle);
            pool.Release(warmHandle);
            Assert.IsNotNull(warm);

            Assert.That(() =>
            {
                for (int i = 0; i < 16; i++)
                {
                    pool.Get(out int handle);
                    pool.Release(handle);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        /// <summary>Sweeping a pool for live instances happens once per frame per moving kind.</summary>
        [Test]
        public void SweepingAPoolDoesNotAllocate()
        {
            ObjectPool<Transform> pool = new ObjectPool<Transform>(
                () => new GameObject("Pooled").transform, _poolRoot, 32, "SweepTest");

            for (int i = 0; i < 8; i++)
            {
                pool.Get(out int _);
            }

            pool.TryGetActive(0, out Transform _);

            Assert.That(() =>
            {
                for (int i = 0; i < pool.Capacity; i++)
                {
                    pool.TryGetActive(i, out Transform _);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        /// <summary>Evaluated once per coin when an arc is laid across a gap.</summary>
        [Test]
        public void JumpProfileEvaluationDoesNotAllocate()
        {
            JumpProfile jump = new JumpProfile(1.5f, 0.45f, 1.5f);
            float warm = jump.HeightAt(0.2f) + jump.AirTime;
            Assert.Greater(warm, 0f);

            Assert.That(() =>
            {
                float total = 0f;
                for (int i = 0; i < 1000; i++)
                {
                    total += jump.HeightAt(i * 0.001f);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        /// <summary>
        /// Difficulty is sampled every frame. AnimationCurve.Evaluate is a native call and
        /// should cost nothing managed — this pins that assumption down.
        /// </summary>
        [Test]
        public void DifficultySamplingDoesNotAllocate()
        {
            DifficultyCurve curve = DifficultyCurve.CreateDefault();

            try
            {
                curve.EvaluateSpeed(100f);
                curve.EvaluateObstacleDensity(100f);
                curve.EvaluateCoinChance(100f);

                Assert.That(() =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        curve.EvaluateSpeed(i);
                        curve.EvaluateObstacleDensity(i);
                        curve.EvaluateCoinChance(i);
                    }
                }, Is.Not.AllocatingGCMemory());
            }
            finally
            {
                Object.DestroyImmediate(curve);
            }
        }

        /// <summary>
        /// Enum values are used as switch keys and dictionary-free lookups all over the spawner.
        /// Boxing one is an easy mistake and an invisible one.
        /// </summary>
        [Test]
        public void ObstacleKindRuleChecksDoNotAllocate()
        {
            ObstacleKindRules.IsLandable(ObstacleKind.Truck);
            ObstacleKindRules.ClosesLane(ObstacleKind.Full);

            Assert.That(() =>
            {
                for (int i = 0; i < 500; i++)
                {
                    ObstacleKind kind = (ObstacleKind)(i % 11);
                    ObstacleKindRules.IsLandable(kind);
                    ObstacleKindRules.ClosesLane(kind);
                    ObstacleKindRules.IsOverhead(kind);
                }
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
