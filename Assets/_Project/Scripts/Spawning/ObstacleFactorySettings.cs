using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>
    /// Everything the factory needs that is not in the catalogue: layers, palette, pool sizing,
    /// and the drift parameters shared across kinds.
    /// </summary>
    /// <remarks>
    /// Built at runtime from the spawner's serialized fields rather than being serialized
    /// itself, so restructuring it never disturbs values already saved in a scene.
    /// </remarks>
    public readonly struct ObstacleFactorySettings
    {
        public readonly int ObstacleLayer;
        public readonly int SurfaceLayer;
        public readonly int AutoCapacity;

        public readonly Color ObstacleColor;
        public readonly Color PitColor;
        public readonly Color BrakeColor;

        public readonly float DriftAmplitude;
        public readonly float DriftPeriod;
        public readonly float OncomingSpeed;
        public readonly float OncomingMaxTravel;
        public readonly float BusCruiseSpeed;
        public readonly float BusBrakeTriggerZ;
        public readonly float BusBrakeDuration;

        public ObstacleFactorySettings(
            int obstacleLayer,
            int surfaceLayer,
            int autoCapacity,
            Color obstacleColor,
            Color pitColor,
            Color brakeColor,
            float driftAmplitude,
            float driftPeriod,
            float oncomingSpeed,
            float oncomingMaxTravel,
            float busCruiseSpeed,
            float busBrakeTriggerZ,
            float busBrakeDuration)
        {
            ObstacleLayer = obstacleLayer;
            SurfaceLayer = surfaceLayer;
            AutoCapacity = Mathf.Max(1, autoCapacity);
            ObstacleColor = obstacleColor;
            PitColor = pitColor;
            BrakeColor = brakeColor;
            DriftAmplitude = driftAmplitude;
            DriftPeriod = driftPeriod;
            OncomingSpeed = oncomingSpeed;
            OncomingMaxTravel = oncomingMaxTravel;
            BusCruiseSpeed = busCruiseSpeed;
            BusBrakeTriggerZ = busBrakeTriggerZ;
            BusBrakeDuration = busBrakeDuration;
        }
    }
}
