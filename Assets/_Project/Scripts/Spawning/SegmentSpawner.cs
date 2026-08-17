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

        [Tooltip("Z behind the player at which obstacles stop being drawn. Keeps a bar you just " +
                 "rolled under from filling the camera and hiding what is coming next.")]
        [SerializeField] private float _obstacleCullZ = -1f;

        [SerializeField] private float _trackSidePadding = 1.2f;
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

        [Header("Grey-cube geometry (Phase 0 stand-ins)")]
        [SerializeField] private Vector3 _lowObstacleSize = new Vector3(1.6f, 0.5f, 0.7f);
        [SerializeField] private Vector3 _highObstacleSize = new Vector3(1.6f, 1.45f, 0.7f);

        [Tooltip("Centre height of a slide-under barrier. Must sit above the sliding hitbox and below the standing one.")]
        [SerializeField] private float _highObstacleCenterY = 1.275f;

        [SerializeField] private Vector3 _fullObstacleSize = new Vector3(1.6f, 2.2f, 0.7f);
        [SerializeField] private Vector3 _coinSize = new Vector3(0.4f, 0.4f, 0.4f);

        [Header("The traffic stack")]
        [Tooltip("Tier one. Roof is landable; everything below the inset is lethal.")]
        [SerializeField] private Vector3 _truckSize = new Vector3(1.8f, 1.2f, 4.6f);

        [Tooltip("Tier two. Out of reach from the street — you get up here off a truck roof.")]
        [SerializeField] private Vector3 _trainSize = new Vector3(1.8f, 2.4f, 4.5f);

        [Tooltip("How far below the roof the lethal volume stops. This gap is what makes " +
                 "landing on top survivable while hitting the side is not.")]
        [SerializeField] private float _roofSafeInset = 0.25f;

        [SerializeField] private float _rampWidth = 1.8f;
        [SerializeField] private float _rampRun = 3f;
        [SerializeField] private float _rampRise = 1.2f;
        [SerializeField] private float _rampThickness = 0.25f;

        [Header("Moving traffic")]
        [Tooltip("How far a drifting truck surges ahead and drops back, in metres.")]
        [SerializeField] private float _movingTruckAmplitude = 2.2f;

        [Tooltip("Seconds for one full surge-and-drop cycle.")]
        [SerializeField] private float _movingTruckPeriod = 3.2f;

        [Tooltip("Closing speed of oncoming traffic, on top of the world's own scroll.")]
        [SerializeField] private float _oncomingSpeed = 14f;
        [SerializeField] private float _oncomingMaxTravel = 90f;

        [Header("Stopping bus")]
        [SerializeField] private Vector3 _busSize = new Vector3(1.8f, 1.4f, 4.6f);

        [Tooltip("Speed it rolls along at before braking. Slower than the player, so you close " +
                 "on it gradually and have time to judge the lane safe.")]
        [SerializeField] private float _busCruiseSpeed = 6f;

        [Tooltip("Distance ahead of the player at which it starts braking. This is the whole " +
                 "fairness budget — it must leave time to read the brake lights and swerve.")]
        [SerializeField] private float _busBrakeTriggerZ = 45f;

        [SerializeField] private float _busBrakeDuration = 0.9f;

        [Tooltip("Brake lights. Without a tell this obstacle is a gotcha rather than a test.")]
        [SerializeField] private Color _brakeColor = new Color(0.72f, 0.16f, 0.12f);

        [Header("Pits")]
        [SerializeField] private Vector3 _pitSize = new Vector3(2f, 0.08f, 4.6f);

        [Tooltip("Height of the kill volume over a pit. Must catch a runner and a roll, and " +
                 "must sit below the jump arc.")]
        [SerializeField] private float _pitTriggerHeight = 0.6f;

        [SerializeField] private Color _pitColor = new Color(0.04f, 0.04f, 0.05f);

        [Header("Underpass")]
        [Tooltip("Height of the gap you run through. Must clear a standing runner on the street " +
                 "and catch one standing on a truck roof.")]
        [SerializeField] private float _underpassClearance = 1.05f;

        [Tooltip("Solid height above the gap. Tall enough that nothing can jump over it, " +
                 "including from a train roof.")]
        [SerializeField] private float _underpassHeight = 3.5f;

        [Tooltip("Spans the whole road, so it is placed as one object in the centre lane.")]
        [SerializeField] private float _underpassWidth = 8f;
        [SerializeField] private float _underpassDepth = 1.2f;

        [Header("Palette — a StateConfig overrides these at runtime")]
        [SerializeField] private Color _groundColor = new Color(0.32f, 0.32f, 0.34f);
        [SerializeField] private Color _obstacleColor = new Color(0.16f, 0.16f, 0.18f);
        [SerializeField] private Color _coinColor = new Color(0.95f, 0.78f, 0.25f);

        [Header("Pool capacities (0 = size automatically from the track layout)")]
        [SerializeField] private int _obstaclePoolCapacityPerKind;
        [SerializeField] private int _coinPoolCapacity;

        [Header("Optional authored prefabs — leave empty to use generated cubes")]
        [SerializeField] private Obstacle _lowObstaclePrefab;
        [SerializeField] private Obstacle _highObstaclePrefab;
        [SerializeField] private Obstacle _fullObstaclePrefab;
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

        private ObjectPool<Obstacle> _lowPool;
        private ObjectPool<Obstacle> _highPool;
        private ObjectPool<Obstacle> _fullPool;
        private ObjectPool<Obstacle> _rampPool;
        private ObjectPool<Obstacle> _truckPool;
        private ObjectPool<Obstacle> _trainPool;
        private ObjectPool<Obstacle> _movingTruckPool;
        private ObjectPool<Obstacle> _oncomingTruckPool;
        private ObjectPool<Obstacle> _pitPool;
        private ObjectPool<Obstacle> _underpassPool;
        private ObjectPool<Obstacle> _busPool;
        private ObjectPool<Coin> _coinPool;

        private Material _groundMaterial;
        private Material _obstacleMaterial;
        private Material _coinMaterial;
        private Material _pitMaterial;
        private Material _brakeMaterial;

        private int _obstacleLayer;
        private int _coinLayer;
        private int _surfaceLayer;


        private TrackGenerator _generator;
        private JumpProfile _jump;
        private System.Random _presentationRng;

        private bool _built;

        /// <summary>False after a mid-play recompile has wiped the runtime state.</summary>
        public bool IsBuilt => _built && _segments != null && _lowPool != null && _generator != null;

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

            _presentationRng = new System.Random(_seed);
            _generator = new TrackGenerator(_seed, _introSequence, obstacleTable, _minRowGapAfterHardRow);

            ApplyPalette(state);
            ResolveLayers();

            _rowsPerSegment = Mathf.Max(1, Mathf.RoundToInt(_segmentLength / Mathf.Max(0.01f, _rowSpacing)));
            _activeSegmentCount = Mathf.Max(2, _activeSegmentCount);

            _segmentRoot = new GameObject("~Segments").transform;
            _segmentRoot.SetParent(transform, false);

            _poolRoot = new GameObject("~Pools").transform;
            _poolRoot.SetParent(transform, false);

            BuildMaterials();
            BuildPools();
            BuildSegmentRing();

            _built = true;
            ResetRun();
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

            // One segment starts behind the player so the track does not visibly begin at their feet.
            float z = -_segmentLength;

            for (int i = 0; i < _segments.Length; i++)
            {
                Segment segment = _segments[i];
                ReleaseContents(segment);

                _segmentTransforms[i].localPosition = new Vector3(0f, 0f, z);
                PopulateSegment(segment, Mathf.Max(0f, z));
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

            TickMovingObstacles(deltaTime);
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

        /// <summary>
        /// Only the segment straddling the cull line can hold anything newly passed: segments
        /// ahead have nothing behind the player yet, and segments fully past it were already
        /// dealt with on an earlier frame. So this is one segment's worth of work, not fourteen.
        /// </summary>
        /// <summary>
        /// Sweeps the moving-truck pool once per frame. Capacity is small and the check is two
        /// array reads, so this costs less than giving every truck its own Update.
        /// </summary>
        private void TickMovingObstacles(float deltaTime)
        {

            TickDriftPool(_movingTruckPool, deltaTime, null);
            TickDriftPool(_oncomingTruckPool, deltaTime, null);
            TickDriftPool(_busPool, deltaTime, _brakeMaterial);
        }

        private static void TickDriftPool(ObjectPool<Obstacle> pool, float deltaTime, Material alertMaterial)
        {
            if (pool == null)
            {
                return;
            }

            for (int i = 0; i < pool.Capacity; i++)
            {
                if (!pool.TryGetActive(i, out Obstacle obstacle) || obstacle.Drift == null)
                {
                    continue;
                }

                obstacle.Drift.Tick(deltaTime);

                // Consumed rather than polled, so the swap costs one call on one frame.
                if (alertMaterial != null && obstacle.Drift.ConsumeBrakeStarted())
                {
                    obstacle.SetMaterial(alertMaterial);
                }
            }
        }

        private void CullPassedObstacles()
        {
            for (int i = 0; i < _segments.Length; i++)
            {
                float segmentZ = _segmentTransforms[i].localPosition.z;

                if (segmentZ > _obstacleCullZ || segmentZ + _segmentLength < _obstacleCullZ)
                {
                    continue;
                }

                _segments[i].HideObstaclesBehind(_obstacleCullZ - segmentZ);
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

        private void PopulateSegment(Segment segment, float runDistanceAtSegmentStart)
        {
            float density = _difficulty.EvaluateObstacleDensity(runDistanceAtSegmentStart);
            float coinChance = _difficulty.EvaluateCoinChance(runDistanceAtSegmentStart);
            float speed = _difficulty.EvaluateSpeed(runDistanceAtSegmentStart);

            for (int row = 0; row < _rowsPerSegment; row++)
            {
                float localZ = row * _rowSpacing + _rowSpacing * 0.5f;
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
            if (plan.HasObstacles)
            {
                PlaceRow(segment, plan.LaneMask, plan.Kind, localZ);
            }

            if (!plan.HasCoins)
            {
                return;
            }

            if (plan.CoinArc)
            {
                EmitCoinArc(segment, plan.CoinLane, localZ, RoofHeightFor(plan.Kind), speed);
                return;
            }

            float height = plan.CoinsOnRoof
                ? RoofHeightFor(plan.Kind) + _coinHeight
                : _coinHeight;

            EmitCoinTrail(segment, plan.CoinLane, localZ, height);
        }

        /// <summary>
        /// Lays coins along the exact parabola of a well-timed jump across this gap.
        /// </summary>
        /// <remarks>
        /// The arc is computed, never authored, because its shape depends on run speed: the same
        /// jump covers about 6.5m at the starting pace and roughly 16m at full tilt, so a drawn
        /// arc would be wrong everywhere except one speed. Deriving it from the live jump profile
        /// and the predicted speed also means the line is honest — if the coins say the jump is
        /// makeable, it is.
        /// </remarks>
        private void EmitCoinArc(Segment segment, int lane, float localZ, float launchHeight, float speed)
        {
            if (!_jump.IsValid || _arcCoinCount < 2)
            {
                EmitCoinTrail(segment, lane, localZ, launchHeight + _coinHeight);
                return;
            }

            float airTime = _jump.AirTime;
            float span = speed * airTime;
            float x = LaneToX(lane);
            float startZ = localZ - span * 0.5f;

            for (int i = 0; i < _arcCoinCount; i++)
            {
                if (!segment.HasCoinRoom)
                {
                    return;
                }

                float t = airTime * i / (_arcCoinCount - 1);

                Coin coin = _coinPool.Get(out int handle);

                Transform transformCache = coin.transform;
                transformCache.SetParent(segment.Root, false);
                transformCache.localPosition = new Vector3(
                    x,
                    launchHeight + _coinHeight + _jump.HeightAt(t),
                    startZ + speed * t);

                segment.AddCoin(coin, handle);
            }
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

            ObjectPool<Obstacle> pool = PoolFor(kind);
            Obstacle obstacle = pool.Get(out int handle);

            Transform t = obstacle.transform;
            t.SetParent(segment.Root, false);
            t.localPosition = new Vector3(LaneToX(lane), CenterHeightFor(kind), localZ);

            if (obstacle.Drift != null)
            {
                if (kind == ObstacleKind.OncomingTruck)
                {
                    obstacle.Drift.BeginApproach(_oncomingSpeed, _oncomingMaxTravel);
                }
                else if (kind == ObstacleKind.StoppingBus)
                {
                    obstacle.Drift.BeginBraking(_busCruiseSpeed, _busBrakeTriggerZ, _busBrakeDuration);
                }
                else
                {
                    // Phase varies per placement so a convoy ripples rather than moving as a block.
                    obstacle.Drift.BeginOscillation(_movingTruckAmplitude, _movingTruckPeriod, Roll() * Mathf.PI * 2f);
                }
            }

            segment.AddObstacle(obstacle, handle, kind, localZ);
        }

        private void EmitCoinTrail(Segment segment, int lane, float localZ, float height)
        {
            float x = LaneToX(lane);
            float startZ = localZ - (_coinsPerRow - 1) * _coinSpacing * 0.5f;

            for (int i = 0; i < _coinsPerRow; i++)
            {
                if (!segment.HasCoinRoom)
                {
                    return;
                }

                Coin coin = _coinPool.Get(out int handle);

                Transform t = coin.transform;
                t.SetParent(segment.Root, false);
                t.localPosition = new Vector3(x, height, startZ + i * _coinSpacing);

                segment.AddCoin(coin, handle);
            }
        }


        /// <summary>Standable height of a piece, or zero if you cannot stand on it.</summary>
        private float RoofHeightFor(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.Truck:
                case ObstacleKind.MovingTruck:
                case ObstacleKind.OncomingTruck: return _truckSize.y;
                case ObstacleKind.StoppingBus: return _busSize.y;
                case ObstacleKind.Train: return _trainSize.y;
                default: return 0f;
            }
        }


        private void ReleaseContents(Segment segment)
        {
            // Deliberately one operation per line. Packing these into a single expression made
            // a NullReferenceException here impossible to localise, because the accessors are
            // one-liners the JIT inlines away and the stack trace collapses onto the caller.
            if (segment == null)
            {
                Debug.LogError($"[{nameof(SegmentSpawner)}] ReleaseContents got a null segment.", this);
                return;
            }

            int obstacleCount = segment.ObstacleCount;
            for (int i = 0; i < obstacleCount; i++)
            {
                ObstacleKind kind = segment.GetObstacleKind(i);
                int handle = segment.GetObstacleHandle(i);
                ObjectPool<Obstacle> pool = PoolFor(kind);

                if (pool == null)
                {
                    Debug.LogError($"[{nameof(SegmentSpawner)}] No pool for kind '{kind}' " +
                                   $"(index {i} of {obstacleCount}, handle {handle}).", this);
                    continue;
                }

                pool.Release(handle);
            }

            int coinCount = segment.CoinCount;
            if (coinCount > 0 && _coinPool == null)
            {
                Debug.LogError($"[{nameof(SegmentSpawner)}] Coin pool is null with {coinCount} coins attached.", this);
            }
            else
            {
                for (int i = 0; i < coinCount; i++)
                {
                    _coinPool.Release(segment.GetCoinHandle(i));
                }
            }

            segment.ClearContents();
        }

        private ObjectPool<Obstacle> PoolFor(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.High: return _highPool;
                case ObstacleKind.Full: return _fullPool;
                case ObstacleKind.Ramp: return _rampPool;
                case ObstacleKind.Truck: return _truckPool;
                case ObstacleKind.Train: return _trainPool;
                case ObstacleKind.MovingTruck: return _movingTruckPool;
                case ObstacleKind.OncomingTruck: return _oncomingTruckPool;
                case ObstacleKind.Pit: return _pitPool;
                case ObstacleKind.Underpass: return _underpassPool;
                case ObstacleKind.StoppingBus: return _busPool;
                default: return _lowPool;
            }
        }

        private float CenterHeightFor(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.High: return _highObstacleCenterY;
                case ObstacleKind.Full: return _fullObstacleSize.y * 0.5f;

                // Ramps, trucks and trains are built upward from a root at street level, so
                // their pivot is the ground rather than their centre.
                case ObstacleKind.Ramp:
                case ObstacleKind.Truck:
                case ObstacleKind.Train:
                case ObstacleKind.MovingTruck:
                case ObstacleKind.OncomingTruck:
                case ObstacleKind.StoppingBus:
                case ObstacleKind.Pit: return 0f;

                // Centre of the solid part, which starts at the top of the gap.
                case ObstacleKind.Underpass: return _underpassClearance + _underpassHeight * 0.5f;

                default: return _lowObstacleSize.y * 0.5f;
            }
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
            DestroyMaterial(_groundMaterial);
            DestroyMaterial(_obstacleMaterial);
            DestroyMaterial(_coinMaterial);
            DestroyMaterial(_pitMaterial);
            DestroyMaterial(_brakeMaterial);
        }

        private static void DestroyMaterial(Material material)
        {
            ObjectUtility.Destroy(material);
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

        private void BuildMaterials()
        {
            _groundMaterial = PrimitiveFactory.CreateSharedMaterial(_groundColor, "M_Ground");
            _obstacleMaterial = PrimitiveFactory.CreateSharedMaterial(_obstacleColor, "M_Obstacle");
            _coinMaterial = PrimitiveFactory.CreateSharedMaterial(_coinColor, "M_Coin");
            _pitMaterial = PrimitiveFactory.CreateSharedMaterial(_pitColor, "M_Pit");
            _brakeMaterial = PrimitiveFactory.CreateSharedMaterial(_brakeColor, "M_Brake");
        }

        private void BuildPools()
        {
            int obstacleCapacity = _obstaclePoolCapacityPerKind > 0
                ? _obstaclePoolCapacityPerKind
                : _activeSegmentCount * _rowsPerSegment;

            int coinCapacity = _coinPoolCapacity > 0
                ? _coinPoolCapacity
                : _activeSegmentCount * _rowsPerSegment * _coinsPerRow;

            _lowPool = new ObjectPool<Obstacle>(CreateLowObstacle, _poolRoot, obstacleCapacity, "Obstacle.Low");
            _highPool = new ObjectPool<Obstacle>(CreateHighObstacle, _poolRoot, obstacleCapacity, "Obstacle.High");
            _fullPool = new ObjectPool<Obstacle>(CreateFullObstacle, _poolRoot, obstacleCapacity, "Obstacle.Full");
            _rampPool = new ObjectPool<Obstacle>(CreateRamp, _poolRoot, obstacleCapacity, "Obstacle.Ramp");
            _truckPool = new ObjectPool<Obstacle>(CreateTruck, _poolRoot, obstacleCapacity, "Obstacle.Truck");
            _trainPool = new ObjectPool<Obstacle>(CreateTrain, _poolRoot, obstacleCapacity, "Obstacle.Train");
            _movingTruckPool = new ObjectPool<Obstacle>(CreateMovingTruck, _poolRoot, obstacleCapacity, "Obstacle.MovingTruck");
            _oncomingTruckPool = new ObjectPool<Obstacle>(CreateOncomingTruck, _poolRoot, obstacleCapacity, "Obstacle.Oncoming");
            _pitPool = new ObjectPool<Obstacle>(CreatePit, _poolRoot, obstacleCapacity, "Obstacle.Pit");
            _underpassPool = new ObjectPool<Obstacle>(CreateUnderpass, _poolRoot, obstacleCapacity, "Obstacle.Underpass");
            _busPool = new ObjectPool<Obstacle>(CreateStoppingBus, _poolRoot, obstacleCapacity, "Obstacle.Bus");
            _coinPool = new ObjectPool<Coin>(CreateCoin, _poolRoot, coinCapacity, "Coin");
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

            float floorWidth = _laneWidth * LaneCount + _trackSidePadding * 2f;
            GameObject floor = PrimitiveFactory.CreateCube(
                "Floor",
                new Vector3(floorWidth, _floorThickness, _segmentLength),
                _groundMaterial,
                root.layer,
                PrimitiveFactory.ColliderMode.None,
                castShadows: false);

            // No collider on the floor at all — the player's vertical motion is kinematic and
            // clamped at y = 0, so a ground collider would only cost PhysX work for nothing.
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -_floorThickness * 0.5f, _segmentLength * 0.5f);

            return root.AddComponent<Segment>();
        }

        private Obstacle CreateLowObstacle() => CreateObstacle(ObstacleKind.Low, _lowObstacleSize, _lowObstaclePrefab);
        private Obstacle CreateHighObstacle() => CreateObstacle(ObstacleKind.High, _highObstacleSize, _highObstaclePrefab);
        private Obstacle CreateFullObstacle() => CreateObstacle(ObstacleKind.Full, _fullObstacleSize, _fullObstaclePrefab);

        private Obstacle CreateObstacle(ObstacleKind kind, Vector3 size, Obstacle prefab)
        {
            Obstacle obstacle;

            if (prefab != null)
            {
                obstacle = Instantiate(prefab);
            }
            else
            {
                GameObject go = PrimitiveFactory.CreateCube(
                    $"Obstacle_{kind}",
                    size,
                    _obstacleMaterial,
                    _obstacleLayer,
                    PrimitiveFactory.ColliderMode.Trigger,
                    castShadows: true);

                obstacle = go.AddComponent<Obstacle>();
            }

            obstacle.Initialize(kind);
            return obstacle;
        }

        private Obstacle CreateTruck() => CreateLandable(ObstacleKind.Truck, _truckSize);

        /// <summary>
        /// Visual and kill volume are the same box, so what you see is exactly what hits you.
        /// It reuses the plain obstacle path — the only thing that makes it an underpass is
        /// that it starts above the ground instead of on it.
        /// </summary>
        private Obstacle CreateUnderpass() => CreateObstacle(
            ObstacleKind.Underpass,
            new Vector3(_underpassWidth, _underpassHeight, _underpassDepth),
            null);

        private Obstacle CreateStoppingBus()
        {
            Obstacle obstacle = CreateLandable(ObstacleKind.StoppingBus, _busSize);
            obstacle.gameObject.AddComponent<MovingObstacle>().Initialize();
            obstacle.Initialize(ObstacleKind.StoppingBus);
            return obstacle;
        }

        private Obstacle CreateOncomingTruck()
        {
            Obstacle obstacle = CreateLandable(ObstacleKind.OncomingTruck, _truckSize);
            obstacle.gameObject.AddComponent<MovingObstacle>().Initialize();
            obstacle.Initialize(ObstacleKind.OncomingTruck);
            return obstacle;
        }

        /// <summary>
        /// A hole. Rendered as a near-black slab just proud of the road rather than an actual
        /// gap — punching a real hole would mean tiling every segment floor per lane and per
        /// row, and this plays identically: jump it, or be somewhere else.
        /// </summary>
        private Obstacle CreatePit()
        {
            GameObject root = new GameObject("Obstacle_Pit");

            GameObject slab = PrimitiveFactory.CreateCube(
                "Pit_Slab",
                _pitSize,
                _pitMaterial,
                root.layer,
                PrimitiveFactory.ColliderMode.None,
                castShadows: false);

            slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = new Vector3(0f, _pitSize.y * 0.5f, 0f);

            GameObject lethal = new GameObject("Lethal") { layer = _obstacleLayer };
            lethal.transform.SetParent(root.transform, false);

            BoxCollider box = lethal.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, _pitTriggerHeight * 0.5f - 0.15f, 0f);
            box.size = new Vector3(_pitSize.x, _pitTriggerHeight, _pitSize.z);

            Obstacle obstacle = root.AddComponent<Obstacle>();
            obstacle.Initialize(ObstacleKind.Pit);
            return obstacle;
        }

        private Obstacle CreateMovingTruck()
        {
            Obstacle obstacle = CreateLandable(ObstacleKind.MovingTruck, _truckSize);
            obstacle.gameObject.AddComponent<MovingObstacle>().Initialize();

            // CreateLandable already ran Initialize, before the drift component existed.
            obstacle.Initialize(ObstacleKind.MovingTruck);
            return obstacle;
        }

        private Obstacle CreateTrain() => CreateLandable(ObstacleKind.Train, _trainSize);

        /// <summary>
        /// A piece of the stack: solid body on the Surface layer for the ground probe to find,
        /// plus a lethal trigger inset from the top. That inset is the entire mechanic — land
        /// on the roof and you are above the lethal volume; clip the side and you are inside it.
        /// </summary>
        private Obstacle CreateLandable(ObstacleKind kind, Vector3 size)
        {
            GameObject root = new GameObject($"Obstacle_{kind}");

            GameObject body = PrimitiveFactory.CreateCube(
                $"{kind}_Body",
                size,
                _obstacleMaterial,
                _surfaceLayer,
                PrimitiveFactory.ColliderMode.Solid,
                castShadows: true);

            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);

            GameObject lethal = new GameObject("Lethal") { layer = _obstacleLayer };
            lethal.transform.SetParent(root.transform, false);

            float lethalHeight = Mathf.Max(0.05f, size.y - _roofSafeInset);
            BoxCollider box = lethal.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, lethalHeight * 0.5f, 0f);
            box.size = new Vector3(size.x, lethalHeight, size.z);

            Obstacle obstacle = root.AddComponent<Obstacle>();
            obstacle.Initialize(kind);
            return obstacle;
        }

        /// <summary>
        /// Never lethal — it exists purely to raise the ground. The player walks up it because
        /// the per-frame rise stays under LaneController's step tolerance, and leaves the top
        /// carrying the climb rate as upward velocity.
        /// </summary>
        private Obstacle CreateRamp()
        {
            GameObject root = new GameObject("Obstacle_Ramp");

            float angle = Mathf.Atan2(_rampRise, _rampRun) * Mathf.Rad2Deg;
            float slopeLength = Mathf.Sqrt(_rampRise * _rampRise + _rampRun * _rampRun);

            GameObject slope = PrimitiveFactory.CreateCube(
                "Ramp_Slope",
                new Vector3(_rampWidth, _rampThickness, slopeLength),
                _obstacleMaterial,
                _surfaceLayer,
                PrimitiveFactory.ColliderMode.Solid,
                castShadows: true);

            slope.transform.SetParent(root.transform, false);

            // Negative pitch lifts the far (+Z) end, which is the end the player reaches last
            // as the world scrolls toward them.
            slope.transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);
            slope.transform.localPosition = new Vector3(0f, _rampRise * 0.5f, _rampRun * 0.5f);

            Obstacle obstacle = root.AddComponent<Obstacle>();
            obstacle.Initialize(ObstacleKind.Ramp);
            return obstacle;
        }

        private Coin CreateCoin()
        {
            Coin coin;

            if (_coinPrefab != null)
            {
                coin = Instantiate(_coinPrefab);
            }
            else
            {
                GameObject go = PrimitiveFactory.CreateCube(
                    "Coin",
                    _coinSize,
                    _coinMaterial,
                    _coinLayer,
                    PrimitiveFactory.ColliderMode.Trigger,
                    castShadows: false);

                coin = go.AddComponent<Coin>();
            }

            coin.Initialize(_coinValue);
            return coin;
        }
    }
}
