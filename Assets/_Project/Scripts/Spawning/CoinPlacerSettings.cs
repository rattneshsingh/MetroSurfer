using UnityEngine;

namespace Runner.Spawning
{
    /// <summary>Layout and appearance for coins. Built at runtime, never serialized directly.</summary>
    public readonly struct CoinPlacerSettings
    {
        public readonly int Layer;
        public readonly int Capacity;
        public readonly Color Color;
        public readonly Vector3 Size;
        public readonly float Spacing;
        public readonly float Height;
        public readonly int PerRow;
        public readonly int Value;
        public readonly int ArcCount;
        public readonly float LaneWidth;

        public CoinPlacerSettings(
            int layer,
            int capacity,
            Color color,
            Vector3 size,
            float spacing,
            float height,
            int perRow,
            int value,
            int arcCount,
            float laneWidth)
        {
            Layer = layer;
            Capacity = Mathf.Max(1, capacity);
            Color = color;
            Size = size;
            Spacing = spacing;
            Height = height;
            PerRow = Mathf.Max(1, perRow);
            Value = value;
            ArcCount = arcCount;
            LaneWidth = laneWidth;
        }
    }
}
