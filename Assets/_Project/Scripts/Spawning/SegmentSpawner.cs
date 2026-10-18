using Runner.Config;
using Runner.Utils;
using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>
    /// Scrolls the world past a stationary player and recycles track segments, obstacles and
    /// coins out of pools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The world moves, the player does not. That keeps the player at z ≈ 0 forever, so float
    /// precision never degrades on a long run, the camera needs no follow code, and "is this
    /// behind us?" is a single comparison.
    /// </para>
    /// <para>
    /// Segments live in a fixed ring rather than an <see cref="ObjectPool{T}"/>: the active
    /// count never changes, so a free-list would always be empty. Obstacles and coins do use
    /// pools, because their active count genuinely varies row to row.
    /// </para>
    /// </remarks>
    public sealed class SegmentSpawner : MonoBehaviour
    {
        private const int LaneCount = TrackGenerator.LaneCount;

        /// <summary>The pool array is indexed by ObstacleKind, so it must span the enum.</summary>
        private const int KindCount = 11;

        [Header("Track")]
        [Tooltip("Overwritten by GameManager so the track and the player can never disagree.")]
        [SerializeField] private float _laneWidth = 2.2f;
        [Tooltip("Total track length is this times Segment Length. Must comfortably exceed the " +
                 "camera's far clip, or the player watches the world end and segments pop in.")]
        [SerializeField] private int _activeSegmentCount = 14;
        [SerializeField] private float _segmentLength = 20f;
        [SerializeField] private float _rowSpacing = 5f;

        [Tooltip("Z behind the player at which a segment is recycled to the front.")]
        [SerializeField] private float _despawnZ = -18f;

        [Tooltip("Metres of empty road ahead of the player when a run starts: no obstacles, no " +
                 "coins. Gives time to settle before the first thing to react to.")]
        [SerializeField] private float _startRunway = 25f;

        [Tooltip("Z behind the player at which ground obstacles stop being drawn, once their far " +
                 "end has passed it.")]
        [SerializeField] private float _obstacleCullZ = -1f;

        [Tooltip("Z at which overhead obstacles (bars to roll under, low bridges) stop being drawn. " +
                 "At the player, because the chase camera looks back through anything above the " +
                 "runner's head the moment they pass under it, and the bar fills the screen.")]
        [SerializeField] private float _overheadCullZ = 0f;

        [SerializeField] private float _trackSidePadding = 1.2f;

        [Header("Lane markings")]
        [Tooltip("Dashed lines between lanes, so which lane you are in can be read at a glance.")]
        [SerializeField] private Color _laneMarkingColor = new Color(0.92f, 0.92f, 0.86f);
        [SerializeField] private float _laneMarkingWidth = 0.12f;
        [SerializeField] private float _dashLength = 2.5f;
        [SerializeField] private float _dashGap = 2.5f;
        [SerializeField] private float _floorThickness = 0.5f;

        [Header("Generation")]
        [SerializeField] private int _seed = 20260817;

        [Tooltip("Authored opening that teaches move/jump/slide before random rows begin. " +
                 "Leave empty to use the built-in default.")]
        [SerializeField] private IntroSequence _introSequence;

        [Tooltip("Clear rows forced after a row that closes a lane off entirely.")]
        [SerializeField] private int _minRowGapAfterHardRow = 1;

        [SerializeField] private int _coinsPerRow = 3;
        [SerializeField] private float _coinSpacing = 1.4f;
        [SerializeField] private float _coinHeight = 0.9f;
        [SerializeField] private int _coinValue = 1;

        [Tooltip("Coins laid along a jump arc across a gap. More reads as a smoother line.")]
        [SerializeField] private int _arcCoinCount = 7;

        [Header("Obstacles")]
        [Tooltip("Every obstacle's geometry and meaning. Leave empty to use the built-in default.")]
        [SerializeField] private ObstacleCatalog _catalog;

        [Header("Drift tuning (applied by mode, not by kind)")]
        [Tooltip("How far an oscillating vehicle surges ahead and drops back, in metres.")]
        [SerializeField] private float _driftAmplitude = 2.2f;
        [SerializeField] private float _driftPeriod = 3.2f;

        [Tooltip("Closing speed of oncoming traffic, on top of the world's own scroll.")]
        [SerializeField] private float _oncomingSpeed = 14f;
        [SerializeField] private float _oncomingMaxTravel = 90f;

        [Tooltip("Speed a bus rolls at before braking. Slower than the player, so you close on " +
                 "it gradually and have time to judge the lane safe.")]
        [SerializeField] private float _busCruiseSpeed = 6f;

        [Tooltip("Distance ahead at which a bus starts braking. This is the whole fairness " +
                 "budget: it must leave time to read the brake lights and swerve.")]
        [SerializeField] private float _busBrakeTriggerZ = 45f;
        [SerializeField] private float _busBrakeDuration = 0.9f;

        [Tooltip("Brake lights. Without a tell a stopping bus is a gotcha rather than a test.")]
        [SerializeField] private Color _brakeColor = new Color(0.72f, 0.16f, 0.12f);

        [SerializeField] private Color _pitColor = new Color(0.04f, 0.04f, 0.05f);
        [SerializeField] private Vector3 _coinSize = new Vector3(0.4f, 0.4f, 0.4f);

        [Header("Palette — a StateConfig overrides these at runtime")]
        [SerializeField] private Color _groundColor = new Color(0.32f, 0.32f, 0.34f);
        [SerializeField] private Color _obstacleColor = new Color(0.16f, 0.16f, 0.18f);
        [SerializeField] private Color _coinColor = new Color(0.95f, 0.78f, 0.25f);

        [Header("Coin pool capacity (0 = size automatically from the track layout)")]
        [SerializeField] private int _coinPoolCapacity;

        [SerializeField] private Coin _coinPrefab;

        [Header("Layers")]
        [SerializeField] private string _obstacleLayerName = "Obstacle";
        [SerializeField] private string _coinLayerName = "Coin";
        [SerializeField] private string _surfaceLayerName = "Surface";

        private DifficultyCurve _difficulty;
        private ObstacleTable _obstacleTable;

        private Transform _segmentRoot;
        private Transform _poolRoot;

        private Segment[] _segments;
        private Transform[] _segmentTransforms;
        private int _head;
        private int _rowsPerSegment;

        private ObstacleFactory _obstacles;
        private CoinPlacer _coins;
        private StateConfig _stateConfig;
        private SceneryBuilder _scenery;

        /// <summary>
        /// Pits by lane in the row just placed and the one before it, so touching pits can drop
        /// the walls between them and read as one hole. Rows are generated strictly in order,
        /// so "the row before" is always the neighbour behind.
        /// </summary>
        private readonly PitHole[] _pitsThisRow = new PitHole[LaneCount];
        private readonly PitHole[] _pitsLastRow = new PitHole[LaneCount];

        private Material _groundMaterial;
        private Material _laneMarkingMaterial;

        /// <summary>
        /// Top of a lane dash. Above the road so it never z-fights it, below a pit's depth cutter
        /// (5 mm) so dashes do not float across holes.
        /// </summary>
        private const float DashTop = 0.003f;

        private int _obstacleLayer;
        private int _coinLayer;
        private int _surfaceLayer;


        private TrackGenerator _generator;
        private JumpProfile _jump;
        private System.Random _presentationRng;

        private bool _built;

        /// <summary>False after a mid-play recompile has wiped the runtime state.</summary>
        public bool IsBuilt => _built && _segments != null && _obstacles != null && _generator != null;

        /// <summary>Builds every pool and the segment ring. Call once, before the first run.</summary>
        public void Initialize(
            float laneWidth,
            DifficultyCurve difficulty,
            ObstacleTable obstacleTable,
            StateConfig state,
            JumpProfile jump)
        {
            _jump = jump;
            _laneWidth = laneWidth;
            _difficulty = difficulty;
            _obstacleTable = obstacleTable;

            if (_introSequence == null)
            {
                _introSequence = IntroSequence.CreateDefault();
            }

            if (_catalog == null)
            {
                _catalog = ObstacleCatalog.CreateDefault();
            }

            WarnIfTuningAssetsAreStale();

            _presentationRng = new System.Random(_seed);
            _generator = new TrackGenerator(_seed, _introSequence, obstacleTable, _catalog, _minRowGapAfterHardRow);

            _stateConfig = state;
            ApplyPalette(state);
            ResolveLayers();

            _rowsPerSegment = Mathf.Max(1, Mathf.RoundToInt(_segmentLength / Mathf.Max(0.01f, _rowSpacing)));
            _activeSegmentCount = Mathf.Max(2, _activeSegmentCount);

            _segmentRoot = new GameObject("~Segments").transform;
            _segmentRoot.SetParent(transform, false);

            _poolRoot = new GameObject("~Pools").transform;
            _poolRoot.SetParent(transform, false);

            _groundMaterial = PrimitiveFactory.CreateSharedMaterial(_groundColor, "M_Ground");
            _laneMarkingMaterial = PrimitiveFactory.CreateSharedMaterial(_laneMarkingColor, "M_LaneMarking");
            BuildCollaborators();

            // Optional: a state without scenery keeps the bare Phase 0 track.
            _scenery = _stateConfig != null && _stateConfig.HasScenery
                ? new SceneryBuilder(_stateConfig, FloorWidth * 0.5f, _segmentLength, _presentationRng, gameObject.layer)
                : null;

            BuildSegmentRing();

            _built = true;
            ResetRun();
        }

        /// <summary>
        /// Shouts when an asset predates the defaults in code.
        /// </summary>
        /// <remarks>
        /// An asset never adopts a new code default -- that is the point of it, and it is why
        /// tuning survives. The cost is that changing a default silently does nothing to an asset
        /// that already exists, which has cost this project several playtests spent wondering why
        /// nothing changed. Better to be told.
        /// </remarks>
        private void WarnIfTuningAssetsAreStale()
        {
            if (_obstacleTable != null &&
                _obstacleTable.DefaultsVersion != ObstacleTable.CurrentDefaultsVersion)
            {
                Debug.LogError(
                    $"ObstacleTable.asset is version {_obstacleTable.DefaultsVersion}, code expects " +
                    $"{ObstacleTable.CurrentDefaultsVersion}. It is running old chunks. " +
                    "Run Runner > Reset Tuning Assets to Defaults.", this);
            }

            if (_introSequence != null &&
                _introSequence.DefaultsVersion != IntroSequence.CurrentDefaultsVersion)
            {
                Debug.LogError(
                    $"IntroSequence.asset is version {_introSequence.DefaultsVersion}, code expects " +
                    $"{IntroSequence.CurrentDefaultsVersion}. It is running the old opening. " +
                    "Run Runner > Reset Tuning Assets to Defaults.", this);
            }
        }

        /// <summary>Rebuilds the whole track for a fresh run. No scene reload, no reallocation.</summary>
        public void ResetRun()
        {
            if (!IsBuilt)
            {
                return;
            }

            _generator.Reset();
            _presentationRng = new System.Random(_seed);
            _head = 0;

            // A new run has no row before its first one.
            System.Array.Clear(_pitsLastRow, 0, _pitsLastRow.Length);

            // One segment starts behind the player so the track does not visibly begin at their feet.
            float z = -_segmentLength;

            for (int i = 0; i < _segments.Length; i++)
            {
                Segment segment = _segments[i];
                ReleaseContents(segment);

                _segmentTransforms[i].localPosition = new Vector3(0f, 0f, z);

                // Rows short of the runway stay empty — including the segment behind the player,
                // which used to put coins under their feet and spend the authored intro where
                // nobody could see it.
                PopulateSegment(segment, Mathf.Max(0f, z), _startRunway - z);
                z += _segmentLength;
            }
        }

        /// <summary>
        /// Advances the world. Called from GameManager's Update so there is exactly one place
        /// that decides whether the run is ticking.
        /// </summary>
        public void Tick(float deltaTime, float speed, float distance)
        {
            if (!IsBuilt)
            {
                return;
            }

            float step = speed * deltaTime;

            // Indexed loop over a cached Transform array — no foreach enumerator, no repeated
            // .transform property calls, both of which show up at this frequency.
            for (int i = 0; i < _segmentTransforms.Length; i++)
            {
                Transform t = _segmentTransforms[i];
                Vector3 position = t.localPosition;
                position.z -= step;
                t.localPosition = position;
            }

            _obstacles.TickDrift(deltaTime);

            // Game time, so the coins freeze with everything else on pause.
            _coins.Spin(Time.time);
            CullPassedObstacles();

            // At most one segment passes the despawn line per frame at any sane speed, but loop
            // anyway so a huge delta cannot leave a gap in the track.
            for (int guard = 0; guard < _segments.Length; guard++)
            {
                Transform headTransform = _segmentTransforms[_head];
                if (headTransform.localPosition.z + _segmentLength >= _despawnZ)
                {
                    break;
                }

                RecycleHead(distance);
            }
        }

        private void CullPassedObstacles()
        {
            for (int i = 0; i < _segments.Length; i++)
            {
                float segmentZ = _segmentTransforms[i].localPosition.z;

                // Only segments straddling either cull line can have something to hide.
                float nearestLine = Mathf.Min(_obstacleCullZ, _overheadCullZ);
                float furthestLine = Mathf.Max(_obstacleCullZ, _overheadCullZ);
                if (segmentZ > furthestLine || segmentZ + _segmentLength < nearestLine)
                {
                    continue;
                }

                _segments[i].HideObstaclesBehind(_obstacleCullZ - segmentZ, _overheadCullZ - segmentZ);
            }
        }

        private void RecycleHead(float distance)
        {
            Segment segment = _segments[_head];
            ReleaseContents(segment);

            // Placement is measured off the segment currently at the front, never off a running
            // counter. A counter does not scroll with the world, so it drifts ahead of the real
            // track edge by exactly the distance travelled and opens a widening hole. Reading
            // the front segment's actual position cannot drift.
            int frontIndex = (_head + _segments.Length - 1) % _segments.Length;
            float z = _segmentTransforms[frontIndex].localPosition.z + _segmentLength;

            _segmentTransforms[_head].localPosition = new Vector3(0f, 0f, z);

            // The run distance at which the player will actually reach this segment — that is
            // what difficulty should ramp against, not the distance already covered.
            PopulateSegment(segment, distance + z);

            _head = (_head + 1) % _segments.Length;
        }

        /// <param name="clearBeforeLocalZ">
        /// Rows nearer the segment start than this are left empty and do not consume generator
        /// rows, so the intro begins where the runway ends. Zero or less fills every row.
        /// </param>
        private void PopulateSegment(Segment segment, float runDistanceAtSegmentStart, float clearBeforeLocalZ = 0f)
        {
            float density = _difficulty.EvaluateObstacleDensity(runDistanceAtSegmentStart);
            float coinChance = _difficulty.EvaluateCoinChance(runDistanceAtSegmentStart);
            float speed = _difficulty.EvaluateSpeed(runDistanceAtSegmentStart);

            for (int row = 0; row < _rowsPerSegment; row++)
            {
                float localZ = row * _rowSpacing + _rowSpacing * 0.5f;
                if (localZ < clearBeforeLocalZ)
                {
                    continue;
                }

                float rowDistance = runDistanceAtSegmentStart + localZ;

                RowPlan plan = _generator.NextRow(rowDistance, density, coinChance);
                Realise(segment, plan, localZ, speed);
            }
        }

        /// <summary>
        /// Turns a decision into objects. Everything above this line is testable without a
        /// scene; everything below it needs one.
        /// </summary>
        private void Realise(Segment segment, RowPlan plan, float localZ, float speed)
        {
            System.Array.Clear(_pitsThisRow, 0, _pitsThisRow.Length);

            if (plan.HasObstacles)
            {
                PlaceRow(segment, plan.LaneMask, plan.Kind, localZ);
            }

            StitchPits();

            if (!plan.HasCoins)
            {
                return;
            }

            if (plan.CoinArc)
            {
                _coins.EmitArc(segment, plan.CoinLane, localZ, _catalog.RoofHeight(plan.Kind), speed, _jump);
                return;
            }

            float height = plan.CoinsOnRoof
                ? _catalog.RoofHeight(plan.Kind) + _coinHeight
                : _coinHeight;

            _coins.EmitTrail(segment, plan.CoinLane, localZ, height);
        }

        private void PlaceRow(Segment segment, int mask, ObstacleKind kind, float localZ)
        {
            for (int lane = 0; lane < TrackGenerator.LaneCount; lane++)
            {
                if ((mask & (1 << lane)) != 0)
                {
                    PlaceObstacle(segment, lane, kind, localZ);
                }
            }
        }


        private void PlaceObstacle(Segment segment, int lane, ObstacleKind kind, float localZ)
        {
            if (!segment.HasObstacleRoom)
            {
                return;
            }

            Obstacle obstacle = _obstacles.Get(kind, out int handle);
            if (obstacle == null)
            {
                return;
            }

            Transform t = obstacle.transform;
            t.SetParent(segment.Root, false);
            t.localPosition = new Vector3(LaneToX(lane), 0f, localZ);

            segment.AddObstacle(obstacle, handle, kind, localZ);

            PitHole pit = obstacle.Pit;
            if (pit != null)
            {
                // Fresh from the pool: full cell, every wall, before neighbours open it up.
                pit.Fit(_laneWidth, _rowSpacing);
                pit.ResetWalls();
                _pitsThisRow[lane] = pit;
            }
        }

        /// <summary>
        /// Opens the walls between touching pits: side by side in this row, and front to back
        /// with the row before. Then this row becomes "the row before" for the next one.
        /// </summary>
        private void StitchPits()
        {
            for (int lane = 0; lane < LaneCount; lane++)
            {
                PitHole pit = _pitsThisRow[lane];
                if (pit == null)
                {
                    continue;
                }

                if (lane > 0 && _pitsThisRow[lane - 1] != null)
                {
                    pit.OpenLeft();
                    _pitsThisRow[lane - 1].OpenRight();
                }

                if (_pitsLastRow[lane] != null)
                {
                    _pitsLastRow[lane].OpenFar();
                }
            }

            System.Array.Copy(_pitsThisRow, _pitsLastRow, LaneCount);
        }

        private void ReleaseContents(Segment segment)
        {
            for (int i = 0; i < segment.ObstacleCount; i++)
            {
                _obstacles.Release(segment.GetObstacleKind(i), segment.GetObstacleHandle(i));
            }

            for (int i = 0; i < segment.CoinCount; i++)
            {
                _coins.Release(segment.GetCoinHandle(i));
            }

            segment.ClearContents();
        }


        private float LaneToX(int lane) => (lane - (LaneCount - 1) * 0.5f) * _laneWidth;

        /// <summary>
        /// Presentation randomness only — drift phase, nothing the generator depends on. The
        /// null guard is a safety net for a script recompile during Play mode, which wipes
        /// private non-serialized fields without re-running Awake.
        /// </summary>
        private float Roll()
        {
            if (_presentationRng == null)
            {
                _presentationRng = new System.Random(_seed);
            }

            return (float)_presentationRng.NextDouble();
        }

        private void OnDestroy()
        {
            // Materials are created with `new Material(...)` at warm-up, so nothing else owns
            // them. Without this Unity reports leaked material instances on scene unload.
            ObjectUtility.Destroy(_groundMaterial);
            ObjectUtility.Destroy(_laneMarkingMaterial);
            _obstacles?.Dispose();
            _coins?.Dispose();
        }

        private void ApplyPalette(StateConfig state)
        {
            // The one place a state's data touches gameplay presentation. Nothing here branches
            // on which state it is — it just reads fields.
            if (state == null)
            {
                return;
            }

            _groundColor = state.GroundColor;
            _obstacleColor = state.ObstacleColor;
            _coinColor = state.CoinColor;
        }

        private void ResolveLayers()
        {
            _obstacleLayer = LayerMask.NameToLayer(_obstacleLayerName);
            _coinLayer = LayerMask.NameToLayer(_coinLayerName);
            _surfaceLayer = LayerMask.NameToLayer(_surfaceLayerName);

            if (_surfaceLayer < 0)
            {
                Debug.LogError($"[{nameof(SegmentSpawner)}] Missing layer '{_surfaceLayerName}'. " +
                               "Re-run Runner > Build Phase 0 Scene, or add it by hand.", this);
                _surfaceLayer = 0;
            }

            if (_obstacleLayer < 0 || _coinLayer < 0)
            {
                Debug.LogError(
                    $"[{nameof(SegmentSpawner)}] Missing layers. Add '{_obstacleLayerName}' and " +
                    $"'{_coinLayerName}' under Project Settings > Tags and Layers.", this);

                _obstacleLayer = Mathf.Max(0, _obstacleLayer);
                _coinLayer = Mathf.Max(0, _coinLayer);
            }
        }

        /// <summary>
        /// Hands the factory and the coin placer everything they need. The spawner keeps only
        /// what it uses itself: the ground material and the segment ring.
        /// </summary>
        private void BuildCollaborators()
        {
            int autoCapacity = _activeSegmentCount * _rowsPerSegment;

            _obstacles = new ObstacleFactory(
                _catalog,
                _poolRoot,
                new ObstacleFactorySettings(
                    _obstacleLayer, _surfaceLayer, autoCapacity,
                    _obstacleColor, _pitColor, _brakeColor,
                    _driftAmplitude, _driftPeriod,
                    _oncomingSpeed, _oncomingMaxTravel,
                    _busCruiseSpeed, _busBrakeTriggerZ, _busBrakeDuration),
                Roll,
                _stateConfig);

            int coinCapacity = _coinPoolCapacity > 0
                ? _coinPoolCapacity
                : _activeSegmentCount * _rowsPerSegment * _coinsPerRow;

            _coins = new CoinPlacer(
                _poolRoot,
                new CoinPlacerSettings(
                    _coinLayer, coinCapacity, _coinColor, _coinSize,
                    _coinSpacing, _coinHeight, _coinsPerRow, _coinValue,
                    _arcCoinCount, _laneWidth),
                _coinPrefab);
        }

        private void BuildSegmentRing()
        {
            _segments = new Segment[_activeSegmentCount];
            _segmentTransforms = new Transform[_activeSegmentCount];

            int maxObstacles = _rowsPerSegment * LaneCount;
            int maxCoins = _rowsPerSegment * _coinsPerRow;

            for (int i = 0; i < _activeSegmentCount; i++)
            {
                Segment segment = CreateSegment(i);
                segment.Initialize(maxObstacles, maxCoins);

                _segments[i] = segment;
                _segmentTransforms[i] = segment.Root;
            }
        }

        private Segment CreateSegment(int index)
        {
            GameObject root = PrimitiveFactory.CreateKinematicRoot($"Segment_{index}");
            root.transform.SetParent(_segmentRoot, false);

            GameObject floor = PrimitiveFactory.CreateCube(
                "Floor",
                new Vector3(FloorWidth, _floorThickness, _segmentLength),
                _groundMaterial,
                root.layer,
                PrimitiveFactory.ColliderMode.None,
                castShadows: false);

            // No collider on the floor at all — the player's vertical motion is kinematic and
            // clamped at y = 0, so a ground collider would only cost PhysX work for nothing.
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -_floorThickness * 0.5f, _segmentLength * 0.5f);

            AddLaneMarkings(root.transform);
            _scenery?.Dress(root.transform);

            return root.AddComponent<Segment>();
        }

        /// <summary>
        /// Dashes on each line between lanes, built once per segment at warm-up. They scroll and
        /// recycle with it, so they cost nothing per frame. Visual only: no colliders.
        /// </summary>
        private void AddLaneMarkings(Transform segment)
        {
            float pitch = _dashLength + _dashGap;
            if (_laneMarkingMaterial == null || _dashLength <= 0f || pitch <= 0f)
            {
                return;
            }

            for (int lane = 0; lane < LaneCount - 1; lane++)
            {
                float x = LaneToX(lane) + _laneWidth * 0.5f;

                for (float z = 0f; z + _dashLength <= _segmentLength + 0.001f; z += pitch)
                {
                    GameObject dash = PrimitiveFactory.CreateCube(
                        "LaneDash", new Vector3(_laneMarkingWidth, DashTop, _dashLength), _laneMarkingMaterial,
                        segment.gameObject.layer, PrimitiveFactory.ColliderMode.None, castShadows: false);

                    dash.transform.SetParent(segment, false);
                    dash.transform.localPosition = new Vector3(x, DashTop * 0.5f, z + _dashLength * 0.5f);
                }
            }
        }

        /// <summary>Road surface width: the lanes plus padding on each side.</summary>
        private float FloorWidth => _laneWidth * LaneCount + _trackSidePadding * 2f;

    }
}
