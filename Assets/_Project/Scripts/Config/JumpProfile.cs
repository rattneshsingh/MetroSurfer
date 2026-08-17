using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// The shape of the player's jump, as data. Lets the spawner draw a coin arc that matches
    /// the real trajectory instead of an authored guess at it — so retuning the jump re-shapes
    /// every arc in the game, and they can never drift out of sync with the physics.
    /// </summary>
    public readonly struct JumpProfile
    {
        public readonly float ApexHeight;
        public readonly float RiseDuration;
        public readonly float FallMultiplier;

        public JumpProfile(float apexHeight, float riseDuration, float fallMultiplier)
        {
            ApexHeight = Mathf.Max(0f, apexHeight);
            RiseDuration = Mathf.Max(0.01f, riseDuration);
            FallMultiplier = Mathf.Max(0.01f, fallMultiplier);
        }

        public bool IsValid => ApexHeight > 0f;

        /// <summary>Take-off to landing. The fall is quicker than the rise whenever gravity is boosted.</summary>
        public float AirTime => RiseDuration + RiseDuration / Mathf.Sqrt(FallMultiplier);

        /// <summary>Height above the take-off surface at a given time into the jump.</summary>
        public float HeightAt(float time)
        {
            if (time <= 0f)
            {
                return 0f;
            }

            float gravity = 2f * ApexHeight / (RiseDuration * RiseDuration);

            if (time <= RiseDuration)
            {
                float launchSpeed = 2f * ApexHeight / RiseDuration;
                return launchSpeed * time - 0.5f * gravity * time * time;
            }

            float falling = time - RiseDuration;
            return Mathf.Max(0f, ApexHeight - 0.5f * gravity * FallMultiplier * falling * falling);
        }
    }
}
