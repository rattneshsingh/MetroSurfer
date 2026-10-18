using UnityEngine;

namespace Runner.Config
{
    /// <summary>
    /// Everything the game needs to know about one kind of obstacle: how big it is, how it is
    /// built, whether it moves, and what it means for the player.
    /// </summary>
    /// <remarks>
    /// Previously all of this was spread across five switch statements in the spawner, so adding
    /// an obstacle meant editing the same file in five places. That is the same failure as
    /// hardcoding a state name, one level down — the cost of new content should be an asset,
    /// not a code change.
    /// </remarks>
    [System.Serializable]
    public class ObstacleDefinition
    {
        [Tooltip("Identity. Tables, formations and pools are all keyed on this.")]
        public ObstacleKind Kind;

        [Tooltip("How it is physically built. Only a genuinely new shape needs a new form.")]
        public ObstacleForm Form;

        public Vector3 Size = Vector3.one;

        [Tooltip("Height of the underside. Zero sits it on the road; raise it for an overhead " +
                 "barrier you roll under or an underpass you must be at street level for.")]
        public float BaseHeight;

        public ObstacleTint Tint = ObstacleTint.Obstacle;

        [Tooltip("How it fails to keep pace with the world, if at all.")]
        public DriftMode Drift = DriftMode.None;

        [Tooltip("True when no jump or roll gets you through, so the lane has to be abandoned. " +
                 "A row of these across every lane is unbeatable and is never generated.")]
        public bool ClosesLane;

        [Tooltip("Authored art. Leave empty and the shape is generated from Form and Size.")]
        public GameObject Prefab;

        [Tooltip("Pool size. Zero sizes it from the track layout.")]
        [Min(0)]
        public int PoolCapacity;

        [Header("Landable only")]
        [Tooltip("How far below the roof the lethal volume stops. This gap is what makes landing " +
                 "on top survivable while hitting the side is not.")]
        public float RoofSafeInset = 0.25f;

        [Header("Ramp only")]
        [Tooltip("Where the slope begins, relative to the obstacle's position. Negative starts it " +
                 "behind the row centre, so a ramp can fill its whole row and end at the next one.")]
        public float RampStart;

        public float RampRun = 3f;
        public float RampRise = 1.2f;
        public float RampThickness = 0.25f;

        [Header("Pit only")]
        [Tooltip("Height of the kill volume. Must catch a runner and a roll, and sit below the jump arc.")]
        public float PitTriggerHeight = 0.6f;

        /// <summary>Standable height, or zero if you cannot stand on it.</summary>
        public float RoofHeight => Form == ObstacleForm.Landable ? BaseHeight + Size.y : 0f;

        public bool IsLandable => Form == ObstacleForm.Landable;

        /// <summary>Bottom of the volume that kills you.</summary>
        public float LethalBottom
        {
            get
            {
                switch (Form)
                {
                    case ObstacleForm.Landable: return BaseHeight;
                    case ObstacleForm.Pit: return BaseHeight - 0.15f;
                    case ObstacleForm.Ramp: return float.MaxValue;
                    default: return BaseHeight;
                }
            }
        }

        /// <summary>Top of the volume that kills you.</summary>
        public float LethalTop
        {
            get
            {
                switch (Form)
                {
                    // Inset from the roof — the whole point of a landable piece.
                    case ObstacleForm.Landable: return BaseHeight + Mathf.Max(0.05f, Size.y - RoofSafeInset);
                    case ObstacleForm.Pit: return BaseHeight - 0.15f + PitTriggerHeight;
                    case ObstacleForm.Ramp: return float.MinValue;
                    default: return BaseHeight + Size.y;
                }
            }
        }
    }
}
