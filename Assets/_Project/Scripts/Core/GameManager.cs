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
        [Tooltip("Score comes only from coins. Distance is still tracked, but it drives " +
                 "difficulty rather than the scoreboard.")]
        [SerializeField] private int _pointsPerCoin = 5;

        [Tooltip("Extra multiplier per unit of height, applied when the coin is taken. Coins " +
                 "sit on the roofs, so this is what makes the climb worth the risk.")]
        [SerializeField] private float _multiplierPerHeightUnit = 0.6f;

        [Header("Streak")]
        [Tooltip("Coins needed to climb one multiplier tier.")]
        [SerializeField] private int _coinsPerStreakTier = 8;
        [SerializeField] private int _maxStreakMultiplier = 5;

        [Tooltip("Metres without a coin before the streak resets. Distance, not seconds — a " +
                 "timer would be far kinder early in a run than late.")]
        [SerializeField] private float _streakBreakDistance = 60f;

        [Header("Pursuit")]
        [SerializeField] private Pursuer _pursuer;
        [SerializeField] private CameraShake _cameraShake;

        [Tooltip("Speed immediately after a stumble, as a fraction of normal. The dip is what " +
                 "makes the pursuer's gain feel earned rather than arbitrary.")]
        [SerializeField, Range(0.2f, 1f)] private float _stumbleSpeedMultiplier = 0.55f;

        [Tooltip("How quickly that speed comes back, per second.")]
        [SerializeField] private float _stumbleSpeedRecoveryPerSecond = 0.9f;

        [Tooltip("Ground lost per stumble. At 0.34 it takes three to be caught.")]
        [SerializeField] private float _stumbleCost = 0.34f;

        [Tooltip("Ground regained per second while running on the street. Deliberately slow.")]
        [SerializeField] private float _streetRecoveryPerSecond = 0.12f;

        [Tooltip("Ground regained per second while up on the stack. He cannot follow you there, " +
                 "so being chased is a reason to climb.")]
        [SerializeField] private float _climbRecoveryPerSecond = 0.6f;

        [Tooltip("Seconds of immunity after a stumble, so one obstacle cannot hit you twice " +
                 "while it passes through you.")]
        [SerializeField] private float _recoveryWindow = 0.7f;

        [Header("Feel")]
        [Tooltip("Beat between the hit landing and the game-over panel appearing.")]
        [SerializeField] private float _deathFreezeDuration = 0.45f;

        [Header("Platform")]
        [SerializeField] private int _targetFrameRate = 60;

        private PlayerCollision _playerCollision;

        private GameState _gameState = GameState.Ready;
        private float _distance;
        private float _speed;
        private float _deathTimer;
        private ScoreKeeper _scoring;
        private PursuitState _pursuit;
        private float _rearmTimer;
        private float _speedPenalty;

        public GameState State => _gameState;
        public int Score => _scoring == null ? 0 : _scoring.Score;
        public int Multiplier => _scoring == null ? 1 : _scoring.Multiplier;
        public float PursuitDistance => _pursuit == null ? 1f : _pursuit.Distance;
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

            if (_pursuit == null)
            {
                _pursuit = new PursuitState(
                    _stumbleCost, _streetRecoveryPerSecond, _climbRecoveryPerSecond);
            }

            if (_scoring == null)
            {
                _scoring = new ScoreKeeper(
                    _pointsPerCoin,
                    _coinsPerStreakTier,
                    _maxStreakMultiplier,
                    _streakBreakDistance,
                    _multiplierPerHeightUnit);
            }

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
                _spawner.Initialize(_laneWidth, _difficulty, _obstacleTable, _state,
                    _player != null ? _player.Jump : default);
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
            _speed = _difficulty.StartSpeed;
            _scoring.Reset();
            _pursuit.Reset();
            _rearmTimer = 0f;
            _speedPenalty = 0f;

            if (_cameraShake != null)
            {
                _cameraShake.StopShake();
            }

            if (_pursuer != null)
            {
                _pursuer.Reset();
            }

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

            // The dip decays back to full speed rather than snapping, so recovering from a
            // stumble is something you feel happening rather than a state that flips.
            _speedPenalty = Mathf.Max(0f, _speedPenalty - _stumbleSpeedRecoveryPerSecond * dt);

            _speed = _difficulty.EvaluateSpeed(_distance) * (1f - _speedPenalty);
            _distance += _speed * dt;

            if (_spawner != null)
            {
                _spawner.Tick(dt, _speed, _distance);
            }

            if (_scoring.Tick(_distance) && _hud != null)
            {
                _hud.SetMultiplier(_scoring.Multiplier);
            }

            TickPursuit(dt);
        }

        /// <summary>
        /// Height decides everything here. On the street the gap crawls back; on a roof it snaps
        /// back, which is what makes climbing the answer to being chased rather than only the
        /// way to score.
        /// </summary>
        private void TickPursuit(float deltaTime)
        {
            float height = _player == null ? 0f : _player.SupportHeight;
            _pursuit.Tick(deltaTime, height);

            if (_rearmTimer > 0f)
            {
                _rearmTimer -= deltaTime;

                if (_rearmTimer <= 0f && _playerCollision != null)
                {
                    _playerCollision.Arm();
                }
            }

            if (_pursuer != null)
            {
                float playerX = _player == null ? 0f : _player.LaneX;
                _pursuer.UpdatePursuit(_pursuit.Distance, playerX, deltaTime, _pursuit.IsPursuing);
            }
        }

        private void TickDead()
        {
            if (_deathTimer > 0f)
            {
                _deathTimer -= Time.deltaTime;

                if (_deathTimer <= 0f && _hud != null)
                {
                    _hud.ShowGameOver(Score);
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
                _hud.SetScore(Score);
                _hud.SetMultiplier(Multiplier);
                _hud.ShowReady();
            }
        }

        private void OnPlayerHit(HitSeverity severity)
        {
            if (_gameState != GameState.Playing)
            {
                return;
            }

            // Clipping something while genuinely reacting to it costs ground, not the run. Only
            // walking into it flat-footed, or stumbling with him already on you, ends it.
            if (severity == HitSeverity.Glancing && !_pursuit.Stumble())
            {
                _scoring.BreakStreak();
                _rearmTimer = _recoveryWindow;
                _speedPenalty = 1f - _stumbleSpeedMultiplier;

                if (_cameraShake != null)
                {
                    _cameraShake.Shake();
                }

                if (_player != null)
                {
                    _player.PlayStumble();
                }

                if (_hud != null)
                {
                    _hud.SetMultiplier(_scoring.Multiplier);
                }

                return;
            }

            // A fatal hit gets a harder knock than a stumble, so the two never read alike.
            if (_cameraShake != null)
            {
                _cameraShake.Shake(0.5f, 0.5f);
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
            // Height is read at the moment of pickup, so a coin taken off a roof is worth more
            // than the same coin on the street.
            float height = _player == null ? 0f : _player.SupportHeight;
            _scoring.Collect(value, height, _distance);

            if (_hud != null)
            {
                _hud.SetScore(_scoring.Score);
                _hud.SetMultiplier(_scoring.Multiplier);
            }
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
