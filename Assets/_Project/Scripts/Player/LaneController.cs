using Runner.Config;
using UnityEngine;

namespace Runner.Player
{
    /// <summary>
    /// Lane position, jump and slide for the runner. Purely kinematic — no Rigidbody forces,
    /// no physics gravity. An endless runner wants exact, repeatable arcs that a designer can
    /// dial in; PhysX would only add variance and cost.
    /// </summary>
    /// <remarks>
    /// Every feel value lives on a PlayerTuning asset rather than on this component. Feel is
    /// found by dragging numbers while the game runs, and Unity throws away Play-mode edits to
    /// components while keeping them on assets — so on a component they would evaporate every
    /// time you pressed Stop.
    /// </remarks>
    public sealed class LaneController : MonoBehaviour
    {
        public const int LaneCount = 3;

        [Tooltip("Every feel value lives here so Play-mode edits survive pressing Stop. " +
                 "Leave empty to use the built-in defaults.")]
        [SerializeField] private PlayerTuning _tuning;

        [Header("Scene references")]
        [Tooltip("Overwritten by GameManager so the track and the player can never disagree.")]
        [SerializeField] private float _laneWidth = 2.2f;

        [SerializeField] private BoxCollider _hitbox;
        [SerializeField] private Transform _visual;
        [SerializeField] private PlayerInputReader _input;

        [Tooltip("Layers the downward probe treats as standable. Resolved from 'Surface' if left empty.")]
        [SerializeField] private LayerMask _surfaceLayers;

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
        private float _flinchTimer;
        private float _lastActionTime = float.NegativeInfinity;

        private float _jumpBufferedAt = float.NegativeInfinity;
        private float _slideBufferedAt = float.NegativeInfinity;

        private bool _running;
        private bool _isDead;
        private int _stumbleCount;
        private float _groundHeight;
        private float _supportY;

        /// <summary>Height of the surface currently underfoot. Drives the tier multiplier.</summary>
        public float SupportHeight => _supportY;

        /// <summary>The jump arc as data, so coin trajectories can be derived from it.</summary>
        public JumpProfile Jump =>
            new JumpProfile(Tuning.JumpHeight, Tuning.JumpRiseDuration, Tuning.FallGravityMultiplier);

        /// <summary>
        /// Falls back to defaults when nothing is assigned, so the player works in a bare scene
        /// and in tests without any asset wiring.
        /// </summary>
        private PlayerTuning Tuning
        {
            get
            {
                if (_tuning == null)
                {
                    _tuning = PlayerTuning.CreateDefault();
                }

                return _tuning;
            }
        }

        public int CurrentLane => _targetLane;

        /// <summary>
        /// Kicks off the visible reaction to a stumble. Purely cosmetic — control is never taken
        /// away, because losing input on a hit you are already recovering from is how a
        /// forgiving mechanic starts feeling punishing.
        /// </summary>
        public void PlayStumble()
        {
            _flinchTimer = Tuning.FlinchDuration;
            _stumbleCount++;
        }

        /// <summary>Marks the run as lost, for anything presenting the player. No gameplay effect.</summary>
        public void PlayDeath()
        {
            _isDead = true;
        }

        /// <summary>
        /// Rises by one per stumble. A counter rather than an event, so a presenter can poll it
        /// each frame without subscriptions or allocations.
        /// </summary>
        public int StumbleCount => _stumbleCount;
        public bool IsDead => _isDead;
        public bool IsRunning => _running;

        /// <summary>True while the horizontal snap is still running.</summary>
        public bool IsChangingLanes => _laneTimer < Tuning.LaneSnapDuration;

        /// <summary>
        /// Seconds since the player last committed to a jump or a roll. Used to judge whether a
        /// collision was a mistimed reaction or no reaction at all — an attempt that finished a
        /// fraction too early is still an attempt.
        /// </summary>
        public float TimeSinceAction => Time.time - _lastActionTime;

        /// <summary>Current horizontal position. The pursuer lines up with it.</summary>
        public float LaneX => _x;
        public bool IsSliding => _isSliding;
        public bool IsGrounded => _isGrounded;

