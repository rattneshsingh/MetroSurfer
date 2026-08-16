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
        private const int LaneCount = 3;

        /// <summary>Saturation point for the row-gap counter. Prevents int overflow on a long run.</summary>
        private const int RowGapCeiling = 1000;

        [Header("Track")]
        [Tooltip("Overwritten by GameManager so the track and the player can never disagree.")]
        [SerializeField] private float _laneWidth = 2.2f;
        [SerializeField] private int _activeSegmentCount = 6;
        [SerializeField] private float _segmentLength = 20f;
        [SerializeField] private float _rowSpacing = 5f;

        [Tooltip("Z behind the player at which a segment is recycled to the front.")]
        [SerializeField] private float _despawnZ = -18f;

        [SerializeField] private float _trackSidePadding = 1.2f;
        [SerializeField] private float _floorThickness = 0.5f;

        [Header("Generation")]
        [SerializeField] private int _seed = 20260817;

        [Tooltip("Rows at the very start of a run left completely clear.")]
        [SerializeField] private int _safeStartRows = 5;

        [Tooltip("Clear rows forced after a row that closes a lane off entirely.")]
        [SerializeField] private int _minRowGapAfterHardRow = 1;

        [SerializeField] private int _coinsPerRow = 3;
        [SerializeField] private float _coinSpacing = 1.4f;
        [SerializeField] private float _coinHeight = 0.9f;
        [SerializeField] private int _coinValue = 1;

        [Header("Grey-cube geometry (Phase 0 stand-ins)")]
        [SerializeField] private Vector3 _lowObstacleSize = new Vector3(1.6f, 0.7f, 0.7f);
        [SerializeField] private Vector3 _highObstacleSize = new Vector3(1.6f, 0.9f, 0.7f);

        [Tooltip("Centre height of a slide-under barrier. Must sit above the sliding hitbox and below the standing one.")]
        [SerializeField] private float _highObstacleCenterY = 1.15f;

        [SerializeField] private Vector3 _fullObstacleSize = new Vector3(1.6f, 2.4f, 0.7f);
        [SerializeField] private Vector3 _coinSize = new Vector3(0.4f, 0.4f, 0.4f);

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

        private DifficultyCurve _difficulty;
        private ObstacleTable _obstacleTable;

        private Transform _segmentRoot;
        private Transform _poolRoot;

        private Segment[] _segments;
        private Transform[] _segmentTransforms;
        private int _head;
        private float _frontZ;
        private int _rowsPerSegment;

        private ObjectPool<Obstacle> _lowPool;
        private ObjectPool<Obstacle> _highPool;
        private ObjectPool<Obstacle> _fullPool;
        private ObjectPool<Coin> _coinPool;

        private Material _groundMaterial;
        private Material _obstacleMaterial;
        private Material _coinMaterial;

        private int _obstacleLayer;
        private int _coinLayer;

        private System.Random _rng;
        private int _rowsGenerated;
        private int _rowsSinceHardRow;
        private int _coinLane = 1;

        private bool _built;

        /// <summary>Builds every pool and the segment ring. Call once, before the first run.</summary>
        public void Initialize(float laneWidth, DifficultyCurve difficulty, ObstacleTable obstacleTable, StateConfig state)
        {
            _laneWidth = laneWidth;
            _difficulty = difficulty;
            _obstacleTable = obstacleTable;

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
            if (!_built)
            {
                return;
            }

            _rng = new System.Random(_seed);
            _rowsGenerated = 0;
            _rowsSinceHardRow = RowGapCeiling;
            _coinLane = 1;
            _head = 0;

            // One segment starts behind the player so the track does not visibly begin at their feet.
            float startZ = -_segmentLength;
            _frontZ = startZ;

            for (int i = 0; i < _segments.Length; i++)
            {
                Segment segment = _segments[i];
                ReleaseContents(segment);

                _segmentTransforms[i].localPosition = new Vector3(0f, 0f, _frontZ);
                PopulateSegment(segment, Mathf.Max(0f, _frontZ));
                _frontZ += _segmentLength;
            }
        }

        /// <summary>
        /// Advances the world. Called from GameManager's Update so there is exactly one place
        /// that decides whether the run is ticking.
        /// </summary>
        public void Tick(float deltaTime, float speed, float distance)
        {
            if (!_built)
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

        private void RecycleHead(float distance)
        {
            Segment segment = _segments[_head];
            ReleaseContents(segment);

            _segmentTransforms[_head].localPosition = new Vector3(0f, 0f, _frontZ);

            // The run distance at which the player will actually reach this segment — that is
            // what difficulty should ramp against, not the distance already covered.
            PopulateSegment(segment, distance + _frontZ);

            _frontZ += _segmentLength;
            _head = (_head + 1) % _segments.Length;
        }

        private void PopulateSegment(Segment segment, float runDistanceAtSegmentStart)
        {
            float density = _difficulty.EvaluateObstacleDensity(runDistanceAtSegmentStart);
            float coinChance = _difficulty.EvaluateCoinChance(runDistanceAtSegmentStart);

            for (int row = 0; row < _rowsPerSegment; row++)
            {
                float localZ = row * _rowSpacing + _rowSpacing * 0.5f;
                float rowDistance = runDistanceAtSegmentStart + localZ;

                int occupiedMask = 0;

                bool inSafeStart = _rowsGenerated < _safeStartRows;
                bool gapSatisfied = _rowsSinceHardRow >= _minRowGapAfterHardRow;

                if (!inSafeStart && gapSatisfied && Roll() < density)
                {
                    occupiedMask = TryPlaceRow(segment, localZ, rowDistance);
                }
                else
                {
                    // Every row must advance the gap counter, including skipped ones —
                    // otherwise a forced breather row never expires and obstacles stop
                    // spawning for the rest of the run.
                    BumpRowGap();
                }

                _rowsGenerated++;

                if (Roll() < coinChance)
                {
                    PlaceCoins(segment, localZ, occupiedMask);
                }
            }
        }

        /// <summary>Returns the lane mask that ended up occupied, or 0 if nothing spawned.</summary>
        private int TryPlaceRow(Segment segment, float localZ, float rowDistance)
        {
            if (_obstacleTable == null || !_obstacleTable.TryPick(rowDistance, Roll(), out ObstaclePattern pattern))
            {
                BumpRowGap();
                return 0;
            }

            int mask = pattern.LaneMask & ObstaclePattern.AllLanesMask;

            // Hard floor on fairness: a Full row must never take every lane. The table filters
            // this already, but the rule is cheap enough to enforce twice rather than ship a
            // run that is impossible by construction.
            if (pattern.Kind == ObstacleKind.Full && mask == ObstaclePattern.AllLanesMask)
            {
                mask &= ~(1 << _rng.Next(0, LaneCount));
            }

            if (mask == 0)
            {
                BumpRowGap();
                return 0;
            }

            for (int lane = 0; lane < LaneCount; lane++)
            {
                if ((mask & (1 << lane)) == 0)
                {
                    continue;
                }

                PlaceObstacle(segment, lane, pattern.Kind, localZ);
            }

            // Only a Full row forces a lane change, so only a Full row earns a breather after it.
            if (pattern.Kind == ObstacleKind.Full)
            {
                _rowsSinceHardRow = 0;
            }
            else
            {
                BumpRowGap();
            }

            return mask;
        }

        private void BumpRowGap()
        {
            if (_rowsSinceHardRow < RowGapCeiling)
            {
                _rowsSinceHardRow++;
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

            segment.AddObstacle(obstacle, handle, kind);
        }

        private void PlaceCoins(Segment segment, float localZ, int occupiedMask)
        {
            // Keep the trail in one lane for as long as that lane stays clear — a coin line
            // that jitters between lanes reads as noise rather than as a path.
            if ((occupiedMask & (1 << _coinLane)) != 0)
            {
                int freeLane = PickFreeLane(occupiedMask);
                if (freeLane < 0)
                {
                    return;
                }

                _coinLane = freeLane;
            }

            float x = LaneToX(_coinLane);
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
                t.localPosition = new Vector3(x, _coinHeight, startZ + i * _coinSpacing);

                segment.AddCoin(coin, handle);
            }
        }

        private int PickFreeLane(int occupiedMask)
        {
            int offset = _rng.Next(0, LaneCount);

            for (int i = 0; i < LaneCount; i++)
            {
                int lane = (offset + i) % LaneCount;
                if ((occupiedMask & (1 << lane)) == 0)
                {
                    return lane;
                }
            }

            return -1;
        }

        private void ReleaseContents(Segment segment)
        {
            for (int i = 0; i < segment.ObstacleCount; i++)
            {
                PoolFor(segment.GetObstacleKind(i)).Release(segment.GetObstacleHandle(i));
            }

            for (int i = 0; i < segment.CoinCount; i++)
            {
                _coinPool.Release(segment.GetCoinHandle(i));
            }

            segment.ClearContents();
        }

        private ObjectPool<Obstacle> PoolFor(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.High: return _highPool;
                case ObstacleKind.Full: return _fullPool;
                default: return _lowPool;
            }
        }

        private float CenterHeightFor(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.High: return _highObstacleCenterY;
                case ObstacleKind.Full: return _fullObstacleSize.y * 0.5f;
                default: return _lowObstacleSize.y * 0.5f;
            }
        }

        private float LaneToX(int lane) => (lane - (LaneCount - 1) * 0.5f) * _laneWidth;

        private float Roll() => (float)_rng.NextDouble();

        private void OnDestroy()
        {
            // Materials are created with `new Material(...)` at warm-up, so nothing else owns
            // them. Without this Unity reports leaked material instances on scene unload.
            DestroyMaterial(_groundMaterial);
            DestroyMaterial(_obstacleMaterial);
            DestroyMaterial(_coinMaterial);
        }

        private static void DestroyMaterial(Material material)
        {
            if (material != null)
            {
                Destroy(material);
            }
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
        }

        private void BuildPools()
        {
            int obstacleCapacity = _obstaclePoolCapacityPerKind > 0
                ? _obstaclePoolCapacityPerKind
                : _activeSegmentCount * _rowsPerSegment * LaneCount;

            int coinCapacity = _coinPoolCapacity > 0
                ? _coinPoolCapacity
                : _activeSegmentCount * _rowsPerSegment * _coinsPerRow;

            _lowPool = new ObjectPool<Obstacle>(CreateLowObstacle, _poolRoot, obstacleCapacity, "Obstacle.Low");
            _highPool = new ObjectPool<Obstacle>(CreateHighObstacle, _poolRoot, obstacleCapacity, "Obstacle.High");
            _fullPool = new ObjectPool<Obstacle>(CreateFullObstacle, _poolRoot, obstacleCapacity, "Obstacle.Full");
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
                triggerCollider: false,
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
                    triggerCollider: true,
                    castShadows: true);

                obstacle = go.AddComponent<Obstacle>();
            }

            obstacle.Initialize(kind);
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
                    triggerCollider: true,
                    castShadows: false);

                coin = go.AddComponent<Coin>();
            }

            coin.Initialize(_coinValue);
            return coin;
        }
    }
}
