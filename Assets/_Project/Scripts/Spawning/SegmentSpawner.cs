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

        /// <summary>Indexed by ObstacleKind, so a lookup is an array read.</summary>
        private ObjectPool<Obstacle>[] _poolsByKind;
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
        public bool IsBuilt => _built && _segments != null && _poolsByKind != null && _generator != null;

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

            _presentationRng = new System.Random(_seed);
            _generator = new TrackGenerator(_seed, _introSequence, obstacleTable, _catalog, _minRowGapAfterHardRow);

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

            for (int i = 0; i < _catalog.Count; i++)
            {
                ObstacleDefinition definition = _catalog.At(i);
                if (definition == null || definition.Drift == DriftMode.None)
                {
                    continue;
                }

                Material alert = definition.Drift == DriftMode.Decelerate ? _brakeMaterial : null;
                TickDriftPool(PoolFor(definition.Kind), deltaTime, alert);
            }
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
                EmitCoinArc(segment, plan.CoinLane, localZ, _catalog.RoofHeight(plan.Kind), speed);
                return;
            }

            float height = plan.CoinsOnRoof
                ? _catalog.RoofHeight(plan.Kind) + _coinHeight
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


        private ObjectPool<Obstacle> PoolFor(ObstacleKind kind)
        {
            int index = (int)kind;
            return (uint)index < (uint)_poolsByKind.Length ? _poolsByKind[index] : null;
        }

        private void BuildPools()
        {
            int autoCapacity = _activeSegmentCount * _rowsPerSegment;
            _poolsByKind = new ObjectPool<Obstacle>[KindCount];

            for (int i = 0; i < _catalog.Count; i++)
            {
                ObstacleDefinition definition = _catalog.At(i);
                if (definition == null)
                {
                    continue;
                }

                int index = (int)definition.Kind;
                if ((uint)index >= (uint)_poolsByKind.Length)
                {
                    continue;
                }

                int capacity = definition.PoolCapacity > 0 ? definition.PoolCapacity : autoCapacity;

                // Captured once at build time. The closure allocates here and never on a spawn,
                // which is the only place it would matter.
                ObstacleDefinition captured = definition;
                _poolsByKind[index] = new ObjectPool<Obstacle>(
                    () => CreateFor(captured), _poolRoot, capacity, "Obstacle." + definition.Kind);
            }

            int coinCapacity = _coinPoolCapacity > 0
                ? _coinPoolCapacity
                : _activeSegmentCount * _rowsPerSegment * _coinsPerRow;

            _coinPool = new ObjectPool<Coin>(CreateCoin, _poolRoot, coinCapacity, "Coin");
        }

        /// <summary>
        /// Builds one obstacle from its definition. Dispatching on form rather than on kind is
        /// the whole point of the catalogue: a new obstacle is a new entry, and only a genuinely
        /// new shape reaches this method at all.
        /// </summary>
        private Obstacle CreateFor(ObstacleDefinition definition)
        {
            if (definition.Prefab != null)
            {
                GameObject authored = Instantiate(definition.Prefab);
                Obstacle existing = authored.GetComponent<Obstacle>();
                Obstacle fromPrefab = existing != null ? existing : authored.AddComponent<Obstacle>();
                fromPrefab.Initialize(definition.Kind);
                return fromPrefab;
            }

            Obstacle obstacle;

            switch (definition.Form)
            {
                case ObstacleForm.Landable:
                    obstacle = BuildLandable(definition);
                    break;

                case ObstacleForm.Ramp:
                    obstacle = BuildRamp(definition);
                    break;

                case ObstacleForm.Pit:
                    obstacle = BuildPit(definition);
                    break;

                default:
                    obstacle = BuildBarrier(definition);
                    break;
            }

            if (definition.Drift != DriftMode.None)
            {
                obstacle.gameObject.AddComponent<MovingObstacle>().Initialize();
            }

            obstacle.Initialize(definition.Kind);
            return obstacle;
        }

        /// <summary>
        /// A solid, lethal box. Ground barriers and overhead slabs are the same thing at
        /// different base heights.
        /// </summary>
        private Obstacle BuildBarrier(ObstacleDefinition definition)
        {
            GameObject root = new GameObject("Obstacle_" + definition.Kind);

            GameObject body = PrimitiveFactory.CreateCube(
                "Body", definition.Size, MaterialFor(definition.Tint), _obstacleLayer,
                PrimitiveFactory.ColliderMode.Trigger, castShadows: true);

            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(
                0f, definition.BaseHeight + definition.Size.y * 0.5f, 0f);

            return root.AddComponent<Obstacle>();
        }

        /// <summary>
        /// Solid body on the Surface layer for the ground probe, plus a lethal trigger inset
        /// below the roof. That inset is the entire mechanic — land on top and you are above the
        /// lethal volume, clip the side and you are inside it.
        /// </summary>
        private Obstacle BuildLandable(ObstacleDefinition definition)
        {
            GameObject root = new GameObject("Obstacle_" + definition.Kind);

            GameObject body = PrimitiveFactory.CreateCube(
                "Body", definition.Size, MaterialFor(definition.Tint), _surfaceLayer,
                PrimitiveFactory.ColliderMode.Solid, castShadows: true);

            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(
                0f, definition.BaseHeight + definition.Size.y * 0.5f, 0f);

            AddLethalVolume(root, definition);
            return root.AddComponent<Obstacle>();
        }

        /// <summary>Never lethal. It exists purely to raise the ground.</summary>
        private Obstacle BuildRamp(ObstacleDefinition definition)
        {
            GameObject root = new GameObject("Obstacle_" + definition.Kind);

            float angle = Mathf.Atan2(definition.RampRise, definition.RampRun) * Mathf.Rad2Deg;
            float slopeLength = Mathf.Sqrt(
                definition.RampRise * definition.RampRise + definition.RampRun * definition.RampRun);

            GameObject slope = PrimitiveFactory.CreateCube(
                "Slope",
                new Vector3(definition.Size.x, definition.RampThickness, slopeLength),
                MaterialFor(definition.Tint), _surfaceLayer,
                PrimitiveFactory.ColliderMode.Solid, castShadows: true);

            slope.transform.SetParent(root.transform, false);

            // Negative pitch lifts the far end, which is the end the player reaches last as the
            // world scrolls toward them.
            slope.transform.localRotation = Quaternion.Euler(-angle, 0f, 0f);
            slope.transform.localPosition = new Vector3(
                0f, definition.RampRise * 0.5f, definition.RampRun * 0.5f);

            return root.AddComponent<Obstacle>();
        }

        /// <summary>
        /// A hole, rendered as a near-black slab just proud of the road. Punching a real gap
        /// would mean tiling every segment floor per lane and per row, and this plays the same:
        /// jump it, or be somewhere else.
        /// </summary>
        private Obstacle BuildPit(ObstacleDefinition definition)
        {
            GameObject root = new GameObject("Obstacle_" + definition.Kind);

            GameObject slab = PrimitiveFactory.CreateCube(
                "Slab", definition.Size, MaterialFor(definition.Tint), root.layer,
                PrimitiveFactory.ColliderMode.None, castShadows: false);

            slab.transform.SetParent(root.transform, false);
            slab.transform.localPosition = new Vector3(
                0f, definition.BaseHeight + definition.Size.y * 0.5f, 0f);

            AddLethalVolume(root, definition);
            return root.AddComponent<Obstacle>();
        }

        /// <summary>The kill volume, whose bounds the definition works out per form.</summary>
        private void AddLethalVolume(GameObject root, ObstacleDefinition definition)
        {
            GameObject lethal = new GameObject("Lethal");
            lethal.layer = _obstacleLayer;
            lethal.transform.SetParent(root.transform, false);

            float bottom = definition.LethalBottom;
            float height = Mathf.Max(0.05f, definition.LethalTop - bottom);

            BoxCollider box = lethal.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, bottom + height * 0.5f, 0f);
            box.size = new Vector3(definition.Size.x, height, definition.Size.z);
        }

        private Material MaterialFor(ObstacleTint tint) =>
            tint == ObstacleTint.Pit ? _pitMaterial : _obstacleMaterial;

        /// <summary>
        /// Drift is applied by mode, so the parameters stay shared across kinds rather than
        /// being duplicated into every definition that moves.
        /// </summary>
        private void ApplyDrift(Obstacle obstacle, ObstacleKind kind)
        {
            if (obstacle.Drift == null)
            {
                return;
            }

            ObstacleDefinition definition = _catalog.Get(kind);
            DriftMode mode = definition == null ? DriftMode.Oscillate : definition.Drift;

            switch (mode)
            {
                case DriftMode.Approach:
                    obstacle.Drift.BeginApproach(_oncomingSpeed, _oncomingMaxTravel);
                    break;

                case DriftMode.Decelerate:
                    obstacle.Drift.BeginBraking(_busCruiseSpeed, _busBrakeTriggerZ, _busBrakeDuration);
                    break;

                default:
                    // Phase varies per placement so a convoy ripples rather than moving as a block.
                    obstacle.Drift.BeginOscillation(_driftAmplitude, _driftPeriod, Roll() * Mathf.PI * 2f);
                    break;
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
            t.localPosition = new Vector3(LaneToX(lane), 0f, localZ);

            ApplyDrift(obstacle, kind);

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
