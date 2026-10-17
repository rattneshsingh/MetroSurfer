using NUnit.Framework;
using Runner.Config;
using Runner.Spawning;
using UnityEngine;

namespace Runner.Tests
{
    /// <summary>
    /// Property tests over the generator. These run thousands of rows and assert things that
    /// must hold for every one of them — which is the only practical way to catch a formation
    /// that starves the track or a row that cannot be beaten, since both look like bad luck
    /// when you meet them once while playing.
    /// </summary>
    public sealed class TrackGeneratorTests
    {
        private const int Rows = 10000;
        private const float RowSpacing = 5f;

        private IntroSequence _intro;
        private ObstacleTable _table;
        private ObstacleCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _intro = IntroSequence.CreateDefault();
            _table = ObstacleTable.CreateDefault();
            _catalog = ObstacleCatalog.CreateDefault();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_intro);
            Object.DestroyImmediate(_table);
            Object.DestroyImmediate(_catalog);
        }

        private TrackGenerator NewGenerator(int seed = 1234) =>
            new TrackGenerator(seed, _intro, _table, _catalog, minRowGapAfterHardRow: 1);

        /// <summary>Vehicles that move at the player or brake in front of them: dodged, not climbed.</summary>
        private bool IsTraffic(ObstacleKind kind)
        {
            ObstacleDefinition definition = _catalog.Get(kind);
            return definition != null &&
                   (definition.Drift == DriftMode.Approach || definition.Drift == DriftMode.Decelerate);
        }

        /// <summary>
        /// The fairness invariant. A row that closes every lane cannot be survived by any
        /// input, so generating one is a guaranteed unfair death.
        /// </summary>
        [Test]
        public void EveryRowLeavesARoute([Values(1, 7, 99, 20260817)] int seed)
        {
            TrackGenerator generator = NewGenerator(seed);

            for (int row = 0; row < Rows; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 1f, coinChance: 0.5f);

                if (!_catalog.ClosesLane(plan.Kind))
                {
                    continue;
                }

                Assert.AreNotEqual(
                    TrackGenerator.AllLanesMask,
                    plan.LaneMask,
                    $"Row {row} closed every lane with {plan.Kind}, which cannot be beaten.");
            }
        }

        /// <summary>
        /// The bug this exists for: the row-gap counter used to saturate at int.MaxValue and
        /// overflow negative, after which the gap was never satisfied again and the track went
        /// permanently empty. Nothing about that is visible until several minutes into a run.
        /// </summary>
        [Test]
        public void ObstaclesNeverStopAppearing()
        {
            TrackGenerator generator = NewGenerator();
            int lastObstacleRow = 0;
            int longestDrought = 0;

            for (int row = 0; row < Rows; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 0.8f, coinChance: 0.4f);

                if (plan.HasObstacles)
                {
                    longestDrought = Mathf.Max(longestDrought, row - lastObstacleRow);
                    lastObstacleRow = row;
                }
            }

            Assert.Less(Rows - lastObstacleRow, 50, "Generation stopped producing obstacles entirely.");
            Assert.Less(longestDrought, 50, $"Went {longestDrought} rows with nothing to do.");
        }

        /// <summary>
        /// Ramps, trucks and trains were added to the intro but never to the procedural table,
        /// so after two scripted lessons they never appeared again. That was invisible in code
        /// review and obvious within a minute of playing.
        /// </summary>
        [Test]
        public void EveryObstacleKindEventuallyAppears()
        {
            TrackGenerator generator = NewGenerator();
            var seen = new System.Collections.Generic.HashSet<ObstacleKind>();

            for (int row = 0; row < Rows; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 0.9f, coinChance: 0.4f);

                if (plan.HasObstacles)
                {
                    seen.Add(plan.Kind);
                }
            }

            foreach (ObstacleKind kind in System.Enum.GetValues(typeof(ObstacleKind)))
            {
                Assert.IsTrue(seen.Contains(kind), $"{kind} never appeared in {Rows} rows.");
            }
        }

        /// <summary>
        /// A formation's meaning is its ordering — a ramp is only useful because a truck follows
        /// it. If anything interleaves, the ramp launches you at nothing.
        /// </summary>
        [Test]
        public void FormationsRunToCompletionWithoutInterleaving()
        {
            TrackGenerator generator = NewGenerator();
            bool wasInFormation = false;
            int formationsCompleted = 0;

            for (int row = 0; row < Rows; row++)
            {
                generator.NextRow(row * RowSpacing, density: 0.9f, coinChance: 0.3f);

                if (wasInFormation && !generator.InFormation)
                {
                    formationsCompleted++;
                }

                wasInFormation = generator.InFormation;
            }

            Assert.Greater(formationsCompleted, 10, "Formations never ran, so the test proves nothing.");
        }

        /// <summary>A ramp that leads nowhere is a launch into empty air.</summary>
        [Test]
        public void EveryRampIsFollowedBySomethingLandable()
        {
            TrackGenerator generator = NewGenerator();
            bool previousWasRamp = false;
            int rampsChecked = 0;

            for (int row = 0; row < Rows; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 0.9f, coinChance: 0.3f);

                if (previousWasRamp)
                {
                    rampsChecked++;
                    Assert.IsTrue(
                        _catalog.IsLandable(plan.Kind) && plan.HasObstacles,
                        $"Row {row}: a ramp led to {plan.Kind} instead of something landable.");
                }

                previousWasRamp = plan.HasObstacles && plan.Kind == ObstacleKind.Ramp;
            }

            Assert.Greater(rampsChecked, 5, "No ramps were generated, so the test proves nothing.");
        }

        /// <summary>Coins on a landable row belong on the roof, or the safe play is to ignore the stack.</summary>
        [Test]
        public void CoinsOnLandableRowsSitOnTheRoof()
        {
            TrackGenerator generator = NewGenerator();
            int roofTrails = 0;

            for (int row = 0; row < Rows; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 0.9f, coinChance: 1f);

                if (!plan.HasCoins)
                {
                    continue;
                }

                bool laneOccupied = (plan.LaneMask & (1 << plan.CoinLane)) != 0;

                if (plan.CoinsOnRoof)
                {
                    roofTrails++;
                    Assert.IsTrue(laneOccupied, $"Row {row}: roof coins in a lane with nothing to stand on.");
                    Assert.IsTrue(_catalog.IsLandable(plan.Kind), $"Row {row}: roof coins over {plan.Kind}.");
                }
                else
                {
                    Assert.IsFalse(laneOccupied, $"Row {row}: street coins inside a {plan.Kind}.");
                }
            }

            Assert.Greater(roofTrails, 5, "No roof coins were generated, so the test proves nothing.");
        }

        /// <summary>
        /// An underpass kills anyone on a roof, so it needs clear rows in front of it — that gap
        /// is the time a roof runner has to fall back to the street. Author one without the room
        /// and it is an unavoidable death for anyone who did the climb they were rewarded for.
        /// </summary>
        [Test]
        public void UnderpassAlwaysLeavesRoomToGetDown()
        {
            TrackGenerator generator = NewGenerator();
            var recent = new System.Collections.Generic.Queue<RowPlan>();
            int underpassesChecked = 0;

            for (int row = 0; row < Rows; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 0.9f, coinChance: 0.3f);

                if (plan.HasObstacles && plan.Kind == ObstacleKind.Underpass)
                {
                    underpassesChecked++;
                    Assert.AreEqual(2, recent.Count, $"Row {row}: underpass appeared too early to check.");

                    foreach (RowPlan previous in recent)
                    {
                        Assert.IsFalse(
                            previous.HasObstacles && _catalog.IsLandable(previous.Kind),
                            $"Row {row}: underpass sits {recent.Count} rows after a {previous.Kind}, " +
                            "leaving no room to drop off it.");
                    }
                }

                recent.Enqueue(plan);
                if (recent.Count > 2)
                {
                    recent.Dequeue();
                }
            }

            Assert.Greater(underpassesChecked, 5, "No underpasses were generated, so the test proves nothing.");
        }

        /// <summary>
        /// A coin arc traces a jump across a gap, so the row it sits on must actually be a gap
        /// with landable ground on both sides. An arc over solid floor teaches a jump that was
        /// never needed; an arc leading nowhere teaches one that cannot be survived.
        /// </summary>
        [Test]
        public void CoinArcsSpanRealGapsBetweenLandableRows()
        {
            TrackGenerator generator = NewGenerator();
            RowPlan previous = default;
            bool previousArc = false;
            int arcsChecked = 0;

            for (int row = 0; row < Rows; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 0.9f, coinChance: 0.3f);

                if (plan.CoinArc)
                {
                    arcsChecked++;
                    Assert.IsFalse(plan.HasObstacles, $"Row {row}: an arc was drawn over solid ground.");
                    Assert.IsTrue(
                        previous.HasObstacles && _catalog.IsLandable(previous.Kind),
                        $"Row {row}: an arc launches from {previous.Kind}, which is not landable.");
                }

                if (previousArc)
                {
                    Assert.IsTrue(
                        plan.HasObstacles && _catalog.IsLandable(plan.Kind),
                        $"Row {row}: an arc landed on {plan.Kind}, which is not landable.");
                }

                previousArc = plan.CoinArc;
                previous = plan;
            }

            Assert.Greater(arcsChecked, 5, "No coin arcs were generated, so the test proves nothing.");
        }

        /// <summary>
        /// A stopping bus punishes committing early, which only works if there was time to
        /// commit. Drop one directly behind another obstacle and it stops being a test of
        /// reading ahead and becomes an ambush.
        /// </summary>
        [Test]
        public void StoppingBusAlwaysHasAClearApproach()
        {
            TrackGenerator generator = NewGenerator();
            RowPlan previous = default;
            int busesChecked = 0;

            for (int row = 0; row < Rows; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 0.9f, coinChance: 0.3f);

                if (plan.HasObstacles && plan.Kind == ObstacleKind.StoppingBus)
                {
                    busesChecked++;
                    Assert.IsFalse(previous.HasObstacles,
                        $"Row {row}: a stopping bus sits directly behind a {previous.Kind}, " +
                        "leaving no room to read it.");
                }

                previous = plan;
            }

            Assert.Greater(busesChecked, 5, "No stopping buses were generated, so the test proves nothing.");
        }

        /// <summary>
        /// The state machine, checked from the outside. A roof can only appear where the player
        /// could actually be standing on one — directly after another roof, after a ramp, or
        /// across a gap that a roof-to-roof jump spans. Anything else is a platform floating in
        /// mid-air that the player has no way to reach.
        /// <para>
        /// Traffic is exempt. Oncoming trucks and stopping buses can be landed on, but on the
        /// street they are hazards to dodge, not roofs the player is meant to be standing on.
        /// </para>
        /// </summary>
        [Test]
        public void RoofsOnlyAppearWhereThePlayerCouldReachThem(
            [Values(1234, 1, 7, 42, 99, 2024)] int seed)
        {
            // Several seeds, because a single one walked straight past the state desync this
            // test exists to catch.
            TrackGenerator generator = NewGenerator(seed);
            RowPlan previous = default;
            int roofsChecked = 0;

            for (int row = 0; row < Rows; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 0.9f, coinChance: 0.4f);

                if (plan.HasObstacles && _catalog.IsLandable(plan.Kind) && !IsTraffic(plan.Kind))
                {
                    bool reachable =
                        previous.CoinArc ||
                        (previous.HasObstacles &&
                         (_catalog.IsLandable(previous.Kind) || previous.Kind == ObstacleKind.Ramp));

                    if (roofsChecked > 0)
                    {
                        Assert.IsTrue(reachable,
                            $"Row {row}: a {plan.Kind} follows {(previous.HasObstacles ? previous.Kind.ToString() : "empty road")}, " +
                            "so there is no way up onto it.");
                    }

                    roofsChecked++;
                }

                previous = plan;
            }

            Assert.Greater(roofsChecked, 20, "Barely any roofs were generated, so the stack is not being used.");
        }

        /// <summary>
        /// Difficulty has to be gated by distance, or the opening kills people who have not yet
        /// learnt the controls.
        /// </summary>
        [Test]
        public void TheOpeningStretchStaysGentle()
        {
            TrackGenerator generator = NewGenerator();

            int obstacleRows = 0;
            int denseRows = 0;

            // Roughly the first 200m, past the scripted intro.
            for (int row = 0; row < 40 + _intro.Count; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 0.9f, coinChance: 0.4f);

                if (!plan.HasObstacles)
                {
                    continue;
                }

                obstacleRows++;

                // Three lanes closed at once is the shape of a demanding row.
                if (plan.LaneMask == TrackGenerator.AllLanesMask)
                {
                    denseRows++;
                }
            }

            Assert.Greater(obstacleRows, 0, "The opening was completely empty.");
            Assert.Less(denseRows, obstacleRows, "Every obstacle row in the opening was full width.");
        }

        /// <summary>Same seed, same run. Tuning against a moving target is impossible.</summary>
        [Test]
        public void GenerationIsDeterministicForASeed()
        {
            TrackGenerator a = NewGenerator(4242);
            TrackGenerator b = NewGenerator(4242);

            for (int row = 0; row < 2000; row++)
            {
                RowPlan planA = a.NextRow(row * RowSpacing, 0.7f, 0.4f);
                RowPlan planB = b.NextRow(row * RowSpacing, 0.7f, 0.4f);

                Assert.AreEqual(planA.LaneMask, planB.LaneMask, $"Lane mask diverged at row {row}.");
                Assert.AreEqual(planA.Kind, planB.Kind, $"Kind diverged at row {row}.");
                Assert.AreEqual(planA.CoinLane, planB.CoinLane, $"Coin lane diverged at row {row}.");
            }
        }

        /// <summary>Reset must return the generator to its exact starting state, restart included.</summary>
        [Test]
        public void ResetReplaysTheRunIdentically()
        {
            TrackGenerator generator = NewGenerator(777);

            var first = new int[500];
            for (int row = 0; row < first.Length; row++)
            {
                first[row] = generator.NextRow(row * RowSpacing, 0.7f, 0.4f).LaneMask;
            }

            generator.Reset();

            for (int row = 0; row < first.Length; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, 0.7f, 0.4f);
                Assert.AreEqual(first[row], plan.LaneMask, $"Row {row} differed after Reset.");
            }
        }

        /// <summary>The opening is scripted so the first thirty seconds teach. It must not be random.</summary>
        [Test]
        public void IntroRunsBeforeAnythingProcedural()
        {
            TrackGenerator generator = NewGenerator();

            for (int row = 0; row < _intro.Count; row++)
            {
                IntroRow expected = _intro.Get(row);
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 0f, coinChance: 0f);

                Assert.AreEqual(expected.LaneMask, plan.LaneMask, $"Intro row {row} was not played verbatim.");
            }
        }

        /// <summary>Density zero means an empty road, whatever the table says.</summary>
        [Test]
        public void ZeroDensityProducesNoObstaclesAfterTheIntro()
        {
            TrackGenerator generator = NewGenerator();

            for (int row = 0; row < _intro.Count; row++)
            {
                generator.NextRow(row * RowSpacing, 0f, 0f);
            }

            for (int row = 0; row < 500; row++)
            {
                RowPlan plan = generator.NextRow(row * RowSpacing, density: 0f, coinChance: 0f);
                Assert.IsFalse(plan.HasObstacles, "Something spawned at zero density.");
            }
        }
    }
}
