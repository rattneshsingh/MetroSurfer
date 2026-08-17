using Runner.Config;
using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>
    /// Decides what goes in each row: the authored opening, then formations and weighted
    /// single rows. Deliberately free of GameObjects, Transforms and pools.
    /// </summary>
    /// <remarks>
    /// Splitting the decision from the spawning is what makes the interesting invariants
    /// testable — that every row is survivable, that obstacles never stop appearing, that a
    /// formation always runs to completion. Those are cheap to assert over ten thousand rows
    /// here, and effectively impossible to assert against a MonoBehaviour that needs a scene,
    /// seven pools and a warmed-up ring of segments.
    /// </remarks>
    public sealed class TrackGenerator
    {
        public const int LaneCount = 3;
        public const int AllLanesMask = 0b111;

        /// <summary>Saturation point for the row-gap counter. Prevents int overflow on a long run.</summary>
        private const int RowGapCeiling = 1000;

        private readonly IntroSequence _intro;
        private readonly ObstacleTable _table;
        private readonly int _minRowGapAfterHardRow;
        private readonly int _seed;

        private System.Random _rng;
        private int _rowsGenerated;
        private int _rowsSinceHardRow;
        private int _coinLane;
        private ObstacleFormation _activeFormation;
        private int _formationRow;

        public int RowsGenerated => _rowsGenerated;
        public bool InFormation => _activeFormation != null;

        public TrackGenerator(int seed, IntroSequence intro, ObstacleTable table, int minRowGapAfterHardRow)
        {
            _seed = seed;
            _intro = intro;
            _table = table;
            _minRowGapAfterHardRow = Mathf.Max(0, minRowGapAfterHardRow);

            Reset();
        }

        public void Reset()
        {
            _rng = new System.Random(_seed);
            _rowsGenerated = 0;
            _rowsSinceHardRow = RowGapCeiling;
            _coinLane = LaneCount / 2;
            _activeFormation = null;
            _formationRow = 0;
        }

        public RowPlan NextRow(float rowDistance, float density, float coinChance)
        {
            RowPlan plan = _rowsGenerated < IntroRowCount
                ? NextIntroRow()
                : NextProceduralRow(rowDistance, density, coinChance);

            _rowsGenerated++;
            return plan;
        }

        private int IntroRowCount => _intro == null ? 0 : _intro.Count;

        private RowPlan NextIntroRow()
        {
            IntroRow row = _intro.Get(_rowsGenerated);

            // The scripted opening paces itself, so procedural generation starts with a clean
            // breather counter the moment it takes over.
            _rowsSinceHardRow = RowGapCeiling;

            return Build(Sanitise(row.LaneMask, row.Kind), row.Kind, row.Coins, row.CoinArc);
        }

        private RowPlan NextProceduralRow(float rowDistance, float density, float coinChance)
        {
            // A formation in progress owns the row outright. Its ordering is the entire point,
            // so nothing is allowed to interleave with it.
            if (_activeFormation != null)
            {
                return NextFormationRow();
            }

            if (_rowsSinceHardRow < _minRowGapAfterHardRow || Roll() >= density)
            {
                // Every row advances the gap counter, including skipped ones. Otherwise a forced
                // breather never expires and obstacles stop appearing for the rest of the run.
                BumpRowGap();
                return Build(0, ObstacleKind.Low, Roll() < coinChance);
            }

            if (_table != null &&
                Roll() < _table.FormationChance &&
                _table.TryPickFormation(rowDistance, Roll(), out ObstacleFormation formation) &&
                formation.RowCount > 0)
            {
                _activeFormation = formation;
                _formationRow = 0;
                return NextFormationRow();
            }

            return NextSingleRow(rowDistance, coinChance);
        }

        private RowPlan NextFormationRow()
        {
            IntroRow row = _activeFormation.Rows[_formationRow];
            int mask = Sanitise(row.LaneMask, row.Kind);

            _formationRow++;
            if (_formationRow >= _activeFormation.RowCount)
            {
                _activeFormation = null;
                _formationRow = 0;
            }

            _rowsSinceHardRow = RowGapCeiling;

            return Build(mask, row.Kind, row.Coins, row.CoinArc);
        }

        private RowPlan NextSingleRow(float rowDistance, float coinChance)
        {
            if (_table == null || !_table.TryPick(rowDistance, Roll(), out ObstaclePattern pattern))
            {
                BumpRowGap();
                return Build(0, ObstacleKind.Low, Roll() < coinChance);
            }

            int mask = Sanitise(pattern.LaneMask & AllLanesMask, pattern.Kind);

            if (mask == 0)
            {
                BumpRowGap();
                return Build(0, ObstacleKind.Low, Roll() < coinChance);
            }

            // Only a row that forces a lane change earns a breather after it.
            if (ObstacleKindRules.ClosesLane(pattern.Kind))
            {
                _rowsSinceHardRow = 0;
            }
            else
            {
                BumpRowGap();
            }

            return Build(mask, pattern.Kind, Roll() < coinChance);
        }

        /// <summary>
        /// The hard fairness rule, applied to authored and generated rows alike: a kind that
        /// cannot be beaten in place may never occupy every lane. Enforced here rather than
        /// trusted to the data, because a table someone edits later must not be able to ship an
        /// impossible run.
        /// </summary>
        private int Sanitise(int mask, ObstacleKind kind)
        {
            mask &= AllLanesMask;

            if (mask == AllLanesMask && ObstacleKindRules.ClosesLane(kind))
            {
                mask &= ~(1 << _rng.Next(0, LaneCount));
            }

            return mask;
        }

        private RowPlan Build(int mask, ObstacleKind kind, bool wantCoins, bool arc = false)
        {
            if (!wantCoins && !arc)
            {
                return new RowPlan(mask, kind, RowPlan.NoCoinLane, false);
            }

            // An arc spans a gap, so it stays in whichever lane the trail is already following —
            // the jump is a continuation of that line, not a new one.
            if (arc)
            {
                return new RowPlan(mask, kind, _coinLane, false, true);
            }

            // On a landable row the coins belong on the roofs. Points have to be where the risk
            // is, or staying on the street is always the better play.
            if (mask != 0 && ObstacleKindRules.IsLandable(kind))
            {
                int roofLane = PickLane(mask, occupied: true);
                if (roofLane >= 0)
                {
                    _coinLane = roofLane;
                    return new RowPlan(mask, kind, roofLane, true);
                }
            }

            int freeLane = PickLane(mask, occupied: false);
            if (freeLane < 0)
            {
                return new RowPlan(mask, kind, RowPlan.NoCoinLane, false);
            }

            _coinLane = freeLane;
            return new RowPlan(mask, kind, freeLane, false);
        }

        /// <summary>
        /// Deterministic, and biased to continuity then to the centre. Randomising this made
        /// coin trails read as scatter rather than as a path worth following.
        /// </summary>
        private int PickLane(int mask, bool occupied)
        {
            if (IsLaneOccupied(mask, _coinLane) == occupied)
            {
                return _coinLane;
            }

            int centre = LaneCount / 2;
            if (IsLaneOccupied(mask, centre) == occupied)
            {
                return centre;
            }

            for (int lane = 0; lane < LaneCount; lane++)
            {
                if (IsLaneOccupied(mask, lane) == occupied)
                {
                    return lane;
                }
            }

            return -1;
        }

        private static bool IsLaneOccupied(int mask, int lane) => (mask & (1 << lane)) != 0;

        private void BumpRowGap()
        {
            if (_rowsSinceHardRow < RowGapCeiling)
            {
                _rowsSinceHardRow++;
            }
        }

        private float Roll() => (float)_rng.NextDouble();
    }
}
