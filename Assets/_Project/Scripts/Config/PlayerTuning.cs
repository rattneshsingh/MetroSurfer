using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// Every number that decides how the player feels, in an asset rather than on a component.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists for one reason: Unity discards Play-mode edits to component fields, and keeps
    /// them on assets. Feel is found by dragging values while the game runs, and a good setting
    /// is a combination of five or six of them — losing that on every Stop makes tuning
    /// impractical rather than merely annoying.
    /// </para>
    /// <para>
    /// Public fields rather than properties: this is a pure data asset with no invariants to
    /// protect, and thirty properties of boilerplate would obscure what it actually is.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Runner/Player Tuning", fileName = "PlayerTuning")]
    public sealed class PlayerTuning : ScriptableObject
    {
        [Header("Lanes")]
        [Range(0, 2)] public int StartLane = 1;

        [Tooltip("Seconds to travel one lane. Lower is snappier. The single biggest feel value.")]
        public float LaneSnapDuration = 0.18f;

        [Tooltip("Eases the horizontal slide. Ends must sit at (0,0) and (1,1).")]
        public AnimationCurve LaneSnapEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Jump")]
        public float JumpHeight = 1.5f;

        [Tooltip("Seconds from take-off to apex. With height, this defines gravity.")]
        public float JumpRiseDuration = 0.45f;

        [Tooltip("Gravity multiplier while descending. Above 1 kills the floaty feel.")]
        public float FallGravityMultiplier = 1.5f;

        [Tooltip("Grace period after leaving the ground where a jump still counts.")]
        public float CoyoteTime = 0.12f;

        [Tooltip("A jump or roll pressed this long before it becomes legal still fires.")]
        public float InputBufferWindow = 0.16f;

        [Header("Roll")]
        public float SlideDuration = 0.6f;

        [Tooltip("Rolling in mid-air slams the player down instead of being ignored.")]
        public bool FastFallOnAirSlide = true;
        public float FastFallSpeed = 18f;

        [Tooltip("Jumping out of a roll cancels it.")]
        public bool JumpCancelsSlide = true;

        [Header("Hitbox — deliberately smaller than the visual mesh. Forgiving, never punishing.")]
        public Vector3 StandingHitboxCenter = new Vector3(0f, 0.5f, 0f);
        public Vector3 StandingHitboxSize = new Vector3(0.45f, 0.85f, 0.45f);
        public Vector3 SlidingHitboxCenter = new Vector3(0f, 0.22f, 0f);
        public Vector3 SlidingHitboxSize = new Vector3(0.45f, 0.35f, 0.45f);

        [Header("Visual")]
        public Vector3 StandingVisualScale = Vector3.one;
        public Vector3 SlidingVisualScale = new Vector3(0.9f, 0.9f, 0.9f);

        [Tooltip("Forward rotations the visual makes during a roll. 0 disables the spin.")]
        public float RollSpinTurns = 1f;
        public float VisualSquashSpeed = 18f;

        [Header("Stumble reaction")]
        public float FlinchDuration = 0.45f;
        public float FlinchTiltDegrees = 26f;
        [Range(0.5f, 1f)] public float FlinchSquash = 0.82f;

        [Header("Ground probe")]
        [Tooltip("Probe starts this far above the feet, so a rising ramp is still found.")]
        public float ProbeStartHeight = 0.6f;
        public float ProbeDistance = 8f;

        [Tooltip("Biggest step walked up rather than crashed into. Ramps must be gentler than " +
                 "this per frame; a truck side must be steeper.")]
        public float StepUpTolerance = 0.35f;

        [Header("Safety")]
        [Tooltip("Clamps a frame hitch so the player cannot tunnel through a collider.")]
        public float MaxDeltaTime = 0.05f;

        /// <summary>Used when no asset is assigned, so the player still works out of the box.</summary>
        public static PlayerTuning CreateDefault()
        {
            PlayerTuning tuning = CreateInstance<PlayerTuning>();
            tuning.name = "PlayerTuning (runtime default)";
            return tuning;
        }
    }
}
