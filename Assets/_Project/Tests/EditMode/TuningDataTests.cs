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

        private FormationQuery Query(
            float distance = 5000f,
            TrackState state = TrackState.Street,
            float min = 0f,
            float max = 1f)
            => new FormationQuery(distance, state, min, max, new ObstacleFormation[4], 0);

        [Test]
        public void EveryChunkRowIsSurvivable()
        {
            for (int i = 0; i < 500; i++)
            {
                if (!_table.TryPickFormation(Query(), i / 500f, out ObstacleFormation chunk))
                {
                    continue;
                }

                for (int row = 0; row < chunk.RowCount; row++)
                {
                    IntroRow entry = chunk.Rows[row];

                    if (!_catalog.ClosesLane(entry.Kind))
                    {
                        continue;
                    }

                    Assert.AreNotEqual(ObstaclePattern.AllLanesMask, entry.LaneMask,
                        $"Chunk '{chunk.Name}' row {row} closes every lane with {entry.Kind}.");
                }
            }
        }

        /// <summary>
        /// A roof run cannot begin on the road, and a street chunk cannot begin in mid-air. This
        /// is the rule that keeps the generator from stitching nonsense together.
        /// </summary>
        [Test]
        public void SelectionNeverReturnsAChunkThatCannotBeEntered()
        {
            foreach (TrackState state in new[] { TrackState.Street, TrackState.Elevated })
            {
                for (int i = 0; i < 300; i++)
                {
                    if (!_table.TryPickFormation(Query(state: state), i / 300f, out ObstacleFormation chunk))
                    {
                        continue;
                    }

                    Assert.IsTrue(
                        chunk.Entry == TrackState.Either || chunk.Entry == state,
                        $"Chunk '{chunk.Name}' wants to start {chunk.Entry} but the player is {state}.");
                }
            }
        }

        /// <summary>Nothing brutal in the opening seconds, whatever the dice say.</summary>
        [Test]
        public void TheOpeningIsNeverHard()
        {
            const float OpeningCeiling = 0.3f;

            for (int i = 0; i < 300; i++)
            {
                if (!_table.TryPickFormation(Query(distance: 0f, max: OpeningCeiling), i / 300f,
                        out ObstacleFormation chunk))
                {
                    continue;
                }

                Assert.LessOrEqual(chunk.Difficulty, OpeningCeiling,
                    $"Chunk '{chunk.Name}' at difficulty {chunk.Difficulty} appeared in the opening.");
            }
        }

        /// <summary>Repeating a chunk back to back is the fastest way to make a level read as generated.</summary>
        [Test]
        public void SelectionAvoidsChunksItJustUsed()
        {
            ObstacleFormation[] recent = new ObstacleFormation[4];

            Assert.IsTrue(_table.TryPickFormation(Query(), 0.5f, out ObstacleFormation first));
            recent[0] = first;

            FormationQuery avoiding = new FormationQuery(
                5000f, TrackState.Street, 0f, 1f, recent, 1);

            int differed = 0;
            for (int i = 0; i < 50; i++)
            {
                if (_table.TryPickFormation(avoiding, i / 50f, out ObstacleFormation next) &&
                    !ReferenceEquals(next, first))
                {
                    differed++;
                }
            }

            Assert.Greater(differed, 40, "Selection kept handing back the chunk it was told to avoid.");
        }

        /// <summary>
        /// Both halves of the stack have to exist. Without a way up the roofs are unreachable;
        /// without a way down the player parks up there and the street stops mattering.
        /// </summary>
        [Test]
        public void ThereAreChunksThatClimbAndChunksThatDescend()
        {
            int climbs = 0;
            int descents = 0;
            int roofRuns = 0;

            for (int i = 0; i < 800; i++)
            {
                foreach (TrackState state in new[] { TrackState.Street, TrackState.Elevated })
                {
                    if (!_table.TryPickFormation(Query(state: state), i / 800f, out ObstacleFormation chunk))
                    {
                        continue;
                    }

                    if (chunk.Entry == TrackState.Street && chunk.Exit == TrackState.Elevated)
                    {
                        climbs++;
                    }
                    else if (chunk.Entry == TrackState.Elevated && chunk.Exit == TrackState.Street)
                    {
                        descents++;
                    }
                    else if (chunk.Entry == TrackState.Elevated && chunk.Exit == TrackState.Elevated)
                    {
                        roofRuns++;
                    }
                }
            }

            Assert.Greater(climbs, 0, "No chunk ever takes the player up.");
            Assert.Greater(descents, 0, "No chunk ever brings the player down.");
            Assert.Greater(roofRuns, 0, "Nothing happens once the player is up there.");
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
