using UnityEngine;

namespace Runner.Player
{
    /// <summary>
    /// Lane position, jump and slide for the runner. Purely kinematic — no Rigidbody forces,
    /// no physics gravity. An endless runner wants exact, repeatable arcs that a designer can
    /// dial in; PhysX would only add variance and cost.
    /// </summary>
    /// <remarks>
    /// Every value under the tuning headers is a [SerializeField] on purpose: feel is Phase 0's
    /// only deliverable, and these want to be dragged while the game runs.
    /// </remarks>
    public sealed class LaneController : MonoBehaviour
    {
        public const int LaneCount = 3;

        [Header("Lanes")]
        [Tooltip("Overwritten by GameManager so the track and the player can never disagree.")]
        [SerializeField] private float _laneWidth = 2.2f;
        [SerializeField, Range(0, LaneCount - 1)] private int _startLane = 1;

        [Tooltip("Seconds to travel one lane. Lower is snappier.")]
        [SerializeField] private float _laneSnapDuration = 0.14f;

        [Tooltip("Eases the horizontal slide. Ends must sit at (0,0) and (1,1).")]
        [SerializeField] private AnimationCurve _laneSnapEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Jump")]
        [SerializeField] private float _jumpHeight = 1.9f;

        [Tooltip("Seconds from take-off to apex. Together with height this defines gravity.")]
        [SerializeField] private float _jumpRiseDuration = 0.32f;

        [Tooltip("Gravity multiplier while descending. >1 kills the floaty feel.")]
        [SerializeField] private float _fallGravityMultiplier = 1.7f;

        [Tooltip("Grace period after leaving the ground where a jump still counts.")]
        [SerializeField] private float _coyoteTime = 0.12f;

        [Tooltip("A jump or slide pressed this long before it becomes legal still fires.")]
        [SerializeField] private float _inputBufferWindow = 0.16f;

        [Header("Slide")]
        [SerializeField] private float _slideDuration = 0.55f;

        [Tooltip("Sliding in mid-air slams the player down instead of being ignored.")]
        [SerializeField] private bool _fastFallOnAirSlide = true;
        [SerializeField] private float _fastFallSpeed = 18f;

        [Tooltip("Jumping out of a slide cancels it.")]
        [SerializeField] private bool _jumpCancelsSlide = true;

        [Header("Hitbox — deliberately smaller than the visual mesh. Forgiving, never punishing.")]
        [SerializeField] private BoxCollider _hitbox;
        [SerializeField] private Vector3 _standingHitboxCenter = new Vector3(0f, 0.5f, 0f);
        [SerializeField] private Vector3 _standingHitboxSize = new Vector3(0.45f, 0.85f, 0.45f);
        [SerializeField] private Vector3 _slidingHitboxCenter = new Vector3(0f, 0.22f, 0f);
        [SerializeField] private Vector3 _slidingHitboxSize = new Vector3(0.45f, 0.35f, 0.45f);

        [Header("Visual")]
        [SerializeField] private Transform _visual;
        [SerializeField] private Vector3 _standingVisualScale = Vector3.one;
        [SerializeField] private Vector3 _slidingVisualScale = new Vector3(1f, 0.45f, 1f);
        [SerializeField] private float _visualSquashSpeed = 18f;

        [Header("Safety")]
        [Tooltip("Clamps a frame hitch so the player cannot tunnel through an obstacle.")]
        [SerializeField] private float _maxDeltaTime = 0.05f;

        [SerializeField] private PlayerInputReader _input;

        private Transform _transform;

        private int _targetLane;
        private float _laneFromX;
        private float _laneTimer;
        private float _x;

        private float _y;
        private float _verticalVelocity;
        private float _gravity;
        private float _jumpVelocity;
        private bool _isGrounded = true;
        private float _timeSinceGrounded;
        private bool _jumpConsumed;

        private bool _isSliding;
        private float _slideTimer;

        private float _jumpBufferedAt = float.NegativeInfinity;
        private float _slideBufferedAt = float.NegativeInfinity;

        private bool _running;

        public int CurrentLane => _targetLane;
        public bool IsSliding => _isSliding;
        public bool IsGrounded => _isGrounded;

        private void Awake()
        {
            _transform = transform;

            if (_hitbox == null)
            {
                _hitbox = GetComponent<BoxCollider>();
            }

            RecalculateJumpConstants();
            ResetForRun();
        }

        private void OnValidate()
        {
            RecalculateJumpConstants();
        }

        private void Update()
        {
            if (!_running)
            {
                return;
            }

            // Clamped so a GC spike or a stalled frame cannot teleport the player through
            // a collider. Perf/robustness beats exactness here.
            float dt = Mathf.Min(Time.deltaTime, _maxDeltaTime);

            PlayerCommand command = _input != null ? _input.Poll() : PlayerCommand.None;

            ApplyLaneInput(command);
            RecordBufferedInput(command);

            TickLane(dt);
            TickVertical(dt);
            TickSlide(dt);

            ApplyTransform();
            TickVisualSquash(dt);
        }

        /// <summary>Called by GameManager so lane width has exactly one source of truth.</summary>
        public void Configure(float laneWidth)
        {
            _laneWidth = laneWidth;
            _x = LaneToX(_targetLane);
            _laneFromX = _x;
            ApplyTransform();
        }

        public void SetRunning(bool running)
        {
            _running = running;

            if (!running && _input != null)
            {
                _input.ResetGesture();
            }
        }

