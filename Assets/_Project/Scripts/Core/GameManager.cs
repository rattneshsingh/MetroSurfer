using Runner.Config;
using Runner.Player;
using Runner.Spawning;
using Runner.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Runner.Core
{
    /// <summary>
    /// Owns the run: state machine, distance, score, restart. The one place that decides
    /// whether the world is ticking.
    /// </summary>
    /// <remarks>
    /// Deliberately not a static singleton. Everything it needs is a serialized reference, and
    /// everything that needs it is either called by it or raises an event it subscribes to.
    /// </remarks>
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private LaneController _player;
        [SerializeField] private SegmentSpawner _spawner;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private HudController _hud;

        [Header("Track layout — the single source of truth for lane width")]
        [SerializeField] private float _laneWidth = 2.2f;

        [Header("Tuning data")]
        [SerializeField] private DifficultyCurve _difficulty;
        [SerializeField] private ObstacleTable _obstacleTable;

        [Tooltip("Phase 0 leaves this empty. When a state is assigned its palette and any " +
                 "tuning overrides are read here — nothing else in the codebase changes.")]
        [SerializeField] private StateConfig _state;

        [Header("Scoring")]
        [SerializeField] private float _pointsPerMetre = 1f;
        [SerializeField] private int _pointsPerCoin = 5;

        [Header("Feel")]
        [Tooltip("Beat between the hit landing and the game-over panel appearing.")]
        [SerializeField] private float _deathFreezeDuration = 0.45f;

        [Header("Platform")]
        [SerializeField] private int _targetFrameRate = 60;

        private PlayerCollision _playerCollision;

        private GameState _gameState = GameState.Ready;
        private float _distance;
        private float _speed;
        private int _coinScore;
        private int _score;
        private float _deathTimer;

        public GameState State => _gameState;
        public int Score => _score;
        public float Distance => _distance;
        public float Speed => _speed;

        private void Awake()
        {
            Application.targetFrameRate = _targetFrameRate;
            QualitySettings.vSyncCount = 0;

            EnsureInitialized();
        }

        /// <summary>
        /// Idempotent setup. Also reachable from Update, because a script recompile during Play
        /// mode resets private non-serialized fields without re-running Awake — which leaves the
        /// spawner ticking with null pools and an unbound restart button.
        /// </summary>
        private void EnsureInitialized()
        {
            ResolveTuningData();

            if (_playerCollision == null && _player != null)
            {
                _player.Configure(_laneWidth);
                _playerCollision = _player.GetComponent<PlayerCollision>();

                if (_playerCollision != null)
                {
                    _playerCollision.Hit += OnPlayerHit;
                    _playerCollision.CoinCollected += OnCoinCollected;
                }
            }

            if (_spawner != null && !_spawner.IsBuilt)
            {
                _spawner.Initialize(_laneWidth, _difficulty, _obstacleTable, _state);
            }

            if (_hud != null)
            {
                _hud.BindRestart(Restart);
            }
        }

        private void OnDestroy()
        {
            if (_playerCollision != null)
            {
                _playerCollision.Hit -= OnPlayerHit;
                _playerCollision.CoinCollected -= OnCoinCollected;
            }
        }

        private void Start()
        {
            EnterReady();
        }

        private void Update()
        {
            // Sentinel for a mid-play recompile having wiped runtime state. One property read
            // per frame in the normal case; no allocation either way.
            if (_spawner != null && !_spawner.IsBuilt)
            {
                EnsureInitialized();
            }

            switch (_gameState)
            {
                case GameState.Ready:
                    TickReady();
                    break;

                case GameState.Playing:
                    TickPlaying();
                    break;

                case GameState.Dead:
                    TickDead();
                    break;
            }
        }

        public void Restart()
        {
            _distance = 0f;
            _coinScore = 0;
            _score = 0;
            _speed = _difficulty.StartSpeed;

            // Pools reset in place. Reloading the scene would pay the whole warm-up cost again
            // and hitch on exactly the frame the player is most impatient.
            if (_spawner != null)
            {
                _spawner.ResetRun();
            }

            if (_player != null)
            {
                _player.ResetForRun();
            }

            if (_playerCollision != null)
            {
                _playerCollision.Arm();
            }

            EnterReady();
        }

        private void TickReady()
        {
            if (_input == null || _input.Poll() == PlayerCommand.None)
            {
                return;
            }

            _gameState = GameState.Playing;

            if (_player != null)
            {
                _player.SetRunning(true);
            }

            if (_hud != null)
            {
                _hud.ShowPlaying();
            }
        }

        private void TickPlaying()
        {
            float dt = Time.deltaTime;

            _speed = _difficulty.EvaluateSpeed(_distance);
            _distance += _speed * dt;

            if (_spawner != null)
            {
                _spawner.Tick(dt, _speed, _distance);
            }

            // Integer cast, no string work — the HUD decides whether anything actually changed.
            _score = _coinScore + (int)(_distance * _pointsPerMetre);

            if (_hud != null)
            {
                _hud.SetScore(_score);
            }
        }

        private void TickDead()
        {
            if (_deathTimer > 0f)
            {
                _deathTimer -= Time.deltaTime;

                if (_deathTimer <= 0f && _hud != null)
                {
                    _hud.ShowGameOver(_score);
                }

                // Input is ignored during the death beat on purpose — restarting mid-freeze
                // swallows the feedback the beat exists to deliver.
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                Restart();
            }
        }

        private void EnterReady()
        {
            _gameState = GameState.Ready;
            _deathTimer = 0f;

            if (_player != null)
            {
                _player.SetRunning(false);
            }

            if (_hud != null)
            {
                _hud.SetScore(_score);
                _hud.ShowReady();
            }
        }

        private void OnPlayerHit()
        {
            if (_gameState != GameState.Playing)
            {
                return;
            }

            _gameState = GameState.Dead;
            _deathTimer = Mathf.Max(0.0001f, _deathFreezeDuration);

            if (_player != null)
            {
                _player.SetRunning(false);
            }
        }

        private void OnCoinCollected(int value)
        {
            _coinScore += value * _pointsPerCoin;
        }

        /// <summary>
        /// A state's tuning wins over the shared defaults; the shared defaults win over nothing
        /// at all. The runtime fallbacks exist so the scene plays without any asset authored.
        /// </summary>
        private void ResolveTuningData()
        {
            if (_state != null && _state.Difficulty != null)
            {
                _difficulty = _state.Difficulty;
            }

            if (_state != null && _state.ObstacleTable != null)
            {
                _obstacleTable = _state.ObstacleTable;
            }

            if (_difficulty == null)
            {
                _difficulty = DifficultyCurve.CreateDefault();
            }

            if (_obstacleTable == null)
            {
                _obstacleTable = ObstacleTable.CreateDefault();
            }
        }
    }
}
