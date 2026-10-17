using UnityEngine;

namespace Runner.Player
{
    /// <summary>
    /// Plays the runner's animations from the player's state. Presentation only: it reads
    /// <see cref="LaneController"/> and never writes to it, so movement and collision are
    /// exactly what they were with the grey cube.
    /// </summary>
    /// <remarks>
    /// Polls once per frame and cross-fades only when the chosen state changes. No animator
    /// parameters, no events, no allocations. The state names match the controller that
    /// Runner > Set Up Runner Character generates.
    /// </remarks>
    public sealed class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private LaneController _player;
        [SerializeField] private Animator _animator;

        [Tooltip("Blend time between animations, in seconds.")]
        [SerializeField] private float _crossFade = 0.12f;

        [Tooltip("How long the stumble reaction holds before running resumes.")]
        [SerializeField] private float _stumbleHold = 0.4f;

        public static readonly int IdleState = Animator.StringToHash("Idle");
        public static readonly int RunState = Animator.StringToHash("Run");
        public static readonly int JumpState = Animator.StringToHash("Jump");
        public static readonly int RollState = Animator.StringToHash("Roll");
        public static readonly int StumbleState = Animator.StringToHash("Stumble");
        public static readonly int DeathState = Animator.StringToHash("Death");

        private int _current;
        private int _seenStumbles;
        private float _stumbleTimer;

        private void Start()
        {
            if (_player != null)
            {
                _seenStumbles = _player.StumbleCount;
            }
        }

        private void LateUpdate()
        {
            if (_player == null || _animator == null)
            {
                return;
            }

            if (_player.StumbleCount != _seenStumbles)
            {
                _seenStumbles = _player.StumbleCount;
                _stumbleTimer = _stumbleHold;
            }

            if (_stumbleTimer > 0f)
            {
                _stumbleTimer -= Time.deltaTime;
            }

            int target = ChooseState();
            if (target != _current)
            {
                _current = target;
                _animator.CrossFadeInFixedTime(target, _crossFade);
            }
        }

        /// <summary>Most urgent first: a crash outranks everything, a roll outranks a jump.</summary>
        private int ChooseState()
        {
            if (_player.IsDead)
            {
                return DeathState;
            }

            if (!_player.IsRunning)
            {
                return IdleState;
            }

            if (_player.IsSliding)
            {
                return RollState;
            }

            // Jump starts, then hands itself to the airborne loop inside the controller.
            if (!_player.IsGrounded)
            {
                return JumpState;
            }

            return _stumbleTimer > 0f ? StumbleState : RunState;
        }
    }
}
