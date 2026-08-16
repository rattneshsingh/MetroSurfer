using Runner.Config;
using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>
    /// One recycled chunk of track. Owns the list of what is currently attached to it;
    /// <see cref="SegmentSpawner"/> owns the pools those things came from.
    /// </summary>
    /// <remarks>
    /// The root carries a kinematic Rigidbody. That is not optional: moving a collider with no
    /// Rigidbody makes PhysX re-bake it as static geometry every single frame, which is a real
    /// cost on a mid-tier Adreno. With the body present, the segment's child triggers are
    /// treated as one moving compound instead.
    /// </remarks>
    public sealed class Segment : MonoBehaviour
    {
        private Transform _transform;

        private Obstacle[] _obstacles;
        private int[] _obstacleHandles;
        private ObstacleKind[] _obstacleKinds;
        private float[] _obstacleLocalZ;
        private int _obstacleCount;

        private Coin[] _coins;
        private int[] _coinHandles;
        private int _coinCount;

        public Transform Root => _transform;

        public int ObstacleCount => _obstacleCount;
        public int CoinCount => _coinCount;

        /// <summary>Capacities are fixed at warm-up — these arrays never resize during a run.</summary>
        public void Initialize(int maxObstacles, int maxCoins)
        {
            _transform = transform;

            _obstacles = new Obstacle[maxObstacles];
            _obstacleHandles = new int[maxObstacles];
            _obstacleKinds = new ObstacleKind[maxObstacles];
            _obstacleLocalZ = new float[maxObstacles];

            _coins = new Coin[maxCoins];
            _coinHandles = new int[maxCoins];
        }

        public bool AddObstacle(Obstacle obstacle, int handle, ObstacleKind kind, float localZ)
        {
            if (_obstacleCount >= _obstacles.Length)
            {
                return false;
            }

            _obstacles[_obstacleCount] = obstacle;
            _obstacleHandles[_obstacleCount] = handle;
            _obstacleKinds[_obstacleCount] = kind;
            _obstacleLocalZ[_obstacleCount] = localZ;
            _obstacleCount++;
            return true;
        }

        /// <summary>
        /// Hides anything already past the cull line. Local Z is cached at spawn time so this
        /// never touches a Transform — it runs every frame on the segment straddling the line.
        /// </summary>
        public void HideObstaclesBehind(float localCullZ)
        {
            for (int i = 0; i < _obstacleCount; i++)
            {
                if (_obstacleLocalZ[i] < localCullZ)
                {
                    _obstacles[i].Hide();
                }
            }
        }

        public bool AddCoin(Coin coin, int handle)
        {
            if (_coinCount >= _coins.Length)
            {
                return false;
            }

            _coins[_coinCount] = coin;
            _coinHandles[_coinCount] = handle;
            _coinCount++;
            return true;
        }

        public bool HasObstacleRoom => _obstacleCount < _obstacles.Length;
        public bool HasCoinRoom => _coinCount < _coins.Length;

        public int GetObstacleHandle(int index) => _obstacleHandles[index];
        public ObstacleKind GetObstacleKind(int index) => _obstacleKinds[index];
        public int GetCoinHandle(int index) => _coinHandles[index];

        /// <summary>Drops the bookkeeping. The spawner must return the handles to their pools first.</summary>
        public void ClearContents()
        {
            for (int i = 0; i < _obstacleCount; i++)
            {
                _obstacles[i] = null;
            }

            for (int i = 0; i < _coinCount; i++)
            {
                _coins[i] = null;
            }

            _obstacleCount = 0;
            _coinCount = 0;
        }
    }
}