        private void Awake()
        {
            _transform = transform;

            if (_hitbox == null)
            {
                _hitbox = GetComponent<BoxCollider>();
            }

            if (_surfaceLayers.value == 0)
            {
                int surface = LayerMask.NameToLayer("Surface");
                if (surface >= 0)
                {
                    _surfaceLayers = 1 << surface;
                }
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
            float dt = Mathf.Min(Time.deltaTime, Tuning.MaxDeltaTime);

            PlayerCommand command = _input != null ? _input.Poll() : PlayerCommand.None;

            ApplyLaneInput(command);
            RecordBufferedInput(command);

            TickLane(dt);
            TickVertical(dt);
            TickSlide(dt);

            ApplyTransform();
            TickVisual(dt);
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
            _targetLane = Mathf.Clamp(Tuning.StartLane, 0, LaneCount - 1);
            _x = LaneToX(_targetLane);
            _laneFromX = _x;
            _laneTimer = Tuning.LaneSnapDuration;

            _y = 0f;
            _supportY = 0f;
            _groundHeight = 0f;
            _verticalVelocity = 0f;
            _isGrounded = true;
            _timeSinceGrounded = 0f;
            _jumpConsumed = false;

            _jumpBufferedAt = float.NegativeInfinity;
            _slideBufferedAt = float.NegativeInfinity;
            _flinchTimer = 0f;
            _lastActionTime = float.NegativeInfinity;
            _isDead = false;

            EndSlide();

            if (_visual != null)
            {
                _visual.localScale = Tuning.StandingVisualScale;
                _visual.localRotation = Quaternion.identity;
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

            if (Tuning.LaneSnapDuration <= 0f)
            {
                _x = targetX;
                return;
            }

            if (_laneTimer >= Tuning.LaneSnapDuration)
            {
                _x = targetX;
                return;
            }

            _laneTimer += dt;
            float t = Mathf.Clamp01(_laneTimer / Tuning.LaneSnapDuration);
            _x = Mathf.LerpUnclamped(_laneFromX, targetX, Tuning.LaneSnapEase.Evaluate(t));
        }

        private void TickVertical(float dt)
        {
            bool jumpBuffered = Time.time - _jumpBufferedAt <= Tuning.InputBufferWindow;
            bool canJump = !_jumpConsumed && (_isGrounded || _timeSinceGrounded <= Tuning.CoyoteTime);

            if (jumpBuffered && canJump)
            {
                if (_isSliding && Tuning.JumpCancelsSlide)
                {
                    EndSlide();
                }

                _verticalVelocity = _jumpVelocity;
                _isGrounded = false;
                _jumpConsumed = true;
                _lastActionTime = Time.time;
                _jumpBufferedAt = float.NegativeInfinity;
            }

            float gravity = _verticalVelocity < 0f ? _gravity * Tuning.FallGravityMultiplier : _gravity;
            _verticalVelocity -= gravity * dt;
            _y += _verticalVelocity * dt;

            _groundHeight = ProbeGroundHeight();
            float rise = _groundHeight - _y;

            if (_verticalVelocity <= 0f && rise >= 0f)
            {
                // Landing out of the air is always allowed — that is how you get onto a roof.
                // Once grounded, only a gentle rise is walkable; anything steeper is a wall,
                // and running into a wall is the lethal collider's business, not ours.
                if (!_isGrounded || rise <= Tuning.StepUpTolerance)
                {
                    _y = _groundHeight;
                    _supportY = _groundHeight;
                }
                else
                {
                    _y = _supportY;
                }

                _verticalVelocity = 0f;
                _isGrounded = true;
                _timeSinceGrounded = 0f;
                _jumpConsumed = false;
                return;
            }

            // Ground fell away from under us — running off a roof, or off the end of a ramp.
            // No launch impulse: a ramp is a smooth ride up, and leaving the top is the player's
            // decision to make with the jump button. Coyote time covers the lip.
            _isGrounded = false;
            _timeSinceGrounded += dt;
        }

        /// <summary>
        /// One downward ray per frame. Physics.Raycast with an out RaycastHit does not
        /// allocate (RaycastAll does). Returns 0 when nothing is hit, so the street itself
        /// needs no collider at all.
        /// </summary>
        private float ProbeGroundHeight()
        {
            Vector3 origin = new Vector3(_x, _y + Tuning.ProbeStartHeight, _transform.position.z);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                Tuning.ProbeStartHeight + Tuning.ProbeDistance, _surfaceLayers,
                                QueryTriggerInteraction.Ignore))
            {
                return hit.point.y;
            }

            return 0f;
        }

        private void TickSlide(float dt)
        {
            bool slideBuffered = Time.time - _slideBufferedAt <= Tuning.InputBufferWindow;

            if (slideBuffered && !_isSliding)
            {
                if (_isGrounded)
                {
                    StartSlide();
                    _slideBufferedAt = float.NegativeInfinity;
                }
                else if (Tuning.FastFallOnAirSlide && _verticalVelocity > -Tuning.FastFallSpeed)
                {
                    // Keep the buffer alive so the slide fires the instant we land.
                    _verticalVelocity = -Tuning.FastFallSpeed;
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
            _lastActionTime = Time.time;
            _isSliding = true;
            _slideTimer = Tuning.SlideDuration;
            ApplyHitbox(Tuning.SlidingHitboxCenter, Tuning.SlidingHitboxSize);
        }

        private void EndSlide()
        {
            _isSliding = false;
            _slideTimer = 0f;
            ApplyHitbox(Tuning.StandingHitboxCenter, Tuning.StandingHitboxSize);
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

        private void TickVisual(float dt)
        {
            if (_visual == null)
            {
                return;
            }

            Vector3 target = _isSliding ? Tuning.SlidingVisualScale : Tuning.StandingVisualScale;

            // A squashed cube reads as ducking. Only actual rotation reads as a roll, and the
            // player needs to see which one they did to learn the timing.
            Quaternion rotation = Quaternion.identity;

            if (_isSliding && Tuning.SlideDuration > 0f && Tuning.RollSpinTurns != 0f)
            {
                float progress = 1f - Mathf.Clamp01(_slideTimer / Tuning.SlideDuration);
                rotation = Quaternion.Euler(progress * 360f * Tuning.RollSpinTurns, 0f, 0f);
            }

            if (_flinchTimer > 0f && Tuning.FlinchDuration > 0f)
            {
                _flinchTimer -= dt;

                // A decaying wobble rather than a single tilt — one lean reads as a pose, a
                // wobble reads as being knocked off balance.
                float life = Mathf.Clamp01(_flinchTimer / Tuning.FlinchDuration);
                float wobble = Mathf.Sin(life * Mathf.PI * 3f) * life;

                rotation *= Quaternion.Euler(0f, 0f, wobble * Tuning.FlinchTiltDegrees);
                target *= Mathf.Lerp(1f, Tuning.FlinchSquash, life);
            }

            _visual.localScale = Vector3.Lerp(_visual.localScale, target, 1f - Mathf.Exp(-Tuning.VisualSquashSpeed * dt));
            _visual.localRotation = rotation;
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
            float rise = Mathf.Max(0.01f, Tuning.JumpRiseDuration);
            _gravity = 2f * Tuning.JumpHeight / (rise * rise);
            _jumpVelocity = 2f * Tuning.JumpHeight / rise;
        }
    }
}
