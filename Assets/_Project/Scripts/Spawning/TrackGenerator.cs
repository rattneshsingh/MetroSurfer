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

        /// <summary>How many recent chunks are barred from repeating.</summary>
        private const int RecentMemory = 4;

        /// <summary>
        /// Metres over which the difficulty band climbs from opening to full. Deliberately short:
        /// content the player never reaches may as well not exist, and most runs end well inside
        /// a kilometre.
        /// </summary>
        private const float DifficultyRampDistance = 700f;

        /// <summary>
        /// Hardest chunk allowed at the very start. High enough that the opening already has
        /// sequences in it rather than only single obstacles.
        /// </summary>
        private const float OpeningDifficulty = 0.4f;

        /// <summary>How far below the ceiling a chunk may sit before it is beneath the player.</summary>
        private const float BandWidth = 0.45f;

        /// <summary>Rolling intensity above this forces a breather.</summary>
        private const float BreatherThreshold = 0.55f;

        /// <summary>Hardest chunk that counts as a breather.</summary>
        private const float BreatherCeiling = 0.25f;

        private readonly IntroSequence _intro;
        private readonly ObstacleTable _table;
        private readonly ObstacleCatalog _catalog;
        private readonly int _minRowGapAfterHardRow;
        private readonly int _seed;

        private System.Random _rng;
        private int _rowsGenerated;
        private int _rowsSinceHardRow;
        private int _coinLane;
        private ObstacleFormation _activeFormation;
        private int _formationRow;

        private TrackState _state;
        private float _intensity;
        private readonly ObstacleFormation[] _recent = new ObstacleFormation[RecentMemory];
        private int _recentCount;

        public int RowsGenerated => _rowsGenerated;
        public bool InFormation => _activeFormation != null;

        public TrackGenerator(
            int seed,
            IntroSequence intro,
            ObstacleTable table,
            ObstacleCatalog catalog,
            int minRowGapAfterHardRow)
        {
            _seed = seed;
            _intro = intro;
            _table = table;
            _catalog = catalog;
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

            _state = TrackState.Street;
            _intensity = 0f;
            _recentCount = 0;

            for (int i = 0; i < _recent.Length; i++)
            {
                _recent[i] = null;
            }
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

            if (_table != null && Roll() < _table.FormationChance && TryStartFormation(rowDistance))
            {
                return NextFormationRow();
            }

            return NextSingleRow(rowDistance, coinChance);
        }

        /// <summary>
        /// Chooses the next chunk and starts it.
        /// </summary>
        /// <remarks>
        /// This is where the level stops being random. Four rules do the work: the difficulty
        /// band rises with distance so early running is fair and late running is not; the entry
        /// state must match where the player actually is, so a roof run cannot begin on the road;
        /// recent chunks are barred so nothing repeats back to back; and a run of hard chunks
        /// forces a breather, because unbroken pressure reads as noise rather than as difficulty.
        /// </remarks>
        private bool TryStartFormation(float rowDistance)
        {
            float climb = Mathf.Clamp01(rowDistance / DifficultyRampDistance);
            float ceiling = Mathf.Lerp(OpeningDifficulty, 1f, climb);

            // After a hard stretch, insist on something calm. The contrast is what makes the
            // hard parts land.
            if (_intensity > BreatherThreshold)
            {
                ceiling = Mathf.Min(ceiling, BreatherCeiling);
            }

            float floor = Mathf.Max(0f, ceiling - BandWidth);

            FormationQuery query = new FormationQuery(
                rowDistance, _state, floor, ceiling, _recent, _recentCount);

            if (!_table.TryPickFormation(query, Roll(), out ObstacleFormation formation) ||
                formation.RowCount == 0)
            {
                return false;
            }

            _activeFormation = formation;
            _formationRow = 0;

            // A rolling average rather than a counter, so one brutal chunk weighs as much as
            // three middling ones.
            _intensity = Mathf.Lerp(_intensity, formation.Difficulty, 0.5f);

            if (formation.Exit != TrackState.Either)
            {
                _state = formation.Exit;
            }

            Remember(formation);
            return true;
        }

        private void Remember(ObstacleFormation formation)
        {
            for (int i = Mathf.Min(_recentCount, RecentMemory - 1); i > 0; i--)
            {
                _recent[i] = _recent[i - 1];
            }

            _recent[0] = formation;
            _recentCount = Mathf.Min(_recentCount + 1, RecentMemory);
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
            if (ClosesLane(pattern.Kind))
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

            if (mask == AllLanesMask && ClosesLane(kind))
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
            if (mask != 0 && IsLandable(kind))
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

        // Read from the catalogue rather than hardcoded, so a new obstacle declares its own
        // fairness contract alongside its geometry instead of in a switch somewhere else.
        private bool ClosesLane(ObstacleKind kind) => _catalog != null && _catalog.ClosesLane(kind);

        private bool IsLandable(ObstacleKind kind) => _catalog != null && _catalog.IsLandable(kind);

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