        public void ResetForRun()
        {
            _targetLane = Mathf.Clamp(_startLane, 0, LaneCount - 1);
            _x = LaneToX(_targetLane);
            _laneFromX = _x;
            _laneTimer = _laneSnapDuration;

            _y = 0f;
            _verticalVelocity = 0f;
            _isGrounded = true;
            _timeSinceGrounded = 0f;
            _jumpConsumed = false;

            _jumpBufferedAt = float.NegativeInfinity;
            _slideBufferedAt = float.NegativeInfinity;

            EndSlide();

            if (_visual != null)
            {
                _visual.localScale = _standingVisualScale;
            }

            if (_input != null)
            {
                _input.ResetGesture();
            }

            ApplyTransform();
        }

        private void ApplyLaneInput(PlayerCommand command)
        {
            // Bitwise, not Enum.HasFlag — HasFlag boxes.
            if ((command & PlayerCommand.Left) != 0)
            {
                ChangeLane(-1);
            }

            if ((command & PlayerCommand.Right) != 0)
            {
                ChangeLane(1);
            }
        }

        private void RecordBufferedInput(PlayerCommand command)
        {
            if ((command & PlayerCommand.Jump) != 0)
            {
                _jumpBufferedAt = Time.time;
            }

            if ((command & PlayerCommand.Slide) != 0)
            {
                _slideBufferedAt = Time.time;
            }
        }

        private void ChangeLane(int delta)
        {
            int target = Mathf.Clamp(_targetLane + delta, 0, LaneCount - 1);
            if (target == _targetLane)
            {
                return;
            }

            _targetLane = target;

            // Re-target from wherever the player currently is rather than snapping back to
            // the previous lane centre — a second swipe mid-slide has to feel instant.
            _laneFromX = _x;
            _laneTimer = 0f;
        }

        private void TickLane(float dt)
        {
            float targetX = LaneToX(_targetLane);

            if (_laneSnapDuration <= 0f)
            {
                _x = targetX;
                return;
            }

            if (_laneTimer >= _laneSnapDuration)
            {
                _x = targetX;
                return;
            }

            _laneTimer += dt;
            float t = Mathf.Clamp01(_laneTimer / _laneSnapDuration);
            _x = Mathf.LerpUnclamped(_laneFromX, targetX, _laneSnapEase.Evaluate(t));
        }

        private void TickVertical(float dt)
        {
            bool jumpBuffered = Time.time - _jumpBufferedAt <= _inputBufferWindow;
            bool canJump = !_jumpConsumed && (_isGrounded || _timeSinceGrounded <= _coyoteTime);

            if (jumpBuffered && canJump)
            {
                if (_isSliding && _jumpCancelsSlide)
                {
                    EndSlide();
                }

                _verticalVelocity = _jumpVelocity;
                _isGrounded = false;
                _jumpConsumed = true;
                _jumpBufferedAt = float.NegativeInfinity;
            }

            float gravity = _verticalVelocity < 0f ? _gravity * _fallGravityMultiplier : _gravity;
            _verticalVelocity -= gravity * dt;
            _y += _verticalVelocity * dt;

            if (_y <= 0f)
            {
                _y = 0f;
                _verticalVelocity = 0f;

                if (!_isGrounded)
                {
                    _isGrounded = true;
                }

                _timeSinceGrounded = 0f;
                _jumpConsumed = false;
            }
            else
            {
                _isGrounded = false;
                _timeSinceGrounded += dt;
            }
        }

        private void TickSlide(float dt)
        {
            bool slideBuffered = Time.time - _slideBufferedAt <= _inputBufferWindow;

            if (slideBuffered && !_isSliding)
            {
                if (_isGrounded)
                {
                    StartSlide();
                    _slideBufferedAt = float.NegativeInfinity;
                }
                else if (_fastFallOnAirSlide && _verticalVelocity > -_fastFallSpeed)
                {
                    // Keep the buffer alive so the slide fires the instant we land.
                    _verticalVelocity = -_fastFallSpeed;
                }
            }

            if (!_isSliding)
            {
                return;
            }

            _slideTimer -= dt;
            if (_slideTimer <= 0f)
            {
                EndSlide();
            }
        }

        private void StartSlide()
        {
            _isSliding = true;
            _slideTimer = _slideDuration;
            ApplyHitbox(_slidingHitboxCenter, _slidingHitboxSize);
        }

        private void EndSlide()
        {
            _isSliding = false;
            _slideTimer = 0f;
            ApplyHitbox(_standingHitboxCenter, _standingHitboxSize);
        }

        private void ApplyHitbox(Vector3 center, Vector3 size)
        {
            if (_hitbox == null)
            {
                return;
            }

            _hitbox.center = center;
            _hitbox.size = size;
        }

        private void TickVisualSquash(float dt)
        {
            if (_visual == null)
            {
                return;
            }

            Vector3 target = _isSliding ? _slidingVisualScale : _standingVisualScale;
            _visual.localScale = Vector3.Lerp(_visual.localScale, target, 1f - Mathf.Exp(-_visualSquashSpeed * dt));
        }

        private void ApplyTransform()
        {
            if (_transform == null)
            {
                _transform = transform;
            }

            Vector3 position = _transform.localPosition;
            position.x = _x;
            position.y = _y;
            _transform.localPosition = position;
        }

        private float LaneToX(int lane) => (lane - (LaneCount - 1) * 0.5f) * _laneWidth;

        /// <summary>
        /// Derives gravity from the height/time the designer actually cares about, rather than
        /// making them solve for it. h = g*t^2/2 and v0 = g*t.
        /// </summary>
        private void RecalculateJumpConstants()
        {
            float rise = Mathf.Max(0.01f, _jumpRiseDuration);
            _gravity = 2f * _jumpHeight / (rise * rise);
            _jumpVelocity = 2f * _jumpHeight / rise;
        }
    }
}
