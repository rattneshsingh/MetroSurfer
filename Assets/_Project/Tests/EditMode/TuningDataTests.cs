using NUnit.Framework;
using Runner.Config;
using UnityEngine;

namespace Runner.Tests
{
    public sealed class TuningDataTests
    {
        private ObstacleTable _table;
        private DifficultyCurve _curve;
        private ObstacleCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _table = ObstacleTable.CreateDefault();
            _curve = DifficultyCurve.CreateDefault();
            _catalog = ObstacleCatalog.CreateDefault();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_table);
            Object.DestroyImmediate(_curve);
            Object.DestroyImmediate(_catalog);
        }

        [Test]
        public void DefaultTableIsPopulated()
        {
            Assert.Greater(_table.Count, 0, "A table with no patterns generates an empty road.");
        }

        [Test]
        public void PickRespectsMinDistance()
        {
            for (int i = 0; i < 200; i++)
            {
                float roll = i / 200f;

                if (_table.TryPick(0f, roll, out ObstaclePattern pattern))
                {
                    Assert.LessOrEqual(pattern.MinDistance, 0f,
                        $"Pattern gated at {pattern.MinDistance}m appeared at distance 0.");
                }
            }
        }

        [Test]
        public void PickNeverReturnsAnImpassablePattern()
        {
            for (int i = 0; i < 500; i++)
            {
                if (_table.TryPick(5000f, i / 500f, out ObstaclePattern pattern))
                {
                    Assert.IsFalse(pattern.IsImpassable, "An unbeatable pattern was selectable.");
                }
            }
        }

        [Test]
        public void PickSucceedsAcrossTheWholeRollRange()
        {
            // The weighted walk subtracts floats; the ends of the range are where it drifts.
            foreach (float roll in new[] { 0f, 0.0001f, 0.5f, 0.9999f, 0.99999994f })
            {
                Assert.IsTrue(_table.TryPick(1000f, roll, out ObstaclePattern _),
                    $"Roll {roll} failed to select anything.");
            }
        }

        [Test]
        public void EveryFormationRowIsSurvivable()
        {
            for (int i = 0; i < 500; i++)
            {
                if (!_table.TryPickFormation(5000f, i / 500f, out ObstacleFormation formation))
                {
                    continue;
                }

                for (int row = 0; row < formation.RowCount; row++)
                {
                    IntroRow entry = formation.Rows[row];

                    if (!_catalog.ClosesLane(entry.Kind))
                    {
                        continue;
                    }

                    Assert.AreNotEqual(ObstaclePattern.AllLanesMask, entry.LaneMask,
                        $"Formation '{formation.Name}' row {row} closes every lane with {entry.Kind}.");
                }
            }
        }

        [Test]
        public void IntroTeachesEveryVerbBeforeProceduralGenerationStarts()
        {
            IntroSequence intro = IntroSequence.CreateDefault();

            try
            {
                var taught = new System.Collections.Generic.HashSet<ObstacleKind>();

                for (int i = 0; i < intro.Count; i++)
                {
                    IntroRow row = intro.Get(i);
                    if (row.LaneMask != 0)
                    {
                        taught.Add(row.Kind);
                    }
                }

                foreach (ObstacleKind required in new[]
                         {
                             ObstacleKind.Low, ObstacleKind.High, ObstacleKind.Full,
                             ObstacleKind.Ramp, ObstacleKind.Truck
                         })
                {
                    Assert.IsTrue(taught.Contains(required), $"The intro never teaches {required}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(intro);
            }
        }

        [Test]
        public void SpeedRisesWithDistanceAndThenHolds()
        {
            float start = _curve.EvaluateSpeed(0f);
            float mid = _curve.EvaluateSpeed(1000f);
            float late = _curve.EvaluateSpeed(100000f);

            Assert.AreEqual(_curve.StartSpeed, start, 0.01f);
            Assert.Greater(mid, start, "Speed must ramp with distance.");
            Assert.GreaterOrEqual(late, mid);
            Assert.Less(late, 1000f, "Speed must be clamped, not unbounded.");
        }

        [Test]
        public void DensityStaysNormalised()
        {
            for (float distance = 0f; distance < 20000f; distance += 250f)
            {
                float density = _curve.EvaluateObstacleDensity(distance);
                Assert.GreaterOrEqual(density, 0f);
                Assert.LessOrEqual(density, 1f);
            }
        }
    }
}
